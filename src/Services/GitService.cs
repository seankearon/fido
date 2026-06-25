using System;
using System.Threading;
using Fido.Models;

namespace Fido.Services;

/// <summary>Wrapper over the <c>git</c> CLI for the operations the opener needs.</summary>
public sealed class GitService
{
    private const string RefsHeads = "refs/heads/";

    private static Task<ProcessResult> Git(string dir, CancellationToken ct, params string[] args)
        => ProcessRunner.RunAsync("git", args, dir, ct);

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

    private static string StripRefsHeads(string reference)
        => reference.StartsWith(RefsHeads, StringComparison.Ordinal) ? reference[RefsHeads.Length..] : reference;
}
