using System;

namespace Fido.Services;

/// <summary>Builds web URLs from git remote URLs. GitHub only for now; degrades to null otherwise.</summary>
public static class GitHostLinks
{
    /// <summary>Abbreviated form of a commit SHA for display (default 9 chars).</summary>
    public static string ShortSha(string? sha, int length = 9)
        => string.IsNullOrEmpty(sha) ? "" : sha.Length >= length ? sha[..length] : sha;

    /// <summary>
    /// GitHub commit URL for <paramref name="sha"/> given an <c>origin</c> URL in https or ssh
    /// form, or null when the remote isn't recognisably GitHub (or inputs are missing).
    /// </summary>
    public static string? GitHubCommitUrl(string? originUrl, string? sha)
    {
        if (string.IsNullOrWhiteSpace(originUrl) || string.IsNullOrWhiteSpace(sha))
            return null;

        var path = ExtractGitHubPath(originUrl.Trim());
        return path is null ? null : $"https://github.com/{path}/commit/{sha}";
    }

    /// <summary>Pulls <c>owner/repo</c> out of an https/ssh GitHub remote URL.</summary>
    private static string? ExtractGitHubPath(string url)
    {
        const string sshPrefix = "git@github.com:";
        const string sshUrlPrefix = "ssh://git@github.com/";
        const string host = "github.com/";

        string? path;
        if (url.StartsWith(sshPrefix, StringComparison.OrdinalIgnoreCase))
            path = url[sshPrefix.Length..];
        else if (url.StartsWith(sshUrlPrefix, StringComparison.OrdinalIgnoreCase))
            path = url[sshUrlPrefix.Length..];
        else
        {
            var idx = url.IndexOf(host, StringComparison.OrdinalIgnoreCase);
            path = idx >= 0 ? url[(idx + host.Length)..] : null;
        }

        if (string.IsNullOrEmpty(path)) return null;
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        path = path.Trim('/');
        return path.Length == 0 ? null : path;
    }
}
