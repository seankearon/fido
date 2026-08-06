using Fido.Models;

namespace Fido.Services;

/// <summary>
/// The modal dialogs the main flow still drives, abstracted from <see cref="Views.MainWindow"/> so the
/// end-to-end flow can be tested headlessly with a fake that returns scripted choices and records what
/// it was shown. Discovery, target choice, and worktree deletion render inline on the main screen now;
/// only settings and the exceptional force-delete recovery remain modal.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// After <c>git worktree remove</c> fails (typically a path too long for the OS), asks whether to
    /// permanently delete the folder straight from disk — a recursive delete that bypasses the Recycle Bin.
    /// Returns true to proceed, false to leave it in place.
    /// </summary>
    Task<bool> ConfirmForceDeleteWorktreeFolderAsync(WorktreeForceDelete request);

    /// <summary>Opens the settings dialog (modal).</summary>
    Task ShowSettingsAsync(AppConfig config, ConfigService configService);

    /// <summary>
    /// Asks where to save the flight log, offering <paramref name="suggestedFileName"/>. Returns the
    /// chosen path, or null when the user cancelled (or the platform has no save picker).
    /// </summary>
    Task<string?> PickFlightLogPathAsync(string suggestedFileName);
}
