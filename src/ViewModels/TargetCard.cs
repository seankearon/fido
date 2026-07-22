using System;
using Fido.Models;

namespace Fido.ViewModels;

/// <summary>
/// One selectable discovery-result card on the main screen: a place the scanned branch is checked
/// out, wrapped with the display strings the card template binds to. Selection state lives on the
/// list control, not here — the card itself is immutable.
/// </summary>
public sealed class TargetCard
{
    public TargetCard(DiscoveredTarget target)
    {
        Target = target;
        Meta = BuildMeta(target);
    }

    public DiscoveredTarget Target { get; }

    /// <summary>Absolute working-tree path — the card's headline, ellipsised by the template.</summary>
    public string Path => Target.Path;

    public bool IsWorktree => Target.Kind == TargetKind.Worktree;
    public bool IsMainClone => Target.Kind == TargetKind.MainClone;

    /// <summary>The trailing kind chip's caption.</summary>
    public string KindLabel => IsWorktree ? "worktree" : "main clone";

    /// <summary>The meta line, e.g. <c>platform · 2 solutions · updated 3d ago</c>.</summary>
    public string Meta { get; }

    private static string BuildMeta(DiscoveredTarget t)
    {
        var solutions = t.Solutions.Count == 1 ? "1 solution" : $"{t.Solutions.Count} solutions";
        var tail = t.Kind == TargetKind.MainClone
            ? "currently on this branch"
            : $"updated {RelativeAge(t.UpdatedUtc)}";
        return $"{t.RepoName} · {solutions} · {tail}";
    }

    /// <summary>Compact "3d ago"-style age; "recently" when the timestamp couldn't be read.</summary>
    private static string RelativeAge(DateTime? updatedUtc)
    {
        if (updatedUtc is not { } utc) return "recently";
        var age = DateTime.UtcNow - utc;
        return age switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalHours: < 1 } => $"{(int)age.TotalMinutes}m ago",
            { TotalDays: < 1 } => $"{(int)age.TotalHours}h ago",
            { TotalDays: < 60 } => $"{(int)age.TotalDays}d ago",
            _ => $"{(int)(age.TotalDays / 30)}mo ago",
        };
    }
}
