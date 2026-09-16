using System.IO;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.Services;

/// <summary>
/// The one answer to "where does this branch's worktree live" — the rule the opener creates worktrees by
/// and the line under the branch box shows. Pure path arithmetic, so no git and no disk except where the
/// nearest-existing-folder walk needs one.
/// </summary>
public class WorktreePathTests
{
    /// <summary>An absolute path built the platform's own way — <c>C:\src\…</c> on Windows, <c>/src/…</c>
    /// elsewhere — so one set of expectations reads correctly on both CI runners.</summary>
    private static string P(params string[] parts) =>
        Path.Combine([Path.GetPathRoot(Path.GetTempPath())!, .. parts]);

    [Test]
    public async Task A_branch_name_becomes_a_folder_name_by_replacing_its_separators()
    {
        await Assert.That(WorktreePath.FolderName("feature/new-ui")).IsEqualTo("feature-new-ui");
        await Assert.That(WorktreePath.FolderName("release")).IsEqualTo("release");
        await Assert.That(WorktreePath.FolderName("  feature/x  ")).IsEqualTo("feature-x");
        await Assert.That(WorktreePath.FolderName("a/b\\c")).IsEqualTo("a-b-c");
    }

    [Test]
    public async Task A_configured_root_puts_every_repos_worktrees_under_it()
    {
        var config = new AppConfig { WorktreeRoot = P("worktrees") };

        var path = WorktreePath.InRepo(P("src", "platform"), "feature/new-ui", config);

        await Assert.That(path).IsEqualTo(P("worktrees", "feature-new-ui"));
    }

    [Test]
    public async Task Without_a_root_a_worktree_is_a_sibling_of_its_clone()
    {
        var config = new AppConfig();

        var path = WorktreePath.InRepo(P("src", "platform"), "feature/new-ui", config);

        await Assert.That(path).IsEqualTo(P("src", "platform.worktrees", "feature-new-ui"));
    }

    [Test]
    public async Task A_trailing_separator_on_the_clone_path_doesnt_change_the_sibling_folder()
    {
        var config = new AppConfig();

        var path = WorktreePath.InRepo(P("src", "platform") + Path.DirectorySeparatorChar, "release", config);

        await Assert.That(path).IsEqualTo(P("src", "platform.worktrees", "release"));
    }

    [Test]
    public async Task A_clone_path_in_gits_own_posix_style_comes_back_in_this_platforms_separators()
    {
        // git answers `rev-parse --git-common-dir` with forward slashes even on Windows, so the clone
        // path arrives as D:/main/fido. What we show and copy must not come back as the half-and-half
        // D:/main\fido.worktrees\xyz.
        var clone = P("main", "fido").Replace(Path.DirectorySeparatorChar, '/');

        var path = WorktreePath.InRepo(clone, "xyz", new AppConfig());

        await Assert.That(path).IsEqualTo(P("main", "fido.worktrees", "xyz"));
        if (Path.DirectorySeparatorChar != '/')
            await Assert.That(path).DoesNotContain("/");
    }

    // --- What the line under the branch box offers -------------------------------------
    //
    // Candidates are folders that are ON DISK: the line reports what is there, not what could be.
    // These tests therefore build real directories rather than arguing about path strings.

    [Test]
    public async Task A_configured_root_answers_in_one_row_whatever_was_scanned()
    {
        using var world = new TestRepoWorld();
        var worktrees = world.SearchRoot("worktrees");
        Directory.CreateDirectory(Path.Combine(worktrees, "feature-new-ui"));
        var config = new AppConfig { WorktreeRoot = worktrees };

        // Every repo's worktrees land under the one root, so the clones don't come into it and there is
        // no repo to attribute the answer to.
        var candidates = WorktreePath.Candidates("feature/new-ui", [P("src", "platform"), P("src", "tools")], config);

        await Assert.That(candidates.Count).IsEqualTo(1);
        await Assert.That(candidates[0].Path).IsEqualTo(Path.Combine(worktrees, "feature-new-ui"));
        await Assert.That(candidates[0].RepoName).IsEqualTo("");
        await Assert.That(candidates[0].HasRepoName).IsFalse();
    }

    [Test]
    public async Task A_root_with_no_folder_for_the_branch_offers_nothing()
    {
        using var world = new TestRepoWorld();
        var config = new AppConfig { WorktreeRoot = world.SearchRoot("worktrees") };   // root exists, branch folder doesn't

        await Assert.That(WorktreePath.Candidates("feature/new-ui", [], config).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Without_a_root_each_clone_that_has_the_folder_contributes_a_row()
    {
        using var world = new TestRepoWorld();
        var main = world.SearchRoot("main");
        var platform = Path.Combine(main, "platform");
        var tools = Path.Combine(main, "tools");
        // Only platform has a folder for this branch; tools has a worktree container but not this branch.
        Directory.CreateDirectory(Path.Combine(main, "platform.worktrees", "feature-new-ui"));
        Directory.CreateDirectory(Path.Combine(main, "tools.worktrees", "something-else"));

        var candidates = WorktreePath.Candidates("feature/new-ui", [platform, tools], new AppConfig());

        await Assert.That(candidates.Count).IsEqualTo(1);
        await Assert.That(candidates[0].RepoName).IsEqualTo("platform");
        await Assert.That(candidates[0].Path).IsEqualTo(Path.Combine(main, "platform.worktrees", "feature-new-ui"));
    }

    [Test]
    public async Task A_folder_that_exists_but_holds_no_worktree_of_the_branch_still_counts()
    {
        // The whole point of the reported case: D:\main\fido.worktrees\xyz is on disk while no repo has
        // a branch called xyz. A leftover, or a tree since switched away, is exactly what you want
        // pointed out when you type the name again — there is no git here, just a folder.
        using var world = new TestRepoWorld();
        var main = world.SearchRoot("main");
        Directory.CreateDirectory(Path.Combine(main, "fido.worktrees", "xyz"));

        var candidates = WorktreePath.Candidates("xyz", [Path.Combine(main, "fido")], new AppConfig());

        await Assert.That(candidates.Count).IsEqualTo(1);
        await Assert.That(candidates[0].Path).IsEqualTo(Path.Combine(main, "fido.worktrees", "xyz"));
    }

    [Test]
    public async Task Rows_come_back_ordered_by_repo_so_they_dont_shuffle_between_keystrokes()
    {
        using var world = new TestRepoWorld();
        var main = world.SearchRoot("main");
        foreach (var repo in new[] { "zulu", "alpha", "mike" })
            Directory.CreateDirectory(Path.Combine(main, repo + ".worktrees", "release"));

        var clones = new[] { "zulu", "mike", "alpha" }.Select(r => Path.Combine(main, r)).ToList();
        var candidates = WorktreePath.Candidates("release", clones, new AppConfig());

        await Assert.That(string.Join("|", candidates.Select(c => c.RepoName))).IsEqualTo("alpha|mike|zulu");
    }

    [Test]
    public async Task Nothing_on_disk_means_nothing_shown()
    {
        using var world = new TestRepoWorld();
        var main = world.SearchRoot("main");

        var candidates = WorktreePath.Candidates("never-existed", [Path.Combine(main, "platform")], new AppConfig());

        await Assert.That(candidates.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Two_clones_that_would_share_a_folder_are_offered_once()
    {
        using var world = new TestRepoWorld();
        var main = world.SearchRoot("main");
        var clone = Path.Combine(main, "platform");
        Directory.CreateDirectory(Path.Combine(main, "platform.worktrees", "release"));

        var candidates = WorktreePath.Candidates("release", [clone, clone + Path.DirectorySeparatorChar], new AppConfig());

        await Assert.That(candidates.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Blank_clone_entries_are_ignored()
    {
        using var world = new TestRepoWorld();
        var main = world.SearchRoot("main");
        Directory.CreateDirectory(Path.Combine(main, "platform.worktrees", "release"));

        var candidates = WorktreePath.Candidates("release", ["   ", Path.Combine(main, "platform")], new AppConfig());

        await Assert.That(candidates.Count).IsEqualTo(1);
        await Assert.That(candidates[0].RepoName).IsEqualTo("platform");
    }

    [Test]
    public async Task A_blank_branch_never_yields_a_path()
    {
        using var world = new TestRepoWorld();
        var config = new AppConfig { WorktreeRoot = world.SearchRoot("worktrees") };

        await Assert.That(WorktreePath.Candidates("", [], config).Count).IsEqualTo(0);
        await Assert.That(WorktreePath.Candidates("   ", [], config).Count).IsEqualTo(0);
    }

    // --- Opening a folder that may not exist yet --------------------------------------

    [Test]
    public async Task The_nearest_existing_folder_is_the_path_itself_when_it_is_there()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("worktrees");

        await Assert.That(WorktreePath.NearestExistingFolder(root)).IsEqualTo(root);
    }

    [Test]
    public async Task An_uncreated_worktree_falls_back_to_the_deepest_folder_that_exists()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("worktrees");

        var nearest = WorktreePath.NearestExistingFolder(Path.Combine(root, "feature-new-ui"));

        await Assert.That(nearest).IsEqualTo(root);
    }

    [Test]
    public async Task The_walk_stops_at_the_first_folder_that_exists_however_deep_the_missing_part()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("worktrees");

        var nearest = WorktreePath.NearestExistingFolder(P(root, "feature-x", "nested", "deeper"));

        await Assert.That(nearest).IsEqualTo(root);
    }
}
