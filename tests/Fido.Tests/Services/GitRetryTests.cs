using Fido.Services;
using Polly;

namespace Fido.Tests.Services;

/// <summary>
/// The retry policy behind reliable worktree/branch deletion: which git failures count as transient (worth a
/// retry) versus permanent (fail fast), and that the pipeline actually re-runs the flaky ones and gives up
/// after the configured budget. All in-memory — no git, no waiting (the delay is dialled to zero).
/// </summary>
public class GitRetryTests
{
    /// <summary>Zero-delay so the retry loop runs instantly under test.</summary>
    private static readonly GitRetryOptions Fast = new()
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.Zero,
        UseJitter = false,
        BackoffType = DelayBackoffType.Constant,
    };

    private static ProcessResult Fail(string stderr) => new(1, "", stderr);
    private static ProcessResult Ok() => new(0, "", "");

    // --- IsTransient: the transient/permanent classification -----------------------------

    [Test]
    [Arguments("fatal: could not remove worktree: 'a.txt': being used by another process")]
    [Arguments("error: unable to unlink old 'src/x': Permission denied")]
    [Arguments("error: unable to delete 'x': Access is denied")]
    [Arguments("fatal: could not remove worktree directory '/repo.worktrees/feature-x': Directory not empty")]
    [Arguments("error: unable to unlink old 'src/x': Device or resource busy")]
    [Arguments("fatal: Unable to create '/repo/.git/worktrees/x/HEAD.lock': File exists")]
    [Arguments("fatal: Unable to create '/repo/.git/index.lock': File exists.\n\nAnother git process seems to be running in this repository")]
    [Arguments("error: cannot lock ref 'refs/heads/feature/x': is at 0000 but expected 1111")]
    [Arguments("fatal: unable to access 'https://github.com/o/r.git/': Could not resolve host: github.com")]
    [Arguments("fatal: unable to access 'https://github.com/o/r.git/': The requested URL returned error: 503")]
    [Arguments("ssh: connect to host github.com port 22: Connection timed out")]
    [Arguments("error: RPC failed; curl 56 Recv failure: Connection reset by peer")]
    [Arguments("fatal: the remote end hung up unexpectedly")]
    [Arguments("error: RPC failed; curl 92 HTTP/2 stream 5 was reset\nfatal: unexpected disconnect while reading sideband packet")]
    public async Task Classifies_lock_and_network_failures_as_transient(string stderr)
        => await Assert.That(GitRetry.IsTransient(Fail(stderr))).IsTrue();

    [Test]
    [Arguments("fatal: 'feature/x' contains modified or untracked files, use --force to delete it")]
    // The permanent "use --force" failure echoes the worktree path; a branch whose name contains ".lock"
    // must NOT be misread as lock contention (regression guard for the over-broad bare ".lock" marker).
    [Arguments("fatal: '/repo.worktrees/fix.lockfile-bug' contains modified or untracked files, use --force to delete it")]
    [Arguments("error: unable to delete 'feature/x': remote ref does not exist\nerror: failed to push some refs to 'origin'")]
    [Arguments("error: The branch 'feature/x' is not fully merged.")]
    [Arguments("error: branch 'feature/x' not found.")]
    public async Task Classifies_permanent_refusals_as_not_transient(string stderr)
        => await Assert.That(GitRetry.IsTransient(Fail(stderr))).IsFalse();

    [Test]
    public async Task A_successful_result_is_never_transient()
        => await Assert.That(GitRetry.IsTransient(Ok())).IsFalse();

    // --- Pipeline behaviour ---------------------------------------------------------------

    [Test]
    public async Task Retries_a_transient_failure_until_it_succeeds()
    {
        var attempts = new List<GitRetryAttempt>();
        var pipeline = GitRetry.BuildPipeline(Fast, attempts.Add);

        var calls = 0;
        var result = await GitRetry.ExecuteAsync(pipeline, "worktree remove", _ =>
        {
            calls++;
            return Task.FromResult(calls < 3 ? Fail("being used by another process") : Ok());
        });

        await Assert.That(result.Success).IsTrue();
        await Assert.That(calls).IsEqualTo(3);                 // failed twice, then succeeded

        // Two retries, each carrying the operation label and a 0-based index of the attempt that failed.
        await Assert.That(attempts.Count).IsEqualTo(2);
        await Assert.That(attempts[0].Operation).IsEqualTo("worktree remove");
        await Assert.That(attempts[0].AttemptNumber).IsEqualTo(0);
        await Assert.That(attempts[1].AttemptNumber).IsEqualTo(1);
    }

    [Test]
    public async Task Does_not_retry_a_permanent_failure()
    {
        var attempts = new List<GitRetryAttempt>();
        var pipeline = GitRetry.BuildPipeline(Fast, attempts.Add);

        var calls = 0;
        var result = await GitRetry.ExecuteAsync(pipeline, "worktree remove", _ =>
        {
            calls++;
            return Task.FromResult(Fail("use --force to delete it"));
        });

        await Assert.That(result.Success).IsFalse();
        await Assert.That(calls).IsEqualTo(1);                 // one attempt, no retries
        await Assert.That(attempts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Gives_up_after_the_retry_budget_on_a_persistent_transient_failure()
    {
        var pipeline = GitRetry.BuildPipeline(Fast);

        var calls = 0;
        var result = await GitRetry.ExecuteAsync(pipeline, "remote branch delete", _ =>
        {
            calls++;
            return Task.FromResult(Fail("fatal: unable to access 'https://o/r': Could not resolve host: o"));
        });

        await Assert.That(result.Success).IsFalse();           // the final failure is returned, not thrown
        await Assert.That(calls).IsEqualTo(4);                 // first try + 3 retries
    }

    [Test]
    public async Task Runs_a_first_time_success_exactly_once()
    {
        var attempts = new List<GitRetryAttempt>();
        var pipeline = GitRetry.BuildPipeline(Fast, attempts.Add);

        var calls = 0;
        var result = await GitRetry.ExecuteAsync(pipeline, "local branch delete", _ =>
        {
            calls++;
            return Task.FromResult(Ok());
        });

        await Assert.That(result.Success).IsTrue();
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(attempts.Count).IsEqualTo(0);
    }
}
