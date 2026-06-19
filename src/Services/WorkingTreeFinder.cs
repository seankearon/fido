using System;
using System.IO;

namespace Fido.Services;

/// <summary>Depth-limited scan of search roots for git working-tree directories.</summary>
public sealed class WorkingTreeFinder
{
    private static readonly HashSet<string> SkipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", ".vs", ".idea", "packages", ".svn", ".hg",
    };

    /// <summary>
    /// Returns every git working-tree root under <paramref name="roots"/> — a directory containing a
    /// <c>.git</c> file (linked worktree) or folder (main tree) — descending at most
    /// <paramref name="maxDepth"/> levels and not descending into a working tree once found
    /// (linked worktrees live outside the main tree, so nothing is missed).
    /// </summary>
    public IReadOnlyList<string> Find(IEnumerable<string> roots, int maxDepth)
    {
        var results = new List<string>();
        var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;

            string full;
            try { full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root)); }
            catch { continue; }

            if (seenRoots.Add(full) && Directory.Exists(full))
                Scan(full, maxDepth, depth: 0, results);
        }

        return results;
    }

    private static void Scan(string dir, int maxDepth, int depth, List<string> results)
    {
        if (IsWorkingTree(dir))
        {
            results.Add(dir);
            return; // a working tree is a unit; don't descend into it
        }

        if (depth >= maxDepth) return;

        IEnumerable<string> subdirs;
        try { subdirs = Directory.EnumerateDirectories(dir); }
        catch { return; }

        foreach (var sub in subdirs)
        {
            var name = Path.GetFileName(sub);
            if (ShouldSkip(name)) continue;
            Scan(sub, maxDepth, depth + 1, results);
        }
    }

    private static bool IsWorkingTree(string dir)
    {
        var gitPath = Path.Combine(dir, ".git");
        return File.Exists(gitPath) || Directory.Exists(gitPath);
    }

    private static bool ShouldSkip(string name)
        => name.Length == 0
           || SkipNames.Contains(name)
           || (name.Length > 1 && name[0] == '.');
}
