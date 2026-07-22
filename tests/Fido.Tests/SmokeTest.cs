using Fido.Models;
using Fido.Tests.Infrastructure;
using Fido.Views;

namespace Fido.Tests;

/// <summary>
/// End-to-end smoke: real <see cref="MainWindow"/> over the headless Skia platform, a real git clone,
/// injected fakes for Rider and dialogs, and a rendered-frame screenshot. Validates the whole harness:
/// discovery lands on Found with an inline target card, and opening with Rider launches the solution.
/// </summary>
[NotInParallel]
public class SmokeTest
{
    [Test]
    public async Task Discovers_a_single_clone_on_main_and_launches_rider()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var rootA = world.SearchRoot("rootA");
        world.Clone(origin, rootA, "Foo");

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([rootA], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            Screenshots.Save(window, "smoke-mainwindow");

            await window.Discover("main", "Foo");

            var vm = window.Vm();
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Found);
            await Assert.That(vm.Targets.Count).IsEqualTo(1);
            await Assert.That(vm.SelectedTarget).IsNotNull();
            await Assert.That(window.LogText()).Contains("✓ Found 1 location(s) for 'main'.");

            await window.OpenWithAsync(new Editor { Name = "Rider", Kind = EditorKind.Rider });

            await Assert.That(rider.Launches.Count).IsEqualTo(1);
            await Assert.That(rider.LastLaunch!.Value.Target).EndsWith("Foo.sln");
            await Assert.That(window.LogText()).Contains("Fido? GO!");
        });
    }

    [Test]
    public async Task Production_default_wiring_constructs_and_shows()
    {
        // The parameterless ctor reproduces today's real wiring (FidoServices.CreateDefault +
        // AvaloniaDialogService). This guards that the test seams didn't break the shipping path.
        await Ui.On(async () =>
        {
            var window = new MainWindow();
            window.Show();
            UiTestExtensions.Pump();

            await Assert.That(window.DataContext).IsNotNull();
            await Assert.That(window.IsVisible).IsTrue();

            window.Close();
        });
    }
}
