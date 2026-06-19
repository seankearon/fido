using Fido.Tests.Infrastructure;
using Fido.ViewModels;

namespace Fido.Tests.E2E;

/// <summary>Scenario A: the same origin cloned twice — the app must detect both clones.</summary>
[NotInParallel]
public class TwoClonesTests
{
    [Test]
    public async Task Branch_checked_out_in_both_clones_shows_a_two_item_chooser_and_opens_the_chosen_one()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var rootA = world.SearchRoot("rootA");
        var rootB = world.SearchRoot("rootB");
        world.Clone(origin, rootA, "Foo");
        var cloneB = world.Clone(origin, rootB, "Foo");

        var rider = new FakeRiderLauncher();
        var dialogs = new FakeDialogService
        {
            OnChooser = request => request.PickTitleContaining(cloneB),   // choose the second clone
        };
        var services = world.BuildServices([rootA, rootB], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("main", "Foo");
            Screenshots.Save(window, "A-two-clones-go");

            // main is checked out in BOTH clones → exactly one chooser, with two options
            await Assert.That(dialogs.ChooserRequests.Count).IsEqualTo(1);
            await Assert.That(dialogs.LastChooser!.Items.Count).IsEqualTo(2);

            var vm = window.Vm();
            await Assert.That(vm.StatusKind).IsEqualTo(StatusKind.Go);
            await Assert.That(rider.Launches.Count).IsEqualTo(1);
            await Assert.That(Paths.StartsWith(rider.LastLaunch!.Value.Target, cloneB)).IsTrue();
            await Assert.That(rider.LastLaunch!.Value.Target).EndsWith("Foo.sln");
        });
    }

    [Test]
    public async Task Unchecked_branch_picks_a_clone_then_creates_a_worktree_there()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var rootA = world.SearchRoot("rootA");
        var rootB = world.SearchRoot("rootB");
        var cloneA = world.Clone(origin, rootA, "Foo");
        world.Clone(origin, rootB, "Foo");

        var rider = new FakeRiderLauncher();
        var dialogs = new FakeDialogService
        {
            OnChooser = request => request.PickTitleContaining(cloneA),   // open in the first clone
            OnDecision = _ => Fido.Models.OpenDecision.Worktree,
        };
        var services = world.BuildServices([rootA, rootB], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/new", "Foo");
            Screenshots.Save(window, "A-worktree-created");

            await Assert.That(dialogs.ChooserRequests.Count).IsEqualTo(1);   // pick which clone
            await Assert.That(dialogs.LastChooser!.Items.Count).IsEqualTo(2);
            await Assert.That(dialogs.DecisionRequests.Count).IsEqualTo(1);  // branch-not-checked-out decision

            var vm = window.Vm();
            await Assert.That(vm.StatusKind).IsEqualTo(StatusKind.Go);
            await Assert.That(rider.Launches.Count).IsEqualTo(1);
            // a real `git worktree add` ran under the chosen clone's sibling .worktrees folder
            await Assert.That(Paths.Contains(rider.LastLaunch!.Value.Target, "Foo.worktrees")).IsTrue();
            await Assert.That(Paths.Contains(rider.LastLaunch!.Value.Target, "feature-new")).IsTrue();
            await Assert.That(rider.LastLaunch!.Value.Target).EndsWith("Foo.sln");
        });
    }
}
