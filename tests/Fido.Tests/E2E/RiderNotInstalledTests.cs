using Fido.Models;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.E2E;

/// <summary>Scenario D: the app must behave gracefully on a machine with no Rider installed.</summary>
[NotInParallel]
public class RiderNotInstalledTests
{
    /// <summary>
    /// Discovery finds the branch, but opening with Rider on a Rider-less machine must not launch
    /// anything or crash: it logs a pointer to Settings and leaves the found state fully usable.
    /// </summary>
    [Test]
    public async Task A_found_branch_with_no_rider_logs_guidance_and_never_launches()
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
            await window.Discover("main");
            var vm = window.Vm();
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Found);

            await window.OpenWithAsync(new Editor { Name = "Rider", Kind = EditorKind.Rider });
            Screenshots.Save(window, "D-rider-not-installed");

            await Assert.That(rider.Launches.Count).IsEqualTo(0);   // discovery succeeded, but nothing is launched
            await Assert.That(window.LogText()).Contains("Rider not located — set its path in Settings");
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Found);   // the miss doesn't disturb the phase machine
            await Assert.That(vm.CanOpen).IsTrue();
        });
    }

    /// <summary>
    /// The "not located" miss is recoverable in-session: once the launcher can locate Rider
    /// (the user set its path in Settings), the very next open launches for real.
    /// </summary>
    [Test]
    public async Task Setting_the_rider_path_lets_a_subsequent_open_succeed()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var rider = new FakeEditorLauncher { LocateResult = null };   // Rider not installed (yet)
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");
            var editor = new Editor { Name = "Rider", Kind = EditorKind.Rider };

            await window.OpenWithAsync(editor);
            await Assert.That(rider.Launches.Count).IsEqualTo(0);

            rider.LocateResult = "/fake/rider/bin/rider";   // user fixes the path in Settings
            await window.OpenWithAsync(editor);

            await Assert.That(rider.Launches.Count).IsEqualTo(1);
            await Assert.That(rider.Launches[0].Executable).IsEqualTo("/fake/rider/bin/rider");
            await Assert.That(rider.Launches[0].Target).Contains("Foo.sln");   // Rider honours the selected solution chip
            await Assert.That(window.LogText()).Contains("The Eagle has landed");
        });
    }
}
