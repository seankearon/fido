using Fido.Models;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.E2E;

/// <summary>
/// The solution-chip row on the redesigned main window: every detected solution renders as a chip
/// (plus the trailing Folder chip) with the first auto-selected, the Solution box filters the chips
/// and re-selects the first survivor, and solution-capable tools launch whichever chip is selected —
/// a specific .sln, or the working tree itself via the Folder chip.
/// </summary>
[NotInParallel]
public class SolutionChipTests
{
    [Test]
    public async Task Two_solutions_render_both_chips_and_the_filter_narrows_and_restores_them()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        // A second solution deeper in the tree (uncommitted is fine — discovery scans the filesystem).
        // Root files are scanned before subfolders, so Foo.sln deterministically leads the chip row.
        TestRepoWorld.WriteSolutionFile(clone, Path.Combine("src", "Bar.sln"));

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");

            // Both solutions plus the trailing Folder chip, first solution auto-selected.
            var vm = window.Vm();
            await Assert.That(vm.HasSolutionChips).IsTrue();
            await Assert.That(vm.SolutionChips.Count).IsEqualTo(3);
            await Assert.That(vm.SolutionChips[0].Label).IsEqualTo("Foo.sln");
            await Assert.That(vm.SolutionChips[1].Label).IsEqualTo("Bar.sln");
            await Assert.That(vm.SolutionChips[2].IsFolder).IsTrue();
            await Assert.That(vm.SelectedSolutionChip).IsEqualTo(vm.SolutionChips[0]);

            // Typing in the Solution box narrows the chips and re-selects the first survivor.
            window.SetText("SolutionBox", "Bar");
            await Assert.That(vm.SolutionChips.Count).IsEqualTo(2);   // Bar.sln + Folder
            await Assert.That(vm.HasSolutionChips).IsTrue();
            await Assert.That(vm.SelectedSolutionChip!.Label).IsEqualTo("Bar.sln");

            // A filter matching nothing leaves only the Folder chip, selected by default.
            window.SetText("SolutionBox", "zzz");
            await Assert.That(vm.SolutionChips.Count).IsEqualTo(1);
            await Assert.That(vm.HasSolutionChips).IsFalse();
            await Assert.That(vm.SelectedSolutionChip!.IsFolder).IsTrue();

            // Clearing the filter restores every chip and the first-solution selection.
            window.SetText("SolutionBox", "");
            await Assert.That(vm.SolutionChips.Count).IsEqualTo(3);
            await Assert.That(vm.SelectedSolutionChip!.Label).IsEqualTo("Foo.sln");
        });
    }

    [Test]
    public async Task Rider_opens_the_chosen_chip_second_solution_or_the_folder()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        TestRepoWorld.WriteSolutionFile(clone, Path.Combine("src", "Bar.sln"));

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");

            // Pick the SECOND solution chip instead of the auto-selected first one.
            var vm = window.Vm();
            await Assert.That(vm.SelectedSolutionChip!.Label).IsEqualTo("Foo.sln");
            vm.SelectedSolutionChip = vm.SolutionChips[1];
            UiTestExtensions.Pump();

            // Rider is solution-capable → it launches THAT solution, not the first.
            await window.OpenWithAsync(new Editor { Name = "Rider", Kind = EditorKind.Rider });
            await Assert.That(launcher.Launches.Count).IsEqualTo(1);
            await Assert.That(launcher.LastLaunch!.Value.Target).EndsWith("Bar.sln");
            await Assert.That(Paths.StartsWith(launcher.LastLaunch!.Value.Target, clone)).IsTrue();

            // The trailing Folder chip means "open the working tree itself", even for Rider.
            vm.SelectedSolutionChip = vm.SolutionChips[^1];
            UiTestExtensions.Pump();
            await Assert.That(vm.SelectedSolutionChip!.IsFolder).IsTrue();

            await window.OpenWithAsync(new Editor { Name = "Rider", Kind = EditorKind.Rider });
            await Assert.That(launcher.Launches.Count).IsEqualTo(2);
            await Assert.That(launcher.LastLaunch!.Value.Target).DoesNotEndWith(".sln");
            await Assert.That(Paths.StartsWith(launcher.LastLaunch!.Value.Target, clone)).IsTrue();
        });
    }

    [Test]
    public async Task A_worktree_with_no_solutions_auto_selects_the_folder_chip_and_rider_opens_the_folder()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/no-sln");
        File.Delete(Path.Combine(worktree, "Foo.sln"));   // leave the worktree with no solution files

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/no-sln");

            // No solutions detected → the chip row collapses to just Folder, pre-selected.
            var vm = window.Vm();
            await Assert.That(vm.Targets.Count).IsEqualTo(1);
            await Assert.That(vm.Targets[0].IsWorktree).IsTrue();
            await Assert.That(vm.HasSolutionChips).IsFalse();
            await Assert.That(vm.SolutionChips.Count).IsEqualTo(1);
            await Assert.That(vm.SelectedSolutionChip!.IsFolder).IsTrue();

            // With nothing to open as a solution, even Rider gets the worktree folder.
            await window.OpenWithAsync(new Editor { Name = "Rider", Kind = EditorKind.Rider });
            await Assert.That(launcher.Launches.Count).IsEqualTo(1);
            await Assert.That(launcher.LastLaunch!.Value.Target).DoesNotEndWith(".sln");
            await Assert.That(Paths.StartsWith(launcher.LastLaunch!.Value.Target, worktree)).IsTrue();
        });
    }
}
