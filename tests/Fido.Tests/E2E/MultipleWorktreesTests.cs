using Fido.Tests.Infrastructure;
using Fido.ViewModels;

namespace Fido.Tests.E2E;

/// <summary>Scenario C: one clone plus several linked worktrees — the right worktree must be chosen.</summary>
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
            await window.Open(branch, "Foo");
            Screenshots.Save(window, $"C-worktree-{expectedSegment}");

            // exactly one worktree holds this branch → no chooser, and it's the right one
            await Assert.That(dialogs.ChooserRequests.Count).IsEqualTo(0);
            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.Go);

            var target = rider.LastLaunch!.Value.Target;
            await Assert.That(Paths.Contains(target, "Foo.worktrees")).IsTrue();
            await Assert.That(Paths.Contains(target, expectedSegment)).IsTrue();
            await Assert.That(Paths.Contains(target, otherSegment)).IsFalse();
            await Assert.That(target).EndsWith("Foo.sln");
        });
    }
}
