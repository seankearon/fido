using System.IO;
using Fido.Models;
using Fido.Tests.Infrastructure;
using Fido.Views;

namespace Fido.Tests.E2E;

/// <summary>
/// Scenario D: a branch that is checked out nowhere. When one of the scanned clones has the branch —
/// a local ref, the cached origin tracking ref, or (via a live <c>ls-remote</c> fallback) a branch
/// pushed to origin that this machine never fetched — discovery offers it as a selectable
/// <see cref="TargetKind.NewWorktree"/> card, and opening creates the worktree first, then launches.
/// Only a branch that exists in no scanned clone at all lands on the locked
/// <see cref="DiscoveryPhase.NotFound"/> state.
/// </summary>
[NotInParallel]
public class NewWorktreeCandidateTests
{
    /// <summary>Creates a local branch ref without checking it out (working tree stays put).</summary>
    private static void AddLocalBranch(string repoPath, string branch) =>
        TestRepoWorld.Git(repoPath, "branch", branch);

    /// <summary>Asserts the full locked contract after a scan that found nothing for <paramref name="branch"/>.</summary>
    private static async Task AssertNotFoundAndLocked(MainWindow window, string branch)
    {
        var vm = window.Vm();
        await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.NotFound);
        await Assert.That(vm.IsNotFound).IsTrue();
        await Assert.That(vm.IsLocked).IsTrue();
        await Assert.That(vm.LockReason).IsEqualTo("🔒 No location found — nothing to open");
        await Assert.That(vm.CanOpen).IsFalse();
        await Assert.That(vm.CanDelete).IsFalse();
        await Assert.That(vm.Targets.Count).IsEqualTo(0);
        await Assert.That(vm.SelectedTarget).IsNull();
        await Assert.That(window.LogText()).Contains($"⚠ No working tree or clone has '{branch}'.");
        await Assert.That(window.LogText().Contains("✓ Found")).IsFalse();
    }

    /// <summary>Asserts a single create-a-worktree offer landed for <paramref name="branch"/>.</summary>
    private static async Task AssertSingleCandidate(MainWindow window, string branch, bool remoteOnly)
    {
        var vm = window.Vm();
        await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Found);
        await Assert.That(vm.CanOpen).IsTrue();
        await Assert.That(vm.Targets.Count).IsEqualTo(1);

        var card = vm.Targets[0];
        await Assert.That(card.IsNewWorktree).IsTrue();
        await Assert.That(card.KindLabel).IsEqualTo("new worktree");
        await Assert.That(card.Target.BranchOnOriginOnly).IsEqualTo(remoteOnly);
        await Assert.That(vm.SelectedTarget).IsEqualTo(card);

        // A candidate is never deletable — there's nothing on disk yet.
        await Assert.That(vm.CanDelete).IsFalse();
        await Assert.That(vm.DeleteDisabledNote).Contains("opening creates this worktree");
        await Assert.That(window.LogText()).Contains($"'{branch}' isn't checked out anywhere");
    }

    [Test]
    public async Task Branch_with_no_refs_anywhere_is_not_found_and_locked()
    {
        using var world = new TestRepoWorld();
        var originFoo = world.CreateOrigin("Foo", "Foo");
        var originBar = world.CreateOrigin("Bar", "Bar");
        var root = world.SearchRoot("root");
        world.Clone(originFoo, root, "Foo");
        world.Clone(originBar, root, "Bar");
        // neither clone (nor either origin) has "feature/zzz"

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/zzz");
            Screenshots.Save(window, "D-not-found-locked");

            await AssertNotFoundAndLocked(window, "feature/zzz");
            await Assert.That(window.Vm().ScannedBranch).IsEqualTo("feature/zzz");
            await Assert.That(rider.Launches.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Local_branch_ref_that_is_checked_out_nowhere_is_offered_as_a_new_worktree()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        AddLocalBranch(clone, "feature/x");   // exists locally, not checked out

        var rider = new FakeEditorLauncher();
        var services = world.BuildServices([root], rider, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            Screenshots.Save(window, "D-new-worktree-offer");

            await AssertSingleCandidate(window, "feature/x", remoteOnly: false);
            await Assert.That(rider.Launches.Count).IsEqualTo(0);   // offering is not opening
        });
    }

    /// <summary>
    /// A branch that lives only on origin is offered whether the clone has already fetched its
    /// remote-tracking ref (pushed before the clone) or has never heard of it (pushed after —
    /// found by the live <c>ls-remote</c> fallback).
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Branch_that_lives_only_on_origin_is_offered_as_a_new_worktree(bool pushedBeforeClone)
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");

        if (pushedBeforeClone)
        {
            world.PublishBranchToOrigin(origin, "feature/x");
            world.Clone(origin, root, "Foo");   // fetches origin/feature/x; stays on main, no local checkout
        }
        else
        {
            world.Clone(origin, root, "Foo");
            world.PublishBranchToOrigin(origin, "feature/x");   // pushed after the clone → never fetched here
        }

        var rider = new FakeEditorLauncher();
        var services = world.BuildServices([root], rider, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");

            await AssertSingleCandidate(window, "feature/x", remoteOnly: true);
            await Assert.That(rider.Launches.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Opening_a_candidate_creates_the_worktree_and_launches_its_solution()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.PublishBranchToOrigin(origin, "feature/x");   // never fetched by the clone

        var rider = new FakeEditorLauncher();
        var services = world.BuildServices([root], rider, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            var vm = window.Vm();
            await window.Discover("feature/x");
            await AssertSingleCandidate(window, "feature/x", remoteOnly: true);

            // The chips preview the clone's solutions before anything exists on disk.
            await Assert.That(vm.HasSolutionChips).IsTrue();
            await Assert.That(vm.SelectedSolutionChip!.Label).IsEqualTo("Foo.sln");

            await window.OpenWithAsync(new Editor { Name = "Rider", Kind = EditorKind.Rider });

            // The worktree now exists, checked out on a tracking branch, and Rider got the
            // same-named solution inside the NEW tree — not the clone's copy.
            var worktree = vm.Targets[0].Path;
            await Assert.That(Directory.Exists(worktree)).IsTrue();
            await Assert.That(await services.Git.GetCurrentBranchAsync(worktree)).IsEqualTo("feature/x");
            await Assert.That(await services.Git.IsLinkedWorktreeAsync(worktree)).IsTrue();

            await Assert.That(rider.Launches.Count).IsEqualTo(1);
            var target = rider.LastLaunch!.Value.Target;
            await Assert.That(Paths.StartsWith(target, worktree)).IsTrue();
            await Assert.That(target).EndsWith("Foo.sln");
            await Assert.That(window.LogText()).Contains("Fido? GO!");

            // A rescan now reports it as a real worktree, deletable like any other.
            await window.RunDiscoveryAsync();
            await Assert.That(vm.Targets.Count).IsEqualTo(1);
            await Assert.That(vm.Targets[0].IsWorktree).IsTrue();
            await Assert.That(vm.CanDelete).IsTrue();
        });
    }

    [Test]
    public async Task Open_and_delete_are_no_ops_while_locked_and_no_dialogs_appear()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/zzz");
            await Assert.That(window.Vm().IsLocked).IsTrue();

            // Both action seams are gated on the phase machine: nothing launches, nothing arms.
            await window.OpenWithAsync(new Editor { Name = "Rider", Kind = EditorKind.Rider });
            await window.RequestDeleteAsync();

            await Assert.That(rider.Launches.Count).IsEqualTo(0);
            await Assert.That(window.Vm().IsConfirmingDelete).IsFalse();
            await Assert.That(window.LogText().Contains("Fido? GO!")).IsFalse();

            // The old chooser/decision/delete dialogs are gone; the surviving modals stay untouched too.
            await Assert.That(dialogs.ForceDeleteConfirmations.Count).IsEqualTo(0);
            await Assert.That(dialogs.SettingsShownCount).IsEqualTo(0);
        });
    }
}
