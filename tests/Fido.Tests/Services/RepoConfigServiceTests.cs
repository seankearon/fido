using System.IO;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.Services;

/// <summary>
/// Reading a repo's own <c>.fido/cfg.yaml</c> against real git repositories: from the working tree when
/// the branch is checked out there, and straight off the branch (<c>git show</c>) when it isn't — plus
/// the <c>*</c> run-file wildcard, which globs the tree on disk or lists the branch's root as needed.
/// </summary>
[NotInParallel]
public class RepoConfigServiceTests
{
    private static RepoConfigService Reader() => new(new GitService());

    private static DiscoveredTarget Checkout(string path, string mainPath, TargetKind kind = TargetKind.Worktree) =>
        new(path, kind, Path.GetFileName(mainPath), mainPath, [], null);

    private static DiscoveredTarget Placement(string mainPath, bool originOnly = false) =>
        new(Path.Combine(mainPath + ".worktrees", "new"), TargetKind.NewWorktree, Path.GetFileName(mainPath),
            mainPath, [], null, BranchOnOriginOnly: originOnly);

    [Test]
    public async Task A_checked_out_branch_is_read_from_its_working_tree()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/cfg");

        // Not committed: the tree on the branch is the live truth, so an edit in flight counts.
        TestRepoWorld.WriteFidoConfig(worktree, "prefer main clone: true\naspire start: true\n");

        var config = await Reader().ReadAsync(Checkout(worktree, clone), "feature/cfg");

        await Assert.That(config).IsNotNull();
        await Assert.That(config!.PreferMainClone).IsTrue();
        await Assert.That(config.AspireStart).IsTrue();
    }

    [Test]
    public async Task A_branch_with_no_config_reads_as_nothing()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/plain");

        await Assert.That(await Reader().ReadAsync(Checkout(worktree, clone), "feature/plain")).IsNull();
        await Assert.That(await Reader().ReadAsync(Placement(clone), "feature/plain")).IsNull();
    }

    [Test]
    public async Task A_placement_offer_is_read_off_the_branch_itself()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");

        // The branch exists but is checked out nowhere: commit the config on it, then step off it.
        world.CreateBranch(clone, "feature/placed");
        TestRepoWorld.CommitFidoConfig(clone, "prefer main clone: true\n");
        TestRepoWorld.Git(clone, "switch", "main");

        var config = await Reader().ReadAsync(Placement(clone), "feature/placed");

        await Assert.That(config).IsNotNull();
        await Assert.That(config!.PreferMainClone).IsTrue();
    }

    [Test]
    public async Task A_branch_only_on_origin_is_read_through_its_tracking_ref()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");

        world.CreateBranch(clone, "feature/remote");
        TestRepoWorld.CommitFidoConfig(clone, "aspire start: true\n");
        world.PushBranch(clone, "feature/remote");
        TestRepoWorld.Git(clone, "switch", "main");
        TestRepoWorld.Git(clone, "branch", "-D", "feature/remote");   // only origin/feature/remote is left

        var config = await Reader().ReadAsync(Placement(clone, originOnly: true), "feature/remote");

        await Assert.That(config).IsNotNull();
        await Assert.That(config!.AspireStart).IsTrue();
    }

    [Test]
    public async Task A_switch_offer_reads_the_branch_not_the_tree_it_would_move()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");

        // Two different configs: one committed on the branch, one sitting in the main tree on `main`.
        world.CreateBranch(clone, "feature/switch");
        TestRepoWorld.CommitFidoConfig(clone, "aspire start: true\n");
        TestRepoWorld.Git(clone, "switch", "main");
        TestRepoWorld.WriteFidoConfig(clone, "prefer main clone: true\n");

        var target = new DiscoveredTarget(clone, TargetKind.SwitchMainClone, "Foo", clone, [], null,
            CurrentBranch: "main");
        var config = await Reader().ReadAsync(target, "feature/switch");

        // The card offers to switch the tree onto the branch, so it's the branch's file that counts —
        // the one on disk belongs to whatever the tree happens to be on now.
        await Assert.That(config).IsNotNull();
        await Assert.That(config!.AspireStart).IsTrue();
        await Assert.That(config.PreferMainClone).IsFalse();
    }

    [Test]
    public async Task The_wildcard_offers_every_script_in_a_checked_out_root()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/scripts");

        File.WriteAllText(Path.Combine(worktree, "test.sh"), "#!/bin/sh\n");
        File.WriteAllText(Path.Combine(worktree, "build.ps1"), "");
        File.WriteAllText(Path.Combine(worktree, "notes.md"), "");              // not a script
        Directory.CreateDirectory(Path.Combine(worktree, "scripts"));
        File.WriteAllText(Path.Combine(worktree, "scripts", "deep.ps1"), "");   // not in the root

        var target = Checkout(worktree, clone);
        var config = RepoConfigService.Parse("run files: '*'");
        var runs = await Reader().ResolveRunFilesAsync(config, target, "feature/scripts");

        await Assert.That(string.Join('|', runs)).IsEqualTo("build.ps1|test.sh");
    }

    [Test]
    public async Task The_wildcard_offers_the_branch_root_when_nothing_is_on_disk_yet()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");

        world.CreateBranch(clone, "feature/unplaced");
        File.WriteAllText(Path.Combine(clone, "build.cmd"), "");
        File.WriteAllText(Path.Combine(clone, "readme.txt"), "");
        TestRepoWorld.CommitFidoConfig(clone, "run files: '*'\n");   // commits the scripts alongside
        TestRepoWorld.Git(clone, "switch", "main");

        var target = Placement(clone);
        var config = await Reader().ReadAsync(target, "feature/unplaced");
        var runs = await Reader().ResolveRunFilesAsync(config!, target, "feature/unplaced");

        // Read out of the branch with ls-tree: the scripts it carries, and only the scripts.
        await Assert.That(string.Join('|', runs)).IsEqualTo("build.cmd");
    }

    [Test]
    public async Task A_created_file_asks_for_nothing_until_it_is_edited()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/init");
        File.WriteAllText(Path.Combine(worktree, "build.ps1"), "");
        File.WriteAllText(Path.Combine(worktree, "notes.md"), "");

        var file = await Reader().CreateAsync(worktree);

        await Assert.That(file.Created).IsTrue();
        await Assert.That(file.Path).IsEqualTo(RepoConfigService.PathIn(worktree));
        await Assert.That(File.Exists(file.Path)).IsTrue();

        // Every setting is present at its default, so the file on its own changes nothing — the scan that
        // follows reads it as "no in-repo config" until someone edits it.
        var text = await File.ReadAllTextAsync(file.Path);
        await Assert.That(RepoConfigService.Parse(text).IsEmpty).IsTrue();

        // The tree's own scripts are named, so the run-file list can be filled in without going looking.
        await Assert.That(text).Contains("build.ps1");
        await Assert.That(text).DoesNotContain("notes.md");

        // And it reads back through the ordinary path, from the working tree it was written into —
        // asking for nothing, which is what the scan treats as "no in-repo config".
        var read = await Reader().ReadAsync(Checkout(worktree, clone), "feature/init");
        await Assert.That(read).IsNotNull();
        await Assert.That(read!.IsEmpty).IsTrue();
    }

    [Test]
    public async Task An_existing_file_is_opened_never_overwritten()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/keep");
        var existing = TestRepoWorld.WriteFidoConfig(worktree, "aspire start: true\n");

        var file = await Reader().CreateAsync(worktree);

        await Assert.That(file.Created).IsFalse();
        await Assert.That(file.Path).IsEqualTo(existing);
        await Assert.That(await File.ReadAllTextAsync(existing)).IsEqualTo("aspire start: true\n");
    }

    [Test]
    public async Task Named_run_files_keep_their_order_and_are_not_offered_twice()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/named");

        File.WriteAllText(Path.Combine(worktree, "build.ps1"), "");
        File.WriteAllText(Path.Combine(worktree, "test.ps1"), "");

        // test.ps1 is named first, so it leads — and the wildcard doesn't offer it again.
        var config = RepoConfigService.Parse("run files: [test.ps1, '*', missing.ps1]");
        var runs = await Reader().ResolveRunFilesAsync(config, Checkout(worktree, clone), "feature/named");

        // A named file is offered as configured, whether or not it's in the tree today.
        await Assert.That(string.Join('|', runs)).IsEqualTo("test.ps1|build.ps1|missing.ps1");
    }
}
