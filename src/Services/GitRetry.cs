using System;
using System.Threading;
using Polly;
using Polly.Retry;

namespace Fido.Services;

/// <summary>Tuning for <see cref="GitRetry"/>'s deletion pipeline. The defaults suit an interactive desktop
/// cleanup — a few quick, backing-off retries — and tests dial the delay down to zero to stay fast.</summary>
public sealed record GitRetryOptions
{
    /// <summary>How many times to re-run a transiently-failing command (on top of the first attempt).</summary>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>Base wait before the first retry; later waits grow per <see cref="BackoffType"/>.</summary>
    public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Growth of the wait between retries. Exponential by default (0.25s, 0.5s, 1s …).</summary>
    public DelayBackoffType BackoffType { get; init; } = DelayBackoffType.Exponential;

    /// <summary>Spread the waits a little so parallel callers don't all retry in lockstep.</summary>
    public bool UseJitter { get; init; } = true;

    /// <summary>The profile used for worktree/branch deletion in production.</summary>
    public static GitRetryOptions Default { get; } = new();
}

/// <summary>One retry about to happen, surfaced so the caller can narrate the wait in the flight log.</summary>
/// <param name="Operation">Human label for the command being retried (e.g. <c>"worktree remove"</c>).</param>
/// <param name="AttemptNumber">Zero-based index of the attempt that just failed (0 = the first try).</param>
/// <param name="RetryDelay">How long the pipeline will wait before the next attempt.</param>
/// <param name="Failure">The failed result that triggered the retry, if one was produced.</param>
public readonly record struct GitRetryAttempt(string Operation, int AttemptNumber, TimeSpan RetryDelay, ProcessResult? Failure);

/// <summary>
/// A Polly retry pipeline for the <em>transient</em> failures git's deletion commands hit — a worktree file
/// still held by an editor or antivirus scan (Windows especially), a git ref/index <c>.lock</c> left by a
/// racing git process, or a network blip while deleting the branch on <c>origin</c>. Only failures that look
/// transient (see <see cref="IsTransient"/>) are retried; a permanent failure ("use --force", "remote ref
/// does not exist") is returned on the first attempt so the caller's own handling — and the tests — stay fast.
/// </summary>
public static class GitRetry
{
    /// <summary>Carries the operation label into <c>OnRetry</c> so the retry narration can name the command.</summary>
    private static readonly ResiliencePropertyKey<string> OperationKey = new("fido.git.operation");

    /// <summary>
    /// Builds a reusable pipeline that retries transient git failures per <paramref name="options"/>. Each retry
    /// invokes <paramref name="onRetry"/> (if given) so a UI can narrate the wait. The predicate never handles
    /// exceptions, so cancellation propagates immediately rather than being retried.
    /// </summary>
    public static ResiliencePipeline<ProcessResult> BuildPipeline(GitRetryOptions options, Action<GitRetryAttempt>? onRetry = null)
    {
        return new ResiliencePipelineBuilder<ProcessResult>()
            .AddRetry(new RetryStrategyOptions<ProcessResult>
            {
                ShouldHandle = static args =>
                {
                    var result = args.Outcome.Result;
                    return new ValueTask<bool>(result is not null && !result.Success && IsTransient(result));
                },
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay = options.Delay,
                BackoffType = options.BackoffType,
                UseJitter = options.UseJitter,
                OnRetry = args =>
                {
                    if (onRetry is not null)
                    {
                        var operation = args.Context.Properties.GetValue(OperationKey, "git");
                        onRetry(new GitRetryAttempt(operation, args.AttemptNumber, args.RetryDelay, args.Outcome.Result));
                    }
                    return default;
                },
            })
            .Build();
    }

    /// <summary>
    /// Runs <paramref name="action"/> through <paramref name="pipeline"/>, tagging the flow with
    /// <paramref name="operation"/> so retries can be narrated, and threading the caller's cancellation token
    /// down to each attempt.
    /// </summary>
    public static async Task<ProcessResult> ExecuteAsync(
        ResiliencePipeline<ProcessResult> pipeline,
        string operation,
        Func<CancellationToken, Task<ProcessResult>> action,
        CancellationToken ct = default)
    {
        var context = ResilienceContextPool.Shared.Get(ct);
        context.Properties.Set(OperationKey, operation);
        try
        {
            return await pipeline.ExecuteAsync(
                static (ctx, state) => new ValueTask<ProcessResult>(state(ctx.CancellationToken)),
                context,
                action);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    /// <summary>
    /// True when a failed git result looks worth retrying — a filesystem/lock hiccup or a network blip —
    /// rather than a permanent refusal. Matches a curated set of markers in the command's stderr/stdout,
    /// case-insensitively. Always false for a successful result.
    /// </summary>
    public static bool IsTransient(ProcessResult result)
    {
        if (result.Success) return false;

        var text = result.StdErr + "\n" + result.StdOut;
        foreach (var marker in TransientMarkers)
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// Substrings that flag a retryable failure. Two families:
    /// <list type="bullet">
    /// <item>Filesystem / lock contention — a worktree file still held open (an editor, an antivirus scan), a
    /// file the OS still reports busy, or a git ref/index <c>.lock</c> left by a racing git process. On Windows
    /// a locked unlink surfaces as "Permission denied" / "Access is denied" / "being used by another process";
    /// these clear on their own, so a brief retry usually wins.</item>
    /// <item>Network — deleting the branch on <c>origin</c> over a flaky connection.</item>
    /// </list>
    /// Deliberately narrow so permanent refusals ("use --force to delete", "remote ref does not exist",
    /// "not fully merged") are <em>not</em> matched and fail fast on the first attempt. In particular, lock
    /// contention is matched by git's lock-<em>creation</em> phrasing ("cannot lock ref", "unable to create …
    /// … .lock", "another git process seems to be running") rather than a bare "<c>.lock</c>" — which would
    /// also match a permanent failure that merely <em>echoes a worktree path</em> containing ".lock" (a branch
    /// like <c>fix.lockfile-bug</c>).
    /// <para>Known over-matches, all bounded and non-fatal: a permanent remote <em>auth</em> failure — HTTP 403
    /// ("unable to access" / "could not read from remote repository") or SSH "Permission denied (publickey)" —
    /// is retried the full budget before the same report. Accepted: remote-delete failures don't roll back the
    /// local cleanup, and the cost is a couple of seconds against catching the far more common file-lock and
    /// transient-network cases.</para>
    /// </summary>
    private static readonly string[] TransientMarkers =
    [
        // Filesystem / lock contention.
        "being used by another process",
        "access is denied",
        "permission denied",
        "resource temporarily unavailable",
        "device or resource busy",
        "directory not empty",
        "cannot lock ref",
        "unable to lock",
        "unable to create",
        "could not lock",
        "another git process seems to be running",
        // Network.
        "could not resolve host",
        "couldn't resolve host",
        "connection timed out",
        "connection reset",
        "connection refused",
        "failed to connect",
        "unable to access",
        "could not read from remote repository",
        "the remote end hung up unexpectedly",
        "rpc failed",
        "early eof",
        "unexpected disconnect while reading sideband packet",
        "operation timed out",
        "temporary failure in name resolution",
        "network is unreachable",
        "no route to host",
        "ssh: connect to host",
        "the requested url returned error: 5",
        "gnutls_handshake",
        "openssl ssl_read",
        "schannel: failed",
    ];
}
