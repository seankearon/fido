using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Fido;
using Fido.Input;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;
using Fido.ViewModels;
using Fido.Views;

namespace Fido.Tests.Dialogs;

/// <summary>
/// The real Keyboard shortcuts dialog: recording through its key caps with real key presses — Enter and Esc
/// included, which belong to the recorder rather than Save and Cancel while it listens — and persisting (or
/// discarding) the result via ConfigService.
/// </summary>
[NotInParallel]
public class ShortcutsDialogTests
{
    private static (ConfigService Service, AppConfig Config, string Dir) NewConfig(TestRepoWorld world)
    {
        var dir = Path.Combine(world.Root, "config", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var service = new ConfigService(dir);
        var config = AppConfig.CreateDefault();
        service.Save(config);
        return (service, config, dir);
    }

    /// <summary>The key cap on the row for <paramref name="action"/>, as rendered.</summary>
    private static Button KeyCap(ShortcutsDialog dialog, ShortcutAction action) =>
        dialog.GetVisualDescendants().OfType<Button>()
            .First(b => b.Classes.Contains("keycap") && b.DataContext is ShortcutRow row && row.Definition.Action == action);

    private static void Click(Button button)
    {
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        UiTestExtensions.Pump();
    }

    [Test]
    public async Task Recording_a_chord_through_the_key_cap_and_saving_persists_it()
    {
        using var world = new TestRepoWorld();
        var (service, config, dir) = NewConfig(world);

        await Harness.OnUi(async owner =>
        {
            var dialog = new ShortcutsDialog(config, service);
            var result = dialog.ShowDialog(owner);
            UiTestExtensions.Pump();
            Screenshots.Save(dialog, "shortcuts-dialog");

            Click(KeyCap(dialog, ShortcutCommand.CopyPath));
            await Assert.That(dialog.ViewModel.IsRecording).IsTrue();

            dialog.PressKey(Key.K, KeyModifiers.Control);
            dialog.PressKey(Key.LeftCtrl, KeyModifiers.Control);
            dialog.PressKey(Key.L, KeyModifiers.Control);
            await Assert.That(KeyCap(dialog, ShortcutCommand.CopyPath).Content).IsEqualTo("Ctrl+K, Ctrl+L");

            dialog.ClickButton("SaveButton");
            await result;
        });

        var reloaded = new ConfigService(dir).Load();
        await Assert.That(reloaded.Shortcuts["CopyPath"]).IsEqualTo("Ctrl+K, Ctrl+L");
        await Assert.That(reloaded.Shortcuts.Count).IsEqualTo(1);   // only what differs from the defaults
    }

    [Test]
    public async Task Enter_and_Esc_belong_to_the_recorder_while_it_listens()
    {
        using var world = new TestRepoWorld();
        var (service, config, dir) = NewConfig(world);

        await Harness.OnUi(async owner =>
        {
            var dialog = new ShortcutsDialog(config, service);
            var result = dialog.ShowDialog(owner);
            UiTestExtensions.Pump();

            // Esc while recording cancels the recording — not the dialog.
            Click(KeyCap(dialog, ShortcutCommand.Rescan));
            dialog.PressKey(Key.Escape);
            await Assert.That(dialog.IsVisible).IsTrue();
            await Assert.That(dialog.ViewModel.IsRecording).IsFalse();

            // Enter after one press keeps it as a single-press shortcut — and doesn't press Save.
            Click(KeyCap(dialog, ShortcutAction.Tool(0)));
            dialog.PressKey(Key.R, KeyModifiers.Alt);
            dialog.PressKey(Key.Enter);
            await Assert.That(dialog.IsVisible).IsTrue();
            await Assert.That(KeyCap(dialog, ShortcutAction.Tool(0)).Content).IsEqualTo("Alt+R");

            // Not recording, Enter is Save again.
            dialog.PressKey(Key.Enter);
            await result;
        });

        var reloaded = new ConfigService(dir).Load();
        await Assert.That(reloaded.Editors[0].Shortcut).IsEqualTo("Alt+R");
        await Assert.That(reloaded.Shortcuts.ContainsKey("Rescan")).IsFalse();   // left at its default
    }

    [Test]
    public async Task Cancel_discards_every_change()
    {
        using var world = new TestRepoWorld();
        var (service, config, dir) = NewConfig(world);

        await Harness.OnUi(async owner =>
        {
            var dialog = new ShortcutsDialog(config, service);
            var result = dialog.ShowDialog(owner);
            UiTestExtensions.Pump();

            dialog.ViewModel.Clear(dialog.ViewModel.Rows[0]);
            dialog.ClickButton("ResetAllButton");
            dialog.ViewModel.Clear(dialog.ViewModel.Rows[1]);
            dialog.ClickButton("CancelButton");
            await result;
        });

        var reloaded = new ConfigService(dir).Load();
        await Assert.That(reloaded.Shortcuts.Count).IsEqualTo(0);
        await Assert.That(reloaded.Editors.All(e => e.Shortcut is null)).IsTrue();
        await Assert.That(config.Editors.All(e => e.Shortcut is null)).IsTrue();   // the live config too
    }

    [Test]
    public async Task Clicking_away_from_a_recording_key_cap_leaves_it_as_it_was()
    {
        using var world = new TestRepoWorld();
        var (service, config, _) = NewConfig(world);

        await Harness.OnUi(async owner =>
        {
            var dialog = new ShortcutsDialog(config, service);
            var result = dialog.ShowDialog(owner);
            UiTestExtensions.Pump();

            Click(KeyCap(dialog, ShortcutCommand.Settings));
            dialog.PressKey(Key.P, KeyModifiers.Control);
            dialog.FindControl<TextBlock>("NoticeText")!.RaiseEvent(new PointerPressedEventArgs(
                dialog, new Pointer(0, PointerType.Mouse, true), dialog, new Avalonia.Point(5, 5), 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None));
            UiTestExtensions.Pump();

            await Assert.That(dialog.ViewModel.IsRecording).IsFalse();
            await Assert.That(KeyCap(dialog, ShortcutCommand.Settings).Content).IsEqualTo("Ctrl+,");

            dialog.ClickButton("CancelButton");
            await result;
        });
    }
}
