using Avalonia.Input;
using Fido.Input;
using Fido.Models;
using Fido.ViewModels;

namespace Fido.Tests.ViewModels;

/// <summary>
/// The Keyboard shortcuts dialog's logic (no UI): recording single presses and chords, refusing keys that
/// can't be shortcuts, taking clashing keys from wherever they were, and writing only what differs from the
/// defaults.
/// </summary>
public class ShortcutsViewModelTests
{
    private static (ShortcutsViewModel Vm, AppConfig Config) Loaded()
    {
        var config = new AppConfig { Editors = Editor.Defaults() };
        var vm = new ShortcutsViewModel();
        vm.LoadFrom(config);
        return (vm, config);
    }

    private static ShortcutRow Row(ShortcutsViewModel vm, ShortcutAction action) =>
        vm.Rows.Single(r => r.Definition.Action == action);

    private static void Press(ShortcutsViewModel vm, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        vm.Press(KeyStroke.From(key, modifiers));

    [Test]
    public async Task Every_tool_and_command_has_a_row_tools_first_under_their_group_headings()
    {
        var (vm, config) = Loaded();

        await Assert.That(vm.Rows.Count).IsEqualTo(config.Editors.Count + ShortcutCatalog.Commands.Count);
        await Assert.That(vm.Rows[0].Name).IsEqualTo("Open in Rider");
        await Assert.That(vm.Rows[0].IsFirstInGroup).IsTrue();
        await Assert.That(vm.Rows[1].IsFirstInGroup).IsFalse();
        await Assert.That(vm.Rows.Count(r => r.IsFirstInGroup)).IsEqualTo(4);
        await Assert.That(vm.Rows[0].KeysText).IsEqualTo("Ctrl+1");
    }

    [Test]
    public async Task Two_presses_record_a_chord_and_assign_it_at_once()
    {
        var (vm, _) = Loaded();
        var copy = Row(vm, ShortcutCommand.CopyPath);

        vm.BeginRecording(copy);
        await Assert.That(copy.KeysText).IsEqualTo("press keys…");
        Press(vm, Key.K, KeyModifiers.Control);
        await Assert.That(copy.KeysText).IsEqualTo("Ctrl+K, …");
        Press(vm, Key.LeftCtrl, KeyModifiers.Control);   // the run-up to the second press changes nothing
        Press(vm, Key.L, KeyModifiers.Control);

        await Assert.That(vm.IsRecording).IsFalse();
        await Assert.That(copy.Keys.ToString()).IsEqualTo("Ctrl+K, Ctrl+L");
        await Assert.That(copy.KeysText).IsEqualTo("Ctrl+K, Ctrl+L");
        await Assert.That(copy.IsCustom).IsTrue();
    }

    [Test]
    public async Task Enter_after_the_first_press_keeps_the_single_press()
    {
        var (vm, _) = Loaded();
        var copy = Row(vm, ShortcutCommand.CopyPath);

        vm.BeginRecording(copy);
        Press(vm, Key.P, KeyModifiers.Control | KeyModifiers.Shift);
        Press(vm, Key.Enter);

        await Assert.That(copy.Keys.ToString()).IsEqualTo("Ctrl+Shift+P");
    }

    [Test]
    public async Task Esc_leaves_the_row_as_it_was_at_either_press()
    {
        var (vm, _) = Loaded();
        var rescan = Row(vm, ShortcutCommand.Rescan);

        vm.BeginRecording(rescan);
        Press(vm, Key.Escape);
        await Assert.That(rescan.Keys.ToString()).IsEqualTo("F5");
        await Assert.That(vm.IsRecording).IsFalse();

        vm.BeginRecording(rescan);
        Press(vm, Key.R, KeyModifiers.Control);
        Press(vm, Key.Escape);
        await Assert.That(rescan.Keys.ToString()).IsEqualTo("F5");
        await Assert.That(rescan.KeysText).IsEqualTo("F5");
    }

    [Test]
    public async Task A_first_press_that_would_type_is_refused_and_recording_carries_on()
    {
        var (vm, _) = Loaded();
        var copy = Row(vm, ShortcutCommand.CopyPath);

        vm.BeginRecording(copy);
        Press(vm, Key.C);

        await Assert.That(vm.IsRecording).IsTrue();
        await Assert.That(copy.Recorded).IsNull();
        await Assert.That(vm.Notice).Contains("can't start with plain C");

        Press(vm, Key.Space, KeyModifiers.Alt);   // the system menu's
        await Assert.That(vm.Notice).Contains("belongs to the system");
        await Assert.That(copy.Keys).IsNull();
    }

    [Test]
    public async Task Keys_that_clash_are_taken_from_whatever_had_them_and_the_notice_says_so()
    {
        var (vm, _) = Loaded();
        var copy = Row(vm, ShortcutCommand.CopyPath);
        var vsCode = Row(vm, ShortcutAction.Tool(2));

        vm.BeginRecording(copy);
        Press(vm, Key.D3, KeyModifiers.Control);
        Press(vm, Key.Enter);

        await Assert.That(copy.Keys.ToString()).IsEqualTo("Ctrl+3");
        await Assert.That(vsCode.Keys).IsNull();
        await Assert.That(vm.Notice).Contains("Taken from Open in VS Code");
    }

    [Test]
    public async Task A_single_press_takes_every_chord_that_starts_with_it()
    {
        var (vm, _) = Loaded();
        var copy = Row(vm, ShortcutCommand.CopyPath);

        vm.BeginRecording(copy);
        Press(vm, Key.K, KeyModifiers.Control);
        Press(vm, Key.Enter);

        await Assert.That(Row(vm, ShortcutCommand.KeyboardShortcuts).Keys).IsNull();
        await Assert.That(Row(vm, ShortcutCommand.ToggleTheme).Keys).IsNull();
        await Assert.That(vm.Notice).Contains("Flip light / dark and Keyboard shortcuts…");
        await Assert.That(vm.Notice).Contains("now have no shortcut");
    }

    [Test]
    public async Task A_chord_sharing_only_a_first_press_takes_nothing()
    {
        var (vm, _) = Loaded();
        var copy = Row(vm, ShortcutCommand.CopyPath);

        vm.BeginRecording(copy);
        Press(vm, Key.K, KeyModifiers.Control);
        Press(vm, Key.C, KeyModifiers.Control);

        await Assert.That(Row(vm, ShortcutCommand.KeyboardShortcuts).Keys.ToString()).IsEqualTo("Ctrl+K, Ctrl+S");
        await Assert.That(vm.Notice).DoesNotContain("Taken");
    }

    [Test]
    public async Task Clear_reset_and_reset_all()
    {
        var (vm, _) = Loaded();
        var settings = Row(vm, ShortcutCommand.Settings);
        var copy = Row(vm, ShortcutCommand.CopyPath);

        vm.Clear(settings);
        await Assert.That(settings.Keys).IsNull();
        await Assert.That(settings.KeysText).IsEqualTo("—");

        // Give Settings' default away, then reset Settings: it takes its keys back.
        vm.BeginRecording(copy);
        Press(vm, Key.OemComma, KeyModifiers.Control);
        Press(vm, Key.Enter);
        vm.Reset(settings);
        await Assert.That(settings.Keys.ToString()).IsEqualTo("Ctrl+,");
        await Assert.That(copy.Keys).IsNull();

        vm.Clear(Row(vm, ShortcutAction.Tool(0)));
        vm.ResetAll();
        await Assert.That(vm.Rows.All(r => !r.IsCustom)).IsTrue();
    }

    [Test]
    public async Task ApplyTo_writes_only_what_differs_from_the_defaults()
    {
        var (vm, config) = Loaded();

        vm.BeginRecording(Row(vm, ShortcutCommand.CopyPath));
        Press(vm, Key.K, KeyModifiers.Control);
        Press(vm, Key.P);
        vm.Clear(Row(vm, ShortcutCommand.Rescan));
        vm.BeginRecording(Row(vm, ShortcutAction.Tool(4)));   // Zed
        Press(vm, Key.Z, KeyModifiers.Alt);
        Press(vm, Key.Enter);
        vm.ApplyTo(config);

        await Assert.That(config.Shortcuts.Count).IsEqualTo(2);
        await Assert.That(config.Shortcuts["CopyPath"]).IsEqualTo("Ctrl+K, P");
        await Assert.That(config.Shortcuts["Rescan"]).IsEqualTo("");
        await Assert.That(config.Editors[4].Shortcut).IsEqualTo("Alt+Z");
        await Assert.That(config.Editors.Where((_, i) => i != 4).All(e => e.Shortcut is null)).IsTrue();
    }

    [Test]
    public async Task Loading_shows_the_keys_that_work_so_saving_tidies_a_clash_left_in_the_file()
    {
        var config = new AppConfig { Editors = Editor.Defaults() };
        config.Shortcuts["CopyPath"] = "Ctrl+2";   // WebStorm's number, by default
        var vm = new ShortcutsViewModel();

        vm.LoadFrom(config);
        vm.ApplyTo(config);

        await Assert.That(Row(vm, ShortcutAction.Tool(1)).Keys).IsNull();
        await Assert.That(config.Editors[1].Shortcut).IsEqualTo("");
    }
}
