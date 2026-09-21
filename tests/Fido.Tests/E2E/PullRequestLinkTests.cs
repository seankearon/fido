using Avalonia.Controls;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.E2E;

/// <summary>
/// The open pull request a branch carries. Every scan that finds the branch asks GitHub afresh; an open
/// PR raises a link row above the target cards, and the same answer — asked again — gates the
/// remote-branch delete. Nothing is remembered between checks: the row says what GitHub said this time.
/// </summary>
[NotInParallel]
public class PullRequestLinkTests
{
    [Test]
    public async Task A_scan_surfaces_the_branch_s_open_pull_request_with_a_link()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.PushBranch(worktree, "feature/x");

        var browser = new FakeBrowser();
        var gh = FakeGitHub.WithOpenPr(7, "https://github.com/acme/app/pull/7", "Add feature x");
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService(),
            gitHub: gh, browser: browser);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            var vm = window.Vm();

            // The branch was found, and GitHub named the PR open on it — link row and all.
            await Assert.That(vm.IsFound).IsTrue();
            await Assert.That(vm.HasOpenPullRequest).IsTrue();
            await Assert.That(vm.ShowPullRequestLink).IsTrue();
            await Assert.That(vm.OpenPullRequestLabel).Contains("#7");
            await Assert.That(vm.OpenPullRequestLabel).Contains("Add feature x");
            await Assert.That(window.FindControl<Button>("BranchPrButton")!.IsEffectivelyVisible).IsTrue();
            Screenshots.Save(window, "pull-request-link-row");

            // The flight log names it too, so the answer survives the row being scrolled past.
            await Assert.That(window.LogText()).Contains("Pull request #7");
            await Assert.That(window.LogText()).Contains("https://github.com/acme/app/pull/7");

            // Clicking the link hands exactly that URL to the browser — and nothing else.
            window.ClickButton("BranchPrButton");
            await Assert.That(browser.Opened.Count).IsEqualTo(1);
            await Assert.That(browser.LastOpened).IsEqualTo("https://github.com/acme/app/pull/7");
        });
    }

    [Test]
    public async Task No_open_pull_request_means_no_row_and_a_log_line_that_says_so()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.PushBranch(worktree, "feature/x");

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService(),
            gitHub: FakeGitHub.None);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");
            var vm = window.Vm();

            await Assert.That(vm.IsFound).IsTrue();
            await Assert.That(vm.HasOpenPullRequest).IsFalse();
            await Assert.That(vm.ShowPullRequestLink).IsFalse();
            await Assert.That(window.FindControl<Button>("BranchPrButton")!.IsEffectivelyVisible).IsFalse();

            // GitHub answered, so Fido says so — "looked, found nothing" is not silence.
            await Assert.That(window.LogText()).Contains("No open pull request for 'feature/x'");
        });
    }

    [Test]
    public async Task A_gh_that_cannot_answer_says_so_rather_than_claiming_there_is_no_pull_request()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.AddWorktree(clone, "feature/x");

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService(),
            gitHub: FakeGitHub.Unavailable);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("feature/x");

            // No row — nothing is known — and the log distinguishes that from a branch without a PR.
            await Assert.That(window.Vm().ShowPullRequestLink).IsFalse();
            await Assert.That(window.LogText()).Contains("Couldn't ask GitHub");
            await Assert.That(window.LogText()).DoesNotContain("No open pull request");
        });
    }

    [Test]
    public async Task Each_scan_asks_again_and_the_row_follows_the_branch()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.AddWorktree(clone, "feature/x");
        world.AddWorktree(clone, "feature/y");

        // gh answers per branch: feature/x has a PR, feature/y doesn't.
        var asked = new List<string>();
        var gh = new GitHubCli((_, args, _) =>
        {
            var branch = args.SkipWhile(a => a != "--head").Skip(1).First();
            asked.Add(branch);
            return Task.FromResult(new ProcessResult(0,
                branch == "feature/x"
                    ? "[{\"number\":7,\"title\":\"Add feature x\",\"url\":\"https://github.com/acme/app/pull/7\"}]"
                    : "[]", ""));
        });

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService(), gitHub: gh);

        await Harness.WithWindow(services, async window =>
        {
            var vm = window.Vm();

            await window.Discover("feature/x");
            await Assert.That(vm.ShowPullRequestLink).IsTrue();

            // A different branch is a different question — asked again, and the row follows the answer.
            await window.Discover("feature/y");
            await Assert.That(vm.ShowPullRequestLink).IsFalse();
            await Assert.That(vm.OpenPullRequestUrl).IsEqualTo("");

            // …and back, which asks a third time rather than reusing the first answer.
            await window.Discover("feature/x");
            await Assert.That(vm.ShowPullRequestLink).IsTrue();
            await Assert.That(vm.OpenPullRequestUrl).IsEqualTo("https://github.com/acme/app/pull/7");

            await Assert.That(asked).IsEquivalentTo(new List<string> { "feature/x", "feature/y", "feature/x" });
        });
    }

    [Test]
    public async Task Arming_the_delete_refreshes_the_link_with_a_pull_request_opened_since_the_scan()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.PushBranch(worktree, "feature/x");

        // The scan finds no PR; by the time the delete is armed, one has been opened.
        var calls = 0;
        var gh = new GitHubCli((_, _, _) => Task.FromResult(new ProcessResult(0,
            calls++ == 0
                ? "[]"
                : "[{\"number\":9,\"title\":\"Ship it\",\"url\":\"https://github.com/acme/app/pull/9\"}]", "")));

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService(), gitHub: gh);

        await Harness.WithWindow(services, async window =>
        {
            var vm = window.Vm();

            await window.Discover("feature/x");
            await Assert.That(vm.ShowPullRequestLink).IsFalse();

            // The delete asks GitHub again, so the confirm strip and the link row both learn about it.
            await window.RequestDeleteAsync();
            await Assert.That(vm.HasOpenPullRequest).IsTrue();
            await Assert.That(vm.ShowPullRequestLink).IsTrue();
            await Assert.That(vm.OpenPullRequestUrl).IsEqualTo("https://github.com/acme/app/pull/9");
            await Assert.That(vm.CanDeleteRemoteBranch).IsFalse();
        });
    }

    [Test]
    public async Task A_pull_request_merged_since_the_scan_stops_blocking_the_remote_delete()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        var worktree = world.AddWorktree(clone, "feature/x");
        world.PushBranch(worktree, "feature/x");

        // The scan finds a PR; by the time the delete is armed it has been merged and closed.
        var calls = 0;
        var gh = new GitHubCli((_, _, _) => Task.FromResult(new ProcessResult(0,
            calls++ == 0
                ? "[{\"number\":9,\"title\":\"Ship it\",\"url\":\"https://github.com/acme/app/pull/9\"}]"
                : "[]", "")));

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService(), gitHub: gh);

        await Harness.WithWindow(services, async window =>
        {
            var vm = window.Vm();

            await window.Discover("feature/x");
            await Assert.That(vm.ShowPullRequestLink).IsTrue();

            // The fresh check clears both the row and the block — no stale PR stands in the way.
            await window.RequestDeleteAsync();
            await Assert.That(vm.HasOpenPullRequest).IsFalse();
            await Assert.That(vm.ShowPullRequestLink).IsFalse();
            await Assert.That(vm.ShowRemoteBranchOption).IsTrue();
            await Assert.That(vm.CanDeleteRemoteBranch).IsTrue();
        });
    }

    [Test]
    public async Task Clearing_the_branch_box_takes_the_row_away()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        var clone = world.Clone(origin, root, "Foo");
        world.AddWorktree(clone, "feature/x");

        var gh = FakeGitHub.WithOpenPr(7, "https://github.com/acme/app/pull/7", "Add feature x");
        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService(), gitHub: gh);

        await Harness.WithWindow(services, async window =>
        {
            var vm = window.Vm();
            await window.Discover("feature/x");
            await Assert.That(vm.ShowPullRequestLink).IsTrue();

            await window.Discover("");
            await Assert.That(vm.Phase).IsEqualTo(DiscoveryPhase.Idle);
            await Assert.That(vm.HasOpenPullRequest).IsFalse();
            await Assert.That(vm.ShowPullRequestLink).IsFalse();
        });
    }
}
