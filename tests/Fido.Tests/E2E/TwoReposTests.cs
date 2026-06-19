using Fido.Tests.Infrastructure;
using Fido.ViewModels;

namespace Fido.Tests.E2E;

/// <summary>Scenario B: two distinct repositories under the search roots.</summary>
[NotInParallel]
public class TwoReposTests
{
    [Test]
    public async Task Solution_name_disambiguates_to_a_single_repo_with_no_chooser()
    {
        using var world = new TestRepoWorld();
        var originFoo = world.CreateOrigin("Foo", "Foo");
        var originBar = world.CreateOrigin("Bar", "Bar");
        var root = world.SearchRoot("root");
        world.Clone(originFoo, root, "Foo");
        var cloneBar = world.Clone(originBar, root, "Bar");

        var rider = new FakeRiderLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("main", "Bar");
            Screenshots.Save(window, "B-two-repos-bar");

            await Assert.That(dialogs.ChooserRequests.Count).IsEqualTo(0);   // only Bar matches → no ambiguity
            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.Go);
            await Assert.That(Paths.StartsWith(rider.LastLaunch!.Value.Target, cloneBar)).IsTrue();
            await Assert.That(rider.LastLaunch!.Value.Target).EndsWith("Bar.sln");
        });
    }

    [Test]
    public async Task Branch_only_search_across_two_repos_offers_a_folder_chooser()
    {
        using var world = new TestRepoWorld();
        var originFoo = world.CreateOrigin("Foo", "Foo");
        var originBar = world.CreateOrigin("Bar", "Bar");
        var root = world.SearchRoot("root");
        var cloneFoo = world.Clone(originFoo, root, "Foo");
        var cloneBar = world.Clone(originBar, root, "Bar");
        world.CreateBranch(cloneFoo, "feature/x");
        world.CreateBranch(cloneBar, "feature/x");

        var rider = new FakeRiderLauncher();
        var dialogs = new FakeDialogService
        {
            // first chooser = which folder (pick Foo); second = which target inside it (pick the solution)
            OnChooser = request => request.Title.Contains("Open from branch folder")
                ? 0
                : request.PickTitleContaining(cloneFoo),
        };
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");   // no solution → branch-only flow
            Screenshots.Save(window, "B-branch-only");

            await Assert.That(dialogs.ChooserRequests.Count).IsEqualTo(2);   // folder chooser, then target chooser
            await Assert.That(dialogs.ChooserRequests[0].Items.Count).IsEqualTo(2);   // Foo + Bar both on feature/x
            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.Go);
            await Assert.That(Paths.StartsWith(rider.LastLaunch!.Value.Target, cloneFoo)).IsTrue();
        });
    }
}
