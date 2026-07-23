using System;
using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Threading;
using Fido.Models;
using Fido.Mvvm;

namespace Fido.ViewModels;

/// <summary>
/// State for the redesigned main window. The screen is a single phase machine
/// (<see cref="DiscoveryPhase"/>): typing a branch triggers discovery, the results render inline as
/// selectable target cards, and the open/delete actions unlock only when the branch is
/// <see cref="DiscoveryPhase.Found"/>. The window's code-behind drives the transitions (debounce,
/// scans, launches); this class holds the observable state the XAML binds to.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    // --- Inputs -----------------------------------------------------------------------

    private string _branchName = "";
    private string _solutionFilter = "";

    public string BranchName
    {
        get => _branchName;
        set => SetField(ref _branchName, value);
    }

    /// <summary>Filters which detected solutions appear as chips (blank = show every one).</summary>
    public string SolutionFilter
    {
        get => _solutionFilter;
        set
        {
            if (SetField(ref _solutionFilter, value))
                RebuildSolutionChips();
        }
    }

    // --- Phase machine ----------------------------------------------------------------

    private DiscoveryPhase _phase = DiscoveryPhase.Idle;

    /// <summary>The branch the current/most recent scan ran for — message text uses this rather than
    /// <see cref="BranchName"/>, which the user may already be retyping.</summary>
    public string ScannedBranch { get; private set; } = "";

    public DiscoveryPhase Phase
    {
        get => _phase;
        private set
        {
            if (!SetField(ref _phase, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(IsScanning));
            OnPropertyChanged(nameof(IsFound));
            OnPropertyChanged(nameof(IsNotFound));
            OnPropertyChanged(nameof(IsLocked));
            OnPropertyChanged(nameof(LockReason));
            OnPropertyChanged(nameof(CanOpen));
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(ShowDeleteRow));
            OnPropertyChanged(nameof(ShowDeleteButton));
            OnPropertyChanged(nameof(ShowDeleteDisabledNote));
        }
    }

    public bool IsIdle => _phase == DiscoveryPhase.Idle;
    public bool IsScanning => _phase == DiscoveryPhase.Scanning;
    public bool IsFound => _phase == DiscoveryPhase.Found;
    public bool IsNotFound => _phase == DiscoveryPhase.NotFound;

    /// <summary>The open-actions gate: everything stays locked until discovery succeeds.</summary>
    public bool IsLocked => _phase != DiscoveryPhase.Found;

    /// <summary>The one-line reason shown in place of the open actions while they're locked.</summary>
    public string LockReason => _phase switch
    {
        DiscoveryPhase.Idle => "🔒 Enter a branch name to begin discovery",
        DiscoveryPhase.Scanning => "🔒 Scanning… open actions unlock when discovery finishes",
        DiscoveryPhase.NotFound => "🔒 No location found — nothing to open",
        _ => "",
    };

    private string _scanningBody = "";

    /// <summary>The scanning card's caption, e.g. <c>Scanning 24 working tree(s) for 'x'…</c>.</summary>
    public string ScanningBody
    {
        get => _scanningBody;
        private set => SetField(ref _scanningBody, value);
    }

    public string NotFoundBody => $"No working tree or clone has '{ScannedBranch}'.";

    /// <summary>The found-state status pill, e.g. <c>✓ 2 locations</c>.</summary>
    public string FoundChipText =>
        Targets.Count == 1 ? "✓ 1 location" : $"✓ {Targets.Count} locations";

    // --- Targets ----------------------------------------------------------------------

    public ObservableCollection<TargetCard> Targets { get; } = new();

    private TargetCard? _selectedTarget;

    public TargetCard? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (!SetField(ref _selectedTarget, value)) return;
            CancelDeleteConfirm();   // a different target invalidates a pending confirm
            RebuildSolutionChips();
            OnPropertyChanged(nameof(CanOpen));
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(SelectedPath));
            OnPropertyChanged(nameof(SelectedKindLabel));
            OnPropertyChanged(nameof(ShowDeleteButton));
            OnPropertyChanged(nameof(ShowDeleteDisabledNote));
            OnPropertyChanged(nameof(DeleteDisabledNote));
        }
    }

    /// <summary>True when there's more than one place to act on — shows the helper strip.</summary>
    public bool HasMultipleTargets => Targets.Count > 1;

    /// <summary>The helper strip's copy — phrased for checkouts, or for placement offers.</summary>
    public string MultiTargetHelperText =>
        Targets.Count > 0 && Targets.All(t => t.IsPlacement)
            ? "This branch isn't checked out anywhere — choose how to place it: a fresh worktree, or switch a clone's main tree:"
            : "This branch is checked out in more than one place — choose which to act on:";

    public string SelectedPath => _selectedTarget?.Path ?? "";
    public string SelectedKindLabel => _selectedTarget?.KindLabel ?? "";

    // --- Solution chips ---------------------------------------------------------------

    public ObservableCollection<SolutionChip> SolutionChips { get; } = new();

    private SolutionChip? _selectedSolutionChip;

    /// <summary>The chosen chip; the Folder chip means "open the working tree itself". Only consulted
    /// by solution-capable tools (Rider / Visual Studio).</summary>
    public SolutionChip? SelectedSolutionChip
    {
        get => _selectedSolutionChip;
        set => SetField(ref _selectedSolutionChip, value);
    }

    /// <summary>True when the selected target detected any solution files — shows the chip row.</summary>
    public bool HasSolutionChips => SolutionChips.Count > 1;   // more than just the Folder chip

    /// <summary>
    /// Rebuilds the chip row for the selected target, applying <see cref="SolutionFilter"/> to the
    /// file names. Selection resets to the first visible solution (or Folder when none survive).
    /// </summary>
    private void RebuildSolutionChips()
    {
        SolutionChips.Clear();
        if (_selectedTarget is not null)
        {
            var filter = SolutionFilter.Trim();
            foreach (var solution in _selectedTarget.Target.Solutions)
            {
                if (filter.Length == 0 ||
                    Path.GetFileName(solution).Contains(filter, StringComparison.OrdinalIgnoreCase))
                    SolutionChips.Add(SolutionChip.ForSolution(solution));
            }
            SolutionChips.Add(SolutionChip.Folder);
            SelectedSolutionChip = SolutionChips.Count > 1 ? SolutionChips[0] : SolutionChip.Folder;
        }
        else
        {
            SelectedSolutionChip = null;
        }
        OnPropertyChanged(nameof(HasSolutionChips));
    }

    // --- Tools (hero + grid) ----------------------------------------------------------

    private EditorLaunchOption? _heroTool;

    /// <summary>The default tool's hero button, or null for the equal-weight grid.</summary>
    public EditorLaunchOption? HeroTool
    {
        get => _heroTool;
        private set
        {
            if (!SetField(ref _heroTool, value)) return;
            OnPropertyChanged(nameof(HasHero));
            OnPropertyChanged(nameof(ShowNoDefaultNote));
            OnPropertyChanged(nameof(HeroLabel));
        }
    }

    public bool HasHero => _heroTool is not null;
    public bool ShowNoDefaultNote => _heroTool is null;
    public string HeroLabel => _heroTool is null ? "" : $"Open in {_heroTool.Name}";

    /// <summary>The non-default tools, laid out as the 3-column grid (all tools when no default).</summary>
    public ObservableCollection<EditorLaunchOption> GridTools { get; } = new();

    /// <summary>
    /// Rebuilds the hero/grid from config. <paramref name="defaultIndex"/> is a position into
    /// <paramref name="editors"/>; <see cref="AppConfig.NoDefaultEditor"/> (or out of range) means no
    /// hero — every tool renders at equal weight. Accelerators stay tied to config order (Ctrl+1…9)
    /// regardless of which tool is the hero.
    /// </summary>
    public void SetEditors(IReadOnlyList<Editor> editors, int defaultIndex)
    {
        GridTools.Clear();
        EditorLaunchOption? hero = null;
        for (var i = 0; i < editors.Count; i++)
        {
            var gesture = i < 9 ? $"Ctrl+{i + 1}" : "";
            var option = new EditorLaunchOption(i, editors[i].Name, gesture, IsDefault: i == defaultIndex);
            if (i == defaultIndex)
                hero = option;
            else
                GridTools.Add(option);
        }
        HeroTool = hero;
    }

    /// <summary>The gear popover's radio rows (each tool + "No default"); populated by the window
    /// alongside <see cref="SetEditors"/> so both views of the config stay in step.</summary>
    public ObservableCollection<DefaultToolChoice> DefaultToolChoices { get; } = new();

    // --- Delete row -------------------------------------------------------------------

    private bool _isBranchProtected;
    private bool _isConfirmingDelete;
    private bool _isDeleting;
    private string _deleteConfirmPath = "";
    private string _deleteConfirmBranch = "";
    private string _deleteConfirmWarnings = "";

    /// <summary>True when the scanned branch is a configured default branch (main/master) — those are
    /// never deletable, even from a worktree. Set by the orchestrator when a scan completes.</summary>
    public bool IsBranchProtected
    {
        get => _isBranchProtected;
        private set
        {
            if (!SetField(ref _isBranchProtected, value)) return;
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(ShowDeleteDisabledNote));
            OnPropertyChanged(nameof(DeleteDisabledNote));
        }
    }

    public bool CanOpen => IsFound && _selectedTarget is not null;

    public bool CanDelete => CanOpen && _selectedTarget!.IsWorktree && !_isBranchProtected;

    /// <summary>The delete row only exists once discovery has found the branch.</summary>
    public bool ShowDeleteRow => IsFound;

    public bool ShowDeleteButton => ShowDeleteRow && !_isConfirmingDelete;

    public bool ShowDeleteDisabledNote => ShowDeleteButton && !CanDelete;

    public string DeleteDisabledNote =>
        _selectedTarget?.IsNewWorktree == true
            ? "nothing to delete yet — opening creates this worktree."
            : _isBranchProtected && _selectedTarget?.IsWorktree == true
                ? "default branches can't be deleted — main/master stay put."
                : "only worktrees can be deleted — the main clone stays put.";

    /// <summary>True while the in-place confirm strip replaces the delete button.</summary>
    public bool IsConfirmingDelete
    {
        get => _isConfirmingDelete;
        private set
        {
            if (!SetField(ref _isConfirmingDelete, value)) return;
            OnPropertyChanged(nameof(ShowDeleteButton));
            OnPropertyChanged(nameof(ShowDeleteDisabledNote));
        }
    }

    /// <summary>True while the deletion is actually running — disables the confirm strip's buttons.</summary>
    public bool IsDeleting
    {
        get => _isDeleting;
        set => SetField(ref _isDeleting, value);
    }

    public string DeleteConfirmPath
    {
        get => _deleteConfirmPath;
        private set => SetField(ref _deleteConfirmPath, value);
    }

    public string DeleteConfirmBranch
    {
        get => _deleteConfirmBranch;
        private set => SetField(ref _deleteConfirmBranch, value);
    }

    /// <summary>Safety warnings folded into the confirm strip (uncommitted changes, orphaned commits);
    /// empty when the worktree is clean and fully pushed.</summary>
    public string DeleteConfirmWarnings
    {
        get => _deleteConfirmWarnings;
        private set
        {
            if (SetField(ref _deleteConfirmWarnings, value))
                OnPropertyChanged(nameof(HasDeleteConfirmWarnings));
        }
    }

    public bool HasDeleteConfirmWarnings => _deleteConfirmWarnings.Length > 0;

    /// <summary>Swaps the delete button for the confirm strip, spelling out exactly what will happen.</summary>
    public void ArmDeleteConfirm(WorktreeDeletion plan)
    {
        DeleteConfirmPath = plan.WorktreePath;
        DeleteConfirmBranch = plan.Branch;

        var warnings = new List<string>();
        if (plan.OutstandingChanges.Count > 0)
            warnings.Add($"⚠ {plan.OutstandingChanges.Count} uncommitted change(s) will be lost.");
        if (plan.OrphanedCommits > 0)
            warnings.Add($"⚠ {plan.OrphanedCommits} commit(s) exist only on this branch — not on origin or any other branch.");
        DeleteConfirmWarnings = string.Join("\n", warnings);

        IsConfirmingDelete = true;
    }

    /// <summary>Backs out of a pending confirm (Esc, Cancel, or the selection changing).</summary>
    public void CancelDeleteConfirm() => IsConfirmingDelete = false;

    // --- Scan lifecycle (driven by the window orchestrator) ---------------------------

    /// <summary>A fresh scan: clears the results, resets the log to the mission-control preamble, and
    /// locks the open actions behind the scanning phase.</summary>
    public void BeginScan(string branch)
    {
        ScannedBranch = branch;
        OnPropertyChanged(nameof(NotFoundBody));
        ScanningBody = $"Scanning working trees for '{branch}'…";
        Targets.Clear();
        SelectedTarget = null;
        OnPropertyChanged(nameof(HasMultipleTargets));
        OnPropertyChanged(nameof(MultiTargetHelperText));
        Phase = DiscoveryPhase.Scanning;
    }

    /// <summary>Updates the scanning caption once the tree enumeration reports its count.</summary>
    public void SetScanTreeCount(int count) =>
        ScanningBody = $"Scanning {count} working tree(s) for '{ScannedBranch}'…";

    /// <summary>Lands the scan: fills the target cards (worktrees first), selects the first, and
    /// resolves the phase to Found or NotFound.</summary>
    public void CompleteScan(IReadOnlyList<DiscoveredTarget> targets, bool branchProtected)
    {
        Targets.Clear();
        foreach (var target in targets)
            Targets.Add(new TargetCard(target));

        IsBranchProtected = branchProtected;
        SelectedTarget = Targets.Count > 0 ? Targets[0] : null;
        OnPropertyChanged(nameof(FoundChipText));
        OnPropertyChanged(nameof(HasMultipleTargets));
        OnPropertyChanged(nameof(MultiTargetHelperText));
        Phase = targets.Count > 0 ? DiscoveryPhase.Found : DiscoveryPhase.NotFound;
    }

    /// <summary>Empty branch box: back to the dashed placeholder, nothing scanned.</summary>
    public void ResetToIdle()
    {
        ScannedBranch = "";
        Targets.Clear();
        SelectedTarget = null;
        OnPropertyChanged(nameof(HasMultipleTargets));
        OnPropertyChanged(nameof(MultiTargetHelperText));
        Phase = DiscoveryPhase.Idle;
    }

    /// <summary>
    /// Drops a just-deleted target from the results, re-selecting the next one — or falling to
    /// NotFound when none remain, matching a fresh scan's outcome.
    /// </summary>
    public void RemoveTarget(TargetCard card)
    {
        Targets.Remove(card);
        OnPropertyChanged(nameof(FoundChipText));
        OnPropertyChanged(nameof(HasMultipleTargets));
        OnPropertyChanged(nameof(MultiTargetHelperText));
        SelectedTarget = Targets.Count > 0 ? Targets[0] : null;
        if (Targets.Count == 0)
            Phase = DiscoveryPhase.NotFound;
    }

    /// <summary>
    /// Swaps a just-placed card for the real checkout it became: a <see cref="TargetKind.NewWorktree"/>
    /// whose worktree was created turns into a <see cref="TargetKind.Worktree"/>, a
    /// <see cref="TargetKind.SwitchMainClone"/> whose main tree was switched turns into a
    /// <see cref="TargetKind.MainClone"/>. Opening the card again then launches the folder that now
    /// exists instead of trying to place the branch a second time — which git rejects, because the
    /// branch is already checked out. Selection follows the swapped card so the just-opened target stays
    /// current (and, for a new worktree, becomes deletable without waiting for a rescan).
    /// </summary>
    public void ReplaceTarget(TargetCard existing, DiscoveredTarget materialised)
    {
        var index = Targets.IndexOf(existing);
        if (index < 0) return;

        var wasSelected = ReferenceEquals(_selectedTarget, existing);
        var card = new TargetCard(materialised);
        Targets[index] = card;

        // Replacing the selected item can bounce the list's SelectedItem through null; set it back
        // explicitly so selection — and everything gated on it — lands on the materialised card.
        if (wasSelected) SelectedTarget = card;

        OnPropertyChanged(nameof(HasMultipleTargets));
        OnPropertyChanged(nameof(MultiTargetHelperText));
    }

    // --- Auto-close countdown ---------------------------------------------------------

    private bool _isClosingCountdown;
    private string _countdownText = "";

    /// <summary>True while the post-launch auto-close countdown is running; shows the "keep open" bar.</summary>
    public bool IsClosingCountdown
    {
        get => _isClosingCountdown;
        private set => SetField(ref _isClosingCountdown, value);
    }

    /// <summary>The countdown caption shown next to the "Keep open" button (e.g. "Closing Fido in 7s").</summary>
    public string CountdownText
    {
        get => _countdownText;
        private set => SetField(ref _countdownText, value);
    }

    /// <summary>Shows/updates the close-countdown bar with the seconds remaining.</summary>
    public void ShowCountdown(int secondsRemaining)
    {
        CountdownText = $"Closing Fido in {secondsRemaining}s";
        IsClosingCountdown = true;
    }

    /// <summary>Hides the close-countdown bar (the countdown was cancelled or has elapsed).</summary>
    public void StopCountdown() => IsClosingCountdown = false;

    // --- MRU --------------------------------------------------------------------------

    /// <summary>Recently used branch names (newest first) shown as the branch box's MRU suggestions.</summary>
    public ObservableCollection<string> RecentBranches { get; } = new();

    /// <summary>Recently used solution names (newest first) shown as the solution box's MRU suggestions.</summary>
    public ObservableCollection<string> RecentSolutions { get; } = new();

    /// <summary>Replaces the MRU suggestion lists from persisted config.</summary>
    public void LoadMru(IEnumerable<string> branches, IEnumerable<string> solutions)
    {
        Replace(RecentBranches, branches);
        Replace(RecentSolutions, solutions);

        static void Replace(ObservableCollection<string> target, IEnumerable<string> source)
        {
            target.Clear();
            foreach (var item in source) target.Add(item);
        }
    }

    // --- Flight log -------------------------------------------------------------------

    private bool _liveLineActive;

    /// <summary>Color-coded flight-log lines.</summary>
    public ObservableCollection<LogLine> Log { get; } = new();

    public void AppendLog(string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
            AddLine(LogLine.Infer(message));
        else
            Dispatcher.UIThread.Post(() => AddLine(LogLine.Infer(message)));
    }

    /// <summary>Appends a line at an explicit colour level (bypasses prefix inference).</summary>
    public void AppendLog(string message, LogLevel level)
    {
        if (Dispatcher.UIThread.CheckAccess())
            AddLine(new LogLine(message, level));
        else
            Dispatcher.UIThread.Post(() => AddLine(new LogLine(message, level)));
    }

    /// <summary>Appends a finished line, closing off any in-place live line (see <see cref="SetLiveLog"/>).</summary>
    private void AddLine(LogLine line)
    {
        _liveLineActive = false;
        Log.Add(line);
    }

    /// <summary>
    /// Writes a single line at the end of the log and overwrites it on each subsequent call, so a value can
    /// tick in place (the close countdown reads 10 → 9 → 8… on one line, not a line per second). The next
    /// <see cref="AppendLog(string)"/> closes the live line and starts a fresh one.
    /// </summary>
    public void SetLiveLog(string message, LogLevel level)
    {
        void Apply()
        {
            var line = new LogLine(message, level);
            if (_liveLineActive && Log.Count > 0)
                Log[^1] = line;
            else
            {
                Log.Add(line);
                _liveLineActive = true;
            }
        }

        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }

    /// <summary>
    /// Writes a plain string as an in-place "live" line (see <see cref="SetLiveLog"/>), inferring its colour
    /// from any marker prefix — the live-log counterpart of <see cref="AppendLog(string)"/>. Used for progress
    /// that should tick in place on one line (e.g. the per-repo branch search) rather than a line apiece.
    /// </summary>
    public void AppendLiveLog(string message) => SetLiveLog(message, LogLine.Infer(message).Level);

    public void ClearLog()
    {
        void Apply()
        {
            _liveLineActive = false;
            Log.Clear();
        }

        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }
}
