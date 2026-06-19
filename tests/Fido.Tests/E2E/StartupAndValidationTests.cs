using Avalonia.Controls;
using Fido;
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
