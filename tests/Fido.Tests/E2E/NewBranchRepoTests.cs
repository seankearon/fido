using Fido.Models;
using Fido.Tests.Infrastructure;
using Fido.Views;

namespace Fido.Tests.E2E;

/// <summary>
/// Scenario D: the not-found contract. A branch that exists only as a ref (a local branch that isn't
/// checked out, or a branch on origin) — or doesn't exist at all — is checked out in no working tree,
/// so discovery lands in <see cref="DiscoveryPhase.NotFound"/> with everything locked: no open, no
/// delete, no launch, no dialogs. The pre-redesign flow this file used to cover — offering the
/// configured "new branch" repos via a decision dialog (check out in main, or create a worktree) —
/// was removed by design: Fido now only opens locations that already exist on disk.
/// </summary>
[NotInParallel]
public class BranchNotFoundTests
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
    public async Task Local_branch_ref_that_is_checked_out_nowhere_is_not_found()
    {
        // Pre-redesign this branch triggered the decision dialog (checkout in main / create worktree).
        // That placement flow is gone: a ref without a working tree is simply not a place to open.
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        AddLocalBranch(clone, "feature/x");   // exists locally, not checked out

        var rider = new FakeEditorLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");

            await AssertNotFoundAndLocked(window, "feature/x");
            await Assert.That(rider.Launches.Count).IsEqualTo(0);
        });
    }

    /// <summary>
    /// A branch that lives only on origin is not found, whether the clone has already fetched its
    /// remote-tracking ref (pushed before the clone) or has never heard of it (pushed after).
    /// Pre-redesign Fido fetched and placed it; now nothing on disk means nothing to open.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Branch_that_lives_only_on_origin_is_not_found(bool pushedBeforeClone)
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
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");

            await AssertNotFoundAndLocked(window, "feature/x");
            await Assert.That(rider.Launches.Count).IsEqualTo(0);
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
