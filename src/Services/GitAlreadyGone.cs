using System;

namespace Fido.Services;

/// <summary>
/// Tells apart the git deletion failures that mean <em>"there was nothing to delete"</em> from the ones that
/// leave the target standing. A branch someone else already removed on the server, a worktree folder cleared
/// by hand, a branch deleted in another window — git fails these with a non-zero exit, but the end state is
/// exactly the one the user asked for, so reporting them as failures (and colouring the flight log red) is
/// simply wrong. Callers map a match to <see cref="Models.DeletionStepStatus.AlreadyGone"/> and carry on.
/// <para>Matching is on git's own wording, case-insensitively, and deliberately narrow: anything not listed
/// here still counts as a failure, which is the safe way round — a real failure reported as "already gone"
/// would quietly leave a branch behind, while the reverse merely offers a retry that finds nothing to do.</para>
/// </summary>
public static class GitAlreadyGone
{
    /// <summary>
    /// True when <c>git worktree remove</c> failed because the worktree isn't registered any more — usually
    /// because it was already removed (git exits 0 when only the <em>folder</em> is missing, so this is the
    /// "not a working tree" case). Callers should also treat a missing folder as already gone.
    /// </summary>
    public static bool Worktree(ProcessResult result) => MatchesAny(result,
    [
        "is not a working tree",
        "no such file or directory",
    ]);

    /// <summary>True when <c>git branch -D</c> failed because the branch isn't there (<c>error: branch 'x'
    /// not found</c>) — both words are required so an unrelated "not found" can't pass for it.</summary>
    public static bool LocalBranch(ProcessResult result) => MatchesAll(result, ["branch", "not found"]);

    /// <summary>True when <c>git push origin --delete</c> failed because <c>origin</c> no longer has the
    /// branch (<c>error: unable to delete 'x': remote ref does not exist</c>) — the ref is gone either way.</summary>
    public static bool RemoteBranch(ProcessResult result) => MatchesAny(result, ["remote ref does not exist"]);

    /// <summary>True when any one of <paramref name="markers"/> appears in a <em>failed</em> result's output.</summary>
    private static bool MatchesAny(ProcessResult result, string[] markers)
    {
        if (result.Success) return false;
        var text = Text(result);
        foreach (var marker in markers)
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>True when <em>every</em> marker appears in a failed result's output.</summary>
    private static bool MatchesAll(ProcessResult result, string[] markers)
    {
        if (result.Success) return false;
        var text = Text(result);
        foreach (var marker in markers)
            if (!text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private static string Text(ProcessResult result) => result.StdErr + "\n" + result.StdOut;
}
