using System.IO;
using Avalonia.Input;
using Fido.Models;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.E2E;

/// <summary>
/// A branch whose <c>.fido/cfg.yaml</c> prefers the main clone, but which is checked out in a linked
/// worktree: git won't switch the main tree onto a branch another worktree holds, so the preference can't
/// just pick a card. Fido offers a "move to main clone" card instead — pre-selected, explained in the flight
/// log, and never acted on without the inline confirm — whose open removes the worktree (the branch stays)
/// and switches the main clone over.
/// </summary>
[NotInParallel]
public class MoveToMainCloneTests
{
    private static Editor Rider => new() { Name = "Rider", Kind = EditorKind.Rider };

    /// <summary>A clone whose branch lives in a clean worktree carrying a committed config that prefers the
    /// main clone. Returns the clone and the worktree.</summary>
    private static (string Clone, string Worktree) WorktreeOnlyBranch(TestRepoWorld world, string root)
    {
        var origin = world.CreateOrigin("Foo", "Foo");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/move");
        TestRepoWorld.CommitFidoConfig(worktree, "prefer main clone: true\n");
        return (clone, worktree);
    }

    [Test]
    public async Task A_worktree_holding_the_branch_gets_a_move_card_that_asks_before_it_acts()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var (clone, worktree) = WorktreeOnlyBranch(world, root);
        var rider = new FakeEditorLauncher();
        var services = world.BuildServices([root], rider, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/move");
            var vm = window.Vm();

            // The worktree is still found and listed; the move card follows it and is the default choice.
            await Assert.That(vm.Targets.Count).IsEqualTo(2);
            await Assert.That(vm.Targets[0].IsWorktree).IsTrue();
            await Assert.That(vm.Targets[1].IsMoveToMain).IsTrue();
            await Assert.That(vm.SelectedTarget!.IsMoveToMain).IsTrue();
            await Assert.That(vm.SelectedTarget!.Meta).Contains("main tree on 'main'");
            await Assert.That(vm.CanDelete).IsFalse();

            // The flight log says why the main clone couldn't simply be picked, and what the card does.
            var log = window.LogText();
            await Assert.That(log).Contains("✓ Found 1 location(s) for 'feature/move'.");
            await Assert.That(log).Contains("git won't switch the main clone onto a branch another worktree holds");
            await Assert.That(log).Contains("'move to main clone' card");

            // Opening only raises the confirm strip: nothing on disk moves, nothing launches.
            await window.OpenWithAsync(Rider);
            Screenshots.Save(window, "move-to-main-clone-confirm");
            await Assert.That(vm.IsConfirmingMove).IsTrue();
            await Assert.That(vm.CanConfirmMove).IsTrue();
            await Assert.That(vm.MoveConfirmLabel).IsEqualTo("Move & open in Rider");
            await Assert.That(vm.MoveConfirmCurrentBranch).IsEqualTo("main");
            await Assert.That(Directory.Exists(worktree)).IsTrue();
            await Assert.That(await services.Git.GetCurrentBranchAsync(clone)).IsEqualTo("main");
            await Assert.That(rider.Launches.Count).IsEqualTo(0);

            // Esc backs out, still touching nothing.
            window.PressKey(Key.Escape);
            await Assert.That(vm.IsConfirmingMove).IsFalse();
            await Assert.That(Directory.Exists(worktree)).IsTrue();
        });
    }

    [Test]
    public async Task Confirming_moves_the_branch_into_the_main_clone_and_opens_it()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var (clone, worktree) = WorktreeOnlyBranch(world, root);
        var rider = new FakeEditorLauncher();
        var services = world.BuildServices([root], rider, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/move");
            var vm = window.Vm();
            await window.OpenWithAsync(Rider);
            await window.ConfirmMoveAsync();

            // The worktree is gone, the branch survives it, and the main clone is on the branch.
            await Assert.That(Directory.Exists(worktree)).IsFalse();
            await Assert.That(await services.Git.LocalBranchExistsAsync(clone, "feature/move")).IsTrue();
            await Assert.That(await services.Git.GetCurrentBranchAsync(clone)).IsEqualTo("feature/move");
            await Assert.That(File.Exists(Path.Combine(clone, ".fido", "cfg.yaml"))).IsTrue();

            // The open the confirm carried on went to the main clone.
            await Assert.That(rider.Launches.Count).IsEqualTo(1);
            await Assert.That(Paths.StartsWith(rider.LastLaunch!.Value.Target, clone)).IsTrue();

            // The results say what's now on disk: one card, the main clone, selected; the strip is down.
            await Assert.That(vm.Targets.Count).IsEqualTo(1);
            await Assert.That(vm.SelectedTarget!.IsMainClone).IsTrue();
            await Assert.That(vm.IsConfirmingMove).IsFalse();
            await Assert.That(window.LogText()).Contains("Main working tree is now on 'feature/move'.");
        });
    }

    [Test]
    public async Task A_worktree_with_changes_is_never_moved_away()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var (clone, worktree) = WorktreeOnlyBranch(world, root);
        world.MakeDirty(worktree);
        var rider = new FakeEditorLauncher();
        var services = world.BuildServices([root], rider, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/move");
            var vm = window.Vm();
            await window.OpenWithAsync(Rider);

            // The strip says why, and its button is dead — even called directly, nothing happens.
            await Assert.That(vm.IsConfirmingMove).IsTrue();
            await Assert.That(vm.CanConfirmMove).IsFalse();
            await Assert.That(vm.MoveConfirmWarnings).Contains("uncommitted or untracked change(s)");
            await window.ConfirmMoveAsync();

            await Assert.That(File.Exists(Path.Combine(worktree, "uncommitted.txt"))).IsTrue();
            await Assert.That(await services.Git.GetCurrentBranchAsync(clone)).IsEqualTo("main");
            await Assert.That(rider.Launches.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Without_the_preference_no_move_is_offered()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var origin = world.CreateOrigin("Foo", "Foo");
        var clone = world.Clone(origin, root, "Foo");
        world.AddWorktree(clone, "feature/plain");
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/plain");
            var vm = window.Vm();
            await Assert.That(vm.Targets.Count).IsEqualTo(1);
            await Assert.That(vm.SelectedTarget!.IsWorktree).IsTrue();
        });
    }
}
