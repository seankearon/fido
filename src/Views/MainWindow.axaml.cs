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

        // Alt+Space drops the native system menu (Avalonia otherwise swallows the gesture).
        SystemMenu.EnableAltSpace(this);

        _dialogs = services.Dialogs ?? new AvaloniaDialogService(this);
        _opener = new OpenerService(_git, _finder, _workingTreeFinder, _vm.AppendLog, _vm.AppendLiveLog);
        _vm.Log.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(ScrollLogToEnd, DispatcherPriority.Background);

        var startup = ApplyStartupArgs();
        var autoOpen = startup.AutoOpen;
        Opened += async (_, _) =>
        {
            BranchBox.Focus();
            if (startup.UnknownEditorSlug is { } slug)
            {
                ReportUnknownEditor(slug);   // an explicit editor that doesn't exist: say so, don't auto-launch
                return;
            }
            if (autoOpen)
            {
                autoOpen = false;   // a CLI-supplied branch runs the open flow exactly once
                await RunOpenAsync(fromCommandLine: true, editor: startup.Editor);
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
    /// Pre-fills inputs from the CLI and resolves which editor to open with. A bare first argument or
    /// <c>--branch/-b</c> sets the branch, <c>--solution/-s</c> the solution, <c>--folder</c> the open mode,
    /// and a bare second argument or <c>--editor/-e</c> picks the editor by its slug (e.g.
    /// <c>fido &lt;branch&gt; rider</c>). <see cref="StartupPlan.AutoOpen"/> is true when a branch was
    /// supplied — the signal that the open flow should run automatically on startup, as if the user had
    /// clicked Open (so any chooser/decision dialogs still appear). When an editor slug is given but matches
    /// no configured editor, <see cref="StartupPlan.UnknownEditorSlug"/> carries it so startup can report it.
    /// </summary>
    private StartupPlan ApplyStartupArgs()
    {
        var args = Program.StartupArgs;
        var branchProvided = false;
        string? editorSlug = null;
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
                case "--editor" or "-e" when i + 1 < args.Length:
                    editorSlug = args[++i];
                    break;
                case "--folder":
                    _vm.IsFolderMode = true;
                    break;
                default:
                    // Bare positional arguments: the first is the branch, the second the editor slug.
                    if (args[i].StartsWith('-')) break;
                    if (!branchProvided)
                    {
                        _vm.BranchName = args[i];
                        branchProvided = true;
                    }
                    else
                    {
                        editorSlug ??= args[i];   // an explicit --editor still wins over the positional
                    }
                    break;
            }
        }

        // Resolve the slug against the configured editors; an unrecognised one is reported at startup
        // rather than silently falling back to the default.
        Editor? editor = null;
        string? unknownSlug = null;
        if (!string.IsNullOrWhiteSpace(editorSlug))
        {
            editor = _config.FindEditorBySlug(editorSlug);
            if (editor is null) unknownSlug = editorSlug.Trim();
        }

        return new StartupPlan(branchProvided, editor, unknownSlug);
    }

    /// <summary>
    /// Surfaces a command-line editor slug that matched no configured editor: a no-go status plus a log line
    /// listing the slugs that <em>are</em> known, so the user can correct the typo.
    /// </summary>
    private void ReportUnknownEditor(string slug)
    {
        var known = string.Join(", ", _config.Editors
            .Select(e => e.Slug)
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        var hint = known.Length > 0 ? $" — known editors: {known}" : "";
        _vm.AppendLog($"[✗] Unknown editor '{slug}' on the command line{hint}.");
        _vm.SetStatus($"unknown editor '{slug}'{hint}", StatusKind.NoGo);
    }

    /// <summary>What <see cref="ApplyStartupArgs"/> resolved from the CLI: whether to auto-open, the chosen
    /// editor (null = use the default), and any editor slug that didn't match a configured editor.</summary>
    private sealed record StartupPlan(bool AutoOpen, Editor? Editor, string? UnknownEditorSlug);

    private void ScrollLogToEnd() => LogScroller.Offset = new Vector(0, LogScroller.Extent.Height);

    private async void OnOpenClick(object? sender, RoutedEventArgs e) => await RunOpenAsync();

    // Key handling for the branch/solution boxes.
    //
    // Ctrl+Space summons the MRU suggestions on demand. The boxes no longer drop the list down on
    // focus (it looked permanently stuck open); instead the list appears when the user starts typing
    // or asks for it with this gesture — but only when there's history to show.
    //
    // Enter opens directly. The AutoCompleteBox eats the first Enter just to close its MRU drop-down,
    // so without this a pasted branch needs a second Enter to act. Close the drop-down, swallow the
    // key so the default button can't also fire, then run the open flow — collapsing it back to a
    // single press. Posted so any pending selection/binding settles before RunOpenAsync reads the
    // branch name.
    private void OnInputBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not AutoCompleteBox box) return;

        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control)
        {
            if (box.ItemsSource is not ICollection { Count: > 0 }) return;
            e.Handled = true;
            box.IsDropDownOpen = true;
            return;
        }

        if (e.Key != Key.Enter) return;
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
                : await ResolveBySolutionAsync(branch, solution, editor);

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
    private async Task<OpenPlan?> ResolveBySolutionAsync(string branch, string solution, Editor editor)
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

        // WebStorm (and any folder-only editor) has no concept of a solution — always hand it the folder.
        var mode = editor.OpensFolderOnly ? OpenMode.Folder : _vm.CurrentMode;
        if (editor.OpensFolderOnly && _vm.CurrentMode == OpenMode.Solution)
            _vm.AppendLog($"{editor.Name} opens folders only — opening the folder instead of the solution.");

        var target = _opener.ResolveTarget(workingDir, repo, mode);
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

        // A located worktree on a real local branch: offer to delete it (worktree + branch + remote).
        var outcome = await ChooseOpenTargetAsync(folder, editor, branch);
        if (outcome.Target is null)
        {
            if (!outcome.Handled) _vm.SetStatus("", StatusKind.None);   // deletion sets its own status
            return null;
        }

        return new OpenPlan(branch, folder, "Worktree located", outcome.Target);
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
            _vm.AppendLog($"[✗] Branch '{branch}' could not be found. Check that your repositories are listed under Settings → New-branch repos.");
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

        // The branch was just placed here; deleting it again makes no sense, so no delete action.
        var outcome = await ChooseOpenTargetAsync(workingDir, editor, branch: null);
        if (outcome.Target is null) { _vm.SetStatus("", StatusKind.None); return null; }

        return new OpenPlan(branch, workingDir, locatedAs, outcome.Target);
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

    /// <summary>
    /// Presents the solutions in a folder (plus an "open the folder" option) and returns the choice. In
    /// branch-only mode (<paramref name="branch"/> non-null) a located <em>linked</em> worktree on a non-default
    /// branch also gets a delete action that removes the worktree, its local branch, and any branch on origin —
    /// handled here, so the flow stops without launching an editor (see <see cref="TargetOutcome"/>). The delete
    /// action stays reachable even when there's nothing to "open" (a folder-only editor, or no solution files):
    /// the chooser still shows, offering the folder plus the delete button.
    /// </summary>
    private async Task<TargetOutcome> ChooseOpenTargetAsync(string folder, Editor editor, string? branch)
    {
        // Offer delete for a located linked worktree on a real (non-default) branch. Resolved up front so it's
        // available even when the chooser would otherwise be skipped. Default branches (main/master) are never
        // offered — deleting them, locally or on origin, is not something this shortcut should make easy.
        var deleteLabel = branch is not null && !IsProtectedBranch(branch) && await _opener.IsLinkedWorktreeAsync(folder)
            ? "Delete worktree & branch"
            : null;

        // Folder-only editors (e.g. WebStorm) can't open a .sln; skip solution discovery for them.
        var solutions = editor.OpensFolderOnly
            ? (IReadOnlyList<string>)[]
            : _opener.FindSolutionsInFolder(folder, _config);

        // Nothing to choose between and nothing to delete → open the folder without a dialog, as before.
        if (solutions.Count == 0 && deleteLabel is null)
        {
            _vm.AppendLog(editor.OpensFolderOnly
                ? $"{editor.Name} opens folders only — opening the folder."
                : $"No solution files found under {folder}; opening the folder.");
            return TargetOutcome.Open(new LaunchTarget(folder, IsSolution: false));
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

        var prompt = solutions.Count > 0
            ? $"Found {solutions.Count} solution(s). Choose what to open:"
            : "Choose what to open:";

        var index = await _dialogs.ShowChooserAsync("Open from branch folder", prompt, items, deleteLabel);

        // The sentinel only ever comes back when the button was shown, which requires a branch — but guard
        // anyway so a stray sentinel without one degrades to a plain cancel rather than a null-deref.
        if (index == ChooserDialog.DeleteRequested && branch is not null)
            return await ConfirmAndDeleteWorktreeAsync(folder, branch);

        if (index is not { } i || i < 0) return TargetOutcome.Cancelled;
        var target = i < solutions.Count
            ? new LaunchTarget(solutions[i], IsSolution: true)
            : new LaunchTarget(folder, IsSolution: false);
        return TargetOutcome.Open(target);
    }

    /// <summary>True when <paramref name="branch"/> is one of the configured default-branch names (e.g. main/master).</summary>
    private bool IsProtectedBranch(string branch) =>
        _config.MainBranchNames.Any(n => string.Equals(n, branch, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Confirms and carries out deleting a located worktree: builds the plan, asks for confirmation, then
    /// removes the worktree, deletes its local branch, and — when present — the branch on origin. Sets the
    /// GO/NO-GO status and returns a <em>handled</em> outcome so the open flow ends without launching an
    /// editor. Backing out of the confirmation cancels quietly; a hard git failure propagates to
    /// <see cref="RunOpenAsync"/>'s handler as a NO-GO.
    /// </summary>
    private async Task<TargetOutcome> ConfirmAndDeleteWorktreeAsync(string folder, string branch)
    {
        var plan = await _opener.BuildWorktreeDeletionAsync(folder, branch);
        if (plan is null)
        {
            // Raced to the main tree somehow — nothing removable. Treat as a plain cancel.
            _vm.AppendLog($"[!] {folder} is a main working tree and can't be removed as a worktree.");
            return TargetOutcome.Cancelled;
        }

        if (!await _dialogs.ConfirmDeleteWorktreeAsync(plan))
            return TargetOutcome.Cancelled;   // user backed out — no-op, status cleared by the caller

        var fullyDeleted = await _opener.DeleteWorktreeAsync(plan);
        if (fullyDeleted)
        {
            var remotePart = plan.RemoteBranchExists ? " and remote branch" : "";
            _vm.SetStatus($"deleted worktree, local branch{remotePart} '{branch}'", StatusKind.Go);
        }
        else
        {
            _vm.SetStatus($"deleted worktree and local branch '{branch}'; remote delete failed — see log", StatusKind.NoGo);
        }
        return TargetOutcome.Deleted;
    }

    private async Task<OpenDecision> ShowDecisionDialogAsync(RepositoryInfo repo, string branch, MainContext ctx)
        => await _dialogs.ShowDecisionAsync(repo, branch, ctx) ?? OpenDecision.Cancel;

    /// <summary>Resolved trajectory: where the branch lives and what to hand the editor.</summary>
    private sealed record OpenPlan(string Branch, string WorkingDir, string LocatedAs, LaunchTarget Target);

    /// <summary>
    /// Result of the branch-folder "what to open" step. <see cref="Target"/> non-null → open it.
    /// <see cref="Target"/> null and <see cref="Handled"/> true → an action (a delete) already ran and set
    /// the status, so the flow stops silently. Null and not handled → the user cancelled; the caller clears
    /// the status.
    /// </summary>
    private sealed record TargetOutcome(LaunchTarget? Target, bool Handled)
    {
        public static TargetOutcome Open(LaunchTarget target) => new(target, Handled: false);
        public static TargetOutcome Deleted => new(null, Handled: true);
        public static TargetOutcome Cancelled => new(null, Handled: false);
    }
}
