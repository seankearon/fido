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

    /// <summary>How long to wait on <c>gh</c> before giving up and treating the answer as "no PR known" —
    /// so a stalled network call can't freeze the delete-confirm UI.</summary>
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);

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
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(QueryTimeout);
            r = await _run(dir, ["pr", "list", "--head", branch, "--state", "open", "--json", "number,url,title", "--limit", "1"], timeout.Token);
        }
        catch
        {
            // gh unavailable, cancelled, or timed out — treated as "no PR known".
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
                // A PR always carries an integer number; treat a record without one as "not a PR" and skip.
                if (!el.TryGetProperty("number", out var numEl) || !numEl.TryGetInt32(out var number)) continue;
                // url/title are best-effort — read them only when they're actually strings, so an unexpected
                // type degrades to empty rather than throwing (the number alone means a PR exists, which is
                // what blocks the remote delete).
                var url = el.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String
                    ? urlEl.GetString() ?? "" : "";
                var title = el.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String
                    ? titleEl.GetString() ?? "" : "";
                return new PullRequestInfo(number, url, title);
            }
            return null;
        }
        catch
        {
            // Contractually never throws — any unexpected parse failure means "no PR known".
            return null;
        }
    }
}
