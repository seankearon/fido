using System;
using System.IO;
using System.Linq;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.Services;

/// <summary>
/// The force-delete fallback used when <c>git worktree remove</c> can't remove the folder (typically a path too
/// long for the OS): <see cref="OpenerService.ForceDeleteWorktreeAsync"/> deletes the folder straight from disk,
/// prunes git's dangling registration, and finishes the ticked branch deletions. Exercised against a real
/// on-disk git world.
/// </summary>
public class OpenerForceDeleteTests
{
    [Test]
    public async Task Force_delete_removes_the_folder_prunes_and_deletes_the_branches()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.PushBranch(worktree, "feature/x");

        var git = new GitService();
        var opener = new OpenerService(git, new SolutionFinder(), new WorkingTreeFinder());
        var plan = new WorktreeDeletion(
            MainWorktreePath: clone,
            WorktreePath: Path.GetFullPath(worktree),
            Branch: "feature/x",
            RemoteBranchExists: true,
            OutstandingChanges: Array.Empty<string>(),
            OrphanedCommits: 0);

        var outcome = await opener.ForceDeleteWorktreeAsync(plan, WorktreeDeletionChoice.All);

        // The folder is gone, and the whole delete completed off the back of it.
        await Assert.That(Directory.Exists(worktree)).IsFalse();
        await Assert.That(outcome.WorktreeRemoved).IsTrue();
        await Assert.That(outcome.LocalBranchDeleted).IsTrue();
        await Assert.That(outcome.RemoteBranchDeleted).IsTrue();
        await Assert.That(await git.LocalBranchExistsAsync(clone, "feature/x")).IsFalse();
        await Assert.That(await git.RemoteHasBranchAsync(clone, "feature/x")).IsFalse();

        // Prune cleared the dangling registration — the branch no longer appears as a worktree.
        var worktrees = await git.ListWorktreesAsync(clone);
        await Assert.That(worktrees.Any(w => w.Branch == "feature/x")).IsFalse();
    }

    [Test]
    public async Task Force_delete_of_a_dirty_worktree_honours_keeping_the_local_branch()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.MakeDirty(worktree);   // uncommitted work — the folder delete removes it regardless

        var git = new GitService();
        var opener = new OpenerService(git, new SolutionFinder(), new WorkingTreeFinder());
        var plan = new WorktreeDeletion(clone, Path.GetFullPath(worktree), "feature/x",
            RemoteBranchExists: false, OutstandingChanges: new[] { "?? uncommitted.txt" }, OrphanedCommits: 0);

        // Only the worktree ticked — the local branch should be kept.
        var outcome = await opener.ForceDeleteWorktreeAsync(
            plan, new WorktreeDeletionChoice(Worktree: true, LocalBranch: false, RemoteBranch: false));

        await Assert.That(Directory.Exists(worktree)).IsFalse();
        await Assert.That(outcome.WorktreeRemoved).IsTrue();
        await Assert.That(outcome.LocalBranchDeleted).IsFalse();
        await Assert.That(await git.LocalBranchExistsAsync(clone, "feature/x")).IsTrue();   // kept, as chosen
    }
}
