using Fido.Models;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.E2E;

/// <summary>
/// Scenario A: the same origin cloned twice, with the scanned branch checked out in both — discovery
/// must list both clones as inline selectable cards (no modal chooser exists any more), auto-select
/// the first, and open from whichever card the user picks. Both results are main clones, so the
/// delete action stays locked with the "main clone" explanation.
/// </summary>
[NotInParallel]
public class TwoClonesTests
{
    [Test]
    public async Task Branch_in_both_clones_shows_two_selectable_cards_with_delete_locked()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var rootA = world.SearchRoot("rootA");
        var rootB = world.SearchRoot("rootB");
        var cloneA = world.Clone(origin, rootA, "Foo");
        var cloneB = world.Clone(origin, rootB, "Foo");
        world.CreateBranch(cloneA, "feature/shared");   // same branch checked out in BOTH clones
        world.CreateBranch(cloneB, "feature/shared");

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([rootA, rootB], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/shared");
            Screenshots.Save(window, "A-two-clones-found");

            var vm = window.Vm();
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Found);
            await Assert.That(vm.Targets.Count).IsEqualTo(2);
            await Assert.That(vm.HasMultipleTargets).IsTrue();
            await Assert.That(vm.FoundChipText).IsEqualTo("✓ 2 locations");
            await Assert.That(window.LogText()).Contains("✓ Found 2 location(s) for 'feature/shared'.");

            // The first card is auto-selected, and both results are main clones.
            await Assert.That(vm.SelectedTarget).IsEqualTo(vm.Targets[0]);
            await Assert.That(vm.Targets[0].IsMainClone).IsTrue();
            await Assert.That(vm.Targets[1].IsMainClone).IsTrue();
            await Assert.That(vm.SelectedKindLabel).IsEqualTo("main clone");

            // Main clones are never deletable — the button is disabled with the main-clone note.
            await Assert.That(vm.CanDelete).IsFalse();
            await Assert.That(vm.ShowDeleteDisabledNote).IsTrue();
            await Assert.That(vm.DeleteDisabledNote).Contains("main clone");

            // The whole flow is inline: nothing launched, no dialog of any kind appeared.
            await Assert.That(rider.Launches.Count).IsEqualTo(0);
            await Assert.That(dialogs.ForceDeleteConfirmations.Count).IsEqualTo(0);
            await Assert.That(dialogs.SettingsShownCount).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Selecting_the_other_clone_card_opens_rider_from_that_clone()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var rootA = world.SearchRoot("rootA");
        var rootB = world.SearchRoot("rootB");
        var cloneA = world.Clone(origin, rootA, "Foo");
        var cloneB = world.Clone(origin, rootB, "Foo");
        world.CreateBranch(cloneA, "feature/shared");
        world.CreateBranch(cloneB, "feature/shared");

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([rootA, rootB], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/shared");

            // The first root's clone is auto-selected; click the OTHER card instead.
            var vm = window.Vm();
            await Assert.That(Paths.StartsWith(vm.SelectedPath, rootA)).IsTrue();
            vm.SelectedTarget = window.CardWithPath(cloneB);
            UiTestExtensions.Pump();
            Screenshots.Save(window, "A-two-clones-open-other");

            await window.OpenWithAsync(new Editor { Name = "Rider", Kind = EditorKind.Rider });

            // Rider opens the selected solution chip of the CHOSEN clone — no chooser dialog involved.
            await Assert.That(rider.Launches.Count).IsEqualTo(1);
            await Assert.That(Paths.StartsWith(rider.LastLaunch!.Value.Target, cloneB)).IsTrue();
            await Assert.That(rider.LastLaunch!.Value.Target).EndsWith("Foo.sln");
            await Assert.That(dialogs.ForceDeleteConfirmations.Count).IsEqualTo(0);
        });
    }
}
