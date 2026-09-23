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

    /// <summary>Retries a transiently-failing pre-run pull. Kept to a single quick retry rather than the
    /// deletion profile's three: the user is waiting on a console, and a pull that still won't go through
    /// costs them nothing but a stale tree. See <see cref="UpdateBeforeRunAsync"/>.</summary>
    private readonly ResiliencePipeline<ProcessResult> _pullRetry;

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

        var pullOptions = retryOptions with { MaxRetryAttempts = 1 };
        _pullRetry = GitRetry.BuildPipeline(pullOptions, attempt =>
            _log($"[!] {attempt.Operation} failed (transient) — retrying in "
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
    /// Inline discovery for the main screen: scans the search roots to reach every git clone, then asks
    /// git itself — <c>git worktree list</c> — which of that clone's working trees is on
    /// <paramref name="branch"/> and describes each as a <see cref="DiscoveredTarget"/> — linked worktree
    /// or main clone, owning repo name, the solution files inside it, and when it last changed. Worktrees
    /// are listed before main clones (they're the likelier target, and only they can be deleted). Asking
    /// git rather than matching the scanned folders means a worktree that lives <em>outside</em> the
    /// search roots — a central worktree directory, one nested past <see cref="AppConfig.SearchDepth"/>,
    /// or one created inside the main tree — is still found and offered to open, instead of slipping
    /// through to a placement card that git would reject because the branch is already checked out there.
    /// When the branch is checked out <em>nowhere</em>, the scanned clones are consulted instead: any
    /// whose refs contain the branch — a local branch, the cached <c>origin</c> tracking ref, or (as a
    /// last resort) a live <c>ls-remote</c> — is offered as a <see cref="TargetKind.NewWorktree"/> that
    /// opening will create. The blocking directory scans run on the thread pool: this is called from a
    /// keystroke-debounced loop, so it must never stall the UI thread the way the older dialog-driven
    /// flow could afford to. <paramref name="onTreeCount"/> reports how many working trees are being
    /// checked as soon as the enumeration finishes, so the UI can narrate "Scanning N working tree(s)…",
    /// and <paramref name="onClones"/> hands over the clones the scan reached the moment they're all
    /// known — branch-independent, so the caller can keep them and answer "where would this branch's
    /// worktree go" for every later branch without scanning again.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredTarget>> DiscoverTargetsAsync(
        AppConfig config, string branch, Action<int>? onTreeCount = null,
        Action<IReadOnlyList<string>>? onClones = null, CancellationToken ct = default)
    {
        var trees = await Task.Run(() => _workingTreeFinder.Find(config.SearchRoots, config.SearchDepth), ct);
        onTreeCount?.Invoke(trees.Count);

        var targets = new List<DiscoveredTarget>();
        // Every clone reached from the scan, keyed by its main working tree: both the candidate pool for
        // the create-a-worktree offer (when the branch is checked out nowhere) and the dedup key that
        // stops one clone's worktrees being enumerated once per working tree the scan happened to surface.
        var clones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in trees)
        {
            ct.ThrowIfCancellationRequested();
            var mainPath = await _git.GetMainWorktreePathAsync(dir, ct) ?? dir;
            if (!clones.Add(mainPath)) continue;   // this clone's worktrees are already accounted for

            // Ask git for every worktree of this clone — authoritative, so a worktree living outside the
            // search roots is still surfaced. The first entry git reports is the main tree; the rest are
            // linked worktrees, which are the only removable ones.
            foreach (var wt in await _git.ListWorktreesAsync(mainPath, ct))
            {
                if (!string.Equals(wt.Branch, branch, StringComparison.Ordinal)) continue;

                var full = Path.GetFullPath(wt.Path);
                if (!seenPaths.Add(full)) continue;

                var kind = wt.IsMain ? TargetKind.MainClone : TargetKind.Worktree;
                var solutions = await Task.Run(() => FindSolutionsInFolder(full, config), ct);

                DateTime? updated = null;
                try { updated = Directory.GetLastWriteTimeUtc(full); }
                catch { /* advisory meta only — an unreadable timestamp shouldn't hide the target */ }

                targets.Add(new DiscoveredTarget(full, kind, RepoNameOf(mainPath), mainPath, solutions, updated));
            }
        }

        // Every clone is known now, whatever the branch turns out to be checked out in — hand them over
        // before the (potentially network-bound) placement pass so the UI needn't wait on it.
        onClones?.Invoke([.. clones]);

        if (targets.Count == 0)
            targets.AddRange(await FindPlacementCandidatesAsync(clones, branch, config, ct));

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

    // --- Moving a branch into the main clone ---------------------------------------------

    /// <summary>
    /// The "move to main clone" offers for a scan whose branch is checked out only in linked worktrees, for
    /// a branch whose <c>.fido/cfg.yaml</c> prefers the main clone. git won't switch a main tree onto a branch
    /// another worktree holds, so the preference can't simply pick a card; this offers, per clone, to release
    /// the branch from its worktree and switch that clone's main tree onto it (see
    /// <see cref="TargetKind.MoveToMainClone"/>). Empty when any main tree is already on the branch — the
    /// preference is honoured as it stands — or when there's no worktree to move from. Only reads: the move
    /// itself is <see cref="MoveToMainCloneAsync"/>, and only after the user confirms it.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredTarget>> FindMoveToMainOffersAsync(
        IReadOnlyList<DiscoveredTarget> targets, AppConfig config, CancellationToken ct = default)
    {
        if (targets.Any(t => t.Kind == TargetKind.MainClone)) return [];

        var offers = new List<DiscoveredTarget>();
        var seenClones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var worktree in targets.Where(t => t.Kind == TargetKind.Worktree))
        {
            ct.ThrowIfCancellationRequested();
            if (!seenClones.Add(worktree.MainPath)) continue;   // git lets one branch into one worktree per clone

            var solutions = await Task.Run(() => FindSolutionsInFolder(worktree.MainPath, config), ct);
            var currentBranch = await _git.GetCurrentBranchAsync(worktree.MainPath, ct);
            var changes = await _git.GetStatusAsync(worktree.MainPath, ct);
            offers.Add(new DiscoveredTarget(
                worktree.MainPath, TargetKind.MoveToMainClone, worktree.RepoName, worktree.MainPath, solutions,
                UpdatedUtc: null, CurrentBranch: currentBranch, UncommittedChanges: changes.Count,
                HeldByWorktree: worktree.Path));
        }
        return offers;
    }

    /// <summary>
    /// What moving <paramref name="branch"/> from its worktree into the main tree would do right now —
    /// gathered fresh when the move is asked for, not when the card was offered, because either tree can
    /// have changed since the scan. Feeds the confirm strip.
    /// </summary>
    public async Task<MainCloneMove> BuildMainCloneMoveAsync(
        DiscoveredTarget offer, string branch, CancellationToken ct = default)
    {
        var worktree = offer.HeldByWorktree
                       ?? throw new InvalidOperationException("A move-to-main-clone offer names no worktree.");
        var worktreeChanges = await _git.GetStatusAsync(worktree, ct);
        var currentBranch = await _git.GetCurrentBranchAsync(offer.MainPath, ct);
        var mainChanges = await _git.GetStatusAsync(offer.MainPath, ct);
        return new MainCloneMove(offer.MainPath, worktree, branch, currentBranch, worktreeChanges, mainChanges);
    }

    /// <summary>
    /// Carries out a confirmed <see cref="MainCloneMove"/>: removes the worktree — never forced, so git
    /// refuses rather than lose a change the plan didn't see — and then switches the main tree onto the
    /// branch. The branch itself is never touched. Returns the main tree's path.
    /// <para>The removal comes first because git allows nothing else, and it's the step that can't be taken
    /// back, so a switch that then fails is said plainly: the branch is intact and checked out nowhere, and
    /// a rescan offers to place it again.</para>
    /// </summary>
    public async Task<string> MoveToMainCloneAsync(MainCloneMove plan, AppConfig config, CancellationToken ct = default)
    {
        if (!plan.CanMove)
            throw new InvalidOperationException(
                $"The worktree at {plan.WorktreePath} has changes — commit, stash or remove them first.");

        _log($"Removing worktree at {plan.WorktreePath} (the branch '{plan.Branch}' stays)…");
        var remove = await GitRetry.ExecuteAsync(_deletionRetry, "worktree remove",
            token => _git.WorktreeRemoveAsync(plan.MainWorktreePath, plan.WorktreePath, force: false, token), ct);
        if (!remove.Success)
            throw new InvalidOperationException(
                $"git couldn't remove the worktree — close anything open in it and try again: {remove.Message}");
        _log("Worktree removed.");

        var repo = new RepositoryInfo(plan.MainWorktreePath, "");
        try
        {
            var ctx = await BuildMainContextAsync(repo, plan.Branch, config, ct);
            return await CheckoutInMainAsync(repo, plan.Branch, ctx, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log($"[!] The worktree is gone, but the main clone couldn't switch to '{plan.Branch}' — the branch " +
                 "is intact and checked out nowhere; rescan to place it.");
            throw;
        }
    }

    // --- Updating before a run ------------------------------------------------------------

    /// <summary>
    /// Brings <paramref name="folder"/> level with its upstream before a command is run in it, so a script or
    /// <c>aspire start</c> runs against what <c>origin</c> has rather than whatever was last checked out here.
    /// <para>
    /// Deliberately indifferent to how the folder came to be. A worktree Fido created moments ago is no more
    /// trustworthy than one that has sat on disk for weeks: <see cref="CreateWorktreeAsync"/> and
    /// <see cref="CheckoutInMainAsync"/> both check out the <em>local</em> branch when there is one, which may
    /// be well behind the remote — only the fetch-and-track path is current by construction. Since the target's
    /// kind can't tell you whether it needs updating, this runs on all of them; a genuinely current tree
    /// answers "already up to date" for the cost of one round trip.
    /// </para>
    /// <para>
    /// Advisory by design: it never throws and its result never withholds a launch. A branch that tracks
    /// nothing is skipped (there is nothing to pull), and a pull git refuses — a diverged branch, local changes
    /// in the way, an unreachable origin — is narrated and left alone, because a stale tree the user can still
    /// work in beats a console that didn't open.
    /// </para>
    /// </summary>
    public async Task<WorktreeUpdate> UpdateBeforeRunAsync(string folder, CancellationToken ct = default)
    {
        var branch = await _git.GetCurrentBranchAsync(folder, ct);
        if (string.Equals(branch, "HEAD", StringComparison.Ordinal))
        {
            _log("▸ Detached HEAD here — nothing to pull.");
            return new WorktreeUpdate(WorktreeUpdateStatus.Skipped);
        }

        if (await _git.GetUpstreamAsync(folder, ct) is not { } upstream)
        {
            _log($"▸ '{branch}' tracks nothing — nothing to pull.");
            return new WorktreeUpdate(WorktreeUpdateStatus.Skipped);
        }

        _liveLog($"Pulling {upstream}…");
        var pull = await GitRetry.ExecuteAsync(_pullRetry, "pull",
            token => _git.PullFastForwardAsync(folder, token), ct);

        if (pull.Success)
        {
            _log($"✓ '{branch}' is up to date with {upstream}.");
            return new WorktreeUpdate(WorktreeUpdateStatus.UpToDate, upstream);
        }

        _log($"[!] Couldn't fast-forward '{branch}' onto {upstream}: {pull.Message}");
        _log("▸ Running against the tree as it stands.");
        return new WorktreeUpdate(WorktreeUpdateStatus.Failed, upstream, pull.Message);
    }

    // --- Pull requests ------------------------------------------------------------------

    /// <summary>
    /// Asks GitHub whether <paramref name="branch"/> has an open pull request, from
    /// <paramref name="mainPath"/> (a clone's main working tree, so <c>gh</c> resolves the repo from its
    /// <c>origin</c> remote). The answer is never cached — every caller asks again, so what the screen
    /// shows is what GitHub said this time round rather than what it said when the branch was typed.
    /// <para>Narrated in one in-place flight-log line: the question while gh is being asked, then the
    /// answer over the top of it. All three outcomes are said out loud, because "GitHub says there is no
    /// open PR" and "gh couldn't be asked" are not the same thing to anyone deciding whether to delete a
    /// branch.</para>
    /// </summary>
    public async Task<PullRequestLookup> FindOpenPullRequestAsync(
        string mainPath, string branch, CancellationToken ct = default)
    {
        _liveLog($"Asking GitHub about open pull requests for '{branch}'…");
        var lookup = await _gitHub.LookUpOpenPullRequestAsync(mainPath, branch, ct);

        _liveLog(lookup switch
        {
            { PullRequest: { } pr } => $"▸ Pull request #{pr.Number} is open for '{branch}' — {pr.Url}",
            { Status: PullRequestLookupStatus.None } => $"▸ No open pull request for '{branch}' on GitHub.",
            _ => $"▸ Couldn't ask GitHub about pull requests for '{branch}' — "
                 + "the GitHub CLI (gh) isn't installed, isn't signed in, or this repo isn't on GitHub.",
        });

        return lookup;
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
        // Asked fresh here rather than reusing whatever the scan found — the confirm strip must speak for
        // the state of the PR now, not for the state it had when the branch was typed.
        var openPr = remoteExists ? (await FindOpenPullRequestAsync(mainPath, branch, ct)).PullRequest : null;
        return new WorktreeDeletion(mainPath, full, branch, remoteExists, changes, orphaned, openPr);
    }

    /// <summary>
    /// Carries out a <see cref="WorktreeDeletion"/> limited to the targets the user ticked in
    /// <paramref name="choice"/>: removes the worktree (forcing when it's dirty), deletes the local branch, and
    /// deletes the branch on <c>origin</c> — each only when selected (and the origin branch only when it exists).
    /// Runs from the clone's main tree. Each git step is wrapped in <see cref="GitRetry"/>, so a
    /// <em>transient</em> failure — a worktree file still locked by an editor, a racing ref <c>.lock</c>, a
    /// network blip on the origin delete — is retried a few times (narrated in the log) before it counts; a
    /// permanent failure fails on the first attempt.
    /// <para>The three targets are independent and <em>tolerant</em>: a step that fails no longer abandons the
    /// ones after it, and one whose target had <em>already</em> gone (a branch someone deleted on the server, a
    /// folder cleared by hand) is reported as success — see <see cref="GitAlreadyGone"/>. Everything lands in
    /// the returned <see cref="WorktreeDeletionOutcome"/>, whose
    /// <see cref="WorktreeDeletionOutcome.Outstanding"/> names just what's still standing, for a retry that
    /// re-runs only those steps. The one exception is a genuinely failed worktree removal, which still
    /// throws a <see cref="WorktreeRemovalException"/> so the caller can offer
    /// <see cref="ForceDeleteWorktreeAsync"/> as a fallback.</para>
    /// </summary>
    public async Task<WorktreeDeletionOutcome> DeleteWorktreeAsync(
        WorktreeDeletion plan, WorktreeDeletionChoice choice, CancellationToken ct = default)
    {
        var steps = new List<WorktreeDeletionStep>();

        if (choice.Worktree)
            steps.Add(await RemoveWorktreeAsync(plan, ct));

        steps.AddRange(await DeleteBranchesAsync(plan, choice, ct));
        return new WorktreeDeletionOutcome(steps);
    }

    /// <summary>
    /// Fallback for when <see cref="DeleteWorktreeAsync"/> raised a <see cref="WorktreeRemovalException"/> —
    /// typically a path too long for the OS. Permanently deletes the worktree folder straight from disk (a
    /// recursive delete that <em>bypasses the Recycle Bin</em> and, on Windows, uses an extended-length path so
    /// it isn't defeated by the same limit that stopped git), then prunes git's now-dangling worktree
    /// registration so the branch is free to delete, and finishes the ticked branch deletions. The caller must
    /// have confirmed the destructive folder delete first. A folder that even this can't remove is reported as
    /// a failed step rather than thrown — the branch deletions still run, and the outcome carries the retry.
    /// </summary>
    public async Task<WorktreeDeletionOutcome> ForceDeleteWorktreeAsync(
        WorktreeDeletion plan, WorktreeDeletionChoice choice, CancellationToken ct = default)
    {
        var steps = new List<WorktreeDeletionStep> { await ForceRemoveFolderAsync(plan, ct) };
        steps.AddRange(await DeleteBranchesAsync(plan, choice, ct));
        return new WorktreeDeletionOutcome(steps);
    }

    /// <summary>
    /// Removes the linked worktree, retrying transient failures. A worktree git no longer knows about counts as
    /// <see cref="DeletionStepStatus.AlreadyGone"/> (its registration is pruned so the branch is free to
    /// delete); anything else throws <see cref="WorktreeRemovalException"/> so the caller can offer the
    /// disk-level fallback.
    /// </summary>
    private async Task<WorktreeDeletionStep> RemoveWorktreeAsync(WorktreeDeletion plan, CancellationToken ct)
    {
        _log($"Removing worktree at {plan.WorktreePath}…");
        var remove = await GitRetry.ExecuteAsync(_deletionRetry, "worktree remove",
            token => _git.WorktreeRemoveAsync(plan.MainWorktreePath, plan.WorktreePath, force: plan.HasOutstandingChanges, token), ct);

        if (remove.Success)
        {
            _log("Worktree removed.");
            return new WorktreeDeletionStep(DeletionTarget.Worktree, DeletionStepStatus.Deleted);
        }

        // Nothing to remove isn't a failure. git knows a worktree by its registration, so "not a working tree"
        // — or a folder that simply isn't on disk any more (removed by hand, or by an earlier attempt that
        // stumbled later on) — means we're already where the user asked to be. Prune the stale registration so
        // the branch is no longer considered checked out, and carry on to the branches.
        if (GitAlreadyGone.Worktree(remove) || !Directory.Exists(plan.WorktreePath))
        {
            _log($"Worktree at {plan.WorktreePath} was already gone — pruning git's registration.");
            await PruneAsync(plan.MainWorktreePath, ct);
            return new WorktreeDeletionStep(DeletionTarget.Worktree, DeletionStepStatus.AlreadyGone, remove.Message);
        }

        throw new WorktreeRemovalException(plan.WorktreePath, remove.Message);
    }

    /// <summary>Deletes the worktree folder straight from disk and prunes git's registration, reporting a
    /// folder that resisted even that as a failed step rather than throwing.</summary>
    private async Task<WorktreeDeletionStep> ForceRemoveFolderAsync(WorktreeDeletion plan, CancellationToken ct)
    {
        _log($"Force-deleting worktree folder {plan.WorktreePath} (bypassing the Recycle Bin)…");
        try
        {
            await Task.Run(() => ForceDeleteFolder(plan.WorktreePath), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log($"[!] The worktree folder {plan.WorktreePath} could not be deleted: {ex.Message}");
            return new WorktreeDeletionStep(DeletionTarget.Worktree, DeletionStepStatus.Failed, ex.Message);
        }

        _log("Worktree folder deleted; pruning git's worktree registration…");
        await PruneAsync(plan.MainWorktreePath, ct);
        return new WorktreeDeletionStep(DeletionTarget.Worktree, DeletionStepStatus.Deleted);
    }

    /// <summary>Drops git's registration of any worktree whose folder has gone. Advisory — a prune that
    /// complains is narrated but never fails the deletion.</summary>
    private async Task PruneAsync(string mainWorktreePath, CancellationToken ct)
    {
        var prune = await _git.PruneWorktreesAsync(mainWorktreePath, ct);
        if (!prune.Success)
            _log($"[!] git worktree prune reported: {prune.Message}");
    }

    /// <summary>
    /// Deletes the local branch and the branch on <c>origin</c> per <paramref name="choice"/> (the shared tail
    /// of both <see cref="DeleteWorktreeAsync"/> and <see cref="ForceDeleteWorktreeAsync"/>). Neither throws:
    /// each reports its own step, and a failed local delete no longer withholds the remote one — they're
    /// independent, and the outcome says exactly which of them is still standing.
    /// </summary>
    private async Task<List<WorktreeDeletionStep>> DeleteBranchesAsync(
        WorktreeDeletion plan, WorktreeDeletionChoice choice, CancellationToken ct)
    {
        var steps = new List<WorktreeDeletionStep>();
        var dir = plan.MainWorktreePath;

        if (choice.LocalBranch)
        {
            _log($"Deleting local branch '{plan.Branch}'…");
            var branchResult = await GitRetry.ExecuteAsync(_deletionRetry, "local branch delete",
                token => _git.DeleteLocalBranchAsync(dir, plan.Branch, token), ct);
            steps.Add(Report(DeletionTarget.LocalBranch, branchResult,
                GitAlreadyGone.LocalBranch,
                deleted: $"Local branch '{plan.Branch}' deleted.",
                alreadyGone: $"Local branch '{plan.Branch}' was already gone — nothing to delete.",
                failed: $"Local branch '{plan.Branch}' could not be deleted"));
        }

        // An open pull request withholds the remote delete even when the caller ticked it — deleting
        // origin/<branch> would sever the PR. The UI also gates this, but enforce it where git runs.
        if (choice.RemoteBranch && plan.RemoteBranchExists && !plan.RemoteDeletionBlocked)
        {
            _log($"Deleting remote branch origin/{plan.Branch}…");
            // Retrying the push is safe — deleting an already-gone branch is a no-op in effect, and git says so
            // ("remote ref does not exist"), which lands as AlreadyGone rather than a failure. That also settles
            // the one rare wrinkle: a transient drop *after* origin deleted the ref but before git read the ack
            // used to report failure though the branch was in fact gone.
            var remoteResult = await GitRetry.ExecuteAsync(_deletionRetry, "remote branch delete",
                token => _git.DeleteRemoteBranchAsync(dir, plan.Branch, token), ct);
            steps.Add(Report(DeletionTarget.RemoteBranch, remoteResult,
                GitAlreadyGone.RemoteBranch,
                deleted: $"Remote branch origin/{plan.Branch} deleted.",
                alreadyGone: $"Remote branch origin/{plan.Branch} was already gone — nothing to delete.",
                failed: $"Remote branch origin/{plan.Branch} could not be deleted"));
        }

        return steps;
    }

    /// <summary>Narrates one branch-delete result into the flight log and turns it into its step: success,
    /// nothing-to-do (per <paramref name="isAlreadyGone"/>), or a failure the caller can retry.</summary>
    private WorktreeDeletionStep Report(DeletionTarget target, ProcessResult result,
        Func<ProcessResult, bool> isAlreadyGone, string deleted, string alreadyGone, string failed)
    {
        if (result.Success)
        {
            _log(deleted);
            return new WorktreeDeletionStep(target, DeletionStepStatus.Deleted);
        }

        if (isAlreadyGone(result))
        {
            _log(alreadyGone);
            return new WorktreeDeletionStep(target, DeletionStepStatus.AlreadyGone, result.Message);
        }

        _log($"[!] {failed}: {result.Message}");
        return new WorktreeDeletionStep(target, DeletionStepStatus.Failed, result.Message);
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

    private static string BuildWorktreePath(RepositoryInfo repo, string branch, AppConfig config) =>
        WorktreePath.InRepo(repo.MainWorktreePath, branch, config);

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
