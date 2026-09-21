namespace Fido.Models;

/// <summary>
/// A repository's own Fido settings, committed to a branch as <c>.fido/cfg.yaml</c> and read by
/// <see cref="Services.RepoConfigService"/> when a scan lands. Where <see cref="AppConfig"/> is the
/// user's machine-wide configuration, this is the <em>repo's</em> — it travels with the branch, so a
/// solution can say how it prefers to be opened and which commands are worth offering.
/// Everything here is optional; a branch with no file behaves exactly as Fido always has.
/// </summary>
public sealed class RepoConfig
{
    /// <summary>
    /// <c>Prefer main clone</c>: which checkout Fido offers by default once a scan has landed — the
    /// clone's own working tree rather than a worktree — for a solution that only builds in its main
    /// checkout (local tooling, IIS bindings, a fixed path). It directs the default <em>choice</em>,
    /// never the scan: every location on the branch is still found and listed, one click away.
    /// </summary>
    public bool PreferMainClone { get; set; }

    /// <summary>
    /// <c>Commands</c>: command lines the Console button offers to run at the selected target, in the
    /// order given — each one exactly what you'd type in a terminal there (<c>build.ps1</c>,
    /// <c>aspire start</c>, <c>npm run dev</c>), handed to the shell as written. Offered only — nothing
    /// here ever runs without the user picking it.
    /// </summary>
    public List<string> Commands { get; set; } = new();

    /// <summary>True when the file asked for nothing Fido acts on — treated the same as no file at all.</summary>
    public bool IsEmpty => !PreferMainClone && Commands.Count == 0;
}
