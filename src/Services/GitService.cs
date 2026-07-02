using System;
using System.Threading;
using Fido.Models;

namespace Fido.Services;

/// <summary>Wrapper over the <c>git</c> CLI for the operations the opener needs.</summary>
public sealed class GitService
{
    private const string RefsHeads = "refs/heads/";

    /// <summary>Runs a git command in <paramref name="workingDir"/> and returns its captured result. The
    /// default shells out to the real <c>git</c> CLI; tests inject a fake to script output (e.g. a transient
    /// failure that then clears) without needing to provoke one from a real repository.</summary>
    public delegate Task<ProcessResult> GitCommandRunner(string workingDir, IReadOnlyList<string> args, CancellationToken ct);

    private readonly GitCommandRunner _run;

    public GitService(GitCommandRunner? run = null) => _run = run ?? new GitCommandRunner(DefaultRun);

    private static Task<ProcessResult> DefaultRun(string dir, IReadOnlyList<string> args, CancellationToken ct)
        => ProcessRunner.RunAsync("git", args, dir, ct);

    private Task<ProcessResult> Git(string dir, CancellationToken ct, params string[] args)
        => _run(dir, args, ct);

    public async Task<bool> IsInsideWorkTreeAsync(string dir, CancellationToken ct = default)
    {
        var r = await Git(dir, ct, "rev-parse", "--is-inside-work-tree");
        return r.Success && r.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses <c>git worktree list --porcelain</c>. Records are separated by blank lines;
    /// the first record is the main working tree (<see cref="WorktreeInfo.IsMain"/>).
    /// </summary>
    public async Task<List<WorktreeInfo>> ListWorktreesAsync(string dir, CancellationToken ct = default)
    {
        var result = new List<WorktreeInfo>();
        var r = await Git(dir, ct, "worktree", "list", "--porcelain");
        if (!r.Success) return result;

        string? path = null, head = null, branch = null;
        bool bare = false, detached = false, isFirst = true;

        void Flush()
        {
            if (path is null) return;
            result.Add(new WorktreeInfo(path, head ?? "", branch, bare, detached, IsMain: isFirst));
            isFirst = false;
            path = null; head = null; branch = null; bare = false; detached = false;
        }

        foreach (var raw in r.StdOut.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) { Flush(); continue; }

            if (line.StartsWith("worktree ", StringComparison.Ordinal)) { Flush(); path = line["worktree ".Length..]; }
            else if (line.StartsWith("HEAD ", StringComparison.Ordinal)) head = line["HEAD ".Length..];
            else if (line.StartsWith("branch ", StringComparison.Ordinal)) branch = StripRefsHeads(line["branch ".Length..]);
            else if (line == "bare") bare = true;
            else if (line == "detached") detached = true;
        }
        Flush();
        return result;
    }

    public async Task<bool> LocalBranchExistsAsync(string dir, string branch, CancellationToken ct = default)
        => (await Git(dir, ct, "rev-parse", "--verify", "--quiet", RefsHeads + branch)).Success;

    public async Task<bool> RemoteBranchExistsAsync(string dir, string branch, CancellationToken ct = default)
        => (await Git(dir, ct, "rev-parse", "--verify", "--quiet", $"refs/remotes/origin/{branch}")).Success;

    /// <summary>
    /// True when <paramref name="branch"/> exists on the <c>origin</c> remote right now — queried live with
    /// <c>git ls-remote</c>. Unlike <see cref="RemoteBranchExistsAsync"/> (which reads the cached
    /// <c>refs/remotes/origin/*</c> tracking refs) this also finds branches pushed to origin but never
    /// fetched into this clone.
    /// </summary>
    public async Task<bool> RemoteHasBranchAsync(string dir, string branch, CancellationToken ct = default)
    {
        var r = await Git(dir, ct, "ls-remote", "--heads", "origin", branch);
        if (!r.Success) return false;

        // ls-remote prints "<sha>\t<ref>" lines and matches the pattern as a path suffix, so asking for
        // "x" would also return "refs/heads/feature/x". Require an exact ref match to avoid false positives.
        var wanted = '\t' + RefsHeads + branch;
        return r.StdOut
            .Split('\n')
            .Any(line => line.TrimEnd('\r').EndsWith(wanted, StringComparison.Ordinal));
    }

    /// <summary>
    /// Fetches <paramref name="branch"/> from <c>origin</c> into its <c>refs/remotes/origin/&lt;branch&gt;</c>
    /// tracking ref, so a subsequent switch/worktree can create a local branch that tracks it. Used when the
    /// branch exists on the remote but hasn't been fetched into this clone yet.
    /// </summary>
    public Task<ProcessResult> FetchBranchAsync(string dir, string branch, CancellationToken ct = default)
        => Git(dir, ct, "fetch", "origin", $"{RefsHeads}{branch}:refs/remotes/origin/{branch}");

    /// <summary>Current branch of the working tree, or <c>"HEAD"</c> when detached.</summary>
    public async Task<string> GetCurrentBranchAsync(string dir, CancellationToken ct = default)
    {
        var r = await Git(dir, ct, "rev-parse", "--abbrev-ref", "HEAD");
        return r.Success ? r.StdOut.Trim() : "HEAD";
    }

    /// <summary>URL of the <c>origin</c> remote, or null if there is none.</summary>
    public async Task<string?> GetRemoteUrlAsync(string dir, CancellationToken ct = default)
    {
        var r = await Git(dir, ct, "remote", "get-url", "origin");
        if (!r.Success) return null;
        var url = r.StdOut.Trim();
        return url.Length > 0 ? url : null;
    }

    /// <summary>Full SHA of HEAD, or null on failure.</summary>
    public async Task<string?> GetHeadShaAsync(string dir, CancellationToken ct = default)
    {
        var r = await Git(dir, ct, "rev-parse", "HEAD");
        if (!r.Success) return null;
        var sha = r.StdOut.Trim();
        return sha.Length > 0 ? sha : null;
    }

    /// <summary>
    /// Resolves the repo's default branch via <c>origin/HEAD</c>, falling back to the first
    /// of <paramref name="fallbacks"/> that exists locally or as a remote-tracking branch.
    /// </summary>
    public async Task<string?> GetDefaultBranchAsync(string dir, IReadOnlyList<string> fallbacks, CancellationToken ct = default)
    {
        var r = await Git(dir, ct, "symbolic-ref", "--short", "refs/remotes/origin/HEAD");
        if (r.Success)
        {
            var name = r.StdOut.Trim();
            if (name.StartsWith("origin/", StringComparison.Ordinal)) name = name["origin/".Length..];
            if (name.Length > 0) return name;
        }

        foreach (var candidate in fallbacks)
        {
            if (await LocalBranchExistsAsync(dir, candidate, ct) || await RemoteBranchExistsAsync(dir, candidate, ct))
                return candidate;
        }
        return null;
    }

    /// <summary>Outstanding changes as porcelain lines; empty when the tree is clean.</summary>
    public async Task<List<string>> GetStatusAsync(string dir, CancellationToken ct = default)
    {
        var r = await Git(dir, ct, "status", "--porcelain=v1");
        if (!r.Success) return new();
        return r.StdOut
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0)
            .ToList();
    }

    // --- Mutating operations in the main working tree -----------------------------------

    public Task<ProcessResult> SwitchAsync(string dir, string branch, CancellationToken ct = default)
        => Git(dir, ct, "switch", branch);

    public Task<ProcessResult> SwitchNewTrackingAsync(string dir, string branch, CancellationToken ct = default)
        => Git(dir, ct, "switch", "-c", branch, "--track", $"origin/{branch}");

    public Task<ProcessResult> SwitchNewFromAsync(string dir, string branch, string? startPoint, CancellationToken ct = default)
        => startPoint is null
            ? Git(dir, ct, "switch", "-c", branch)
            : Git(dir, ct, "switch", "-c", branch, startPoint);

    // --- Worktree creation --------------------------------------------------------------

    public Task<ProcessResult> WorktreeAddExistingAsync(string dir, string path, string branch, CancellationToken ct = default)
        => Git(dir, ct, "worktree", "add", path, branch);

    public Task<ProcessResult> WorktreeAddNewAsync(string dir, string path, string branch, string? startPoint, CancellationToken ct = default)
        => startPoint is null
            ? Git(dir, ct, "worktree", "add", "-b", branch, path)
            : Git(dir, ct, "worktree", "add", "-b", branch, path, startPoint);

    // --- Worktree / branch deletion -----------------------------------------------------

    /// <summary>
    /// True when <paramref name="dir"/> is a <em>linked</em> worktree rather than the clone's main tree.
    /// Compares the worktree's own git dir with the shared common git dir — identical for the main tree, but
    /// a linked worktree's git dir is <c>…/worktrees/&lt;name&gt;</c>. It never compares working-tree paths, so
    /// it stays correct under a symlinked search root that would defeat a string path comparison. False on any
    /// git error (so the destructive delete action is withheld when the topology can't be established).
    /// </summary>
    public async Task<bool> IsLinkedWorktreeAsync(string dir, CancellationToken ct = default)
    {
        var r = await Git(dir, ct, "rev-parse", "--path-format=absolute", "--git-dir", "--git-common-dir");
        if (!r.Success) return false;
        var lines = r.StdOut.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();
        return lines.Length >= 2 && !string.Equals(lines[0], lines[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// Counts commits reachable from <paramref name="branch"/> but from no other ref — no other local branch,
    /// no remote-tracking branch, no tag. These are the commits that would be <em>orphaned</em> (lost to normal
    /// use) if the branch were force-deleted: unpushed, unmerged work that exists nowhere else. Zero when the
    /// branch is fully pushed or already merged. Returns 0 on any git error — it drives an advisory warning,
    /// not a gate.
    /// </summary>
    public async Task<int> CountOrphanedCommitsAsync(string dir, string branch, CancellationToken ct = default)
    {
        var r = await Git(dir, ct, "rev-list", "--count", branch,
            "--not", "--exclude=" + branch, "--branches", "--tags", "--remotes");
        return r.Success && int.TryParse(r.StdOut.Trim(), out var n) ? n : 0;
    }

    /// <summary>
    /// Removes the linked worktree at <paramref name="worktreePath"/>. Run from the clone's main tree
    /// so it can drop a worktree it isn't standing in. A clean worktree removes without
    /// <paramref name="force"/>; a dirty one (uncommitted or untracked files) needs it, and forcing
    /// discards those changes.
    /// </summary>
    public Task<ProcessResult> WorktreeRemoveAsync(string dir, string worktreePath, bool force, CancellationToken ct = default)
        => force
            ? Git(dir, ct, "worktree", "remove", "--force", worktreePath)
            : Git(dir, ct, "worktree", "remove", worktreePath);

    /// <summary>
    /// Force-deletes the local branch (<c>git branch -D</c>) — used once its worktree is gone, so the
    /// branch is no longer checked out. <c>-D</c> deletes even when the branch isn't merged, matching the
    /// user's explicit intent to remove it.
    /// </summary>
    public Task<ProcessResult> DeleteLocalBranchAsync(string dir, string branch, CancellationToken ct = default)
        => Git(dir, ct, "branch", "-D", branch);

    /// <summary>Deletes the branch on <c>origin</c> (<c>git push origin --delete &lt;branch&gt;</c>).</summary>
    public Task<ProcessResult> DeleteRemoteBranchAsync(string dir, string branch, CancellationToken ct = default)
        => Git(dir, ct, "push", "origin", "--delete", branch);

    private static string StripRefsHeads(string reference)
        => reference.StartsWith(RefsHeads, StringComparison.Ordinal) ? reference[RefsHeads.Length..] : reference;
}
