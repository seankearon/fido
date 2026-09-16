using System;
using System.IO;
using System.Linq;
using Fido.Models;

namespace Fido.Services;

/// <summary>
/// Where a branch's linked worktree lives — the one place that answers it, so the path Fido
/// <em>creates</em> a worktree at and the path it <em>shows</em> you for a branch can never drift apart.
/// </summary>
public static class WorktreePath
{
    /// <summary>
    /// The worktree path for <paramref name="branch"/> in <paramref name="mainWorktreePath"/>'s clone:
    /// under the configured <see cref="AppConfig.WorktreeRoot"/> when there is one, else the sibling
    /// <c>&lt;repo&gt;.worktrees</c> convention beside the clone.
    /// </summary>
    public static string InRepo(string mainWorktreePath, string branch, AppConfig config)
    {
        var folder = FolderName(branch);

        return Normalize(string.IsNullOrWhiteSpace(config.WorktreeRoot)
            ? Path.Combine(ContainerOf(mainWorktreePath), folder)
            : Path.Combine(config.WorktreeRoot, folder));
    }

    /// <summary>
    /// The path written the way this platform writes them. git reports POSIX-style paths even on Windows
    /// — <c>git rev-parse --git-common-dir</c> answers <c>D:/main/fido/.git</c> — so a folder derived
    /// from one would otherwise read, and paste, as <c>D:/main\fido.worktrees\xyz</c>. Only a rooted path
    /// is resolved: a worktree root still carrying an unexpanded <c>%USERPROFILE%</c> is left exactly as
    /// the user wrote it rather than being resolved against the current directory.
    /// </summary>
    private static string Normalize(string path)
    {
        try { return Path.IsPathRooted(path) ? Path.GetFullPath(path) : path; }
        catch { return path; }   // a malformed path is still better shown than thrown over
    }

    /// <summary>
    /// Every folder <paramref name="branch"/>'s worktree could live in, for the line under the branch box.
    /// A configured <see cref="AppConfig.WorktreeRoot"/> settles it outright — one answer, no repo to
    /// attribute it to, and <paramref name="clonePaths"/> doesn't matter. Without one the sibling
    /// convention applies and a branch names one folder <em>per clone</em>, so each scanned clone
    /// contributes its own, named by its repo. Paths are de-duplicated, and a blank branch or an empty
    /// clone list (nothing scanned yet) yields nothing.
    /// </summary>
    public static IReadOnlyList<WorktreeCandidate> Candidates(
        string branch, IReadOnlyList<string> clonePaths, AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(branch)) return [];

        if (!string.IsNullOrWhiteSpace(config.WorktreeRoot))
            return [new WorktreeCandidate(InRepo("", branch, config), "")];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<WorktreeCandidate>();
        foreach (var clone in clonePaths)
        {
            if (string.IsNullOrWhiteSpace(clone)) continue;
            var path = InRepo(clone, branch, config);
            if (seen.Add(path)) candidates.Add(new WorktreeCandidate(path, RepoNameOf(clone)));
        }
        return candidates;
    }

    /// <summary>
    /// The scanned clones in the order their worktree folders are worth offering: the ones that already
    /// have a worktree container beside them lead — a repo with a <c>&lt;repo&gt;.worktrees</c> folder
    /// demonstrably works this way, so its answer is the likelier one — and the rest follow by name.
    /// Branch-independent, so the caller ranks once per scan rather than once per keystroke, which is
    /// what keeps the disk out of the typing path.
    /// </summary>
    public static IReadOnlyList<string> RankClones(IEnumerable<string> clonePaths) =>
        [.. clonePaths
            .Where(clone => !string.IsNullOrWhiteSpace(clone))
            .OrderByDescending(HasWorktreeContainer)
            .ThenBy(RepoNameOf, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Whether the clone's sibling <c>&lt;repo&gt;.worktrees</c> folder is already on disk.</summary>
    private static bool HasWorktreeContainer(string clonePath)
    {
        try { return Directory.Exists(ContainerOf(clonePath)); }
        catch { return false; }
    }

    /// <summary>The sibling folder a clone's linked worktrees go in: <c>&lt;clone&gt;.worktrees</c>.</summary>
    private static string ContainerOf(string clonePath)
    {
        var repoDir = clonePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(repoDir) ?? repoDir;
        return Path.Combine(parent, $"{Path.GetFileName(repoDir)}.worktrees");
    }

    /// <summary>The clone's display name — its main working tree's folder name.</summary>
    private static string RepoNameOf(string clonePath) =>
        Path.GetFileName(clonePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>
    /// The folder name a branch maps to: its slashes and any characters the filesystem rejects replaced
    /// by dashes, so <c>feature/new-ui</c> becomes <c>feature-new-ui</c>.
    /// </summary>
    public static string FolderName(string branch)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = branch.Trim()
            .Select(c => c is '/' or '\\' || Array.IndexOf(invalid, c) >= 0 ? '-' : c)
            .ToArray();
        return new string(chars);
    }

    /// <summary>
    /// The deepest folder of <paramref name="path"/> that exists — the path itself when the worktree is
    /// already on disk, otherwise the nearest ancestor that is (its root folder, typically), or
    /// <c>null</c> when none of it exists. Lets "open this in the file manager" still land somewhere
    /// useful for a worktree that hasn't been created yet.
    /// </summary>
    public static string? NearestExistingFolder(string path)
    {
        for (var dir = path; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir) ?? "")
        {
            try { if (Directory.Exists(dir)) return dir; }
            catch { return null; }   // a malformed path can't be walked; nothing to open
        }
        return null;
    }
}
