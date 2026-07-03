using System.IO;
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

    [Test]
    public async Task Removes_a_worktree_then_deletes_its_local_and_remote_branch()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");        // main tree on main
        var worktree = world.AddWorktree(clone, "feature/x"); // linked worktree on feature/x
        world.PushBranch(worktree, "feature/x");              // publish it to origin

        var git = new GitService();

        // Preconditions: branch is local, checked out in the worktree, and present on origin.
        await Assert.That(await git.LocalBranchExistsAsync(clone, "feature/x")).IsTrue();
        await Assert.That(await git.RemoteBranchExistsAsync(clone, "feature/x")).IsTrue();
        await Assert.That(Directory.Exists(worktree)).IsTrue();

        // Remove the worktree — run from the main tree so git isn't standing inside it.
        var removed = await git.WorktreeRemoveAsync(clone, worktree, force: false);
        await Assert.That(removed.Success).IsTrue();
        await Assert.That(Directory.Exists(worktree)).IsFalse();

        // With the worktree gone the branch is free to delete locally…
        var deletedLocal = await git.DeleteLocalBranchAsync(clone, "feature/x");
        await Assert.That(deletedLocal.Success).IsTrue();
        await Assert.That(await git.LocalBranchExistsAsync(clone, "feature/x")).IsFalse();

        // …and on origin.
        var deletedRemote = await git.DeleteRemoteBranchAsync(clone, "feature/x");
        await Assert.That(deletedRemote.Success).IsTrue();
        await Assert.That(await git.RemoteHasBranchAsync(clone, "feature/x")).IsFalse();
    }

    [Test]
    public async Task Detects_linked_worktrees_versus_the_main_tree()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");

        var git = new GitService();

        await Assert.That(await git.IsLinkedWorktreeAsync(clone)).IsFalse();     // the main tree
        await Assert.That(await git.IsLinkedWorktreeAsync(worktree)).IsTrue();   // a linked worktree
    }

    [Test]
    public async Task Counts_commits_that_exist_only_on_the_branch()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");   // branches off main at its HEAD

        var git = new GitService();

        // A branch at the same commit as main has nothing unique to lose.
        await Assert.That(await git.CountOrphanedCommitsAsync(clone, "feature/x")).IsEqualTo(0);

        // Two commits made only on the worktree's branch are unique to it — not on origin, not on main.
        File.WriteAllText(Path.Combine(worktree, "one.txt"), "1");
        TestRepoWorld.Git(worktree, "add", "-A");
        TestRepoWorld.Git(worktree, "commit", "-m", "local one");
        File.WriteAllText(Path.Combine(worktree, "two.txt"), "2");
        TestRepoWorld.Git(worktree, "add", "-A");
        TestRepoWorld.Git(worktree, "commit", "-m", "local two");
        await Assert.That(await git.CountOrphanedCommitsAsync(clone, "feature/x")).IsEqualTo(2);

        // Once pushed to origin they exist elsewhere, so nothing would be orphaned.
        world.PushBranch(worktree, "feature/x");
        await Assert.That(await git.CountOrphanedCommitsAsync(clone, "feature/x")).IsEqualTo(0);
    }

    [Test]
    public async Task Worktree_add_and_remove_pass_gits_long_paths_flag()
    {
        // Long paths (deep node_modules, generated output) can cross Windows' 260-char MAX_PATH; `-c
        // core.longpaths=true` lets git's own file ops handle them when adding or removing a worktree.
        var captured = new List<IReadOnlyList<string>>();
        var git = new GitService((_, args, _) =>
        {
            captured.Add(args);
            return Task.FromResult(new ProcessResult(0, "", ""));
        });

        await git.WorktreeRemoveAsync("/repo", "/repo.worktrees/x", force: false);
        await git.WorktreeRemoveAsync("/repo", "/repo.worktrees/x", force: true);
        await git.WorktreeAddExistingAsync("/repo", "/repo.worktrees/x", "feature/x");
        await git.WorktreeAddNewAsync("/repo", "/repo.worktrees/x", "feature/x", startPoint: null);

        await Assert.That(captured.Count).IsEqualTo(4);
        foreach (var args in captured)
        {
            await Assert.That(args[0]).IsEqualTo("-c");
            await Assert.That(args[1]).IsEqualTo("core.longpaths=true");
        }
    }

    [Test]
    public async Task Forced_worktree_remove_discards_uncommitted_changes()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.MakeDirty(worktree);   // an uncommitted file — a plain remove would refuse

        var git = new GitService();

        var unforced = await git.WorktreeRemoveAsync(clone, worktree, force: false);
        await Assert.That(unforced.Success).IsFalse();       // git refuses to drop a dirty worktree
        await Assert.That(Directory.Exists(worktree)).IsTrue();

        var forced = await git.WorktreeRemoveAsync(clone, worktree, force: true);
        await Assert.That(forced.Success).IsTrue();
        await Assert.That(Directory.Exists(worktree)).IsFalse();
    }
}
