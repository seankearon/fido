namespace Fido.Models;

/// <summary>How an update-before-running attempt ended.</summary>
public enum WorktreeUpdateStatus
{
    /// <summary>There was nothing to pull: a detached HEAD, or a branch that tracks no upstream. Not a
    /// failure — a local-only branch is simply already as current as it can be.</summary>
    Skipped,

    /// <summary>The tree is level with its upstream, whether this pull fast-forwarded it or found it
    /// already there.</summary>
    UpToDate,

    /// <summary>git wouldn't (or couldn't) fast-forward: a diverged branch, local changes in the way, or an
    /// unreachable <c>origin</c>. The tree is stale but perfectly usable, so this never withholds the launch.</summary>
    Failed,
}

/// <summary>
/// What <see cref="Services.OpenerService.UpdateBeforeRunAsync"/> did to a folder before a command ran in
/// it. Advisory throughout: every status here — including <see cref="WorktreeUpdateStatus.Failed"/> — still
/// opens the console, because a stale tree the user can work in beats a console that never appeared.
/// </summary>
/// <param name="Status">How it ended.</param>
/// <param name="Upstream">The tracked ref (e.g. <c>origin/feature/x</c>), or empty when there was none.</param>
/// <param name="Detail">git's message for a failed pull; empty otherwise.</param>
public readonly record struct WorktreeUpdate(WorktreeUpdateStatus Status, string Upstream = "", string Detail = "")
{
    /// <summary>True when the tree is level with its upstream — the only status that changed anything.</summary>
    public bool IsUpToDate => Status is WorktreeUpdateStatus.UpToDate;
}
