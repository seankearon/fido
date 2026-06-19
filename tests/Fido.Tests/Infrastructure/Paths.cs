using System.IO;

namespace Fido.Tests.Infrastructure;

/// <summary>
/// Separator-robust path assertions. Git porcelain emits forward slashes even on Windows, while
/// <see cref="Path.Combine"/> emits backslashes, so launch-target comparisons must normalize first.
/// </summary>
internal static class Paths
{
    private static string Full(string path) => Path.GetFullPath(path);

    public static bool StartsWith(string path, string prefix) =>
        Full(path).StartsWith(Full(prefix), StringComparison.OrdinalIgnoreCase);

    public static bool Contains(string path, string needle) =>
        Full(path).Contains(needle.Replace('/', Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}
