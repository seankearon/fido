using Fido.Input;
using Fido.Mvvm;

namespace Fido.ViewModels;

/// <summary>
/// One action in the Keyboard shortcuts dialog: its name, the keys it has, and whether those are being
/// recorded right now. <see cref="ShortcutsViewModel"/> owns the rules — this only holds the row's state and
/// says how it reads.
/// </summary>
public sealed class ShortcutRow : ObservableObject
{
    private Shortcut? _keys;
    private bool _isRecording;
    private KeyStroke? _recorded;

    public ShortcutRow(ShortcutDefinition definition, Shortcut? keys, bool isFirstInGroup)
    {
        Definition = definition;
        _keys = keys;
        IsFirstInGroup = isFirstInGroup;
    }

    public ShortcutDefinition Definition { get; }

    public string Name => Definition.Name;

    /// <summary>The heading this row sits under; shown above the first row of each group.</summary>
    public string Group => Definition.Group;

    public bool IsFirstInGroup { get; }

    /// <summary>The keys this action has, or null for none.</summary>
    public Shortcut? Keys
    {
        get => _keys;
        set
        {
            if (!SetField(ref _keys, value)) return;
            OnPropertyChanged(nameof(KeysText));
            OnPropertyChanged(nameof(HasKeys));
            OnPropertyChanged(nameof(IsCustom));
        }
    }

    /// <summary>True when the row has keys — what the clear button is enabled by.</summary>
    public bool HasKeys => _keys is not null;

    /// <summary>True when the keys differ from the default — what the reset button is enabled by.</summary>
    public bool IsCustom => _keys != Definition.Default;

    /// <summary>The reset button's tooltip, naming what it would put back.</summary>
    public string ResetTip => Definition.Default is { } keys
        ? $"Back to the default, {keys.DisplayText}"
        : "Back to the default, which is no shortcut";

    /// <summary>True while this row is listening for keys.</summary>
    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (!SetField(ref _isRecording, value)) return;
            if (!value) _recorded = null;
            OnPropertyChanged(nameof(KeysText));
        }
    }

    /// <summary>The first press of a chord being recorded, while the row waits to hear whether a second follows.</summary>
    public KeyStroke? Recorded
    {
        get => _recorded;
        set
        {
            if (!SetField(ref _recorded, value)) return;
            OnPropertyChanged(nameof(KeysText));
        }
    }

    /// <summary>The key cap's caption: the keys, a dash for none, or what recording has heard so far.</summary>
    public string KeysText =>
        _isRecording
            ? _recorded is { } first ? $"{first.DisplayText}, …" : "press keys…"
            : _keys?.DisplayText ?? "—";
}
