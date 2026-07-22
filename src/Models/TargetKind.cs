namespace Fido.Models;

/// <summary>What kind of checkout a discovered target is — drives its labelling and whether it can be deleted.</summary>
public enum TargetKind
{
    /// <summary>A linked worktree (removable with <c>git worktree remove</c>).</summary>
    Worktree,

    /// <summary>A clone's main working tree — it can be opened but never deleted.</summary>
    MainClone,
}
