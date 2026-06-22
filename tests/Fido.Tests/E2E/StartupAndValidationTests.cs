using Avalonia.Controls;
using Fido;
using Fido.Models;
using Fido.Tests.Infrastructure;
using Fido.ViewModels;

namespace Fido.Tests.E2E;

/// <summary>CLI prefill, input validation, and that the real Open button drives the flow.</summary>
[NotInParallel]
public class StartupAndValidationTests
{
    [Test]
    public async Task Startup_args_prefill_branch_solution_and_folder_mode()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var services = world.BuildServices([root], new FakeRiderLauncher(), new FakeDialogService());

        var original = Program.StartupArgs;
        Program.StartupArgs = ["-b", "feature/z", "-s", "MyApp", "--folder"];
        try
        {
            await Harness.WithWindow(services, async window =>
            {
                var vm = window.Vm();
                await Assert.That(vm.BranchName).IsEqualTo("feature/z");
                await Assert.That(vm.SolutionName).IsEqualTo("MyApp");
                await Assert.That(vm.IsFolderMode).IsTrue();

                // the real input control reflects the prefill via its two-way binding
                await Assert.That(window.FindControl<AutoCompleteBox>("BranchBox")!.Text).IsEqualTo("feature/z");
                Screenshots.Save(window, "cli-args-prefill");
            });
        }
        finally
        {
            Program.StartupArgs = original;
        }
    }

    [Test]
    public async Task Positional_argument_prefills_the_branch()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var services = world.BuildServices([root], new FakeRiderLauncher(), new FakeDialogService());

        var original = Program.StartupArgs;
        Program.StartupArgs = ["feature/positional"];   // a bare argument is taken as the branch
        try
        {
            await Harness.WithWindow(services, async window =>
            {
                await Assert.That(window.Vm().BranchName).IsEqualTo("feature/positional");
            });
        }
        finally
        {
            Program.StartupArgs = original;
        }
    }

    [Test]
    public async Task Cli_branch_auto_opens_the_flow_and_closes_on_success()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var rider = new FakeRiderLauncher();
        var services = world.BuildServices([root], rider, new FakeDialogService());   // default: close on CLI launch

        var original = Program.StartupArgs;
        Program.StartupArgs = ["-b", "main", "-s", "Foo"];
        try
        {
            await Harness.WithWindow(services, async window =>
            {
                var closed = new TaskCompletionSource();
                window.Closed += (_, _) => closed.TrySetResult();

                // No button click: providing the branch on the CLI runs the open flow on startup.
                var launched = await Task.WhenAny(rider.FirstLaunch, Task.Delay(TimeSpan.FromSeconds(10)));
                await Assert.That(launched).IsEqualTo((Task)rider.FirstLaunch);
                await Assert.That(rider.Launches.Count).IsEqualTo(1);

                var didClose = await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
                await Assert.That(didClose).IsEqualTo((Task)closed.Task);
            });
        }
        finally
        {
            Program.StartupArgs = original;
        }
    }

    [Test]
    public async Task Button_open_does_not_close_the_window_by_default()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var rider = new FakeRiderLauncher();
        var services = world.BuildServices([root], rider, new FakeDialogService());   // default: CommandLine

        await Harness.WithWindow(services, async window =>
        {
            var closed = false;
            window.Closed += (_, _) => closed = true;

            await window.Open("main", "Foo");   // interactive open (not from the command line)

            await Assert.That(rider.Launches.Count).IsEqualTo(1);
            await Assert.That(closed).IsFalse();
            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.Go);
        });
    }

    [Test]
    public async Task CloseAfterOpen_Always_closes_after_a_button_open()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var rider = new FakeRiderLauncher();
        var services = world.BuildServices([root], rider, new FakeDialogService(),
            closeAfterOpen: CloseAfterOpen.Always);

        await Harness.WithWindow(services, async window =>
        {
            var closed = false;
            window.Closed += (_, _) => closed = true;

            await window.Open("main", "Foo");

            await Assert.That(rider.Launches.Count).IsEqualTo(1);
            await Assert.That(closed).IsTrue();
        });
    }

    [Test]
    public async Task Empty_branch_is_no_go_and_touches_neither_git_nor_rider()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var rider = new FakeRiderLauncher();
        var services = world.BuildServices([root], rider, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("   ", "");   // whitespace-only branch

            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.NoGo);
            await Assert.That(window.Vm().StatusText).Contains("please enter a branch name");
            await Assert.That(rider.Launches.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Clicking_the_open_button_runs_the_flow()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var rider = new FakeRiderLauncher();
        var services = world.BuildServices([root], rider, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            window.SetText("BranchBox", "main");
            window.SetText("SolutionBox", "Foo");
            window.ClickButton("OpenButton");   // real Click → async-void handler → RunOpenAsync

            // the click can't be awaited directly; wait on the launch signal (bounded)
            var completed = await Task.WhenAny(rider.FirstLaunch, Task.Delay(TimeSpan.FromSeconds(10)));
            await Assert.That(completed).IsEqualTo((Task)rider.FirstLaunch);
            await Assert.That(rider.Launches.Count).IsEqualTo(1);
        });
    }
}
