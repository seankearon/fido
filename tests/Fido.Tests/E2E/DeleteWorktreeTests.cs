using Avalonia.Input;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.E2E;

/// <summary>
/// Scenario D: the inline delete flow. Discovery renders the branch's locations as target cards; the
/// delete button unlocks only for a linked worktree on an unprotected branch, arms an in-place confirm
/// strip (with dirty/orphaned-work warnings), and a confirmed delete removes the worktree and its
/// <em>local</em> branch — the remote is never touched. Driven end-to-end through the real window.
/// </summary>
[NotInParallel]
public class DeleteWorktreeTests
{
    [Test]
    public async Task Delete_unlocks_for_a_worktree_but_not_for_a_main_clone()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var alpha = world.Clone(origin, root, "Alpha");
        var beta = world.Clone(origin, root, "Beta");
        world.AddWorktree(alpha, "feature/x");     // Alpha holds the branch in a linked worktree…
        world.CreateBranch(beta, "feature/x");     // …Beta holds it in its main working tree.

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            var vm = window.Vm();

            // Both locations were found; the worktree card sorts first and is auto-selected — deletable.
            await Assert.That(vm.Targets.Count).IsEqualTo(2);
            await Assert.That(vm.SelectedTarget!.IsWorktree).IsTrue();
            await Assert.That(vm.CanDelete).IsTrue();
            await Assert.That(vm.ShowDeleteButton).IsTrue();
            await Assert.That(vm.ShowDeleteDisabledNote).IsFalse();

            // Clicking Beta's card (the main clone) locks the delete but explains why; open stays live.
            vm.SelectedTarget = window.CardWithPath("Beta");
            await Assert.That(vm.SelectedTarget!.IsMainClone).IsTrue();
            await Assert.That(vm.CanDelete).IsFalse();
            await Assert.That(vm.CanOpen).IsTrue();
            await Assert.That(vm.ShowDeleteDisabledNote).IsTrue();
            await Assert.That(vm.DeleteDisabledNote).Contains("main clone");

            // The gate holds at the seam too: a delete request for a main clone is a no-op.
            await window.RequestDeleteAsync();
            await Assert.That(vm.IsConfirmingDelete).IsFalse();
            await Assert.That(Directory.Exists(beta)).IsTrue();
        });
    }

    [Test]
    [Arguments("main")]
    [Arguments("master")]
    public async Task A_protected_branch_is_never_deletable_even_from_a_worktree(string branch)
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo", defaultBranch: branch);
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.CreateBranch(clone, "sidework");                          // free the default branch…
        var worktree = world.AddWorktreeExisting(clone, branch);        // …and check it out in a worktree

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover(branch);
            var vm = window.Vm();

            // Found in a worktree — yet the delete stays locked with the protected-branch note.
            await Assert.That(vm.IsFound).IsTrue();
            await Assert.That(vm.SelectedTarget!.IsWorktree).IsTrue();
            await Assert.That(vm.CanDelete).IsFalse();
            await Assert.That(vm.ShowDeleteDisabledNote).IsTrue();
            await Assert.That(vm.DeleteDisabledNote).Contains("default branches");

            await window.RequestDeleteAsync();   // no-op against a protected branch
            await Assert.That(vm.IsConfirmingDelete).IsFalse();
            await Assert.That(Directory.Exists(worktree)).IsTrue();

            // Opening the protected branch is still a normal, unlocked action.
            await window.OpenWithAsync(new Editor { Name = "Rider", Kind = EditorKind.Rider });
            await Assert.That(rider.LastLaunch).IsNotNull();
            await Assert.That(Paths.StartsWith(rider.LastLaunch!.Value.Target, worktree)).IsTrue();
        });
    }

    [Test]
    public async Task Requesting_delete_arms_the_confirm_with_path_branch_and_warnings()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.CommitFile(worktree, "orphan.txt");   // a commit that lives only on this branch…
        world.MakeDirty(worktree);                  // …plus an uncommitted file

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            await window.RequestDeleteAsync();
            Screenshots.Save(window, "D-delete-confirm-armed");
            var vm = window.Vm();

            // The button swapped for the in-place confirm strip, spelling out exactly what goes.
            await Assert.That(vm.IsConfirmingDelete).IsTrue();
            await Assert.That(vm.ShowDeleteButton).IsFalse();
            await Assert.That(vm.DeleteConfirmPath).IsEqualTo(Path.GetFullPath(worktree));
            await Assert.That(vm.DeleteConfirmBranch).IsEqualTo("feature/x");

            // Both safety warnings fold into the strip: dirty files and never-pushed commits.
            await Assert.That(vm.HasDeleteConfirmWarnings).IsTrue();
            await Assert.That(vm.DeleteConfirmWarnings).Contains("uncommitted");
            await Assert.That(vm.DeleteConfirmWarnings).Contains("only on this branch");

            // Arming alone deletes nothing.
            await Assert.That(Directory.Exists(worktree)).IsTrue();
        });
    }

    [Test]
    public async Task Confirming_a_dirty_forked_worktree_still_removes_it_end_to_end()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.CommitFile(worktree, "orphan.txt");   // an unpushed commit (needs `branch -D`)…
        world.MakeDirty(worktree);                  // …and a dirty tree (needs `worktree remove --force`)

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            await window.RequestDeleteAsync();
            await window.ConfirmDeleteAsync();

            // Despite the warnings, a confirmed delete clears both — no modal fallback needed.
            var git = new GitService();
            await Assert.That(Directory.Exists(worktree)).IsFalse();
            await Assert.That(await git.LocalBranchExistsAsync(clone, "feature/x")).IsFalse();
            await Assert.That(window.LogText()).Contains("✓ Removed worktree & branch 'feature/x'.");
            await Assert.That(dialogs.ForceDeleteConfirmations.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Escape_cancels_the_pending_confirm_and_deletes_nothing()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            await window.RequestDeleteAsync();
            var vm = window.Vm();

            // A clean, unforked worktree arms with no warnings at all.
            await Assert.That(vm.IsConfirmingDelete).IsTrue();
            await Assert.That(vm.HasDeleteConfirmWarnings).IsFalse();

            window.PressKey(Key.Escape);   // back out of the confirm

            await Assert.That(vm.IsConfirmingDelete).IsFalse();
            await Assert.That(vm.ShowDeleteButton).IsTrue();

            // Nothing was deleted — the worktree and its branch survive.
            var git = new GitService();
            await Assert.That(Directory.Exists(worktree)).IsTrue();
            await Assert.That(await git.LocalBranchExistsAsync(clone, "feature/x")).IsTrue();
        });
    }

    [Test]
    public async Task Confirming_the_delete_removes_the_worktree_and_local_branch_but_never_the_remote()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.PushBranch(worktree, "feature/x");   // published — and the inline flow must leave origin alone

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            await window.RequestDeleteAsync();
            await window.ConfirmDeleteAsync();
            Screenshots.Save(window, "D-delete-worktree");
            var vm = window.Vm();

            // The worktree folder and the local branch are gone; the branch on origin is untouched.
            var git = new GitService();
            await Assert.That(Directory.Exists(worktree)).IsFalse();
            await Assert.That(await git.LocalBranchExistsAsync(clone, "feature/x")).IsFalse();
            await Assert.That(await git.RemoteHasBranchAsync(clone, "feature/x")).IsTrue();

            // The log reports the removal; nothing was launched, no modal fallback was needed.
            await Assert.That(window.LogText()).Contains("✓ Removed worktree & branch 'feature/x'.");
            await Assert.That(rider.LastLaunch).IsNull();
            await Assert.That(dialogs.ForceDeleteConfirmations.Count).IsEqualTo(0);

            // The only card is gone, so the screen falls back to NotFound and re-locks.
            await Assert.That(vm.Targets.Count).IsEqualTo(0);
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.NotFound);
            await Assert.That(vm.IsLocked).IsTrue();
            await Assert.That(vm.CanOpen).IsFalse();
        });
    }

    [Test]
    public async Task Deleting_one_of_two_locations_keeps_the_other_and_re_selects_it()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var alpha = world.Clone(origin, root, "Alpha");   // two independent clones of the same origin…
        var beta = world.Clone(origin, root, "Beta");
        var alphaWt = world.AddWorktree(alpha, "feature/x");   // …each with its own worktree on feature/x
        var betaWt = world.AddWorktree(beta, "feature/x");

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            var vm = window.Vm();
            await Assert.That(vm.Targets.Count).IsEqualTo(2);

            // Click Beta's card, then delete it.
            vm.SelectedTarget = window.CardWithPath("Beta.worktrees");
            await window.RequestDeleteAsync();
            await window.ConfirmDeleteAsync();

            var git = new GitService();
            // The picked (Beta) worktree and its branch are gone…
            await Assert.That(Directory.Exists(betaWt)).IsFalse();
            await Assert.That(await git.LocalBranchExistsAsync(beta, "feature/x")).IsFalse();
            // …while the other (Alpha) clone is untouched.
            await Assert.That(Directory.Exists(alphaWt)).IsTrue();
            await Assert.That(await git.LocalBranchExistsAsync(alpha, "feature/x")).IsTrue();

            // The survivor takes the selection; the screen stays Found on one location.
            await Assert.That(vm.Targets.Count).IsEqualTo(1);
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Found);
            await Assert.That(Paths.Contains(vm.SelectedTarget!.Path, "Alpha.worktrees")).IsTrue();
            await Assert.That(vm.FoundChipText).IsEqualTo("✓ 1 location");
            await Assert.That(vm.HasMultipleTargets).IsFalse();
        });
    }

    [Test]
    public async Task A_worktree_with_no_solution_still_gets_a_card_and_can_be_deleted()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");

        // Strip the solution — a worktree with nothing to open must still be discoverable and deletable.
        TestRepoWorld.Git(worktree, "rm", "-r", "Foo.sln", "src");
        TestRepoWorld.Git(worktree, "commit", "-m", "drop solution");

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            var vm = window.Vm();

            // Found, no solution chips beyond the Folder fallback, delete unlocked.
            await Assert.That(vm.IsFound).IsTrue();
            await Assert.That(vm.HasSolutionChips).IsFalse();
            await Assert.That(vm.CanDelete).IsTrue();

            await window.RequestDeleteAsync();
            await window.ConfirmDeleteAsync();

            var git = new GitService();
            await Assert.That(Directory.Exists(worktree)).IsFalse();
            await Assert.That(await git.LocalBranchExistsAsync(clone, "feature/x")).IsFalse();
            await Assert.That(rider.LastLaunch).IsNull();
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
        var dialogs = new FakeDialogService { OnConfirmForceDelete = _ => true };   // accept the disk-level delete
        var services = world.BuildServices([root], rider, dialogs, git: git);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            await window.RequestDeleteAsync();
            await window.ConfirmDeleteAsync();
            Screenshots.Save(window, "D-force-delete-worktree");
            var vm = window.Vm();

            // The modal fallback was offered (with the worktree path) and accepted.
            await Assert.That(dialogs.ForceDeleteConfirmations.Count).IsEqualTo(1);
            await Assert.That(dialogs.ForceDeleteConfirmations[0].WorktreePath).IsEqualTo(Path.GetFullPath(worktree));

            // The folder and the local branch are gone; origin still has the branch (never touched).
            var check = new GitService();
            await Assert.That(Directory.Exists(worktree)).IsFalse();
            await Assert.That(await check.LocalBranchExistsAsync(clone, "feature/x")).IsFalse();
            await Assert.That(await check.RemoteHasBranchAsync(clone, "feature/x")).IsTrue();

            // The flow lands exactly like a clean delete: reported, card gone, screen re-locked.
            await Assert.That(window.LogText()).Contains("✓ Removed worktree & branch 'feature/x'.");
            await Assert.That(vm.Targets.Count).IsEqualTo(0);
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.NotFound);
        });
    }

    [Test]
    public async Task When_git_cant_remove_the_worktree_declining_the_force_delete_leaves_it_in_place()
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
        var dialogs = new FakeDialogService();   // default OnConfirmForceDelete declines
        var services = world.BuildServices([root], rider, dialogs, git: git);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            await window.RequestDeleteAsync();
            await window.ConfirmDeleteAsync();
            var vm = window.Vm();

            await Assert.That(dialogs.ForceDeleteConfirmations.Count).IsEqualTo(1);   // it asked…

            // …and, declined, nothing was deleted — worktree and branch remain, the log says so.
            var check = new GitService();
            await Assert.That(Directory.Exists(worktree)).IsTrue();
            await Assert.That(await check.LocalBranchExistsAsync(clone, "feature/x")).IsTrue();
            await Assert.That(window.LogText()).Contains("Couldn't remove the worktree");

            // The card survives (the delete failed), the confirm strip has reset, delete can be retried.
            await Assert.That(vm.Targets.Count).IsEqualTo(1);
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Found);
            await Assert.That(vm.IsConfirmingDelete).IsFalse();
            await Assert.That(vm.CanDelete).IsTrue();
        });
    }

    [Test]
    public async Task Ticking_the_remote_option_deletes_origin_when_no_pull_request_blocks_it()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.PushBranch(worktree, "feature/x");   // published to origin, no PR

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs, gitHub: FakeGitHub.None);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            await window.RequestDeleteAsync();
            var vm = window.Vm();

            // The remote option shows (origin has the branch) and is enabled (no PR blocks it).
            await Assert.That(vm.ShowRemoteBranchOption).IsTrue();
            await Assert.That(vm.CanDeleteRemoteBranch).IsTrue();
            await Assert.That(vm.HasOpenPullRequest).IsFalse();

            window.SetChecked("DeleteRemoteCheck", true);
            await Assert.That(vm.DeleteRemoteBranch).IsTrue();

            await window.ConfirmDeleteAsync();

            var git = new GitService();
            await Assert.That(Directory.Exists(worktree)).IsFalse();
            await Assert.That(await git.LocalBranchExistsAsync(clone, "feature/x")).IsFalse();
            // The opt-in was honoured — the branch on origin is gone too.
            await Assert.That(await git.RemoteHasBranchAsync(clone, "feature/x")).IsFalse();
            await Assert.That(window.LogText()).Contains("origin/feature/x");
        });
    }

    [Test]
    public async Task An_open_pull_request_blocks_the_remote_delete_and_surfaces_a_link()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.PushBranch(worktree, "feature/x");

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var gh = FakeGitHub.WithOpenPr(7, "https://github.com/acme/app/pull/7", "Add feature x");
        var services = world.BuildServices([root], rider, dialogs, gitHub: gh);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            await window.RequestDeleteAsync();
            Screenshots.Save(window, "D-delete-remote-pr-blocked");
            var vm = window.Vm();

            // The remote option shows but is disabled, and the PR is surfaced with its link.
            await Assert.That(vm.ShowRemoteBranchOption).IsTrue();
            await Assert.That(vm.HasOpenPullRequest).IsTrue();
            await Assert.That(vm.CanDeleteRemoteBranch).IsFalse();
            await Assert.That(vm.OpenPullRequestLabel).Contains("#7");
            await Assert.That(vm.OpenPullRequestLabel).Contains("Add feature x");
            await Assert.That(vm.OpenPullRequestUrl).IsEqualTo("https://github.com/acme/app/pull/7");

            // Even if the flag is forced on, the delete must not touch origin while a PR blocks it.
            vm.DeleteRemoteBranch = true;
            await window.ConfirmDeleteAsync();

            var git = new GitService();
            await Assert.That(Directory.Exists(worktree)).IsFalse();
            await Assert.That(await git.LocalBranchExistsAsync(clone, "feature/x")).IsFalse();
            await Assert.That(await git.RemoteHasBranchAsync(clone, "feature/x")).IsTrue();   // origin untouched
        });
    }

    [Test]
    public async Task The_remote_option_is_hidden_when_the_branch_is_not_on_origin()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.AddWorktree(clone, "feature/x");   // never pushed — no remote branch

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            await window.RequestDeleteAsync();
            var vm = window.Vm();
            await Assert.That(vm.ShowRemoteBranchOption).IsFalse();
            await Assert.That(vm.CanDeleteRemoteBranch).IsFalse();
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
}
