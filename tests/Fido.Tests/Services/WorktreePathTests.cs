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

    // --- What a branch name alone settles ---------------------------------------------

    [Test]
    public async Task A_configured_root_makes_the_branch_name_enough()
    {
        var config = new AppConfig { WorktreeRoot = P("worktrees") };

        await Assert.That(WorktreePath.ForBranch("feature/new-ui", config))
            .IsEqualTo(P("worktrees", "feature-new-ui"));
    }

    [Test]
    public async Task Without_a_root_a_branch_name_names_as_many_folders_as_there_are_repos_so_none_is_offered()
    {
        // The sibling convention needs a clone to sit beside, and a branch name doesn't name one.
        await Assert.That(WorktreePath.ForBranch("feature/new-ui", new AppConfig())).IsEqualTo("");

        // Not even a blank root counts as one.
        await Assert.That(WorktreePath.ForBranch("feature/new-ui", new AppConfig { WorktreeRoot = "   " }))
            .IsEqualTo("");
    }

    [Test]
    public async Task A_blank_branch_never_yields_a_path()
    {
        var config = new AppConfig { WorktreeRoot = P("worktrees") };

        await Assert.That(WorktreePath.ForBranch("", config)).IsEqualTo("");
        await Assert.That(WorktreePath.ForBranch("   ", config)).IsEqualTo("");
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
