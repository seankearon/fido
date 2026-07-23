using System.Text.Json;
using System.Threading;
using Fido.Models;

namespace Fido.Services;

/// <summary>
/// Thin wrapper over the GitHub CLI (<c>gh</c>) for the one query Fido needs: is there an open pull
/// request for a branch? Mirrors <see cref="GitService"/>'s injectable-runner seam so tests can script
/// gh's output without a real gh install. Every failure mode — gh not installed, the repo isn't a GitHub
/// remote, the user isn't authenticated, malformed output — degrades to <c>null</c> (no PR known), never
/// an exception: the check is advisory, gating only whether the remote-branch delete is offered.
/// </summary>
public sealed class GitHubCli
{
    /// <summary>Runs a <c>gh</c> command in <paramref name="workingDir"/> and returns its captured result.
    /// The default shells out to the real <c>gh</c> CLI; tests inject a fake to script output.</summary>
    public delegate Task<ProcessResult> CliRunner(string workingDir, IReadOnlyList<string> args, CancellationToken ct);

    private readonly CliRunner _run;

    public GitHubCli(CliRunner? run = null) => _run = run ?? DefaultRun;

    private static async Task<ProcessResult> DefaultRun(string dir, IReadOnlyList<string> args, CancellationToken ct)
    {
        try
        {
            return await ProcessRunner.RunAsync("gh", args, dir, ct);
        }
        catch
        {
            // gh not on PATH (Win32Exception) or otherwise un-launchable — treated as "no PR known".
            return new ProcessResult(127, "", "gh not available");
        }
    }

    /// <summary>
    /// The first <em>open</em> pull request whose head branch is <paramref name="branch"/>, or <c>null</c>
    /// when there is none (or gh can't answer). Runs
    /// <c>gh pr list --head &lt;branch&gt; --state open --json number,url,title --limit 1</c> in
    /// <paramref name="dir"/> (the clone's main tree, so gh resolves the repo from its <c>origin</c> remote).
    /// Never throws.
    /// </summary>
    public async Task<PullRequestInfo?> FindOpenPullRequestAsync(string dir, string branch, CancellationToken ct = default)
    {
        ProcessResult r;
        try
        {
            r = await _run(dir, ["pr", "list", "--head", branch, "--state", "open", "--json", "number,url,title", "--limit", "1"], ct);
        }
        catch
        {
            return null;
        }

        if (!r.Success || string.IsNullOrWhiteSpace(r.StdOut)) return null;

        try
        {
            using var doc = JsonDocument.Parse(r.StdOut);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                if (!el.TryGetProperty("number", out var numEl) || numEl.ValueKind != JsonValueKind.Number) continue;
                var number = numEl.GetInt32();
                var url = el.TryGetProperty("url", out var urlEl) ? urlEl.GetString() ?? "" : "";
                var title = el.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "" : "";
                return new PullRequestInfo(number, url, title);
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
