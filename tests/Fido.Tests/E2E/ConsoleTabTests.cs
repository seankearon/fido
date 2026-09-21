using System.IO;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Fido.Models;
using Fido.Services;
using Fido.Theme;
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

    // --- Links on screen ---------------------------------------------------------------------
    //
    // Ctrl+Click on a link in the terminal opens it in the default browser. The terminal finds the link
    // and reports the click (it underlines one under the pointer and shows the hand cursor); what
    // follows is Fido's, and that part is what these check. The window's handler is driven directly:
    // the pane forwards the terminal's event to it, and that hop is a XAML event attribute the build
    // already refuses to compile if the handler goes missing or changes shape.

    [Test]
    public async Task A_link_clicked_in_the_console_goes_to_the_browser_and_is_named_in_the_flight_log()
    {
        var (world, root) = PlainRepo();
        using var _ = world;
        var browser = new FakeBrowser();
        var services = world.BuildServices(
            [root], new FakeEditorLauncher(), new FakeDialogService(), browser: browser);

        await Harness.WithWindow(services, async window =>
        {
            window.OpenTerminalLink("https://example.com/build/42");

            await Assert.That(browser.LastOpened).IsEqualTo("https://example.com/build/42");

            // Named, not just opened: a wrapped URL is hard to read back off the screen, and an OSC 8
            // hyperlink needn't show its target at all, so the log is where the user finds out where
            // their click went.
            await Assert.That(window.LogText()).Contains("▸ Opening https://example.com/build/42 in your browser");
        });
    }

    [Test]
    public async Task A_link_that_isnt_the_web_is_refused_out_loud_and_never_reaches_the_opener()
    {
        var (world, root) = PlainRepo();
        using var _ = world;
        var browser = new FakeBrowser();
        var services = world.BuildServices(
            [root], new FakeEditorLauncher(), new FakeDialogService(), browser: browser);

        await Harness.WithWindow(services, async window =>
        {
            // The scrollback belongs to whatever the shell just ran, and on Windows "open" would hand
            // this to the shell — which for a scheme other than http(s) can run a program rather than
            // show a page.
            window.OpenTerminalLink("file:///etc/passwd");

            await Assert.That(browser.Opened.Count).IsEqualTo(0);

            // Out loud, because a silent refusal reads as a click that missed.
            await Assert.That(window.LogText()).Contains("⚠ Not opening file:///etc/passwd");
        });
    }

    [Test]
    public async Task A_browser_that_never_opens_is_reported_rather_than_assumed()
    {
        var (world, root) = PlainRepo();
        using var _ = world;
        var browser = new FakeBrowser(succeeds: false);
        var services = world.BuildServices(
            [root], new FakeEditorLauncher(), new FakeDialogService(), browser: browser);

        await Harness.WithWindow(services, async window =>
        {
            window.OpenTerminalLink("https://example.com/build/42");

            await Assert.That(window.LogText()).Contains("⚠ Couldn't open https://example.com/build/42");
        });
    }

    /// <summary>
    /// With the setting on, the console is wearing Fido's colours before the shell starts.
    ///
    /// The brushes are what this pins, because they are what the emulator seeds itself from: leave them
    /// at their defaults and it ignores the palette in <c>Options.Theme</c> entirely — stock ground,
    /// stock colours — however carefully that object was filled in. They are also set on the control
    /// itself, so this holds whether or not the headless harness gave the pane a template.
    ///
    /// Ordering is the other half, and it is why <c>ApplyPalette</c> runs before <c>LaunchProcess</c>:
    /// the emulator takes its colours at launch and keeps that copy, so a palette applied afterwards
    /// paints nothing. What the palette itself says is pinned by
    /// <see cref="The_palette_is_Fidos_own_in_both_themes"/>.
    /// </summary>
    [Test]
    [Timeout(120_000)]
    public async Task The_console_takes_Fidos_palette_before_the_shell_starts()
    {
        var (world, root) = PlainRepo();
        using var _ = world;
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService(),
            consoleUsesFidoPalette: true);

        await Harness.WithWindow(services, async window =>
        {
            App.ApplyTheme(AppTheme.Light);
            UiTestExtensions.Pump();

            await window.Discover("main");
            await window.RunConsoleOptionAsync(window.Vm().ConsoleTabRuns[0]);   // shell here

            var terminal = window.FindControl<ConsolePane>("ConsoleView")!
                .FindControl<Iciclecreek.Terminal.TerminalControl>("Terminal")!;

            // Fido's light ground and ink — not xterm's black.
            await Assert.That((terminal.Background as ISolidColorBrush)?.Color).IsEqualTo(Color.Parse("#F5F1E8"));
            await Assert.That((terminal.Foreground as ISolidColorBrush)?.Color).IsEqualTo(Color.Parse("#211E17"));

            // And the emulator's own options, when the harness built them (see the capture test below).
            if (terminal.Options?.Theme is { } theme)
            {
                await Assert.That(theme.Background).IsEqualTo("#F5F1E8");
                await Assert.That(theme.Green).IsEqualTo("#3E7C55");
                await Assert.That(terminal.Options.MinimumContrastRatio).IsEqualTo(TerminalPalette.MinimumContrast);
            }

            App.ApplyTheme(AppTheme.System);
        });
    }

    /// <summary>
    /// And with the setting off — the default — the console keeps the emulator's <em>sixteen colours</em>,
    /// but not its ground: that is black-and-white the right way up for the theme, so a light Fido gets a
    /// white console rather than a black box sitting in a cream window.
    ///
    /// The setting chooses between the plain scheme and Fido's, in other words, not between a fixed
    /// console and one that follows the theme. Asserted on the brushes as well as the options, because
    /// setting those is what makes any of it take.
    /// </summary>
    [Test]
    [Timeout(120_000)]
    public async Task By_default_the_console_keeps_the_terminals_own_colours_the_right_way_up()
    {
        var (world, root) = PlainRepo();
        using var _ = world;
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            App.ApplyTheme(AppTheme.Light);
            UiTestExtensions.Pump();

            await window.Discover("main");
            await window.RunConsoleOptionAsync(window.Vm().ConsoleTabRuns[0]);   // shell here

            var pane = window.FindControl<ConsolePane>("ConsoleView")!;
            var terminal = pane.FindControl<Iciclecreek.Terminal.TerminalControl>("Terminal")!;

            await Assert.That(pane.UseFidoPalette).IsFalse();

            // Plain, not Fido's: the light theme's white ground, and none of the warm cream.
            await Assert.That((terminal.Background as ISolidColorBrush)?.Color).IsEqualTo(Colors.White);
            await Assert.That((terminal.Foreground as ISolidColorBrush)?.Color).IsEqualTo(Colors.Black);

            // And the emulator's own options, when the harness built them (see the capture test below).
            if (terminal.Options?.Theme is { } theme)
            {
                await Assert.That(theme.Background).IsEqualTo("#FFFFFF");
                await Assert.That(theme.Background).IsNotEqualTo("#F5F1E8");
                // The sixteen are still the emulator's own — Fido never writes them in this mode.
                await Assert.That(theme.Green).IsNotEqualTo("#3E7C55");
            }

            App.ApplyTheme(AppTheme.System);
        });
    }

    /// <summary>
    /// The plain scheme turns over with the theme, and it does it under a <em>running</em> shell — the
    /// emulator reads its colours through the live options object, so there is nothing to wait for.
    /// </summary>
    [Test]
    [Timeout(120_000)]
    public async Task The_plain_console_turns_over_with_the_theme_under_a_running_shell()
    {
        var (world, root) = PlainRepo();
        using var _ = world;
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            App.ApplyTheme(AppTheme.Light);
            UiTestExtensions.Pump();

            await window.Discover("main");
            await window.RunConsoleOptionAsync(window.Vm().ConsoleTabRuns[0]);   // shell here

            var terminal = window.FindControl<ConsolePane>("ConsoleView")!
                .FindControl<Iciclecreek.Terminal.TerminalControl>("Terminal")!;
            await Assert.That((terminal.Background as ISolidColorBrush)?.Color).IsEqualTo(Colors.White);

            App.ApplyTheme(AppTheme.Dark);
            UiTestExtensions.Pump();

            await Assert.That((terminal.Background as ISolidColorBrush)?.Color).IsEqualTo(Colors.Black);
            await Assert.That((terminal.Foreground as ISolidColorBrush)?.Color).IsEqualTo(Colors.White);

            if (terminal.Options?.Theme is { } theme)
            {
                await Assert.That(theme.Background).IsEqualTo("#000000");
                // …and the caret with it, which is the entry that would otherwise be white on white.
                await Assert.That(theme.Cursor).IsEqualTo("#FFFFFF");
            }

            App.ApplyTheme(AppTheme.System);
        });
    }

    /// <summary>
    /// The plain scheme, asserted on a bare <c>ThemeOptions</c> so it holds wherever the control does or
    /// doesn't get built: the emulator's own sixteen, with only the ground, the ink and the caret turned
    /// over for the theme.
    ///
    /// The round trip is the point. Everything colouring this control mutates one shared options object,
    /// so "Fido's palette off" cannot mean "stop writing" — the last thing written would simply stay, and
    /// unticking the setting would leave Fido's green sitting in the theme for ever. It means putting back
    /// the snapshot taken before Fido ever wrote there, which is what this pins.
    /// </summary>
    [Test]
    public async Task The_plain_scheme_is_the_emulators_own_sixteen_with_the_ground_the_right_way_up()
    {
        // Stand-in for whatever the emulator ships: what matters is that these exact values come back.
        var stock = new XTerm.Options.ThemeOptions
        {
            Background = "#000000", Foreground = "#FFFFFF", Cursor = "#FFFFFF", Green = "#00CD00",
        };

        var options = new XTerm.Options.ThemeOptions();
        TerminalPalette.Apply(options, ThemeVariant.Light);            // Fido's palette goes on…
        await Assert.That(options.Green).IsEqualTo("#3E7C55");

        TerminalPalette.ApplyPlain(options, ThemeVariant.Light, stock);   // …and comes off again
        await Assert.That(options.Green).IsEqualTo("#00CD00");         // the emulator's own is back
        await Assert.That(options.Background).IsEqualTo("#FFFFFF");    // on a light ground
        await Assert.That(options.Foreground).IsEqualTo("#000000");
        await Assert.That(options.Cursor).IsEqualTo("#000000");        // a caret you can find on white

        var dark = new XTerm.Options.ThemeOptions();
        TerminalPalette.ApplyPlain(dark, ThemeVariant.Dark, stock);
        await Assert.That(dark.Background).IsEqualTo("#000000");       // the stock pair, untouched
        await Assert.That(dark.Foreground).IsEqualTo("#FFFFFF");
        await Assert.That(dark.Green).IsEqualTo("#00CD00");
    }

    /// <summary>
    /// The palette itself: Fido's brushes, per theme, rather than xterm's. Asserted on a bare
    /// <c>ThemeOptions</c>, so it holds wherever the control does or doesn't get built.
    /// </summary>
    [Test]
    public async Task The_palette_is_Fidos_own_in_both_themes()
    {
        var light = new XTerm.Options.ThemeOptions();
        TerminalPalette.Apply(light, ThemeVariant.Light);

        await Assert.That(light.Background).IsEqualTo("#F5F1E8");   // FidoLogBg
        await Assert.That(light.Foreground).IsEqualTo("#211E17");   // FidoTextPrimary
        await Assert.That(light.Green).IsEqualTo("#3E7C55");        // FidoLogOk — the flight log's own ✓
        // On a pale ground "bright" means more contrast, so the Bright* entries are darker, not lighter.
        await Assert.That(light.BrightGreen).IsEqualTo("#2E6341");

        var dark = new XTerm.Options.ThemeOptions();
        TerminalPalette.Apply(dark, ThemeVariant.Dark);

        await Assert.That(dark.Background).IsEqualTo("#1C1812");
        await Assert.That(dark.Foreground).IsEqualTo("#F0EBDF");
        await Assert.That(dark.Green).IsEqualTo("#5FA97C");
        await Assert.That(dark.BrightGreen).IsEqualTo("#83C79C");
    }

    /// <summary>
    /// Captures the Console tab for the docs gallery and for review.
    ///
    /// It asserts little on purpose. The embedded terminal only paints in the <em>first</em> window a
    /// headless process shows: in a window opened after another has come and gone the control never
    /// resolves its own control theme (no template, no visual children), so the pane comes out blank
    /// however long the shell is given. Run this test on its own — <c>--treenode-filter
    /// "/*/*/ConsoleTabTests/Capture_the_console_tab_for_review"</c>, which is how the gallery generator
    /// captures it — and the same code paints a real shell. Nothing about the app is conditional on that;
    /// it is a headless-harness artefact, and the palette above is where the colours are actually pinned.
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
