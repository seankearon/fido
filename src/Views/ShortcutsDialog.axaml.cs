using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Fido.Input;
using Fido.Models;
using Fido.Services;
using Fido.ViewModels;

namespace Fido.Views;

/// <summary>
/// The Keyboard shortcuts dialog: the main screen's actions, each with its keys and a recorder to change them
/// (see <see cref="ShortcutsViewModel"/> for how recording and clashes work). Save writes the config; Cancel,
/// or closing the window, leaves it as it was.
/// </summary>
public partial class ShortcutsDialog : Window
{
    private readonly ShortcutsViewModel _vm = new();
    private readonly AppConfig _config = null!;
    private readonly ConfigService _configService = null!;

    public ShortcutsDialog()
    {
        InitializeComponent();
        SystemMenu.EnableAltSpace(this);   // Alt+Space → native system menu

        // While a row is recording, every key is the recorder's — Enter and Esc included, which would
        // otherwise press Save or Cancel. Tunnel, so it hears them before any button does.
        AddHandler(KeyDownEvent, OnRecordingKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        // A click anywhere but a key cap leaves the recording row as it was.
        AddHandler(PointerPressedEvent, OnPointerPressedAnywhere, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public ShortcutsDialog(AppConfig config, ConfigService configService) : this()
    {
        _config = config;
        _configService = configService;
        _vm.LoadFrom(config);
        DataContext = _vm;
    }

    /// <summary>The dialog's state, for tests that drive the recorder through the real window.</summary>
    internal ShortcutsViewModel ViewModel => _vm;

    private void OnRecordingKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_vm.IsRecording) return;
        e.Handled = true;
        _vm.Press(KeyStroke.From(e.Key, e.KeyModifiers));
    }

    private void OnPointerPressedAnywhere(object? sender, PointerPressedEventArgs e)
    {
        if (!_vm.IsRecording) return;
        // A key cap's own click starts (or restarts) recording on its row, which ends any other.
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is { } button
            && button.Classes.Contains("keycap"))
            return;
        _vm.CancelRecording();
    }

    private void OnRecordClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ShortcutRow row })
            _vm.BeginRecording(row);
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ShortcutRow row })
            _vm.Reset(row);
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ShortcutRow row })
            _vm.Clear(row);
    }

    private void OnResetAllClick(object? sender, RoutedEventArgs e) => _vm.ResetAll();

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        _vm.CancelRecording();
        _vm.ApplyTo(_config);
        try
        {
            _configService.Save(_config);
        }
        catch
        {
            // best-effort, as Settings is: the shortcuts still apply to this run
        }
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
