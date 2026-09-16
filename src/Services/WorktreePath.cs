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

        if (!string.IsNullOrWhiteSpace(config.WorktreeRoot))
            return Path.Combine(config.WorktreeRoot, folder);

        var repoDir = mainWorktreePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(repoDir) ?? repoDir;
        var repoName = Path.GetFileName(repoDir);
        return Path.Combine(parent, $"{repoName}.worktrees", folder);
    }

    /// <summary>
    /// The worktree path a branch name alone determines, or <c>""</c> when it doesn't determine one.
    /// A configured <see cref="AppConfig.WorktreeRoot"/> settles it outright — every branch lands under
    /// that root, whatever repo it belongs to. Without one the sibling convention applies, and that needs
    /// a clone to sit beside: a branch name names as many folders as there are repos, so this returns
    /// empty rather than picking one of them. (Nothing is lost by that — once a scan has run, the
    /// <see cref="TargetKind.NewWorktree"/> card carries the path for the repo it belongs to.)
    /// </summary>
    public static string ForBranch(string branch, AppConfig config) =>
        string.IsNullOrWhiteSpace(branch) || string.IsNullOrWhiteSpace(config.WorktreeRoot)
            ? ""
            : Path.Combine(config.WorktreeRoot, FolderName(branch));

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
