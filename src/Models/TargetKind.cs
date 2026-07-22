namespace Fido.Models;

/// <summary>What kind of checkout a discovered target is — drives its labelling and whether it can be deleted.</summary>
public enum TargetKind
{
    /// <summary>A linked worktree (removable with <c>git worktree remove</c>).</summary>
    Worktree,

    /// <summary>A clone's main working tree — it can be opened but never deleted.</summary>
    MainClone,

    /// <summary>
    /// The branch isn't checked out anywhere, but this clone has it — as a local branch or on
    /// <c>origin</c>. Opening this target first creates a linked worktree for the branch (fetching
    /// and tracking the remote ref when needed), then launches into it. Never deletable — there's
    /// nothing on disk yet.
    /// </summary>
    NewWorktree,
}
