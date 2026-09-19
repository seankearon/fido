using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;
using Fido.ViewModels;
using Fido.Views;

namespace Fido.Tests.E2E;

/// <summary>
/// In-repo configuration end to end: a branch carrying <c>.fido/cfg.yaml</c> pre-selects the main clone
/// and stocks the Console button's run menu, and picking from that menu runs the command in a console at
/// the selected location. The config belongs to the scan, so a new branch starts from nothing.
/// </summary>
[NotInParallel]
public class RepoConfigTests
{
    /// <summary>The Console tool's grid button — the one the run menu hangs off.</summary>
    private static EditorLaunchOption ConsoleTool(MainWindow window) =>
        window.Vm().GridTools.First(t => t.Name == "Console");

    [Test]
    public async Task Prefer_main_clone_changes_the_default_choice_not_the_scan()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var rootA = world.SearchRoot("rootA");
        var rootB = world.SearchRoot("rootB");
        var cloneA = world.Clone(origin, rootA, "Foo");
        var cloneB = world.Clone(origin, rootB, "Foo");
        world.CreateBranch(cloneA, "feature/cfg");              // cloneA's main tree is on the branch
        var worktree = world.AddWorktree(cloneB, "feature/cfg");  // cloneB has a worktree on it

        var launcher = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([rootA, rootB], launcher, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            // Worktrees lead the results, so without a config the worktree is the default choice.
            await window.Discover("feature/cfg");
            var vm = window.Vm();
            await Assert.That(vm.Targets.Count).IsEqualTo(2);
            await Assert.That(vm.SelectedTarget!.IsWorktree).IsTrue();

            // The branch asks for the main clone; the next scan offers that one by default instead.
            TestRepoWorld.WriteFidoConfig(worktree, "prefer main clone: true\n");
            await window.Discover("feature/cfg");
            Screenshots.Save(window, "repo-config-prefer-main-clone");

            await Assert.That(vm.SelectedTarget!.IsMainClone).IsTrue();
            await Assert.That(Paths.StartsWith(vm.SelectedPath, cloneA)).IsTrue();
            await Assert.That(window.LogText()).Contains(".fido/cfg.yaml on 'feature/cfg' — main clone preferred");

            // The setting directs the choice, not the scan: the same two locations are found, in the
            // same order (worktrees first), and the worktree is still right there to pick.
            await Assert.That(vm.Targets.Count).IsEqualTo(2);
            await Assert.That(vm.Targets[0].IsWorktree).IsTrue();
            await Assert.That(vm.Targets[1].IsMainClone).IsTrue();
            await Assert.That(window.LogText()).Contains("✓ Found 2 location(s) for 'feature/cfg'.");
        });
    }

    [Test]
    public async Task Run_files_and_aspire_start_become_the_console_buttons_menu()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/runs");

        File.WriteAllText(Path.Combine(worktree, "build.ps1"), "");
        File.WriteAllText(Path.Combine(worktree, "notes.md"), "");   // not a script: never offered
        TestRepoWorld.WriteFidoConfig(worktree,
            """
            run files:
              - '*'
            aspire start: true
            """);

        var launcher = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], launcher, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/runs");
            Screenshots.Save(window, "repo-config-console-runs");

            // The wildcard found the one root script, and `aspire start` rides at the end.
            var console = ConsoleTool(window);
            await Assert.That(console.HasRuns).IsTrue();
            await Assert.That(string.Join('|', console.Runs.Select(r => r.Label)))
                .IsEqualTo("build.ps1|aspire start");
            await Assert.That(window.LogText()).Contains("2 console run option(s)");

            // Only the Console tool grows a menu — the editors have nothing to do with run files.
            foreach (var other in window.Vm().GridTools.Where(t => t.Name != "Console"))
                await Assert.That(other.HasRuns).IsFalse();
            await Assert.That(window.Vm().HasHeroRuns).IsFalse();   // Rider is the hero here

            // Picking one opens the console at the selected location and runs it there.
            await window.RunConsoleOptionAsync(console.Runs[0]);

            await Assert.That(launcher.Launches.Count).IsEqualTo(1);
            var launch = launcher.LastLaunch!.Value;
            await Assert.That(launch.Editor.Kind).IsEqualTo(EditorKind.Console);
            await Assert.That(launch.ConsoleCommand).IsEqualTo("build.ps1");
            await Assert.That(Paths.StartsWith(launch.Target, worktree)).IsTrue();
            await Assert.That(window.LogText()).Contains("▸ Running 'build.ps1' in");
        });
    }

    [Test]
    public async Task The_rendered_caret_opens_a_menu_whose_rows_run_their_command()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/menu");
        TestRepoWorld.WriteFidoConfig(worktree, "run files: [build.ps1]\naspire start: true\n");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/menu");

            // Every tool button is built with a caret, but only the Console tool's is ever shown — and
            // the hero's copy stays hidden here, since Rider is the default tool.
            var carets = window.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("runcaret")).ToList();
            await Assert.That(carets.Count(b => b.IsVisible)).IsEqualTo(1);
            var caret = carets.Single(b => b.IsVisible);

            // Open the real flyout and click a real row: this is the wiring the user actually touches.
            var flyout = (Flyout)caret.Flyout!;
            flyout.ShowAt(caret);
            UiTestExtensions.Pump();

            var rows = ((Control)flyout.Content!).GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("runitem")).ToList();
            await Assert.That(string.Join('|', rows.Select(r => r.Content))).IsEqualTo("build.ps1|aspire start");

            rows[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            UiTestExtensions.Pump();

            var completed = await Task.WhenAny(launcher.FirstLaunch, Task.Delay(TimeSpan.FromSeconds(10)));
            await Assert.That(completed).IsEqualTo((Task)launcher.FirstLaunch);
            await Assert.That(launcher.LastLaunch!.Value.Editor.Kind).IsEqualTo(EditorKind.Console);
            await Assert.That(launcher.LastLaunch!.Value.ConsoleCommand).IsEqualTo("aspire start");
        });
    }

    [Test]
    public async Task Aspire_start_alone_is_offered_without_any_run_files()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/aspire");
        TestRepoWorld.WriteFidoConfig(worktree, "aspire start: true\n");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/aspire");

            var console = ConsoleTool(window);
            await Assert.That(console.Runs.Count).IsEqualTo(1);
            await Assert.That(console.Runs[0].Command).IsEqualTo("aspire start");

            await window.RunConsoleOptionAsync(console.Runs[0]);

            await Assert.That(launcher.LastLaunch!.Value.ConsoleCommand).IsEqualTo("aspire start");
        });
    }

    [Test]
    public async Task The_menu_follows_the_console_tool_when_it_is_the_hero()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/hero");
        TestRepoWorld.WriteFidoConfig(worktree, "aspire start: true\n");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/hero");
            var vm = window.Vm();
            await Assert.That(vm.HasHeroRuns).IsFalse();   // Rider is the default tool out of the box

            // Make Console the default: the run menu moves to the hero, and survives the tool list
            // being rebuilt mid-scan (which is what changing the default does).
            var editors = Editor.Defaults();
            vm.SetEditors(editors, editors.FindIndex(e => e.Kind == EditorKind.Console));
            UiTestExtensions.Pump();

            await Assert.That(vm.HasHeroRuns).IsTrue();
            await Assert.That(vm.HeroTool!.Runs[0].Command).IsEqualTo("aspire start");
            await Assert.That(vm.GridTools.Any(t => t.HasRuns)).IsFalse();
            Screenshots.Save(window, "repo-config-console-hero-runs");

            // The hero's caret is the visible one now, and its rows launch the same way.
            var carets = window.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("runcaret")).ToList();
            await Assert.That(carets.Count(b => b.IsVisible)).IsEqualTo(1);
            await Assert.That(carets.Single(b => b.IsVisible).Name).IsEqualTo("HeroRunsButton");

            await window.RunConsoleOptionAsync(vm.HeroTool!.Runs[0]);
            await Assert.That(launcher.LastLaunch!.Value.Editor.Kind).IsEqualTo(EditorKind.Console);
            await Assert.That(launcher.LastLaunch!.Value.ConsoleCommand).IsEqualTo("aspire start");
        });
    }

    [Test]
    public async Task The_run_menu_belongs_to_the_scanned_branch_and_nothing_else()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var configured = world.AddWorktree(clone, "feature/with-cfg");
        world.AddWorktree(clone, "feature/without-cfg");
        TestRepoWorld.WriteFidoConfig(configured, "run files: [build.ps1]\n");

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/with-cfg");
            await Assert.That(ConsoleTool(window).HasRuns).IsTrue();

            // A branch of its own with no config: the previous branch's menu must not linger.
            await window.Discover("feature/without-cfg");
            await Assert.That(ConsoleTool(window).HasRuns).IsFalse();

            // Nor after the branch box is cleared back to idle.
            await window.Discover("feature/with-cfg");
            await Assert.That(ConsoleTool(window).HasRuns).IsTrue();
            window.SetText("BranchBox", "");
            UiTestExtensions.Pump();
            await Assert.That(window.Vm().IsIdle).IsTrue();
            await Assert.That(ConsoleTool(window).HasRuns).IsFalse();
        });
    }

    [Test]
    public async Task A_checkout_behind_the_branch_still_gets_its_run_menu()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/stale");

        // The worktree was made before the repo had any Fido settings; they landed on the branch after,
        // and all this machine has done since is fetch. Nothing is on disk here to read.
        world.PushBranch(worktree, "feature/stale");
        world.CommitFidoConfigToOrigin(origin, "feature/stale", "run files: [build.ps1]\naspire start: true\n");
        TestRepoWorld.Fetch(clone);

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/stale");

            // The menu is the branch's, not the folder's — and the log says where it came from, because
            // the tree the commands will run in hasn't caught up with the settings running them.
            var console = ConsoleTool(window);
            await Assert.That(string.Join('|', console.Runs.Select(r => r.Label))).IsEqualTo("build.ps1|aspire start");
            await Assert.That(window.LogText()).Contains("Read from origin/feature/stale");
            await Assert.That(window.LogText()).Contains("2 console run option(s)");
            Screenshots.Save(window, "repo-config-from-origin");

            // And picking one still runs at the selected location, as it always has.
            await window.RunConsoleOptionAsync(console.Runs[0]);
            await Assert.That(launcher.LastLaunch!.Value.ConsoleCommand).IsEqualTo("build.ps1");
            await Assert.That(Paths.StartsWith(launcher.LastLaunch!.Value.Target, worktree)).IsTrue();
        });
    }

    [Test]
    public async Task A_branch_carrying_no_config_says_so_rather_than_saying_nothing()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.AddWorktree(clone, "feature/plain");

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/plain");

            // "Looked, found nothing" and "never looked" are not the same thing to anyone wondering where
            // their run menu went — and the line names both places that were checked.
            await Assert.That(ConsoleTool(window).HasRuns).IsFalse();
            await Assert.That(window.LogText()).Contains("No .fido/cfg.yaml on 'feature/plain'");
            await Assert.That(window.LogText()).Contains("origin/feature/plain");
        });
    }

    [Test]
    public async Task The_context_strip_creates_the_config_file_and_opens_it()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/init");
        File.WriteAllText(Path.Combine(worktree, "build.ps1"), "");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/init");

            // Nothing to run yet: the branch carries no config, so the Console button has no caret.
            await Assert.That(ConsoleTool(window).HasRuns).IsFalse();
            await Assert.That(window.FindControl<Button>("RepoConfigButton")!.IsVisible).IsTrue();

            window.ClickButton("RepoConfigButton");
            var completed = await Task.WhenAny(launcher.FirstLaunch, Task.Delay(TimeSpan.FromSeconds(10)));
            await Assert.That(completed).IsEqualTo((Task)launcher.FirstLaunch);
            Screenshots.Save(window, "repo-config-created");

            // The file is seeded in the selected location and handed to the default tool to edit.
            var path = RepoConfigService.PathIn(worktree);
            await Assert.That(File.Exists(path)).IsTrue();
            await Assert.That(window.LogText()).Contains("✓ Created");
            await Assert.That(launcher.LastLaunch!.Value.Editor.Kind).IsEqualTo(EditorKind.Rider);
            await Assert.That(launcher.LastLaunch!.Value.Target).IsEqualTo(path);

            // Seeded from what the scan already knows, and inert until edited: a rescan still finds
            // nothing to apply, so no run menu appears behind the user's back.
            await Assert.That(await File.ReadAllTextAsync(path)).Contains("build.ps1");
            await window.Discover("feature/init");
            await Assert.That(ConsoleTool(window).HasRuns).IsFalse();

            // Clicking again opens the file as it is — a second click must never re-seed it.
            await File.WriteAllTextAsync(path, "aspire start: true\n");
            window.ClickButton("RepoConfigButton");
            UiTestExtensions.Pump();
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo("aspire start: true\n");
            await Assert.That(window.LogText()).Contains("already exists");
        });
    }

    [Test]
    public async Task A_placement_offer_has_nowhere_to_write_the_config_yet()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.CreateBranch(clone, "feature/unplaced");
        TestRepoWorld.Git(clone, "switch", "main");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/unplaced");
            await Assert.That(window.Vm().SelectedTarget!.IsPlacement).IsTrue();

            // No tree on disk to write into, so the action isn't offered at all…
            await Assert.That(window.Vm().CanEditRepoConfig).IsFalse();
            await Assert.That(window.FindControl<Button>("RepoConfigButton")!.IsVisible).IsFalse();

            // …and the run menu's footer row, which can still be reached, says why rather than failing.
            await window.EditRepoConfigAsync();
            await Assert.That(window.LogText()).Contains("isn't on disk here yet");
            await Assert.That(launcher.Launches.Count).IsEqualTo(0);

            // Selecting the worktree the placement would create is what unlocks it.
            window.Vm().SelectedTarget = window.Vm().Targets.First(t => t.IsSwitchClone);
            UiTestExtensions.Pump();
            await Assert.That(window.Vm().CanEditRepoConfig).IsFalse();
        });
    }

    [Test]
    public async Task A_config_committed_on_an_unplaced_branch_is_read_before_the_offers()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");

        // The branch exists in the clone but is checked out nowhere: Fido offers to place it, and reads
        // the config straight off the branch to do so.
        world.CreateBranch(clone, "feature/unplaced");
        File.WriteAllText(Path.Combine(clone, "build.sh"), "");
        TestRepoWorld.CommitFidoConfig(clone, "prefer main clone: true\nrun files: ['*']\n");
        TestRepoWorld.Git(clone, "switch", "main");

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/unplaced");
            var vm = window.Vm();

            // Both placement offers are up, and the main-tree switch — the main clone of the two — leads.
            await Assert.That(vm.Targets.Count).IsEqualTo(2);
            await Assert.That(vm.SelectedTarget!.IsSwitchClone).IsTrue();

            // The run file came out of the branch, not off a disk that hasn't been written yet.
            await Assert.That(string.Join('|', ConsoleTool(window).Runs.Select(r => r.Label)))
                .IsEqualTo("build.sh");
        });
    }
}
