namespace Fido.ViewModels;

/// <summary>
/// A launch button/shortcut for one configured editor, shown on the main window. The default editor
/// drives the primary Open button; the rest are offered as secondary buttons with a numbered gesture.
/// </summary>
/// <param name="Index">Position in <see cref="Models.AppConfig.Editors"/> — identifies which editor to launch.</param>
/// <param name="Name">The editor's display name.</param>
/// <param name="Gesture">Keyboard-shortcut hint (e.g. <c>Ctrl+2</c>), or empty when it has no numbered shortcut.</param>
/// <param name="IsDefault">True for the default editor (the primary Open button).</param>
public sealed record EditorLaunchOption(int Index, string Name, string Gesture, bool IsDefault)
{
    /// <summary>Button caption for a secondary editor, e.g. <c>VS Code · Ctrl+2</c> (drops the gesture when none).</summary>
    public string ButtonLabel => string.IsNullOrEmpty(Gesture) ? Name : $"{Name}  ·  {Gesture}";
}
