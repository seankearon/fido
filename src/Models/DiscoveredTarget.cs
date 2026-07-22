namespace Fido.Models;

/// <summary>
/// One place the requested branch can be acted on, found by the main screen's discovery scan: a
/// working tree already on the branch (linked worktree or the clone's main tree), or — when the
/// branch is checked out nowhere — a clone that has the branch in its refs, offered as a
/// <see cref="TargetKind.NewWorktree"/> that opening will create.
/// </summary>
/// <param name="Path">Absolute path of the working tree on the branch; for a
/// <see cref="TargetKind.NewWorktree"/>, the path the worktree would be created at.</param>
/// <param name="Kind">Linked worktree, main clone, or a creatable worktree (worktrees are listed
/// first, and only existing linked worktrees can be deleted).</param>
/// <param name="RepoName">Folder name of the clone the target belongs to, e.g. <c>platform</c>.</param>
/// <param name="MainPath">The clone's main working tree — where git commands for this target run.</param>
/// <param name="Solutions">Solution files under the target, depth-limited; consulted only by
/// solution-capable tools. For a creatable worktree this previews the clone's own solutions.</param>
/// <param name="UpdatedUtc">Last write time of the folder — the "updated 3d ago" meta line; null when
/// unreadable or when nothing exists on disk yet.</param>
/// <param name="BranchOnOriginOnly">For a creatable worktree: true when the branch exists only on
/// <c>origin</c> (creation will fetch and track it), false when a local branch already carries it.</param>
public sealed record DiscoveredTarget(
    string Path,
    TargetKind Kind,
    string RepoName,
    string MainPath,
    IReadOnlyList<string> Solutions,
    DateTime? UpdatedUtc,
    bool BranchOnOriginOnly = false);
