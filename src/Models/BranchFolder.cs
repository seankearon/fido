namespace Fido.Models;

/// <summary>
/// A working tree found on the requested branch (branch-only mode), with its HEAD commit
/// and a GitHub commit URL when the remote is GitHub (null otherwise).
/// </summary>
public sealed record BranchFolder(string Path, string Head, string? CommitUrl);
