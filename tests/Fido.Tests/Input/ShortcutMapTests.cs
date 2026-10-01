using Avalonia.Input;
using Fido.Input;
using Fido.Models;

namespace Fido.Tests.Input;

/// <summary>
/// <see cref="ShortcutMap"/> built from a config: the defaults every Fido starts with, the two places a
/// binding is stored, and how a clash a hand edit left in the file is settled.
/// </summary>
public class ShortcutMapTests
{
    private static AppConfig WithDefaults() => new() { Editors = Editor.Defaults() };

    private static Shortcut Keys(string text)
    {
        if (!Shortcut.TryParse(text, out var keys)) throw new ArgumentException($"Not a shortcut: {text}");
        return keys;
    }

    [Test]
    public async Task The_tools_are_numbered_by_default()
    {
        var map = ShortcutMap.FromConfig(WithDefaults());

        for (var i = 0; i < Editor.Defaults().Count; i++)
            await Assert.That(map.For(ShortcutAction.Tool(i))).IsEqualTo(Keys($"Ctrl+{i + 1}"));
    }

    [Test]
    public async Task Only_the_first_nine_tools_get_a_number()
    {
        var config = new AppConfig { Editors = Enumerable.Range(0, 11).Select(i => new Editor { Name = $"T{i}" }).ToList() };

        var map = ShortcutMap.FromConfig(config);

        await Assert.That(map.For(ShortcutAction.Tool(8))).IsEqualTo(Keys("Ctrl+9"));
        await Assert.That(map.For(ShortcutAction.Tool(9))).IsNull();
        await Assert.That(map.For(ShortcutAction.Tool(10))).IsNull();
    }

    [Test]
    public async Task The_commands_start_with_their_defaults()
    {
        var map = ShortcutMap.FromConfig(WithDefaults());

        await Assert.That(map.For(ShortcutCommand.Rescan)).IsEqualTo(Keys("F5"));
        await Assert.That(map.For(ShortcutCommand.Settings)).IsEqualTo(Keys("Ctrl+,"));
        await Assert.That(map.For(ShortcutCommand.KeyboardShortcuts)).IsEqualTo(Keys("Ctrl+K, Ctrl+S"));
        await Assert.That(map.For(ShortcutCommand.ToggleTheme)).IsEqualTo(Keys("Ctrl+K, Ctrl+T"));
        await Assert.That(map.For(ShortcutCommand.CopyPath)).IsNull();
    }

    [Test]
    public async Task The_defaults_never_clash_with_one_another()
    {
        // Every default makes it into the map — none is dropped for colliding with another.
        var config = WithDefaults();
        var definitions = ShortcutCatalog.For(config.Editors).Where(d => d.Default is not null).ToList();

        var map = ShortcutMap.FromConfig(config);

        await Assert.That(map.Bindings.Count).IsEqualTo(definitions.Count);
    }

    [Test]
    public async Task A_command_takes_the_keys_the_config_gives_it()
    {
        var config = WithDefaults();
        config.Shortcuts["CopyPath"] = "Ctrl+K, Ctrl+C";
        config.Shortcuts["rescan"] = "Ctrl+R";   // keys are matched without regard to case

        var map = ShortcutMap.FromConfig(config);

        await Assert.That(map.For(ShortcutCommand.CopyPath)).IsEqualTo(Keys("Ctrl+K, Ctrl+C"));
        await Assert.That(map.For(ShortcutCommand.Rescan)).IsEqualTo(Keys("Ctrl+R"));
        await Assert.That(map.Find(Keys("F5"))).IsNull();   // the default went with the change
    }

    [Test]
    public async Task An_empty_value_means_no_shortcut_rather_than_the_default()
    {
        var config = WithDefaults();
        config.Shortcuts["Rescan"] = "";
        config.Editors[1].Shortcut = "";

        var map = ShortcutMap.FromConfig(config);

        await Assert.That(map.For(ShortcutCommand.Rescan)).IsNull();
        await Assert.That(map.For(ShortcutAction.Tool(1))).IsNull();
        await Assert.That(map.For(ShortcutAction.Tool(2))).IsEqualTo(Keys("Ctrl+3"));   // the rest keep their numbers
    }

    [Test]
    public async Task A_tool_shortcut_lives_on_the_tool_and_goes_where_it_goes()
    {
        var config = WithDefaults();
        config.Editors[2].Shortcut = "Ctrl+K, V";   // VS Code
        config.Editors.RemoveAt(0);                 // Rider goes; VS Code moves up to index 1

        var map = ShortcutMap.FromConfig(config);

        await Assert.That(map.For(ShortcutAction.Tool(1))).IsEqualTo(Keys("Ctrl+K, V"));
        await Assert.That(map.For(ShortcutAction.Tool(0))).IsEqualTo(Keys("Ctrl+1"));   // unset: follows its place
    }

    [Test]
    public async Task A_value_that_will_not_parse_counts_as_none()
    {
        var config = WithDefaults();
        config.Shortcuts["Rescan"] = "Ctrl+Nonsense";
        config.Shortcuts["Settings"] = "K";   // parses, but would be typing

        var map = ShortcutMap.FromConfig(config);

        await Assert.That(map.For(ShortcutCommand.Rescan)).IsNull();
        await Assert.That(map.For(ShortcutCommand.Settings)).IsNull();
    }

    [Test]
    public async Task A_chosen_binding_beats_a_default_for_the_same_keys()
    {
        var config = WithDefaults();
        config.Shortcuts["ToggleTheme"] = "Ctrl+3";   // VS Code's number, by default

        var map = ShortcutMap.FromConfig(config);

        await Assert.That(map.Find(Keys("Ctrl+3"))!.Action).IsEqualTo((ShortcutAction)ShortcutCommand.ToggleTheme);
        await Assert.That(map.For(ShortcutAction.Tool(2))).IsNull();   // lost its keys, so shows none
    }

    [Test]
    public async Task A_single_press_and_a_chord_starting_with_it_cannot_both_be_bound()
    {
        var config = WithDefaults();
        config.Shortcuts["CopyPath"] = "Ctrl+K";   // where the two default chords start

        var map = ShortcutMap.FromConfig(config);

        await Assert.That(map.For(ShortcutCommand.CopyPath)).IsEqualTo(Keys("Ctrl+K"));
        await Assert.That(map.For(ShortcutCommand.KeyboardShortcuts)).IsNull();
        await Assert.That(map.For(ShortcutCommand.ToggleTheme)).IsNull();
        await Assert.That(map.StartsChord(KeyStroke.Ctrl(Key.K))).IsFalse();
    }

    [Test]
    public async Task Between_two_chosen_bindings_the_dialog_order_decides()
    {
        var config = WithDefaults();
        config.Editors[0].Shortcut = "Ctrl+R";          // tools come first in the dialog…
        config.Shortcuts["Rescan"] = "Ctrl+R";          // …so Rider keeps it

        var map = ShortcutMap.FromConfig(config);

        await Assert.That(map.Find(Keys("Ctrl+R"))!.Action).IsEqualTo(ShortcutAction.Tool(0));
        await Assert.That(map.For(ShortcutCommand.Rescan)).IsNull();
    }

    [Test]
    public async Task Writing_the_default_stores_nothing_so_a_tool_keeps_following_its_place()
    {
        var config = WithDefaults();
        config.Editors[1].Shortcut = "Ctrl+W";
        config.Shortcuts["Rescan"] = "Ctrl+R";
        var definitions = ShortcutCatalog.For(config.Editors);

        ShortcutCatalog.Write(config, definitions.First(d => d.Action == ShortcutAction.Tool(1)), Keys("Ctrl+2"));
        ShortcutCatalog.Write(config, definitions.First(d => d.Action == ShortcutCommand.Rescan), Keys("F5"));
        ShortcutCatalog.Write(config, definitions.First(d => d.Action == ShortcutCommand.CopyPath), null);   // default is none

        await Assert.That(config.Editors[1].Shortcut).IsNull();
        await Assert.That(config.Shortcuts.ContainsKey("Rescan")).IsFalse();
        await Assert.That(config.Shortcuts.ContainsKey("CopyPath")).IsFalse();
    }

    [Test]
    public async Task Writing_none_over_a_default_stores_an_empty_value()
    {
        var config = WithDefaults();
        var definitions = ShortcutCatalog.For(config.Editors);

        ShortcutCatalog.Write(config, definitions.First(d => d.Action == ShortcutCommand.Settings), null);
        ShortcutCatalog.Write(config, definitions.First(d => d.Action == ShortcutAction.Tool(0)), null);

        await Assert.That(config.Shortcuts["Settings"]).IsEqualTo("");
        await Assert.That(config.Editors[0].Shortcut).IsEqualTo("");
    }

    [Test]
    public async Task Writing_replaces_a_key_saved_in_another_case()
    {
        var config = WithDefaults();
        config.Shortcuts["rescan"] = "Ctrl+R";

        ShortcutCatalog.Write(config, ShortcutCatalog.Commands.First(d => d.Action == ShortcutCommand.Rescan), Keys("Ctrl+E"));

        await Assert.That(config.Shortcuts.Count).IsEqualTo(1);
        await Assert.That(config.Shortcuts["Rescan"]).IsEqualTo("Ctrl+E");
    }
}
