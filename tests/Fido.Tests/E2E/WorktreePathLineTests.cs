using System.IO;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Fido.Models;
using Fido.Tests.Infrastructure;
using Fido.ViewModels;

namespace Fido.Tests.E2E;

/// <summary>
/// The line under the branch box: where the typed branch's worktree would live, with a button to copy
/// each path and one to open it in the OS file manager. A configured worktree root answers it from the
/// branch name alone, as you type; without one the sibling convention needs a repo, so the clones the
/// scan reached each contribute a row — and being branch-independent, they answer every later branch
/// without scanning again.
/// </summary>
[NotInParallel]
public class WorktreePathLineTests
{
    [Test]
    public async Task A_worktree_root_answers_as_you_type_with_no_scan_at_all()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var worktrees = world.SearchRoot("worktrees");
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService(),
            worktreeRoot: worktrees);

        await Harness.WithWindow(services, async window =>
        {
            var vm = window.Vm();

            // Nothing typed yet: no branch, so no path and no line.
            await Assert.That(vm.HasWorktreePaths).IsFalse();

            window.SetText("BranchBox", "feature/new-ui");

            // No scan has run — the branch name alone settled it, and a root is repo-independent so the
            // row carries no repo name.
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Idle);
            await Assert.That(vm.WorktreePaths.Count).IsEqualTo(1);
            await Assert.That(vm.WorktreePaths[0].Path).IsEqualTo(Path.Combine(worktrees, "feature-new-ui"));
            await Assert.That(vm.WorktreePaths[0].HasRepoName).IsFalse();
            await Assert.That(vm.HasExtraWorktreePaths).IsFalse();
            Screenshots.Save(window, "worktree-path-line-root");

            // Clearing the box takes the line away again.
            window.SetText("BranchBox", "");
            await Assert.That(vm.HasWorktreePaths).IsFalse();
        });
    }

    [Test]
    public async Task With_no_root_the_scanned_clones_each_offer_their_own_folder()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("platform", "Platform");
        var other = world.CreateOrigin("tools", "Tools");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "platform");
        world.Clone(other, root, "tools");

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            var vm = window.Vm();

            // Before any scan there is no repo to hang the sibling convention off, so nothing shows.
            window.SetText("BranchBox", "qwe");
            await Assert.That(vm.HasWorktreePaths).IsFalse();

            // The scan finds no branch called 'qwe' anywhere — but it does reach both clones, and that
            // is all the line needs.
            await window.Discover("qwe");
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.NotFound);

            await Assert.That(vm.WorktreePaths.Count).IsEqualTo(2);
            await Assert.That(vm.WorktreePaths.Select(p => p.Path)).Contains(
                Path.Combine(root, "platform.worktrees", "qwe"));
            await Assert.That(vm.WorktreePaths.Select(p => p.Path)).Contains(
                Path.Combine(root, "tools.worktrees", "qwe"));
            await Assert.That(vm.WorktreePaths.All(p => p.HasRepoName)).IsTrue();
            Screenshots.Save(window, "worktree-path-line-per-repo");
        });
    }

    [Test]
    public async Task The_clones_outlive_their_scan_so_the_next_branch_answers_instantly()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("platform", "Platform");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "platform");

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("qwe");

            // A different branch, typed with no scan behind it: the clones don't depend on the branch,
            // so the line re-answers from what the last scan already reached.
            window.SetText("BranchBox", "feature/other");

            var vm = window.Vm();
            await Assert.That(vm.WorktreePaths.Count).IsEqualTo(1);
            await Assert.That(vm.WorktreePaths[0].Path)
                .IsEqualTo(Path.Combine(root, "platform.worktrees", "feature-other"));
            await Assert.That(vm.WorktreePaths[0].RepoName).IsEqualTo("platform");
        });
    }

    [Test]
    public async Task A_repo_that_already_uses_worktrees_leads_and_the_rest_are_capped_with_a_note()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        // More clones than the line lists, so the cap and its note both come into play. "zulu" is last
        // alphabetically but the only one already working in worktrees, so it must still lead.
        var origin = world.CreateOrigin("seed", "Seed");
        for (var i = 0; i < MainWindowViewModel.MaxWorktreePaths + 2; i++)
            world.Clone(origin, root, $"repo{i}");
        var zulu = world.Clone(origin, root, "zulu");
        world.AddWorktree(zulu, "already-here");   // gives zulu its sibling .worktrees folder

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("qwe");

            var vm = window.Vm();
            await Assert.That(vm.WorktreePaths.Count).IsEqualTo(MainWindowViewModel.MaxWorktreePaths);
            await Assert.That(vm.WorktreePaths[0].RepoName).IsEqualTo("zulu");
            await Assert.That(vm.HasExtraWorktreePaths).IsTrue();
            await Assert.That(vm.ExtraWorktreePathsNote).Contains("more repo(s)");
            await Assert.That(vm.ExtraWorktreePathsNote).Contains("Worktree root");
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
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService(),
            worktreeRoot: worktrees);

        await Harness.WithWindow(services, async window =>
        {
            window.SetText("BranchBox", "feature/new-ui");
            var expected = Path.Combine(worktrees, "feature-new-ui");

            await window.CopyWorktreePathAsync(window.Vm().WorktreePaths[0].Path);

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

            window.OpenWorktreePathInFileManager(window.Vm().WorktreePaths[0].Path);

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

            window.OpenWorktreePathInFileManager(window.Vm().WorktreePaths[0].Path);

            await Assert.That(launcher.LastLaunch!.Value.Target).IsEqualTo(worktrees);
            await Assert.That(window.LogText()).Contains($"▸ {missing} doesn't exist yet — opening {worktrees}");
        });
    }

    [Test]
    public async Task The_rows_are_real_buttons_wired_to_their_own_path()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("platform", "Platform");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "platform");
        world.AddWorktree(clone, "already-here");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("qwe");

            // Click the open button rendered for the first row, not the handler behind it.
            var rows = window.FindControl<ItemsControl>("WorktreePathRows")!;
            var buttons = rows.GetVisualDescendants().OfType<Button>().ToList();
            await Assert.That(buttons.Count).IsEqualTo(2);   // copy + open, for the one repo
            buttons[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            UiTestExtensions.Pump();

            // The worktree container exists (already-here lives there) so it opens that, and the log
            // names the folder the row actually carried.
            await Assert.That(launcher.Launches.Count).IsEqualTo(1);
            await Assert.That(launcher.LastLaunch!.Value.Target)
                .IsEqualTo(Path.Combine(root, "platform.worktrees"));
            await Assert.That(window.LogText()).Contains(Path.Combine(root, "platform.worktrees", "qwe"));
        });
    }

    [Test]
    public async Task A_search_root_that_is_itself_a_clone_still_offers_its_sibling_worktree_folder()
    {
        // The reported layout: the search roots name the clones themselves (D:\main\fido,
        // D:\main\klippy) rather than a folder containing them, and each clone's worktrees live in a
        // SIBLING container (D:\main\fido.worktrees) that no search root covers.
        using var world = new TestRepoWorld();
        var main = world.SearchRoot("main");
        var fidoOrigin = world.CreateOrigin("fido", "Fido");
        var klippyOrigin = world.CreateOrigin("klippy", "Klippy");
        var fido = world.Clone(fidoOrigin, main, "fido");
        var klippy = world.Clone(klippyOrigin, main, "klippy");
        // A worktree on some other branch, which is what puts fido.worktrees on disk.
        world.AddWorktree(fido, "claude-something");

        var services = world.BuildServices([fido, klippy], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("xyz");

            var vm = window.Vm();
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.NotFound);   // no repo has 'xyz'

            // …and yet both clones say where 'xyz' would go, fido first: it is the one that already
            // has a worktree container beside it.
            await Assert.That(vm.WorktreePaths.Count).IsEqualTo(2);
            await Assert.That(vm.WorktreePaths[0].RepoName).IsEqualTo("fido");
            await Assert.That(vm.WorktreePaths[0].Path)
                .IsEqualTo(Path.Combine(main, "fido.worktrees", "xyz"));
            await Assert.That(vm.WorktreePaths[1].RepoName).IsEqualTo("klippy");
            await Assert.That(vm.WorktreePaths[1].Path)
                .IsEqualTo(Path.Combine(main, "klippy.worktrees", "xyz"));
            Screenshots.Save(window, "worktree-path-line-clone-roots");
        });
    }

    [Test]
    public async Task A_search_root_that_isnt_on_this_machine_doesnt_stop_the_others_answering()
    {
        // The reported config also listed a root that holds no clone at all; it must cost nothing.
        using var world = new TestRepoWorld();
        var main = world.SearchRoot("main");
        var fido = world.Clone(world.CreateOrigin("fido", "Fido"), main, "fido");
        var missing = Path.Combine(world.Root, "no-such-root");

        var services = world.BuildServices([missing, fido], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("xyz");

            var vm = window.Vm();
            await Assert.That(vm.WorktreePaths.Count).IsEqualTo(1);
            await Assert.That(vm.WorktreePaths[0].Path).IsEqualTo(Path.Combine(main, "fido.worktrees", "xyz"));
        });
    }
}
