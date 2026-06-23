using Avalonia.Controls;
using Avalonia.Input;
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
    public async Task CloseAfterOpen_with_a_delay_counts_down_then_closes()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var rider = new FakeRiderLauncher();
        var services = world.BuildServices([root], rider, new FakeDialogService(),
            closeAfterOpen: CloseAfterOpen.Always, closeAfterOpenDelaySeconds: 1);

        await Harness.WithWindow(services, async window =>
        {
            var closed = new TaskCompletionSource();
            window.Closed += (_, _) => closed.TrySetResult();

            await window.Open("main", "Foo");

            // Launched — but the close is deferred behind the countdown: the window is still up, the
            // "keep open" bar is showing, and the console has started narrating the countdown.
            await Assert.That(rider.Launches.Count).IsEqualTo(1);
            await Assert.That(closed.Task.IsCompleted).IsFalse();
            await Assert.That(window.Vm().IsClosingCountdown).IsTrue();
            await Assert.That(window.LogText()).Contains("Closing in");
            Screenshots.Save(window, "close-countdown");

            // ...and once the countdown elapses, it closes itself.
            var didClose = await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(8)));
            await Assert.That(didClose).IsEqualTo((Task)closed.Task);
        });
    }

    [Test]
    public async Task Countdown_ticks_down_in_place_on_a_single_log_line()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var rider = new FakeRiderLauncher();
        var services = world.BuildServices([root], rider, new FakeDialogService(),
            closeAfterOpen: CloseAfterOpen.Always, closeAfterOpenDelaySeconds: 4);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("main", "Foo");

            var lineCount = window.Vm().Log.Count;
            var firstTick = window.Vm().Log[^1].Text;
            await Assert.That(firstTick).Contains("Closing in 4");

            await Task.Delay(TimeSpan.FromSeconds(1.5));   // let it tick at least once

            // The number reduced, but on the same line — the log didn't grow by a line per second.
            await Assert.That(window.Vm().Log.Count).IsEqualTo(lineCount);
            await Assert.That(window.Vm().Log[^1].Text).IsNotEqualTo(firstTick);
            await Assert.That(window.Vm().Log[^1].Text).Contains("Closing in");
        });
    }

    [Test]
    public async Task Keep_open_cancels_a_pending_auto_close()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var rider = new FakeRiderLauncher();
        var services = world.BuildServices([root], rider, new FakeDialogService(),
            closeAfterOpen: CloseAfterOpen.Always, closeAfterOpenDelaySeconds: 2);

        await Harness.WithWindow(services, async window =>
        {
            var closed = false;
            window.Closed += (_, _) => closed = true;

            await window.Open("main", "Foo");           // arms a 2s countdown, shows the bar
            await Assert.That(window.Vm().IsClosingCountdown).IsTrue();

            window.ClickButton("KeepOpenButton");        // the escape hatch
            await Assert.That(window.Vm().IsClosingCountdown).IsFalse();

            // Wait past the original delay: because it was kept open, the window stays up.
            await Task.Delay(TimeSpan.FromSeconds(3));
            await Assert.That(closed).IsFalse();
            await Assert.That(rider.Launches.Count).IsEqualTo(1);
        });
    }

    [Test]
    public async Task Starting_another_open_cancels_a_pending_auto_close()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var rider = new FakeRiderLauncher();
        var services = world.BuildServices([root], rider, new FakeDialogService(),
            closeAfterOpen: CloseAfterOpen.Always, closeAfterOpenDelaySeconds: 1);

        await Harness.WithWindow(services, async window =>
        {
            var closed = false;
            window.Closed += (_, _) => closed = true;

            await window.Open("main", "Foo");   // arms a 1s countdown
            await Assert.That(closed).IsFalse();
            await Assert.That(window.Vm().IsClosingCountdown).IsTrue();

            await window.Open("", "");           // a fresh open (here a no-go) supersedes the countdown
            await Assert.That(window.Vm().IsClosingCountdown).IsFalse();

            // Wait well past the original delay: because the countdown was cancelled, the window stays open.
            await Task.Delay(TimeSpan.FromSeconds(2.5));
            await Assert.That(closed).IsFalse();
            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.NoGo);
            await Assert.That(rider.Launches.Count).IsEqualTo(1);
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
    public async Task Enter_in_the_branch_box_opens_in_a_single_press()
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

            window.PressKeyOn("BranchBox", Key.Enter);   // one Enter — not the drop-down-dismissing first press plus a second

            // a single launch: the box handled Enter and the default button did not also fire
            var completed = await Task.WhenAny(rider.FirstLaunch, Task.Delay(TimeSpan.FromSeconds(10)));
            await Assert.That(completed).IsEqualTo((Task)rider.FirstLaunch);
            await Assert.That(rider.Launches.Count).IsEqualTo(1);
        });
    }

    [Test]
    public async Task Enter_with_the_mru_dropdown_open_opens_in_one_press()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var rider = new FakeRiderLauncher();
        var services = world.BuildServices([root], rider, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            // Give the branch box MRU history so its suggestion drop-down has something to show,
            // then set the inputs as if a branch had just been pasted in.
            window.Vm().LoadMru(["main"], []);
            window.SetText("BranchBox", "main");
            window.SetText("SolutionBox", "Foo");

            // Focusing the box surfaces the MRU drop-down (OnMruGotFocus) — the state the user is
            // in when they hit Enter. Assert it's actually showing so we exercise the real swallow.
            var box = window.FindControl<AutoCompleteBox>("BranchBox")!;
            box.Focus();
            UiTestExtensions.Pump();
            await Assert.That(box.IsDropDownOpen).IsTrue();

            // One Enter on the real control. Avalonia's AutoCompleteBox.OnKeyDown consumes it just
            // to dismiss the drop-down (marking it handled); the fix opens on that same press.
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await Assert.That(box.IsDropDownOpen).IsFalse();   // the press dismisses the MRU list...
            UiTestExtensions.Pump();

            // ...and, on that same press, acts on the branch — exactly once, with no second Enter.
            var completed = await Task.WhenAny(rider.FirstLaunch, Task.Delay(TimeSpan.FromSeconds(10)));
            await Assert.That(completed).IsEqualTo((Task)rider.FirstLaunch);
            await Assert.That(rider.Launches.Count).IsEqualTo(1);
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
