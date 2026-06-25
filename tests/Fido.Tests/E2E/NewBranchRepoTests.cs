using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;
using Fido.ViewModels;

namespace Fido.Tests.E2E;

/// <summary>
/// Scenario D: a branch typed with no solution that isn't checked out anywhere. Fido looks across the
/// repos configured for new branches, keeps only those whose refs actually contain the branch (local or
/// origin), and offers to place it there as a worktree or a main-tree checkout. Found in none ⇒ abandon.
/// </summary>
[NotInParallel]
public class NewBranchRepoTests
{
    private static void ConfigureNewBranchRepos(FidoServices services, params string[] repos)
    {
        var cfg = services.ConfigService.Load();
        cfg.NewBranchRepos = repos.ToList();
        services.ConfigService.Save(cfg);
    }

    /// <summary>Creates a local branch ref without checking it out (working tree stays put).</summary>
    private static void AddLocalBranch(string repoPath, string branch) =>
        TestRepoWorld.Git(repoPath, "branch", branch);

    [Test]
    public async Task Unknown_branch_with_no_configured_repos_is_no_go()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var rider = new FakeRiderLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);   // NewBranchRepos left empty

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");   // no solution → branch-only flow

            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.NoGo);
            await Assert.That(dialogs.DecisionRequests.Count).IsEqualTo(0);
            await Assert.That(rider.LastLaunch).IsNull();
        });
    }

    [Test]
    public async Task Branch_present_in_configured_repo_is_offered_as_a_worktree()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        AddLocalBranch(clone, "feature/x");   // exists locally, not checked out

        var rider = new FakeRiderLauncher();
        var dialogs = new FakeDialogService { OnDecision = _ => OpenDecision.Worktree };
        var services = world.BuildServices([root], rider, dialogs);
        ConfigureNewBranchRepos(services, clone);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");
            Screenshots.Save(window, "D-new-branch-worktree");

            await Assert.That(dialogs.DecisionRequests.Count).IsEqualTo(1);
            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.Go);

            var target = rider.LastLaunch!.Value.Target;
            await Assert.That(Paths.Contains(target, "Foo.worktrees")).IsTrue();
            await Assert.That(Paths.Contains(target, "feature-x")).IsTrue();   // branch name sanitized for the path
            await Assert.That(target).EndsWith("Foo.sln");
        });
    }

    [Test]
    public async Task Branch_present_in_configured_repo_can_be_checked_out_in_main()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        AddLocalBranch(clone, "feature/x");

        var rider = new FakeRiderLauncher();
        var dialogs = new FakeDialogService { OnDecision = _ => OpenDecision.Main };
        var services = world.BuildServices([root], rider, dialogs);
        ConfigureNewBranchRepos(services, clone);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");

            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.Go);
            await Assert.That(Paths.StartsWith(rider.LastLaunch!.Value.Target, clone)).IsTrue();

            var current = await new GitService().GetCurrentBranchAsync(clone);
            await Assert.That(current).IsEqualTo("feature/x");   // the main tree really switched
        });
    }

    [Test]
    public async Task Branch_present_in_two_configured_repos_prompts_a_repo_chooser()
    {
        using var world = new TestRepoWorld();
        var originFoo = world.CreateOrigin("Foo", "Foo");
        var originBar = world.CreateOrigin("Bar", "Bar");
        var root = world.SearchRoot("root");
        var cloneFoo = world.Clone(originFoo, root, "Foo");
        var cloneBar = world.Clone(originBar, root, "Bar");
        AddLocalBranch(cloneFoo, "feature/x");
        AddLocalBranch(cloneBar, "feature/x");

        var rider = new FakeRiderLauncher();
        var dialogs = new FakeDialogService
        {
            OnDecision = _ => OpenDecision.Worktree,
            // first chooser = which repo (pick Bar); later = which target inside it (pick the solution)
            OnChooser = req => req.Title == "Select repository" ? req.PickTitleContaining(cloneBar) : 0,
        };
        var services = world.BuildServices([root], rider, dialogs);
        ConfigureNewBranchRepos(services, cloneFoo, cloneBar);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");

            var repoChooser = dialogs.ChooserRequests.FirstOrDefault(r => r.Title == "Select repository");
            await Assert.That(repoChooser).IsNotNull();
            await Assert.That(repoChooser!.Items.Count).IsEqualTo(2);
            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.Go);
            await Assert.That(Paths.Contains(rider.LastLaunch!.Value.Target, "Bar.worktrees")).IsTrue();
        });
    }

    [Test]
    public async Task Only_the_configured_repo_that_has_the_branch_is_offered_without_a_chooser()
    {
        using var world = new TestRepoWorld();
        var originFoo = world.CreateOrigin("Foo", "Foo");
        var originBar = world.CreateOrigin("Bar", "Bar");
        var root = world.SearchRoot("root");
        var cloneFoo = world.Clone(originFoo, root, "Foo");
        var cloneBar = world.Clone(originBar, root, "Bar");
        AddLocalBranch(cloneFoo, "feature/x");   // only Foo has the branch

        var rider = new FakeRiderLauncher();
        var dialogs = new FakeDialogService { OnDecision = _ => OpenDecision.Worktree };
        var services = world.BuildServices([root], rider, dialogs);
        ConfigureNewBranchRepos(services, cloneFoo, cloneBar);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");

            // Bar is filtered out → only one candidate → no "Select repository" chooser.
            await Assert.That(dialogs.ChooserRequests.Any(r => r.Title == "Select repository")).IsFalse();
            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.Go);
            await Assert.That(Paths.Contains(rider.LastLaunch!.Value.Target, "Foo.worktrees")).IsTrue();
        });
    }

    [Test]
    public async Task Branch_absent_from_all_configured_repos_is_abandoned()
    {
        using var world = new TestRepoWorld();
        var originFoo = world.CreateOrigin("Foo", "Foo");
        var originBar = world.CreateOrigin("Bar", "Bar");
        var root = world.SearchRoot("root");
        var cloneFoo = world.Clone(originFoo, root, "Foo");
        var cloneBar = world.Clone(originBar, root, "Bar");
        // neither clone has "feature/zzz"

        var rider = new FakeRiderLauncher();
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], rider, dialogs);
        ConfigureNewBranchRepos(services, cloneFoo, cloneBar);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/zzz");

            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.NoGo);
            await Assert.That(dialogs.DecisionRequests.Count).IsEqualTo(0);   // never reached the decision
            await Assert.That(rider.LastLaunch).IsNull();
        });
    }

    [Test]
    public async Task Decision_cancel_aborts_cleanly()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        AddLocalBranch(clone, "feature/x");

        var rider = new FakeRiderLauncher();
        var dialogs = new FakeDialogService { OnDecision = _ => OpenDecision.Cancel };
        var services = world.BuildServices([root], rider, dialogs);
        ConfigureNewBranchRepos(services, clone);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");

            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.None);
            await Assert.That(rider.LastLaunch).IsNull();
        });
    }

    [Test]
    public async Task Branch_present_only_on_an_unfetched_remote_is_found_and_opened()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");       // only main fetched
        world.PublishBranchToOrigin(origin, "feature/x");   // pushed to origin AFTER the clone → not fetched here

        var rider = new FakeRiderLauncher();
        var dialogs = new FakeDialogService { OnDecision = _ => OpenDecision.Worktree };
        var services = world.BuildServices([root], rider, dialogs);
        ConfigureNewBranchRepos(services, clone);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");
            Screenshots.Save(window, "D-unfetched-remote-branch");

            // Before the fix this was a no-go ("branch not found in any configured repo"); now Fido
            // queries the remote, fetches the branch, and opens it.
            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.Go);
            await Assert.That(Paths.Contains(rider.LastLaunch!.Value.Target, "feature-x")).IsTrue();

            var current = await new GitService().GetCurrentBranchAsync(clone);
            await Assert.That(current).IsEqualTo("main");   // worktree took the branch; main tree untouched
        });
    }

    [Test]
    public async Task Branch_present_only_on_origin_is_checked_out_tracking()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");

        // Publish feature/x to origin from a clone that lives OUTSIDE the search roots.
        var other = world.SearchRoot("other");
        var publisher = world.Clone(origin, other, "Pub");
        world.CreateBranch(publisher, "feature/x");
        TestRepoWorld.Git(publisher, "push", "-u", "origin", "feature/x");

        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");   // fetches origin/feature/x; stays on main, no local ref

        var rider = new FakeRiderLauncher();
        var dialogs = new FakeDialogService { OnDecision = _ => OpenDecision.Worktree };
        var services = world.BuildServices([root], rider, dialogs);
        ConfigureNewBranchRepos(services, clone);

        await Harness.WithWindow(services, async window =>
        {
            await window.Open("feature/x");

            await Assert.That(window.Vm().StatusKind).IsEqualTo(StatusKind.Go);
            await Assert.That(Paths.Contains(rider.LastLaunch!.Value.Target, "feature-x")).IsTrue();
        });
    }
}
