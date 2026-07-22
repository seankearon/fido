using Fido.Models;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.E2E;

/// <summary>
/// Scenario B: the same branch checked out in worktrees of two <em>different</em> repositories.
/// Discovery must surface both as selectable worktree cards (with each repo named in the meta line),
/// selection must decide which repo's worktree a launch targets, and the solution chips must follow
/// the selected card — each repo carries its own <c>.sln</c> name.
/// </summary>
[NotInParallel]
public class TwoReposTests
{
    [Test]
    public async Task Discovery_finds_a_worktree_in_each_repo_and_labels_them()
    {
        using var world = new TestRepoWorld();
        var originFoo = world.CreateOrigin("Foo", "Foo");
        var originBar = world.CreateOrigin("Bar", "Bar");
        var root = world.SearchRoot("root");
        var cloneFoo = world.Clone(originFoo, root, "Foo");
        var cloneBar = world.Clone(originBar, root, "Bar");
        var wtFoo = world.AddWorktree(cloneFoo, "feature/x");
        var wtBar = world.AddWorktree(cloneBar, "feature/x");

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            Screenshots.Save(window, "B-two-repos-cards");

            var vm = window.Vm();
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Found);
            await Assert.That(vm.IsLocked).IsFalse();
            await Assert.That(vm.Targets.Count).IsEqualTo(2);
            await Assert.That(vm.HasMultipleTargets).IsTrue();
            await Assert.That(vm.FoundChipText).IsEqualTo("✓ 2 locations");
            await Assert.That(window.LogText()).Contains("✓ Found 2 location(s) for 'feature/x'.");

            // both hits are linked worktrees, and each card's meta names its own repo
            var fooCard = window.CardWithPath(wtFoo);
            var barCard = window.CardWithPath(wtBar);
            await Assert.That(fooCard.IsWorktree).IsTrue();
            await Assert.That(barCard.IsWorktree).IsTrue();
            await Assert.That(fooCard.KindLabel).IsEqualTo("worktree");
            await Assert.That(barCard.KindLabel).IsEqualTo("worktree");
            await Assert.That(fooCard.Meta).Contains("Foo · 1 solution");
            await Assert.That(barCard.Meta).Contains("Bar · 1 solution");
        });
    }

    [Test]
    public async Task Selecting_a_card_launches_that_repos_worktree()
    {
        using var world = new TestRepoWorld();
        var originFoo = world.CreateOrigin("Foo", "Foo");
        var originBar = world.CreateOrigin("Bar", "Bar");
        var root = world.SearchRoot("root");
        var cloneFoo = world.Clone(originFoo, root, "Foo");
        var cloneBar = world.Clone(originBar, root, "Bar");
        var wtFoo = world.AddWorktree(cloneFoo, "feature/x");
        var wtBar = world.AddWorktree(cloneBar, "feature/x");

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            var vm = window.Vm();

            // click Bar's card → Rider gets Bar's worktree (via its own solution)
            vm.SelectedTarget = window.CardWithPath(wtBar);
            await window.OpenWithAsync(new Editor { Name = "Rider", Kind = EditorKind.Rider });
            await Assert.That(rider.Launches.Count).IsEqualTo(1);
            await Assert.That(Paths.StartsWith(rider.LastLaunch!.Value.Target, wtBar)).IsTrue();
            await Assert.That(rider.LastLaunch!.Value.Target).EndsWith("Bar.sln");

            // switch repos: Foo's card → the next launch lands in Foo's worktree instead
            vm.SelectedTarget = window.CardWithPath(wtFoo);
            await window.OpenWithAsync(new Editor { Name = "Rider", Kind = EditorKind.Rider });
            Screenshots.Save(window, "B-two-repos-selection");
            await Assert.That(rider.Launches.Count).IsEqualTo(2);
            await Assert.That(Paths.StartsWith(rider.LastLaunch!.Value.Target, wtFoo)).IsTrue();
            await Assert.That(rider.LastLaunch!.Value.Target).EndsWith("Foo.sln");
        });
    }

    [Test]
    public async Task Solution_chips_follow_the_selected_repo()
    {
        using var world = new TestRepoWorld();
        var originFoo = world.CreateOrigin("Foo", "Foo");
        var originBar = world.CreateOrigin("Bar", "Bar");
        var root = world.SearchRoot("root");
        var cloneFoo = world.Clone(originFoo, root, "Foo");
        var cloneBar = world.Clone(originBar, root, "Bar");
        var wtFoo = world.AddWorktree(cloneFoo, "feature/x");
        var wtBar = world.AddWorktree(cloneBar, "feature/x");

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            var vm = window.Vm();

            // Foo selected → Foo's solution chip leads the row (plus the trailing Folder chip)
            vm.SelectedTarget = window.CardWithPath(wtFoo);
            await Assert.That(vm.HasSolutionChips).IsTrue();
            await Assert.That(vm.SolutionChips.Count).IsEqualTo(2);   // Foo.sln + Folder
            await Assert.That(vm.SelectedSolutionChip!.Label).IsEqualTo("Foo.sln");
            await Assert.That(Paths.StartsWith(vm.SelectedSolutionChip!.SolutionPath!, wtFoo)).IsTrue();

            // switching cards rebuilds the chips for the other repo's solution
            vm.SelectedTarget = window.CardWithPath(wtBar);
            Screenshots.Save(window, "B-two-repos-chips");
            await Assert.That(vm.SolutionChips.Count).IsEqualTo(2);   // Bar.sln + Folder
            await Assert.That(vm.SelectedSolutionChip!.Label).IsEqualTo("Bar.sln");
            await Assert.That(Paths.StartsWith(vm.SelectedSolutionChip!.SolutionPath!, wtBar)).IsTrue();
        });
    }
}
