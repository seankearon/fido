using System.IO;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Fido.Models;
using Fido.Services;
using Fido.ViewModels;
using Fido.Tests.Infrastructure;
using Fido.Views;

namespace Fido.Tests.E2E;

/// <summary>
/// The Console tab: how the run menu composes, and where a pick is sent. The shell composition is
/// asserted directly rather than by starting one — what matters here is that a pick reaches the pane
/// instead of the user's terminal, and that the command a shell would be handed keeps the window alive
/// after it finishes.
/// </summary>
[NotInParallel]
public class ConsoleTabTests
{
    [Test]
    public async Task The_shell_spec_runs_a_command_and_leaves_an_interactive_shell()
    {
        // The composition the window hands to the PTY — asserted directly, because it's the part that
        // decides whether a failed script leaves its output on screen or takes the window with it.
        var spec = RunnerShell.For("echo hello");

        await Assert.That(spec.Executable).IsNotEmpty();
        if (!OperatingSystem.IsWindows())
        {
            await Assert.That(spec.Args[0]).IsEqualTo("-c");
            // The run, then a live shell: the window survives the command either way.
            await Assert.That(spec.Args[1]).Contains("echo hello");
            await Assert.That(spec.Args[1]).Contains("exec ");
        }
    }

    [Test]
    public async Task A_run_file_convention_is_the_same_one_the_launch_path_uses()
    {
        // A .ps1 goes to pwsh whichever console it lands in — the shared helper in EditorLauncher.
        var spec = RunnerShell.For("build.ps1 --no-restore");
        if (!OperatingSystem.IsWindows())
            await Assert.That(spec.Args[1]).Contains("pwsh build.ps1");
        else
            await Assert.That(spec.Args).Contains("& ./build.ps1 --no-restore");
    }

    // --- PowerShell's current-directory rule -------------------------------------------------
    //
    // These run everywhere, not just on Windows: WindowsCommand is a pure string composition, and the
    // rule it encodes is the one thing about this path a Linux CI box can still check.

    [Test]
    public async Task A_root_script_is_made_relative_because_PowerShell_wont_look_in_the_current_folder()
    {
        // Bare 'build.ps1' is "not recognized …" even standing in the folder that holds it.
        await Assert.That(RunnerShell.WindowsCommand("build.ps1")).IsEqualTo("& ./build.ps1");
        await Assert.That(RunnerShell.WindowsCommand("build.cmd")).IsEqualTo("& ./build.cmd");
    }

    [Test]
    public async Task Arguments_ride_along_after_the_relative_script()
    {
        await Assert.That(RunnerShell.WindowsCommand("build.ps1 --no-restore -c Release"))
            .IsEqualTo("& ./build.ps1 --no-restore -c Release");
    }

    [Test]
    public async Task A_script_whose_name_has_spaces_is_quoted_for_PowerShell()
    {
        // The run menu double-quotes such a name; PowerShell wants single quotes, and the call operator
        // is what stops the quoted path being read as a bare string expression.
        await Assert.That(RunnerShell.WindowsCommand("\"my script.ps1\""))
            .IsEqualTo("& './my script.ps1'");
    }

    [Test]
    public async Task A_native_command_is_left_alone()
    {
        // 'aspire start' resolves on PATH; ./aspire is a path that doesn't exist.
        await Assert.That(RunnerShell.WindowsCommand("aspire start")).IsEqualTo("aspire start");
        await Assert.That(RunnerShell.WindowsCommand("dotnet build")).IsEqualTo("dotnet build");
    }

    [Test]
    public async Task A_command_that_already_carries_a_path_is_left_alone()
    {
        await Assert.That(RunnerShell.WindowsCommand("./build.ps1")).IsEqualTo("./build.ps1");
        await Assert.That(RunnerShell.WindowsCommand(@"C:\tools\build.ps1")).IsEqualTo(@"C:\tools\build.ps1");
    }

    private static EditorLaunchOption ConsoleTool(MainWindow window) =>
        window.Vm().GridTools.First(t => t.Name == "Console");

    /// <summary>A world with one clone on a branch, and no <c>.fido/cfg.yaml</c> anywhere.</summary>
    private static (TestRepoWorld World, string Root) PlainRepo()
    {
        var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");
        return (world, root);
    }

    [Test]
    public async Task The_console_tab_always_leads_with_a_shell_even_with_no_fido_config()
    {
        var (world, root) = PlainRepo();
        using var _ = world;
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");

            // The tab's menu drives the pane beneath it, so "give me a clean prompt" is always on offer —
            // it has nothing to do with what the branch's config nominated, and there may be no config.
            var runs = window.Vm().ConsoleTabRuns;
            await Assert.That(runs.Count).IsEqualTo(1);
            await Assert.That(runs[0].IsShell).IsTrue();
            await Assert.That(runs[0].Label).IsEqualTo("shell here");
        });
    }

    [Test]
    public async Task The_tool_buttons_menu_carries_only_what_the_branch_config_asked_for()
    {
        var (world, root) = PlainRepo();
        using var _ = world;
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService(), runInFido: true);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");

            // No shell entry here even with RunInFido on: the Console *button* already opens a terminal
            // at the folder, so a second way to do the same thing would be noise. The caret stays hidden.
            await Assert.That(ConsoleTool(window).HasRuns).IsFalse();
        });
    }

    [Test]
    public async Task A_console_tab_pick_runs_in_the_pane_and_never_reaches_the_launcher()
    {
        var (world, root) = PlainRepo();
        using var _ = world;
        var launcher = new FakeEditorLauncher();

        // RunInFido left off on purpose: it governs the launch buttons, and must not reach into a tab
        // the user is already looking at.
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");
            await window.RunConsoleOptionAsync(window.Vm().ConsoleTabRuns[0]);

            await Assert.That(launcher.Launches.Count).IsEqualTo(0);
            await Assert.That(window.Vm().IsConsoleTab).IsTrue();   // the pane is shown before it runs
            await Assert.That(window.LogText()).Contains("Opening a shell in");
        });
    }

    [Test]
    public async Task The_flight_log_is_the_tab_you_land_on()
    {
        var (world, root) = PlainRepo();
        using var _ = world;
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            var vm = window.Vm();
            await Assert.That(vm.IsFlightLogTab).IsTrue();
            await Assert.That(vm.IsConsoleTab).IsFalse();

            // The two are one piece of state, so the log's copy/save buttons and the console's run menu
            // can never both be on the tab row at once.
            vm.IsConsoleTab = true;
            await Assert.That(vm.IsFlightLogTab).IsFalse();

            vm.IsFlightLogTab = true;
            await Assert.That(vm.IsConsoleTab).IsFalse();
        });
    }

    /// <summary>
    /// Captures the Console tab for review. Deliberately asserts almost nothing: under
    /// Avalonia.Headless the embedded terminal paints nothing at all, even though the shell runs and the
    /// control reports a correct grid against real bounds. The same control in a top-level window paints
    /// fine, so this is specific to the embedded pane and/or the headless renderer. Until that is
    /// understood, this exists to produce the screenshot — it is not evidence the pane works.
    /// </summary>
    [Test]
    [Timeout(120_000)]
    public async Task Capture_the_console_tab_for_review()
    {
        var (world, root) = PlainRepo();
        using var _ = world;
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        var command = OperatingSystem.IsWindows()
            ? "Write-Host 'FIDO CONSOLE OK' -ForegroundColor Green; ls"
            : "printf '\\033[32mok\\033[0m \\033[33mwarn\\033[0m \\033[31mfail\\033[0m \\033[34minfo\\033[0m\\n'; ls -la";

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");

            // The terminal sizes its cell grid from the typeface's advance width, so it needs a real
            // monospace face. Fido's FidoMono list (JetBrains Mono / Cascadia Code / Consolas) is a
            // developer-machine assumption; on a bare CI box none of them exist and the fallback is
            // proportional, which smears the grid.
            if (window.FindControl<ConsolePane>("ConsoleView") is { } pane &&
                pane.FindControl<Iciclecreek.Terminal.TerminalControl>("Terminal") is { } terminal)
                terminal.FontFamily = new Avalonia.Media.FontFamily("DejaVu Sans Mono, Liberation Mono, monospace");

            await window.RunConsoleOptionAsync(
                window.Vm().ConsoleTabRuns[0] with { Command = command, IsShell = false });

            // Let the shell start, run, and paint before the frame that gets kept.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(25);
            while (DateTime.UtcNow < deadline)
            {
                UiTestExtensions.Pump();
                await Task.Delay(150);
            }

            var frame = Screenshots.Save(window, "console-tab");
            await Assert.That(frame).IsNotNull();
        });
    }
}
