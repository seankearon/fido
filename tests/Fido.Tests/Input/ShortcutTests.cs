using Avalonia.Input;
using Fido.Input;

namespace Fido.Tests.Input;

/// <summary>
/// <see cref="KeyStroke"/> and <see cref="Shortcut"/>: the text form the config stores and people type, the
/// normalisation that makes two presses of the same keys equal, and the rules for what can be a shortcut.
/// </summary>
public class ShortcutTests
{
    [Test]
    [Arguments("Ctrl+K", "Ctrl+K")]
    [Arguments("ctrl+k", "Ctrl+K")]                         // any case
    [Arguments("Control+Shift+P", "Ctrl+Shift+P")]
    [Arguments("Shift+Ctrl+P", "Ctrl+Shift+P")]             // modifiers come back in one order
    [Arguments("Alt+Meta+X", "Alt+Meta+X")]
    [Arguments("Cmd+X", "Meta+X")]                          // Cmd, Win, Super: all the Meta key
    [Arguments("Win+X", "Meta+X")]
    [Arguments("F5", "F5")]
    [Arguments("Ctrl+,", "Ctrl+,")]                         // keys by the character on them
    [Arguments("Ctrl+Comma", "Ctrl+,")]                     // …or by name
    [Arguments("Ctrl+[", "Ctrl+[")]
    [Arguments("Ctrl+=", "Ctrl+=")]
    [Arguments("Ctrl++", "Ctrl+=")]                         // the plus is the = key
    [Arguments("Ctrl+Return", "Ctrl+Enter")]
    [Arguments("Ctrl+PgUp", "Ctrl+PageUp")]
    [Arguments("Alt+Home", "Alt+Home")]
    public async Task A_stroke_reads_and_writes_back_in_the_canonical_form(string text, string canonical)
    {
        await Assert.That(KeyStroke.TryParse(text, out var stroke)).IsTrue();
        await Assert.That(stroke.ToString()).IsEqualTo(canonical);
    }

    [Test]
    [Arguments("")]
    [Arguments("Ctrl+")]
    [Arguments("Ctrl")]                 // a modifier on its own is not a key press
    [Arguments("Hyper+K")]
    [Arguments("Ctrl+NotAKey")]
    [Arguments("Ctrl+42")]              // Enum.TryParse would take a number; a shortcut mustn't
    public async Task Nonsense_is_not_a_stroke(string text)
    {
        await Assert.That(KeyStroke.TryParse(text, out _)).IsFalse();
    }

    [Test]
    public async Task The_numpad_digits_are_the_top_row_digits()
    {
        await Assert.That(KeyStroke.From(Key.NumPad3, KeyModifiers.Control))
            .IsEqualTo(KeyStroke.From(Key.D3, KeyModifiers.Control));
    }

    [Test]
    public async Task Lock_and_button_flags_are_not_part_of_the_stroke()
    {
        // KeyModifiers only carries the four, but a stroke built from a wider value must still equal the plain one.
        var noisy = KeyStroke.From(Key.K, KeyModifiers.Control | (KeyModifiers)0x100);

        await Assert.That(noisy).IsEqualTo(KeyStroke.Ctrl(Key.K));
    }

    [Test]
    [Arguments("Ctrl+K", true)]
    [Arguments("Alt+K", true)]
    [Arguments("Meta+K", true)]
    [Arguments("F5", true)]             // types nothing, so it can stand alone
    [Arguments("Shift+F5", true)]
    [Arguments("K", false)]             // typing
    [Arguments("Shift+K", false)]       // typing, upper case
    [Arguments("Enter", false)]
    [Arguments("Alt+Space", false)]     // the system menu's
    [Arguments("Alt+F4", false)]        // closing the window
    public async Task Only_a_stroke_that_cannot_be_typing_can_begin_a_shortcut(string text, bool canBegin)
    {
        KeyStroke.TryParse(text, out var stroke);

        await Assert.That(stroke.CanBegin).IsEqualTo(canBegin);
    }

    [Test]
    [Arguments("Ctrl+K, Ctrl+L")]
    [Arguments("ctrl+k ctrl+l")]          // as VS Code writes it
    [Arguments("Ctrl+K,Ctrl+L")]
    [Arguments("Ctrl + K, Ctrl + L")]     // as people write it
    [Arguments("  Ctrl+K ,  Ctrl+L  ")]
    public async Task A_chord_reads_in_every_usual_notation(string text)
    {
        await Assert.That(Shortcut.TryParse(text, out var chord)).IsTrue();
        await Assert.That(chord).IsEqualTo(new Shortcut(KeyStroke.Ctrl(Key.K), KeyStroke.Ctrl(Key.L)));
        await Assert.That(chord.ToString()).IsEqualTo("Ctrl+K, Ctrl+L");
    }

    [Test]
    [Arguments("Ctrl+K, T")]
    [Arguments("Ctrl+,, Ctrl+L")]         // the comma key, then the gap
    [Arguments("Ctrl+K, Comma")]          // a bare comma second press is written by name…
    [Arguments("Ctrl+K, Enter")]
    [Arguments("F5")]
    [Arguments("Ctrl+Shift+Alt+Meta+F12")]
    public async Task Every_shortcut_round_trips_through_its_text(string text)
    {
        await Assert.That(Shortcut.TryParse(text, out var first)).IsTrue();
        await Assert.That(Shortcut.TryParse(first.ToString(), out var again)).IsTrue();
        await Assert.That(again).IsEqualTo(first);
    }

    [Test]
    public async Task A_bare_comma_second_press_is_written_by_name_so_it_reads_back()
    {
        var chord = new Shortcut(KeyStroke.Ctrl(Key.K), KeyStroke.From(Key.OemComma));

        await Assert.That(chord.ToString()).IsEqualTo("Ctrl+K, Comma");
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("Ctrl+K, Ctrl+L, Ctrl+M")]   // two presses is the limit
    [Arguments("Ctrl+K, Nope")]
    public async Task Blank_unreadable_or_too_long_is_not_a_shortcut(string text)
    {
        await Assert.That(Shortcut.TryParse(text, out _)).IsFalse();
    }

    [Test]
    [Arguments("Ctrl+K, T", true)]
    [Arguments("Ctrl+K, Ctrl+K", true)]
    [Arguments("F5, F6", true)]
    [Arguments("K", false)]               // would type
    [Arguments("Ctrl+K, Esc", false)]     // Esc calls the chord off; it can't finish one
    public async Task A_shortcut_is_valid_when_it_can_actually_be_pressed(string text, bool valid)
    {
        Shortcut.TryParse(text, out var shortcut);

        await Assert.That(shortcut.IsValid).IsEqualTo(valid);
    }

    [Test]
    [Arguments("Ctrl+K", "Ctrl+K", true)]                  // the same keys
    [Arguments("Ctrl+K", "Ctrl+K, Ctrl+S", true)]          // one is where the other starts
    [Arguments("Ctrl+K, Ctrl+S", "Ctrl+K", true)]
    [Arguments("Ctrl+K, Ctrl+S", "Ctrl+K, Ctrl+T", false)] // two chords sharing a first press are fine
    [Arguments("Ctrl+K, Ctrl+S", "Ctrl+S", false)]         // a second press is free to be a shortcut of its own
    [Arguments("Ctrl+1", "Ctrl+2", false)]
    public async Task Shortcuts_collide_when_both_could_not_be_pressed(string a, string b, bool collide)
    {
        Shortcut.TryParse(a, out var first);
        Shortcut.TryParse(b, out var second);

        await Assert.That(first.CollidesWith(second)).IsEqualTo(collide);
    }
}
