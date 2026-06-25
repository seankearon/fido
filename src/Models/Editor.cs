namespace Fido.Models;

/// <summary>
/// A configured editor/IDE Fido can launch a resolved target in. One editor in
/// <see cref="AppConfig.Editors"/> is the default (driven by the Open button / Enter); the rest are
/// reachable by numbered keyboard shortcuts. Known <see cref="EditorKind"/>s auto-detect when
/// <see cref="Path"/> is blank; <see cref="EditorKind.Custom"/> requires an explicit path.
/// </summary>
public sealed class Editor
{
    /// <summary>Display name shown on the launch button and in the flight log (e.g. "Rider", "VS Code").</summary>
    public string Name { get; set; } = "";

    /// <summary>Which editor family this is — selects the auto-detection strategy.</summary>
    public EditorKind Kind { get; set; } = EditorKind.Custom;

    /// <summary>Explicit path to the executable/app bundle; auto-detected from <see cref="Kind"/> when null/empty.</summary>
    public string? Path { get; set; }

    /// <summary>
    /// Optional extra command-line arguments passed before the target path (space-separated).
    /// Leave blank for the editor's defaults.
    /// </summary>
    public string? Arguments { get; set; }

    /// <summary>The built-in editors offered out of the box, in shortcut order; Rider is the default.</summary>
    public static List<Editor> Defaults() => new()
    {
        new Editor { Name = "Rider", Kind = EditorKind.Rider },
        new Editor { Name = "VS Code", Kind = EditorKind.VsCode },
        new Editor { Name = "Visual Studio", Kind = EditorKind.VisualStudio },
        new Editor { Name = "Zed", Kind = EditorKind.Zed },
    };
}
