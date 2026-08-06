namespace Fido.Models;

/// <summary>The three things a "delete this worktree" action can remove, each reported on separately so a
/// part-way failure can be described — and retried — without redoing the parts that already went.</summary>
public enum DeletionTarget
{
    /// <summary>The linked worktree folder.</summary>
    Worktree,

    /// <summary>The local branch the worktree had checked out.</summary>
    LocalBranch,

    /// <summary>The branch on <c>origin</c>.</summary>
    RemoteBranch,
}

/// <summary>How one deletion step ended.</summary>
public enum DeletionStepStatus
{
    /// <summary>Never attempted — the user didn't tick it, or there was nothing to act on.</summary>
    Skipped,

    /// <summary>git removed it.</summary>
    Deleted,

    /// <summary>There was nothing to remove: it had already gone (a branch deleted on the server, a folder
    /// cleared by hand). The end state the user asked for, so this counts as success — not a failure.</summary>
    AlreadyGone,

    /// <summary>git couldn't remove it and it's still there. The only status worth retrying.</summary>
    Failed,
}

/// <summary>One target's result, carrying git's message when it failed (or when it was already gone).</summary>
/// <param name="Target">Which of the three things this step acted on.</param>
/// <param name="Status">How it ended.</param>
/// <param name="Detail">git's stderr/stdout for a failed or already-gone step; empty otherwise.</param>
public sealed record WorktreeDeletionStep(DeletionTarget Target, DeletionStepStatus Status, string Detail = "")
{
    /// <summary>True when the target is no longer there — whether this step removed it or found it gone.</summary>
    public bool IsGone => Status is DeletionStepStatus.Deleted or DeletionStepStatus.AlreadyGone;

    /// <summary>True when the target is still there and the step is worth retrying.</summary>
    public bool IsFailed => Status is DeletionStepStatus.Failed;
}

/// <summary>
/// What a delete actually removed, step by step, so the caller can report it accurately and offer a retry
/// limited to whatever is still standing. Each of the three targets is independent: a step that fails no
/// longer abandons the ones after it, and a target that was <em>already</em> gone is reported as success
/// rather than as a failure — deleting a branch the server no longer has leaves things exactly as asked.
/// </summary>
/// <param name="Steps">One entry per target that was considered, in the order they ran.</param>
public sealed record WorktreeDeletionOutcome(IReadOnlyList<WorktreeDeletionStep> Steps)
{
    /// <summary>An outcome that did nothing at all — the seed for merging, and the "declined" result.</summary>
    public static WorktreeDeletionOutcome Nothing { get; } = new([]);

    /// <summary>This run's step for <paramref name="target"/>, or null when it wasn't considered.</summary>
    public WorktreeDeletionStep? StepFor(DeletionTarget target) => Steps.FirstOrDefault(s => s.Target == target);

    /// <summary>How <paramref name="target"/> ended, treating "never considered" as <see cref="DeletionStepStatus.Skipped"/>.</summary>
    public DeletionStepStatus StatusOf(DeletionTarget target) => StepFor(target)?.Status ?? DeletionStepStatus.Skipped;

    /// <summary>True when <paramref name="target"/> is no longer there (deleted now, or already gone).</summary>
    public bool IsGone(DeletionTarget target) => StepFor(target)?.IsGone == true;

    /// <summary>True when the worktree folder is gone.</summary>
    public bool WorktreeRemoved => IsGone(DeletionTarget.Worktree);

    /// <summary>True when the local branch is gone.</summary>
    public bool LocalBranchDeleted => IsGone(DeletionTarget.LocalBranch);

    /// <summary>True when <em>this</em> run deleted the branch on <c>origin</c>.</summary>
    public bool RemoteBranchDeleted => StatusOf(DeletionTarget.RemoteBranch) is DeletionStepStatus.Deleted;

    /// <summary>True when <c>origin</c> had already lost the branch — nothing to delete, and no failure.</summary>
    public bool RemoteBranchAlreadyGone => StatusOf(DeletionTarget.RemoteBranch) is DeletionStepStatus.AlreadyGone;

    /// <summary>True when the branch on <c>origin</c> is still there because the delete failed.</summary>
    public bool RemoteDeleteFailed => StatusOf(DeletionTarget.RemoteBranch) is DeletionStepStatus.Failed;

    /// <summary>True when at least one target was actually removed by this run.</summary>
    public bool AnyDeleted => Steps.Any(s => s.Status is DeletionStepStatus.Deleted);

    /// <summary>Everything still standing that the run tried and failed to remove — what a retry would re-run.</summary>
    public IReadOnlyList<WorktreeDeletionStep> Failures => [.. Steps.Where(s => s.IsFailed)];

    /// <summary>True when something the user asked for is still there.</summary>
    public bool AnyFailed => Steps.Any(s => s.IsFailed);

    /// <summary>
    /// What a retry should run: everything <paramref name="asked"/> for that isn't gone yet — the steps that
    /// failed, plus any that never got to run (a worktree removal the user declined to force takes its branch
    /// deletions down with it). Empty when the deletion is complete, which is how the caller knows to drop the
    /// retry offer entirely.
    /// </summary>
    public WorktreeDeletionChoice Outstanding(WorktreeDeletionChoice asked) => new(
        Worktree: asked.Worktree && !IsGone(DeletionTarget.Worktree),
        LocalBranch: asked.LocalBranch && !IsGone(DeletionTarget.LocalBranch),
        RemoteBranch: asked.RemoteBranch && !IsGone(DeletionTarget.RemoteBranch));

    /// <summary>
    /// Folds a later run (a retry) over this one so the report covers the whole attempt: a target the retry
    /// acted on takes the retry's result, everything else keeps what the first run found. Skipped steps in
    /// <paramref name="later"/> never overwrite — a retry that only re-ran the remote delete must not forget
    /// that the worktree and local branch already went.
    /// </summary>
    public WorktreeDeletionOutcome Merge(WorktreeDeletionOutcome later)
    {
        var merged = new List<WorktreeDeletionStep>(Steps);
        foreach (var step in later.Steps)
        {
            if (step.Status is DeletionStepStatus.Skipped) continue;
            var index = merged.FindIndex(s => s.Target == step.Target);
            if (index >= 0) merged[index] = step;
            else merged.Add(step);
        }
        return new WorktreeDeletionOutcome(merged);
    }
}
