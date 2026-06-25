using Fido.Services;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.Services;

/// <summary>GitService porcelain parsing against real repositories.</summary>
public class GitServiceTests
{
    [Test]
    public async Task List_worktrees_returns_the_main_tree_first_then_linked_worktrees()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.AddWorktree(clone, "feature/x");

        var worktrees = await new GitService().ListWorktreesAsync(clone);

        await Assert.That(worktrees.Count).IsEqualTo(2);
        await Assert.That(worktrees[0].IsMain).IsTrue();
        await Assert.That(worktrees[0].Branch).IsEqualTo("main");
        await Assert.That(worktrees.Any(w => w.Branch == "feature/x")).IsTrue();
    }

    [Test]
    public async Task Reads_current_branch_and_branch_existence()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.CreateBranch(clone, "feature/x");

        var git = new GitService();

        await Assert.That(await git.GetCurrentBranchAsync(clone)).IsEqualTo("feature/x");
        await Assert.That(await git.LocalBranchExistsAsync(clone, "feature/x")).IsTrue();
        await Assert.That(await git.LocalBranchExistsAsync(clone, "nope")).IsFalse();
    }

    [Test]
    public async Task Finds_then_fetches_a_branch_that_exists_only_on_an_unfetched_remote()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");        // only main fetched at clone time
        world.PublishBranchToOrigin(origin, "feature/x");    // pushed to origin AFTER the clone

        var git = new GitService();

        // No local branch and no cached remote-tracking ref — the local-only checks miss it.
        await Assert.That(await git.LocalBranchExistsAsync(clone, "feature/x")).IsFalse();
        await Assert.That(await git.RemoteBranchExistsAsync(clone, "feature/x")).IsFalse();

        // A live query finds it on origin; a shorter lookalike must not suffix-match, nor an absent name.
        await Assert.That(await git.RemoteHasBranchAsync(clone, "feature/x")).IsTrue();
        await Assert.That(await git.RemoteHasBranchAsync(clone, "x")).IsFalse();
        await Assert.That(await git.RemoteHasBranchAsync(clone, "nope")).IsFalse();

        // Fetching populates the remote-tracking ref that switch/worktree rely on.
        var fetch = await git.FetchBranchAsync(clone, "feature/x");
        await Assert.That(fetch.Success).IsTrue();
        await Assert.That(await git.RemoteBranchExistsAsync(clone, "feature/x")).IsTrue();
    }
}
