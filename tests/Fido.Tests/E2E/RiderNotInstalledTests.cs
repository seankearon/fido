using Fido.Tests.Infrastructure;
using Fido.ViewModels;

namespace Fido.Tests.E2E;

/// <summary>Scenario D: the app must behave gracefully on a machine with no Rider installed.</summary>
[NotInParallel]
public class RiderNotInstalledTests
{
    [Test]
    public async Task A_successful_resolution_with_no_rider_is_no_go_and_never_launches()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var rider = new FakeEditorLauncher { LocateResult = null };   // Rider not installed
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("main", "Foo");
            Screenshots.Save(window, "D-rider-not-installed");

            var vm = window.Vm();
            await Assert.That(vm.StatusKind).IsEqualTo(StatusKind.NoGo);
            await Assert.That(vm.StatusText).Contains("Rider not found");
            await Assert.That(window.LogText()).Contains("Rider not located");
            await Assert.That(rider.Launches.Count).IsEqualTo(0);   // resolution succeeded, but nothing is launched
        });
    }
}
