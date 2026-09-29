using System.IO;
using Avalonia.Controls;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;
using Fido.Views;

namespace Fido.Tests.E2E;

/// <summary>
/// A later <c>fido &lt;branch&gt;</c> for a branch a window already has goes to that window instead of opening a
/// second one. Pins what the window does with the command line it's offered — takes it only for its own branch,
/// comes to the front, and runs a command line that asks for more than the branch — and, over a real pipe, that
/// an open window is found and a closed one isn't.
/// </summary>
[NotInParallel]
public class WindowHandoffTests
{
    private static StartupCommand Command(params string[] args) => StartupCommand.Parse(args);

    [Test]
    public async Task A_bare_branch_brings_its_window_forward_and_leaves_the_screen_as_it_is()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            var vm = window.Vm();
            await window.Discover("main");
            var selected = vm.SelectedTarget;

            window.WindowState = WindowState.Maximized;
            window.WindowState = WindowState.Minimized;
            UiTestExtensions.Pump();

            await Assert.That(window.TakeHandoff(Command("main"))).IsTrue();
            UiTestExtensions.Pump();

            // Back as it stood before it was minimised, and in front.
            await Assert.That(window.WindowState).IsEqualTo(WindowState.Maximized);
            await Assert.That(window.IsActive).IsTrue();

            // Nothing re-run: the same results, the same pick, nothing opened — and the log says why it came up.
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Found);
            await Assert.That(vm.SelectedTarget).IsSameReferenceAs(selected);
            await Assert.That(launcher.Launches.Count).IsEqualTo(0);
            await Assert.That(window.LogText()).Contains("Called up for 'main' again");
        });
    }

    [Test]
    public async Task A_window_declines_a_branch_that_is_not_the_one_it_has()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            // An empty window has no branch to match.
            await Assert.That(window.TakeHandoff(Command("main"))).IsFalse();

            await window.Discover("main");

            await Assert.That(window.TakeHandoff(Command("feature/other"))).IsFalse();
            await Assert.That(window.TakeHandoff(Command("Main"))).IsFalse();   // git branch names are case-sensitive
            await Assert.That(window.TakeHandoff(Command("-s", "Foo"))).IsFalse();   // no branch at all
            await Assert.That(window.Vm().BranchName).IsEqualTo("main");
            await Assert.That(window.LogText()).DoesNotContain("Called up");
        });
    }

    [Test]
    public async Task A_named_tool_rescans_the_branch_and_auto_opens_its_single_location()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");   // exactly one location for 'main'

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService(),
            closeAfterOpen: CloseAfterOpen.Never);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");
            await Assert.That(launcher.Launches.Count).IsEqualTo(0);

            // `fido main zed` with this window open: it runs here, just as a fresh launch would have.
            await Assert.That(window.TakeHandoff(Command("main", "zed"))).IsTrue();
            await window.StartupScan;

            await Assert.That(launcher.Launches.Count).IsEqualTo(1);
            await Assert.That(launcher.LastLaunch!.Value.Editor.Kind).IsEqualTo(EditorKind.Zed);
            await Assert.That(window.Vm().HeroLabel).IsEqualTo("Open in Zed");   // the run's default now
        });
    }

    [Test]
    public async Task Being_called_up_calls_off_a_running_auto_close()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService(),
            closeAfterOpen: CloseAfterOpen.Always, closeAfterOpenDelaySeconds: 30);

        await Harness.WithWindow(services, async window =>
        {
            var vm = window.Vm();
            await window.Discover("main");
            await window.OpenWithAsync(new Editor { Name = "Rider", Kind = EditorKind.Rider });
            await Assert.That(vm.IsClosingCountdown).IsTrue();

            await Assert.That(window.TakeHandoff(Command("main"))).IsTrue();

            await Assert.That(vm.IsClosingCountdown).IsFalse();
        });
    }

    [Test]
    public async Task Over_a_real_pipe_an_open_window_is_found_and_a_closed_one_is_not()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var handoff = new InstanceHandoff(Path.Combine(world.Root, "instances"));
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService(),
            instances: handoff);

        await Ui.On(async () =>
        {
            var window = new MainWindow(services);
            window.Show();
            UiTestExtensions.Pump();
            await window.Discover("main");

            // The launch's half runs off the UI thread, as it would in a process of its own; the window answers
            // on the UI thread, which is free while the test awaits.
            await Assert.That(await Task.Run(() => handoff.TryHandOffAsync(["main"]))).IsTrue();
            await Assert.That(window.LogText()).Contains("Called up for 'main' again");
            await Assert.That(await Task.Run(() => handoff.TryHandOffAsync(["feature/other"]))).IsFalse();

            window.Close();
            UiTestExtensions.Pump();

            await Assert.That(Directory.GetFiles(handoff.RegistryDirectory).Length).IsEqualTo(0);
            await Assert.That(await Task.Run(() => handoff.TryHandOffAsync(["main"]))).IsFalse();
        });
    }
}
