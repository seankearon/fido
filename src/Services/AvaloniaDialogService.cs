using Avalonia.Controls;
using Fido.Models;
using Fido.ViewModels;
using Fido.Views;

namespace Fido.Services;

/// <summary>Shows the real Avalonia dialog windows, owned by the main window.</summary>
public sealed class AvaloniaDialogService : IDialogService
{
    private readonly Window _owner;

    public AvaloniaDialogService(Window owner) => _owner = owner;

    public Task<int?> ShowChooserAsync(string title, string prompt, IReadOnlyList<ChooserItem> items, string? deleteLabel = null)
        => new ChooserDialog(title, prompt, items, deleteLabel).ShowDialog<int?>(_owner);

    public Task<WorktreeDeletionChoice?> ConfirmDeleteWorktreeAsync(WorktreeDeletion plan)
        => new DeleteWorktreeDialog(plan).ShowDialog<WorktreeDeletionChoice?>(_owner);

    public Task<bool> ConfirmForceDeleteWorktreeFolderAsync(WorktreeForceDelete request)
        => new ForceDeleteDialog(request).ShowDialog<bool>(_owner);

    public Task<OpenDecision?> ShowDecisionAsync(RepositoryInfo repo, string branch, MainContext context)
        => new DecisionDialog(repo, branch, context).ShowDialog<OpenDecision?>(_owner);

    public Task ShowSettingsAsync(AppConfig config, ConfigService configService)
        => new SettingsDialog(config, configService).ShowDialog(_owner);
}
