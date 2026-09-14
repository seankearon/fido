namespace Fido.Models;

/// <summary>
/// A repository's own Fido settings, committed to a branch as <c>.fido/cfg.yaml</c> and read by
/// <see cref="Services.RepoConfigService"/> when a scan lands. Where <see cref="AppConfig"/> is the
/// user's machine-wide configuration, this is the <em>repo's</em> — it travels with the branch, so a
/// solution can say how it prefers to be opened and which of its scripts are worth offering.
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
    /// <c>Run files</c>: script names the Console button offers to run at the selected target, in the
    /// order given. A single <c>*</c> entry stands for "every script in the tree root" and is expanded
    /// in place (see <see cref="Services.RepoConfigService.RunFilesWildcard"/>). Offered only — nothing
    /// here ever runs without the user picking it.
    /// </summary>
    public List<string> RunFiles { get; set; } = new();

    /// <summary>
    /// <c>Aspire start</c>: add <c>aspire start</c> to the Console button's run options, for a repo whose
    /// usual entry point is the .NET Aspire app host.
    /// </summary>
    public bool AspireStart { get; set; }

    /// <summary>True when the file asked for nothing Fido acts on — treated the same as no file at all.</summary>
    public bool IsEmpty => !PreferMainClone && !AspireStart && RunFiles.Count == 0;
}
