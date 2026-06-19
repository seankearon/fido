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
}
