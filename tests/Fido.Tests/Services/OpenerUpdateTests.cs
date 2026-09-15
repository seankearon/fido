using System.IO;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;
using Polly;

namespace Fido.Tests.Services;

/// <summary>
/// <see cref="OpenerService.UpdateBeforeRunAsync"/> against real repositories: what it fast-forwards, what it
/// leaves alone, and what it refuses to do on the user's behalf. The whole point is that it's advisory — it
/// reports and carries on rather than throwing — so every case here asserts a status, never an exception.
/// </summary>
public class OpenerUpdateTests
{
    /// <summary>Zero-delay retry so a scripted transient failure doesn't make the test wait.</summary>
    private static readonly GitRetryOptions Fast = new()
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.Zero,
        UseJitter = false,
        BackoffType = DelayBackoffType.Constant,
    };

    private static OpenerService Opener(List<string>? log = null, GitService.GitCommandRunner? runner = null) =>
        new(runner is null ? new GitService() : new GitService(runner),
            new SolutionFinder(), new WorkingTreeFinder(),
            log: log is null ? null : log.Add, deletionRetry: Fast);

    [Test]
    public async Task Fast_forwards_a_worktree_that_has_fallen_behind_its_upstream()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.CreateBranch(clone, "feature/x");
        world.PushBranch(clone, "feature/x");
        TestRepoWorld.Git(clone, "switch", "main");   // free the branch for a worktree
        var worktree = world.AddWorktreeExisting(clone, "feature/x");

        // A teammate pushes to the branch after the worktree was checked out.
        var landed = world.CommitToOrigin(origin, "feature/x");
        await Assert.That(File.Exists(Path.Combine(worktree, landed))).IsFalse();

        var log = new List<string>();
        var update = await Opener(log).UpdateBeforeRunAsync(worktree);

        await Assert.That(update.Status).IsEqualTo(WorktreeUpdateStatus.UpToDate);
        await Assert.That(update.Upstream).IsEqualTo("origin/feature/x");
        // The commit is actually on disk — the command that runs next sees what origin has.
        await Assert.That(File.Exists(Path.Combine(worktree, landed))).IsTrue();
        await Assert.That(string.Join('\n', log)).Contains("up to date with origin/feature/x");
    }

    [Test]
    public async Task A_tree_already_level_with_its_upstream_is_up_to_date()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");

        var update = await Opener().UpdateBeforeRunAsync(clone);

        await Assert.That(update.Status).IsEqualTo(WorktreeUpdateStatus.UpToDate);
        await Assert.That(update.Upstream).IsEqualTo("origin/main");
    }

    [Test]
    public async Task A_branch_that_tracks_nothing_is_skipped_rather_than_failed()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/local-only");   // new branch, never pushed

        var log = new List<string>();
        var update = await Opener(log).UpdateBeforeRunAsync(worktree);

        // Nothing to pull is the end state the user wanted, not a failure worth warning about.
        await Assert.That(update.Status).IsEqualTo(WorktreeUpdateStatus.Skipped);
        await Assert.That(string.Join('\n', log)).Contains("'feature/local-only' tracks nothing");
    }

    [Test]
    public async Task A_detached_head_is_skipped()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        TestRepoWorld.Git(clone, "checkout", "--detach");

        var log = new List<string>();
        var update = await Opener(log).UpdateBeforeRunAsync(clone);

        await Assert.That(update.Status).IsEqualTo(WorktreeUpdateStatus.Skipped);
        await Assert.That(string.Join('\n', log)).Contains("Detached HEAD");
    }

    [Test]
    public async Task A_diverged_branch_is_reported_and_left_exactly_as_it_was()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");

        world.CommitFile(clone, "mine.txt");              // a local commit …
        world.CommitToOrigin(origin, "main");             // … and a different one on origin
        var head = await new GitService().GetHeadShaAsync(clone);

        var log = new List<string>();
        var update = await Opener(log).UpdateBeforeRunAsync(clone);

        // --ff-only refuses: reconciling a divergence is the user's call, never Fido's.
        await Assert.That(update.Status).IsEqualTo(WorktreeUpdateStatus.Failed);
        await Assert.That(await new GitService().GetHeadShaAsync(clone)).IsEqualTo(head);
        await Assert.That(File.Exists(Path.Combine(clone, "mine.txt"))).IsTrue();
        await Assert.That(string.Join('\n', log)).Contains("Couldn't fast-forward");
        await Assert.That(string.Join('\n', log)).Contains("Running against the tree as it stands");
    }

    [Test]
    public async Task Local_changes_in_the_way_stop_the_pull_without_stopping_the_run()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");

        // origin changes a file the user is also editing: git refuses to overwrite the local edit.
        world.CommitToOrigin(origin, "main", "contested.txt");
        File.WriteAllText(Path.Combine(clone, "contested.txt"), "my uncommitted work");

        var update = await Opener().UpdateBeforeRunAsync(clone);

        await Assert.That(update.Status).IsEqualTo(WorktreeUpdateStatus.Failed);
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(clone, "contested.txt")))
            .IsEqualTo("my uncommitted work");
    }

    [Test]
    public async Task A_dirty_tree_still_fast_forwards_when_the_local_edits_are_not_in_the_way()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");

        var landed = world.CommitToOrigin(origin, "main");
        File.WriteAllText(Path.Combine(clone, "scratch.txt"), "unrelated work in progress");

        var update = await Opener().UpdateBeforeRunAsync(clone);

        // Fido doesn't pre-empt git with a cleanliness check — git alone decides what's in the way.
        await Assert.That(update.Status).IsEqualTo(WorktreeUpdateStatus.UpToDate);
        await Assert.That(File.Exists(Path.Combine(clone, landed))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(clone, "scratch.txt"))).IsTrue();
    }

    [Test]
    public async Task A_transient_network_failure_is_retried_once_and_then_succeeds()
    {
        var pulls = 0;
        var log = new List<string>();
        GitService.GitCommandRunner runner = (_, args, _) =>
        {
            if (args.Count > 0 && args[0] == "pull")
            {
                pulls++;
                return Task.FromResult(pulls == 1
                    ? new ProcessResult(1, "", "fatal: unable to access 'origin': Could not resolve host: github.com")
                    : new ProcessResult(0, "Updating 1234abc..5678def\nFast-forward\n", ""));
            }
            if (args.Contains("@{u}")) return Task.FromResult(new ProcessResult(0, "origin/feature/x\n", ""));
            return Task.FromResult(new ProcessResult(0, "feature/x\n", ""));   // rev-parse --abbrev-ref HEAD
        };

        var update = await Opener(log, runner).UpdateBeforeRunAsync("/repo");

        await Assert.That(pulls).IsEqualTo(2);
        await Assert.That(update.Status).IsEqualTo(WorktreeUpdateStatus.UpToDate);
        await Assert.That(string.Join('\n', log)).Contains("pull failed (transient)");
    }

    [Test]
    public async Task An_unreachable_origin_gives_up_quickly_rather_than_retrying_to_exhaustion()
    {
        var pulls = 0;
        GitService.GitCommandRunner runner = (_, args, _) =>
        {
            if (args.Count > 0 && args[0] == "pull")
            {
                pulls++;
                return Task.FromResult(new ProcessResult(1, "", "fatal: Could not resolve host: github.com"));
            }
            if (args.Contains("@{u}")) return Task.FromResult(new ProcessResult(0, "origin/feature/x\n", ""));
            return Task.FromResult(new ProcessResult(0, "feature/x\n", ""));
        };

        var update = await Opener(runner: runner).UpdateBeforeRunAsync("/repo");

        // One retry, not the deletion profile's three: the user is waiting on a console.
        await Assert.That(pulls).IsEqualTo(2);
        await Assert.That(update.Status).IsEqualTo(WorktreeUpdateStatus.Failed);
    }
}
