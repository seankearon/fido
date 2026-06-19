using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Fido.Models;
using Fido.Services;
using Fido.ViewModels;

namespace Fido.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm = new();
    private readonly ConfigService _configService;
    private readonly GitService _git;
    private readonly SolutionFinder _finder;
    private readonly WorkingTreeFinder _workingTreeFinder;
    private readonly IRiderLauncher _rider;
    private readonly IDialogService _dialogs;
    private readonly OpenerService _opener;
    private readonly AppConfig _config;

    public MainWindow() : this(FidoServices.CreateDefault())
    {
    }

    /// <summary>Test seam: build the window with injected collaborators (fakes for Rider/dialogs/config).</summary>
    internal MainWindow(FidoServices services)
    {
        _configService = services.ConfigService;
        _git = services.Git;
        _finder = services.Finder;
        _workingTreeFinder = services.WorkingTreeFinder;
        _rider = services.Rider;

        // Load config and apply the theme variant before the XAML resolves its DynamicResources.
        _config = _configService.Load();
        App.ApplyTheme(_config.Theme);

        InitializeComponent();
        DataContext = _vm;
        _vm.LoadMru(_config.RecentBranches, _config.RecentSolutions);

        _dialogs = services.Dialogs ?? new AvaloniaDialogService(this);
        _opener = new OpenerService(_git, _finder, _workingTreeFinder, _vm.AppendLog);
        _vm.Log.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(ScrollLogToEnd, DispatcherPriority.Background);

        ApplyStartupArgs();
        Opened += (_, _) => BranchBox.Focus();
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
        => await _dialogs.ShowSettingsAsync(_config, _configService);

    // Surface the MRU suggestions as soon as a field is focused — but only when there's history to show.
    private void OnMruGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (sender is AutoCompleteBox { ItemsSource: ICollection { Count: > 0 } } box)
            Dispatcher.UIThread.Post(() => box.IsDropDownOpen = true);
    }

    /// <summary>Promotes the entered branch/solution to the front of the MRU lists and persists them.</summary>
    private void RecordMru(string branch, string solution)
    {
        var changed = Mru.Add(_config.RecentBranches, branch);
        changed |= Mru.Add(_config.RecentSolutions, solution);
        if (!changed) return;

        _vm.LoadMru(_config.RecentBranches, _config.RecentSolutions);
        TrySaveConfig();
    }

    // The ✕ on a dropdown suggestion: drop that entry from its MRU list (Tag says which) and persist.
    private void OnRemoveMruItem(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string field, DataContext: string value }) return;
        e.Handled = true;   // a click on ✕ removes the entry — it must not also select / fill the box

        var (stored, shown, box) = field == "solution"
            ? (_config.RecentSolutions, _vm.RecentSolutions, SolutionBox)
            : (_config.RecentBranches, _vm.RecentBranches, BranchBox);

        var removed = stored.RemoveAll(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)) > 0;
        for (var i = shown.Count - 1; i >= 0; i--)
            if (string.Equals(shown[i], value, StringComparison.OrdinalIgnoreCase))
                shown.RemoveAt(i);

        if (!removed) return;
        TrySaveConfig();
        if (shown.Count == 0) box.IsDropDownOpen = false;   // nothing left to suggest
    }

    /// <summary>Best-effort persist of the config; the MRU is a convenience, not worth surfacing a save failure.</summary>
    private void TrySaveConfig()
    {
        try
        {
            _configService.Save(_config);
        }
        catch
        {
            // swallow: a failed MRU save shouldn't interrupt the user
        }
    }

    // --- Startup / log ------------------------------------------------------------------

    /// <summary>Pre-fills inputs from the CLI: <c>--branch/-b</c>, <c>--solution/-s</c>, <c>--folder</c>.</summary>
    private void ApplyStartupArgs()
    {
        var args = Program.StartupArgs;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--branch" or "-b" when i + 1 < args.Length:
                    _vm.BranchName = args[++i];
                    break;
                case "--solution" or "-s" when i + 1 < args.Length:
                    _vm.SolutionName = args[++i];
                    break;
                case "--folder":
                    _vm.IsFolderMode = true;
                    break;
            }
        }
    }

    private void ScrollLogToEnd() => LogScroller.Offset = new Vector(0, LogScroller.Extent.Height);

    private async void OnOpenClick(object? sender, RoutedEventArgs e) => await RunOpenAsync();

    // Mission control: poll each station, and only when every one is GO do we launch.
    // Internal so tests can await the full flow deterministically rather than racing the async-void click.
    internal async Task RunOpenAsync()
    {
        var branch = _vm.BranchName.Trim();
        var solution = _vm.SolutionName.Trim();
        if (string.IsNullOrEmpty(branch))
        {
            _vm.SetStatus("please enter a branch name", StatusKind.NoGo);
            return;
        }

        // Remember what was entered as soon as it's attempted — even if the launch later fails.
        RecordMru(branch, solution);

        _vm.IsBusy = true;
        _vm.ClearLog();
        _vm.SetStatus("", StatusKind.None);
        _vm.AppendLog("🚀 Going around the horn…");
        try
        {
            var plan = string.IsNullOrEmpty(solution)
                ? await ResolveByBranchOnlyAsync(branch)
                : await ResolveBySolutionAsync(branch, solution);

            if (plan is null) return;   // a station called no-go (or aborted); status already set

            _vm.AppendLog($"[✓] Branch resolved: {plan.Branch}");
            _vm.AppendLog($"[✓] {plan.LocatedAs}: {plan.WorkingDir}");
            _vm.AppendLog(plan.Target.IsSolution
                ? $"[✓] Solution found: {Path.GetFileName(plan.Target.Path)}"
                : $"[✓] Folder located: {plan.Target.Path}");

            var riderPath = _rider.Locate(_config);
            if (riderPath is null)
            {
                _vm.AppendLog("[✗] Rider not located.");
                _vm.SetStatus("Rider not found — set the Rider path in Settings", StatusKind.NoGo);
                return;
            }
            _vm.AppendLog($"[✓] Rider located: {riderPath}");

            _vm.AppendLog("");
            _vm.AppendLog("Fido? GO!");
            _vm.AppendLog("The Eagle has landed...");
            _rider.Launch(riderPath, plan.Target.Path);
            _vm.SetStatus($"Rider launched on {(plan.Target.IsSolution ? "solution" : "folder")}: {plan.Target.Path}", StatusKind.Go);
        }
        catch (Exception ex)
        {
            _vm.AppendLog($"[✗] {ex.Message}");
            _vm.SetStatus(ex.Message, StatusKind.NoGo);
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    /// <summary>Solution-centric flow: locate the clone(s), reuse/checkout/worktree the branch, resolve a target.</summary>
    private async Task<OpenPlan?> ResolveBySolutionAsync(string branch, string solution)
    {
        var repos = await _opener.FindRepositoriesAsync(solution, _config);
        if (repos.Count == 0)
        {
            _vm.AppendLog($"[✗] No repository with solution '{solution}' in range.");
            _vm.SetStatus($"no repository containing '{solution}' under the search roots", StatusKind.NoGo);
            return null;
        }

        RepositoryInfo repo;
        string workingDir;
        string locatedAs;

        var existing = await _opener.FindExistingCheckoutsAsync(repos, branch, _config.MainBranchNames);
        if (existing.Count > 0)
        {
            ExistingCheckout chosen;
            if (existing.Count == 1)
            {
                chosen = existing[0];
            }
            else
            {
                _vm.AppendLog($"Branch '{branch}' is checked out in {existing.Count} places.");
                var pickedExisting = await ChooseExistingAsync(existing, branch);
                if (pickedExisting is null) { _vm.SetStatus("", StatusKind.None); return null; }
                chosen = pickedExisting;
            }
            repo = chosen.Repo;
            workingDir = chosen.Path;
            locatedAs = "Worktree located";
        }
        else
        {
            var pickedRepo = repos.Count == 1 ? repos[0] : await ChooseRepoAsync(repos);
            if (pickedRepo is null) { _vm.SetStatus("", StatusKind.None); return null; }
            repo = pickedRepo;

            var ctx = await _opener.BuildMainContextAsync(repo, branch, _config);
            switch (await ShowDecisionDialogAsync(repo, branch, ctx))
            {
                case OpenDecision.Main:
                    workingDir = await _opener.CheckoutInMainAsync(repo, branch, ctx);
                    locatedAs = "Main tree switched";
                    break;
                case OpenDecision.Worktree:
                    workingDir = await _opener.CreateWorktreeAsync(repo, branch, ctx);
                    locatedAs = "Worktree created";
                    break;
                default:
                    _vm.SetStatus("", StatusKind.None);
                    return null;
            }
        }

        var target = _opener.ResolveTarget(workingDir, repo, _vm.CurrentMode);
        return new OpenPlan(branch, workingDir, locatedAs, target);
    }

    /// <summary>Branch-only flow: find a folder already on the branch, then pick a solution or the folder.</summary>
    private async Task<OpenPlan?> ResolveByBranchOnlyAsync(string branch)
    {
        var folders = await _opener.FindBranchFoldersAsync(_config, branch);
        if (folders.Count == 0)
        {
            _vm.AppendLog($"[✗] No working tree on '{branch}' under the search roots.");
            return await OfferConfiguredRepoAsync(branch);
        }

        var folder = folders.Count == 1 ? folders[0].Path : await ChooseFolderAsync(folders, branch);
        if (folder is null) { _vm.SetStatus("", StatusKind.None); return null; }

        var target = await ChooseOpenTargetAsync(folder);
        if (target is null) { _vm.SetStatus("", StatusKind.None); return null; }

        return new OpenPlan(branch, folder, "Worktree located", target);
    }

    /// <summary>
    /// The branch isn't checked out in any working tree under the search roots. Of the repos configured in
    /// Settings, keep only those whose refs actually contain the branch (local or origin) and offer to place
    /// it there — a main-tree checkout or a new worktree — reusing the solution flow's decision dialog and
    /// git steps. If the branch exists in none of them, abandon with a clear no-go.
    /// </summary>
    private async Task<OpenPlan?> OfferConfiguredRepoAsync(string branch)
    {
        var configured = _config.NewBranchRepos
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => new RepositoryInfo(p, ""))
            .ToList();

        if (configured.Count == 0)
        {
            _vm.SetStatus($"branch '{branch}' isn't checked out, and no repos are configured in Settings", StatusKind.NoGo);
            return null;
        }

        var repos = await _opener.FindReposWithBranchAsync(configured, branch);
        if (repos.Count == 0)
        {
            _vm.AppendLog($"[✗] '{branch}' exists in none of the {configured.Count} configured repo(s).");
            _vm.SetStatus($"branch '{branch}' not found in any configured repo", StatusKind.NoGo);
            return null;
        }

        var repo = repos.Count == 1
            ? repos[0]
            : await ChooseRepoAsync(repos, "Select repository",
                $"'{branch}' exists in {repos.Count} configured repos. Choose where to open it:");
        if (repo is null) { _vm.SetStatus("", StatusKind.None); return null; }

        var ctx = await _opener.BuildMainContextAsync(repo, branch, _config);

        string workingDir;
        string locatedAs;
        switch (await ShowDecisionDialogAsync(repo, branch, ctx))
        {
            case OpenDecision.Main:
                workingDir = await _opener.CheckoutInMainAsync(repo, branch, ctx);
                locatedAs = "Main tree switched";
                break;
            case OpenDecision.Worktree:
                workingDir = await _opener.CreateWorktreeAsync(repo, branch, ctx);
                locatedAs = "Worktree created";
                break;
            default:
                _vm.SetStatus("", StatusKind.None);
                return null;
        }

        var target = await ChooseOpenTargetAsync(workingDir);
        if (target is null) { _vm.SetStatus("", StatusKind.None); return null; }

        return new OpenPlan(branch, workingDir, locatedAs, target);
    }

    private async Task<RepositoryInfo?> ChooseRepoAsync(
        IReadOnlyList<RepositoryInfo> repos,
        string title = "Select repository",
        string prompt = "More than one clone contains this solution. Choose where to open it:")
    {
        var items = new List<ChooserItem>(repos.Count);
        foreach (var r in repos)
        {
            var (origin, current, worktrees) = await _opener.DescribeRepoAsync(r);
            var subtitle = $"on {current}   ·   origin {origin ?? "(none)"}   ·   {worktrees} worktree(s)";
            items.Add(new ChooserItem(r.MainWorktreePath, subtitle));
        }

        var index = await _dialogs.ShowChooserAsync(title, prompt, items);

        return index is { } i && i >= 0 && i < repos.Count ? repos[i] : null;
    }

    private async Task<ExistingCheckout?> ChooseExistingAsync(IReadOnlyList<ExistingCheckout> existing, string branch)
    {
        var items = new List<ChooserItem>(existing.Count);
        foreach (var e in existing)
        {
            var origin = await _git.GetRemoteUrlAsync(e.Repo.MainWorktreePath);
            var subtitle = (e.Worktree.IsMain ? "main working tree" : "linked worktree")
                           + $"   ·   clone {e.Repo.MainWorktreePath}";
            items.Add(new ChooserItem(
                e.Path,
                subtitle,
                GitHostLinks.ShortSha(e.Worktree.Head),
                GitHostLinks.GitHubCommitUrl(origin, e.Worktree.Head)));
        }

        var index = await _dialogs.ShowChooserAsync(
            "Branch checked out in multiple places",
            $"'{branch}' is checked out in more than one folder. Choose which to open:",
            items);

        return index is { } i && i >= 0 && i < existing.Count ? existing[i] : null;
    }

    private async Task<string?> ChooseFolderAsync(IReadOnlyList<BranchFolder> folders, string branch)
    {
        var items = folders
            .Select(f => new ChooserItem(f.Path, subtitle: null, GitHostLinks.ShortSha(f.Head), f.CommitUrl))
            .ToList();

        var index = await _dialogs.ShowChooserAsync(
            "Branch checked out in multiple places",
            $"'{branch}' is checked out in more than one folder. Choose which to open:",
            items);

        return index is { } i && i >= 0 && i < folders.Count ? folders[i].Path : null;
    }

    /// <summary>Presents the solutions in a folder (plus an "open the folder" option) and returns the choice.</summary>
    private async Task<RiderTarget?> ChooseOpenTargetAsync(string folder)
    {
        var solutions = _opener.FindSolutionsInFolder(folder, _config);
        if (solutions.Count == 0)
        {
            _vm.AppendLog($"No solution files found under {folder}; opening the folder.");
            return new RiderTarget(folder, IsSolution: false);
        }

        var items = solutions
            .Select(s =>
            {
                var dir = Path.GetDirectoryName(s) ?? folder;
                var rel = Path.GetRelativePath(folder, dir);
                return new ChooserItem(Path.GetFileName(s), rel is "." or "" ? "repo root" : rel);
            })
            .ToList();
        items.Add(new ChooserItem("Open this folder in Rider", folder));

        var index = await _dialogs.ShowChooserAsync(
            "Open from branch folder",
            $"Found {solutions.Count} solution(s). Choose what to open:",
            items);

        if (index is not { } i || i < 0) return null;
        return i < solutions.Count
            ? new RiderTarget(solutions[i], IsSolution: true)
            : new RiderTarget(folder, IsSolution: false);
    }

    private async Task<OpenDecision> ShowDecisionDialogAsync(RepositoryInfo repo, string branch, MainContext ctx)
        => await _dialogs.ShowDecisionAsync(repo, branch, ctx) ?? OpenDecision.Cancel;

    /// <summary>Resolved trajectory: where the branch lives and what to hand Rider.</summary>
    private sealed record OpenPlan(string Branch, string WorkingDir, string LocatedAs, RiderTarget Target);
}
