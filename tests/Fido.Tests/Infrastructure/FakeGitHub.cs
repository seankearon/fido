using Fido.Services;

namespace Fido.Tests.Infrastructure;

/// <summary>
/// Scripts the <c>gh</c> CLI for tests without a real gh install — via the injectable runner on
/// <see cref="GitHubCli"/>. <see cref="None"/> mimics "no open PR"; <see cref="Unavailable"/> mimics gh
/// missing; <see cref="WithOpenPr"/> returns one open PR for any branch queried.
/// </summary>
internal static class FakeGitHub
{
    /// <summary>gh returning an empty PR list — the branch has no open pull request.</summary>
    public static GitHubCli None { get; } =
        new((_, _, _) => Task.FromResult(new ProcessResult(0, "[]", "")));

    /// <summary>gh that fails to run (not installed / not a GitHub repo) — treated as "no PR known".</summary>
    public static GitHubCli Unavailable { get; } =
        new((_, _, _) => Task.FromResult(new ProcessResult(127, "", "gh: command not found")));

    /// <summary>gh reporting one open PR with the given number/url/title for any branch queried.</summary>
    public static GitHubCli WithOpenPr(int number, string url, string title) =>
        new((_, _, _) => Task.FromResult(new ProcessResult(0,
            $"[{{\"number\":{number},\"title\":{JsonString(title)},\"url\":{JsonString(url)}}}]", "")));

    private static string JsonString(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
