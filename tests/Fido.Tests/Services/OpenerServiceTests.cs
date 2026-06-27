using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.Services;

/// <summary>OpenerService resolution logic against a real on-disk git world (no UI).</summary>
public class OpenerServiceTests
{
    private static OpenerService NewOpener() =>
        new(new GitService(), new SolutionFinder(), new WorkingTreeFinder());

    [Test]
    public async Task Two_clones_of_one_origin_are_two_repositories()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var rootA = world.SearchRoot("rootA");
        var rootB = world.SearchRoot("rootB");
        world.Clone(origin, rootA, "Foo");
        world.Clone(origin, rootB, "Foo");

        var repos = await NewOpener().FindRepositoriesAsync("Foo", world.Config(rootA, rootB));

        await Assert.That(repos.Count).IsEqualTo(2);
    }

    [Test]
    public async Task FindSolutionsInFolder_offers_sln_slnx_and_slnf_files()
    {
        using var world = new TestRepoWorld();
        var folder = world.SearchRoot("repo");
        TestRepoWorld.WriteSolutionFile(folder, "App.sln");
        TestRepoWorld.WriteSolutionFile(folder, "App.slnx");
        TestRepoWorld.WriteSolutionFile(folder, "App.Backend.slnf");   // a solution filter

        var found = NewOpener().FindSolutionsInFolder(folder, world.Config(folder));

        await Assert.That(found.Any(p => Path.GetFileName(p) == "App.sln")).IsTrue();
        await Assert.That(found.Any(p => Path.GetFileName(p) == "App.slnx")).IsTrue();
        await Assert.That(found.Any(p => Path.GetFileName(p) == "App.Backend.slnf")).IsTrue();
    }

    [Test]
    public async Task A_repo_with_only_a_solution_filter_is_discovered_by_name()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        File.Delete(Path.Combine(clone, "Foo.sln"));             // leave only a filter behind
        TestRepoWorld.WriteSolutionFile(clone, "Foo.slnf");

        var repos = await NewOpener().FindRepositoriesAsync("Foo", world.Config(root));

        await Assert.That(repos.Count).IsEqualTo(1);
        await Assert.That(repos[0].SolutionFileName).IsEqualTo("Foo.slnf");
    }

    [Test]
    public async Task A_full_solution_beats_a_same_named_filter_as_the_repo_target()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        TestRepoWorld.WriteSolutionFile(clone, "Foo.slnf");      // filter sits beside Foo.sln

        var repos = await NewOpener().FindRepositoriesAsync("Foo", world.Config(root));

        await Assert.That(repos.Count).IsEqualTo(1);
        await Assert.That(repos[0].SolutionFileName).IsEqualTo("Foo.sln");   // full solution wins de-dup
    }

    [Test]
    public async Task Worktrees_of_one_clone_collapse_to_a_single_repository()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.AddWorktree(clone, "feature/x");
        world.AddWorktree(clone, "feature/y");

        var repos = await NewOpener().FindRepositoriesAsync("Foo", world.Config(root));

        await Assert.That(repos.Count).IsEqualTo(1);
        await Assert.That(repos[0].MainWorktreePath).IsEqualTo(clone);
    }

    [Test]
    public async Task Asking_for_main_reuses_an_existing_master_checkout()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo", defaultBranch: "master");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");   // checked out on master, has no "main"
        var config = world.Config(root);

        var opener = NewOpener();
        var repos = await opener.FindRepositoriesAsync("Foo", config);
        var existing = await opener.FindExistingCheckoutsAsync(repos, "main", config.MainBranchNames);

        await Assert.That(existing.Count).IsEqualTo(1);
        await Assert.That(existing[0].Worktree.Branch).IsEqualTo("master");
    }

    [Test]
    public async Task Main_context_reports_a_dirty_working_tree()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.MakeDirty(clone);
        var config = world.Config(root);

        var opener = NewOpener();
        var repos = await opener.FindRepositoriesAsync("Foo", config);
        var context = await opener.BuildMainContextAsync(repos[0], "feature/new", config);

        await Assert.That(context.HasOutstandingChanges).IsTrue();
        await Assert.That(context.CurrentBranch).IsEqualTo("main");
    }

    [Test]
    public async Task Create_worktree_makes_a_real_linked_worktree_on_disk()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var config = world.Config(root);

        var opener = NewOpener();
        var repos = await opener.FindRepositoriesAsync("Foo", config);
        var context = await opener.BuildMainContextAsync(repos[0], "feature/new", config);
        var path = await opener.CreateWorktreeAsync(repos[0], "feature/new", context);

        await Assert.That(System.IO.Directory.Exists(path)).IsTrue();
        var worktrees = await new GitService().ListWorktreesAsync(clone);
        await Assert.That(worktrees.Any(w => w.Branch == "feature/new")).IsTrue();
    }

    [Test]
    public async Task FindAllRepositories_lists_distinct_main_clones_and_collapses_worktrees()
    {
        using var world = new TestRepoWorld();
        var originFoo = world.CreateOrigin("Foo", "Foo");
        var originBar = world.CreateOrigin("Bar", "Bar");
        var root = world.SearchRoot("root");
        var cloneFoo = world.Clone(originFoo, root, "Foo");
        var cloneBar = world.Clone(originBar, root, "Bar");
        world.AddWorktree(cloneFoo, "feature/x");   // a linked worktree must fold back into its main clone

        var repos = await NewOpener().FindAllRepositoriesAsync(world.Config(root));

        await Assert.That(repos.Count).IsEqualTo(2);
        await Assert.That(repos.Any(r => r.MainWorktreePath == cloneFoo)).IsTrue();
        await Assert.That(repos.Any(r => r.MainWorktreePath == cloneBar)).IsTrue();
    }

    [Test]
    public async Task FindReposWithBranch_keeps_only_repos_whose_refs_have_the_branch()
    {
        using var world = new TestRepoWorld();
        var originFoo = world.CreateOrigin("Foo", "Foo");
        var originBar = world.CreateOrigin("Bar", "Bar");
        var root = world.SearchRoot("root");
        var cloneFoo = world.Clone(originFoo, root, "Foo");
        var cloneBar = world.Clone(originBar, root, "Bar");

        // Foo has a local "feature/x" ref (not checked out); Bar has no such branch at all.
        TestRepoWorld.Git(cloneFoo, "branch", "feature/x");

        var repos = new[] { new RepositoryInfo(cloneFoo, ""), new RepositoryInfo(cloneBar, "") };
        var withBranch = await NewOpener().FindReposWithBranchAsync(repos, "feature/x");

        await Assert.That(withBranch.Count).IsEqualTo(1);
        await Assert.That(withBranch[0].MainWorktreePath).IsEqualTo(cloneFoo);
    }

    [Test]
    public async Task FindReposWithBranch_finds_a_branch_that_exists_only_on_an_unfetched_remote()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.PublishBranchToOrigin(origin, "feature/x");   // on origin, not fetched into the clone

        var repos = new[] { new RepositoryInfo(clone, "") };
        var withBranch = await NewOpener().FindReposWithBranchAsync(repos, "feature/x");

        await Assert.That(withBranch.Count).IsEqualTo(1);
        await Assert.That(withBranch[0].MainWorktreePath).IsEqualTo(clone);
    }

    [Test]
    public async Task FindReposWithBranch_narrates_local_then_remote_search_when_the_branch_is_remote_only()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.PublishBranchToOrigin(origin, "feature/x");   // remote-only ⇒ local check misses, forcing the remote search

        var live = new List<string>();
        var opener = new OpenerService(new GitService(), new SolutionFinder(), new WorkingTreeFinder(), liveLog: live.Add);
        await opener.FindReposWithBranchAsync([new RepositoryInfo(clone, "")], "feature/x");

        var name = new DirectoryInfo(clone).Name;
        await Assert.That(live.Contains($"Searching for local branch in {name}")).IsTrue();
        await Assert.That(live.Contains($"Searching for remote branch in {name}")).IsTrue();
    }

    [Test]
    public async Task FindReposWithBranch_does_not_reach_the_remote_search_when_the_branch_is_local()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        TestRepoWorld.Git(clone, "branch", "feature/x");   // a local ref ⇒ the local check hits first

        var live = new List<string>();
        var opener = new OpenerService(new GitService(), new SolutionFinder(), new WorkingTreeFinder(), liveLog: live.Add);
        await opener.FindReposWithBranchAsync([new RepositoryInfo(clone, "")], "feature/x");

        var name = new DirectoryInfo(clone).Name;
        await Assert.That(live.Contains($"Searching for local branch in {name}")).IsTrue();
        await Assert.That(live.Contains($"Searching for remote branch in {name}")).IsFalse();   // never queried origin
    }

    [Test]
    public async Task Main_context_for_an_unfetched_remote_branch_plans_to_fetch_and_track()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");
        world.PublishBranchToOrigin(origin, "feature/x");
        var config = world.Config(root);

        var opener = NewOpener();
        var repos = await opener.FindRepositoriesAsync("Foo", config);
        var context = await opener.BuildMainContextAsync(repos[0], "feature/x", config);

        await Assert.That(context.BranchExistsLocally).IsFalse();
        await Assert.That(context.BranchExistsOnRemote).IsTrue();   // discovered live, not from a tracking ref
        await Assert.That(context.RequiresFetch).IsTrue();
    }

    [Test]
    public async Task Create_worktree_fetches_then_tracks_an_unfetched_remote_branch()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.PublishBranchToOrigin(origin, "feature/x");
        var config = world.Config(root);

        var opener = NewOpener();
        var repos = await opener.FindRepositoriesAsync("Foo", config);
        var context = await opener.BuildMainContextAsync(repos[0], "feature/x", config);
        var path = await opener.CreateWorktreeAsync(repos[0], "feature/x", context);

        var git = new GitService();
        await Assert.That(System.IO.Directory.Exists(path)).IsTrue();
        await Assert.That(await git.GetCurrentBranchAsync(path)).IsEqualTo("feature/x");   // really on the branch
        await Assert.That(await git.RemoteBranchExistsAsync(clone, "feature/x")).IsTrue(); // fetched as a side effect
    }
}
