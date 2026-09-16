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

    [Test]
    public async Task A_configured_root_answers_in_one_row_whatever_was_scanned()
    {
        var config = new AppConfig { WorktreeRoot = P("worktrees") };

        // Every repo's worktrees land under the one root, so the clones don't come into it and there is
        // no repo to attribute the answer to.
        var candidates = WorktreePath.Candidates("feature/new-ui", [P("src", "platform"), P("src", "tools")], config);

        await Assert.That(candidates.Count).IsEqualTo(1);
        await Assert.That(candidates[0].Path).IsEqualTo(P("worktrees", "feature-new-ui"));
        await Assert.That(candidates[0].RepoName).IsEqualTo("");
        await Assert.That(candidates[0].HasRepoName).IsFalse();
    }

    [Test]
    public async Task A_root_answers_even_before_anything_has_been_scanned()
    {
        var config = new AppConfig { WorktreeRoot = P("worktrees") };

        var candidates = WorktreePath.Candidates("feature/new-ui", [], config);

        await Assert.That(candidates.Count).IsEqualTo(1);
        await Assert.That(candidates[0].Path).IsEqualTo(P("worktrees", "feature-new-ui"));
    }

    [Test]
    public async Task Without_a_root_every_scanned_clone_contributes_its_own_sibling_folder()
    {
        var candidates = WorktreePath.Candidates(
            "feature/new-ui", [P("src", "platform"), P("other", "tools")], new AppConfig());

        await Assert.That(candidates.Count).IsEqualTo(2);
        await Assert.That(candidates[0].Path).IsEqualTo(P("src", "platform.worktrees", "feature-new-ui"));
        await Assert.That(candidates[0].RepoName).IsEqualTo("platform");
        await Assert.That(candidates[1].Path).IsEqualTo(P("other", "tools.worktrees", "feature-new-ui"));
        await Assert.That(candidates[1].RepoName).IsEqualTo("tools");
    }

    [Test]
    public async Task Without_a_root_and_with_nothing_scanned_there_is_nothing_to_offer()
    {
        // The sibling convention needs a clone to sit beside, and a branch name doesn't name one.
        await Assert.That(WorktreePath.Candidates("feature/new-ui", [], new AppConfig()).Count).IsEqualTo(0);

        // Not even a blank root counts as one.
        await Assert.That(WorktreePath.Candidates("feature/new-ui", [], new AppConfig { WorktreeRoot = "   " }).Count)
            .IsEqualTo(0);
    }

    [Test]
    public async Task Two_clones_that_would_share_a_folder_are_offered_once()
    {
        var clone = P("src", "platform");

        var candidates = WorktreePath.Candidates("release", [clone, clone + Path.DirectorySeparatorChar], new AppConfig());

        await Assert.That(candidates.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Blank_clone_entries_are_ignored()
    {
        var candidates = WorktreePath.Candidates("release", ["   ", P("src", "platform")], new AppConfig());

        await Assert.That(candidates.Count).IsEqualTo(1);
        await Assert.That(candidates[0].RepoName).IsEqualTo("platform");
    }

    [Test]
    public async Task A_blank_branch_never_yields_a_path()
    {
        var config = new AppConfig { WorktreeRoot = P("worktrees") };

        await Assert.That(WorktreePath.Candidates("", [], config).Count).IsEqualTo(0);
        await Assert.That(WorktreePath.Candidates("   ", [], config).Count).IsEqualTo(0);
    }

    // --- Ranking the clones ------------------------------------------------------------

    [Test]
    public async Task Clones_that_already_have_a_worktrees_folder_lead_and_the_rest_follow_by_name()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var alpha = Path.Combine(root, "alpha");
        var zulu = Path.Combine(root, "zulu");
        var mike = Path.Combine(root, "mike");
        foreach (var clone in new[] { alpha, zulu, mike }) Directory.CreateDirectory(clone);
        // zulu is the only one that demonstrably works in worktrees, so it leads despite its name.
        Directory.CreateDirectory(zulu + ".worktrees");

        var ranked = WorktreePath.RankClones([mike, alpha, zulu]);

        await Assert.That(ranked[0]).IsEqualTo(zulu);
        await Assert.That(ranked[1]).IsEqualTo(alpha);
        await Assert.That(ranked[2]).IsEqualTo(mike);
    }

    [Test]
    public async Task Ranking_drops_blank_entries()
    {
        var ranked = WorktreePath.RankClones([P("src", "platform"), "", "  "]);

        await Assert.That(ranked.Count).IsEqualTo(1);
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
