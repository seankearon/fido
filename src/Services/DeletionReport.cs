using System.Collections.Generic;
using System.Linq;
using Fido.Models;

namespace Fido.Services;

/// <summary>
/// Turns a <see cref="WorktreeDeletionOutcome"/> into the words the user reads — the flight log's one-line
/// summary and the retry strip's prompt. It lives in one place so the log line and the strip can never
/// disagree about what actually happened.
/// <para>The rule that matters: the line only carries a ⚠ when something the user asked for is <em>still
/// there</em>. A target that was already gone is reported plainly as part of a ✓ — a failed remote delete
/// against a branch the server had already dropped used to read as a failure while the local cleanup had in
/// fact succeeded, which is precisely the report this replaces.</para>
/// </summary>
public static class DeletionReport
{
    /// <summary>The flight-log line for a finished (or part-finished) delete: ✓ when everything asked for is
    /// gone, ⚠ — with the retry offer — when something survived.</summary>
    public static string Summary(WorktreeDeletionOutcome outcome, string branch)
    {
        var removed = Removed(outcome, branch);
        var notes = AlreadyGoneNotes(outcome, branch);

        if (!outcome.AnyFailed)
        {
            var line = removed.Length > 0 ? removed : "Nothing left to remove";
            if (notes.Count > 0) line += " — " + string.Join("; ", notes);
            return $"✓ {line}.";
        }

        var failed = Names(FailedTargets(outcome), branch);
        var lead = removed.Length > 0
            ? $"{removed}, but {failed} could not be deleted"
            : $"Couldn't delete {failed}";
        if (notes.Count > 0) lead += $" ({string.Join("; ", notes)})";
        return $"⚠ {lead} — use Retry to run just that step again.";
    }

    /// <summary>
    /// The retry strip's headline: what is still standing, and what already went, in one sentence. Named from
    /// <paramref name="outstanding"/> rather than from the failed steps, so a delete that fell over before it
    /// could report anything (git refusing to start, an IO error mid-way) still describes what's left.
    /// </summary>
    public static string RetryHeadline(WorktreeDeletionOutcome outcome, WorktreeDeletionChoice outstanding, string branch)
    {
        var still = $"Couldn't delete {Names(outstanding, branch)}.";
        var removed = Removed(outcome, branch);
        var notes = AlreadyGoneNotes(outcome, branch);
        if (removed.Length > 0) still += $" {removed} — that part is done.";
        else if (notes.Count > 0) still += $" ({string.Join("; ", notes)}.)";
        return still;
    }

    /// <summary>git's own words for the failed steps — the detail line under the retry strip's headline.</summary>
    public static string RetryDetail(WorktreeDeletionOutcome outcome) =>
        string.Join("\n", outcome.Failures.Select(f => f.Detail).Where(d => d.Length > 0));

    /// <summary>"Removed worktree &amp; branch 'x' + origin/x" for whatever this run actually deleted;
    /// empty when it deleted nothing.</summary>
    private static string Removed(WorktreeDeletionOutcome outcome, string branch)
    {
        var local = new List<string>();
        if (outcome.StatusOf(DeletionTarget.Worktree) is DeletionStepStatus.Deleted) local.Add("worktree");
        if (outcome.StatusOf(DeletionTarget.LocalBranch) is DeletionStepStatus.Deleted) local.Add($"branch '{branch}'");

        var text = local.Count > 0 ? "Removed " + string.Join(" & ", local) : "";
        if (outcome.RemoteBranchDeleted)
            text = text.Length > 0 ? $"{text} + origin/{branch}" : $"Removed origin/{branch}";
        return text;
    }

    /// <summary>The "nothing to do here" notes — one per target that had already gone.</summary>
    private static List<string> AlreadyGoneNotes(WorktreeDeletionOutcome outcome, string branch)
    {
        var notes = new List<string>();
        foreach (var step in outcome.Steps)
        {
            if (step.Status is not DeletionStepStatus.AlreadyGone) continue;
            notes.Add(step.Target switch
            {
                DeletionTarget.Worktree => "the worktree folder was already gone",
                DeletionTarget.LocalBranch => $"branch '{branch}' was already gone",
                _ => $"origin/{branch} was already gone",
            });
        }
        return notes;
    }

    /// <summary>Just the targets that failed, as a selection — so failures and outstanding work are named
    /// by the same code.</summary>
    private static WorktreeDeletionChoice FailedTargets(WorktreeDeletionOutcome outcome) => new(
        Worktree: outcome.StatusOf(DeletionTarget.Worktree) is DeletionStepStatus.Failed,
        LocalBranch: outcome.StatusOf(DeletionTarget.LocalBranch) is DeletionStepStatus.Failed,
        RemoteBranch: outcome.StatusOf(DeletionTarget.RemoteBranch) is DeletionStepStatus.Failed);

    /// <summary>A selection named as the user knows it ("the worktree, branch 'x' and origin/x").</summary>
    private static string Names(WorktreeDeletionChoice choice, string branch)
    {
        var names = new List<string>();
        if (choice.Worktree) names.Add("the worktree");
        if (choice.LocalBranch) names.Add($"branch '{branch}'");
        if (choice.RemoteBranch) names.Add($"origin/{branch}");

        return names.Count switch
        {
            0 => "",
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
        };
    }
}
