namespace Fido.Models;

/// <summary>
/// What moving a branch out of its linked worktree and into the clone's main tree would do, gathered
/// fresh when the move is asked for (see <see cref="TargetKind.MoveToMainClone"/>): the worktree that
/// would be removed and what's outstanding in it, and the main tree that would be switched and what's
/// outstanding there. Feeds the inline confirm strip and the git steps that carry the move out.
/// </summary>
/// <param name="MainWorktreePath">The clone's main working tree — switched onto the branch, and where
/// the git commands run.</param>
/// <param name="WorktreePath">The linked worktree that has the branch checked out, removed first.</param>
/// <param name="Branch">The branch being moved. It is never deleted: only the worktree goes.</param>
/// <param name="CurrentBranch">What the main tree is on now — the branch the switch moves it off.</param>
/// <param name="WorktreeChanges">Uncommitted or untracked changes in the worktree (porcelain lines). Any at
/// all block the move: removing the worktree would lose them, so Fido never forces it.</param>
/// <param name="MainChanges">Uncommitted changes in the main tree (porcelain lines). They ride along onto the
/// branch, as with any switch — the strip warns, and git refuses where they conflict.</param>
public sealed record MainCloneMove(
    string MainWorktreePath,
    string WorktreePath,
    string Branch,
    string CurrentBranch,
    IReadOnlyList<string> WorktreeChanges,
    IReadOnlyList<string> MainChanges)
{
    /// <summary>False while the worktree carries changes the removal would lose.</summary>
    public bool CanMove => WorktreeChanges.Count == 0;
}
