using System.IO;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.Services;

/// <summary>
/// Reading a repo's own <c>.fido/cfg.yaml</c> against real git repositories: from the working tree when
/// the branch is checked out there, and straight off the branch (<c>git show</c>) when it isn't — plus
/// the <c>*</c> run-file wildcard, which globs the tree on disk or lists the branch's root as needed.
/// <para>
/// The order the three copies are taken in — the local edit, then <c>origin</c>'s when it differs, then
/// this machine's — is pinned below against real clones that are genuinely behind their upstream, because
/// that's the case where reading only what's on disk quietly loses the settings the branch carries.
/// </para>
/// </summary>
[NotInParallel]
public class RepoConfigServiceTests
{
    private static RepoConfigService Reader() => new(new GitService());

    /// <summary>A read of <paramref name="yaml"/> as if it had come from the copy this machine has.</summary>
    private static RepoConfigRead Local(string yaml) =>
        new(RepoConfigService.Parse(yaml), RepoConfigSource.Local);

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

        var read = await Reader().ReadAsync(Checkout(worktree, clone), "feature/cfg");

        await Assert.That(read).IsNotNull();
        await Assert.That(read!.Source).IsEqualTo(RepoConfigSource.LocalEdit);
        await Assert.That(read.Config.PreferMainClone).IsTrue();
        await Assert.That(read.Config.AspireStart).IsTrue();
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

        var read = await Reader().ReadAsync(Placement(clone), "feature/placed");

        await Assert.That(read).IsNotNull();
        await Assert.That(read!.Source).IsEqualTo(RepoConfigSource.Local);
        await Assert.That(read.Config.PreferMainClone).IsTrue();
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

        var read = await Reader().ReadAsync(Placement(clone, originOnly: true), "feature/remote");

        await Assert.That(read).IsNotNull();
        await Assert.That(read!.Source).IsEqualTo(RepoConfigSource.Origin);
        await Assert.That(read.Config.AspireStart).IsTrue();
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
        var read = await Reader().ReadAsync(target, "feature/switch");

        // The card offers to switch the tree onto the branch, so it's the branch's file that counts —
        // the one on disk belongs to whatever the tree happens to be on now.
        await Assert.That(read).IsNotNull();
        await Assert.That(read!.Config.AspireStart).IsTrue();
        await Assert.That(read.Config.PreferMainClone).IsFalse();
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
        var runs = await Reader().ResolveRunFilesAsync(Local("run files: '*'"), target, "feature/scripts");

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
        var read = await Reader().ReadAsync(target, "feature/unplaced");
        var runs = await Reader().ResolveRunFilesAsync(read!, target, "feature/unplaced");

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

        // And it reads back through the ordinary path as nothing at all: a file that asks for nothing is
        // "no in-repo config", so it can't shadow a copy of the real settings further down the order.
        await Assert.That(await Reader().ReadAsync(Checkout(worktree, clone), "feature/init")).IsNull();
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
        var runs = await Reader().ResolveRunFilesAsync(
            Local("run files: [test.ps1, '*', missing.ps1]"), Checkout(worktree, clone), "feature/named");

        // A named file is offered as configured, whether or not it's in the tree today.
        await Assert.That(string.Join('|', runs)).IsEqualTo("test.ps1|build.ps1|missing.ps1");
    }

    // --- Which copy wins: the local edit, then origin's, then this machine's ------------------

    /// <summary>
    /// A worktree on the branch, level with origin when it was made, that has since had the config land on
    /// the branch behind it — and a routine fetch (nothing more) since. This is the case that made the
    /// feature look intermittent: the file exists on the branch, just not in this folder yet.
    /// </summary>
    private static (TestRepoWorld World, string Clone, string Worktree) StaleCheckout(
        string branch, string yaml, params string[] alsoOnOrigin)
    {
        var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, branch);

        world.PushBranch(worktree, branch);                                  // origin has the branch as it stands
        world.CommitFidoConfigToOrigin(origin, branch, yaml, alsoOnOrigin);  // …then it grows the config
        TestRepoWorld.Fetch(clone);                                          // refs catch up; the folder doesn't

        return (world, clone, worktree);
    }

    [Test]
    public async Task A_checkout_too_old_for_the_file_reads_it_off_origin()
    {
        var (world, clone, worktree) = StaleCheckout("feature/stale", "prefer main clone: true\naspire start: true\n");
        using var _ = world;

        // Nothing on disk to read — the folder predates the config — so the branch's own copy answers.
        await Assert.That(File.Exists(RepoConfigService.PathIn(worktree))).IsFalse();

        var read = await Reader().ReadAsync(Checkout(worktree, clone), "feature/stale");

        await Assert.That(read).IsNotNull();
        await Assert.That(read!.Source).IsEqualTo(RepoConfigSource.Origin);
        await Assert.That(read.IsFromOrigin).IsTrue();
        await Assert.That(read.Config.PreferMainClone).IsTrue();
        await Assert.That(read.Config.AspireStart).IsTrue();
    }

    [Test]
    public async Task An_edit_in_flight_beats_the_copy_on_origin()
    {
        var (world, clone, worktree) = StaleCheckout("feature/editing", "aspire start: true\n");
        using var _ = world;

        // The file you're writing right now is the most deliberate statement there is — even against a
        // newer one on the branch, and even though it's untracked here.
        TestRepoWorld.WriteFidoConfig(worktree, "prefer main clone: true\n");

        var read = await Reader().ReadAsync(Checkout(worktree, clone), "feature/editing");

        await Assert.That(read).IsNotNull();
        await Assert.That(read!.Source).IsEqualTo(RepoConfigSource.LocalEdit);
        await Assert.That(read.Config.PreferMainClone).IsTrue();
        await Assert.That(read.Config.AspireStart).IsFalse();
    }

    [Test]
    public async Task A_starter_file_that_asks_for_nothing_cannot_mask_the_branchs_settings()
    {
        var (world, clone, worktree) = StaleCheckout("feature/seeded", "aspire start: true\n");
        using var _ = world;

        // The create/edit button's output: present, uncommitted, and asking for nothing. "No config here"
        // is exactly what that means, so the read carries on to the copy that does ask for something.
        await Reader().CreateAsync(worktree);

        var read = await Reader().ReadAsync(Checkout(worktree, clone), "feature/seeded");

        await Assert.That(read).IsNotNull();
        await Assert.That(read!.Source).IsEqualTo(RepoConfigSource.Origin);
        await Assert.That(read.Config.AspireStart).IsTrue();
    }

    [Test]
    public async Task A_checkout_holding_the_same_file_as_origin_reads_as_its_own()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/level");

        TestRepoWorld.CommitFidoConfig(worktree, "aspire start: true\n");
        world.PushBranch(worktree, "feature/level");

        var read = await Reader().ReadAsync(Checkout(worktree, clone), "feature/level");

        // Both copies say the same thing, so there's nothing to report: the checkout isn't behind, and the
        // log must not tell the user it is.
        await Assert.That(read).IsNotNull();
        await Assert.That(read!.Source).IsEqualTo(RepoConfigSource.Local);
        await Assert.That(read.IsFromOrigin).IsFalse();
        await Assert.That(read.Config.AspireStart).IsTrue();
    }

    [Test]
    public async Task A_placement_takes_origins_copy_over_a_local_branch_that_is_behind()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");

        // A local branch, pushed, then left behind while the config landed on it — and checked out nowhere,
        // so both cards on offer are placements reading refs rather than folders.
        world.CreateBranch(clone, "feature/behind");
        world.PushBranch(clone, "feature/behind");
        TestRepoWorld.Git(clone, "switch", "main");
        world.CommitFidoConfigToOrigin(origin, "feature/behind", "prefer main clone: true\n");
        TestRepoWorld.Fetch(clone);

        var read = await Reader().ReadAsync(Placement(clone), "feature/behind");

        await Assert.That(read).IsNotNull();
        await Assert.That(read!.Source).IsEqualTo(RepoConfigSource.Origin);
        await Assert.That(read.Config.PreferMainClone).IsTrue();
    }

    [Test]
    public async Task The_wildcard_lists_the_tree_the_settings_came_from()
    {
        var (world, clone, worktree) = StaleCheckout("feature/scripts", "run files: ['*']\n", "deploy.ps1");
        using var _ = world;

        File.WriteAllText(Path.Combine(worktree, "stale.ps1"), "");   // only ever existed here

        var target = Checkout(worktree, clone);
        var read = await Reader().ReadAsync(target, "feature/scripts");
        var runs = await Reader().ResolveRunFilesAsync(read!, target, "feature/scripts");

        // Settings off origin are expanded against origin's root: pairing them with this folder's older
        // listing would offer scripts from one commit under settings from another.
        await Assert.That(string.Join('|', runs)).IsEqualTo("deploy.ps1");
    }
}
