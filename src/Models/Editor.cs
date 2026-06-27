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

    /// <summary>
    /// Short command-line token that selects this editor when Fido is launched from the CLI — e.g.
    /// <c>fido &lt;branch&gt; rider</c> or <c>fido -b &lt;branch&gt; -e rider</c>. Matched case-insensitively;
    /// blank means the editor can't be picked by slug (see <see cref="AppConfig.FindEditorBySlug"/>).
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>Which editor family this is — selects the auto-detection strategy.</summary>
    public EditorKind Kind { get; set; } = EditorKind.Custom;

    /// <summary>
    /// True when this target understands only a project folder, never a <c>.sln</c>/<c>.slnx</c> — WebStorm,
    /// or the non-editor <see cref="EditorKind.Console"/> / <see cref="EditorKind.FileExplorer"/> targets, which
    /// always open the folder itself. Fido forces folder mode for these and skips the "which solution?" chooser.
    /// </summary>
    public bool OpensFolderOnly => Kind is EditorKind.WebStorm or EditorKind.Console or EditorKind.FileExplorer;

    /// <summary>Explicit path to the executable/app bundle; auto-detected from <see cref="Kind"/> when null/empty.</summary>
    public string? Path { get; set; }

    /// <summary>
    /// Optional extra command-line arguments passed before the target path (space-separated).
    /// Leave blank for the editor's defaults.
    /// </summary>
    public string? Arguments { get; set; }

    /// <summary>The built-in targets offered out of the box, in shortcut order; Rider is the default. The
    /// last two — Console and File Explorer — open the folder in a terminal or the OS file manager.</summary>
    public static List<Editor> Defaults() => new()
    {
        new Editor { Name = "Rider", Kind = EditorKind.Rider, Slug = "rider" },
        new Editor { Name = "WebStorm", Kind = EditorKind.WebStorm, Slug = "ws" },
        new Editor { Name = "VS Code", Kind = EditorKind.VsCode, Slug = "vsc" },
        new Editor { Name = "Visual Studio", Kind = EditorKind.VisualStudio, Slug = "vs" },
        new Editor { Name = "Zed", Kind = EditorKind.Zed, Slug = "zed" },
        new Editor { Name = "Console", Kind = EditorKind.Console, Slug = "term" },
        new Editor { Name = "File Explorer", Kind = EditorKind.FileExplorer, Slug = "files" },
    };
}
