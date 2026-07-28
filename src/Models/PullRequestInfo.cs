namespace Fido.Models;

/// <summary>An open pull request found for a branch — enough to name it and link to it.</summary>
/// <param name="Number">The PR number (e.g. 42), shown as <c>#42</c>.</param>
/// <param name="Url">The PR's web URL, opened in the browser from the confirm strip.</param>
/// <param name="Title">The PR title, shown alongside its number.</param>
public sealed record PullRequestInfo(int Number, string Url, string Title);
