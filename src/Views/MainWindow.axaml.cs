using System;
using System.Collections;
using System.ComponentModel;
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
    /// <summary>How long the branch box stays quiet before a scan fires (Enter fires immediately).</summary>
    internal static readonly TimeSpan ScanDebounce = TimeSpan.FromMilliseconds(600);

    private readonly MainWindowViewModel _vm = new();
    private readonly ConfigService _configService;
    private readonly GitService _git;
    private readonly IEditorLauncher _launcher;
    private readonly IDialogService _dialogs;
    private readonly OpenerService _opener;
    private readonly AppConfig _config;

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

    /// <summary>The deletion plan built when the confirm strip was armed; consumed on Delete.</summary>
    private WorktreeDeletion? _pendingDeletePlan;
    private TargetCard? _pendingDeleteCard;

    /// <summary>Guards the popover radio handlers while the list itself is being rebuilt.</summary>
    private bool _rebuildingToolChoices;

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
        _launcher = services.Launcher;

        // Load config and apply the theme variant before the XAML resolves its DynamicResources.
        _config = _configService.Load();
        App.ApplyTheme(_config.Theme);

        InitializeComponent();
        DataContext = _vm;
        _vm.LoadMru(_config.RecentBranches, _config.RecentSolutions);

        _runDefaultToolIndex = _config.DefaultEditorIndex;
        _vm.SetEditors(_config.Editors, _runDefaultToolIndex);
        RebuildDefaultToolChoices();

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
        _opener = new OpenerService(_git, services.Finder, services.WorkingTreeFinder, _vm.AppendLog, _vm.AppendLiveLog);
        _vm.Log.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(ScrollLogToEnd, DispatcherPriority.Background);

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
                _ = RunDiscoveryAsync();
            }
        };
    }

    // --- Discovery ----------------------------------------------------------------------

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
                ct: cts.Token);

            if (cts.IsCancellationRequested) return;

            _vm.CompleteScan(targets, IsProtectedBranch(branch));
            var placementRepos = targets.Count > 0 && targets.All(IsPlacementKind)
                ? targets.Select(t => t.MainPath).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                : 0;
            _vm.AppendLog(targets.Count == 0
                ? $"⚠ No working tree or clone has '{branch}'."
                : placementRepos > 0
                    ? $"✓ '{branch}' isn't checked out anywhere — {placementRepos} repo(s) can place it (new worktree, or switch the main tree)."
                    : $"✓ Found {targets.Count} location(s) for '{branch}'.");

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

    /// <summary>
    /// Opens the selected target with <paramref name="editor"/>. Gated on the phase machine: does
    /// nothing unless discovery has found the branch. A <see cref="TargetKind.NewWorktree"/> target is
    /// created first — fetch/track and worktree add via the opener — then opened like any other.
    /// Solution-capable tools (Rider / Visual Studio) honour the chosen solution chip; every other
    /// tool opens the folder. Internal for tests.
    /// </summary>
    internal async Task OpenWithAsync(Editor editor, bool fromCommandLine = false)
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
            }
            else if (card.Target.Kind == TargetKind.SwitchMainClone)
            {
                // The other placement offer: move the clone's main tree onto the branch. Any
                // uncommitted changes ride along — the card warned; git refuses on conflicts.
                var repo = new RepositoryInfo(card.Target.MainPath, "");
                _vm.AppendLog($"▸ Switching {card.Target.RepoName}'s main tree to '{branch}'…");
                var ctx = await _opener.BuildMainContextAsync(repo, branch, _config);
                folder = await _opener.CheckoutInMainAsync(repo, branch, ctx);
            }
            else
            {
                folder = card.Target.Path;
            }

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
            var editorPath = _launcher.Locate(editor);
            if (editorPath is null)
            {
                _vm.AppendLog($"⚠ {editor.Name} not located — set its path in Settings.");
                return;
            }

            _vm.AppendLog($"✓ {editor.Name} located: {editorPath}");
            _vm.AppendLog($"▸ Opening {(solution is null ? folder : Path.GetFileName(solution))} in {editor.Name}");
            _vm.AppendLog("Fido? GO!");
            _launcher.Launch(editor, editorPath, targetPath);
            _vm.AppendLog("The Eagle has landed...");
            MaybeCloseAfterLaunch(fromCommandLine);
        }
        catch (Exception ex)
        {
            _vm.AppendLog($"⚠ {ex.Message}");
        }
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
    /// The confirmed delete: removes the worktree and its local branch (never the remote — that lives
    /// in Settings-free land now), drops the card from the results, and re-selects the next target.
    /// When git can't remove the folder (typically a path too long for the OS) the user is offered the
    /// permanent, Recycle-Bin-bypassing folder delete — still a modal, it's exceptional error recovery,
    /// not part of the redesigned happy path. Internal for tests.
    /// </summary>
    internal async Task ConfirmDeleteAsync()
    {
        if (_pendingDeletePlan is not { } plan || _pendingDeleteCard is not { } card) return;

        var choice = new WorktreeDeletionChoice(Worktree: true, LocalBranch: true, RemoteBranch: false);
        _vm.IsDeleting = true;
        _vm.AppendLog($"🗑 Deleting worktree at {plan.WorktreePath}…");
        try
        {
            try
            {
                await _opener.DeleteWorktreeAsync(plan, choice);
            }
            catch (WorktreeRemovalException ex)
            {
                // git gave up on the folder (usually a path too long). Offer to delete it straight from disk.
                _vm.AppendLog($"⚠ git couldn't remove the worktree: {ex.Message}");
                var force = await _dialogs.ConfirmForceDeleteWorktreeFolderAsync(new WorktreeForceDelete(ex.WorktreePath, ex.Message));
                if (!force)
                {
                    _vm.AppendLog($"⚠ Couldn't remove the worktree for '{plan.Branch}' — left in place.");
                    return;
                }
                await _opener.ForceDeleteWorktreeAsync(plan, choice);
            }

            _vm.AppendLog($"✓ Removed worktree & branch '{plan.Branch}'.");
            // Only drop the card if it's still part of the current results — a scan that superseded
            // this delete owns the list (and the phase machine) now.
            if (_vm.Targets.Contains(card))
                _vm.RemoveTarget(card);
        }
        catch (Exception ex)
        {
            _vm.AppendLog($"⚠ {ex.Message}");
            // A part-way failure can still have removed the folder (e.g. the branch delete failed
            // after the worktree went) — don't leave a card pointing at nothing.
            if (!Directory.Exists(card.Target.Path) && _vm.Targets.Contains(card))
                _vm.RemoveTarget(card);
        }
        finally
        {
            _vm.IsDeleting = false;
            _vm.CancelDeleteConfirm();
            _pendingDeletePlan = null;
            _pendingDeleteCard = null;
        }
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e) => await RequestDeleteAsync();

    private async void OnDeleteConfirmClick(object? sender, RoutedEventArgs e) => await ConfirmDeleteAsync();

    private void OnDeleteCancelClick(object? sender, RoutedEventArgs e)
    {
        _vm.CancelDeleteConfirm();
        _pendingDeletePlan = null;
        _pendingDeleteCard = null;
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

    private async void OnAllSettingsClick(object? sender, RoutedEventArgs e)
    {
        GearButton.Flyout?.Hide();
        await _dialogs.ShowSettingsAsync(_config, _configService);
        // Editors (and the default) may have changed; the CLI per-run override yields to explicit edits.
        _runDefaultToolIndex = _config.DefaultEditorIndex;
        _vm.SetEditors(_config.Editors, _runDefaultToolIndex);
        RebuildDefaultToolChoices();
    }

    // --- Keyboard -------------------------------------------------------------------------

    // Ctrl+1…Ctrl+9 → open with tool index 0…8 (matching the accelerators on the buttons),
    // gated on discovery having found the branch. Esc backs out of a pending delete confirm.
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _vm.IsConfirmingDelete)
        {
            e.Handled = true;
            OnDeleteCancelClick(null, new RoutedEventArgs());
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

    private void ScrollLogToEnd() => LogScroller.Offset = new Vector(0, LogScroller.Extent.Height);

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
        base.OnClosed(e);
    }
}
