using Fido.Models;

namespace Fido.ViewModels;

/// <summary>Read-only display data for the branch-not-found decision dialog.</summary>
public sealed class DecisionDialogViewModel
{
    public DecisionDialogViewModel(RepositoryInfo repo, string branch, MainContext context)
    {
        RepoDisplay = repo.DisplayName;
        Branch = branch;
        BranchExistsOnRemote = context.BranchExistsOnRemote;
        StartPointDescription = context.StartPointDescription;
        CurrentBranch = context.CurrentBranch;
        ProposedWorktreePath = context.ProposedWorktreePath;
        OutstandingChanges = context.OutstandingChanges;
    }

    public string RepoDisplay { get; }
    public string Branch { get; }

    public bool BranchExistsOnRemote { get; }
    public string RemoteText => BranchExistsOnRemote ? "Yes — exists on origin" : "No — not on origin";

    public string StartPointDescription { get; }
    public string CurrentBranch { get; }
    public string ProposedWorktreePath { get; }

    public IReadOnlyList<string> OutstandingChanges { get; }
    public bool HasOutstandingChanges => OutstandingChanges.Count > 0;

    public string OutstandingSummary => HasOutstandingChanges
        ? $"{OutstandingChanges.Count} outstanding change(s) in the main working tree:"
        : "Main working tree is clean.";

    public bool ShowSwitchWarning => HasOutstandingChanges;

    public string SwitchWarning =>
        "⚠ Switching in the main tree carries these changes onto the branch, or may be blocked by conflicts. " +
        "Creating a worktree leaves the main tree untouched.";
}
