namespace Fido.Models;

/// <summary>Result of the branch-not-found decision dialog.</summary>
public enum OpenDecision
{
    /// <summary>Switch the main working tree to the branch (creating it if needed).</summary>
    Main,

    /// <summary>Create a new linked worktree and check out there.</summary>
    Worktree,

    /// <summary>User dismissed the dialog.</summary>
    Cancel,
}
