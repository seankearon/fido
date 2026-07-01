using Fido.Models;

namespace Fido.ViewModels;

/// <summary>Read-only display data for the "delete this worktree?" confirmation dialog.</summary>
public sealed class DeleteWorktreeDialogViewModel
{
    public DeleteWorktreeDialogViewModel(WorktreeDeletion plan)
    {
        WorktreePath = plan.WorktreePath;
        Branch = plan.Branch;
        RemoteBranchExists = plan.RemoteBranchExists;
        OutstandingChanges = plan.OutstandingChanges;
        OrphanedCommits = plan.OrphanedCommits;
    }

    public string WorktreePath { get; }
    public string Branch { get; }

    public bool RemoteBranchExists { get; }
    public string RemoteText => RemoteBranchExists
        ? $"Yes — origin/{Branch} will be deleted too"
        : "No — nothing to delete on origin";

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
