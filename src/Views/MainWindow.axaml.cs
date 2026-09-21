using System;
using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Fido.Models;
using Fido.Services;
using Fido.ViewModels;

namespace Fido.Views;

public partial class MainWindow : Window
{
    /// <summary>How long the branch box stays quiet before a scan fires (Enter fires immediately).</summary>
    internal static readonly TimeSpan ScanDebounce = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// The flight log's content-sized ceiling from the redesign: with the window only as tall as its
    /// content the panel grows with the lines up to this, and no further. Past it the panel is sized by
    /// the window's slack instead — see <see cref="UpdateFlightLogHeight"/>.
    /// </summary>
    private const double LogContentMaxHeight = 150;

    private readonly MainWindowViewModel _vm = new();
    private readonly ConfigService _configService;
    private readonly GitService _git;
    private readonly IEditorLauncher _launcher;
    private readonly IDialogService _dialogs;
    private readonly OpenerService _opener;
    private readonly RepoConfigService _repoConfigs;
    private readonly AppConfig _config;

    /// <summary>Hands a URL to the OS default browser — injected, so a test never opens one.</summary>
    private readonly Func<string, bool> _openUrl;

    private readonly DispatcherTimer _scanDebounce;

    /// <summary>Cancels the in-flight discovery scan when a newer one supersedes it.</summary>
    private CancellationTokenSource? _scanCts;

    /// <summary>The per-run default tool: config's unless a CLI --tool overrode it for this run.</summary>
    private int _runDefaultToolIndex;

    /// <summary>The CLI tool for the one-shot auto-open, armed until the first scan completes.</summary>
    private Editor? _autoOpenTool;

    /// <summary>An unknown CLI tool id, reported once the startup scan lands (the scan clears the log).</summary>
    private string? _startupUnknownToolSlug;

    /// <summary>One-shot CLI <c>--folder</c>: pre-select the Folder chip when the startup scan lands.</summary>
    private bool _startupPreferFolder;

    /// <summary>Whether this scan found the branch an in-repo config. Placing the branch can turn up one the
    /// scan couldn't see (see <see cref="RefreshRepoConfigAsync"/>); this keeps that from re-narrating a
    /// config the scan already applied.</summary>
    private bool _scanFoundRepoConfig;

    /// <summary>The deletion plan built when the confirm strip was armed; consumed on Delete.</summary>
    private WorktreeDeletion? _pendingDeletePlan;
    private TargetCard? _pendingDeleteCard;

    /// <summary>What a part-way delete left behind, held while the retry strip offers another go: the same
    /// plan, the steps still outstanding, and everything earlier passes already removed (so the report after
    /// a successful retry describes the whole attempt).</summary>
    private WorktreeDeletion? _retryPlan;
    private TargetCard? _retryCard;
    private WorktreeDeletionChoice _retryChoice = new(false, false, false);
    private WorktreeDeletionOutcome _retryOutcome = WorktreeDeletionOutcome.Nothing;

    /// <summary>Guards the popover radio handlers while the list itself is being rebuilt.</summary>
    private bool _rebuildingToolChoices;

    /// <summary>Live while a post-launch auto-close countdown is running; cancelling it aborts the close.</summary>
    private CancellationTokenSource? _closeCountdown;

    /// <summary>
    /// The discovery scan a CLI-supplied branch kicks off on open. Tests await this instead of starting a
    /// scan of their own: whichever scan lands first consumes the run's one-shots (the auto-open, the
    /// unknown-tool report) and a superseding scan clears the log, so racing it is a coin toss.
    /// </summary>
    internal Task StartupScan { get; private set; } = Task.CompletedTask;

    public MainWindow() : this(FidoServices.CreateDefault())
    {
    }

    /// <summary>Test seam: build the window with injected collaborators (fakes for Rider/dialogs/config).</summary>
    internal MainWindow(FidoServices services)
    {
        _configService = services.ConfigService;
        _git = services.Git;
        _launcher = services.Launcher;
        _openUrl = services.OpenUrl;

        // Load config and apply the theme variant before the XAML resolves its DynamicResources.
        _config = _configService.Load();
        App.ApplyTheme(_config.Theme);

        InitializeComponent();
        DataContext = _vm;
        _vm.LoadMru(_config.RecentBranches, _config.RecentSolutions);
        _vm.ShowTargetInTitle = _config.ShowTargetInWindowTitle;
        _vm.SetConfig(_config);

        _runDefaultToolIndex = _config.DefaultEditorIndex;
        _vm.SetEditors(_config.Editors, _runDefaultToolIndex);
        RebuildDefaultToolChoices();
        ConsoleView.UseFidoPalette = _config.ConsoleUsesFidoPalette;

        // Typing in the branch box debounces into a scan; Enter (below) fires one immediately.
        _scanDebounce = new DispatcherTimer { Interval = ScanDebounce };
        _scanDebounce.Tick += (_, _) =>
        {
            _scanDebounce.Stop();
            _ = RunDiscoveryAsync();
        };
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        // Enter/Ctrl+Space handling on the inputs. The AutoCompleteBox marks the first Enter handled
        // just to dismiss its MRU drop-down — handledEventsToo lets us still act on it.
        BranchBox.AddHandler(InputElement.KeyDownEvent, OnInputBoxKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        SolutionBox.AddHandler(InputElement.KeyDownEvent, OnInputBoxKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);

        // Window-level keys: Ctrl+1…9 open with the Nth tool (respecting the found-gate), Esc backs
        // out of a pending delete confirm. handledEventsToo so a focused child can't swallow them.
        AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);

        // Alt+Space drops the native system menu (Avalonia otherwise swallows the gesture).
        SystemMenu.EnableAltSpace(this);

        _dialogs = services.Dialogs ?? new AvaloniaDialogService(this);
        _opener = new OpenerService(_git, services.Finder, services.WorkingTreeFinder, _vm.AppendLog, _vm.AppendLiveLog, gitHub: services.GitHub);
        _repoConfigs = new RepoConfigService(_git);
        _vm.Log.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(ScrollLogToEnd, DispatcherPriority.Background);

        // The flight log absorbs whatever vertical room the rest of the screen doesn't need, so a taller
        // window means a taller log rather than a gap above it. Re-run after every layout pass: the
        // window can be resized, and the upper stack's own height moves with the discovery results.
        LayoutUpdated += (_, _) => UpdateFlightLogHeight();

        var startup = ApplyStartupArgs();
        Opened += (_, _) =>
        {
            BranchBox.Focus();
            if (startup.UnknownToolSlug is { } slug && !startup.BranchProvided)
            {
                ReportUnknownTool(slug);   // an explicit tool that doesn't exist: say so, don't auto-open
                return;
            }
            // A CLI-supplied branch starts discovery straight away; if a tool was named too, the first
            // scan's completion may auto-open (single location only — see RunDiscoveryAsync). An unknown
            // tool id suppresses only the auto-open, never the scan itself — it's reported after the
            // scan lands, because starting a scan clears the log.
            if (startup.BranchProvided)
            {
                _autoOpenTool = startup.Tool;
                _startupUnknownToolSlug = startup.UnknownToolSlug;
                _startupPreferFolder = startup.PreferFolder;
                StartupScan = RunDiscoveryAsync();
            }
        };
    }

    // --- Discovery ----------------------------------------------------------------------

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Opening the Console tab is what asks for a shell — Fido doesn't spawn one on every launch just
        // in case. Only for a checkout that exists: a placement card's folder hasn't been created yet, and
        // the pane keeps its "pick a location" placeholder until one has.
        if (e.PropertyName == nameof(MainWindowViewModel.IsConsoleTab) && _vm.IsConsoleTab)
        {
            if (_vm.SelectedTarget is { Target: { Kind: TargetKind.Worktree or TargetKind.MainClone } target })
                ConsoleView.EnsureStarted(target.Path);
            return;
        }

        if (e.PropertyName != nameof(MainWindowViewModel.BranchName)) return;

        _scanDebounce.Stop();
        if (string.IsNullOrWhiteSpace(_vm.BranchName))
        {
            _scanCts?.Cancel();
            _vm.ResetToIdle();
            return;
        }
        _scanDebounce.Start();
    }

    /// <summary>
    /// The core loop: scans every configured working tree for the branch and lands the phase machine
    /// on Found/NotFound. A newer scan cancels the one in flight. Internal so tests can await the flow
    /// deterministically instead of racing the debounce timer.
    /// </summary>
    internal async Task RunDiscoveryAsync()
    {
        _scanDebounce.Stop();
        var branch = _vm.BranchName.Trim();
        if (branch.Length == 0)
        {
            _vm.ResetToIdle();
            return;
        }

        _scanCts?.Cancel();
        CancelPendingClose();   // a fresh scan supersedes any auto-close countdown still running
        var cts = new CancellationTokenSource();
        _scanCts = cts;

        // The CLI auto-open belongs to this scan alone: disarm it up front so a superseding scan
        // (the user typing a different branch) can never inherit the one-shot launch. The benign
        // one-shots (unknown-tool report, --folder chip preference) instead ride until the first
        // scan that actually completes — see below — so a superseded startup scan doesn't eat them.
        var autoTool = _autoOpenTool;
        _autoOpenTool = null;

        _vm.BeginScan(branch);
        _vm.ClearLog();
        _vm.AppendLog("🚀 Going around the horn…");
        _vm.AppendLiveLog($"Scanning working trees for '{branch}'…");

        try
        {
            var targets = await _opener.DiscoverTargetsAsync(_config, branch,
                onTreeCount: count =>
                {
                    if (cts.IsCancellationRequested) return;   // a superseded scan mustn't narrate over the new one
                    _vm.SetScanTreeCount(count);
                    _vm.AppendLiveLog($"Scanning {count} working tree(s) for '{branch}'…");
                },
                onClones: clones =>
                {
                    // Branch-independent, so they outlive this scan and answer the worktree-path line for
                    // every branch typed afterwards without another walk of the search roots.
                    if (!cts.IsCancellationRequested) _vm.SetClones(clones);
                },
                ct: cts.Token);

            if (cts.IsCancellationRequested) return;

            // The branch's own Fido config, read before the checkout options are offered: it can pre-select
            // the main clone and stock the Console button's run menu.
            var repoConfig = await ReadRepoConfigAsync(targets, branch, cts.Token);
            if (cts.IsCancellationRequested) return;
            _scanFoundRepoConfig = repoConfig.Read is not null;

            _vm.CompleteScan(targets, IsProtectedBranch(branch),
                preferMainClone: repoConfig.Read?.Config.PreferMainClone == true);
            var placementRepos = targets.Count > 0 && targets.All(IsPlacementKind)
                ? targets.Select(t => t.MainPath).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                : 0;
            _vm.AppendLog(targets.Count == 0
                ? $"⚠ No working tree or clone has '{branch}'."
                : placementRepos > 0
                    ? $"✓ '{branch}' isn't checked out anywhere — {placementRepos} repo(s) can place it (new worktree, or switch the main tree)."
                    : $"✓ Found {targets.Count} location(s) for '{branch}'.");

            // What the branch's own .fido/cfg.yaml asked for: narrated, and stocked into the Console menu.
            // A branch that carries none is said out loud too — "looked, found nothing" and "never looked"
            // are not the same thing to anyone wondering where their run menu went.
            if (repoConfig is { Read: { } read, Target: { } readFrom })
                await ApplyRepoConfigAsync(read, readFrom, branch, cts.Token);
            else if (!repoConfig.Reported && targets.Count > 0)
                _vm.AppendLog($"▸ No {RepoConfigService.RepoRelativePath} on '{branch}' — nothing here, " +
                              $"and nothing on {RepoConfigService.OriginRef(branch)} as last fetched.");

            // Starting a scan wipes the log, so a bad CLI tool id is reported here — after the first
            // completed scan — where it stays visible.
            if (_startupUnknownToolSlug is { } unknownToolSlug)
            {
                _startupUnknownToolSlug = null;
                ReportUnknownTool(unknownToolSlug);
            }

            // CLI --folder: the run starts on the Folder chip (it rides last) instead of the first solution.
            if (_startupPreferFolder)
            {
                _startupPreferFolder = false;
                if (_vm.SolutionChips.Count > 0)
                    _vm.SelectedSolutionChip = _vm.SolutionChips[^1];
            }

            // One-shot CLI auto-open: only for an explicitly named tool, and never when the result
            // needs disambiguating — presenting the choice is the whole point of the redesign.
            if (autoTool is not null && targets.Count == 1)
                await OpenWithAsync(autoTool, fromCommandLine: true);
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer scan or a cleared branch box — that flow owns the UI now
        }
        catch (Exception ex)
        {
            if (cts.IsCancellationRequested) return;
            _vm.CompleteScan([], branchProtected: false);
            _vm.AppendLog($"⚠ Discovery failed: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_scanCts, cts))
            {
                _scanCts = null;
                cts.Dispose();
            }
        }
    }

    /// <summary>True when <paramref name="branch"/> is one of the configured default-branch names (e.g. main/master).</summary>
    private bool IsProtectedBranch(string branch) =>
        _config.MainBranchNames.Any(n => string.Equals(n, branch, StringComparison.OrdinalIgnoreCase));

    /// <summary>True for either placement offer (new worktree / switch the main tree).</summary>
    private static bool IsPlacementKind(DiscoveredTarget t) =>
        t.Kind is TargetKind.NewWorktree or TargetKind.SwitchMainClone;

    // --- In-repo config (.fido/cfg.yaml) ------------------------------------------------

    /// <summary>
    /// The outcome of a scan's in-repo config read: the settings that applied and the target they were read
    /// from, or neither. <paramref name="Reported"/> marks a read that <em>failed</em> and has already said
    /// so in the log, so the plain "this branch carries none" line isn't printed over the top of an error.
    /// </summary>
    private sealed record RepoConfigLookup(
        RepoConfigRead? Read = null, DiscoveredTarget? Target = null, bool Reported = false);

    /// <summary>
    /// The Fido config the scanned branch carries, and the target it was read from. The results are all
    /// the same branch, so the file is the <em>branch's</em> rather than any one card's: the targets are
    /// consulted in order (worktrees first) and the first that carries a config wins — which also means
    /// a clone that has the branch but no <c>.fido</c> folder doesn't mask one that has. Each target is
    /// asked through <see cref="RepoConfigService.ReadAsync"/>, so a copy on <c>origin</c> counts as the
    /// branch carrying one even when the checkout in hand is too old to have it.
    /// </summary>
    private async Task<RepoConfigLookup> ReadRepoConfigAsync(
        IReadOnlyList<DiscoveredTarget> targets, string branch, CancellationToken ct)
    {
        // A clone's two placement cards read the same file out of the same refs, so ask git once.
        var clonesRead = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            if (IsPlacementKind(target) && !clonesRead.Add(target.MainPath)) continue;
            // Reading a repo's own file must never sink a scan: report and carry on without it.
            try
            {
                if (await _repoConfigs.ReadAsync(target, branch, ct) is { } read)
                    return new RepoConfigLookup(read, target);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _vm.AppendLog($"[!] Couldn't read {RepoConfigService.RepoRelativePath} in {target.RepoName}: {ex.Message}");
                return new RepoConfigLookup(Reported: true);
            }
        }
        return new RepoConfigLookup();
    }

    /// <summary>
    /// Acts on the branch's config once the cards are up: narrates what it asked for, and fills the
    /// Console button's run menu with its run files (a <c>*</c> expanded against the branch) plus
    /// <c>aspire start</c> when it asked for one. Nothing here runs a command — the menu only offers them.
    /// </summary>
    private async Task ApplyRepoConfigAsync(RepoConfigRead read, DiscoveredTarget target, string branch,
        CancellationToken ct)
    {
        var runs = new List<ConsoleRunOption>();
        try
        {
            foreach (var file in await _repoConfigs.ResolveRunFilesAsync(read, target, branch, ct))
                runs.Add(ConsoleRunOption.ForRunFile(file));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _vm.AppendLog($"[!] Couldn't list the run files on '{branch}': {ex.Message}");
        }
        if (read.Config.AspireStart) runs.Add(ConsoleRunOption.AspireStart);

        if (ct.IsCancellationRequested) return;
        _vm.SetConsoleRuns(runs);

        var asked = new List<string>();
        if (read.Config.PreferMainClone) asked.Add("main clone preferred");
        if (runs.Count > 0) asked.Add($"{runs.Count} console run option(s)");
        _vm.AppendLog(asked.Count > 0
            ? $"✓ {RepoConfigService.RepoRelativePath} on '{branch}' — {string.Join(", ", asked)}."
            : $"✓ {RepoConfigService.RepoRelativePath} on '{branch}' — nothing in it applies here.");

        // Which copy answered is only worth a line when there's a folder it disagrees with: the settings
        // just applied are the branch's, but the tree the open actions will act on hasn't caught up. A
        // placement offer has no such folder — reading a branch off its refs is simply how those work, and
        // placing it brings the tree down level anyway.
        if (read.IsFromOrigin && !IsPlacementKind(target))
            _vm.AppendLog($"[!] Read from {RepoConfigService.OriginRef(branch)} — the copy here is missing " +
                          "or out of date. Pull to bring this checkout level.");

        // A preference for the main clone that this scan can't honour is worth saying out loud, rather
        // than leaving the user wondering why a worktree is selected.
        if (read.Config.PreferMainClone && _vm.SelectedTarget is { IsMainClone: false, IsSwitchClone: false })
            _vm.AppendLog("[!] No main clone among the results — staying on the first location.");
    }

    /// <summary>
    /// Reads the in-repo config again for a target that has <em>just</em> become a folder on disk, and applies
    /// it when the scan itself came back with nothing. That gap is real: a branch this clone had never fetched
    /// has no config for a scan to find — the scan won't go to the network — and placing it brings both the
    /// branch and its <c>.fido/cfg.yaml</c> down in one go. Skipped when the scan already applied a config, so
    /// a placement never narrates the same settings twice. Never throws: the placement worked and the launch
    /// it belongs to must carry on regardless.
    /// </summary>
    private async Task RefreshRepoConfigAsync(DiscoveredTarget placed, string branch)
    {
        if (_scanFoundRepoConfig || branch.Length == 0) return;
        try
        {
            if (await _repoConfigs.ReadAsync(placed, branch) is { } read)
            {
                _scanFoundRepoConfig = true;
                await ApplyRepoConfigAsync(read, placed, branch, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _vm.AppendLog($"[!] Couldn't read {RepoConfigService.RepoRelativePath} in {placed.RepoName}: {ex.Message}");
        }
    }

    private async void OnRepoConfigClick(object? sender, RoutedEventArgs e) => await EditRepoConfigAsync();

    /// <summary>
    /// The context strip's create/edit action (and the run menu's footer row): makes sure the selected
    /// location has a <c>.fido/cfg.yaml</c> and opens it for editing. A new file is seeded from the
    /// template — every setting at its default, so creating it changes nothing until it's edited — and an
    /// existing one is never touched, only opened. Fido doesn't stage or commit it: what goes into the
    /// repo's history stays the user's call, as with every other git action here. Internal for tests.
    /// </summary>
    internal async Task EditRepoConfigAsync()
    {
        if (!_vm.CanOpen || _vm.SelectedTarget is not { } card) return;
        if (card.IsPlacement)
        {
            // The strip's button is hidden for these, but the run menu's footer row can still be reached.
            _vm.AppendLog($"[!] '{_vm.ScannedBranch}' isn't on disk here yet — open it first, then its {RepoConfigService.RepoRelativePath}.");
            return;
        }

        RepoConfigFile file;
        try
        {
            file = await _repoConfigs.CreateAsync(card.Target.Path);
        }
        catch (Exception ex)
        {
            _vm.AppendLog($"⚠ Couldn't write {RepoConfigService.RepoRelativePath}: {ex.Message}");
            return;
        }

        _vm.AppendLog(file.Created
            ? $"✓ Created {file.Path} — every setting at its default, so nothing changes until you edit it."
            : $"▸ {file.Path} already exists — opening it as it is.");
        _vm.AppendLog("Edit it, commit it, then press Enter to rescan and pick the changes up.");
        OpenFileInDefaultTool(file.Path);
    }

    /// <summary>
    /// Opens <paramref name="path"/> in the run's default tool, so the create/edit action lands the user in
    /// their editor. A terminal or file manager would open the wrong thing and no default means there's
    /// nothing to choose, so those simply don't open it — the log has already named the file either way.
    /// </summary>
    private void OpenFileInDefaultTool(string path)
    {
        if (_runDefaultToolIndex < 0 || _runDefaultToolIndex >= _config.Editors.Count) return;
        var editor = _config.Editors[_runDefaultToolIndex];
        if (editor.Kind is EditorKind.Console or EditorKind.FileExplorer) return;

        var editorPath = _launcher.Locate(editor);
        if (editorPath is null)
        {
            _vm.AppendLog($"[!] {editor.Name} not located — open the file yourself.");
            return;
        }

        try
        {
            _vm.AppendLog($"▸ Opening it in {editor.Name}");
            _launcher.Launch(editor, editorPath, path);
        }
        catch (Exception ex)
        {
            _vm.AppendLog($"⚠ {ex.Message}");
        }
    }

    // --- Opening ------------------------------------------------------------------------

    private async void OnHeroClick(object? sender, RoutedEventArgs e)
    {
        if (_vm.HeroTool is { } hero && hero.Index >= 0 && hero.Index < _config.Editors.Count)
            await OpenWithAsync(_config.Editors[hero.Index]);
    }

    private async void OnToolClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: EditorLaunchOption option }) return;
        if (option.Index < 0 || option.Index >= _config.Editors.Count) return;
        await OpenWithAsync(_config.Editors[option.Index]);
    }

    private async void OnConsoleRunClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ConsoleRunOption run }) return;
        await RunConsoleOptionAsync(run);
    }

    /// <summary>
    /// A pick from the Console button's run menu: open the console on the selected target and run the
    /// chosen command (a run file, or <c>aspire start</c>) there. The option names its own tool, so this
    /// serves the menu whether Console is the hero button or one of the grid buttons. Internal for tests,
    /// which pick from the menu through here rather than through a flyout that only exists once it's open.
    /// </summary>
    internal async Task RunConsoleOptionAsync(ConsoleRunOption run)
    {
        // A shell entry has nothing to run, so it travels as a null command — which also means it skips
        // the pull-before-run fast-forward below. Opening a shell to look around isn't running the thing.
        var command = run.IsShell ? null : run.Command;

        // No tool index means the pick came from the Console tab's own menu rather than a tool button's.
        // That menu exists to drive the pane beneath it, so it always runs there — RunInFido is about
        // what the *launch* buttons do, and has no say over a tab the user is already looking at.
        if (run.ToolIndex < 0)
        {
            await OpenWithAsync(ConsoleEditor(), consoleCommand: command, fromRunMenu: true, inConsolePane: true);
            return;
        }

        if (run.ToolIndex >= _config.Editors.Count) return;
        await OpenWithAsync(_config.Editors[run.ToolIndex], consoleCommand: command, fromRunMenu: true);
    }

    /// <summary>
    /// The Console tool to attribute a Console-tab run to. Its configured path is irrelevant — the pane
    /// hosts the shell itself — so a setup with no Console tool still gets a working tab.
    /// </summary>
    private Editor ConsoleEditor() =>
        _config.Editors.FirstOrDefault(e => e.Kind == EditorKind.Console)
        ?? new Editor { Name = "Console", Kind = EditorKind.Console };

    /// <summary>
    /// Opens the selected target with <paramref name="editor"/>. Gated on the phase machine: does
    /// nothing unless discovery has found the branch. A <see cref="TargetKind.NewWorktree"/> target is
    /// created first — fetch/track and worktree add via the opener — then opened like any other.
    /// Solution-capable tools (Rider / Visual Studio) honour the chosen solution chip; every other
    /// tool opens the folder. <paramref name="consoleCommand"/> — a pick from the Console button's run
    /// menu — is run in the terminal at that folder instead of just opening one there. Internal for tests.
    /// </summary>
    internal async Task OpenWithAsync(Editor editor, bool fromCommandLine = false, string? consoleCommand = null,
        bool fromRunMenu = false, bool inConsolePane = false)
    {
        if (!_vm.CanOpen || _vm.SelectedTarget is not { } card) return;

        CancelPendingClose();   // a fresh open supersedes any countdown left running from the last one
        var branch = _vm.ScannedBranch;
        var solution = editor.OpensSolutions ? _vm.SelectedSolutionChip?.SolutionPath : null;
        RecordMru(branch, _vm.SolutionFilter.Trim());

        try
        {
            string folder;
            if (card.Target.Kind == TargetKind.NewWorktree)
            {
                // The branch isn't checked out anywhere yet — create the offered worktree first.
                var repo = new RepositoryInfo(card.Target.MainPath, "");
                _vm.AppendLog($"▸ Creating a worktree for '{branch}' in {card.Target.RepoName}…");
                var ctx = await _opener.BuildMainContextAsync(repo, branch, _config);
                folder = await _opener.CreateWorktreeAsync(repo, branch, ctx);
                // The worktree now exists on disk: convert the card to a real one so a second tool
                // click opens it instead of trying to add the same worktree again (git would refuse —
                // the branch is now checked out here).
                await MaterialisePlacementAsync(card, folder, TargetKind.Worktree);
            }
            else if (card.Target.Kind == TargetKind.SwitchMainClone)
            {
                // The other placement offer: move the clone's main tree onto the branch. Any
                // uncommitted changes ride along — the card warned; git refuses on conflicts.
                var repo = new RepositoryInfo(card.Target.MainPath, "");
                _vm.AppendLog($"▸ Switching {card.Target.RepoName}'s main tree to '{branch}'…");
                var ctx = await _opener.BuildMainContextAsync(repo, branch, _config);
                folder = await _opener.CheckoutInMainAsync(repo, branch, ctx);
                // The main tree is now on the branch: convert the card to a plain main-clone target so
                // a second click just reopens it rather than re-running the switch.
                await MaterialisePlacementAsync(card, folder, TargetKind.MainClone);
            }
            else
            {
                folder = card.Target.Path;
            }

            // Every card kind converges here on a folder that exists, which is the one place a pre-run
            // update belongs: a freshly-created worktree is no more current than one that's been on disk
            // for weeks (both check out the local branch when there is one), so the target's kind can't
            // decide this and doesn't try to. Advisory — a pull that won't go through still opens the
            // console. Only for a run command: opening a folder to look at it doesn't warrant the wait.
            if (consoleCommand is not null && _config.PullBeforeRun)
                await _opener.UpdateBeforeRunAsync(folder);

            // A placement card's chips previewed the clone's files before the branch was placed;
            // re-resolve the chosen solution against what's actually in the tree now.
            if (solution is not null && card.Target.Kind is TargetKind.NewWorktree or TargetKind.SwitchMainClone)
            {
                var name = Path.GetFileName(solution);
                // A switch keeps the same paths (chip globbed under this very tree); a new worktree
                // gets fresh ones. Either way the file may not exist on this branch — verify, else re-find.
                solution = File.Exists(solution) && solution.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                    ? solution
                    : await Task.Run(() =>
                        Directory.EnumerateFiles(folder, name, SearchOption.AllDirectories).FirstOrDefault());
                if (solution is null)
                    _vm.AppendLog($"[!] {name} isn't on this branch — opening the folder instead.");
            }

            var targetPath = solution ?? folder;

            // Run it here rather than hand it off, when that's what the user has asked for. Only ever for
            // a run-menu pick at the Console tool: opening a folder to look at it is a launch, and belongs
            // in their terminal. The Console tool button is the way out — same command, same folder, their
            // terminal — so choosing this is never a one-way door.
            if (fromRunMenu && editor.Kind == EditorKind.Console && (inConsolePane || _config.RunInFido))
            {
                _vm.AppendLog(consoleCommand is null
                    ? $"▸ Opening a shell in {folder} (Console tab)"
                    : $"▸ Running '{consoleCommand}' in {folder} (Console tab)");
                _vm.AppendLog("Fido? GO!");
                // Start the run, then reveal the tab — both in this turn, so the pane is on screen well
                // before any output arrives. The other order costs a shell: revealing the tab is what
                // asks EnsureStarted for one, and this call would kill that newborn shell a line later,
                // which the terminal reports in the scrollback the run is about to write to.
                ConsoleView.Run(folder, consoleCommand);
                _vm.IsConsoleTab = true;
                return;
            }

            var editorPath = _launcher.Locate(editor);
            if (editorPath is null)
            {
                _vm.AppendLog($"⚠ {editor.Name} not located — set its path in Settings.");
                return;
            }

            _vm.AppendLog($"✓ {editor.Name} located: {editorPath}");
            _vm.AppendLog(consoleCommand is null
                ? $"▸ Opening {(solution is null ? folder : Path.GetFileName(solution))} in {editor.Name}"
                : $"▸ Running '{consoleCommand}' in {folder} ({editor.Name})");
            _vm.AppendLog("Fido? GO!");
            _launcher.Launch(editor, editorPath, targetPath, consoleCommand);
            _vm.AppendLog("The Eagle has landed...");
            MaybeCloseAfterLaunch(fromCommandLine);
        }
        catch (Exception ex)
        {
            _vm.AppendLog($"⚠ {ex.Message}");
        }
    }

    /// <summary>
    /// Replaces a placement card whose branch was just put on disk (worktree created, or clone
    /// switched) with the real <paramref name="kind"/> checkout at <paramref name="folder"/>. Done as
    /// soon as the placement succeeds — before the launch itself, which may fail to locate the editor —
    /// so a follow-up click on the same card opens the existing folder instead of asking git to place
    /// the branch again. The solutions are re-globbed from the tree that now exists (the placement card
    /// only previewed the clone's), keeping the chip row honest for the next open.
    /// </summary>
    private async Task MaterialisePlacementAsync(TargetCard card, string folder, TargetKind kind)
    {
        DateTime? updated = null;
        try { updated = Directory.GetLastWriteTimeUtc(folder); }
        catch { /* advisory meta only — an unreadable timestamp shouldn't block the swap */ }

        var solutions = await Task.Run(() => _opener.FindSolutionsInFolder(folder, _config));
        var materialised = card.Target with
        {
            Path = folder,
            Kind = kind,
            Solutions = solutions,
            UpdatedUtc = updated,
        };
        _vm.ReplaceTarget(card, materialised);

        // The branch is a working tree now, which is more than the scan had to read: a branch this clone
        // had never fetched arrived with the placement, config and all.
        await RefreshRepoConfigAsync(materialised, _vm.ScannedBranch);
    }

    // --- Delete (inline two-step confirm) -------------------------------------------------

    /// <summary>First click: build a fresh deletion plan (dirty files / orphaned commits included)
    /// and swap the button for the in-place confirm strip. Internal for tests.</summary>
    internal async Task RequestDeleteAsync()
    {
        if (!_vm.CanDelete || _vm.SelectedTarget is not { } card) return;
        var branch = _vm.ScannedBranch;

        WorktreeDeletion? plan;
        try
        {
            plan = await _opener.BuildWorktreeDeletionAsync(card.Target.Path, branch);
        }
        catch (Exception ex)
        {
            // Building the plan shells out to git; a failure here must not crash the async-void click.
            _vm.AppendLog($"⚠ {ex.Message}");
            return;
        }

        if (plan is null)
        {
            // Raced to the main tree somehow — nothing removable.
            _vm.AppendLog($"⚠ {card.Target.Path} is a main working tree and can't be removed as a worktree.");
            return;
        }

        // The await above can race a fresh scan or a selection change; arming a confirm against
        // anything but the still-selected card would spell out the wrong deletion.
        if (!_vm.CanDelete || !ReferenceEquals(_vm.SelectedTarget, card) || _vm.ScannedBranch != branch)
            return;

        _pendingDeletePlan = plan;
        _pendingDeleteCard = card;
        _vm.ArmDeleteConfirm(plan);
    }

    /// <summary>
    /// The confirmed delete: removes the worktree and its local branch always, and the branch on
    /// <c>origin</c> too when the user ticked the opt-in and no open pull request blocks it — then drops
    /// the card from the results and re-selects the next target. When git can't remove the folder
    /// (typically a path too long for the OS) the user is offered the permanent, Recycle-Bin-bypassing
    /// folder delete — still a modal, it's exceptional error recovery, not part of the redesigned happy
    /// path. Internal for tests.
    /// </summary>
    internal async Task ConfirmDeleteAsync()
    {
        if (_pendingDeletePlan is not { } plan || _pendingDeleteCard is not { } card) return;

        // Remote delete only when the user ticked it, origin actually has the branch, and no open PR blocks it.
        var deleteRemote = _vm.DeleteRemoteBranch && plan.RemoteBranchExists && !plan.RemoteDeletionBlocked;
        var choice = new WorktreeDeletionChoice(Worktree: true, LocalBranch: true, RemoteBranch: deleteRemote);
        _vm.AppendLog($"🗑 Deleting worktree at {plan.WorktreePath}…");
        try
        {
            await RunDeletionAsync(plan, choice, card, WorktreeDeletionOutcome.Nothing);
        }
        finally
        {
            _vm.CancelDeleteConfirm();
            _pendingDeletePlan = null;
            _pendingDeleteCard = null;
        }
    }

    /// <summary>
    /// Another go at whatever the last delete left standing — the retry strip's button. Only the outstanding
    /// steps re-run (a worktree and local branch that already went are not touched again), and the report that
    /// follows covers the whole attempt, not just this pass. Internal for tests.
    /// </summary>
    internal async Task RetryDeleteAsync()
    {
        if (!_vm.IsDeleteRetryPending || _retryPlan is not { } plan) return;

        var choice = _retryChoice;
        var sofar = _retryOutcome;
        _vm.AppendLog($"🗑 Retrying the delete for '{plan.Branch}'…");
        await RunDeletionAsync(plan, choice, _retryCard, sofar);
    }

    /// <summary>
    /// Runs one deletion pass — the first attempt or a retry — and settles what follows: the flight-log
    /// report, the card, and the retry offer. Every step is reported rather than thrown (see
    /// <see cref="OpenerService.DeleteWorktreeAsync"/>), so a part-way failure lands here with an outcome
    /// naming exactly what is still standing; <paramref name="sofar"/> carries what earlier passes already
    /// removed so the summary describes the whole attempt.
    /// </summary>
    private async Task RunDeletionAsync(
        WorktreeDeletion plan, WorktreeDeletionChoice choice, TargetCard? card, WorktreeDeletionOutcome sofar)
    {
        _vm.IsDeleting = true;
        _vm.ClearDeleteRetry();
        var outcome = sofar;
        var unreported = "";
        try
        {
            try
            {
                outcome = sofar.Merge(await _opener.DeleteWorktreeAsync(plan, choice));
                _vm.AppendLog(DeletionReport.Summary(outcome, plan.Branch));
            }
            catch (WorktreeRemovalException ex)
            {
                // git gave up on the folder (usually a path too long). Offer to delete it straight from disk.
                _vm.AppendLog($"⚠ git couldn't remove the worktree: {ex.Message}");
                var force = await _dialogs.ConfirmForceDeleteWorktreeFolderAsync(new WorktreeForceDelete(ex.WorktreePath, ex.Message));
                if (force)
                {
                    outcome = sofar.Merge(await _opener.ForceDeleteWorktreeAsync(plan, choice));
                    _vm.AppendLog(DeletionReport.Summary(outcome, plan.Branch));
                }
                else
                {
                    // Declined — the worktree stays, and so does the offer to try again later.
                    _vm.AppendLog($"⚠ Couldn't remove the worktree for '{plan.Branch}' — left in place.");
                    outcome = sofar.Merge(new WorktreeDeletionOutcome(
                        [new WorktreeDeletionStep(DeletionTarget.Worktree, DeletionStepStatus.Failed, ex.Message)]));
                }
            }
        }
        catch (Exception ex)
        {
            // Whatever the steps couldn't absorb — git failing to start, an IO error mid-delete. Report it and
            // let the settle below offer a retry of everything that's still outstanding.
            _vm.AppendLog($"⚠ {ex.Message}");
            unreported = ex.Message;
        }
        finally
        {
            _vm.IsDeleting = false;
            SettleDeletion(plan, choice, card, outcome, unreported);
        }
    }

    /// <summary>
    /// The aftermath of a deletion pass: drop the card once its folder has actually gone, then either clear the
    /// retry offer (everything asked for is gone) or arm it against exactly what's left — remembering the plan
    /// so <see cref="RetryDeleteAsync"/> can re-run just those steps.
    /// </summary>
    private void SettleDeletion(
        WorktreeDeletion plan, WorktreeDeletionChoice choice, TargetCard? card, WorktreeDeletionOutcome outcome,
        string unreportedFailure = "")
    {
        // Drop the card once the worktree it points at has gone — including when a later step failed, so a
        // card is never left pointing at nothing. Only if it's still part of the current results, mind: a scan
        // that superseded this delete owns the list (and the phase machine) now.
        var worktreeGone = card is not null
                           && (outcome.IsGone(DeletionTarget.Worktree) || !Directory.Exists(card.Target.Path));
        if (worktreeGone && _vm.Targets.Contains(card!))
            _vm.RemoveTarget(card!);

        var outstanding = outcome.Outstanding(choice);
        if (!outstanding.AnySelected)
        {
            _retryPlan = null;
            _retryCard = null;
            _vm.ClearDeleteRetry();
            return;
        }

        _retryPlan = plan;
        _retryCard = card;
        _retryChoice = outstanding;
        _retryOutcome = outcome;

        // git's own words where the steps produced them; the escaped exception's otherwise.
        var detail = DeletionReport.RetryDetail(outcome);
        if (detail.Length == 0) detail = unreportedFailure;
        _vm.ArmDeleteRetry(DeletionReport.RetryHeadline(outcome, outstanding, plan.Branch), detail);
    }

    /// <summary>Drops the retry offer without touching anything on disk — the leftovers stay where they are.</summary>
    internal void DismissDeleteRetry()
    {
        _retryPlan = null;
        _retryCard = null;
        _vm.ClearDeleteRetry();
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e) => await RequestDeleteAsync();

    private async void OnDeleteConfirmClick(object? sender, RoutedEventArgs e) => await ConfirmDeleteAsync();

    private async void OnDeleteRetryClick(object? sender, RoutedEventArgs e) => await RetryDeleteAsync();

    private void OnDeleteRetryDismissClick(object? sender, RoutedEventArgs e) => DismissDeleteRetry();

    private void OnDeleteCancelClick(object? sender, RoutedEventArgs e)
    {
        _vm.CancelDeleteConfirm();
        _pendingDeletePlan = null;
        _pendingDeleteCard = null;
    }

    private void OnOpenPullRequestClick(object? sender, RoutedEventArgs e)
    {
        var url = _vm.OpenPullRequestUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!_openUrl(url))
            _vm.AppendLog($"⚠ Couldn't open the pull request — {url}");
    }

    private void OnConsoleUrlClicked(object? sender, string url) => OpenTerminalLink(url);

    /// <summary>
    /// Follows a link Ctrl+Clicked in the Console tab, in the browser rather than in Fido, and says in
    /// the flight log which URL went out. Internal for tests.
    ///
    /// The line is not decoration. A build log's link is often long enough to be ellipsised by the eye
    /// rather than the terminal, and an OSC 8 hyperlink need not show its target at all — the tool
    /// prints "view the report" and the URL lives in the escape sequence. Naming it after the fact is
    /// the only place the user gets to read what their click actually opened.
    ///
    /// Only <c>http</c> and <c>https</c> go anywhere. What the shell puts on screen is the shell's
    /// business, but handing an arbitrary scheme to the OS opener is not opening a page — see
    /// <see cref="UrlLauncher.IsWebUrl"/> — and a refusal that said nothing would read as a click that
    /// missed.
    /// </summary>
    internal void OpenTerminalLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!UrlLauncher.IsWebUrl(url))
        {
            _vm.AppendLog($"⚠ Not opening {url} — the console only follows http and https links.");
            return;
        }

        _vm.AppendLog($"▸ Opening {url} in your browser");
        if (!_openUrl(url))
            _vm.AppendLog($"⚠ Couldn't open {url}");
    }

    private async void OnCopyPathClick(object? sender, RoutedEventArgs e) => await CopySelectedPathAsync();

    /// <summary>
    /// Copies the selected target's working-tree path to the clipboard (the ellipsised card path is
    /// otherwise unreadable and un-selectable), narrating the copy in the flight log. Internal for tests.
    /// </summary>
    internal Task CopySelectedPathAsync() =>
        CopyToClipboardAsync(_vm.SelectedPath, "the path", path => $"📋 Copied path to clipboard: {path}");

    private async void OnCopyWorktreePathClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: WorktreeCandidate candidate })
            await CopyWorktreePathAsync(candidate.Path);
    }

    /// <summary>
    /// Copies one of the branch's proposed worktree paths — a row of the line under the branch box — to
    /// the clipboard, so it can be pasted into a terminal whether or not that folder exists yet.
    /// Internal for tests.
    /// </summary>
    internal Task CopyWorktreePathAsync(string path) =>
        CopyToClipboardAsync(path, "the worktree path",
            copied => $"📋 Copied worktree path to clipboard: {copied}");

    private void OnOpenWorktreePathClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: WorktreeCandidate candidate })
            OpenWorktreePathInFileManager(candidate.Path);
    }

    /// <summary>
    /// Opens one of the branch's proposed worktree folders in the OS file manager. The whole point of the
    /// line is that it answers <em>before</em> the worktree exists, so a folder that isn't there yet opens
    /// the nearest ancestor that is — usually the worktree container — with the flight log saying so
    /// rather than failing at a path the user can plainly see on screen. Internal for tests.
    /// </summary>
    internal void OpenWorktreePathInFileManager(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        var folder = WorktreePath.NearestExistingFolder(path);
        if (folder is null)
        {
            _vm.AppendLog($"⚠ Nothing to open — neither {path} nor any folder above it exists yet.");
            return;
        }

        // The user's configured file manager when they kept one; the built-in otherwise, so removing the
        // row from the tool list doesn't take this button with it.
        var explorer = _config.Editors.FirstOrDefault(e => e.Kind == EditorKind.FileExplorer)
                       ?? new Editor { Name = MainWindowViewModel.FileManagerName, Kind = EditorKind.FileExplorer };

        var executable = _launcher.Locate(explorer);
        if (executable is null)
        {
            _vm.AppendLog($"⚠ {explorer.Name} not located — set its path in Settings.");
            return;
        }

        try
        {
            _vm.AppendLog(string.Equals(folder, path, StringComparison.Ordinal)
                ? $"▸ Opening {path} in {explorer.Name}"
                : $"▸ {path} doesn't exist yet — opening {folder} in {explorer.Name} instead.");
            _launcher.Launch(explorer, executable, folder);
        }
        catch (Exception ex)
        {
            _vm.AppendLog($"⚠ {ex.Message}");
        }
    }

    /// <summary>
    /// Puts <paramref name="text"/> on the clipboard and narrates it in the flight log. Best-effort: a
    /// missing or throwing clipboard is reported — named by <paramref name="subject"/> — and never
    /// crashes the async-void click that called it. Nothing to copy is a no-op, log included.
    /// </summary>
    private async Task CopyToClipboardAsync(string text, string subject, Func<string, string> success)
    {
        if (string.IsNullOrEmpty(text)) return;

        var clipboard = Clipboard;
        if (clipboard is null)
        {
            _vm.AppendLog($"⚠ Clipboard unavailable — couldn't copy {subject}.");
            return;
        }

        try
        {
            await clipboard.SetTextAsync(text);
            _vm.AppendLog(success(text));
        }
        catch (Exception ex)
        {
            _vm.AppendLog($"⚠ Couldn't copy {subject}: {ex.Message}");
        }
    }

    // --- Flight log -----------------------------------------------------------------------

    private void ScrollLogToEnd() => LogScroller.Offset = new Vector(0, LogScroller.Extent.Height);

    /// <summary>
    /// Hands the flight log every pixel the rest of the screen isn't using, so making the window taller
    /// grows the log panel rather than opening a gap above it. With no slack to give — a short window, or
    /// a discovery list filling it — the panel falls back to the redesign's content-sized 104…150 box and
    /// the upper section scrolls as before.
    /// </summary>
    private void UpdateFlightLogHeight()
    {
        // While the window is still auto-sizing to its content there is no slack by definition — the
        // height is whatever the screen asked for — and taking any would start a fight the two can't
        // finish: the window trails the panel by a layout pass, so it would shrink back to the panel's
        // old height, hand out that difference again, and never settle. Avalonia drops SizeToContent the
        // moment the user drags an edge, which is exactly when there is room to give.
        if (SizeToContent is SizeToContent.Height or SizeToContent.WidthAndHeight)
        {
            ResetFlightLogHeight();
            return;
        }

        // The upper section scrolls, so it never needs more than its content's height: what's left of the
        // window after that, the log's own label row, and the countdown bar belongs to the panel. Both
        // subtrahends are *desired* heights — unchanged by what the panel is actually given — so with the
        // window's height fixed this lands in one further layout pass.
        var chrome = LogRegion.DesiredSize.Height - LogPanel.DesiredSize.Height;   // label row + gaps + margin
        var spare = RootGrid.Bounds.Height
                    - UpperStack.DesiredSize.Height
                    - CountdownBar.DesiredSize.Height
                    - chrome;

        if (spare <= LogContentMaxHeight)
        {
            ResetFlightLogHeight();
            return;
        }

        // Sub-pixel drift isn't worth a whole layout pass to chase.
        if (Math.Abs(spare - LogPanel.Height) < 1) return;
        LogPanel.MaxHeight = double.PositiveInfinity;
        LogPanel.Height = spare;
    }

    /// <summary>Back to the redesign's content-sized panel: 104px, growing with the lines to 150px.</summary>
    private void ResetFlightLogHeight()
    {
        LogPanel.Height = double.NaN;
        LogPanel.MaxHeight = LogContentMaxHeight;
    }

    private async void OnCopyLogClick(object? sender, RoutedEventArgs e) => await CopyFlightLogAsync();

    private async void OnSaveLogClick(object? sender, RoutedEventArgs e) => await SaveFlightLogAsync();

    /// <summary>
    /// Copies the whole flight log to the clipboard as plain text — the panel shows only its last few
    /// lines, and a launch that went wrong is worth pasting somewhere. Best-effort: a missing or throwing
    /// clipboard is reported, never crashes the async-void click. Internal for tests.
    /// </summary>
    internal Task CopyFlightLogAsync()
    {
        var lines = _vm.Log.Count;   // read before the copy's own line joins them
        return CopyToClipboardAsync(_vm.LogText, "the flight log",
            _ => $"📋 Copied {lines} flight-log line(s) to the clipboard.");
    }

    /// <summary>
    /// Saves the flight log to a text file the user picks. Cancelling the picker is silent — nothing was
    /// asked for; a save that fails says so in the log itself. Internal for tests.
    /// </summary>
    internal async Task SaveFlightLogAsync()
    {
        var text = _vm.LogText;
        if (text.Length == 0) return;

        string? path;
        try
        {
            path = await _dialogs.PickFlightLogPathAsync($"fido-flight-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        }
        catch (Exception ex)
        {
            _vm.AppendLog($"⚠ Couldn't open the save dialog: {ex.Message}");
            return;
        }
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            await File.WriteAllTextAsync(path, text + Environment.NewLine);
            _vm.AppendLog($"✓ Flight log saved to {path}");
        }
        catch (Exception ex)
        {
            _vm.AppendLog($"⚠ Couldn't save the flight log: {ex.Message}");
        }
    }

    // --- Default tool popover / settings ---------------------------------------------------

    /// <summary>Rebuilds the gear popover's radio list from config, ticking the persisted default.</summary>
    private void RebuildDefaultToolChoices()
    {
        _rebuildingToolChoices = true;
        try
        {
            foreach (var old in _vm.DefaultToolChoices)
                old.PropertyChanged -= OnToolChoiceChanged;
            _vm.DefaultToolChoices.Clear();

            for (var i = 0; i < _config.Editors.Count; i++)
                _vm.DefaultToolChoices.Add(new DefaultToolChoice(i, _config.Editors[i].Name));
            _vm.DefaultToolChoices.Add(new DefaultToolChoice(AppConfig.NoDefaultEditor, "No default (equal weight)"));

            foreach (var choice in _vm.DefaultToolChoices)
            {
                choice.IsSelected = choice.Index == _config.DefaultEditorIndex
                    || (choice.Index == AppConfig.NoDefaultEditor && _config.DefaultEditorIndex == AppConfig.NoDefaultEditor);
                choice.PropertyChanged += OnToolChoiceChanged;
            }
        }
        finally
        {
            _rebuildingToolChoices = false;
        }
    }

    // A popover radio was picked: persist it as the default and re-render the hero/grid. A CLI
    // --tool override lasts only until the user makes an explicit choice here.
    private void OnToolChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_rebuildingToolChoices) return;
        if (e.PropertyName != nameof(DefaultToolChoice.IsSelected)) return;
        if (sender is not DefaultToolChoice { IsSelected: true } choice) return;

        _config.DefaultEditorIndex = choice.Index;
        _runDefaultToolIndex = choice.Index;
        TrySaveConfig();
        _vm.SetEditors(_config.Editors, _runDefaultToolIndex);
    }

    /// <summary>
    /// The header's sun/moon: light ↔ dark for the screen in front of you, and nothing else.
    ///
    /// Nothing is saved, on purpose — the config's <see cref="AppConfig.Theme"/> is the default and stays
    /// the default, so this run's flip dies with the window and Settings still shows (and still means)
    /// whatever it showed before. The whole screen follows the variant off <c>DynamicResource</c>, the
    /// Console tab included: it watches the application's variant and repaints with it.
    /// </summary>
    private void OnThemeToggleClick(object? sender, RoutedEventArgs e) => App.ToggleTheme();

    private async void OnAllSettingsClick(object? sender, RoutedEventArgs e) => await ShowSettingsAsync();

    /// <summary>
    /// Opens the settings dialog and re-applies what it changed to the live screen. Internal for tests,
    /// which can't reach the button inside the gear flyout without opening it.
    /// </summary>
    internal async Task ShowSettingsAsync()
    {
        GearButton.Flyout?.Hide();
        await _dialogs.ShowSettingsAsync(_config, _configService);
        // Editors (and the default) may have changed; the CLI per-run override yields to explicit edits.
        _vm.ShowTargetInTitle = _config.ShowTargetInWindowTitle;
        _vm.SetConfig(_config);   // the worktree root may have moved the line under the branch box
        _runDefaultToolIndex = _config.DefaultEditorIndex;
        _vm.SetEditors(_config.Editors, _runDefaultToolIndex);
        RebuildDefaultToolChoices();
        // Takes effect from the next shell: the one running kept the colours it started with.
        ConsoleView.UseFidoPalette = _config.ConsoleUsesFidoPalette;
    }

    // --- Keyboard -------------------------------------------------------------------------

    // Ctrl+1…Ctrl+9 → open with tool index 0…8 (matching the accelerators on the buttons),
    // gated on discovery having found the branch. Esc backs out of a pending delete confirm, or —
    // once the delete has run — dismisses a retry offer left over from it.
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _vm.IsConfirmingDelete)
        {
            e.Handled = true;
            OnDeleteCancelClick(null, new RoutedEventArgs());
            return;
        }

        if (e.Key == Key.Escape && _vm.IsDeleteRetryPending && !_vm.IsDeleting)
        {
            e.Handled = true;
            DismissDeleteRetry();
            return;
        }

        if (e.KeyModifiers != KeyModifiers.Control) return;
        var index = DigitKeyToIndex(e.Key);
        if (index is not { } i || i < 0 || i >= _config.Editors.Count) return;

        e.Handled = true;
        if (!_vm.CanOpen) return;   // the found-gate applies to accelerators too
        var editor = _config.Editors[i];
        Dispatcher.UIThread.Post(() => _ = OpenWithAsync(editor), DispatcherPriority.Input);
    }

    /// <summary>Maps a top-row or numpad digit key (1–9) to a zero-based tool index, else null.</summary>
    private static int? DigitKeyToIndex(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => key - Key.D1,
        >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1,
        _ => null,
    };

    // Key handling for the branch/solution boxes.
    //
    // Ctrl+Space summons the MRU suggestions on demand. Enter in the branch box forces an immediate
    // scan (the debounce is skipped); the AutoCompleteBox eats the first Enter just to close its MRU
    // drop-down, so this handler runs with handledEventsToo and posts the scan after any pending
    // selection/binding settles.
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
        if (ReferenceEquals(box, BranchBox))
            Dispatcher.UIThread.Post(() => _ = RunDiscoveryAsync(), DispatcherPriority.Input);
    }

    // --- MRU ------------------------------------------------------------------------------

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
            // swallow: a failed save here shouldn't interrupt the user
        }
    }

    // --- Startup / CLI --------------------------------------------------------------------

    /// <summary>
    /// Pre-fills inputs from the CLI and resolves the run's tool. A bare first argument or
    /// <c>--branch/-b</c> sets the branch (which starts discovery on open), <c>--solution/-s</c> the
    /// solution filter, and a bare second argument or <c>--tool/-t</c> (legacy <c>--editor/-e</c>)
    /// names a tool: it becomes the run's default (hero button), and — the only auto behaviour left —
    /// opens automatically when discovery finds <em>exactly one</em> location. <c>--tool none</c>
    /// shows the equal-weight grid for this run. An unknown tool id is reported, never guessed.
    /// </summary>
    private StartupPlan ApplyStartupArgs()
    {
        var args = Program.StartupArgs;
        var branchProvided = false;
        var preferFolder = false;
        string? toolSlug = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--branch" or "-b" when i + 1 < args.Length:
                    _vm.BranchName = args[++i];
                    branchProvided = true;
                    break;
                case "--solution" or "-s" when i + 1 < args.Length:
                    _vm.SolutionFilter = args[++i];
                    break;
                case "--tool" or "-t" or "--editor" or "-e" when i + 1 < args.Length:
                    toolSlug = args[++i];
                    break;
                case "--folder":
                    // The Solution/Folder toggle is gone; honour existing scripts by starting the run
                    // on the Folder chip (only Rider/Visual Studio consult the choice anyway).
                    preferFolder = true;
                    break;
                default:
                    // Bare positional arguments: the first is the branch, the second the tool id.
                    if (args[i].StartsWith('-')) break;
                    if (!branchProvided)
                    {
                        _vm.BranchName = args[i];
                        branchProvided = true;
                    }
                    else
                    {
                        toolSlug ??= args[i];   // an explicit --tool still wins over the positional
                    }
                    break;
            }
        }

        // The pre-fill above already queued a debounced scan via the BranchName listener; the Opened
        // handler runs discovery itself, so stop the timer double-firing it.
        _scanDebounce.Stop();

        Editor? tool = null;
        string? unknownSlug = null;
        if (!string.IsNullOrWhiteSpace(toolSlug))
        {
            if (string.Equals(toolSlug.Trim(), "none", StringComparison.OrdinalIgnoreCase))
            {
                // Equal-weight grid for this run only; the persisted config is untouched.
                _runDefaultToolIndex = AppConfig.NoDefaultEditor;
                _vm.SetEditors(_config.Editors, _runDefaultToolIndex);
            }
            else
            {
                tool = _config.FindEditorBySlug(toolSlug);
                if (tool is null)
                {
                    unknownSlug = toolSlug.Trim();
                }
                else
                {
                    // The named tool is this run's default — it takes the hero button.
                    _runDefaultToolIndex = _config.Editors.IndexOf(tool);
                    _vm.SetEditors(_config.Editors, _runDefaultToolIndex);
                }
            }
        }

        return new StartupPlan(branchProvided, tool, unknownSlug, preferFolder);
    }

    /// <summary>
    /// Surfaces a command-line tool id that matched no configured tool: a warning log line listing the
    /// ids that <em>are</em> known, so the user can correct the typo. No auto-open happens.
    /// </summary>
    private void ReportUnknownTool(string slug)
    {
        var known = string.Join(", ", _config.Editors
            .Select(e => e.Slug)
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        var hint = known.Length > 0 ? $" — known tools: {known}" : "";
        _vm.AppendLog($"⚠ Unknown tool '{slug}' on the command line{hint}.");
    }

    /// <summary>What <see cref="ApplyStartupArgs"/> resolved from the CLI: whether a branch was supplied
    /// (starts discovery), the explicitly named tool (drives the one-shot auto-open), any tool id that
    /// didn't match, and whether <c>--folder</c> asked the run to start on the Folder chip.</summary>
    private sealed record StartupPlan(bool BranchProvided, Editor? Tool, string? UnknownToolSlug, bool PreferFolder);

    // --- Auto-close -----------------------------------------------------------------------

    /// <summary>
    /// Closes Fido after a successful launch when the configured <see cref="CloseAfterOpen"/> policy says so:
    /// <see cref="CloseAfterOpen.Always"/> on any launch, <see cref="CloseAfterOpen.CommandLine"/> only for a
    /// CLI-driven run, <see cref="CloseAfterOpen.Never"/> not at all. The close is deferred by
    /// <see cref="AppConfig.CloseAfterOpenDelaySeconds"/> (0 = immediately). Tools launch detached, so
    /// closing Fido leaves them running.
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
    /// another open, or closing the window aborts it and leaves the window as the user left it.
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
        _scanCts?.Cancel();
        _scanDebounce.Stop();
        // The console holds a live child process. Fido is on its way out, and an orphaned shell with no
        // terminal attached to it would linger.
        ConsoleView.Stop();
        base.OnClosed(e);
    }
}
