namespace Fido.Models;

/// <summary>
/// One entry parsed from <c>git worktree list --porcelain</c>. The first entry
/// enumerated by git is the main working tree (<see cref="IsMain"/>).
/// </summary>
public sealed record WorktreeInfo(
    string Path,
    string Head,
    string? Branch,
    bool IsBare,
    bool IsDetached,
    bool IsMain);
