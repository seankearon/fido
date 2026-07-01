using Fido.Models;
using Fido.Mvvm;

namespace Fido.ViewModels;

/// <summary>
/// Backing data for the "delete this worktree?" confirmation dialog. Exposes a ticked-by-default checkbox for
/// each present target — the worktree, its local branch, and the branch on origin — that the user can untick
/// to keep. Deleting the local branch requires removing the worktree first (a checked-out branch can't be
/// deleted), so the local-branch box is disabled and cleared whenever the worktree box is unticked.
/// </summary>
public sealed class DeleteWorktreeDialogViewModel : ObservableObject
{
    private bool _deleteWorktree = true;
    private bool _deleteLocalBranch = true;
    private bool _deleteRemoteBranch;

    public DeleteWorktreeDialogViewModel(WorktreeDeletion plan)
    {
        WorktreePath = plan.WorktreePath;
        Branch = plan.Branch;
        RemoteBranchExists = plan.RemoteBranchExists;
        OutstandingChanges = plan.OutstandingChanges;
        OrphanedCommits = plan.OrphanedCommits;
        _deleteRemoteBranch = plan.RemoteBranchExists;   // ticked by default when there's a remote branch
    }

    public string WorktreePath { get; }
    public string Branch { get; }

    public bool RemoteBranchExists { get; }

    /// <summary>The origin-branch checkbox is only shown when the branch actually exists on origin.</summary>
    public bool HasRemoteBranch => RemoteBranchExists;

    /// <summary>Dim note shown in place of the origin checkbox when the branch was never pushed.</summary>
    public bool ShowNoRemoteNote => !RemoteBranchExists;

    public bool DeleteWorktree
    {
        get => _deleteWorktree;
        set
        {
            if (!SetField(ref _deleteWorktree, value)) return;
            // Keep the local-branch box in step: a branch checked out in a worktree we're keeping can't be deleted.
            if (!value) DeleteLocalBranch = false;
            OnPropertyChanged(nameof(CanDeleteLocalBranch));
            OnPropertyChanged(nameof(ShowLocalBranchCoupledHint));
            OnPropertyChanged(nameof(CanConfirm));
        }
    }

    public bool DeleteLocalBranch
    {
        get => _deleteLocalBranch;
        set { if (SetField(ref _deleteLocalBranch, value)) OnPropertyChanged(nameof(CanConfirm)); }
    }

    public bool DeleteRemoteBranch
    {
        get => _deleteRemoteBranch;
        set { if (SetField(ref _deleteRemoteBranch, value)) OnPropertyChanged(nameof(CanConfirm)); }
    }

    /// <summary>The local-branch box is usable only while the worktree is being removed.</summary>
    public bool CanDeleteLocalBranch => DeleteWorktree;

    /// <summary>Shows the "remove the worktree first" hint when keeping the worktree blocks deleting its branch.</summary>
    public bool ShowLocalBranchCoupledHint => !DeleteWorktree;

    /// <summary>Delete is enabled only when at least one present target is selected.</summary>
    public bool CanConfirm => DeleteWorktree || DeleteLocalBranch || (DeleteRemoteBranch && HasRemoteBranch);

    /// <summary>The user's selection, with the origin flag forced off when there's no remote branch to delete.</summary>
    public WorktreeDeletionChoice ToChoice() =>
        new(DeleteWorktree, DeleteLocalBranch, DeleteRemoteBranch && HasRemoteBranch);

    public IReadOnlyList<string> OutstandingChanges { get; }
    public bool HasOutstandingChanges => OutstandingChanges.Count > 0;

    /// <summary>Red warning shown when the worktree is dirty — a forced removal loses these changes.</summary>
    public string OutstandingSummary => HasOutstandingChanges
        ? $"⚠ {OutstandingChanges.Count} uncommitted change(s) in this worktree will be lost."
        : "";

    public int OrphanedCommits { get; }
    public bool HasOrphanedCommits => OrphanedCommits > 0;

    /// <summary>
    /// Red warning shown when the branch carries commits that exist nowhere else — force-deleting the branch
    /// (<c>git branch -D</c>) would orphan them even though the working tree is clean and "not on origin"
    /// alone wouldn't signal the loss.
    /// </summary>
    public string OrphanedSummary => HasOrphanedCommits
        ? $"⚠ {OrphanedCommits} commit(s) exist only on '{Branch}' — not on origin or any other branch. Deleting the branch loses them."
        : "";
}
