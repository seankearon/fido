namespace Fido.Models;

/// <summary>
/// One place the requested branch is checked out, found by the main screen's discovery scan: the
/// working-tree folder, whether it's a linked worktree or the clone's main tree, which repo it
/// belongs to, and the solution files (<c>*.sln</c>/<c>*.slnx</c>/<c>*.slnf</c>) inside it.
/// </summary>
/// <param name="Path">Absolute path of the working tree on the branch.</param>
/// <param name="Kind">Linked worktree or main clone (worktrees are listed first, and only they can be deleted).</param>
/// <param name="RepoName">Folder name of the clone the target belongs to, e.g. <c>platform</c>.</param>
/// <param name="Solutions">Solution files under the target, depth-limited; consulted only by solution-capable tools.</param>
/// <param name="UpdatedUtc">Last write time of the folder — the "updated 3d ago" meta line; null when unreadable.</param>
public sealed record DiscoveredTarget(
    string Path,
    TargetKind Kind,
    string RepoName,
    IReadOnlyList<string> Solutions,
    DateTime? UpdatedUtc);
