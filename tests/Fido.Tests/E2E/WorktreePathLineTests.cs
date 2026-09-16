using System.IO;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Fido.Models;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.E2E;

/// <summary>
/// The line under the branch box: the worktree folders the typed branch already has <em>on disk</em>,
/// with a button to copy each path and one to open it in the OS file manager. The path is worked out
/// the way a worktree would be created — under a configured worktree root, else beside each clone the
/// scan reached — but only folders that exist are listed, so the line reports what is there and says
/// nothing when there is nothing to say.
/// </summary>
[NotInParallel]
public class WorktreePathLineTests
{
    [Test]
    public async Task A_worktree_root_answers_as_you_type_when_the_folder_is_there()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var worktrees = world.SearchRoot("worktrees");
        var folder = Path.Combine(worktrees, "feature-new-ui");
        Directory.CreateDirectory(folder);
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService(),
            worktreeRoot: worktrees);

        await Harness.WithWindow(services, async window =>
        {
            var vm = window.Vm();
            await Assert.That(vm.HasWorktreePaths).IsFalse();   // nothing typed yet

            window.SetText("BranchBox", "feature/new-ui");

            // No scan needed: a root is repo-independent, so the branch name and the disk settle it.
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Idle);
            await Assert.That(vm.WorktreePaths.Count).IsEqualTo(1);
            await Assert.That(vm.WorktreePaths[0].Path).IsEqualTo(folder);
            await Assert.That(vm.WorktreePaths[0].HasRepoName).IsFalse();
            Screenshots.Save(window, "worktree-path-line-root");

            // Clearing the box takes the line away again.
            window.SetText("BranchBox", "");
            await Assert.That(vm.HasWorktreePaths).IsFalse();
        });
    }

    [Test]
    public async Task A_branch_with_no_folder_anywhere_shows_nothing()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var worktrees = world.SearchRoot("worktrees");   // the root is there; this branch's folder is not
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService(),
            worktreeRoot: worktrees);

        await Harness.WithWindow(services, async window =>
        {
            window.SetText("BranchBox", "never-existed");

            await Assert.That(window.Vm().HasWorktreePaths).IsFalse();
            await Assert.That(window.FindControl<ItemsControl>("WorktreePathRows")!.IsVisible).IsFalse();
        });
    }

    [Test]
    public async Task Without_a_root_only_the_repos_that_have_the_folder_are_listed()
    {
        using var world = new TestRepoWorld();
        var main = world.SearchRoot("main");
        var fido = world.Clone(world.CreateOrigin("fido", "Fido"), main, "fido");
        world.Clone(world.CreateOrigin("klippy", "Klippy"), main, "klippy");
        // fido has a folder for 'xyz'; klippy doesn't. No repo has a *branch* called xyz.
        var folder = Path.Combine(main, "fido.worktrees", "xyz");
        Directory.CreateDirectory(folder);

        var services = world.BuildServices([fido, Path.Combine(main, "klippy")],
            new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("xyz");

            var vm = window.Vm();
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.NotFound);
            await Assert.That(vm.WorktreePaths.Count).IsEqualTo(1);
            await Assert.That(vm.WorktreePaths[0].RepoName).IsEqualTo("fido");
            await Assert.That(vm.WorktreePaths[0].Path).IsEqualTo(folder);
            Screenshots.Save(window, "worktree-path-line-on-disk");
        });
    }

    [Test]
    public async Task A_real_worktree_of_the_branch_is_listed_too()
    {
        using var world = new TestRepoWorld();
        var main = world.SearchRoot("main");
        var clone = world.Clone(world.CreateOrigin("platform", "Platform"), main, "platform");
        var worktree = world.AddWorktree(clone, "feature/x");

        var services = world.BuildServices([clone], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");

            var vm = window.Vm();
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Found);
            await Assert.That(vm.WorktreePaths.Count).IsEqualTo(1);
            await Assert.That(vm.WorktreePaths[0].Path).IsEqualTo(worktree);
        });
    }

    [Test]
    public async Task The_clones_outlive_their_scan_so_the_next_branch_answers_instantly()
    {
        using var world = new TestRepoWorld();
        var main = world.SearchRoot("main");
        var clone = world.Clone(world.CreateOrigin("platform", "Platform"), main, "platform");
        Directory.CreateDirectory(Path.Combine(main, "platform.worktrees", "feature-other"));

        var services = world.BuildServices([clone], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("xyz");
            await Assert.That(window.Vm().HasWorktreePaths).IsFalse();   // no folder for xyz

            // A different branch, typed with no scan behind it: the clones don't depend on the branch,
            // so the line re-answers from what the last scan already reached.
            window.SetText("BranchBox", "feature/other");

            var vm = window.Vm();
            await Assert.That(vm.WorktreePaths.Count).IsEqualTo(1);
            await Assert.That(vm.WorktreePaths[0].Path)
                .IsEqualTo(Path.Combine(main, "platform.worktrees", "feature-other"));
            await Assert.That(vm.WorktreePaths[0].RepoName).IsEqualTo("platform");
        });
    }

    [Test]
    public async Task Setting_a_worktree_root_in_settings_answers_the_line_straight_away()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var worktrees = world.SearchRoot("worktrees");
        Directory.CreateDirectory(Path.Combine(worktrees, "feature-new-ui"));
        var dialogs = new FakeDialogService { OnShowSettings = config => config.WorktreeRoot = worktrees };
        var services = world.BuildServices([root], new FakeEditorLauncher(), dialogs);

        await Harness.WithWindow(services, async window =>
        {
            window.SetText("BranchBox", "feature/new-ui");
            await Assert.That(window.Vm().HasWorktreePaths).IsFalse();

            await window.ShowSettingsAsync();

            await Assert.That(window.Vm().WorktreePaths.Count).IsEqualTo(1);
            await Assert.That(window.Vm().WorktreePaths[0].Path)
                .IsEqualTo(Path.Combine(worktrees, "feature-new-ui"));
        });
    }

    [Test]
    public async Task Copying_a_row_puts_the_whole_path_on_the_clipboard()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var worktrees = world.SearchRoot("worktrees");
        var expected = Path.Combine(worktrees, "feature-new-ui");
        Directory.CreateDirectory(expected);
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService(),
            worktreeRoot: worktrees);

        await Harness.WithWindow(services, async window =>
        {
            window.SetText("BranchBox", "feature/new-ui");

            await window.CopyWorktreePathAsync(window.Vm().WorktreePaths[0].Path);

            await Assert.That(window.LogText()).Contains($"📋 Copied worktree path to clipboard: {expected}");

            var clipboard = TopLevel.GetTopLevel(window)?.Clipboard;
            await Assert.That(clipboard).IsNotNull();
            await Assert.That(await clipboard!.TryGetTextAsync()).IsEqualTo(expected);
        });
    }

    [Test]
    public async Task Opening_a_row_launches_the_file_manager_on_its_folder()
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

            window.OpenWorktreePathInFileManager(window.Vm().WorktreePaths[0].Path);

            await Assert.That(launcher.Launches.Count).IsEqualTo(1);
            await Assert.That(launcher.LastLaunch!.Value.Target).IsEqualTo(folder);
            await Assert.That(launcher.LastLaunch!.Value.Editor.Kind).IsEqualTo(EditorKind.FileExplorer);
            await Assert.That(window.LogText()).Contains($"▸ Opening {folder} in File Explorer");
        });
    }

    [Test]
    public async Task A_folder_deleted_since_the_row_was_drawn_opens_the_nearest_one_left()
    {
        // Rows only ever carry folders that existed when they were built, so this is the race: the
        // folder went away between drawing the row and clicking it. Opening the nearest surviving
        // parent beats failing at a path that is still on screen.
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var worktrees = world.SearchRoot("worktrees");
        var vanished = Path.Combine(worktrees, "feature-new-ui");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService(), worktreeRoot: worktrees);

        await Harness.WithWindow(services, async window =>
        {
            window.OpenWorktreePathInFileManager(vanished);

            await Assert.That(launcher.LastLaunch!.Value.Target).IsEqualTo(worktrees);
            await Assert.That(window.LogText()).Contains($"▸ {vanished} doesn't exist yet — opening {worktrees}");
        });
    }

    [Test]
    public async Task The_rows_are_real_buttons_wired_to_their_own_path()
    {
        using var world = new TestRepoWorld();
        var main = world.SearchRoot("main");
        var clone = world.Clone(world.CreateOrigin("platform", "Platform"), main, "platform");
        var folder = Path.Combine(main, "platform.worktrees", "xyz");
        Directory.CreateDirectory(folder);

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([clone], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("xyz");

            // Click the open button rendered for the row, not the handler behind it.
            var rows = window.FindControl<ItemsControl>("WorktreePathRows")!;
            var buttons = rows.GetVisualDescendants().OfType<Button>().ToList();
            await Assert.That(buttons.Count).IsEqualTo(2);   // copy + open, for the one repo
            buttons[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            UiTestExtensions.Pump();

            await Assert.That(launcher.Launches.Count).IsEqualTo(1);
            await Assert.That(launcher.LastLaunch!.Value.Target).IsEqualTo(folder);
        });
    }

    [Test]
    public async Task A_search_root_that_isnt_on_this_machine_doesnt_stop_the_others_answering()
    {
        using var world = new TestRepoWorld();
        var main = world.SearchRoot("main");
        var fido = world.Clone(world.CreateOrigin("fido", "Fido"), main, "fido");
        var folder = Path.Combine(main, "fido.worktrees", "xyz");
        Directory.CreateDirectory(folder);
        var missing = Path.Combine(world.Root, "no-such-root");

        var services = world.BuildServices([missing, fido], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("xyz");

            var vm = window.Vm();
            await Assert.That(vm.WorktreePaths.Count).IsEqualTo(1);
            await Assert.That(vm.WorktreePaths[0].Path).IsEqualTo(folder);
        });
    }
}
