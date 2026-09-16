using System.IO;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Fido.Models;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.E2E;

/// <summary>
/// The line under the branch box: where the typed branch's worktree would live, worked out from the
/// branch name alone — so it answers while you type, before any scan and whether or not the folder is
/// there yet — with a button to copy it and one to open it in the OS file manager.
/// </summary>
[NotInParallel]
public class WorktreePathLineTests
{
    [Test]
    public async Task Typing_a_branch_shows_where_its_worktree_would_live()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var worktrees = world.SearchRoot("worktrees");
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService(),
            worktreeRoot: worktrees);

        await Harness.WithWindow(services, async window =>
        {
            var vm = window.Vm();
            var row = window.FindControl<Grid>("WorktreePathRow")!;

            // Nothing typed yet: no branch, so no path and no line.
            await Assert.That(vm.HasProposedWorktreePath).IsFalse();
            await Assert.That(row.IsVisible).IsFalse();

            window.SetText("BranchBox", "feature/new-ui");

            // No scan has run — the branch name alone settled it.
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Idle);
            await Assert.That(vm.ProposedWorktreePath)
                .IsEqualTo(Path.Combine(worktrees, "feature-new-ui"));
            await Assert.That(vm.HasProposedWorktreePath).IsTrue();
            await Assert.That(row.IsVisible).IsTrue();
            Screenshots.Save(window, "worktree-path-line");

            // Clearing the box takes the line away again.
            window.SetText("BranchBox", "");
            await Assert.That(vm.HasProposedWorktreePath).IsFalse();
            await Assert.That(row.IsVisible).IsFalse();
        });
    }

    [Test]
    public async Task With_no_worktree_root_the_line_stays_away()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            window.SetText("BranchBox", "feature/new-ui");

            // Worktrees are siblings of their clone here, so a branch name names one folder per repo
            // rather than one folder — the new-worktree card carries the path after a scan instead.
            await Assert.That(window.Vm().HasProposedWorktreePath).IsFalse();
            await Assert.That(window.FindControl<Grid>("WorktreePathRow")!.IsVisible).IsFalse();
        });
    }

    [Test]
    public async Task Setting_a_worktree_root_in_settings_answers_the_line_straight_away()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var worktrees = world.SearchRoot("worktrees");
        var dialogs = new FakeDialogService { OnShowSettings = config => config.WorktreeRoot = worktrees };
        var services = world.BuildServices([root], new FakeEditorLauncher(), dialogs);

        await Harness.WithWindow(services, async window =>
        {
            window.SetText("BranchBox", "feature/new-ui");
            await Assert.That(window.Vm().HasProposedWorktreePath).IsFalse();

            await window.ShowSettingsAsync();

            await Assert.That(window.Vm().ProposedWorktreePath)
                .IsEqualTo(Path.Combine(worktrees, "feature-new-ui"));
        });
    }

    [Test]
    public async Task Copying_the_line_puts_the_whole_path_on_the_clipboard()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var worktrees = world.SearchRoot("worktrees");
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService(),
            worktreeRoot: worktrees);

        await Harness.WithWindow(services, async window =>
        {
            window.SetText("BranchBox", "feature/new-ui");
            var expected = Path.Combine(worktrees, "feature-new-ui");

            await window.CopyWorktreePathAsync();

            await Assert.That(window.LogText()).Contains($"📋 Copied worktree path to clipboard: {expected}");

            var clipboard = TopLevel.GetTopLevel(window)?.Clipboard;
            await Assert.That(clipboard).IsNotNull();
            await Assert.That(await clipboard!.TryGetTextAsync()).IsEqualTo(expected);
        });
    }

    [Test]
    public async Task Opening_an_existing_worktree_folder_launches_the_file_manager_on_it()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var worktrees = world.SearchRoot("worktrees");
        var folder = Path.Combine(worktrees, "feature-new-ui");
        Directory.CreateDirectory(folder);

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService(), worktreeRoot: worktrees);

        await Harness.WithWindow(services, async window =>
        {
            window.SetText("BranchBox", "feature/new-ui");

            window.ClickButton("OpenWorktreePathButton");

            await Assert.That(launcher.Launches.Count).IsEqualTo(1);
            await Assert.That(launcher.LastLaunch!.Value.Target).IsEqualTo(folder);
            await Assert.That(launcher.LastLaunch!.Value.Editor.Kind).IsEqualTo(EditorKind.FileExplorer);
            await Assert.That(window.LogText()).Contains($"▸ Opening {folder} in File Explorer");
        });
    }

    [Test]
    public async Task A_worktree_that_doesnt_exist_yet_opens_the_nearest_folder_that_does()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var worktrees = world.SearchRoot("worktrees");   // the root exists; the branch's folder doesn't

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService(), worktreeRoot: worktrees);

        await Harness.WithWindow(services, async window =>
        {
            window.SetText("BranchBox", "feature/new-ui");
            var missing = Path.Combine(worktrees, "feature-new-ui");

            window.ClickButton("OpenWorktreePathButton");

            await Assert.That(launcher.LastLaunch!.Value.Target).IsEqualTo(worktrees);
            await Assert.That(window.LogText()).Contains($"▸ {missing} doesn't exist yet — opening {worktrees}");
        });
    }
}
