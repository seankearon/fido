using Fido.Models;
using Fido.Services;
using Polly;

namespace Fido.Tests.Services;

/// <summary>
/// End-to-end wiring of the deletion retry: <see cref="OpenerService.DeleteWorktreeAsync"/> re-runs a
/// git delete step that fails transiently and gives up on one that fails permanently. Git is faked through
/// <see cref="GitService.GitCommandRunner"/> so the transient failures are scripted rather than provoked,
/// and the retry delay is zero so the tests don't wait.
/// </summary>
public class OpenerDeletionRetryTests
{
    /// <summary>Zero-delay retry so the loop runs instantly.</summary>
    private static readonly GitRetryOptions Fast = new()
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.Zero,
        UseJitter = false,
        BackoffType = DelayBackoffType.Constant,
    };

    private static WorktreeDeletion Plan() => new(
        MainWorktreePath: "/repo",
        WorktreePath: "/repo.worktrees/feature-x",
        Branch: "feature/x",
        RemoteBranchExists: true,
        OutstandingChanges: Array.Empty<string>(),
        OrphanedCommits: 0);

    private static OpenerService Opener(GitService.GitCommandRunner runner, List<string>? log = null) =>
        new(new GitService(runner), new SolutionFinder(), new WorkingTreeFinder(),
            log: log is null ? null : log.Add, deletionRetry: Fast);

    // Finds the git subcommand as a consecutive run anywhere in the args — so it spots "worktree remove" even
    // behind the leading "-c core.longpaths=true" flags that GitService now passes.
    private static bool Matches(IReadOnlyList<string> args, params string[] sub)
    {
        for (var i = 0; i + sub.Length <= args.Count; i++)
        {
            var all = true;
            for (var j = 0; j < sub.Length; j++)
                if (args[i + j] != sub[j]) { all = false; break; }
            if (all) return true;
        }
        return false;
    }

    [Test]
    public async Task Retries_a_transiently_locked_worktree_removal_then_completes_the_delete()
    {
        var removeCalls = 0;
        var log = new List<string>();
        GitService.GitCommandRunner runner = (_, args, _) =>
        {
            if (Matches(args, "worktree", "remove"))
            {
                removeCalls++;
                // Windows: a file in the worktree is still held open by an editor — clears after a moment.
                return Task.FromResult(removeCalls < 3
                    ? new ProcessResult(1, "", $"fatal: failed to delete '{args[^1]}': being used by another process")
                    : new ProcessResult(0, "", ""));
            }
            return Task.FromResult(new ProcessResult(0, "", ""));   // branch -D and push --delete succeed
        };

        var outcome = await Opener(runner, log).DeleteWorktreeAsync(Plan(), WorktreeDeletionChoice.All);

        await Assert.That(removeCalls).IsEqualTo(3);                // failed twice, third try stuck
        await Assert.That(outcome.WorktreeRemoved).IsTrue();
        await Assert.That(outcome.LocalBranchDeleted).IsTrue();
        await Assert.That(outcome.RemoteBranchDeleted).IsTrue();
        await Assert.That(outcome.RemoteDeleteFailed).IsFalse();
        await Assert.That(log.Any(l => l.Contains("worktree remove failed (transient)"))).IsTrue();
    }

    [Test]
    public async Task Retries_a_transient_remote_delete_then_reports_the_remote_gone()
    {
        var pushCalls = 0;
        GitService.GitCommandRunner runner = (_, args, _) =>
        {
            if (Matches(args, "push"))
            {
                pushCalls++;
                return Task.FromResult(pushCalls < 2
                    ? new ProcessResult(128, "", "fatal: unable to access 'https://origin/r.git/': Could not resolve host: origin")
                    : new ProcessResult(0, "", ""));
            }
            return Task.FromResult(new ProcessResult(0, "", ""));   // worktree remove and branch -D succeed
        };

        var outcome = await Opener(runner).DeleteWorktreeAsync(Plan(), WorktreeDeletionChoice.All);

        await Assert.That(pushCalls).IsEqualTo(2);                  // failed once (host blip), retried, then gone
        await Assert.That(outcome.RemoteBranchDeleted).IsTrue();
        await Assert.That(outcome.RemoteDeleteFailed).IsFalse();
    }

    [Test]
    public async Task Does_not_retry_a_permanent_worktree_removal_failure()
    {
        var removeCalls = 0;
        GitService.GitCommandRunner runner = (_, args, _) =>
        {
            if (Matches(args, "worktree", "remove"))
            {
                removeCalls++;
                return Task.FromResult(new ProcessResult(128, "",
                    "fatal: 'feature/x' contains modified or untracked files, use --force to delete it"));
            }
            return Task.FromResult(new ProcessResult(0, "", ""));
        };

        WorktreeRemovalException? thrown = null;
        try
        {
            await Opener(runner).DeleteWorktreeAsync(Plan(), WorktreeDeletionChoice.All);
        }
        catch (WorktreeRemovalException ex)
        {
            thrown = ex;   // a permanent worktree-remove failure throws so the caller can offer a force-delete
        }

        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.WorktreePath).IsEqualTo("/repo.worktrees/feature-x");
        await Assert.That(removeCalls).IsEqualTo(1);                // one attempt, no wasted retries
    }
}
