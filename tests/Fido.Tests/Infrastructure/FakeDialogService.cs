using Fido.Models;
using Fido.Services;
using Fido.ViewModels;

namespace Fido.Tests.Infrastructure;

public sealed record ChooserRequest(string Title, string Prompt, IReadOnlyList<ChooserItem> Items, string? DeleteLabel = null);

public sealed record DecisionRequest(RepositoryInfo Repo, string Branch, MainContext Context);

/// <summary>
/// A real <see cref="IDialogService"/> that returns scripted choices and records every request, so the
/// end-to-end flow can be driven without real modal windows. The real dialog widgets are tested
/// separately. A fake, not a mock — assert against the recorded requests after the flow runs.
/// </summary>
public sealed class FakeDialogService : IDialogService
{
    /// <summary>Chooser responder; defaults to selecting the first item.</summary>
    public Func<ChooserRequest, int?> OnChooser { get; set; } = _ => 0;

    /// <summary>Decision responder; defaults to "create worktree".</summary>
    public Func<DecisionRequest, OpenDecision?> OnDecision { get; set; } = _ => OpenDecision.Worktree;

    /// <summary>Delete-confirmation responder; defaults to declining (the safe default for a destructive action).</summary>
    public Func<WorktreeDeletion, bool> OnConfirmDelete { get; set; } = _ => false;

    public List<ChooserRequest> ChooserRequests { get; } = new();
    public List<DecisionRequest> DecisionRequests { get; } = new();
    public List<WorktreeDeletion> DeleteConfirmations { get; } = new();
    public int SettingsShownCount { get; private set; }

    public ChooserRequest? LastChooser => ChooserRequests.Count > 0 ? ChooserRequests[^1] : null;

    public Task<int?> ShowChooserAsync(string title, string prompt, IReadOnlyList<ChooserItem> items, string? deleteLabel = null)
    {
        var request = new ChooserRequest(title, prompt, items, deleteLabel);
        ChooserRequests.Add(request);
        return Task.FromResult(OnChooser(request));
    }

    public Task<bool> ConfirmDeleteWorktreeAsync(WorktreeDeletion plan)
    {
        DeleteConfirmations.Add(plan);
        return Task.FromResult(OnConfirmDelete(plan));
    }

    public Task<OpenDecision?> ShowDecisionAsync(RepositoryInfo repo, string branch, MainContext context)
    {
        var request = new DecisionRequest(repo, branch, context);
        DecisionRequests.Add(request);
        return Task.FromResult(OnDecision(request));
    }

    public Task ShowSettingsAsync(AppConfig config, ConfigService configService)
    {
        SettingsShownCount++;
        return Task.CompletedTask;
    }
}
