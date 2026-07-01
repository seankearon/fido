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
}

/// <summary>What a delete actually removed, so the caller can report it accurately.</summary>
public sealed record WorktreeDeletionOutcome(
    bool WorktreeRemoved,
    bool LocalBranchDeleted,
    bool RemoteBranchDeleted,
    bool RemoteDeleteFailed)
{
    public bool AnyDeleted => WorktreeRemoved || LocalBranchDeleted || RemoteBranchDeleted;
}
