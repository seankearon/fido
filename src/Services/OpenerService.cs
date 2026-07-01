using System;
using System.IO;
using System.Threading;
using Fido.Models;

namespace Fido.Services;

/// <summary>
/// UI-agnostic orchestration of the open-in-Rider flow. The view layer drives the
/// steps (so it can interleave dialogs) and observes progress via the log callback.
/// </summary>
public sealed class OpenerService
{
    /// <summary>
    /// Solution file extensions recognised, in preference order: a full solution (<c>.sln</c>/<c>.slnx</c>)
    /// or a Visual Studio solution filter (<c>.slnf</c>) — a subset view that editors such as Rider and
    /// Visual Studio open directly. Full solutions are listed first so they win de-duplication over a
    /// filter that shares the same base name.
    /// </summary>
    private static readonly string[] SolutionExtensions = [".sln", ".slnx", ".slnf"];

    /// <summary>Glob patterns for the recognised <see cref="SolutionExtensions"/>, kept in sync with it.</summary>
    private static readonly string[] SolutionGlobs = [.. SolutionExtensions.Select(ext => "*" + ext)];

    /// <summary>Project file extensions recognised when locating a repo, in preference order.</summary>
    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];

    /// <summary>
    /// All openable target extensions used to locate a repo — solutions first so that, when a clone
    /// has both, the solution wins de-duplication and a bare project only matches a project-only repo.
    /// </summary>
    private static readonly string[] TargetExtensions = [.. SolutionExtensions, .. ProjectExtensions];

    private readonly GitService _git;
    private readonly SolutionFinder _finder;
    private readonly WorkingTreeFinder _workingTreeFinder;
    private readonly Action<string> _log;
    private readonly Action<string> _liveLog;

    public OpenerService(GitService git, SolutionFinder finder, WorkingTreeFinder workingTreeFinder,
        Action<string>? log = null, Action<string>? liveLog = null)
    {
        _git = git;
        _finder = finder;
        _workingTreeFinder = workingTreeFinder;
        _log = log ?? (_ => { });
        _liveLog = liveLog ?? (_ => { });
    }

    /// <summary>
    /// Branch-only discovery: scans the search roots for git working trees currently on
    /// <paramref name="branch"/> and returns their folders (a branch can be checked out in several).
    /// </summary>
    public async Task<IReadOnlyList<BranchFolder>> FindBranchFoldersAsync(AppConfig config, string branch, CancellationToken ct = default)
    {
        var trees = _workingTreeFinder.Find(config.SearchRoots, config.SearchDepth);
        _log($"Scanning {trees.Count} working tree(s) for branch '{branch}'…");

        var matches = new List<BranchFolder>();
        foreach (var dir in trees)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.Equals(await _git.GetCurrentBranchAsync(dir, ct), branch, StringComparison.Ordinal))
                continue;

            var head = await _git.GetHeadShaAsync(dir, ct) ?? "";
            var origin = await _git.GetRemoteUrlAsync(dir, ct);
            matches.Add(new BranchFolder(dir, head, GitHostLinks.GitHubCommitUrl(origin, head)));
        }

        _log($"Found {matches.Count} folder(s) on '{branch}'.");
        return matches;
    }

    /// <summary>All solution files (.sln/.slnx/.slnf) under a folder, depth-limited.</summary>
    public IReadOnlyList<string> FindSolutionsInFolder(string folder, AppConfig config)
        => _finder.Find([folder], SolutionGlobs, config.SearchDepth);

    /// <summary>
    /// Finds repositories whose tree contains a solution or project matching <paramref name="solutionName"/>,
    /// deduplicated by canonical main working tree (so copies inside worktrees collapse to one).
    /// A solution is preferred over a bare project when the same clone has both.
    /// </summary>
    public async Task<IReadOnlyList<RepositoryInfo>> FindRepositoriesAsync(
        string solutionName, AppConfig config, CancellationToken ct = default)
    {
        var baseName = StripTargetExtension(solutionName);
        var patterns = TargetExtensions.Select(ext => baseName + ext).ToList();
        _log($"Searching for {string.Join(" / ", patterns)} under {config.SearchRoots.Count} root(s)…");

        var matches = _finder.Find(config.SearchRoots, patterns, config.SearchDepth);
        _log($"Found {matches.Count} candidate solution file(s).");

        var byMain = new Dictionary<string, RepositoryInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var slnPath in matches)
        {
            ct.ThrowIfCancellationRequested();
            var dir = Path.GetDirectoryName(slnPath);
            if (dir is null) continue;

            // The first worktree git reports is the canonical main tree; if the .sln isn't
            // inside a git repo at all, fall back to its own directory.
            var worktrees = await _git.ListWorktreesAsync(dir, ct);
            var mainPath = worktrees.FirstOrDefault(w => w.IsMain)?.Path ?? dir;
            mainPath = Path.GetFullPath(mainPath);

            if (!byMain.ContainsKey(mainPath))
                byMain[mainPath] = new RepositoryInfo(mainPath, Path.GetFileName(slnPath));
        }

        return byMain.Values.ToList();
    }

    /// <summary>
    /// Every distinct repository (canonical main working tree) found under the search roots, regardless
    /// of solution — used to offer a place to create a branch that isn't checked out anywhere. Linked
    /// worktrees collapse back to their main clone, mirroring <see cref="FindRepositoriesAsync"/>.
    /// </summary>
    public async Task<IReadOnlyList<RepositoryInfo>> FindAllRepositoriesAsync(
        AppConfig config, CancellationToken ct = default)
    {
        var trees = _workingTreeFinder.Find(config.SearchRoots, config.SearchDepth);
        var byMain = new Dictionary<string, RepositoryInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in trees)
        {
            ct.ThrowIfCancellationRequested();
            var worktrees = await _git.ListWorktreesAsync(dir, ct);
            var mainPath = Path.GetFullPath(worktrees.FirstOrDefault(w => w.IsMain)?.Path ?? dir);
            byMain.TryAdd(mainPath, new RepositoryInfo(mainPath, ""));
        }

        return byMain.Values.ToList();
    }

    /// <summary>
    /// Of <paramref name="repos"/>, those that contain <paramref name="branch"/> — as a local branch, an
    /// <c>origin</c> remote-tracking branch, or a branch that exists on <c>origin</c> but hasn't been fetched
    /// yet (checked live). Lets the branch-only flow offer a repo only where the branch genuinely exists,
    /// rather than silently spawning an unrelated new branch elsewhere — and find branches a clone hasn't
    /// fetched. The remote is queried only when both local checks miss, so already-known branches stay offline.
    /// </summary>
    public async Task<IReadOnlyList<RepositoryInfo>> FindReposWithBranchAsync(
        IReadOnlyList<RepositoryInfo> repos, string branch, CancellationToken ct = default)
    {
        var found = new List<RepositoryInfo>();
        foreach (var repo in repos)
        {
            ct.ThrowIfCancellationRequested();
            var dir = repo.MainWorktreePath;

            // Narrate the hunt in place — one line ticking through the repo names, not a line per repo
            // (mirrors the close countdown). The "remote" line covers both the cached origin tracking ref
            // and the live ls-remote query, and shows only once the local-branch check has missed.
            _liveLog($"Searching for local branch in {repo.Name}");
            if (await _git.LocalBranchExistsAsync(dir, branch, ct))
            {
                found.Add(repo);
                continue;
            }

            _liveLog($"Searching for remote branch in {repo.Name}");
            if (await _git.RemoteBranchExistsAsync(dir, branch, ct)
                || await _git.RemoteHasBranchAsync(dir, branch, ct))
            {
                found.Add(repo);
            }
        }

        return found;
    }

    /// <summary>
    /// Scans every candidate clone for a worktree already checked out on <paramref name="branch"/>
    /// (a clone's own main tree counts). This spans clones so a branch already checked out in one
    /// clone is reused rather than duplicated into another clone of the same repo.
    /// <para>
    /// When <paramref name="branch"/> is a default-branch alias (one of <paramref name="mainBranchNames"/>,
    /// e.g. <c>main</c>) that doesn't actually exist in a clone, a worktree on that clone's <em>other</em>
    /// default-branch alias (e.g. <c>master</c>) counts as a match — so asking for <c>main</c> reuses the
    /// <c>master</c> checkout already on disk instead of reporting "not checked out anywhere" and offering
    /// to create a redundant branch. The alias fallback never overrides a branch that genuinely exists.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<ExistingCheckout>> FindExistingCheckoutsAsync(
        IReadOnlyList<RepositoryInfo> repos, string branch, IReadOnlyList<string> mainBranchNames, CancellationToken ct = default)
    {
        var found = new List<ExistingCheckout>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var branchIsDefaultAlias = IsDefaultAlias(branch, mainBranchNames);

        foreach (var repo in repos)
        {
            ct.ThrowIfCancellationRequested();
            var worktrees = await _git.ListWorktreesAsync(repo.MainWorktreePath, ct);

            // Fall back to a default-branch alias only when the requested branch genuinely doesn't
            // exist in this clone — otherwise a real, distinct branch of that name would be hijacked.
            var aliasFallback = branchIsDefaultAlias
                && !await _git.LocalBranchExistsAsync(repo.MainWorktreePath, branch, ct)
                && !await _git.RemoteBranchExistsAsync(repo.MainWorktreePath, branch, ct);

            foreach (var wt in worktrees)
            {
                var exact = string.Equals(wt.Branch, branch, StringComparison.Ordinal);
                var alias = !exact && aliasFallback && IsDefaultAlias(wt.Branch, mainBranchNames);
                if (!exact && !alias) continue;
                if (!seenPaths.Add(Path.GetFullPath(wt.Path))) continue;

                if (alias)
                    _log($"'{branch}' doesn't exist here; '{wt.Branch}' is the default branch and is already checked out — reusing it.");
                found.Add(new ExistingCheckout(repo, wt));
            }
        }

        return found;
    }

    /// <summary>True when <paramref name="branch"/> is one of the configured default-branch names (e.g. main/master).</summary>
    private static bool IsDefaultAlias(string? branch, IReadOnlyList<string> mainBranchNames)
        => branch is not null && mainBranchNames.Any(n => string.Equals(n, branch, StringComparison.OrdinalIgnoreCase));

    /// <summary>Gathers display facts about a clone for the chooser: origin, current branch, worktree count.</summary>
    public async Task<(string? OriginUrl, string CurrentBranch, int WorktreeCount)> DescribeRepoAsync(
        RepositoryInfo repo, CancellationToken ct = default)
    {
        var dir = repo.MainWorktreePath;
        var origin = await _git.GetRemoteUrlAsync(dir, ct);
        var current = await _git.GetCurrentBranchAsync(dir, ct);
        var worktrees = await _git.ListWorktreesAsync(dir, ct);
        return (origin, current, worktrees.Count);
    }

    /// <summary>Gathers the main tree's state and the plan for placing the branch.</summary>
    public async Task<MainContext> BuildMainContextAsync(
        RepositoryInfo repo, string branch, AppConfig config, CancellationToken ct = default)
    {
        var dir = repo.MainWorktreePath;
        var currentBranch = await _git.GetCurrentBranchAsync(dir, ct);
        var existsLocal = await _git.LocalBranchExistsAsync(dir, branch, ct);
        var existsRemote = await _git.RemoteBranchExistsAsync(dir, branch, ct);
        var status = await _git.GetStatusAsync(dir, ct);

        // No local branch and no cached remote-tracking ref doesn't mean the branch is new — it may exist on
        // origin but never have been fetched into this clone. Ask the remote directly before treating it as
        // brand-new; if it's there, track it (fetching the ref first) instead of creating a divergent branch.
        var requiresFetch = false;
        if (!existsLocal && !existsRemote && await _git.RemoteHasBranchAsync(dir, branch, ct))
        {
            existsRemote = true;
            requiresFetch = true;
        }

        string? startPoint = null;
        var startIsRemote = false;
        string startDescription;

        if (existsLocal)
        {
            startDescription = $"existing local branch '{branch}'";
        }
        else if (existsRemote)
        {
            startPoint = $"origin/{branch}";
            startIsRemote = true;
            startDescription = requiresFetch
                ? $"remote branch origin/{branch} (will fetch and track)"
                : $"remote branch origin/{branch} (will track)";
        }
        else
        {
            // Brand-new branch: prefer the default branch's remote-tracking ref as start point.
            var defaultBranch = await _git.GetDefaultBranchAsync(dir, config.MainBranchNames, ct);
            if (defaultBranch is not null && await _git.RemoteBranchExistsAsync(dir, defaultBranch, ct))
            {
                startPoint = $"origin/{defaultBranch}";
                startDescription = $"new branch from origin/{defaultBranch}";
            }
            else if (defaultBranch is not null)
            {
                startPoint = defaultBranch;
                startDescription = $"new branch from {defaultBranch}";
            }
            else
            {
                startPoint = null;   // current HEAD
                startDescription = $"new branch from current HEAD ({currentBranch})";
            }
        }

        return new MainContext
        {
            MainWorktreePath = dir,
            CurrentBranch = currentBranch,
            BranchExistsLocally = existsLocal,
            BranchExistsOnRemote = existsRemote,
            RequiresFetch = requiresFetch,
            OutstandingChanges = status,
            ProposedWorktreePath = BuildWorktreePath(repo, branch, config),
            StartPoint = startPoint,
            StartPointIsRemoteTracking = startIsRemote,
            StartPointDescription = startDescription,
        };
    }

    /// <summary>Switches the main working tree to the branch (creating it if needed). Returns its path.</summary>
    public async Task<string> CheckoutInMainAsync(RepositoryInfo repo, string branch, MainContext ctx, CancellationToken ct = default)
    {
        var dir = repo.MainWorktreePath;
        ProcessResult result;

        if (ctx.BranchExistsLocally)
        {
            _log($"Switching main working tree to existing branch '{branch}'…");
            result = await _git.SwitchAsync(dir, branch, ct);
        }
        else if (ctx.BranchExistsOnRemote)
        {
            if (ctx.RequiresFetch) await FetchTrackingRefAsync(dir, branch, ct);
            _log($"Creating local branch '{branch}' tracking origin/{branch}…");
            result = await _git.SwitchNewTrackingAsync(dir, branch, ct);
        }
        else
        {
            _log($"Creating '{branch}' ({ctx.StartPointDescription})…");
            result = await _git.SwitchNewFromAsync(dir, branch, ctx.StartPoint, ct);
        }

        if (!result.Success)
            throw new InvalidOperationException($"git switch failed: {result.Message}");

        _log($"Main working tree is now on '{branch}'.");
        return dir;
    }

    /// <summary>Creates a new linked worktree for the branch and returns its path.</summary>
    public async Task<string> CreateWorktreeAsync(RepositoryInfo repo, string branch, MainContext ctx, CancellationToken ct = default)
    {
        var dir = repo.MainWorktreePath;
        var path = EnsureUniquePath(ctx.ProposedWorktreePath);

        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

        ProcessResult result;
        if (ctx.BranchExistsLocally || ctx.BranchExistsOnRemote)
        {
            if (ctx.RequiresFetch) await FetchTrackingRefAsync(dir, branch, ct);
            // For a remote-only branch, `git worktree add <path> <branch>` DWIMs a local
            // tracking branch from origin/<branch>.
            _log($"Adding worktree for '{branch}' at {path}…");
            result = await _git.WorktreeAddExistingAsync(dir, path, branch, ct);
        }
        else
        {
            _log($"Adding worktree with new branch '{branch}' at {path} ({ctx.StartPointDescription})…");
            result = await _git.WorktreeAddNewAsync(dir, path, branch, ctx.StartPoint, ct);
        }

        if (!result.Success)
            throw new InvalidOperationException($"git worktree add failed: {result.Message}");

        _log($"Worktree ready at {path}.");
        return path;
    }

    // --- Worktree deletion --------------------------------------------------------------

    /// <summary>
    /// True when <paramref name="folder"/> is a <em>linked</em> worktree (not its clone's main working
    /// tree). Only a linked worktree can be removed with <c>git worktree remove</c>, so this gates whether
    /// the branch-folder chooser offers the delete action. Cheap, offline, and symlink-proof (it compares
    /// git dirs, not paths — see <see cref="GitService.IsLinkedWorktreeAsync"/>).
    /// </summary>
    public Task<bool> IsLinkedWorktreeAsync(string folder, CancellationToken ct = default)
        => _git.IsLinkedWorktreeAsync(folder, ct);

    /// <summary>
    /// Gathers what a "delete this worktree" action would remove: the clone's main tree (where the git steps
    /// run), whether the branch is on <c>origin</c> (cached tracking ref, then a live check for a branch never
    /// fetched), any outstanding changes in the worktree, and how many commits live only on the branch (so the
    /// dialog can warn about losing unpushed, unmerged work). Returns null when <paramref name="folder"/> is
    /// the clone's main tree — that can't be removed as a worktree.
    /// </summary>
    public async Task<WorktreeDeletion?> BuildWorktreeDeletionAsync(string folder, string branch, CancellationToken ct = default)
    {
        if (!await _git.IsLinkedWorktreeAsync(folder, ct))
            return null;

        var full = Path.GetFullPath(folder);
        var worktrees = await _git.ListWorktreesAsync(folder, ct);
        var mainPath = Path.GetFullPath(worktrees.FirstOrDefault(w => w.IsMain)?.Path ?? folder);

        var remoteExists = await _git.RemoteBranchExistsAsync(mainPath, branch, ct)
                           || await _git.RemoteHasBranchAsync(mainPath, branch, ct);
        var changes = await _git.GetStatusAsync(full, ct);
        var orphaned = await _git.CountOrphanedCommitsAsync(mainPath, branch, ct);
        return new WorktreeDeletion(mainPath, full, branch, remoteExists, changes, orphaned);
    }

    /// <summary>
    /// Carries out a <see cref="WorktreeDeletion"/>: removes the worktree (forcing when it's dirty), deletes
    /// the now-free local branch, and — when the branch is on <c>origin</c> — deletes it there too. Runs from
    /// the clone's main tree. A failed worktree removal or local-branch delete throws (nothing has been lost
    /// yet, or the local cleanup couldn't proceed); a failed <em>remote</em> delete is logged and reported as
    /// a partial success (<c>false</c>) rather than throwing, because the local worktree and branch are
    /// already gone and re-running wouldn't undo that.
    /// </summary>
    public async Task<bool> DeleteWorktreeAsync(WorktreeDeletion plan, CancellationToken ct = default)
    {
        var dir = plan.MainWorktreePath;

        _log($"Removing worktree at {plan.WorktreePath}…");
        var remove = await _git.WorktreeRemoveAsync(dir, plan.WorktreePath, force: plan.HasOutstandingChanges, ct);
        if (!remove.Success)
            throw new InvalidOperationException($"git worktree remove failed: {remove.Message}");
        _log("Worktree removed.");

        _log($"Deleting local branch '{plan.Branch}'…");
        var branchResult = await _git.DeleteLocalBranchAsync(dir, plan.Branch, ct);
        if (!branchResult.Success)
            throw new InvalidOperationException($"git branch -D failed: {branchResult.Message}");
        _log($"Local branch '{plan.Branch}' deleted.");

        if (!plan.RemoteBranchExists)
            return true;

        _log($"Deleting remote branch origin/{plan.Branch}…");
        var remoteResult = await _git.DeleteRemoteBranchAsync(dir, plan.Branch, ct);
        if (remoteResult.Success)
        {
            _log($"Remote branch origin/{plan.Branch} deleted.");
            return true;
        }

        _log($"[!] Remote branch origin/{plan.Branch} could not be deleted: {remoteResult.Message}");
        return false;
    }

    /// <summary>
    /// Fetches <c>origin/&lt;branch&gt;</c> into the clone so a tracking branch or worktree can be created from
    /// a branch that exists on the remote but hadn't been fetched yet (see <see cref="MainContext.RequiresFetch"/>).
    /// </summary>
    private async Task FetchTrackingRefAsync(string dir, string branch, CancellationToken ct)
    {
        _log($"Fetching origin/{branch} (not yet in this clone)…");
        var result = await _git.FetchBranchAsync(dir, branch, ct);
        if (!result.Success)
            throw new InvalidOperationException($"git fetch failed: {result.Message}");
    }

    /// <summary>
    /// Resolves what to hand the editor: the solution file in folder mode falls back to the folder
    /// when the <c>.sln</c> can't be located.
    /// </summary>
    public LaunchTarget ResolveTarget(string workingDir, RepositoryInfo repo, OpenMode mode)
    {
        if (mode == OpenMode.Folder)
            return new LaunchTarget(workingDir, IsSolution: false);

        string? found = null;
        try
        {
            found = Directory.EnumerateFiles(workingDir, repo.SolutionFileName, SearchOption.AllDirectories).FirstOrDefault();
        }
        catch
        {
            // fall through to folder fallback
        }

        if (found is not null)
            return new LaunchTarget(found, IsSolution: true);

        _log($"[!] {repo.SolutionFileName} not found under {workingDir}; opening the folder instead.");
        return new LaunchTarget(workingDir, IsSolution: false);
    }

    // --- Path helpers -------------------------------------------------------------------

    private static string BuildWorktreePath(RepositoryInfo repo, string branch, AppConfig config)
    {
        var sanitized = SanitizeBranch(branch);

        if (!string.IsNullOrWhiteSpace(config.WorktreeRoot))
            return Path.Combine(config.WorktreeRoot, sanitized);

        var repoDir = repo.MainWorktreePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(repoDir) ?? repoDir;
        var repoName = Path.GetFileName(repoDir);
        return Path.Combine(parent, $"{repoName}.worktrees", sanitized);
    }

    private static string SanitizeBranch(string branch)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = branch
            .Select(c => c is '/' or '\\' || Array.IndexOf(invalid, c) >= 0 ? '-' : c)
            .ToArray();
        return new string(chars);
    }

    private static string EnsureUniquePath(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return path;
        for (var i = 2; ; i++)
        {
            var candidate = $"{path}-{i}";
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }
    }

    private static string StripTargetExtension(string name)
    {
        name = name.Trim();
        foreach (var ext in TargetExtensions)
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return name[..^ext.Length];
        return name;
    }
}
