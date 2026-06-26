using System;

namespace Fido.Models;

/// <summary>
/// Persisted user settings (JSON under <c>%APPDATA%/Fido/config.json</c>).
/// Use <see cref="CreateDefault"/> when no file exists so default search roots are
/// expanded against the current user profile rather than baked into the type.
/// </summary>
public sealed class AppConfig
{
    /// <summary>
    /// Latest config schema version. Bumped when a one-time forward-migration is needed (e.g. seeding a
    /// newly-introduced built-in editor into existing lists); see <c>ConfigService.Normalize</c>.
    /// </summary>
    public const int CurrentConfigVersion = 1;

    /// <summary>
    /// Schema version of the loaded config; <c>0</c> for files written before versioning. Drives the
    /// one-time migrations in <c>ConfigService.Normalize</c>, which stamps it to <see cref="CurrentConfigVersion"/>.
    /// </summary>
    public int ConfigVersion { get; set; }

    /// <summary>Directories scanned (depth-limited) for a matching <c>&lt;name&gt;</c> solution or project.</summary>
    public List<string> SearchRoots { get; set; } = new();

    /// <summary>
    /// The editors/IDEs Fido can launch into. The one at <see cref="DefaultEditorIndex"/> drives the
    /// Open button and Enter; the rest are reachable by numbered keyboard shortcuts (Ctrl+1…Ctrl+9).
    /// </summary>
    public List<Editor> Editors { get; set; } = new();

    /// <summary>Index into <see cref="Editors"/> of the default editor (the Open button / Enter).</summary>
    public int DefaultEditorIndex { get; set; }

    /// <summary>
    /// Legacy explicit Rider path. Superseded by <see cref="Editors"/>; retained so an older config is
    /// migrated forward into the Rider editor's path on load (see <c>ConfigService.Normalize</c>).
    /// </summary>
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

    /// <summary>When Fido closes itself after a successful launch; defaults to command-line launches only.</summary>
    public CloseAfterOpen CloseAfterOpen { get; set; } = CloseAfterOpen.CommandLine;

    /// <summary>Default for <see cref="CloseAfterOpenDelaySeconds"/>: a forgiving countdown the user can pre-empt.</summary>
    public const int DefaultCloseAfterOpenDelaySeconds = 10;

    /// <summary>Upper bound for <see cref="CloseAfterOpenDelaySeconds"/>, so a stray value can't strand the window open.</summary>
    public const int MaxCloseAfterOpenDelaySeconds = 3600;

    /// <summary>
    /// Seconds Fido counts down after a successful launch before auto-closing (only when <see cref="CloseAfterOpen"/>
    /// says to close). <c>0</c> closes immediately; <see cref="CloseAfterOpen.Never"/> turns auto-close off entirely.
    /// </summary>
    public int CloseAfterOpenDelaySeconds { get; set; } = DefaultCloseAfterOpenDelaySeconds;

    /// <summary>
    /// The currently selected default editor (the Open button / Enter), or null when none are configured.
    /// Clamps an out-of-range <see cref="DefaultEditorIndex"/> back to the first editor.
    /// </summary>
    public Editor? DefaultEditor =>
        Editors.Count == 0 ? null : Editors[Math.Clamp(DefaultEditorIndex, 0, Editors.Count - 1)];

    /// <summary>
    /// Finds the configured editor whose <see cref="Editor.Slug"/> matches <paramref name="slug"/>
    /// (case-insensitive, trimmed), or null when none match. Editors with a blank slug are skipped, so they
    /// can't be selected from the command line. Used to honour <c>fido &lt;branch&gt; &lt;slug&gt;</c>.
    /// </summary>
    public Editor? FindEditorBySlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        var wanted = slug.Trim();
        return Editors.FirstOrDefault(e =>
            !string.IsNullOrWhiteSpace(e.Slug) &&
            string.Equals(e.Slug.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Builds a config seeded with common dev locations and the built-in editor list.</summary>
    public static AppConfig CreateDefault()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new AppConfig
        {
            ConfigVersion = CurrentConfigVersion,
            SearchRoots =
            {
                System.IO.Path.Combine(home, "source", "repos"),
                System.IO.Path.Combine(home, "src"),
                System.IO.Path.Combine(home, "RiderProjects"),
                System.IO.Path.Combine(home, "Projects"),
            },
            Editors = Editor.Defaults(),
        };
    }
}
