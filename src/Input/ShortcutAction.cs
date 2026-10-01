namespace Fido.Input;

/// <summary>
/// What a keyboard shortcut can do on the main screen. Every one of these is also a button or a click
/// somewhere on it; a shortcut is simply another way in, and is held to the same gates — nothing opens before
/// discovery has found the branch, and a delete still asks first.
///
/// The names are the config's keys for them (see <see cref="Models.AppConfig.Shortcuts"/>), so renaming one
/// quietly drops the binding anybody has saved for it.
/// </summary>
public enum ShortcutCommand
{
    /// <summary>Open the selected location with one configured tool — see <see cref="ShortcutAction.ToolIndex"/>.
    /// Bound per tool, on the tool itself (<see cref="Models.Editor.Shortcut"/>), so it moves with it.</summary>
    OpenTool,
    OpenDefault,
    Rescan,
    FocusBranch,
    FocusSolution,
    NextLocation,
    PreviousLocation,
    CopyPath,
    EditRepoConfig,
    OpenPullRequest,
    DeleteWorktree,
    ShowFlightLog,
    ShowConsole,
    CopyFlightLog,
    SaveFlightLog,
    ToggleTheme,
    Settings,
    KeyboardShortcuts,
}

/// <summary>
/// One thing a shortcut can be bound to: a <see cref="ShortcutCommand"/>, and for
/// <see cref="ShortcutCommand.OpenTool"/> which tool — its position in <see cref="Models.AppConfig.Editors"/>.
/// </summary>
public readonly record struct ShortcutAction(ShortcutCommand Command, int ToolIndex = -1)
{
    /// <summary>Opening the selected location with the tool at <paramref name="index"/>.</summary>
    public static ShortcutAction Tool(int index) => new(ShortcutCommand.OpenTool, index);

    public static implicit operator ShortcutAction(ShortcutCommand command) => new(command);

    public bool IsTool => Command == ShortcutCommand.OpenTool;
}
