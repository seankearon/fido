namespace Fido.Models;

/// <summary>
/// A prompt to permanently delete a worktree folder that <c>git worktree remove</c> couldn't — most often
/// because a path inside it is too long for the OS. The delete is a direct, recursive removal from disk: it
/// <em>bypasses the Recycle Bin</em> and can't be undone.
/// </summary>
/// <param name="WorktreePath">Absolute path of the worktree folder to delete.</param>
/// <param name="Reason">The git failure that prompted the offer, shown so the user knows why it's needed.</param>
public sealed record WorktreeForceDelete(string WorktreePath, string Reason);
