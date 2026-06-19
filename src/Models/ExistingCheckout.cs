namespace Fido.Models;

/// <summary>
/// An existing on-disk checkout of the requested branch: the <see cref="Worktree"/>
/// (which may be a clone's main tree or a linked worktree) and the <see cref="Repo"/>
/// (clone) it belongs to. Used to open an existing checkout instead of creating a duplicate.
/// </summary>
public sealed record ExistingCheckout(RepositoryInfo Repo, WorktreeInfo Worktree)
{
    public string Path => Worktree.Path;
}
