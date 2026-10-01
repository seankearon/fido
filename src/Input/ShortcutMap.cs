using System.Collections.Generic;
using System.Linq;
using Fido.Models;

namespace Fido.Input;

/// <summary>A shortcut that is live: these keys do this.</summary>
public sealed record ShortcutBinding(ShortcutDefinition Definition, Shortcut Keys)
{
    public ShortcutAction Action => Definition.Action;
}

/// <summary>
/// The shortcuts in force — which keys do what — built from the config, and the answer to "what do these keys
/// do?" for <see cref="ChordMatcher"/> and "what are this action's keys?" for every label that shows one.
///
/// It never holds two bindings that get in each other's way: not the same keys twice, and not a single press
/// that is also where a chord starts (with <c>Ctrl+K</c> bound, <c>Ctrl+K, Ctrl+S</c> could never be
/// reached). The Keyboard shortcuts dialog doesn't let that happen, but the config is a file people can edit,
/// so <see cref="FromConfig"/> settles a clash the same way every time: a binding someone chose beats a
/// default, then the order the dialog lists them in decides. Whatever loses is simply not bound — and so
/// shows no keys anywhere, because it has none that work.
/// </summary>
public sealed class ShortcutMap
{
    private readonly Dictionary<ShortcutAction, ShortcutBinding> _byAction = new();
    private readonly Dictionary<Shortcut, ShortcutBinding> _byKeys = new();
    private readonly HashSet<KeyStroke> _chordStarts = new();

    private ShortcutMap(IEnumerable<ShortcutBinding> bindings)
    {
        foreach (var binding in bindings)
        {
            if (_byAction.ContainsKey(binding.Action)) continue;
            if (!binding.Keys.IsValid) continue;
            if (_byAction.Values.Any(taken => taken.Keys.CollidesWith(binding.Keys))) continue;

            _byAction[binding.Action] = binding;
            _byKeys[binding.Keys] = binding;
            if (binding.Keys.IsChord) _chordStarts.Add(binding.Keys.First);
        }
        Bindings = _byAction.Values.ToList();
    }

    /// <summary>No shortcuts at all.</summary>
    public static ShortcutMap Empty { get; } = new([]);

    /// <summary>The live bindings, in the order they won their keys.</summary>
    public IReadOnlyList<ShortcutBinding> Bindings { get; }

    /// <summary>
    /// The shortcuts <paramref name="config"/> asks for, with any clash settled (see the type's remarks).
    /// </summary>
    public static ShortcutMap FromConfig(AppConfig config)
    {
        var chosen = new List<ShortcutBinding>();
        var defaults = new List<ShortcutBinding>();
        foreach (var definition in ShortcutCatalog.For(config.Editors))
        {
            var (keys, isSet) = ShortcutCatalog.Read(config, definition);
            if (keys is { } bound) (isSet ? chosen : defaults).Add(new ShortcutBinding(definition, bound));
        }
        return new ShortcutMap([.. chosen, .. defaults]);
    }

    /// <summary>Builds a map from bindings taken in order — first come, first served. For tests and the dialog.</summary>
    public static ShortcutMap From(IEnumerable<ShortcutBinding> bindings) => new(bindings);

    /// <summary>The keys bound to <paramref name="action"/>, or null when it has none.</summary>
    public Shortcut? For(ShortcutAction action) =>
        _byAction.TryGetValue(action, out var binding) ? binding.Keys : null;

    /// <summary>The keys bound to <paramref name="action"/> as they read on screen, or empty when it has none.</summary>
    public string DisplayFor(ShortcutAction action) => For(action)?.DisplayText ?? "";

    /// <summary>What <paramref name="keys"/> do, or null when nothing is bound to exactly them.</summary>
    public ShortcutBinding? Find(Shortcut keys) => _byKeys.GetValueOrDefault(keys);

    /// <summary>True when <paramref name="stroke"/> is the first press of some bound chord.</summary>
    public bool StartsChord(KeyStroke stroke) => _chordStarts.Contains(stroke);
}
