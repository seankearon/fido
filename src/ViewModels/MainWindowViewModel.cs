using System;
using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Threading;
using Fido.Models;
using Fido.Mvvm;
using Fido.Services;

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
    /// <summary>The plain window title: what the title bar reads before discovery resolves anything, and
    /// all it ever reads when <see cref="ShowTargetInTitle"/> is off.</summary>
    public const string AppTitle = "Fido";

    /// <summary>The running build's version, for the badge beside the header wordmark: <c>v0.9.3</c>.
    /// Read from the assembly, so it names what is actually running rather than what a file on disk says.</summary>
    public string VersionLabel => AppVersion.Label;

    /// <summary>Whether there is a version to show at all — false only for a build that carries none,
    /// which keeps the badge (and its gap after the wordmark) out of the header entirely.</summary>
    public bool HasVersionLabel => VersionLabel.Length > 0;

    public MainWindowViewModel() =>
        // The log's copy/save actions are gated on there being something to hand over.
        Log.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasLog));

    // --- Inputs -----------------------------------------------------------------------

    private string _branchName = "";
    private string _solutionFilter = "";

    public string BranchName
    {
        get => _branchName;
        set
        {
            if (SetField(ref _branchName, value))
                RebuildWorktreePaths();
        }
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

    // --- Worktree path ----------------------------------------------------------------

    private AppConfig _config = new();
    private IReadOnlyList<string> _clones = [];

    /// <summary>
    /// Hands the view model the config the worktree-path line is worked out from. Called by the window
    /// on startup and again after Settings, so editing the worktree root re-answers the line under the
    /// branch box straight away — the same instance both times, hence the unconditional rebuild.
    /// </summary>
    public void SetConfig(AppConfig config)
    {
        _config = config;
        RebuildWorktreePaths();
    }

    /// <summary>
    /// Hands over the clones the last scan reached. They're what makes the sibling convention
    /// answerable — a branch name alone doesn't name a repo — and they don't depend on the branch, so
    /// they're kept and every later branch is answered without scanning again.
    /// </summary>
    public void SetClones(IReadOnlyList<string> clones)
    {
        _clones = clones;
        RebuildWorktreePaths();
    }

    /// <summary>
    /// The worktree folders this branch already has on disk — one row each, with the repo that owns it
    /// (unnamed when a configured worktree root settles the path for every repo at once). Empty, and so
    /// hidden, when the branch has no folder anywhere: the line reports what is there, never what could be.
    /// </summary>
    public ObservableCollection<WorktreeCandidate> WorktreePaths { get; } = new();

    /// <summary>True when there is at least one folder to show — drives the line's visibility.</summary>
    public bool HasWorktreePaths => WorktreePaths.Count > 0;

    /// <summary>What this OS calls its file manager, so the open button's tooltip says Finder on a Mac.</summary>
    public static string FileManagerName =>
        OperatingSystem.IsMacOS() ? "Finder"
        : OperatingSystem.IsWindows() ? "File Explorer"
        : "your file manager";

    /// <summary>The open button's tooltip, named for this OS's file manager.</summary>
    public string OpenWorktreePathTip => $"Open in {FileManagerName}";

    private void RebuildWorktreePaths()
    {
        WorktreePaths.Clear();
        foreach (var candidate in WorktreePath.Candidates(_branchName, _clones, _config))
            WorktreePaths.Add(candidate);
        OnPropertyChanged(nameof(HasWorktreePaths));
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
            OnPropertyChanged(nameof(CanEditRepoConfig));
            OnPropertyChanged(nameof(ShowDeleteRow));
            OnPropertyChanged(nameof(ShowDeleteButton));
            OnPropertyChanged(nameof(ShowDeleteDisabledNote));
            OnPropertyChanged(nameof(ShowPullRequestLink));
            OnPropertyChanged(nameof(WindowTitle));
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
            CancelMoveConfirm();     // …and a pending move, which was spelled out for the old one
            RebuildSolutionChips();
            OnPropertyChanged(nameof(CanOpen));
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(CanEditRepoConfig));
            OnPropertyChanged(nameof(SelectedPath));
            OnPropertyChanged(nameof(SelectedKindLabel));
            OnPropertyChanged(nameof(WindowTitle));
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

    // --- Window title -----------------------------------------------------------------

    private bool _showTargetInTitle = true;

    /// <summary>
    /// Whether a resolved discovery takes over the window title (the
    /// <see cref="AppConfig.ShowTargetInWindowTitle"/> setting). Off keeps <see cref="AppTitle"/> throughout.
    /// </summary>
    public bool ShowTargetInTitle
    {
        get => _showTargetInTitle;
        set
        {
            if (SetField(ref _showTargetInTitle, value))
                OnPropertyChanged(nameof(WindowTitle));
        }
    }

    /// <summary>
    /// The window title. Once discovery has resolved the branch and a target is selected it names that
    /// work — <c>platform · feature/new-ui</c>, the selected card's repo and the scanned branch, with no
    /// "Fido" in front, so a taskbar full of Fido windows is readable. Falls back to <see cref="AppTitle"/>
    /// while nothing is resolved, and whenever <see cref="ShowTargetInTitle"/> is off.
    /// </summary>
    public string WindowTitle =>
        _showTargetInTitle && IsFound && _selectedTarget is { } card
            ? $"{card.Target.RepoName} · {ScannedBranch}"
            : AppTitle;

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
            OnPropertyChanged(nameof(HasHeroRuns));
        }
    }

    public bool HasHero => _heroTool is not null;
    public bool ShowNoDefaultNote => _heroTool is null;
    public string HeroLabel => _heroTool is null ? "" : $"Open in {_heroTool.Name}";

    /// <summary>True when the hero <em>is</em> the Console tool and the branch offered run commands —
    /// the caret beside the hero button.</summary>
    public bool HasHeroRuns => _heroTool?.HasRuns == true;

    /// <summary>The non-default tools, laid out as the 3-column grid (all tools when no default).</summary>
    public ObservableCollection<EditorLaunchOption> GridTools { get; } = new();

    private IReadOnlyList<Editor> _editors = [];
    private int _defaultToolIndex = AppConfig.NoDefaultEditor;
    private IReadOnlyList<ConsoleRunOption> _consoleRuns = [];

    private bool _isConsoleTab;

    /// <summary>
    /// Which of the two bottom panels is showing. They share the strip of window under the open actions,
    /// and the right-hand end of the tab row belongs to whichever is selected — the log's copy/save
    /// buttons, or the console's run menu.
    /// </summary>
    public bool IsConsoleTab
    {
        get => _isConsoleTab;
        set
        {
            if (!SetField(ref _isConsoleTab, value)) return;
            OnPropertyChanged(nameof(IsFlightLogTab));
        }
    }

    /// <summary>The other side of <see cref="IsConsoleTab"/>; bound two-way by the Flight log tab.</summary>
    public bool IsFlightLogTab
    {
        get => !_isConsoleTab;
        set => IsConsoleTab = !value;
    }

    /// <summary>
    /// The Console tab's run menu: a fresh shell first, then whatever the branch's <c>.fido/cfg.yaml</c>
    /// nominated. The shell entry is unconditional here — unlike the Console <em>tool</em> button, which
    /// launches the user's own terminal, this menu only ever runs in the pane below it, so "give me a
    /// clean prompt again" is a useful thing to ask for after a script has left the screen full.
    /// </summary>
    public IReadOnlyList<ConsoleRunOption> ConsoleTabRuns =>
        [ConsoleRunOption.ShellHere, .. _consoleRuns];

    /// <summary>
    /// Sets the tools the screen offers. <paramref name="defaultIndex"/> is a position into
    /// <paramref name="editors"/>; <see cref="AppConfig.NoDefaultEditor"/> (or out of range) means no
    /// hero — every tool renders at equal weight. Accelerators stay tied to config order (Ctrl+1…9)
    /// regardless of which tool is the hero.
    /// </summary>
    public void SetEditors(IReadOnlyList<Editor> editors, int defaultIndex)
    {
        _editors = editors;
        _defaultToolIndex = defaultIndex;
        RebuildTools();
    }

    /// <summary>
    /// Replaces the run commands offered under the Console tool — the ones the scanned branch's
    /// <c>.fido/cfg.yaml</c> listed. They belong to the scan, so a new one clears them; a later
    /// <see cref="SetEditors"/> (a settings change mid-scan) keeps them.
    /// </summary>
    public void SetConsoleRuns(IReadOnlyList<ConsoleRunOption> runs)
    {
        // Every scan clears the menu on the way in; when there was nothing there, skip the rebuild
        // rather than churning the tool buttons on each keystroke-debounced scan.
        if (_consoleRuns.Count == 0 && runs.Count == 0) return;
        _consoleRuns = runs;
        OnPropertyChanged(nameof(ConsoleTabRuns));
        RebuildTools();
    }

    /// <summary>Rebuilds the hero/grid from the stored tool list, default position and console runs.</summary>
    private void RebuildTools()
    {
        GridTools.Clear();
        EditorLaunchOption? hero = null;
        for (var i = 0; i < _editors.Count; i++)
        {
            var gesture = i < 9 ? $"Ctrl+{i + 1}" : "";
            // Only the Console tool carries a run menu — it's the one that can host a command.
            var runs = _editors[i].Kind == EditorKind.Console
                ? _consoleRuns.Select(run => run with { ToolIndex = i }).ToArray()
                : [];
            var option = new EditorLaunchOption(i, _editors[i].Name, gesture, IsDefault: i == _defaultToolIndex)
            {
                Runs = runs,
            };
            if (i == _defaultToolIndex)
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
    private bool _remoteBranchExists;
    private bool _deleteRemoteBranch;
    private PullRequestInfo? _openPullRequest;

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

    /// <summary>
    /// Whether the selected location can carry a <c>.fido/cfg.yaml</c> — true for a real checkout, false
    /// for a placement offer, which has no tree on disk to write one into yet. Drives the context strip's
    /// create/edit action, which is simply absent rather than dimmed when there's nowhere to write.
    /// </summary>
    public bool CanEditRepoConfig => CanOpen && _selectedTarget is { IsPlacement: false };

    /// <summary>The delete row only exists once discovery has found the branch.</summary>
    public bool ShowDeleteRow => IsFound;

    public bool ShowDeleteButton => ShowDeleteRow && !_isConfirmingDelete;

    public bool ShowDeleteDisabledNote => ShowDeleteButton && !CanDelete;

    public string DeleteDisabledNote =>
        _selectedTarget?.IsNewWorktree == true
            ? "nothing to delete yet — opening creates this worktree."
            : _selectedTarget?.IsMoveToMain == true
            ? "nothing to delete here — opening moves the branch into the main clone, and asks first."
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

    /// <summary>True when origin has this branch — the confirm strip then offers to delete it too.</summary>
    public bool RemoteBranchExists
    {
        get => _remoteBranchExists;
        private set
        {
            if (!SetField(ref _remoteBranchExists, value)) return;
            OnPropertyChanged(nameof(ShowRemoteBranchOption));
            OnPropertyChanged(nameof(CanDeleteRemoteBranch));
        }
    }

    /// <summary>The "also delete the remote branch" row shows only when there's a remote branch to delete.</summary>
    public bool ShowRemoteBranchOption => _remoteBranchExists;

    /// <summary>The remote-branch delete opt-in — unticked by default (the safe choice), and forced off /
    /// disabled while an open pull request blocks it.</summary>
    public bool DeleteRemoteBranch
    {
        get => _deleteRemoteBranch;
        set => SetField(ref _deleteRemoteBranch, value);
    }

    /// <summary>The remote-branch checkbox is enabled only when there's a remote branch and no open PR.</summary>
    public bool CanDeleteRemoteBranch => _remoteBranchExists && _openPullRequest is null;

    /// <summary>
    /// True when the scanned branch has an open pull request, as of the last time Fido asked GitHub. It
    /// drives two things at once: the link row under the discovery results, and — in the delete confirm
    /// strip — the block on deleting the branch from <c>origin</c>.
    /// </summary>
    public bool HasOpenPullRequest => _openPullRequest is not null;

    /// <summary>The PR's caption, e.g. <c>PR #42 · Add the widget</c>; empty when none.</summary>
    public string OpenPullRequestLabel =>
        _openPullRequest is null ? "" : $"PR #{_openPullRequest.Number} · {_openPullRequest.Title}";

    /// <summary>The PR's web URL, opened from the link row and from the confirm strip; empty when none.</summary>
    public string OpenPullRequestUrl => _openPullRequest?.Url ?? "";

    /// <summary>The PR link row belongs to a landed scan: it shows once a branch has been found somewhere
    /// and GitHub has named a pull request open on it.</summary>
    public bool ShowPullRequestLink => IsFound && HasOpenPullRequest;

    /// <summary>
    /// Records what GitHub said about the branch this time round — a PR, or <c>null</c> for "none open,
    /// or nobody could tell us". Every check routes through here (the scan's own lookup, and the one the
    /// delete plan makes), so the link row and the remote-delete gate always agree and always show the
    /// latest answer rather than a remembered one.
    /// </summary>
    public void SetOpenPullRequest(PullRequestInfo? pullRequest)
    {
        _openPullRequest = pullRequest;
        OnPropertyChanged(nameof(HasOpenPullRequest));
        OnPropertyChanged(nameof(OpenPullRequestLabel));
        OnPropertyChanged(nameof(OpenPullRequestUrl));
        OnPropertyChanged(nameof(ShowPullRequestLink));
        OnPropertyChanged(nameof(CanDeleteRemoteBranch));
    }

    /// <summary>The remote-branch option's caption, naming the ref that would be deleted.</summary>
    public string RemoteBranchOptionText => $"Also delete the remote branch origin/{_deleteConfirmBranch}";

    /// <summary>Swaps the delete button for the confirm strip, spelling out exactly what will happen.</summary>
    public void ArmDeleteConfirm(WorktreeDeletion plan)
    {
        DeleteConfirmPath = plan.WorktreePath;
        DeleteConfirmBranch = plan.Branch;

        // Remote-branch opt-in + PR gate. The plan asked GitHub afresh, so what it carries refreshes the link
        // row above as well as the gate here — but it only asks when there's a branch on origin to delete, so
        // with no remote branch it never asked, and that silence mustn't be mistaken for an answer that
        // clears what the scan found. Default the checkbox OFF (opt-in); it's disabled outright when a PR
        // blocks it, and RemoteBranchExists is set last so its notifications land on the settled PR state.
        if (plan.RemoteBranchExists)
            SetOpenPullRequest(plan.OpenPullRequest);
        DeleteRemoteBranch = false;
        RemoteBranchExists = plan.RemoteBranchExists;
        OnPropertyChanged(nameof(RemoteBranchOptionText));
        OnPropertyChanged(nameof(ShowRemoteBranchOption));

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

    // --- Move-to-main-clone confirm strip -----------------------------------------------

    private bool _isConfirmingMove;
    private bool _isMoving;
    private MainCloneMove? _moveConfirm;
    private string _moveConfirmLabel = "Move";

    /// <summary>True while the in-place strip asks before a "move to main clone" card removes its worktree.</summary>
    public bool IsConfirmingMove
    {
        get => _isConfirmingMove;
        private set => SetField(ref _isConfirmingMove, value);
    }

    /// <summary>True while the move is running — disables the strip's buttons.</summary>
    public bool IsMoving
    {
        get => _isMoving;
        set
        {
            if (SetField(ref _isMoving, value))
                OnPropertyChanged(nameof(CanConfirmMove));
        }
    }

    public string MoveConfirmWorktreePath => _moveConfirm?.WorktreePath ?? "";
    public string MoveConfirmBranch => _moveConfirm?.Branch ?? "";
    public string MoveConfirmCurrentBranch => _moveConfirm?.CurrentBranch ?? "";

    /// <summary>The confirm button's caption, naming what happens after the move — e.g. <c>Move &amp; open in Rider</c>.</summary>
    public string MoveConfirmLabel
    {
        get => _moveConfirmLabel;
        private set => SetField(ref _moveConfirmLabel, value);
    }

    /// <summary>What stands in the way (changes in the worktree) or rides along (changes in the main tree);
    /// empty when both trees are clean.</summary>
    public string MoveConfirmWarnings
    {
        get
        {
            if (_moveConfirm is not { } move) return "";
            var warnings = new List<string>();
            if (move.WorktreeChanges.Count > 0)
                warnings.Add($"⚠ The worktree has {move.WorktreeChanges.Count} uncommitted or untracked change(s) — " +
                             "commit, stash or remove them first; Fido won't force the worktree away.");
            if (move.MainChanges.Count > 0)
                warnings.Add($"⚠ {move.MainChanges.Count} uncommitted change(s) in the main clone ride along onto " +
                             $"'{move.Branch}'.");
            return string.Join("\n", warnings);
        }
    }

    public bool HasMoveConfirmWarnings => MoveConfirmWarnings.Length > 0;

    /// <summary>The confirm button is live only when the worktree can go without losing anything.</summary>
    public bool CanConfirmMove => _moveConfirm is { CanMove: true } && !_isMoving;

    /// <summary>Raises the strip for <paramref name="move"/>; <paramref name="action"/> names what follows the move.</summary>
    public void ArmMoveConfirm(MainCloneMove move, string action)
    {
        _moveConfirm = move;
        MoveConfirmLabel = $"Move & {action}";
        OnPropertyChanged(nameof(MoveConfirmWorktreePath));
        OnPropertyChanged(nameof(MoveConfirmBranch));
        OnPropertyChanged(nameof(MoveConfirmCurrentBranch));
        OnPropertyChanged(nameof(MoveConfirmWarnings));
        OnPropertyChanged(nameof(HasMoveConfirmWarnings));
        OnPropertyChanged(nameof(CanConfirmMove));
        IsConfirmingMove = true;
    }

    /// <summary>Backs out of a pending move (Esc, Cancel, the selection changing, or the move being done).</summary>
    public void CancelMoveConfirm()
    {
        IsConfirmingMove = false;
        _moveConfirm = null;
        OnPropertyChanged(nameof(CanConfirmMove));
    }

    // --- Delete retry strip -------------------------------------------------------------

    private bool _isDeleteRetryPending;
    private string _deleteRetryHeadline = "";
    private string _deleteRetryDetail = "";

    /// <summary>True when a delete left something standing (a branch on <c>origin</c> the push couldn't remove,
    /// a folder still held open) and the retry strip is offering another go at just that step. It outlives the
    /// deleted card — the strip sits outside the delete row, so it survives the results emptying.</summary>
    public bool IsDeleteRetryPending
    {
        get => _isDeleteRetryPending;
        private set => SetField(ref _isDeleteRetryPending, value);
    }

    /// <summary>What's still there, and what already went — the retry strip's one-line explanation.</summary>
    public string DeleteRetryHeadline
    {
        get => _deleteRetryHeadline;
        private set => SetField(ref _deleteRetryHeadline, value);
    }

    /// <summary>git's own words for the failure, shown under the headline; empty when it said nothing useful.</summary>
    public string DeleteRetryDetail
    {
        get => _deleteRetryDetail;
        private set
        {
            if (SetField(ref _deleteRetryDetail, value))
                OnPropertyChanged(nameof(HasDeleteRetryDetail));
        }
    }

    public bool HasDeleteRetryDetail => _deleteRetryDetail.Length > 0;

    /// <summary>Offers a retry of whatever a delete left behind, spelling out what's outstanding.</summary>
    public void ArmDeleteRetry(string headline, string detail)
    {
        DeleteRetryHeadline = headline;
        DeleteRetryDetail = detail;
        IsDeleteRetryPending = true;
    }

    /// <summary>Takes the retry offer away — it succeeded, was dismissed, or a fresh scan made it stale.</summary>
    public void ClearDeleteRetry()
    {
        IsDeleteRetryPending = false;
        DeleteRetryHeadline = "";
        DeleteRetryDetail = "";
    }

    // --- Scan lifecycle (driven by the window orchestrator) ---------------------------

    /// <summary>A fresh scan: clears the results, resets the log to the mission-control preamble, and
    /// locks the open actions behind the scanning phase.</summary>
    public void BeginScan(string branch)
    {
        ScannedBranch = branch;
        OnPropertyChanged(nameof(NotFoundBody));
        ScanningBody = $"Scanning working trees for '{branch}'…";
        ClearDeleteRetry();        // a leftover from the last branch's delete has nothing to say about this scan
        SetConsoleRuns([]);        // the console's run menu came from the last branch's .fido config
        SetOpenPullRequest(null);  // and the last branch's pull request is not this branch's — ask again
        Targets.Clear();
        SelectedTarget = null;
        OnPropertyChanged(nameof(HasMultipleTargets));
        OnPropertyChanged(nameof(MultiTargetHelperText));
        Phase = DiscoveryPhase.Scanning;
    }

    /// <summary>Updates the scanning caption once the tree enumeration reports its count.</summary>
    public void SetScanTreeCount(int count) =>
        ScanningBody = $"Scanning {count} working tree(s) for '{ScannedBranch}'…";

    /// <summary>
    /// Lands the scan: fills the target cards (worktrees first), selects one, and resolves the phase to
    /// Found or NotFound. <paramref name="preferMainClone"/> comes from the branch's own
    /// <c>.fido/cfg.yaml</c> and decides which of the cards is offered by default (see
    /// <see cref="PickInitialTarget"/>) — the scan itself found them all regardless.
    /// </summary>
    public void CompleteScan(IReadOnlyList<DiscoveredTarget> targets, bool branchProtected,
        bool preferMainClone = false)
    {
        Targets.Clear();
        foreach (var target in targets)
            Targets.Add(new TargetCard(target));

        IsBranchProtected = branchProtected;
        SelectedTarget = PickInitialTarget(preferMainClone);
        OnPropertyChanged(nameof(FoundChipText));
        OnPropertyChanged(nameof(HasMultipleTargets));
        OnPropertyChanged(nameof(MultiTargetHelperText));
        Phase = targets.Count > 0 ? DiscoveryPhase.Found : DiscoveryPhase.NotFound;
    }

    /// <summary>
    /// The checkout offered by default once the scan has landed — what the open actions act on until
    /// another card is picked. Normally the first result, and worktrees lead, so that's a worktree when
    /// there is one; when the branch's <c>.fido/cfg.yaml</c> asks for the main clone it's the clone's
    /// own working tree instead, whether that's already on the branch
    /// (<see cref="TargetKind.MainClone"/>) or offered to switch onto it
    /// (<see cref="TargetKind.SwitchMainClone"/>), or offered to take the branch from the worktree that
    /// holds it (<see cref="TargetKind.MoveToMainClone"/>, which asks before it acts). With no main tree
    /// among the cards the first one keeps the default.
    /// </summary>
    private TargetCard? PickInitialTarget(bool preferMainClone)
    {
        if (Targets.Count == 0) return null;
        if (!preferMainClone) return Targets[0];
        return Targets.FirstOrDefault(t => t.IsMainClone)
               ?? Targets.FirstOrDefault(t => t.IsSwitchClone)
               ?? Targets.FirstOrDefault(t => t.IsMoveToMain)
               ?? Targets[0];
    }

    /// <summary>Empty branch box: back to the dashed placeholder, nothing scanned.</summary>
    public void ResetToIdle()
    {
        ScannedBranch = "";
        SetConsoleRuns([]);        // no branch, so no in-repo config and no run menu
        SetOpenPullRequest(null);  // …and no branch to have a pull request open on it
        Targets.Clear();
        SelectedTarget = null;
        OnPropertyChanged(nameof(HasMultipleTargets));
        OnPropertyChanged(nameof(MultiTargetHelperText));
        Phase = DiscoveryPhase.Idle;
    }

    /// <summary>
    /// Drops a target that's gone from disk from the results, re-selecting the next one when it was the
    /// selected card — or falling to
    /// NotFound when none remain, matching a fresh scan's outcome.
    /// </summary>
    public void RemoveTarget(TargetCard card)
    {
        Targets.Remove(card);
        OnPropertyChanged(nameof(FoundChipText));
        OnPropertyChanged(nameof(HasMultipleTargets));
        OnPropertyChanged(nameof(MultiTargetHelperText));
        // Keep the selection when a card other than the selected one went (a move released its worktree).
        var kept = _selectedTarget is not null && Targets.Contains(_selectedTarget) ? _selectedTarget : null;
        SelectedTarget = kept ?? (Targets.Count > 0 ? Targets[0] : null);
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

    /// <summary>True once the log has a line in it — enables the copy/save actions above the panel.</summary>
    public bool HasLog => Log.Count > 0;

    /// <summary>
    /// The whole flight log as plain text, one line per entry — what the copy-to-clipboard and
    /// save-to-file actions hand over. Colour levels are presentation only and don't survive the trip.
    /// </summary>
    public string LogText => string.Join(Environment.NewLine, Log.Select(line => line.Text));

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
