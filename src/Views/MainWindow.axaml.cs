using System;
using System.Collections;
using System.IO;
using System.Threading;
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
    private readonly IEditorLauncher _launcher;
    private readonly IDialogService _dialogs;
    private readonly OpenerService _opener;
    private readonly AppConfig _config;

    /// <summary>Live while a post-launch auto-close countdown is running; cancelling it aborts the close.</summary>
    private CancellationTokenSource? _closeCountdown;

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
        _launcher = services.Launcher;

        // Load config and apply the theme variant before the XAML resolves its DynamicResources.
        _config = _configService.Load();
        App.ApplyTheme(_config.Theme);

        InitializeComponent();
        DataContext = _vm;
        _vm.LoadMru(_config.RecentBranches, _config.RecentSolutions);
        _vm.SetEditors(_config.Editors, _config.DefaultEditorIndex);

        // A single Enter in either input should open. The AutoCompleteBox marks the first Enter
        // handled just to dismiss its MRU drop-down, so it never reaches the default button —
        // handledEventsToo lets us still act on it (see OnInputBoxKeyDown).
        BranchBox.AddHandler(InputElement.KeyDownEvent, OnInputBoxKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        SolutionBox.AddHandler(InputElement.KeyDownEvent, OnInputBoxKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);

        // Ctrl+1…Ctrl+9 launch with the Nth configured editor. Handled at the window so it fires even
        // while a text box has focus (Ctrl-modified digits aren't text input, so the boxes ignore them);
        // handledEventsToo in case a child marks the keystroke handled on the way up.
        AddHandler(InputElement.KeyDownEvent, OnEditorShortcutKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);

        _dialogs = services.Dialogs ?? new AvaloniaDialogService(this);
        _opener = new OpenerService(_git, _finder, _workingTreeFinder, _vm.AppendLog);
        _vm.Log.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(ScrollLogToEnd, DispatcherPriority.Background);

        var autoOpen = ApplyStartupArgs();
        Opened += async (_, _) =>
        {
            BranchBox.Focus();
            if (autoOpen)
            {
                autoOpen = false;   // a CLI-supplied branch runs the open flow exactly once
                await RunOpenAsync(fromCommandLine: true);
            }
        };
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        await _dialogs.ShowSettingsAsync(_config, _configService);
        _vm.SetEditors(_config.Editors, _config.DefaultEditorIndex);   // editors may have changed
    }

    // Launch with a non-default editor when its secondary button is clicked.
    private async void OnLaunchWithEditorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: EditorLaunchOption option }) return;
        if (option.Index < 0 || option.Index >= _config.Editors.Count) return;
        await RunOpenAsync(editor: _config.Editors[option.Index]);
    }

    // Ctrl+1…Ctrl+9 → launch with editor index 0…8 (matching the secondary buttons' gestures).
    private void OnEditorShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control) return;
        var index = DigitKeyToIndex(e.Key);
        if (index is not { } i || i < 0 || i >= _config.Editors.Count) return;

        e.Handled = true;
        var editor = _config.Editors[i];
        Dispatcher.UIThread.Post(() => _ = RunOpenAsync(editor: editor), DispatcherPriority.Input);
    }

    /// <summary>Maps a top-row or numpad digit key (1–9) to a zero-based editor index, else null.</summary>
    private static int? DigitKeyToIndex(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => key - Key.D1,
        >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1,
        _ => null,
    };

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

    /// <summary>
    /// Pre-fills inputs from the CLI: a bare first argument or <c>--branch/-b</c> sets the branch,
    /// <c>--solution/-s</c> the solution, <c>--folder</c> the open mode. Returns true when a branch was
    /// supplied — the signal that the open flow should run automatically on startup, as if the user had
    /// clicked Open in Rider (so any chooser/decision dialogs still appear).
    /// </summary>
    private bool ApplyStartupArgs()
    {
        var args = Program.StartupArgs;
        var branchProvided = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--branch" or "-b" when i + 1 < args.Length:
                    _vm.BranchName = args[++i];
                    branchProvided = true;
                    break;
                case "--solution" or "-s" when i + 1 < args.Length:
                    _vm.SolutionName = args[++i];
                    break;
                case "--folder":
                    _vm.IsFolderMode = true;
                    break;
                default:
                    // A bare positional argument (not an option, not consumed as a value) is the branch.
                    if (!branchProvided && !args[i].StartsWith('-'))
                    {
                        _vm.BranchName = args[i];
                        branchProvided = true;
                    }
                    break;
            }
        }
        return branchProvided;
    }

    private void ScrollLogToEnd() => LogScroller.Offset = new Vector(0, LogScroller.Extent.Height);

    private async void OnOpenClick(object? sender, RoutedEventArgs e) => await RunOpenAsync();

    // Enter inside the branch/solution boxes opens directly. The AutoCompleteBox eats the first
    // Enter just to close its MRU drop-down, so without this a pasted branch needs a second Enter
    // to act. Close the drop-down, swallow the key so the default button can't also fire, then run
    // the open flow — collapsing it back to a single press. Posted so any pending selection/binding
    // settles before RunOpenAsync reads the branch name.
    private void OnInputBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not AutoCompleteBox box) return;
        box.IsDropDownOpen = false;
        e.Handled = true;
        Dispatcher.UIThread.Post(() => _ = RunOpenAsync(), DispatcherPriority.Input);
    }

    // Mission control: poll each station, and only when every one is GO do we launch.
    // Internal so tests can await the full flow deterministically rather than racing the async-void click.
    // <paramref name="fromCommandLine"/> marks a startup run driven by a CLI branch — it governs whether
    // Fido closes itself afterwards (see <see cref="MaybeCloseAfterLaunch"/>).
    internal async Task RunOpenAsync(bool fromCommandLine = false, Editor? editor = null)
    {
        CancelPendingClose();   // a fresh open supersedes any countdown left running from the last one
        editor ??= _config.DefaultEditor;
        if (editor is null)
        {
            _vm.SetStatus("no editor configured — add one in Settings", StatusKind.NoGo);
            return;
        }

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
                ? await ResolveByBranchOnlyAsync(branch, editor)
                : await ResolveBySolutionAsync(branch, solution);

            if (plan is null) return;   // a station called no-go (or aborted); status already set

            _vm.AppendLog($"[✓] Branch resolved: {plan.Branch}");
            _vm.AppendLog($"[✓] {plan.LocatedAs}: {plan.WorkingDir}");
            _vm.AppendLog(plan.Target.IsSolution
                ? $"[✓] Solution found: {Path.GetFileName(plan.Target.Path)}"
                : $"[✓] Folder located: {plan.Target.Path}");

            var editorPath = _launcher.Locate(editor);
            if (editorPath is null)
            {
                _vm.AppendLog($"[✗] {editor.Name} not located.");
                _vm.SetStatus($"{editor.Name} not found — set its path in Settings", StatusKind.NoGo);
                return;
            }
            _vm.AppendLog($"[✓] {editor.Name} located: {editorPath}");

            _vm.AppendLog("");
            _vm.AppendLog("Fido? GO!");
            _vm.AppendLog("The Eagle has landed...");
            _launcher.Launch(editor, editorPath, plan.Target.Path);
            _vm.SetStatus($"{editor.Name} launched on {(plan.Target.IsSolution ? "solution" : "folder")}: {plan.Target.Path}", StatusKind.Go);
            MaybeCloseAfterLaunch(fromCommandLine);
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

    /// <summary>
    /// Closes Fido after a successful launch when the configured <see cref="CloseAfterOpen"/> policy says so:
    /// <see cref="CloseAfterOpen.Always"/> on any launch, <see cref="CloseAfterOpen.CommandLine"/> only for a
    /// CLI-driven run, <see cref="CloseAfterOpen.Never"/> not at all. The close is deferred by
    /// <see cref="AppConfig.CloseAfterOpenDelaySeconds"/> (0 = immediately). Rider is launched detached, so
    /// closing Fido leaves it running.
    /// </summary>
    private void MaybeCloseAfterLaunch(bool fromCommandLine)
    {
        var close = _config.CloseAfterOpen switch
        {
            CloseAfterOpen.Always => true,
            CloseAfterOpen.CommandLine => fromCommandLine,
            _ => false,
        };
        if (!close) return;

        var seconds = Math.Clamp(_config.CloseAfterOpenDelaySeconds, 0, AppConfig.MaxCloseAfterOpenDelaySeconds);
        if (seconds == 0)
            Close();
        else
            _ = CloseAfterCountdownAsync(seconds);
    }

    /// <summary>
    /// Counts down once per second — narrating "Closing in 10… 9… 8…" into the flight log and into the
    /// "Keep open" bar — then closes Fido. The countdown is cancellable: clicking "Keep open", starting
    /// another open (see <see cref="RunOpenAsync"/>), or closing the window aborts it and leaves the
    /// window as the user left it.
    /// </summary>
    private async Task CloseAfterCountdownAsync(int seconds)
    {
        CancelPendingClose();
        var cts = new CancellationTokenSource();
        _closeCountdown = cts;
        try
        {
            for (var remaining = seconds; remaining > 0; remaining--)
            {
                _vm.ShowCountdown(remaining);
                _vm.SetLiveLog($"Closing in {remaining}…", LogLevel.Accent);   // ticks in place: 10 → 9 → 8…
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
            }
            _vm.AppendLog("Fido out.", LogLevel.Accent);
            Close();
        }
        catch (OperationCanceledException)
        {
            // superseded by another open, "Keep open", or a manual close — leave the window be
        }
        finally
        {
            if (ReferenceEquals(_closeCountdown, cts))
            {
                _closeCountdown = null;
                cts.Dispose();
            }
        }
    }

    /// <summary>"Keep open": call off a running auto-close so Fido stays up.</summary>
    private void OnKeepOpenClick(object? sender, RoutedEventArgs e)
    {
        if (_closeCountdown is null) return;
        CancelPendingClose();
        _vm.AppendLog("Holding — Fido standing by.");
    }

    /// <summary>Aborts a running auto-close countdown, if any, hides its bar, and releases its token source.</summary>
    private void CancelPendingClose()
    {
        var cts = _closeCountdown;
        _closeCountdown = null;
        cts?.Cancel();
        cts?.Dispose();
        _vm.StopCountdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        CancelPendingClose();
        base.OnClosed(e);
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
    private async Task<OpenPlan?> ResolveByBranchOnlyAsync(string branch, Editor editor)
    {
        var folders = await _opener.FindBranchFoldersAsync(_config, branch);
        if (folders.Count == 0)
        {
            _vm.AppendLog($"[✗] No working tree on '{branch}' under the search roots.");
            return await OfferConfiguredRepoAsync(branch, editor);
        }

        var folder = folders.Count == 1 ? folders[0].Path : await ChooseFolderAsync(folders, branch);
        if (folder is null) { _vm.SetStatus("", StatusKind.None); return null; }

        var target = await ChooseOpenTargetAsync(folder, editor);
        if (target is null) { _vm.SetStatus("", StatusKind.None); return null; }

        return new OpenPlan(branch, folder, "Worktree located", target);
    }

    /// <summary>
    /// The branch isn't checked out in any working tree under the search roots. Of the repos configured in
    /// Settings, keep only those whose refs actually contain the branch (local or origin) and offer to place
    /// it there — a main-tree checkout or a new worktree — reusing the solution flow's decision dialog and
    /// git steps. If the branch exists in none of them, abandon with a clear no-go.
    /// </summary>
    private async Task<OpenPlan?> OfferConfiguredRepoAsync(string branch, Editor editor)
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

        var target = await ChooseOpenTargetAsync(workingDir, editor);
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
    private async Task<LaunchTarget?> ChooseOpenTargetAsync(string folder, Editor editor)
    {
        var solutions = _opener.FindSolutionsInFolder(folder, _config);
        if (solutions.Count == 0)
        {
            _vm.AppendLog($"No solution files found under {folder}; opening the folder.");
            return new LaunchTarget(folder, IsSolution: false);
        }

        var items = solutions
            .Select(s =>
            {
                var dir = Path.GetDirectoryName(s) ?? folder;
                var rel = Path.GetRelativePath(folder, dir);
                return new ChooserItem(Path.GetFileName(s), rel is "." or "" ? "repo root" : rel);
            })
            .ToList();
        items.Add(new ChooserItem($"Open this folder in {editor.Name}", folder));

        var index = await _dialogs.ShowChooserAsync(
            "Open from branch folder",
            $"Found {solutions.Count} solution(s). Choose what to open:",
            items);

        if (index is not { } i || i < 0) return null;
        return i < solutions.Count
            ? new LaunchTarget(solutions[i], IsSolution: true)
            : new LaunchTarget(folder, IsSolution: false);
    }

    private async Task<OpenDecision> ShowDecisionDialogAsync(RepositoryInfo repo, string branch, MainContext ctx)
        => await _dialogs.ShowDecisionAsync(repo, branch, ctx) ?? OpenDecision.Cancel;

    /// <summary>Resolved trajectory: where the branch lives and what to hand the editor.</summary>
    private sealed record OpenPlan(string Branch, string WorkingDir, string LocatedAs, LaunchTarget Target);
}
