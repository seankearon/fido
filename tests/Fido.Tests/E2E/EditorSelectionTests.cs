using System.Linq;
using Avalonia.Input;
using Avalonia.Threading;
using Fido.Models;
using Fido.Tests.Infrastructure;
using Fido.ViewModels;

namespace Fido.Tests.E2E;

/// <summary>Launching into a chosen editor, and the main window's editor launch-option wiring.</summary>
[NotInParallel]
public class EditorSelectionTests
{
    [Test]
    public async Task The_default_editor_drives_the_open_button_and_is_used_on_open()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            // Defaults are seeded by ConfigService; Rider is the default, the rest are secondary.
            await Assert.That(window.Vm().OpenButtonText).IsEqualTo("Open in Rider");
            await Assert.That(window.Vm().HasSecondaryEditors).IsTrue();

            await window.Open("main", "Foo");

            await Assert.That(launcher.Launches.Count).IsEqualTo(1);
            await Assert.That(launcher.LastLaunch!.Value.Editor.Kind).IsEqualTo(EditorKind.Rider);
        });
    }

    [Test]
    public async Task Opening_with_a_chosen_editor_launches_that_editor()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            var vsCode = new Editor { Name = "VS Code", Kind = EditorKind.VsCode };

            window.SetText("BranchBox", "main");
            window.SetText("SolutionBox", "Foo");
            await window.RunOpenAsync(editor: vsCode);   // as a Ctrl+N shortcut / secondary button would

            await Assert.That(launcher.Launches.Count).IsEqualTo(1);
            await Assert.That(launcher.LastLaunch!.Value.Editor.Name).IsEqualTo("VS Code");
            await Assert.That(window.Vm().StatusText).Contains("VS Code launched");
        });
    }

    [Test]
    public async Task Ctrl_n_launches_the_nth_configured_editor()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            window.SetText("BranchBox", "main");
            window.SetText("SolutionBox", "Foo");

            // Seeded order is [Rider, WebStorm, VS Code, Visual Studio, Zed, Console, File Explorer]; Ctrl+3 → index 2 → VS Code.
            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.D3,
                KeyModifiers = KeyModifiers.Control,
            });
            Dispatcher.UIThread.RunJobs();

            var completed = await Task.WhenAny(launcher.FirstLaunch, Task.Delay(TimeSpan.FromSeconds(10)));
            await Assert.That(completed).IsEqualTo((Task)launcher.FirstLaunch);
            await Assert.That(launcher.LastLaunch!.Value.Editor.Kind).IsEqualTo(EditorKind.VsCode);
        });
    }

    [Test]
    public async Task The_open_folder_chooser_entry_names_the_chosen_editor()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var launcher = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], launcher, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            var vsCode = new Editor { Name = "VS Code", Kind = EditorKind.VsCode };

            window.SetText("BranchBox", "main");
            window.SetText("SolutionBox", "");   // branch-only flow → folder chooser lists solutions + "open folder"
            await window.RunOpenAsync(editor: vsCode);

            var items = dialogs.LastChooser!.Items;
            await Assert.That(items.Any(i => i.Title == "Open this folder in VS Code")).IsTrue();
            await Assert.That(items.Any(i => i.Title == "Open this folder in Rider")).IsFalse();
        });
    }

    [Test]
    public async Task A_folder_only_editor_opens_the_folder_even_in_solution_mode()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            var webStorm = new Editor { Name = "WebStorm", Kind = EditorKind.WebStorm };

            await Assert.That(window.Vm().IsSolutionMode).IsTrue();   // default mode would hand over the .sln

            window.SetText("BranchBox", "main");
            window.SetText("SolutionBox", "Foo");
            await window.RunOpenAsync(editor: webStorm);

            // WebStorm has no concept of a solution, so the folder is handed over despite solution mode.
            await Assert.That(launcher.LastLaunch!.Value.Target).IsNotEmpty();
            await Assert.That(launcher.LastLaunch!.Value.Target).DoesNotEndWith("Foo.sln");
            await Assert.That(Paths.StartsWith(launcher.LastLaunch!.Value.Target, clone)).IsTrue();
            await Assert.That(window.Vm().StatusText).Contains("folder");
        });
    }

    [Test]
    public async Task A_folder_only_editor_skips_the_branch_folder_target_chooser()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.CreateBranch(clone, "feature/x");

        var launcher = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], launcher, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            var webStorm = new Editor { Name = "WebStorm", Kind = EditorKind.WebStorm };

            window.SetText("BranchBox", "feature/x");
            window.SetText("SolutionBox", "");   // branch-only flow → would normally offer solution-or-folder
            await window.RunOpenAsync(editor: webStorm);

            // A single folder on the branch and a folder-only editor → no chooser at all, folder handed over.
            await Assert.That(dialogs.ChooserRequests.Count).IsEqualTo(0);
            await Assert.That(launcher.LastLaunch!.Value.Target).DoesNotEndWith("Foo.sln");
            await Assert.That(Paths.StartsWith(launcher.LastLaunch!.Value.Target, clone)).IsTrue();
        });
    }

    [Test]
    public async Task Opening_with_the_console_hands_over_the_folder()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            var console = new Editor { Name = "Console", Kind = EditorKind.Console };

            window.SetText("BranchBox", "main");
            window.SetText("SolutionBox", "Foo");   // solution mode, but a console only ever opens the folder
            await window.RunOpenAsync(editor: console);

            await Assert.That(launcher.LastLaunch!.Value.Editor.Kind).IsEqualTo(EditorKind.Console);
            await Assert.That(launcher.LastLaunch!.Value.Target).DoesNotEndWith("Foo.sln");
            await Assert.That(Paths.StartsWith(launcher.LastLaunch!.Value.Target, clone)).IsTrue();
            await Assert.That(window.Vm().StatusText).Contains("folder");
        });
    }

    [Test]
    public async Task Secondary_editors_carry_numbered_shortcut_gestures()
    {
        var vm = new MainWindowViewModel();
        vm.SetEditors(new List<Editor>
        {
            new() { Name = "Rider", Kind = EditorKind.Rider },
            new() { Name = "VS Code", Kind = EditorKind.VsCode },
            new() { Name = "Zed", Kind = EditorKind.Zed },
        }, defaultIndex: 0);

        await Assert.That(vm.OpenButtonText).IsEqualTo("Open in Rider");
        await Assert.That(vm.SecondaryEditors.Count).IsEqualTo(2);
        await Assert.That(vm.SecondaryEditors[0].Name).IsEqualTo("VS Code");
        await Assert.That(vm.SecondaryEditors[0].Gesture).IsEqualTo("Ctrl+2");   // editor index 1 → Ctrl+2
        await Assert.That(vm.SecondaryEditors[1].Gesture).IsEqualTo("Ctrl+3");
    }
}
