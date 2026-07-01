using Fido.Models;
using Fido.ViewModels;

namespace Fido.Services;

/// <summary>
/// The modal dialogs the main flow drives, abstracted from <see cref="Views.MainWindow"/> so the
/// end-to-end flow can be tested headlessly with a fake that returns scripted choices and records
/// what it was shown. The real dialog windows are exercised directly by the dialog widget tests.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Single-select chooser; returns the chosen index, or null if cancelled. When
    /// <paramref name="deleteLabel"/> is supplied the dialog also shows a destructive action button, and a
    /// result of <see cref="Views.ChooserDialog.DeleteRequested"/> means the user chose it.
    /// </summary>
    Task<int?> ShowChooserAsync(string title, string prompt, IReadOnlyList<ChooserItem> items, string? deleteLabel = null);

    /// <summary>Confirms the destructive delete of a located worktree; returns true only when confirmed.</summary>
    Task<bool> ConfirmDeleteWorktreeAsync(WorktreeDeletion plan);

    /// <summary>Branch-not-checked-out decision; returns the chosen action, or null if dismissed.</summary>
    Task<OpenDecision?> ShowDecisionAsync(RepositoryInfo repo, string branch, MainContext context);

    /// <summary>Opens the settings dialog (modal).</summary>
    Task ShowSettingsAsync(AppConfig config, ConfigService configService);
}
