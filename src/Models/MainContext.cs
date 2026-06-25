namespace Fido.Models;

/// <summary>
/// Snapshot of the main working tree's state plus the resolved plan for putting the
/// requested branch somewhere. Feeds the branch-not-found decision dialog.
/// </summary>
public sealed class MainContext
{
    /// <summary>Absolute path of the repo's main working tree.</summary>
    public required string MainWorktreePath { get; init; }

    /// <summary>Branch currently checked out in the main working tree (<c>HEAD</c> if detached).</summary>
    public required string CurrentBranch { get; init; }

    public bool BranchExistsLocally { get; init; }
    public bool BranchExistsOnRemote { get; init; }

    /// <summary>
    /// True when the branch exists on <c>origin</c> but hasn't been fetched into this clone yet, so its
    /// <c>refs/remotes/origin/&lt;branch&gt;</c> tracking ref must be fetched before a tracking branch or
    /// worktree can be created from it.
    /// </summary>
    public bool RequiresFetch { get; init; }

    /// <summary>Outstanding changes in the main tree (porcelain lines); empty when clean.</summary>
    public IReadOnlyList<string> OutstandingChanges { get; init; } = [];

    public bool HasOutstandingChanges => OutstandingChanges.Count > 0;

    /// <summary>Path that would be used if the user chooses to create a new worktree.</summary>
    public required string ProposedWorktreePath { get; init; }

    /// <summary>Git start-point ref used when the branch must be created (null = current HEAD).</summary>
    public string? StartPoint { get; init; }

    public bool StartPointIsRemoteTracking { get; init; }

    /// <summary>Human-readable description of where the new branch will start from.</summary>
    public required string StartPointDescription { get; init; }
}
