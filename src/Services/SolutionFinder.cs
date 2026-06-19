using System;
using System.IO;

namespace Fido.Services;

/// <summary>Depth-limited scan of search roots for a specific <c>&lt;name&gt;.sln</c> file.</summary>
public sealed class SolutionFinder
{
    private static readonly HashSet<string> SkipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", ".vs", ".idea", "packages", ".svn", ".hg",
    };

    /// <summary>
    /// Returns every file matching any name in <paramref name="solutionFileNames"/> found
    /// under any of <paramref name="roots"/>, descending at most <paramref name="maxDepth"/>
    /// levels and skipping build/VCS/tooling directories.
    /// </summary>
    public IReadOnlyList<string> Find(IEnumerable<string> roots, IReadOnlyList<string> solutionFileNames, int maxDepth)
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
                Scan(full, solutionFileNames, maxDepth, depth: 0, results);
        }

        return results;
    }

    private static void Scan(string dir, IReadOnlyList<string> targets, int maxDepth, int depth, List<string> results)
    {
        try
        {
            foreach (var target in targets)
                foreach (var file in Directory.EnumerateFiles(dir, target))
                    results.Add(file);
        }
        catch
        {
            // Unreadable directory (permissions / reparse points) -> skip silently.
        }

        if (depth >= maxDepth) return;

        IEnumerable<string> subdirs;
        try { subdirs = Directory.EnumerateDirectories(dir); }
        catch { return; }

        foreach (var sub in subdirs)
        {
            var name = Path.GetFileName(sub);
            if (ShouldSkip(name)) continue;
            Scan(sub, targets, maxDepth, depth + 1, results);
        }
    }

    private static bool ShouldSkip(string name)
        => name.Length == 0
           || SkipNames.Contains(name)
           || (name.Length > 1 && name[0] == '.');  // hidden / dot-directories
}
