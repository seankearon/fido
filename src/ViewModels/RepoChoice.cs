using System.IO;
using Fido.Mvvm;

namespace Fido.ViewModels;

/// <summary>
/// A repository the user can tick in Settings to include in the new-branch offer (the place Fido
/// proposes when a typed branch isn't checked out anywhere). Backs the settings checklist.
/// </summary>
public sealed class RepoChoice : ObservableObject
{
    private bool _isEnabled;

    public RepoChoice(string path, bool isEnabled)
    {
        Path = path;
        _isEnabled = isEnabled;
        DisplayName = $"{new DirectoryInfo(path).Name}  ({path})";
    }

    /// <summary>Canonical main-clone path persisted to <see cref="Models.AppConfig.NewBranchRepos"/>.</summary>
    public string Path { get; }

    /// <summary>Friendly label for the checklist, e.g. <c>my-app  (C:\src\my-app)</c>.</summary>
    public string DisplayName { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }
}
