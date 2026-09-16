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
    /// The folders holding a worktree for <paramref name="branch"/> that are <em>actually on disk</em>, for
    /// the line under the branch box. Candidates are worked out exactly as a worktree would be created —
    /// under the configured <see cref="AppConfig.WorktreeRoot"/> when there is one (repo-independent, so a
    /// single answer with no repo to attribute it to), else the sibling convention beside each of
    /// <paramref name="clonePaths"/> — and then only the ones that exist are returned. A folder that
    /// exists but isn't a worktree of this branch still counts: a leftover, or a tree since switched to
    /// another branch, is exactly what you want pointed out when you type the name again. Ordered by repo
    /// so the list doesn't shuffle between keystrokes, and de-duplicated by path.
    /// </summary>
    public static IReadOnlyList<WorktreeCandidate> Candidates(
        string branch, IReadOnlyList<string> clonePaths, AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(branch)) return [];

        if (!string.IsNullOrWhiteSpace(config.WorktreeRoot))
        {
            var rooted = InRepo("", branch, config);
            return Exists(rooted) ? [new WorktreeCandidate(rooted, "")] : [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<WorktreeCandidate>();
        foreach (var clone in clonePaths)
        {
            if (string.IsNullOrWhiteSpace(clone)) continue;
            var path = InRepo(clone, branch, config);
            if (seen.Add(path) && Exists(path))
                candidates.Add(new WorktreeCandidate(path, RepoNameOf(clone)));
        }
        return [.. candidates.OrderBy(c => c.RepoName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Whether the folder is on disk. An unreadable or malformed path simply isn't offered.</summary>
    private static bool Exists(string path)
    {
        try { return Directory.Exists(path); }
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
