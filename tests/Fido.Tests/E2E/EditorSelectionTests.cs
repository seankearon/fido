using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Fido.Models;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.E2E;

/// <summary>
/// Tool selection on the redesigned main window: the default tool renders as the hero button with the
/// rest in the shortcut grid, solution-capable tools (Rider / Visual Studio) honour the selected
/// solution chip while every other tool always opens the folder, clicks on the rendered hero / grid
/// buttons route through the real Click handlers, Ctrl+N accelerators launch by config order, and
/// nothing launches until discovery has found the branch.
/// </summary>
[NotInParallel]
public class EditorSelectionTests
{
    [Test]
    public async Task The_default_config_seeds_a_Rider_hero_and_a_six_tool_grid()
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

            // ConfigService seeds Editor.Defaults(); DefaultEditorIndex 0 makes Rider the hero.
            await Assert.That(vm.HasHero).IsTrue();
            await Assert.That(vm.HeroTool!.Name).IsEqualTo("Rider");
            await Assert.That(vm.HeroTool!.Gesture).IsEqualTo("Ctrl+1");
            await Assert.That(vm.HeroTool!.IsDefault).IsTrue();
            await Assert.That(vm.HeroLabel).IsEqualTo("Open in Rider");

            // The remaining six render in config order, gestures still tied to config position.
            await Assert.That(vm.GridTools.Count).IsEqualTo(6);
            string[] expectedNames = ["WebStorm", "VS Code", "Visual Studio", "Zed", "Console", "File Explorer"];
            for (var i = 0; i < expectedNames.Length; i++)
            {
                await Assert.That(vm.GridTools[i].Name).IsEqualTo(expectedNames[i]);
                await Assert.That(vm.GridTools[i].Gesture).IsEqualTo($"Ctrl+{i + 2}");
                await Assert.That(vm.GridTools[i].IsDefault).IsFalse();
            }
        });
    }

    [Test]
    [Arguments(EditorKind.Rider, "Rider")]
    [Arguments(EditorKind.VisualStudio, "Visual Studio")]
    public async Task Solution_capable_tools_open_the_selected_solution(EditorKind kind, string name)
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");

            // The first detected solution chip is auto-selected; a solution-capable tool hands it over.
            await Assert.That(window.Vm().SelectedSolutionChip!.SolutionPath).IsNotNull();
            await window.OpenWithAsync(new Editor { Name = name, Kind = kind });

            await Assert.That(launcher.Launches.Count).IsEqualTo(1);
            await Assert.That(launcher.LastLaunch!.Value.Editor.Kind).IsEqualTo(kind);
            await Assert.That(launcher.LastLaunch!.Value.Target).EndsWith("Foo.sln");
            await Assert.That(Paths.StartsWith(launcher.LastLaunch!.Value.Target, clone)).IsTrue();
        });
    }

    [Test]
    [Arguments(EditorKind.WebStorm, "WebStorm")]
    [Arguments(EditorKind.VsCode, "VS Code")]
    [Arguments(EditorKind.Zed, "Zed")]
    [Arguments(EditorKind.Console, "Console")]
    [Arguments(EditorKind.FileExplorer, "File Explorer")]
    public async Task Folder_only_tools_open_the_folder_even_with_a_solution_chip_selected(EditorKind kind, string name)
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");

            // A solution chip is selected, but these tools would open a .sln as a text file (or not at
            // all) — the folder is handed over and the chip ignored.
            await Assert.That(window.Vm().SelectedSolutionChip!.SolutionPath).IsNotNull();
            await window.OpenWithAsync(new Editor { Name = name, Kind = kind });

            await Assert.That(launcher.Launches.Count).IsEqualTo(1);
            await Assert.That(launcher.LastLaunch!.Value.Editor.Kind).IsEqualTo(kind);
            await Assert.That(launcher.LastLaunch!.Value.Target).DoesNotEndWith("Foo.sln");
            await Assert.That(Paths.StartsWith(launcher.LastLaunch!.Value.Target, clone)).IsTrue();
        });
    }

    [Test]
    public async Task Clicking_the_rendered_grid_and_hero_buttons_launches_their_bound_tools()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");

            // Click the real Zed button in the shortcut grid — the Click handler resolves the editor
            // from the clicked button's DataContext, so this exercises the actual rendered controls.
            var zed = window.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("tool"))
                .Single(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Zed"));
            zed.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            UiTestExtensions.Pump();   // the async-void handler resumes through the dispatcher

            var completed = await Task.WhenAny(launcher.FirstLaunch, Task.Delay(TimeSpan.FromSeconds(10)));
            await Assert.That(completed).IsEqualTo((Task)launcher.FirstLaunch);
            await Assert.That(launcher.LastLaunch!.Value.Editor.Kind).IsEqualTo(EditorKind.Zed);
            await Assert.That(launcher.LastLaunch!.Value.Target).DoesNotEndWith("Foo.sln");
            await Assert.That(Paths.StartsWith(launcher.LastLaunch!.Value.Target, clone)).IsTrue();

            // The hero is the one Button carrying the "hero" class (the "fido hero" input boxes are
            // AutoCompleteBoxes, not Buttons); default config makes it Rider, which is solution-capable
            // and so hands over the auto-selected solution chip rather than the folder.
            var hero = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("hero"));
            hero.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            UiTestExtensions.Pump();

            await Assert.That(launcher.Launches.Count).IsEqualTo(2);
            await Assert.That(launcher.LastLaunch!.Value.Editor.Kind).IsEqualTo(EditorKind.Rider);
            await Assert.That(launcher.LastLaunch!.Value.Target)
                .IsEqualTo(window.Vm().SelectedSolutionChip!.SolutionPath);
        });
    }

    [Test]
    public async Task Ctrl_n_launches_the_nth_configured_tool()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");

            // Seeded order is [Rider, WebStorm, VS Code, ...]; Ctrl+3 → index 2 → VS Code. PressKey has
            // no modifier parameter, so raise the routed event with Control set, as the real key press would.
            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.D3,
                KeyModifiers = KeyModifiers.Control,
            });
            UiTestExtensions.Pump();   // the handler posts the open back through the dispatcher

            var completed = await Task.WhenAny(launcher.FirstLaunch, Task.Delay(TimeSpan.FromSeconds(10)));
            await Assert.That(completed).IsEqualTo((Task)launcher.FirstLaunch);
            await Assert.That(launcher.LastLaunch!.Value.Editor.Kind).IsEqualTo(EditorKind.VsCode);
        });
    }

    [Test]
    public async Task Opening_launches_nothing_before_discovery_has_found_the_branch()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            // No scan has run — the phase machine still says Idle, so every open path is locked.
            await Assert.That(window.Vm().CanOpen).IsFalse();

            await window.OpenWithAsync(new Editor { Name = "Rider", Kind = EditorKind.Rider });

            await Assert.That(launcher.Launches.Count).IsEqualTo(0);
        });
    }
}
