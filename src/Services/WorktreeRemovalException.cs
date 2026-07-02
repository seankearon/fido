using System;

namespace Fido.Services;

/// <summary>
/// Thrown by <see cref="OpenerService.DeleteWorktreeAsync"/> when <c>git worktree remove</c> fails — most
/// often because a path in the worktree is too long for the OS even with git's long-path support. Carries the
/// worktree path so the caller can offer a permanent, Recycle-Bin-bypassing folder delete as a fallback (see
/// <see cref="OpenerService.ForceDeleteWorktreeAsync"/>).
/// </summary>
public sealed class WorktreeRemovalException : Exception
{
    /// <summary>Absolute path of the worktree folder git couldn't remove.</summary>
    public string WorktreePath { get; }

    public WorktreeRemovalException(string worktreePath, string message) : base(message)
        => WorktreePath = worktreePath;
}
