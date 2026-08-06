using Fido.Models;
using Fido.Services;

namespace Fido.Tests.Services;

/// <summary>
/// The words a finished delete is reported with. The rule under test: a ⚠ is spent only on something the
/// user asked for that is <em>still there</em>. A branch origin had already lost, or a worktree folder that
/// had already gone, is part of a ✓ — the report that used to read as a failure while the local cleanup had
/// in fact succeeded is what this replaces.
/// </summary>
public class DeletionReportTests
{
    private const string Branch = "feature/x";

    private static WorktreeDeletionOutcome Outcome(params WorktreeDeletionStep[] steps) => new(steps);

    private static WorktreeDeletionStep Step(DeletionTarget target, DeletionStepStatus status, string detail = "") =>
        new(target, status, detail);

    [Test]
    public async Task Reports_a_clean_local_delete()
    {
        var outcome = Outcome(
            Step(DeletionTarget.Worktree, DeletionStepStatus.Deleted),
            Step(DeletionTarget.LocalBranch, DeletionStepStatus.Deleted));

        await Assert.That(DeletionReport.Summary(outcome, Branch))
            .IsEqualTo("✓ Removed worktree & branch 'feature/x'.");
    }

    [Test]
    public async Task Reports_the_origin_branch_when_it_went_too()
    {
        var outcome = Outcome(
            Step(DeletionTarget.Worktree, DeletionStepStatus.Deleted),
            Step(DeletionTarget.LocalBranch, DeletionStepStatus.Deleted),
            Step(DeletionTarget.RemoteBranch, DeletionStepStatus.Deleted));

        await Assert.That(DeletionReport.Summary(outcome, Branch))
            .IsEqualTo("✓ Removed worktree & branch 'feature/x' + origin/feature/x.");
    }

    [Test]
    public async Task An_origin_branch_that_was_already_gone_is_a_tick_not_a_warning()
    {
        // The reported case: `git push --delete` failed with "remote ref does not exist" while the worktree
        // and local branch went perfectly. Nothing was left behind, so nothing is flagged.
        var outcome = Outcome(
            Step(DeletionTarget.Worktree, DeletionStepStatus.Deleted),
            Step(DeletionTarget.LocalBranch, DeletionStepStatus.Deleted),
            Step(DeletionTarget.RemoteBranch, DeletionStepStatus.AlreadyGone, "remote ref does not exist"));

        var summary = DeletionReport.Summary(outcome, Branch);

        await Assert.That(summary)
            .IsEqualTo("✓ Removed worktree & branch 'feature/x' — origin/feature/x was already gone.");
        await Assert.That(summary).DoesNotContain("⚠");
    }

    [Test]
    public async Task A_worktree_that_was_already_gone_is_noted_alongside_what_did_go()
    {
        var outcome = Outcome(
            Step(DeletionTarget.Worktree, DeletionStepStatus.AlreadyGone, "is not a working tree"),
            Step(DeletionTarget.LocalBranch, DeletionStepStatus.Deleted));

        await Assert.That(DeletionReport.Summary(outcome, Branch))
            .IsEqualTo("✓ Removed branch 'feature/x' — the worktree folder was already gone.");
    }

    [Test]
    public async Task Nothing_left_to_remove_still_reads_as_success()
    {
        var outcome = Outcome(
            Step(DeletionTarget.Worktree, DeletionStepStatus.AlreadyGone),
            Step(DeletionTarget.LocalBranch, DeletionStepStatus.AlreadyGone));

        await Assert.That(DeletionReport.Summary(outcome, Branch)).StartsWith("✓ Nothing left to remove — ");
    }

    [Test]
    public async Task A_real_failure_names_what_survived_what_went_and_offers_the_retry()
    {
        var outcome = Outcome(
            Step(DeletionTarget.Worktree, DeletionStepStatus.Deleted),
            Step(DeletionTarget.LocalBranch, DeletionStepStatus.Deleted),
            Step(DeletionTarget.RemoteBranch, DeletionStepStatus.Failed, "remote: permission denied"));

        var summary = DeletionReport.Summary(outcome, Branch);

        await Assert.That(summary).StartsWith("⚠ ");
        await Assert.That(summary).Contains("Removed worktree & branch 'feature/x'");
        await Assert.That(summary).Contains("origin/feature/x could not be deleted");
        await Assert.That(summary).Contains("Retry");
    }

    [Test]
    public async Task Two_failures_are_listed_together()
    {
        var outcome = Outcome(
            Step(DeletionTarget.Worktree, DeletionStepStatus.Failed, "still in use"),
            Step(DeletionTarget.LocalBranch, DeletionStepStatus.Failed, "checked out"));

        var summary = DeletionReport.Summary(outcome, Branch);

        await Assert.That(summary).StartsWith("⚠ Couldn't delete the worktree and branch 'feature/x'");
    }

    [Test]
    public async Task The_retry_strip_leads_with_what_is_still_there_and_credits_what_went()
    {
        var outcome = Outcome(
            Step(DeletionTarget.Worktree, DeletionStepStatus.Deleted),
            Step(DeletionTarget.LocalBranch, DeletionStepStatus.Deleted),
            Step(DeletionTarget.RemoteBranch, DeletionStepStatus.Failed, "remote: permission denied"));

        var outstanding = outcome.Outstanding(WorktreeDeletionChoice.All);
        await Assert.That(DeletionReport.RetryHeadline(outcome, outstanding, Branch))
            .IsEqualTo("Couldn't delete origin/feature/x. Removed worktree & branch 'feature/x' — that part is done.");
        await Assert.That(DeletionReport.RetryDetail(outcome)).IsEqualTo("remote: permission denied");
    }

    [Test]
    public async Task A_delete_that_fell_over_before_reporting_anything_still_names_what_is_outstanding()
    {
        // Nothing ran — an exception escaped the first step — so there are no failed steps to name from;
        // the headline has to come from what was asked for and isn't gone.
        var outstanding = WorktreeDeletionOutcome.Nothing.Outstanding(WorktreeDeletionChoice.All);

        await Assert.That(DeletionReport.RetryHeadline(WorktreeDeletionOutcome.Nothing, outstanding, Branch))
            .IsEqualTo("Couldn't delete the worktree, branch 'feature/x' and origin/feature/x.");
    }

    [Test]
    public async Task A_retry_that_finishes_the_job_reports_the_whole_attempt()
    {
        var first = Outcome(
            Step(DeletionTarget.Worktree, DeletionStepStatus.Deleted),
            Step(DeletionTarget.LocalBranch, DeletionStepStatus.Deleted),
            Step(DeletionTarget.RemoteBranch, DeletionStepStatus.Failed, "connection reset"));
        var retry = Outcome(Step(DeletionTarget.RemoteBranch, DeletionStepStatus.Deleted));

        var merged = first.Merge(retry);

        // The retry only touched origin, but the summary still covers what the first pass removed.
        await Assert.That(DeletionReport.Summary(merged, Branch))
            .IsEqualTo("✓ Removed worktree & branch 'feature/x' + origin/feature/x.");
        await Assert.That(merged.AnyFailed).IsFalse();
        await Assert.That(merged.Outstanding(WorktreeDeletionChoice.All).AnySelected).IsFalse();
    }

    [Test]
    public async Task A_skipped_step_in_a_retry_never_forgets_what_the_first_pass_did()
    {
        var first = Outcome(
            Step(DeletionTarget.Worktree, DeletionStepStatus.Deleted),
            Step(DeletionTarget.LocalBranch, DeletionStepStatus.Failed, "checked out elsewhere"));
        var retry = Outcome(
            Step(DeletionTarget.Worktree, DeletionStepStatus.Skipped),
            Step(DeletionTarget.LocalBranch, DeletionStepStatus.Deleted));

        var merged = first.Merge(retry);

        await Assert.That(merged.WorktreeRemoved).IsTrue();
        await Assert.That(merged.StatusOf(DeletionTarget.Worktree)).IsEqualTo(DeletionStepStatus.Deleted);
        await Assert.That(merged.LocalBranchDeleted).IsTrue();
    }
}
