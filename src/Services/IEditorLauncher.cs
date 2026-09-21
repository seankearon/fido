using Fido.Models;

namespace Fido.Services;

/// <summary>
/// Locates and launches a configured <see cref="Editor"/>. Abstracted from <see cref="EditorLauncher"/>
/// so the UI flow can be driven in tests with a fake that records launches and returns a configurable
/// locate result (including "not installed" → null) without spawning a real IDE.
/// </summary>
public interface IEditorLauncher
{
    /// <summary>Returns the editor's executable/app-bundle path, or null if none is found.</summary>
    string? Locate(Editor editor);

    /// <summary>
    /// Starts <paramref name="editor"/> on <paramref name="targetPath"/> without waiting for it.
    /// <paramref name="consoleCommand"/> — one of the commands the branch's <c>.fido/cfg.yaml</c>
    /// listed — makes a Console target run that command at the folder rather than just
    /// opening there; every other kind of target ignores it.
    /// </summary>
    void Launch(Editor editor, string executable, string targetPath, string? consoleCommand = null);
}
