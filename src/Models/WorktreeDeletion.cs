namespace Fido.Models;

/// <summary>
/// What a "delete this worktree" action removes: the linked worktree folder, its local branch, and —
/// when it exists — the branch on <c>origin</c>. Built from the located worktree in branch-only mode,
/// it feeds the delete-confirmation dialog and the git steps that carry the deletion out.
/// </summary>
/// <param name="MainWorktreePath">The clone's main working tree — where the git commands run, so the
/// linked worktree can be dropped without standing inside it.</param>
/// <param name="WorktreePath">Absolute path of the linked worktree to remove.</param>
/// <param name="Branch">The local branch checked out in the worktree, deleted after the worktree is gone.</param>
/// <param name="RemoteBranchExists">True when <c>origin/&lt;Branch&gt;</c> exists and should be deleted too.</param>
/// <param name="OutstandingChanges">Uncommitted changes in the worktree (porcelain lines); empty when clean.
/// A dirty worktree needs a forced removal and would lose these — the dialog warns about it.</param>
/// <param name="OrphanedCommits">Commits that live only on this branch — not pushed, not merged, not on any
/// other ref — and so would be lost when the branch is force-deleted. The dialog warns when this is above 0,
/// since neither an uncommitted-changes warning nor "not on origin" would otherwise flag the loss.</param>
public sealed record WorktreeDeletion(
    string MainWorktreePath,
    string WorktreePath,
    string Branch,
    bool RemoteBranchExists,
    IReadOnlyList<string> OutstandingChanges,
    int OrphanedCommits)
{
    public bool HasOutstandingChanges => OutstandingChanges.Count > 0;
    public bool HasOrphanedCommits => OrphanedCommits > 0;
}
