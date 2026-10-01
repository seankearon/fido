namespace Fido.Input;

/// <summary>What a key press came to, as far as the shortcuts are concerned.</summary>
public enum ChordOutcomeKind
{
    /// <summary>Not a shortcut, nor the start of one: the key belongs to whatever has focus.</summary>
    Unbound,

    /// <summary>The first press of a chord: Fido is waiting for the second.</summary>
    Waiting,

    /// <summary>A modifier going down while a chord waits — the start of the second press, not a press.</summary>
    StillWaiting,

    /// <summary>A shortcut, single or completed chord: run <see cref="ChordOutcome.Binding"/>.</summary>
    Matched,

    /// <summary>The second press of a chord that leads nowhere — <see cref="ChordOutcome.Keys"/> are both presses.</summary>
    Missed,

    /// <summary><c>Esc</c> after the first press of a chord: called off.</summary>
    Cancelled,
}

/// <summary>The result of one <see cref="ChordMatcher.Press"/>: what happened, the keys it was about, and for a
/// match what to run.</summary>
public readonly record struct ChordOutcome(ChordOutcomeKind Kind, Shortcut? Keys = null, ShortcutBinding? Binding = null);

/// <summary>
/// Turns key presses into shortcuts, two-press chords included: <c>Ctrl+K</c> on its own may do nothing yet
/// but wait, and <c>Ctrl+K</c> then <c>Ctrl+S</c> opens the Keyboard shortcuts dialog.
///
/// Just the state machine — it knows nothing of windows or focus, so the rules are tested on their own. The
/// main window decides which presses to feed it (see <c>MainWindow.OnShortcutKeyDown</c>): every press while
/// a chord is waiting, since by then the press is the chord's; otherwise only presses nothing else used. As
/// in VS Code, a waiting chord waits for as long as it takes — the window calls it off when it loses the
/// keyboard or the mouse goes down, and <c>Esc</c> calls it off from the keyboard.
/// </summary>
public sealed class ChordMatcher
{
    private ShortcutMap _map;

    public ChordMatcher(ShortcutMap map) => _map = map;

    /// <summary>The shortcuts in force. Replacing them calls off a chord in progress: its keys may mean
    /// something else now.</summary>
    public ShortcutMap Map
    {
        get => _map;
        set
        {
            _map = value;
            Pending = null;
        }
    }

    /// <summary>The first press of a chord still waiting for its second, or null.</summary>
    public KeyStroke? Pending { get; private set; }

    public bool IsPending => Pending is not null;

    /// <summary>Takes one key press, and says what it came to.</summary>
    public ChordOutcome Press(KeyStroke stroke)
    {
        if (Pending is { } first)
        {
            if (stroke.IsModifierKey) return new ChordOutcome(ChordOutcomeKind.StillWaiting, new Shortcut(first));

            Pending = null;
            if (stroke.IsEscape) return new ChordOutcome(ChordOutcomeKind.Cancelled, new Shortcut(first));

            var chord = new Shortcut(first, stroke);
            return _map.Find(chord) is { } binding
                ? new ChordOutcome(ChordOutcomeKind.Matched, chord, binding)
                : new ChordOutcome(ChordOutcomeKind.Missed, chord);
        }

        if (stroke.IsModifierKey) return new ChordOutcome(ChordOutcomeKind.Unbound);

        var single = new Shortcut(stroke);
        if (_map.Find(single) is { } direct) return new ChordOutcome(ChordOutcomeKind.Matched, single, direct);
        if (!_map.StartsChord(stroke)) return new ChordOutcome(ChordOutcomeKind.Unbound);

        Pending = stroke;
        return new ChordOutcome(ChordOutcomeKind.Waiting, single);
    }

    /// <summary>Calls off a chord in progress, if there is one. True when there was.</summary>
    public bool Reset()
    {
        var was = IsPending;
        Pending = null;
        return was;
    }
}
