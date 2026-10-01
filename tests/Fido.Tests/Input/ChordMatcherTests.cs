using Avalonia.Input;
using Fido.Input;
using Fido.Models;

namespace Fido.Tests.Input;

/// <summary>The <see cref="ChordMatcher"/> state machine: single presses, two-press chords, misses and Esc.</summary>
public class ChordMatcherTests
{
    private static ChordMatcher WithDefaults() =>
        new(ShortcutMap.FromConfig(new AppConfig { Editors = Editor.Defaults() }));

    private static readonly KeyStroke CtrlK = KeyStroke.Ctrl(Key.K);

    [Test]
    public async Task A_single_press_shortcut_matches_at_once()
    {
        var matcher = WithDefaults();

        var outcome = matcher.Press(KeyStroke.Ctrl(Key.D3));

        await Assert.That(outcome.Kind).IsEqualTo(ChordOutcomeKind.Matched);
        await Assert.That(outcome.Binding!.Action).IsEqualTo(ShortcutAction.Tool(2));
        await Assert.That(matcher.IsPending).IsFalse();
    }

    [Test]
    public async Task The_first_press_of_a_chord_waits_and_the_second_completes_it()
    {
        var matcher = WithDefaults();

        var first = matcher.Press(CtrlK);
        await Assert.That(first.Kind).IsEqualTo(ChordOutcomeKind.Waiting);
        await Assert.That(matcher.Pending).IsEqualTo(CtrlK);

        var second = matcher.Press(KeyStroke.Ctrl(Key.S));
        await Assert.That(second.Kind).IsEqualTo(ChordOutcomeKind.Matched);
        await Assert.That(second.Binding!.Action).IsEqualTo((ShortcutAction)ShortcutCommand.KeyboardShortcuts);
        await Assert.That(matcher.IsPending).IsFalse();
    }

    [Test]
    public async Task Modifiers_going_down_between_the_presses_keep_the_chord_waiting()
    {
        var matcher = WithDefaults();
        matcher.Press(CtrlK);

        var ctrl = matcher.Press(KeyStroke.From(Key.LeftCtrl, KeyModifiers.Control));

        await Assert.That(ctrl.Kind).IsEqualTo(ChordOutcomeKind.StillWaiting);
        await Assert.That(matcher.Press(KeyStroke.Ctrl(Key.T)).Kind).IsEqualTo(ChordOutcomeKind.Matched);
    }

    [Test]
    public async Task A_second_press_that_leads_nowhere_is_a_miss_naming_both_presses()
    {
        var matcher = WithDefaults();
        matcher.Press(CtrlK);

        var outcome = matcher.Press(KeyStroke.From(Key.X));

        await Assert.That(outcome.Kind).IsEqualTo(ChordOutcomeKind.Missed);
        await Assert.That(outcome.Keys!.Value.ToString()).IsEqualTo("Ctrl+K, X");
        await Assert.That(matcher.IsPending).IsFalse();   // and the next press starts afresh
        await Assert.That(matcher.Press(KeyStroke.Ctrl(Key.D1)).Kind).IsEqualTo(ChordOutcomeKind.Matched);
    }

    [Test]
    public async Task A_second_press_that_is_a_shortcut_of_its_own_is_still_the_chords()
    {
        // F5 rescans on its own, but after Ctrl+K it is the second half of a chord that doesn't exist.
        var matcher = WithDefaults();
        matcher.Press(CtrlK);

        await Assert.That(matcher.Press(KeyStroke.From(Key.F5)).Kind).IsEqualTo(ChordOutcomeKind.Missed);
    }

    [Test]
    public async Task Esc_calls_a_waiting_chord_off()
    {
        var matcher = WithDefaults();
        matcher.Press(CtrlK);

        var outcome = matcher.Press(KeyStroke.From(Key.Escape));

        await Assert.That(outcome.Kind).IsEqualTo(ChordOutcomeKind.Cancelled);
        await Assert.That(matcher.IsPending).IsFalse();
    }

    [Test]
    public async Task A_press_that_is_nothing_is_left_alone()
    {
        var matcher = WithDefaults();

        await Assert.That(matcher.Press(KeyStroke.From(Key.A)).Kind).IsEqualTo(ChordOutcomeKind.Unbound);
        await Assert.That(matcher.Press(KeyStroke.Ctrl(Key.C)).Kind).IsEqualTo(ChordOutcomeKind.Unbound);
        await Assert.That(matcher.Press(KeyStroke.From(Key.LeftCtrl, KeyModifiers.Control)).Kind)
            .IsEqualTo(ChordOutcomeKind.Unbound);
        await Assert.That(matcher.IsPending).IsFalse();
    }

    [Test]
    public async Task Replacing_the_shortcuts_calls_off_a_chord_half_pressed_under_the_old_ones()
    {
        var matcher = WithDefaults();
        matcher.Press(CtrlK);

        matcher.Map = ShortcutMap.Empty;

        await Assert.That(matcher.IsPending).IsFalse();
        await Assert.That(matcher.Press(KeyStroke.Ctrl(Key.S)).Kind).IsEqualTo(ChordOutcomeKind.Unbound);
    }

    [Test]
    public async Task Reset_says_whether_there_was_anything_to_call_off()
    {
        var matcher = WithDefaults();

        await Assert.That(matcher.Reset()).IsFalse();
        matcher.Press(CtrlK);
        await Assert.That(matcher.Reset()).IsTrue();
    }
}
