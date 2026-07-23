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
