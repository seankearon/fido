using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Fido.Models;

namespace Fido.Input;

/// <summary>
/// One entry in the list of things a shortcut can do: what it's called, which group the Keyboard shortcuts
/// dialog files it under, and the keys it has out of the box (null for none).
/// </summary>
public sealed record ShortcutDefinition(ShortcutAction Action, string Name, string Group, Shortcut? Default);

/// <summary>
/// Every action a shortcut can be bound to, with its defaults, and the rules for reading and writing a
/// binding in the config.
///
/// Two places hold bindings. A <em>tool's</em> sits on the tool (<see cref="Editor.Shortcut"/>), so it goes
/// where the tool goes; left unset, the first nine tools answer to <c>Ctrl+1</c> … <c>Ctrl+9</c> by
/// position, which is how Fido has always numbered them. <em>Everything else</em> is keyed by command name
/// in <see cref="AppConfig.Shortcuts"/>, holding only what differs from the default. In both, an empty
/// string means "no shortcut" — deliberately none, not "the default".
/// </summary>
public static class ShortcutCatalog
{
    public const string OpenGroup = "Open";
    public const string BranchGroup = "Branch & location";
    public const string PanelGroup = "Flight log & Console";
    public const string FidoGroup = "Fido";

    /// <summary>The tools that get a numbered shortcut by default: <c>Ctrl+1</c> … <c>Ctrl+9</c>.</summary>
    public const int NumberedTools = 9;

    /// <summary>The commands other than opening a tool, in the order the dialog lists them.</summary>
    public static IReadOnlyList<ShortcutDefinition> Commands { get; } =
    [
        new(ShortcutCommand.OpenDefault, "Open in the default tool", OpenGroup, null),

        new(ShortcutCommand.Rescan, "Scan for the branch again", BranchGroup, Keys(KeyStroke.From(Key.F5))),
        new(ShortcutCommand.FocusBranch, "Go to the branch box", BranchGroup, null),
        new(ShortcutCommand.FocusSolution, "Go to the solution filter", BranchGroup, null),
        new(ShortcutCommand.NextLocation, "Select the next location", BranchGroup, null),
        new(ShortcutCommand.PreviousLocation, "Select the previous location", BranchGroup, null),
        new(ShortcutCommand.CopyPath, "Copy the selected path", BranchGroup, null),
        new(ShortcutCommand.EditRepoConfig, "Create or edit .fido/cfg.yaml", BranchGroup, null),
        new(ShortcutCommand.OpenPullRequest, "Open the pull request", BranchGroup, null),
        new(ShortcutCommand.DeleteWorktree, "Delete the worktree… (asks first)", BranchGroup, null),

        new(ShortcutCommand.ShowFlightLog, "Show the flight log", PanelGroup, null),
        new(ShortcutCommand.ShowConsole, "Show the Console tab", PanelGroup, null),
        new(ShortcutCommand.CopyFlightLog, "Copy the flight log", PanelGroup, null),
        new(ShortcutCommand.SaveFlightLog, "Save the flight log…", PanelGroup, null),

        new(ShortcutCommand.ToggleTheme, "Flip light / dark", FidoGroup,
            Keys(KeyStroke.Ctrl(Key.K), KeyStroke.Ctrl(Key.T))),
        new(ShortcutCommand.Settings, "Settings…", FidoGroup, Keys(KeyStroke.Ctrl(Key.OemComma))),
        new(ShortcutCommand.KeyboardShortcuts, "Keyboard shortcuts…", FidoGroup,
            Keys(KeyStroke.Ctrl(Key.K), KeyStroke.Ctrl(Key.S))),
    ];

    /// <summary>
    /// Every bindable action for this tool list — a row per tool, then the commands — in the order the dialog
    /// shows them, which is also the order a clash between two saved bindings is settled in.
    /// </summary>
    public static IReadOnlyList<ShortcutDefinition> For(IReadOnlyList<Editor> editors) =>
    [
        .. editors.Select((editor, i) =>
            new ShortcutDefinition(ShortcutAction.Tool(i), ToolName(editor), OpenGroup, ToolDefault(i))),
        .. Commands,
    ];

    /// <summary>A tool's row: <c>Open in Rider</c>.</summary>
    public static string ToolName(Editor editor) =>
        $"Open in {(string.IsNullOrWhiteSpace(editor.Name) ? editor.Kind.ToString() : editor.Name.Trim())}";

    /// <summary>The tool at <paramref name="index"/>'s shortcut when none is set: its number, for the first nine.</summary>
    public static Shortcut? ToolDefault(int index) =>
        index is >= 0 and < NumberedTools ? Keys(KeyStroke.Ctrl(Key.D1 + index)) : null;

    /// <summary>
    /// The binding <paramref name="definition"/> has in <paramref name="config"/>, and whether it was set there
    /// rather than left at its default. A value that won't parse — a hand edit gone wrong — counts as set to
    /// none: quietly falling back to the default would hide that it was ever there.
    /// </summary>
    public static (Shortcut? Keys, bool IsSet) Read(AppConfig config, ShortcutDefinition definition)
    {
        var action = definition.Action;
        var text = action.IsTool
            ? action.ToolIndex < config.Editors.Count ? config.Editors[action.ToolIndex].Shortcut : null
            : Lookup(config.Shortcuts, action.Command);

        if (text is null) return (definition.Default, false);
        return (Parse(text), true);
    }

    /// <summary>
    /// Records <paramref name="keys"/> as <paramref name="definition"/>'s binding in <paramref name="config"/>.
    /// The default is written as nothing at all, so a later change of default reaches everyone who never
    /// chose otherwise — and a tool that has its number keeps following its position.
    /// </summary>
    public static void Write(AppConfig config, ShortcutDefinition definition, Shortcut? keys)
    {
        var action = definition.Action;
        var text = keys == definition.Default ? null : keys?.ToString() ?? "";

        if (action.IsTool)
        {
            if (action.ToolIndex < config.Editors.Count) config.Editors[action.ToolIndex].Shortcut = text;
            return;
        }

        var shortcuts = config.Shortcuts ??= new();
        foreach (var stale in shortcuts.Keys.Where(k => IsKeyFor(k, action.Command)).ToList())
            shortcuts.Remove(stale);
        if (text is not null) shortcuts[action.Command.ToString()] = text;
    }

    /// <summary>The keys in <paramref name="text"/>: null for blank or unreadable, and for anything that couldn't
    /// be pressed as a shortcut anyway (a plain letter would be typing).</summary>
    private static Shortcut? Parse(string text) =>
        Shortcut.TryParse(text, out var keys) && keys.IsValid ? keys : null;

    private static string? Lookup(Dictionary<string, string>? shortcuts, ShortcutCommand command)
    {
        if (shortcuts is null) return null;
        foreach (var (key, value) in shortcuts)
            if (IsKeyFor(key, command)) return value ?? "";
        return null;
    }

    // Matched case-insensitively: the file is there to be hand-edited, and "rescan" means Rescan.
    private static bool IsKeyFor(string key, ShortcutCommand command) =>
        string.Equals(key?.Trim(), command.ToString(), StringComparison.OrdinalIgnoreCase);

    private static Shortcut Keys(KeyStroke first, KeyStroke? second = null) => new(first, second);
}
