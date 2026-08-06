using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Fido.Models;
using Fido.Views;

namespace Fido.Services;

/// <summary>Shows the real Avalonia dialog windows, owned by the main window.</summary>
public sealed class AvaloniaDialogService : IDialogService
{
    private readonly Window _owner;

    public AvaloniaDialogService(Window owner) => _owner = owner;

    public Task<bool> ConfirmForceDeleteWorktreeFolderAsync(WorktreeForceDelete request)
        => new ForceDeleteDialog(request).ShowDialog<bool>(_owner);

    public Task ShowSettingsAsync(AppConfig config, ConfigService configService)
        => new SettingsDialog(config, configService).ShowDialog(_owner);

    public async Task<string?> PickFlightLogPathAsync(string suggestedFileName)
    {
        var storage = _owner.StorageProvider;
        if (!storage.CanSave) return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save flight log",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "txt",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType("Text file")
                {
                    Patterns = ["*.txt"],
                    MimeTypes = ["text/plain"],
                },
            ],
        });

        // A picked file is written by path — Fido isn't sandboxed, and the log is plain text.
        return file?.TryGetLocalPath();
    }
}
