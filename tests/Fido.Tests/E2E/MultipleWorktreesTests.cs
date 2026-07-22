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
}
