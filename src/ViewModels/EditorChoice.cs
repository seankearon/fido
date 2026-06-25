using System.Collections.Generic;
using Fido.Models;
using Fido.Mvvm;

namespace Fido.ViewModels;

/// <summary>
/// An editor row in the Settings editor list — editable name/kind/path plus a "default" flag. The
/// settings view model keeps the default single-select across the collection. Maps to/from
/// <see cref="Editor"/> on load and save.
/// </summary>
public sealed class EditorChoice : ObservableObject
{
    private string _name;
    private EditorKind _kind;
    private string _path;
    private string _arguments;
    private bool _isDefault;

    public EditorChoice(Editor editor, bool isDefault)
    {
        _name = editor.Name;
        _kind = editor.Kind;
        _path = editor.Path ?? "";
        _arguments = editor.Arguments ?? "";
        _isDefault = isDefault;
    }

    /// <summary>The editor kinds offered in the row's drop-down.</summary>
    public static IReadOnlyList<EditorKind> Kinds { get; } = new[]
    {
        EditorKind.Rider, EditorKind.VsCode, EditorKind.VisualStudio, EditorKind.Zed, EditorKind.Custom,
    };

    /// <summary>Instance view of <see cref="Kinds"/> for the row's compiled-binding ComboBox.</summary>
    public IReadOnlyList<EditorKind> KindOptions => Kinds;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public EditorKind Kind
    {
        get => _kind;
        set
        {
            if (SetField(ref _kind, value))
                OnPropertyChanged(nameof(PathPlaceholder));
        }
    }

    public string Path
    {
        get => _path;
        set => SetField(ref _path, value);
    }

    public string Arguments
    {
        get => _arguments;
        set => SetField(ref _arguments, value);
    }

    /// <summary>True for the default editor (the Open button / Enter); only one row is default at a time.</summary>
    public bool IsDefault
    {
        get => _isDefault;
        set => SetField(ref _isDefault, value);
    }

    /// <summary>Hint shown in the path box — auto-detect for known kinds, "required" for Custom.</summary>
    public string PathPlaceholder => _kind == EditorKind.Custom ? "path to executable (required)" : "blank = auto-detect";

    /// <summary>Materialises the row back into a persisted <see cref="Editor"/>.</summary>
    public Editor ToEditor() => new()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? Kind.ToString() : Name.Trim(),
        Kind = Kind,
        Path = string.IsNullOrWhiteSpace(Path) ? null : Path.Trim(),
        Arguments = string.IsNullOrWhiteSpace(Arguments) ? null : Arguments.Trim(),
    };
}
