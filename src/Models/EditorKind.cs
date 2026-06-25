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

    /// <summary>Microsoft Visual Studio (Windows).</summary>
    VisualStudio,

    /// <summary>Visual Studio Code.</summary>
    VsCode,

    /// <summary>Zed.</summary>
    Zed,
}
