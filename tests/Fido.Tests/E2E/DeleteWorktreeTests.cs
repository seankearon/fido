using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;
using Fido.ViewModels;
using Fido.Views;

namespace Fido.Tests.E2E;

/// <summary>
/// Scenario D: the branch-folder chooser's delete action removes a located linked worktree together with
/// its local branch and any branch on origin — driven end-to-end through the real window.
/// </summary>
[NotInParallel]
public class DeleteWorktreeTests
{
    [Test]
    public async Task Delete_removes_the_worktree_its_local_branch_and_the_remote_branch()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");          // main tree stays on main
        var worktree = world.AddWorktree(clone, "feature/x");  // linked worktree on feature/x
        world.PushBranch(worktree, "feature/x");               // publish it to origin

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService
        {
            OnChooser = _ => ChooserDialog.DeleteRequested,   // click the delete button in the target chooser
            OnConfirmDelete = _ => WorktreeDeletionChoice.All,                      // confirm the destructive action
        };
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");   // branch-only flow → single worktree → target chooser
            Screenshots.Save(window, "D-delete-worktree");

            // The chooser offered the delete action (only shown for a linked worktree), and it was confirmed.
            await Assert.That(dialogs.ChooserRequests.Count).IsEqualTo(1);
            await Assert.That(dialogs.ChooserRequests[0].Title).Contains("Open from branch folder");
            await Assert.That(dialogs.ChooserRequests[0].DeleteLabel).IsNotNull();
            await Assert.That(dialogs.DeleteConfirmations.Count).IsEqualTo(1);

            // Nothing was launched, and the deletion reported success.
            await Assert.That(rider.LastLaunch).IsNull();
            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.Go);

            // The worktree folder, the local branch, and the origin branch are all gone.
            var git = new GitService();
            await Assert.That(Directory.Exists(worktree)).IsFalse();
            await Assert.That(await git.LocalBranchExistsAsync(clone, "feature/x")).IsFalse();
            await Assert.That(await git.RemoteHasBranchAsync(clone, "feature/x")).IsFalse();
        });
    }

    [Test]
    public async Task Declining_the_confirmation_deletes_nothing_and_launches_nothing()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.PushBranch(worktree, "feature/x");

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService
        {
            OnChooser = _ => ChooserDialog.DeleteRequested,
            OnConfirmDelete = _ => null,   // back out at the confirmation
        };
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");

            await Assert.That(dialogs.DeleteConfirmations.Count).IsEqualTo(1);   // it asked…
            await Assert.That(rider.LastLaunch).IsNull();                        // …but nothing launched…

            // …and nothing was deleted.
            var git = new GitService();
            await Assert.That(Directory.Exists(worktree)).IsTrue();
            await Assert.That(await git.LocalBranchExistsAsync(clone, "feature/x")).IsTrue();
            await Assert.That(await git.RemoteHasBranchAsync(clone, "feature/x")).IsTrue();
        });
    }

    [Test]
    public async Task Deleting_a_branch_never_pushed_removes_it_locally_without_touching_origin()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");   // never pushed → not on origin

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService
        {
            OnChooser = _ => ChooserDialog.DeleteRequested,
            OnConfirmDelete = _ => WorktreeDeletionChoice.All,
        };
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");

            // The confirmation was told the branch isn't on origin, so no remote delete is attempted…
            await Assert.That(dialogs.DeleteConfirmations.Count).IsEqualTo(1);
            await Assert.That(dialogs.DeleteConfirmations[0].RemoteBranchExists).IsFalse();
            await Assert.That(window.Vm().StatusText).DoesNotContain("remote");

            // …and the worktree + local branch are still gone.
            var git = new GitService();
            await Assert.That(Directory.Exists(worktree)).IsFalse();
            await Assert.That(await git.LocalBranchExistsAsync(clone, "feature/x")).IsFalse();
            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.Go);
        });
    }

    [Test]
    public async Task Deleting_a_dirty_worktree_force_removes_it_end_to_end()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.PushBranch(worktree, "feature/x");
        world.MakeDirty(worktree);   // an uncommitted file — a plain remove would refuse

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService
        {
            OnChooser = _ => ChooserDialog.DeleteRequested,
            OnConfirmDelete = _ => WorktreeDeletionChoice.All,
        };
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");

            // The plan flagged the worktree dirty, and the forced removal still cleared it end to end.
            await Assert.That(dialogs.DeleteConfirmations.Count).IsEqualTo(1);
            await Assert.That(dialogs.DeleteConfirmations[0].HasOutstandingChanges).IsTrue();

            var git = new GitService();
            await Assert.That(Directory.Exists(worktree)).IsFalse();
            await Assert.That(await git.LocalBranchExistsAsync(clone, "feature/x")).IsFalse();
            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.Go);
        });
    }

    [Test]
    public async Task Delete_is_offered_even_when_the_worktree_has_no_solution()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");

        // Strip the solution so the "what to open" chooser would otherwise be skipped — the delete action
        // must still be reachable for a linked worktree that has nothing to open.
        TestRepoWorld.Git(worktree, "rm", "-r", "Foo.sln", "src");
        TestRepoWorld.Git(worktree, "commit", "-m", "drop solution");

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService
        {
            OnChooser = _ => ChooserDialog.DeleteRequested,
            OnConfirmDelete = _ => WorktreeDeletionChoice.All,
        };
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");

            await Assert.That(dialogs.ChooserRequests.Count).IsEqualTo(1);
            await Assert.That(dialogs.ChooserRequests[0].DeleteLabel).IsNotNull();   // offered despite no .sln
            await Assert.That(dialogs.DeleteConfirmations.Count).IsEqualTo(1);
            await Assert.That(rider.LastLaunch).IsNull();

            var git = new GitService();
            await Assert.That(Directory.Exists(worktree)).IsFalse();
            await Assert.That(await git.LocalBranchExistsAsync(clone, "feature/x")).IsFalse();
        });
    }

    [Test]
    public async Task No_delete_action_is_offered_for_a_default_branch()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.CreateBranch(clone, "sidework");                        // free 'main' from the main tree
        var mainWorktree = world.AddWorktreeExisting(clone, "main");  // linked worktree on the default branch

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();   // default OnChooser picks the first item (the solution)
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("main");

            // 'main' is a configured default branch — the delete shortcut must not offer to nuke it.
            await Assert.That(dialogs.ChooserRequests.Count).IsEqualTo(1);
            await Assert.That(dialogs.ChooserRequests[0].DeleteLabel).IsNull();
            await Assert.That(dialogs.DeleteConfirmations.Count).IsEqualTo(0);

            // A normal open still happens against the located worktree.
            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.Go);
            await Assert.That(Paths.StartsWith(rider.LastLaunch!.Value.Target, mainWorktree)).IsTrue();
        });
    }

    [Test]
    public async Task A_failed_remote_delete_still_completes_the_local_cleanup_and_reports_it()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.PushBranch(worktree, "feature/x");              // cached tracking ref now says it's on origin
        TestRepoWorld.Git(origin, "branch", "-D", "feature/x");   // but it's since vanished from origin

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService
        {
            OnChooser = _ => ChooserDialog.DeleteRequested,
            OnConfirmDelete = _ => WorktreeDeletionChoice.All,
        };
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");

            // The push --delete fails (the ref is already gone), but the local worktree + branch removal stands.
            var git = new GitService();
            await Assert.That(Directory.Exists(worktree)).IsFalse();
            await Assert.That(await git.LocalBranchExistsAsync(clone, "feature/x")).IsFalse();

            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.NoGo);
            await Assert.That(window.Vm().StatusText).Contains("remote");
        });
    }

    [Test]
    public async Task Deleting_from_a_multi_folder_chooser_removes_the_folder_the_user_picked()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var alpha = world.Clone(origin, root, "Alpha");   // two independent clones of the same origin…
        var beta = world.Clone(origin, root, "Beta");
        var alphaWt = world.AddWorktree(alpha, "feature/x");   // …each with its own worktree on feature/x
        var betaWt = world.AddWorktree(beta, "feature/x");

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService
        {
            // First chooser = which folder (pick Beta's); the branch-folder chooser = delete.
            OnChooser = req => req.Title.Contains("Open from branch folder")
                ? ChooserDialog.DeleteRequested
                : req.PickTitleContaining(betaWt),
            OnConfirmDelete = _ => WorktreeDeletionChoice.All,
        };
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");   // branch-only → two folders → folder chooser, then delete

            await Assert.That(dialogs.ChooserRequests.Count).IsEqualTo(2);   // folder chooser, then target chooser
            await Assert.That(dialogs.DeleteConfirmations.Count).IsEqualTo(1);

            var git = new GitService();
            // The picked (Beta) worktree and its branch are gone…
            await Assert.That(Directory.Exists(betaWt)).IsFalse();
            await Assert.That(await git.LocalBranchExistsAsync(beta, "feature/x")).IsFalse();
            // …while the other (Alpha) clone is untouched.
            await Assert.That(Directory.Exists(alphaWt)).IsTrue();
            await Assert.That(await git.LocalBranchExistsAsync(alpha, "feature/x")).IsTrue();
        });
    }

    [Test]
    public async Task When_git_cant_remove_the_worktree_an_accepted_force_delete_completes_it()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.PushBranch(worktree, "feature/x");

        // Make `git worktree remove` fail as if a path were too long; everything else runs against real git.
        var git = new GitService((dir, args, ct) =>
            HasSubcommand(args, "worktree", "remove")
                ? Task.FromResult(new ProcessResult(128, "", $"error: unable to unlink '{worktree}/a/very/long/path': Filename too long"))
                : ProcessRunner.RunAsync("git", args, dir, ct));

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService
        {
            OnChooser = _ => ChooserDialog.DeleteRequested,
            OnConfirmDelete = _ => WorktreeDeletionChoice.All,
            OnConfirmForceDelete = _ => true,   // accept the disk-level delete
        };
        var services = world.BuildServices([root], rider, dialogs, git: git);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");
            Screenshots.Save(window, "D-force-delete-worktree");

            // The fallback was offered (with the worktree path) and accepted.
            await Assert.That(dialogs.ForceDeleteConfirmations.Count).IsEqualTo(1);
            await Assert.That(dialogs.ForceDeleteConfirmations[0].WorktreePath).IsEqualTo(Path.GetFullPath(worktree));

            // The folder, the local branch, and the origin branch are all gone; the flow reports GO.
            var check = new GitService();
            await Assert.That(Directory.Exists(worktree)).IsFalse();
            await Assert.That(await check.LocalBranchExistsAsync(clone, "feature/x")).IsFalse();
            await Assert.That(await check.RemoteHasBranchAsync(clone, "feature/x")).IsFalse();
            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.Go);
        });
    }

    [Test]
    public async Task When_git_cant_remove_the_worktree_declining_the_force_delete_is_a_no_go()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");

        var git = new GitService((dir, args, ct) =>
            HasSubcommand(args, "worktree", "remove")
                ? Task.FromResult(new ProcessResult(128, "", "error: unable to unlink: Filename too long"))
                : ProcessRunner.RunAsync("git", args, dir, ct));

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService
        {
            OnChooser = _ => ChooserDialog.DeleteRequested,
            OnConfirmDelete = _ => WorktreeDeletionChoice.All,
            OnConfirmForceDelete = _ => false,   // back out of the disk-level delete
        };
        var services = world.BuildServices([root], rider, dialogs, git: git);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");

            await Assert.That(dialogs.ForceDeleteConfirmations.Count).IsEqualTo(1);   // it asked…

            // …and, declined, nothing was deleted — the worktree and its branch remain, status is NO-GO.
            var check = new GitService();
            await Assert.That(Directory.Exists(worktree)).IsTrue();
            await Assert.That(await check.LocalBranchExistsAsync(clone, "feature/x")).IsTrue();
            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.NoGo);
        });
    }

    /// <summary>True when <paramref name="args"/> contains <paramref name="first"/> immediately followed by
    /// <paramref name="second"/> — used to spot the git subcommand under any leading <c>-c key=value</c> flags.</summary>
    private static bool HasSubcommand(IReadOnlyList<string> args, string first, string second)
    {
        for (var i = 0; i + 1 < args.Count; i++)
            if (args[i] == first && args[i + 1] == second) return true;
        return false;
    }

    [Test]
    public async Task No_delete_action_is_offered_when_the_branch_sits_in_the_main_tree()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.CreateBranch(clone, "feature/x");   // switch the MAIN tree onto the branch (no linked worktree)

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();   // default OnChooser picks the first item (the solution)
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");

            // The located folder is the main working tree, which can't be worktree-removed — so no button.
            await Assert.That(dialogs.ChooserRequests.Count).IsEqualTo(1);
            await Assert.That(dialogs.ChooserRequests[0].DeleteLabel).IsNull();
            await Assert.That(dialogs.DeleteConfirmations.Count).IsEqualTo(0);

            // A normal open still happens.
            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.Go);
            await Assert.That(rider.LastLaunch).IsNotNull();
        });
    }
}
