using System.IO;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;
using Fido.ViewModels;
using Fido.Views;

namespace Fido.Tests.E2E;

/// <summary>
/// Picking a command from the Console run menu brings the target up to date first, so a script or
/// <c>aspire start</c> runs against what <c>origin</c> has. It runs at the one point every card kind has
/// converged on a folder, so a worktree Fido creates on the spot is covered by the same code as one that has
/// sat on disk for weeks — and it is advisory throughout: a pull that can't go through still opens the console.
/// </summary>
[NotInParallel]
public class PullBeforeRunTests
{
    private static EditorLaunchOption ConsoleTool(MainWindow window) =>
        window.Vm().GridTools.First(t => t.Name == "Console");

    [Test]
    public async Task Running_a_script_fast_forwards_an_existing_worktree_first()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.CreateBranch(clone, "feature/run");
        world.PushBranch(clone, "feature/run");
        TestRepoWorld.Git(clone, "switch", "main");   // free the branch for a worktree
        var worktree = world.AddWorktreeExisting(clone, "feature/run");
        TestRepoWorld.WriteFidoConfig(worktree, "commands: [aspire start]\n");

        // A teammate pushes while this worktree sits there.
        var landed = world.CommitToOrigin(origin, "feature/run");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/run");
            await window.RunConsoleOptionAsync(ConsoleTool(window).Runs[0]);

            // Pulled, then launched — in that order.
            await Assert.That(File.Exists(Path.Combine(worktree, landed))).IsTrue();
            await Assert.That(window.LogText()).Contains("up to date with origin/feature/run");
            await Assert.That(launcher.Launches.Count).IsEqualTo(1);
            await Assert.That(launcher.LastLaunch!.Value.ConsoleCommand).IsEqualTo("aspire start");
        });
    }

    [Test]
    public async Task A_worktree_Fido_creates_on_the_spot_goes_through_the_same_update()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");

        // The branch exists locally but is behind origin — exactly the case a freshly-created worktree
        // can't be trusted for: `worktree add` checks out the local ref, stale and all.
        world.CreateBranch(clone, "feature/stale");
        world.PushBranch(clone, "feature/stale");
        TestRepoWorld.CommitFidoConfig(clone, "commands: [aspire start]\n");
        world.PushBranch(clone, "feature/stale");
        TestRepoWorld.Git(clone, "switch", "main");
        var landed = world.CommitToOrigin(origin, "feature/stale");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/stale");
            var card = window.Vm().SelectedTarget!;
            await Assert.That(card.Target.Kind).IsEqualTo(TargetKind.NewWorktree);

            await window.RunConsoleOptionAsync(ConsoleTool(window).Runs[0]);

            var created = launcher.LastLaunch!.Value.Target;
            await Assert.That(File.Exists(Path.Combine(created, landed))).IsTrue();
            await Assert.That(window.LogText()).Contains("up to date with origin/feature/stale");
        });
    }

    [Test]
    public async Task A_pull_that_cannot_go_through_still_opens_the_console()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.CreateBranch(clone, "feature/diverged");
        world.PushBranch(clone, "feature/diverged");
        TestRepoWorld.Git(clone, "switch", "main");   // free the branch for a worktree
        var worktree = world.AddWorktreeExisting(clone, "feature/diverged");
        TestRepoWorld.WriteFidoConfig(worktree, "commands: [aspire start]\n");

        world.CommitFile(worktree, "mine.txt");                   // local commit …
        world.CommitToOrigin(origin, "feature/diverged");         // … and a different one on origin

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/diverged");
            await window.RunConsoleOptionAsync(ConsoleTool(window).Runs[0]);

            // The refusal is narrated, the local work is untouched, and the console opens anyway.
            await Assert.That(window.LogText()).Contains("Couldn't fast-forward");
            await Assert.That(File.Exists(Path.Combine(worktree, "mine.txt"))).IsTrue();
            await Assert.That(launcher.Launches.Count).IsEqualTo(1);
            await Assert.That(launcher.LastLaunch!.Value.ConsoleCommand).IsEqualTo("aspire start");
        });
    }

    [Test]
    public async Task Opening_a_folder_without_a_command_never_pulls()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.CreateBranch(clone, "feature/look");
        world.PushBranch(clone, "feature/look");
        TestRepoWorld.Git(clone, "switch", "main");   // free the branch for a worktree
        var worktree = world.AddWorktreeExisting(clone, "feature/look");

        var landed = world.CommitToOrigin(origin, "feature/look");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/look");
            await window.OpenWithAsync(services.ConfigService.Load().Editors.First());

            // Opening a tree to look at it shouldn't move it under the user, or cost them a round trip.
            await Assert.That(File.Exists(Path.Combine(worktree, landed))).IsFalse();
            await Assert.That(window.LogText()).DoesNotContain("up to date with");
            await Assert.That(launcher.Launches.Count).IsEqualTo(1);
        });
    }

    [Test]
    public async Task The_setting_turns_the_update_off()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.CreateBranch(clone, "feature/off");
        world.PushBranch(clone, "feature/off");
        TestRepoWorld.Git(clone, "switch", "main");   // free the branch for a worktree
        var worktree = world.AddWorktreeExisting(clone, "feature/off");
        TestRepoWorld.WriteFidoConfig(worktree, "commands: [aspire start]\n");

        var landed = world.CommitToOrigin(origin, "feature/off");

        var launcher = new FakeEditorLauncher();
        var services = world.BuildServices([root], launcher, new FakeDialogService(), pullBeforeRun: false);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/off");
            await window.RunConsoleOptionAsync(ConsoleTool(window).Runs[0]);

            await Assert.That(File.Exists(Path.Combine(worktree, landed))).IsFalse();
            await Assert.That(window.LogText()).DoesNotContain("up to date with");
            await Assert.That(launcher.Launches.Count).IsEqualTo(1);
        });
    }
}
