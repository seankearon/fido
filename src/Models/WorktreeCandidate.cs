namespace Fido.Models;

/// <summary>
/// A folder the typed branch's worktree would be created in. <paramref name="RepoName"/> names the clone
/// whose sibling convention decides it, and is empty when a configured <see cref="AppConfig.WorktreeRoot"/>
/// settles the path for every repo at once — there is no one repo to attribute it to then.
/// </summary>
/// <param name="Path">Absolute path of the folder; it needn't exist yet.</param>
/// <param name="RepoName">The owning clone's folder name, or <c>""</c> when a worktree root settles it.</param>
public sealed record WorktreeCandidate(string Path, string RepoName)
{
    /// <summary>Whether to show the repo name beside the path — false for a worktree-root answer.</summary>
    public bool HasRepoName => RepoName.Length > 0;

    /// <summary>
    /// The row's tooltip. It carries the whole path — the row itself ellipsises in a narrow window —
    /// under a line saying what it is, since the line has no heading of its own and the folder may not
    /// exist yet.
    /// </summary>
    public string Tip => HasRepoName
        ? $"Where {RepoName}'s worktree for this branch would live:\n{Path}"
        : $"Where this branch's worktree would live:\n{Path}";
}
