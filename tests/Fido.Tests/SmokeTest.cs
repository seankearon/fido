using Avalonia.Threading;
using Fido.Tests.Infrastructure;
using Fido.ViewModels;
using Fido.Views;

namespace Fido.Tests;

/// <summary>
/// End-to-end smoke: real <see cref="MainWindow"/> over the headless Skia platform, a real git clone,
/// injected fakes for Rider and dialogs, and a rendered-frame screenshot. Validates the whole harness.
/// </summary>
[NotInParallel]
public class SmokeTest
{
    [Test]
    public async Task Opens_a_single_clone_on_main_and_launches_rider()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var rootA = world.SearchRoot("rootA");
        world.Clone(origin, rootA, "Foo");

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([rootA], rider, dialogs);

        await Ui.On(async () =>
        {
            var window = new MainWindow(services);
            window.Show();
            UiTestExtensions.Pump();
            Screenshots.Save(window, "smoke-mainwindow");

            window.SetText("BranchBox", "main");
            window.SetText("SolutionBox", "Foo");
            await window.RunOpenAsync();
            UiTestExtensions.Pump();

            var vm = window.Vm();
            await Assert.That(vm.StatusKind).IsEqualTo(StatusKind.Go);
            await Assert.That(rider.Launches.Count).IsEqualTo(1);
            await Assert.That(rider.LastLaunch!.Value.Target).EndsWith("Foo.sln");

            window.Close();
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
