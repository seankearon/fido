using System;
using System.IO;
using System.Threading;
using Fido.Models;

namespace Fido.Services;

/// <summary>
/// Reads a repository's own Fido settings — the <c>.fido/cfg.yaml</c> file a repo commits — for a
/// discovered target, so the branch can say how it prefers to be opened <em>before</em> the checkout
/// options are offered. The file is read from the working tree when the branch is checked out there
/// (so an edit in flight counts), and straight from the branch via <c>git show</c> for a placement
/// offer, where nothing is on disk yet. A missing, unreadable or unparseable file is not an error: it
/// yields no configuration and the scan carries on exactly as it always has.
/// Also writes the starter file (<see cref="CreateAsync"/>) behind the UI's create/edit action, so a
/// repo can be set up without hand-writing YAML — seeded at its defaults, and never overwriting one
/// the repo already has.
/// </summary>
public sealed class RepoConfigService
{
    /// <summary>The committed folder Fido looks in, at the root of the tree.</summary>
    public const string FolderName = ".fido";

    /// <summary>The configuration file inside <see cref="FolderName"/>.</summary>
    public const string FileName = "cfg.yaml";

    /// <summary>The file's repo-relative path. git's <c>&lt;ref&gt;:&lt;path&gt;</c> syntax always uses forward slashes.</summary>
    public const string RepoRelativePath = FolderName + "/" + FileName;

    /// <summary>The <c>Run files</c> wildcard: offer every script sitting in the tree root.</summary>
    public const string RunFilesWildcard = "*";

    /// <summary>What counts as "a script" when <see cref="RunFilesWildcard"/> is expanded.</summary>
    private static readonly string[] ScriptExtensions = [".ps1", ".cmd", ".bat", ".sh"];

    private readonly GitService _git;

    public RepoConfigService(GitService git) => _git = git;

    /// <summary>
    /// The Fido configuration <paramref name="branch"/> carries at <paramref name="target"/>, or null when
    /// it carries none. A real checkout (<see cref="TargetKind.Worktree"/> / <see cref="TargetKind.MainClone"/>)
    /// is read from disk; a placement offer is read from the branch itself — its own tree isn't on the
    /// branch yet, and for <see cref="TargetKind.SwitchMainClone"/> the folder on disk is another branch
    /// entirely, so reading it would answer the wrong question.
    /// </summary>
    public async Task<RepoConfig?> ReadAsync(DiscoveredTarget target, string branch, CancellationToken ct = default)
    {
        if (target.Kind is TargetKind.Worktree or TargetKind.MainClone)
            return await Task.Run(() => ReadFile(PathIn(target.Path)), ct);

        var yaml = await _git.ShowFileAsync(target.MainPath, BranchRef(target, branch), RepoRelativePath, ct);
        return yaml is null ? null : Parse(yaml);
    }

    /// <summary>
    /// The run files to offer for <paramref name="target"/>: the configured names in the order given, with a
    /// <see cref="RunFilesWildcard"/> entry expanded in place to every script at the tree root — globbed
    /// from the working tree when the branch is checked out, and listed from the branch itself
    /// (<c>git ls-tree</c>) for a placement offer. Names are de-duplicated case-insensitively, so a name
    /// listed explicitly <em>and</em> caught by the wildcard is offered once, keeping its explicit position.
    /// </summary>
    public async Task<IReadOnlyList<string>> ResolveRunFilesAsync(
        RepoConfig config, DiscoveredTarget target, string branch, CancellationToken ct = default)
    {
        var resolved = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string>? rootScripts = null;

        foreach (var entry in config.RunFiles)
        {
            if (entry != RunFilesWildcard)
            {
                if (seen.Add(entry)) resolved.Add(entry);
                continue;
            }

            rootScripts ??= await RootScriptsAsync(target, branch, ct);
            foreach (var script in rootScripts)
                if (seen.Add(script)) resolved.Add(script);
        }
        return resolved;
    }

    /// <summary>
    /// The scripts at the root of the branch's tree: enumerated on disk for a checkout, and read out of
    /// the branch with <c>git ls-tree</c> for a placement offer (nothing is on disk to look at). Sorted
    /// by name so the Console menu is stable from one scan to the next.
    /// </summary>
    private async Task<IReadOnlyList<string>> RootScriptsAsync(
        DiscoveredTarget target, string branch, CancellationToken ct)
    {
        if (target.Kind is TargetKind.Worktree or TargetKind.MainClone)
            return await Task.Run(() => RootScripts(target.Path), ct);

        var names = await _git.ListRootFilesAsync(target.MainPath, BranchRef(target, branch), ct);
        return Sorted(names);
    }

    /// <summary>The scripts sitting in <paramref name="folder"/> itself, sorted by name.</summary>
    private static IReadOnlyList<string> RootScripts(string folder) => Sorted(EnumerateRootFiles(folder));

    private static IReadOnlyList<string> Sorted(IEnumerable<string> names) =>
        [.. names.Where(IsScript).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];

    /// <summary>The ref carrying the branch for a placement offer: the local branch, or <c>origin</c>'s
    /// when this clone only knows the branch from the remote.</summary>
    internal static string BranchRef(DiscoveredTarget target, string branch) =>
        target.BranchOnOriginOnly ? "origin/" + branch : branch;

    /// <summary>Whether <paramref name="name"/> is one of the run-file kinds Fido recognises. Internal so
    /// the console shares this one list rather than keeping a second that can drift from it.</summary>
    internal static bool IsScript(string name) =>
        ScriptExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

    /// <summary>Top-level file names in <paramref name="folder"/>; empty when it can't be read.</summary>
    private static IEnumerable<string> EnumerateRootFiles(string folder)
    {
        try { return [.. Directory.EnumerateFiles(folder).Select(f => Path.GetFileName(f))]; }
        catch { return []; }
    }

    // --- Creating the file ----------------------------------------------------------------

    /// <summary>Where the config file sits inside a working tree.</summary>
    public static string PathIn(string folder) => Path.Combine(folder, FolderName, FileName);

    /// <summary>
    /// Makes sure <paramref name="folder"/> has a <c>.fido/cfg.yaml</c> to edit, seeding a new one from
    /// <see cref="Template"/> with the tree's own root scripts named in a comment. An existing file is
    /// left exactly as it is — this is how the UI's "create or edit" action reaches both cases, and it
    /// must never be able to overwrite a repo's real settings. The returned
    /// <see cref="RepoConfigFile.Created"/> says which of the two happened.
    /// </summary>
    public Task<RepoConfigFile> CreateAsync(string folder, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var path = PathIn(folder);
            if (File.Exists(path)) return new RepoConfigFile(path, Created: false);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Template(RootScripts(folder)));
            return new RepoConfigFile(path, Created: true);
        }, ct);

    /// <summary>
    /// The starter file. Every setting is present at its default, so creating it changes nothing about
    /// the scan that's on screen — it's a form to fill in, not a switch being thrown — and
    /// <paramref name="detectedScripts"/> (the tree's root scripts, if any) are named in a comment so
    /// the run-file list can be filled in without going looking.
    /// </summary>
    public static string Template(IReadOnlyList<string> detectedScripts)
    {
        var found = detectedScripts.Count > 0
            ? $"# Scripts in this tree right now: {string.Join(", ", detectedScripts)}\n"
            : "";

        return $"""
                # Fido settings for this repository — https://github.com/seankearon/fido
                #
                # Fido reads this file off the branch when a scan lands, before it offers the checkout
                # options. Commit it to share it with the team. Every setting below is at its default,
                # so the file changes nothing until you edit it.

                # Which checkout Fido offers by default once it has scanned: this clone's own working
                # tree rather than a worktree. Every location it finds is still listed, one click away.
                prefer main clone: false

                # Scripts offered under the Console button, in the order given; '*' stands for every
                # script in the repository root.  e.g.  run files: [build.ps1, '*']
                {found}run files: []

                # Offer `aspire start` under the Console button too.
                aspire start: false

                """.ReplaceLineEndings();
    }

    /// <summary>Parses the file at <paramref name="path"/>; null when it isn't there or can't be read.</summary>
    private static RepoConfig? ReadFile(string path)
    {
        try { return File.Exists(path) ? Parse(File.ReadAllText(path)) : null; }
        catch { return null; }   // unreadable file -> no configuration, never a failed scan
    }

    // --- Parsing --------------------------------------------------------------------------

    /// <summary>
    /// Parses the small YAML subset <c>.fido/cfg.yaml</c> needs: top-level <c>key: value</c> scalars,
    /// block sequences (<c>- item</c> lines under a key) and inline flow sequences (<c>[a, b]</c>), with
    /// comments, blank lines, a document marker and quoted values all handled. Anything richer — nested
    /// maps, anchors, multi-line scalars — is skipped rather than rejected, so an unexpected file costs
    /// the settings it carries and nothing more. Keys are matched loosely: case, spaces, dashes and
    /// underscores are ignored, so <c>preferMainClone</c>, <c>prefer-main-clone</c> and
    /// <c>Prefer main clone</c> are one and the same key.
    /// </summary>
    public static RepoConfig Parse(string yaml)
    {
        var scalars = new Dictionary<string, string>(StringComparer.Ordinal);
        var sequences = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        string? key = null;

        foreach (var raw in yaml.Split('\n'))
        {
            var line = StripComment(raw.TrimEnd('\r'));
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed is "---" or "...") continue;

            // A sequence item belongs to the key above it, whatever its indentation.
            if (trimmed == "-" || trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                if (key is null) continue;
                Add(sequences, key, Unquote(trimmed[1..]));
                continue;
            }

            // Indented and not a sequence item: nesting Fido has no use for.
            if (line.Length != trimmed.Length) continue;

            var colon = trimmed.IndexOf(':');
            if (colon <= 0) continue;
            key = NormalizeKey(trimmed[..colon]);
            var value = trimmed[(colon + 1)..].Trim();

            if (value.Length == 0) continue;                      // a block sequence (or map) follows
            if (value.StartsWith('['))                            // inline flow sequence: [a, b]
                foreach (var item in value.Trim('[', ']').Split(','))
                    Add(sequences, key, Unquote(item));
            else
                scalars[key] = Unquote(value);
        }

        return new RepoConfig
        {
            PreferMainClone = Truthy(scalars, "prefermainclone"),
            AspireStart = Truthy(scalars, "aspirestart"),
            RunFiles = RunFiles("runfiles"),
        };

        // A run-file list can arrive as a sequence, or as a lone scalar (`run files: "*"`).
        List<string> RunFiles(string name) =>
            sequences.TryGetValue(name, out var items) ? items
            : scalars.TryGetValue(name, out var single) ? [single]
            : [];

        static void Add(Dictionary<string, List<string>> into, string key, string value)
        {
            if (value.Length == 0) return;
            if (!into.TryGetValue(key, out var list)) into[key] = list = [];
            list.Add(value);
        }

        static bool Truthy(Dictionary<string, string> scalars, string key) =>
            scalars.TryGetValue(key, out var value) &&
            (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
             value == "1");
    }

    /// <summary>Drops a trailing <c>#</c> comment (and a whole-line one), per YAML's "after whitespace" rule.</summary>
    private static string StripComment(string line)
    {
        if (line.TrimStart().StartsWith('#')) return "";
        var hash = line.IndexOf(" #", StringComparison.Ordinal);
        return hash >= 0 ? line[..hash] : line;
    }

    /// <summary>Case, spaces, dashes and underscores all folded away, so any spelling of a key matches.</summary>
    private static string NormalizeKey(string key) =>
        Unquote(key).Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();

    /// <summary>Trims whitespace and a matching pair of single/double quotes.</summary>
    private static string Unquote(string value)
    {
        value = value.Trim();
        return value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]
            ? value[1..^1]
            : value;
    }
}

/// <summary>
/// The repo config file the "create or edit" action landed on: where it is, and whether this call is what
/// wrote it (<c>false</c> when the repo already had one, which is never overwritten).
/// </summary>
/// <param name="Path">Full path of the <c>.fido/cfg.yaml</c>.</param>
/// <param name="Created">True when the file was just seeded from the template.</param>
public sealed record RepoConfigFile(string Path, bool Created);

