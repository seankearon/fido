using Fido.Services;

namespace Fido.Tests.Services;

/// <summary>Parsing and failure-degradation of the gh-CLI wrapper, driven through its injectable runner.</summary>
public class GitHubCliTests
{
    [Test]
    public async Task Parses_the_first_open_pull_request()
    {
        var gh = new GitHubCli((_, _, _) => Task.FromResult(new ProcessResult(0,
            "[{\"number\":42,\"title\":\"Add the widget\",\"url\":\"https://github.com/acme/app/pull/42\"}]", "")));

        var pr = await gh.FindOpenPullRequestAsync("/repo", "feature/x");

        await Assert.That(pr).IsNotNull();
        await Assert.That(pr!.Number).IsEqualTo(42);
        await Assert.That(pr.Title).IsEqualTo("Add the widget");
        await Assert.That(pr.Url).IsEqualTo("https://github.com/acme/app/pull/42");
    }

    [Test]
    public async Task Returns_null_when_there_are_no_open_pull_requests()
    {
        var gh = new GitHubCli((_, _, _) => Task.FromResult(new ProcessResult(0, "[]", "")));
        await Assert.That(await gh.FindOpenPullRequestAsync("/repo", "feature/x")).IsNull();
    }

    [Test]
    public async Task Returns_null_when_gh_fails_or_is_unavailable()
    {
        var gh = new GitHubCli((_, _, _) => Task.FromResult(new ProcessResult(127, "", "gh: command not found")));
        await Assert.That(await gh.FindOpenPullRequestAsync("/repo", "feature/x")).IsNull();
    }

    [Test]
    public async Task Returns_null_on_malformed_output()
    {
        var gh = new GitHubCli((_, _, _) => Task.FromResult(new ProcessResult(0, "not json at all", "")));
        await Assert.That(await gh.FindOpenPullRequestAsync("/repo", "feature/x")).IsNull();
    }

    [Test]
    public async Task Returns_the_pr_even_when_optional_fields_have_unexpected_types()
    {
        // A valid integer number means a PR exists (and must block the remote delete); a non-string
        // url/title must degrade to empty rather than throw.
        var gh = new GitHubCli((_, _, _) => Task.FromResult(new ProcessResult(0,
            "[{\"number\":42,\"url\":123,\"title\":\"x\"}]", "")));

        var pr = await gh.FindOpenPullRequestAsync("/repo", "feature/x");

        await Assert.That(pr).IsNotNull();
        await Assert.That(pr!.Number).IsEqualTo(42);
        await Assert.That(pr.Title).IsEqualTo("x");
        await Assert.That(pr.Url).IsEqualTo("");
    }

    [Test]
    public async Task Returns_null_when_the_number_is_not_an_integer_instead_of_throwing()
    {
        var gh = new GitHubCli((_, _, _) => Task.FromResult(new ProcessResult(0,
            "[{\"number\":42.5,\"url\":\"u\",\"title\":\"t\"}]", "")));

        await Assert.That(await gh.FindOpenPullRequestAsync("/repo", "feature/x")).IsNull();
    }

    [Test]
    public async Task Queries_gh_for_the_branch_head_in_the_open_state()
    {
        IReadOnlyList<string>? seen = null;
        var gh = new GitHubCli((_, args, _) => { seen = args; return Task.FromResult(new ProcessResult(0, "[]", "")); });

        await gh.FindOpenPullRequestAsync("/repo", "feature/x");

        await Assert.That(seen).IsNotNull();
        await Assert.That(seen!.Contains("pr")).IsTrue();
        await Assert.That(seen!.Contains("list")).IsTrue();
        await Assert.That(seen!.Contains("--head")).IsTrue();
        await Assert.That(seen!.Contains("feature/x")).IsTrue();
        await Assert.That(seen!.Contains("--state")).IsTrue();
        await Assert.That(seen!.Contains("open")).IsTrue();
    }
}
