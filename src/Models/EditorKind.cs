namespace Fido.Models;

/// <summary>
/// A recognised editor/IDE family. Known kinds carry built-in auto-detection (PATH + common install
/// locations); <see cref="Custom"/> relies solely on an explicit path the user supplies.
/// </summary>
public enum EditorKind
{
    /// <summary>An editor located only by the explicit path on its <see cref="Editor"/>.</summary>
    Custom,

    /// <summary>JetBrains Rider.</summary>
    Rider,

    /// <summary>JetBrains WebStorm. Opens a project folder only (it has no concept of a <c>.sln</c>).</summary>
    WebStorm,

    /// <summary>Microsoft Visual Studio (Windows).</summary>
    VisualStudio,

    /// <summary>Visual Studio Code.</summary>
    VsCode,

    /// <summary>Zed.</summary>
    Zed,

    /// <summary>
    /// A terminal/console opened at the folder. Not an editor: the located program is a terminal emulator
    /// (Windows Terminal / cmd / PowerShell, macOS Terminal, a Linux terminal) and it's always handed the
    /// folder, never a <c>.sln</c>. The configured <see cref="Editor.Path"/> picks the terminal; blank
    /// auto-detects the OS default.
    /// </summary>
    Console,

    /// <summary>
    /// The OS file manager (Windows Explorer, macOS Finder, a Linux file manager) revealing the folder.
    /// Like <see cref="Console"/> it always opens the folder; <see cref="Editor.Path"/> overrides the
    /// file manager, blank auto-detects the OS default.
    /// </summary>
    FileExplorer,
}
