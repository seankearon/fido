using System.IO;

namespace Fido.Models;

/// <summary>
/// A candidate repository that contains the requested solution, identified by its
/// canonical <see cref="MainWorktreePath"/> (so <c>.sln</c> copies discovered inside
/// linked worktrees collapse back to one repo).
/// </summary>
public sealed record RepositoryInfo(string MainWorktreePath, string SolutionFileName)
{
    /// <summary>The repo's folder name, e.g. <c>my-app</c> — used in pickers, logs and search progress.</summary>
    public string Name => new DirectoryInfo(MainWorktreePath).Name;

    /// <summary>Friendly label for pickers/logs, e.g. <c>MyApp (C:\src\my-app)</c>.</summary>
    public string DisplayName => $"{Name}  ({MainWorktreePath})";
}
