namespace Fido.Models;

/// <summary>
/// Which parts of a located worktree the user chose to delete in the confirmation dialog. Each is ticked by
/// default (when the target is present) and can be unticked to keep it. Deleting the local branch requires
/// removing the worktree first — a checked-out branch can't be deleted — so the dialog keeps those coupled.
/// </summary>
public sealed record WorktreeDeletionChoice(bool Worktree, bool LocalBranch, bool RemoteBranch)
{
    /// <summary>True when at least one target is selected — otherwise the delete would be a no-op.</summary>
    public bool AnySelected => Worktree || LocalBranch || RemoteBranch;

    /// <summary>Everything ticked — the default when all three targets are present.</summary>
    public static WorktreeDeletionChoice All { get; } = new(true, true, true);

    /// <summary>True when <paramref name="target"/> is ticked — the selection read one target at a time.</summary>
    public bool Includes(DeletionTarget target) => target switch
    {
        DeletionTarget.Worktree => Worktree,
        DeletionTarget.LocalBranch => LocalBranch,
        DeletionTarget.RemoteBranch => RemoteBranch,
        _ => false,
    };
}
