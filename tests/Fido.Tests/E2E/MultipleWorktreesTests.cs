using Fido.Models;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.E2E;

/// <summary>Scenario C: one clone plus several linked worktrees — discovery must surface only the right one.</summary>
[NotInParallel]
public class MultipleWorktreesTests
{
    [Test]
    [Arguments("feature/x", "feature-x", "feature-y")]
    [Arguments("feature/y", "feature-y", "feature-x")]
    public async Task Branch_resolves_to_its_own_linked_worktree(string branch, string expectedSegment, string otherSegment)
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");   // main working tree on main
        world.AddWorktree(clone, "feature/x");
        world.AddWorktree(clone, "feature/y");

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover(branch);
            Screenshots.Save(window, $"C-worktree-{expectedSegment}");

            // exactly one location holds this branch → a single card, and it's the right worktree
            var vm = window.Vm();
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Found);
            await Assert.That(vm.Targets.Count).IsEqualTo(1);

            var card = vm.Targets[0];
            await Assert.That(card.IsWorktree).IsTrue();
            await Assert.That(card.KindLabel).IsEqualTo("worktree");
            await Assert.That(Paths.Contains(card.Path, "Foo.worktrees")).IsTrue();
            await Assert.That(Paths.Contains(card.Path, expectedSegment)).IsTrue();
            await Assert.That(Paths.Contains(card.Path, otherSegment)).IsFalse();

            // the sibling worktree's path never leaks into the results
            foreach (var target in vm.Targets)
                await Assert.That(Paths.Contains(target.Path, otherSegment)).IsFalse();

            // unlocked, with the worktree auto-selected and Foo.sln as the leading solution chip
            await Assert.That(vm.CanOpen).IsTrue();
            await Assert.That(vm.SelectedTarget).IsEqualTo(card);
            await Assert.That(vm.SelectedSolutionChip!.SolutionPath!).EndsWith("Foo.sln");

            // Rider opens solutions → the launch target is that worktree's Foo.sln
            await window.OpenWithAsync(new Editor { Name = "Rider", Kind = EditorKind.Rider });
            var launched = rider.LastLaunch!.Value.Target;
            await Assert.That(Paths.Contains(launched, "Foo.worktrees")).IsTrue();
            await Assert.That(Paths.Contains(launched, expectedSegment)).IsTrue();
            await Assert.That(Paths.Contains(launched, otherSegment)).IsFalse();
            await Assert.That(launched).EndsWith("Foo.sln");
        });
    }

    /// <summary>
    /// Regression: a worktree that lives <em>outside</em> the search roots (a central worktree directory,
    /// a tree nested past the scan depth, or one inside the main clone) is still detected — via
    /// <c>git worktree list</c> — and offered as an openable worktree, rather than slipping through to a
    /// "new worktree" placement card that git would reject because the branch is already checked out.
    /// </summary>
    [Test]
    public async Task A_worktree_outside_the_search_roots_is_offered_to_open_not_recreated()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var external = world.AddExternalWorktree(clone, "feature/x");   // lives outside the search root

        var rider = new FakeEditorLauncher();
        var services = world.BuildServices([root], rider, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            var vm = window.Vm();

            // One card, and it's the existing worktree — not a placement offer.
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Found);
            await Assert.That(vm.Targets.Count).IsEqualTo(1);
            await Assert.That(vm.Targets[0].IsWorktree).IsTrue();
            await Assert.That(vm.Targets[0].IsNewWorktree).IsFalse();
            await Assert.That(Paths.StartsWith(vm.SelectedPath, external)).IsTrue();
            await Assert.That(vm.CanDelete).IsTrue();   // a real linked worktree, so deletable

            // Opening launches straight into the existing worktree — no re-add, no error.
            await window.OpenWithAsync(new Editor { Name = "Rider", Kind = EditorKind.Rider });
            await Assert.That(rider.Launches.Count).IsEqualTo(1);
            await Assert.That(Paths.StartsWith(rider.LastLaunch!.Value.Target, external)).IsTrue();
            await Assert.That(rider.LastLaunch!.Value.Target).EndsWith("Foo.sln");
            await Assert.That(window.LogText().Contains("worktree add failed")).IsFalse();
        });
    }
}
