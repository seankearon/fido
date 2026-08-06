using Avalonia.Controls;
using Avalonia.Input;
using Fido;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.E2E;

/// <summary>
/// CLI startup semantics for the discovery-first flow: argument prefill, the one-shot auto-open
/// (explicit tool + exactly one location, and nothing else), the <c>--tool none</c> equal-weight
/// grid, and blank-branch validation. The CLI no longer opens anything unless a tool is named —
/// a bare branch just runs the scan and presents the results. Also pins the default-tool boundary
/// — a CLI <c>--tool</c> override is run-scoped while a gear-popover pick persists — and Enter in
/// the branch box firing an immediate scan.
/// </summary>
[NotInParallel]
public class StartupAndValidationTests
{
    [Test]
    public async Task Bare_branch_argument_prefills_and_scans_but_never_auto_opens()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");   // exactly one location for 'main'

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        var original = Program.StartupArgs;
        Program.StartupArgs = ["main"];   // a bare argument is taken as the branch — no tool named
        try
        {
            await Harness.WithWindow(services, async window =>
            {
                var vm = window.Vm();
                await Assert.That(vm.BranchName).IsEqualTo("main");

                // Await the scan the Opened handler started, rather than racing it with one of our
                // own. One location — but with no tool named on the command line, presenting the
                // result is all that happens.
                await window.StartupScan;

                await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Found);
                await Assert.That(vm.Targets.Count).IsEqualTo(1);
                await Assert.That(launcher.Launches.Count).IsEqualTo(0);
            });
        }
        finally
        {
            Program.StartupArgs = original;
        }
    }

    [Test]
    public async Task Branch_plus_tool_auto_opens_once_for_a_single_location_and_closes()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");   // exactly one location for 'main'

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());   // default: close on CLI launch

        var original = Program.StartupArgs;
        Program.StartupArgs = ["main", "rider"];   // bare branch, then a bare tool id
        try
        {
            // Watch for the close from before the window is shown: the startup scan can find its one
            // location, open Rider, and close Fido while Show() is still running — a subscription made
            // inside the body would then be waiting for a close that already happened.
            var closed = new TaskCompletionSource();

            await Harness.WithWindow(services, async window =>
            {
                // No interaction: naming a tool on the CLI auto-opens when the scan finds one location.
                var launched = await Task.WhenAny(launcher.FirstLaunch, Task.Delay(TimeSpan.FromSeconds(10)));
                await Assert.That(launched).IsEqualTo((Task)launcher.FirstLaunch);
                await Assert.That(launcher.Launches.Count).IsEqualTo(1);
                await Assert.That(launcher.LastLaunch!.Value.Editor.Kind).IsEqualTo(EditorKind.Rider);

                // ...and a CLI-driven launch closes Fido (CloseAfterOpen.CommandLine, no delay).
                var didClose = await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
                await Assert.That(didClose).IsEqualTo((Task)closed.Task);
            }, beforeShow: window => window.Closed += (_, _) => closed.TrySetResult());
        }
        finally
        {
            Program.StartupArgs = original;
        }
    }

    [Test]
    public async Task Two_locations_with_an_explicit_tool_lists_both_and_does_not_auto_open()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");
        world.Clone(origin, root, "Bar");   // a second clone: 'main' now lives in two places

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        var original = Program.StartupArgs;
        Program.StartupArgs = ["-b", "main", "-e", "zed"];   // legacy --editor/-e alias still accepted
        try
        {
            await Harness.WithWindow(services, async window =>
            {
                var vm = window.Vm();
                await window.StartupScan;   // the scan the Opened handler started, awaited to completion

                // Both locations are presented as cards for the user to disambiguate...
                await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Found);
                await Assert.That(vm.Targets.Count).IsEqualTo(2);
                await Assert.That(vm.HasMultipleTargets).IsTrue();
                await Assert.That(window.CardWithPath("root/Foo").IsMainClone).IsTrue();
                await Assert.That(window.CardWithPath("root/Bar").IsMainClone).IsTrue();

                // ...nothing opened by itself — the ambiguity is the user's to resolve...
                await Assert.That(launcher.Launches.Count).IsEqualTo(0);

                // ...but the named tool still became this run's default (the hero button).
                await Assert.That(vm.HasHero).IsTrue();
                await Assert.That(vm.HeroLabel).IsEqualTo("Open in Zed");
            });
        }
        finally
        {
            Program.StartupArgs = original;
        }
    }

    [Test]
    public async Task Unknown_tool_id_warns_with_known_ids_and_never_auto_opens()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        var original = Program.StartupArgs;
        Program.StartupArgs = ["-b", "main", "-t", "nope"];   // a tool id that matches nothing
        try
        {
            await Harness.WithWindow(services, async window =>
            {
                var vm = window.Vm();

                // The branch still scans (--branch prefills AND scans, per the handoff); the typo
                // only disarms the one-shot auto-open. Await that very scan: starting a second one
                // would clear the log the first had already written the warning into.
                await window.StartupScan;

                // The typo is reported after the scan (which resets the log), listing the ids that
                // would have worked; the branch stays prefilled so the user can correct and retry.
                await Assert.That(vm.BranchName).IsEqualTo("main");
                await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Found);
                await Assert.That(window.LogText()).Contains("Unknown tool 'nope'");
                await Assert.That(window.LogText()).Contains("rider");

                // A single location was found, yet nothing auto-opened.
                await Assert.That(launcher.Launches.Count).IsEqualTo(0);
            });
        }
        finally
        {
            Program.StartupArgs = original;
        }
    }

    [Test]
    public async Task Tool_none_drops_the_hero_and_shows_the_full_equal_grid()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        var original = Program.StartupArgs;
        Program.StartupArgs = ["--tool", "none"];   // equal-weight grid for this run only
        try
        {
            await Harness.WithWindow(services, async window =>
            {
                var vm = window.Vm();
                await Assert.That(vm.HasHero).IsFalse();
                await Assert.That(vm.HeroTool).IsNull();

                // No hero means nobody is promoted out of the grid: all seven default tools sit there.
                await Assert.That(vm.GridTools.Count).IsEqualTo(7);
                await Assert.That(vm.GridTools.Any(t => t.Name == "Rider")).IsTrue();
            });
        }
        finally
        {
            Program.StartupArgs = original;
        }
    }

    [Test]
    public async Task Branch_and_solution_flags_prefill_the_real_input_boxes()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        var original = Program.StartupArgs;
        Program.StartupArgs = ["-b", "feature/z", "-s", "MyApp"];   // -s is the solution *filter* now
        try
        {
            await Harness.WithWindow(services, async window =>
            {
                var vm = window.Vm();
                await Assert.That(vm.BranchName).IsEqualTo("feature/z");
                await Assert.That(vm.SolutionFilter).IsEqualTo("MyApp");

                // the real input controls reflect the prefill via their two-way bindings
                await Assert.That(window.FindControl<AutoCompleteBox>("BranchBox")!.Text).IsEqualTo("feature/z");
                await Assert.That(window.FindControl<AutoCompleteBox>("SolutionBox")!.Text).IsEqualTo("MyApp");
                Screenshots.Save(window, "cli-args-prefill");
            });
        }
        finally
        {
            Program.StartupArgs = original;
        }
    }

    [Test]
    public async Task Blank_branch_stays_idle_and_locked_and_touches_neither_git_nor_a_tool()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("   ");   // whitespace-only branch — a scan has nothing to look for

            var vm = window.Vm();
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Idle);
            await Assert.That(vm.IsLocked).IsTrue();
            await Assert.That(launcher.Launches.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Cli_tool_override_is_run_scoped_and_never_persists_as_the_default()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");   // exactly one location → the named tool auto-opens

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());
        var configDir = services.ConfigService.ConfigDirectory;

        var original = Program.StartupArgs;
        Program.StartupArgs = ["-b", "main", "-t", "zed"];
        try
        {
            await Harness.WithWindow(services, async window =>
            {
                // The override owns this run: Zed takes the hero button...
                await Assert.That(window.Vm().HeroTool!.Name).IsEqualTo("Zed");

                // ...and the single-location auto-open fires — which records the MRU and saves the
                // config, so the file on disk is definitely rewritten during this run. That makes
                // the reload below a real check, not just re-reading the seed BuildServices wrote.
                var launched = await Task.WhenAny(launcher.FirstLaunch, Task.Delay(TimeSpan.FromSeconds(10)));
                await Assert.That(launched).IsEqualTo((Task)launcher.FirstLaunch);
            });
        }
        finally
        {
            Program.StartupArgs = original;
        }

        // The window is closed; what's on disk is what the next run will load. The persisted
        // default is still Rider (index 0) — `--tool zed` was a per-run override, not a settings
        // change.
        var reloaded = new ConfigService(configDir).Load();
        await Assert.That(reloaded.DefaultEditorIndex).IsEqualTo(0);
        await Assert.That(reloaded.Editors[reloaded.DefaultEditorIndex].Name).IsEqualTo("Rider");
    }

    [Test]
    public async Task Gear_popover_pick_persists_the_default_and_re_renders_the_hero()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());
        var configDir = services.ConfigService.ConfigDirectory;

        await Harness.WithWindow(services, async window =>
        {
            var vm = window.Vm();
            await Assert.That(vm.HeroTool!.Name).IsEqualTo("Rider");   // the seeded default

            // Tick Zed's radio row — the popover binds each row's IsSelected straight to these
            // objects, so this is exactly what clicking the RadioButton drives.
            vm.DefaultToolChoices.Single(c => c.Name == "Zed").IsSelected = true;
            UiTestExtensions.Pump();

            // The hero re-renders immediately (Zed leaves the grid to take the button)...
            await Assert.That(vm.HeroTool!.Name).IsEqualTo("Zed");
            await Assert.That(vm.HeroLabel).IsEqualTo("Open in Zed");
            await Assert.That(vm.GridTools.Any(t => t.Name == "Zed")).IsFalse();

            // ...and unlike a CLI --tool override, the choice is written to disk there and then.
            var afterZed = new ConfigService(configDir).Load();
            await Assert.That(afterZed.Editors[afterZed.DefaultEditorIndex].Name).IsEqualTo("Zed");

            // "No default (equal weight)": the hero disappears, every tool returns to the grid,
            // and the deliberate NoDefaultEditor (-1) choice persists through a reload.
            vm.DefaultToolChoices.Single(c => c.Index == AppConfig.NoDefaultEditor).IsSelected = true;
            UiTestExtensions.Pump();

            await Assert.That(vm.HasHero).IsFalse();
            await Assert.That(vm.HeroTool).IsNull();
            await Assert.That(vm.GridTools.Count).IsEqualTo(7);

            var afterNone = new ConfigService(configDir).Load();
            await Assert.That(afterNone.DefaultEditorIndex).IsEqualTo(AppConfig.NoDefaultEditor);
        });
    }

    [Test]
    public async Task Enter_in_the_branch_box_triggers_an_immediate_scan()
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

            // Type the branch (arming the 600ms debounce) and press Enter on the real box — the
            // key handler posts a scan through the dispatcher, superseding the timer. No direct
            // RunDiscoveryAsync call here: the whole flow runs off the keystroke.
            window.SetText("BranchBox", "main");
            window.PressKeyOn("BranchBox", Key.Enter);

            // The scan starts on a posted dispatcher job and lands on a background continuation:
            // pump-and-poll until the phase machine leaves Idle/Scanning (the 5s ceiling is slack
            // for a loaded CI box, not an expectation).
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (vm.Phase is DiscoveryPhase.Idle or DiscoveryPhase.Scanning && DateTime.UtcNow < deadline)
            {
                UiTestExtensions.Pump();
                await Task.Delay(50);
            }

            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Found);
            await Assert.That(vm.Targets.Count).IsEqualTo(1);
        });
    }
}
