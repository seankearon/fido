using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Fido.Input;
using Fido.Models;
using Fido.Mvvm;

namespace Fido.ViewModels;

/// <summary>
/// The Keyboard shortcuts dialog: every action Fido has a shortcut for, and the recorder that assigns one.
///
/// Recording works as it does in VS Code. Click a row's key cap and press the keys: the first press is
/// held, and a second press makes it a two-press chord (<c>Ctrl+K, Ctrl+S</c>) and assigns it there and then;
/// <c>Enter</c> instead keeps the single press as the whole shortcut. <c>Esc</c> — or clicking away — leaves
/// the row as it was. Keys that would type, or that the OS answers first, are refused with a note saying why.
///
/// No two actions ever share keys, or have one start the other's chord: assigning keys that clash takes them
/// from whatever had them, and <see cref="Notice"/> says what was taken from where. So what is saved is always
/// a set of shortcuts that can all be pressed. Nothing reaches the config until <see cref="ApplyTo"/>.
/// </summary>
public sealed class ShortcutsViewModel : ObservableObject
{
    /// <summary>What the notice line says before anything has happened.</summary>
    public const string Guidance =
        "Click a shortcut, then press the keys. For a chord, press two in a row — Ctrl+K, then Ctrl+S.";

    private ShortcutRow? _recording;
    private string _notice = Guidance;

    /// <summary>Every bindable action, tools first, in the order the main screen's groups read.</summary>
    public ObservableCollection<ShortcutRow> Rows { get; } = new();

    /// <summary>The line under the list: guidance while recording, and what the last change did.</summary>
    public string Notice
    {
        get => _notice;
        private set => SetField(ref _notice, value);
    }

    /// <summary>The row listening for keys, or null.</summary>
    public ShortcutRow? RecordingRow => _recording;

    public bool IsRecording => _recording is not null;

    /// <summary>
    /// Fills the rows from <paramref name="config"/>, each with the keys that actually work today — a clash a
    /// hand edit left in the file shows as it plays out (see <see cref="ShortcutMap"/>), so saving tidies it.
    /// </summary>
    public void LoadFrom(AppConfig config)
    {
        EndRecording();
        var live = ShortcutMap.FromConfig(config);
        Rows.Clear();
        string? group = null;
        foreach (var definition in ShortcutCatalog.For(config.Editors))
        {
            Rows.Add(new ShortcutRow(definition, live.For(definition.Action), definition.Group != group));
            group = definition.Group;
        }
        Notice = Guidance;
    }

    /// <summary>Writes every row's keys into <paramref name="config"/>, keeping only what differs from a default.</summary>
    public void ApplyTo(AppConfig config)
    {
        foreach (var row in Rows)
            ShortcutCatalog.Write(config, row.Definition, row.Keys);
    }

    /// <summary>Starts listening for keys for <paramref name="row"/>; one row at a time.</summary>
    public void BeginRecording(ShortcutRow row)
    {
        EndRecording();
        _recording = row;
        row.IsRecording = true;
        Notice = $"{row.Name}: press the keys. Esc cancels.";
        OnPropertyChanged(nameof(RecordingRow));
        OnPropertyChanged(nameof(IsRecording));
    }

    /// <summary>Stops listening without changing anything.</summary>
    public void CancelRecording()
    {
        if (_recording is not { } row) return;
        EndRecording();
        Notice = $"{row.Name}: left as it was.";
    }

    /// <summary>
    /// A key pressed while a row is recording. The first real press is held; a second completes a chord, and
    /// <c>Enter</c> settles for the single press. Modifiers going down on their own are just the run-up.
    /// </summary>
    public void Press(KeyStroke stroke)
    {
        if (_recording is not { } row || stroke.IsModifierKey) return;

        if (stroke.IsEscape)
        {
            CancelRecording();
            return;
        }

        if (row.Recorded is not { } first)
        {
            if (!stroke.CanBegin)
            {
                Notice = stroke.IsReserved
                    ? $"{stroke.DisplayText} belongs to the system — try another."
                    : $"A shortcut can't start with plain {stroke.DisplayText} — that belongs to the box you're " +
                      $"typing in. Hold Ctrl, Alt or {KeyStroke.PlatformMetaName} for the first key, or use a " +
                      "function key.";
                return;
            }

            row.Recorded = stroke;
            Notice = $"{stroke.DisplayText} — press another key to make it a chord, or Enter to keep it as it is. " +
                     "Esc cancels.";
            return;
        }

        var keys = stroke.IsEnter ? new Shortcut(first) : new Shortcut(first, stroke);
        EndRecording();
        Assign(row, keys);
    }

    /// <summary>Takes <paramref name="row"/>'s shortcut away.</summary>
    public void Clear(ShortcutRow row)
    {
        EndRecording();
        row.Keys = null;
        Notice = $"{row.Name} has no shortcut now.";
    }

    /// <summary>Puts <paramref name="row"/>'s default back — taking it from anything that has it since.</summary>
    public void Reset(ShortcutRow row)
    {
        EndRecording();
        if (row.Definition.Default is { } keys)
        {
            Assign(row, keys);
        }
        else
        {
            row.Keys = null;
            Notice = $"{row.Name} is back to its default, which is no shortcut.";
        }
    }

    /// <summary>Every row back to its default. The defaults never clash with one another, so nothing is taken.</summary>
    public void ResetAll()
    {
        EndRecording();
        foreach (var row in Rows) row.Keys = row.Definition.Default;
        Notice = "Every shortcut is back to its default.";
    }

    /// <summary>
    /// Gives <paramref name="keys"/> to <paramref name="row"/>, taking them from any row whose keys clash —
    /// the same keys, or a single press where the other's chord starts — and saying so.
    /// </summary>
    private void Assign(ShortcutRow row, Shortcut keys)
    {
        var taken = Rows
            .Where(other => !ReferenceEquals(other, row) && other.Keys is { } theirs && theirs.CollidesWith(keys))
            .ToList();
        foreach (var other in taken) other.Keys = null;
        row.Keys = keys;

        Notice = taken.Count == 0
            ? $"{keys.DisplayText} → {row.Name}."
            : $"{keys.DisplayText} → {row.Name}. Taken from {JoinNames(taken)}, which " +
              $"{(taken.Count == 1 ? "now has" : "now have")} no shortcut.";
    }

    private void EndRecording()
    {
        if (_recording is not { } row) return;
        row.IsRecording = false;
        _recording = null;
        OnPropertyChanged(nameof(RecordingRow));
        OnPropertyChanged(nameof(IsRecording));
    }

    private static string JoinNames(IReadOnlyList<ShortcutRow> rows) =>
        rows.Count == 1
            ? rows[0].Name
            : $"{string.Join(", ", rows.Take(rows.Count - 1).Select(r => r.Name))} and {rows[^1].Name}";
}
