namespace Fido.Models;

/// <summary>
/// Which of the three copies of a branch's <c>.fido/cfg.yaml</c> a read settled on. A branch can carry
/// the file in more than one place at once — edited in the tree, committed on <c>origin</c>, committed in
/// this clone — and they need not agree, so the source is reported rather than left to be guessed at.
/// The order they're tried in is <see cref="Services.RepoConfigService.ReadAsync"/>'s.
/// </summary>
public enum RepoConfigSource
{
    /// <summary>
    /// The file in the working tree, carrying changes that aren't committed — the one being written right
    /// now. It wins outright: an edit in flight is the most deliberate statement of intent there is.
    /// </summary>
    LocalEdit,

    /// <summary>
    /// The copy committed on <c>origin/&lt;branch&gt;</c>, used whenever it differs from the one this
    /// machine has. That's the case a stale checkout creates: the branch grew a config (or a newer one)
    /// after this worktree was made, and reading only what's on disk would silently miss it. Read from the
    /// tracking ref as last fetched — a scan never goes to the network.
    /// </summary>
    Origin,

    /// <summary>
    /// The copy this clone has: the committed file in the working tree, or — for a branch that's checked
    /// out nowhere — the one on the local branch ref. What Fido has always read, and still the answer when
    /// <c>origin</c> has nothing to add.
    /// </summary>
    Local,
}

/// <summary>
/// A branch's Fido settings and where they were read from. Only ever handed back for a config that asks
/// for something: a file that parses to nothing is treated as no file at all, so it never shadows a copy
/// further down the order (see <see cref="Services.RepoConfigService.ReadAsync"/>).
/// </summary>
/// <param name="Config">What the file asked for.</param>
/// <param name="Source">Which copy answered — the flight log says so when it wasn't this machine's.</param>
public sealed record RepoConfigRead(RepoConfig Config, RepoConfigSource Source)
{
    /// <summary>True when the settings came off <c>origin</c> because the copy here is missing or differs.</summary>
    public bool IsFromOrigin => Source is RepoConfigSource.Origin;
}
