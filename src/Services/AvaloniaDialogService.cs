using Avalonia.Controls;
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
}
