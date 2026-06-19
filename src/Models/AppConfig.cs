using System;

namespace Fido.Models;

/// <summary>
/// Persisted user settings (JSON under <c>%APPDATA%/Fido/config.json</c>).
/// Use <see cref="CreateDefault"/> when no file exists so default search roots are
/// expanded against the current user profile rather than baked into the type.
/// </summary>
public sealed class AppConfig
{
    /// <summary>Directories scanned (depth-limited) for a matching <c>&lt;name&gt;</c> solution or project.</summary>
    public List<string> SearchRoots { get; set; } = new();

    /// <summary>Explicit path to the Rider executable; auto-detected when null/empty.</summary>
    public string? RiderPath { get; set; }

    /// <summary>Override for where new worktrees are created; sibling convention when null/empty.</summary>
    public string? WorktreeRoot { get; set; }

    /// <summary>
    /// Main-clone paths offered when a typed branch (no solution) isn't checked out anywhere — Fido
    /// proposes creating it there as a worktree or a main-tree checkout. Populated in Settings.
    /// </summary>
    public List<string> NewBranchRepos { get; set; } = new();

    /// <summary>Branch names treated as the repo's default when origin/HEAD can't be read.</summary>
    public List<string> MainBranchNames { get; set; } = new() { "main", "master" };

    /// <summary>How many directory levels below each search root to descend.</summary>
    public int SearchDepth { get; set; } = 4;

    /// <summary>Recently entered branch names, newest first (see <see cref="Mru"/>).</summary>
    public List<string> RecentBranches { get; set; } = new();

    /// <summary>Recently entered solution names, newest first (see <see cref="Mru"/>).</summary>
    public List<string> RecentSolutions { get; set; } = new();

    /// <summary>Theme preference; <see cref="AppTheme.System"/> follows the OS.</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Builds a config seeded with common dev locations under the user profile.</summary>
    public static AppConfig CreateDefault()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new AppConfig
        {
            SearchRoots =
            {
                System.IO.Path.Combine(home, "source", "repos"),
                System.IO.Path.Combine(home, "src"),
                System.IO.Path.Combine(home, "RiderProjects"),
                System.IO.Path.Combine(home, "Projects"),
            },
        };
    }
}
