using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.E2E;

/// <summary>
/// Scenario D2: what happens when a delete doesn't go perfectly. The three targets are independent, so a
/// step that fails no longer condemns the report of the ones that worked — and a target that had
/// <em>already</em> gone is reported as done rather than as a failure. Whatever is genuinely still standing
/// is offered back as an inline <b>Retry</b> that re-runs only that step. Driven through the real window.
/// </summary>
[NotInParallel]
public class DeleteRetryTests
{
    [Test]
    public async Task An_origin_branch_that_had_already_gone_reports_success_and_offers_no_retry()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.PushBranch(worktree, "feature/x");

        // Someone already deleted the branch on the server between the scan and the delete — git fails the
        // push, but origin no longer has the branch, which is exactly what was asked for.
        var git = new GitService((dir, args, ct) =>
            HasSubcommand(args, "push", "origin")
                ? Task.FromResult(new ProcessResult(1, "",
                    "error: unable to delete 'feature/x': remote ref does not exist\n"
                    + "error: failed to push some refs to 'https://github.com/acme/app.git'"))
                : ProcessRunner.RunAsync("git", args, dir, ct));

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs, git: git, gitHub: FakeGitHub.None);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            await window.RequestDeleteAsync();
            window.SetChecked("DeleteRemoteCheck", true);
            await window.ConfirmDeleteAsync();
            Screenshots.Save(window, "D-delete-remote-already-gone");
            var vm = window.Vm();

            // The local cleanup really happened, and the log says so with a ✓ — not the ⚠ this used to earn.
            var check = new GitService();
            await Assert.That(Directory.Exists(worktree)).IsFalse();
            await Assert.That(await check.LocalBranchExistsAsync(clone, "feature/x")).IsFalse();
            await Assert.That(window.LogText())
                .Contains("✓ Removed worktree & branch 'feature/x' — origin/feature/x was already gone.");
            await Assert.That(window.LogText()).DoesNotContain("could not be deleted");

            // Nothing is outstanding, so no retry is offered.
            await Assert.That(vm.IsDeleteRetryPending).IsFalse();
        });
    }

    [Test]
    public async Task A_failed_origin_delete_keeps_the_local_result_and_offers_a_retry_that_finishes_it()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.PushBranch(worktree, "feature/x");

        // The first push --delete is refused; every later one runs for real, so the retry can succeed.
        var pushes = 0;
        var git = new GitService((dir, args, ct) =>
        {
            if (!HasSubcommand(args, "push", "origin")) return ProcessRunner.RunAsync("git", args, dir, ct);
            return ++pushes == 1
                ? Task.FromResult(new ProcessResult(1, "", "! [remote rejected] feature/x (pre-receive hook declined)"))
                : ProcessRunner.RunAsync("git", args, dir, ct);
        });

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs, git: git, gitHub: FakeGitHub.None);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            await window.RequestDeleteAsync();
            window.SetChecked("DeleteRemoteCheck", true);
            await window.ConfirmDeleteAsync();
            Screenshots.Save(window, "D-delete-retry-offered");
            var vm = window.Vm();
            var check = new GitService();

            // The worktree and local branch went; only origin is still there — and the report says exactly that.
            await Assert.That(Directory.Exists(worktree)).IsFalse();
            await Assert.That(await check.LocalBranchExistsAsync(clone, "feature/x")).IsFalse();
            await Assert.That(await check.RemoteHasBranchAsync(clone, "feature/x")).IsTrue();
            await Assert.That(window.LogText()).Contains("Removed worktree & branch 'feature/x', but origin/feature/x could not be deleted");

            // The retry strip is armed with what's left — and survives the results emptying, since deleting
            // the only card dropped the delete row with it.
            await Assert.That(vm.IsDeleteRetryPending).IsTrue();
            await Assert.That(vm.ShowDeleteRow).IsFalse();
            await Assert.That(vm.DeleteRetryHeadline).Contains("Couldn't delete origin/feature/x");
            await Assert.That(vm.DeleteRetryHeadline).Contains("that part is done");
            await Assert.That(vm.DeleteRetryDetail).Contains("pre-receive hook declined");

            // Retrying re-runs only the outstanding step…
            await window.RetryDeleteAsync();

            await Assert.That(pushes).IsEqualTo(2);
            await Assert.That(await check.RemoteHasBranchAsync(clone, "feature/x")).IsFalse();
            // …and the report covers the whole attempt, not just this pass.
            await Assert.That(window.LogText()).Contains("✓ Removed worktree & branch 'feature/x' + origin/feature/x.");
            await Assert.That(vm.IsDeleteRetryPending).IsFalse();
        });
    }

    [Test]
    public async Task A_retry_that_fails_again_stays_on_offer()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.PushBranch(worktree, "feature/x");

        var pushes = 0;
        var git = new GitService((dir, args, ct) =>
        {
            if (!HasSubcommand(args, "push", "origin")) return ProcessRunner.RunAsync("git", args, dir, ct);
            pushes++;
            return Task.FromResult(new ProcessResult(1, "", "! [remote rejected] feature/x (pre-receive hook declined)"));
        });

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs, git: git, gitHub: FakeGitHub.None);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            await window.RequestDeleteAsync();
            window.SetChecked("DeleteRemoteCheck", true);
            await window.ConfirmDeleteAsync();
            await window.RetryDeleteAsync();
            var vm = window.Vm();

            await Assert.That(pushes).IsEqualTo(2);
            await Assert.That(vm.IsDeleteRetryPending).IsTrue();   // still there, still offered

            // Dismissing takes the offer away and touches nothing on disk.
            window.DismissDeleteRetry();
            var check = new GitService();
            await Assert.That(vm.IsDeleteRetryPending).IsFalse();
            await Assert.That(vm.DeleteRetryHeadline).IsEqualTo("");
            await Assert.That(await check.RemoteHasBranchAsync(clone, "feature/x")).IsTrue();
            await Assert.That(Directory.Exists(worktree)).IsFalse();
        });
    }

    [Test]
    public async Task A_declined_force_delete_leaves_the_whole_delete_on_offer_for_a_retry()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");

        // git can't remove the folder on the first attempt (a path too long); later attempts run for real.
        var removes = 0;
        var git = new GitService((dir, args, ct) =>
        {
            if (!HasSubcommand(args, "worktree", "remove")) return ProcessRunner.RunAsync("git", args, dir, ct);
            return ++removes == 1
                ? Task.FromResult(new ProcessResult(128, "", "error: unable to unlink: Filename too long"))
                : ProcessRunner.RunAsync("git", args, dir, ct);
        });

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();   // declines the disk-level force delete
        var services = world.BuildServices([root], rider, dialogs, git: git);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            await window.RequestDeleteAsync();
            await window.ConfirmDeleteAsync();
            var vm = window.Vm();
            var check = new GitService();

            // Declining left everything in place — and the delete is offered back rather than lost.
            await Assert.That(dialogs.ForceDeleteConfirmations.Count).IsEqualTo(1);
            await Assert.That(Directory.Exists(worktree)).IsTrue();
            await Assert.That(vm.IsDeleteRetryPending).IsTrue();
            await Assert.That(vm.DeleteRetryHeadline).Contains("Couldn't delete the worktree");

            // The retry runs the whole thing again — worktree and local branch, since neither has gone.
            await window.RetryDeleteAsync();

            await Assert.That(Directory.Exists(worktree)).IsFalse();
            await Assert.That(await check.LocalBranchExistsAsync(clone, "feature/x")).IsFalse();
            await Assert.That(vm.IsDeleteRetryPending).IsFalse();
            await Assert.That(window.LogText()).Contains("✓ Removed worktree & branch 'feature/x'.");
            await Assert.That(vm.Targets.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task A_fresh_scan_clears_a_stale_retry_offer()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.PushBranch(worktree, "feature/x");
        world.AddWorktree(clone, "feature/y");

        var git = new GitService((dir, args, ct) =>
            HasSubcommand(args, "push", "origin")
                ? Task.FromResult(new ProcessResult(1, "", "! [remote rejected] feature/x (pre-receive hook declined)"))
                : ProcessRunner.RunAsync("git", args, dir, ct));

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs, git: git, gitHub: FakeGitHub.None);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            await window.RequestDeleteAsync();
            window.SetChecked("DeleteRemoteCheck", true);
            await window.ConfirmDeleteAsync();
            await Assert.That(window.Vm().IsDeleteRetryPending).IsTrue();

            // A new branch on the screen has nothing to do with the last one's leftovers.
            await window.Discover("feature/y");
            await Assert.That(window.Vm().IsDeleteRetryPending).IsFalse();
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
