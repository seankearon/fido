namespace Fido.Models;

/// <summary>What a pull-request lookup was able to establish about a branch.</summary>
public enum PullRequestLookupStatus
{
    /// <summary>GitHub named an open pull request for the branch.</summary>
    Open,

    /// <summary>GitHub answered, and the branch has no open pull request.</summary>
    None,

    /// <summary>Nobody could say: the GitHub CLI isn't installed or authenticated, the remote isn't GitHub,
    /// or the query failed or timed out. Not the same as "none" — and never reported as one.</summary>
    Unknown,
}

/// <summary>
/// The outcome of asking GitHub whether a branch has an open pull request: what could be established, and
/// the pull request itself when one was. The status is what lets Fido say "no open pull request" only when
/// GitHub actually said so, rather than whenever the answer failed to arrive.
/// </summary>
/// <param name="Status">What the lookup established.</param>
/// <param name="PullRequest">The open pull request; non-null exactly when <paramref name="Status"/> is
/// <see cref="PullRequestLookupStatus.Open"/>.</param>
public sealed record PullRequestLookup(PullRequestLookupStatus Status, PullRequestInfo? PullRequest = null)
{
    /// <summary>GitHub answered: this branch has no open pull request.</summary>
    public static readonly PullRequestLookup None = new(PullRequestLookupStatus.None);

    /// <summary>Nobody could answer — treated as "no PR known", never as "no PR".</summary>
    public static readonly PullRequestLookup Unknown = new(PullRequestLookupStatus.Unknown);

    /// <summary>An open pull request was found.</summary>
    public static PullRequestLookup Found(PullRequestInfo pr) => new(PullRequestLookupStatus.Open, pr);
}
