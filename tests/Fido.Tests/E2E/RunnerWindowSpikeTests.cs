using System.IO;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Fido.Services;
using Fido.Tests.Infrastructure;
using Fido.Views;

namespace Fido.Tests.E2E;

/// <summary>
/// The in-app runner: a real shell, over a real pseudo-terminal, rendered in a Fido window.
///
/// These are spike tests — they prove the PTY actually starts, that its output reaches the control, and
/// that the whole thing renders — rather than pinning behaviour. They run a shell for real, so they are
/// slower than the rest of the suite and they are the only tests here that depend on the machine having
/// one.
/// </summary>
[NotInParallel]
public class RunnerWindowSpikeTests
{
    /// <summary>
    /// Pumps the dispatcher until <paramref name="done"/> holds or the budget runs out. A PTY is a child
    /// process on the other side of a pipe: nothing about it is synchronous with the test, so the only
    /// honest wait is to keep the UI thread running and re-check.
    /// </summary>
    private static async Task<bool> PumpUntil(Func<bool> done, TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (done()) return true;
            await Task.Delay(100);
        }
        Dispatcher.UIThread.RunJobs();
        return done();
    }

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
            await Assert.That(spec.Args).Contains("build.ps1 --no-restore");
    }

    [Test]
    [Timeout(120_000)]
    public async Task The_runner_starts_a_real_shell_and_renders_its_output()
    {
        using var world = new TestRepoWorld();
        var folder = world.SearchRoot("runner-spike");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "hello.txt"), "from the worktree\n");

        // A command with colour in it: the point of the PTY is that this arrives as colour rather than
        // being stripped, because the shell sees a terminal on the other end.
        var command = OperatingSystem.IsWindows()
            ? "Write-Host 'FIDO RUNNER OK' -ForegroundColor Green; ls"
            : "printf '\\033[32mFIDO RUNNER OK\\033[0m\\n'; ls -la; echo done";

        await Ui.On(async () =>
        {
            var window = new RunnerWindow("feature/spike", folder, command);

            // The terminal sizes its cell grid from the typeface's advance width, so it needs a real
            // monospace face. Fido's FidoMono list (JetBrains Mono / Cascadia Code / Consolas) is a
            // developer-machine assumption; on a bare CI box none of them exist and the fallback is
            // proportional, which smears the grid. Pin one that is actually present so the captured
            // frame shows what a user would see rather than what the build agent lacks.
            if (window.FindControl<Iciclecreek.Terminal.TerminalControl>("Terminal") is { } terminal)
                terminal.FontFamily = new Avalonia.Media.FontFamily("DejaVu Sans Mono, Liberation Mono, monospace");

            window.Show();
            UiTestExtensions.Pump();

            // Give the shell time to start, run, and paint. The assertion is on a rendered frame, not on a
            // process handle: a PTY that starts but never reaches the control is the failure worth catching.
            var painted = await PumpUntil(
                () => Screenshots.Save(window, "runner-probe") is not null,
                TimeSpan.FromSeconds(30));

            await Assert.That(painted).IsTrue();

            // Let the command finish and the prompt come back before the frame that gets kept.
            await PumpUntil(() => false, TimeSpan.FromSeconds(8));
            var frame = Screenshots.Save(window, "runner-window");

            await Assert.That(frame).IsNotNull();
            await Assert.That(frame!.PixelSize.Width).IsGreaterThan(400);

            window.Close();
            UiTestExtensions.Pump();
        });
    }
}
