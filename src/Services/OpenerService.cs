using System;
using System.IO;
using System.Threading;
using Fido.Models;
using Polly;

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
    private readonly GitHubCli _gitHub;

    /// <summary>Retries the transient failures the worktree/branch deletion commands hit (locked files, ref
    /// <c>.lock</c> races, network blips), narrating each retry into the flight log. See <see cref="GitRetry"/>.</summary>
    private readonly ResiliencePipeline<ProcessResult> _deletionRetry;

    public OpenerService(GitService git, SolutionFinder finder, WorkingTreeFinder workingTreeFinder,
        Action<string>? log = null, Action<string>? liveLog = null, GitRetryOptions? deletionRetry = null,
        GitHubCli? gitHub = null)
    {
        _git = git;
        _finder = finder;
        _workingTreeFinder = workingTreeFinder;
        _log = log ?? (_ => { });
        _liveLog = liveLog ?? (_ => { });
        _gitHub = gitHub ?? new GitHubCli();

        var retryOptions = deletionRetry ?? GitRetryOptions.Default;
        _deletionRetry = GitRetry.BuildPipeline(retryOptions, attempt =>
            _log($"[!] {attempt.Operation} failed (transient) — retrying "
                + $"{attempt.AttemptNumber + 1}/{retryOptions.MaxRetryAttempts} in "
                + $"{attempt.RetryDelay.TotalSeconds:0.#}s: {attempt.Failure?.Message}"));
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
    /// Inline discovery for the main screen: scans the search roots for git working trees currently on
    /// <paramref name="branch"/> and describes each as a <see cref="DiscoveredTarget"/> — linked worktree
    /// or main clone, owning repo name, the solution files inside it, and when it last changed. Worktrees
    /// are listed before main clones (they're the likelier target, and only they can be deleted). When the
    /// branch is checked out <em>nowhere</em>, the scanned clones are consulted instead: any whose refs
    /// contain the branch — a local branch, the cached <c>origin</c> tracking ref, or (as a last resort)
    /// a live <c>ls-remote</c> — is offered as a <see cref="TargetKind.NewWorktree"/> that opening will
    /// create. The blocking directory scans run on the thread pool: this is called from a
    /// keystroke-debounced loop, so it must never stall the UI thread the way the older dialog-driven
    /// flow could afford to. <paramref name="onTreeCount"/> reports how many working trees are being
    /// checked as soon as the enumeration finishes, so the UI can narrate "Scanning N working tree(s)…".
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredTarget>> DiscoverTargetsAsync(
        AppConfig config, string branch, Action<int>? onTreeCount = null, CancellationToken ct = default)
    {
        var trees = await Task.Run(() => _workingTreeFinder.Find(config.SearchRoots, config.SearchDepth), ct);
        onTreeCount?.Invoke(trees.Count);

        var targets = new List<DiscoveredTarget>();
        // Every clone seen during the scan, keyed by its main working tree — the candidate pool for
        // the create-a-worktree offer when the branch turns out to be checked out nowhere.
        var clones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in trees)
        {
            ct.ThrowIfCancellationRequested();
            var mainPath = await _git.GetMainWorktreePathAsync(dir, ct) ?? dir;
            clones.TryAdd(mainPath, mainPath);

            if (!string.Equals(await _git.GetCurrentBranchAsync(dir, ct), branch, StringComparison.Ordinal))
                continue;

            var kind = await _git.IsLinkedWorktreeAsync(dir, ct) ? TargetKind.Worktree : TargetKind.MainClone;
            var solutions = await Task.Run(() => FindSolutionsInFolder(dir, config), ct);

            DateTime? updated = null;
            try { updated = Directory.GetLastWriteTimeUtc(dir); }
            catch { /* advisory meta only — an unreadable timestamp shouldn't hide the target */ }

            targets.Add(new DiscoveredTarget(dir, kind, RepoNameOf(mainPath), mainPath, solutions, updated));
        }

        if (targets.Count == 0)
            targets.AddRange(await FindPlacementCandidatesAsync(clones.Keys, branch, config, ct));

        // Stable sort: worktrees ahead of main clones; among placement offers, the safer
        // create-a-worktree card leads and the switch-the-main-tree card follows.
        return [.. targets.OrderBy(t => t.Kind switch
        {
            TargetKind.Worktree => 0,
            TargetKind.MainClone => 1,
            TargetKind.NewWorktree => 2,
            _ => 3,
        })];
    }

    /// <summary>
    /// The placement offers for a branch checked out nowhere: each clone whose refs contain
    /// <paramref name="branch"/> yields two cards — create a linked worktree (safe, leads), or switch
    /// the clone's main tree onto the branch (mutates a tree the user may be working in; the card
    /// carries the current branch and any uncommitted-change count so the UI can warn). Cached refs
    /// (local branch, <c>refs/remotes/origin</c>) are consulted first — fast and offline, so the
    /// keystroke-debounced scan stays cheap. Only when no clone has a cached ref does it fall back to
    /// one live <c>ls-remote</c> sweep, narrated per repo, which finds branches pushed to origin that
    /// this machine never fetched. The actual fetch/switch/worktree-add happens at open time
    /// (<see cref="BuildMainContextAsync"/> and friends), not here.
    /// </summary>
    private async Task<List<DiscoveredTarget>> FindPlacementCandidatesAsync(
        IEnumerable<string> clonePaths, string branch, AppConfig config, CancellationToken ct)
    {
        var candidates = new List<DiscoveredTarget>();

        async Task AddCandidatesAsync(string mainPath, bool remoteOnly)
        {
            var repoName = RepoNameOf(mainPath);
            var solutions = await Task.Run(() => FindSolutionsInFolder(mainPath, config), ct);

            candidates.Add(new DiscoveredTarget(
                BuildWorktreePath(new RepositoryInfo(mainPath, ""), branch, config),
                TargetKind.NewWorktree, repoName, mainPath, solutions,
                UpdatedUtc: null, BranchOnOriginOnly: remoteOnly));

            var currentBranch = await _git.GetCurrentBranchAsync(mainPath, ct);
            var changes = await _git.GetStatusAsync(mainPath, ct);
            candidates.Add(new DiscoveredTarget(
                mainPath, TargetKind.SwitchMainClone, repoName, mainPath, solutions,
                UpdatedUtc: null, BranchOnOriginOnly: remoteOnly,
                CurrentBranch: currentBranch, UncommittedChanges: changes.Count));
        }

        var misses = new List<string>();
        foreach (var mainPath in clonePaths)
        {
            ct.ThrowIfCancellationRequested();
            if (await _git.LocalBranchExistsAsync(mainPath, branch, ct))
                await AddCandidatesAsync(mainPath, remoteOnly: false);
            else if (await _git.RemoteBranchExistsAsync(mainPath, branch, ct))
                await AddCandidatesAsync(mainPath, remoteOnly: true);
            else
                misses.Add(mainPath);
        }

        if (candidates.Count > 0) return candidates;

        // Nothing cached anywhere: ask each origin directly, once. A teammate (or a cloud session)
        // may have pushed the branch after this machine last fetched.
        foreach (var mainPath in misses)
        {
            ct.ThrowIfCancellationRequested();
            _liveLog($"Checking origin of {RepoNameOf(mainPath)} for '{branch}'…");
            if (await _git.RemoteHasBranchAsync(mainPath, branch, ct))
                await AddCandidatesAsync(mainPath, remoteOnly: true);
        }

        return candidates;
    }

    /// <summary>The clone's display name — its main working tree's folder name.</summary>
    private static string RepoNameOf(string mainPath) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(mainPath));

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
        // Only worth asking gh when there's a remote branch to delete; an open PR blocks that deletion.
        var openPr = remoteExists ? await _gitHub.FindOpenPullRequestAsync(mainPath, branch, ct) : null;
        return new WorktreeDeletion(mainPath, full, branch, remoteExists, changes, orphaned, openPr);
    }

    /// <summary>
    /// Carries out a <see cref="WorktreeDeletion"/> limited to the targets the user ticked in
    /// <paramref name="choice"/>: removes the worktree (forcing when it's dirty), deletes the local branch, and
    /// deletes the branch on <c>origin</c> — each only when selected (and the origin branch only when it exists).
    /// Runs from the clone's main tree. Each git step is wrapped in <see cref="GitRetry"/>, so a
    /// <em>transient</em> failure — a worktree file still locked by an editor, a racing ref <c>.lock</c>, a
    /// network blip on the origin delete — is retried a few times (narrated in the log) before it counts; a
    /// permanent failure fails on the first attempt. A failed worktree removal throws a
    /// <see cref="WorktreeRemovalException"/> (so the caller can offer <see cref="ForceDeleteWorktreeAsync"/> as
    /// a fallback); a failed local-branch delete throws (the local cleanup couldn't proceed); a failed
    /// <em>remote</em> delete is logged and reflected in the returned outcome rather than throwing, because any
    /// local cleanup is already done and re-running wouldn't undo it.
    /// </summary>
    public async Task<WorktreeDeletionOutcome> DeleteWorktreeAsync(
        WorktreeDeletion plan, WorktreeDeletionChoice choice, CancellationToken ct = default)
    {
        var worktreeRemoved = false;

        if (choice.Worktree)
        {
            _log($"Removing worktree at {plan.WorktreePath}…");
            var remove = await GitRetry.ExecuteAsync(_deletionRetry, "worktree remove",
                token => _git.WorktreeRemoveAsync(plan.MainWorktreePath, plan.WorktreePath, force: plan.HasOutstandingChanges, token), ct);
            if (!remove.Success)
                throw new WorktreeRemovalException(plan.WorktreePath, remove.Message);
            _log("Worktree removed.");
            worktreeRemoved = true;
        }

        return await DeleteBranchesAsync(plan, choice, worktreeRemoved, ct);
    }

    /// <summary>
    /// Fallback for when <see cref="DeleteWorktreeAsync"/> raised a <see cref="WorktreeRemovalException"/> —
    /// typically a path too long for the OS. Permanently deletes the worktree folder straight from disk (a
    /// recursive delete that <em>bypasses the Recycle Bin</em> and, on Windows, uses an extended-length path so
    /// it isn't defeated by the same limit that stopped git), then prunes git's now-dangling worktree
    /// registration so the branch is free to delete, and finishes the ticked branch deletions. The caller must
    /// have confirmed the destructive folder delete first.
    /// </summary>
    public async Task<WorktreeDeletionOutcome> ForceDeleteWorktreeAsync(
        WorktreeDeletion plan, WorktreeDeletionChoice choice, CancellationToken ct = default)
    {
        _log($"Force-deleting worktree folder {plan.WorktreePath} (bypassing the Recycle Bin)…");
        await Task.Run(() => ForceDeleteFolder(plan.WorktreePath), ct);
        _log("Worktree folder deleted; pruning git's worktree registration…");

        var prune = await _git.PruneWorktreesAsync(plan.MainWorktreePath, ct);
        if (!prune.Success)
            _log($"[!] git worktree prune reported: {prune.Message}");

        return await DeleteBranchesAsync(plan, choice, worktreeRemoved: true, ct);
    }

    /// <summary>
    /// Deletes the local branch and the branch on <c>origin</c> per <paramref name="choice"/> (the shared tail
    /// of both <see cref="DeleteWorktreeAsync"/> and <see cref="ForceDeleteWorktreeAsync"/>, run once the
    /// worktree is gone). A failed local delete throws; a failed remote delete is logged and flagged in the
    /// outcome rather than thrown. See <see cref="DeleteWorktreeAsync"/> for the retry semantics.
    /// </summary>
    private async Task<WorktreeDeletionOutcome> DeleteBranchesAsync(
        WorktreeDeletion plan, WorktreeDeletionChoice choice, bool worktreeRemoved, CancellationToken ct)
    {
        var dir = plan.MainWorktreePath;
        bool localDeleted = false, remoteDeleted = false, remoteFailed = false;

        if (choice.LocalBranch)
        {
            _log($"Deleting local branch '{plan.Branch}'…");
            var branchResult = await GitRetry.ExecuteAsync(_deletionRetry, "local branch delete",
                token => _git.DeleteLocalBranchAsync(dir, plan.Branch, token), ct);
            if (!branchResult.Success)
                throw new InvalidOperationException($"git branch -D failed: {branchResult.Message}");
            _log($"Local branch '{plan.Branch}' deleted.");
            localDeleted = true;
        }

        // An open pull request withholds the remote delete even when the caller ticked it — deleting
        // origin/<branch> would sever the PR. The UI also gates this, but enforce it where git runs.
        if (choice.RemoteBranch && plan.RemoteBranchExists && !plan.RemoteDeletionBlocked)
        {
            _log($"Deleting remote branch origin/{plan.Branch}…");
            // Retrying the push is safe — deleting an already-gone branch is a no-op in effect. One rare,
            // non-destructive wrinkle: if a transient drop happens *after* origin deleted the ref but before
            // git reads the ack, the retry sees "remote ref does not exist" (permanent) and reports failure
            // though the branch is in fact gone. We don't infer success from that message — on a first attempt
            // it legitimately means the ref was already gone, which callers surface as a NO-GO — so we accept
            // the occasional misleading report over guessing.
            var remoteResult = await GitRetry.ExecuteAsync(_deletionRetry, "remote branch delete",
                token => _git.DeleteRemoteBranchAsync(dir, plan.Branch, token), ct);
            if (remoteResult.Success)
            {
                _log($"Remote branch origin/{plan.Branch} deleted.");
                remoteDeleted = true;
            }
            else
            {
                _log($"[!] Remote branch origin/{plan.Branch} could not be deleted: {remoteResult.Message}");
                remoteFailed = true;
            }
        }

        return new WorktreeDeletionOutcome(worktreeRemoved, localDeleted, remoteDeleted, remoteFailed);
    }

    /// <summary>
    /// Permanently deletes <paramref name="folder"/> and everything under it — a direct recursive delete, never
    /// the Recycle Bin. Clears read-only attributes first (git marks pack files read-only) and, on Windows, runs
    /// against an extended-length (<c>\\?\</c>) path so it isn't defeated by the same MAX_PATH limit that stopped
    /// git. A folder that's already gone (git removed it partially before failing) is a no-op.
    /// </summary>
    private static void ForceDeleteFolder(string folder)
    {
        var full = Path.GetFullPath(folder);
        if (!Directory.Exists(full)) return;

        var target = ExtendedPath(full);
        foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); }
            catch { /* best effort — a genuinely locked file will surface on the delete below */ }
        }
        Directory.Delete(target, recursive: true);
    }

    /// <summary>
    /// On Windows, prefixes a full path with <c>\\?\</c> (or <c>\\?\UNC\</c> for a network share) so the Win32
    /// file APIs skip MAX_PATH normalisation — the whole point of the fallback is to remove paths that were
    /// already "too long" for git. A no-op on other platforms and when the prefix is already present.
    /// </summary>
    private static string ExtendedPath(string fullPath)
    {
        if (!OperatingSystem.IsWindows() || fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            return fullPath;
        return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + fullPath.TrimStart('\\')   // \\server\share\… → \\?\UNC\server\share\…
            : @"\\?\" + fullPath;
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
