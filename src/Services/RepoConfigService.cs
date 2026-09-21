using System;
using System.IO;
using System.Threading;
using Fido.Models;

namespace Fido.Services;

/// <summary>
/// Reads a repository's own Fido settings — the <c>.fido/cfg.yaml</c> file a repo commits — for a
/// discovered target, so the branch can say how it prefers to be opened <em>before</em> the checkout
/// options are offered. A missing, unreadable or unparseable file is not an error: it yields no
/// configuration and the scan carries on exactly as it always has.
/// <para>
/// A branch can carry the file in three places at once, and they need not agree, so
/// <see cref="ReadAsync"/> takes them in a fixed order (see <see cref="RepoConfigSource"/>):
/// <list type="number">
///   <item><description>the <b>local edit</b> — the file in the working tree with changes that aren't
///   committed, which is the one being written right now;</description></item>
///   <item><description>the copy on <b>origin</b>, whenever it differs from the one this machine has —
///   the case a stale checkout creates, where the branch grew a config after this worktree was made and
///   reading only what's on disk would silently miss it;</description></item>
///   <item><description>the <b>local</b> copy — the committed file in the tree, or the one on the local
///   branch ref for a branch that's checked out nowhere.</description></item>
/// </list>
/// A copy that parses to nothing is treated as no file at all, so it never shadows the one below it: a
/// freshly created starter file can't mask the settings the branch really carries. Everything is read
/// from what this machine already has — the working tree, and the tracking ref as last fetched — because
/// a scan runs on a keystroke and must never go to the network.
/// </para>
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

    /// <summary>What counts as "a script": what the starter file names in its comment, and what
    /// <see cref="RunnerShell"/> makes explicitly relative for PowerShell.</summary>
    private static readonly string[] ScriptExtensions = [".ps1", ".cmd", ".bat", ".sh"];

    private readonly GitService _git;

    public RepoConfigService(GitService git) => _git = git;

    /// <summary>
    /// The Fido configuration <paramref name="branch"/> carries at <paramref name="target"/> and which copy
    /// of it answered, or null when the branch carries none that asks for anything.
    /// <para>
    /// A real checkout (<see cref="TargetKind.Worktree"/> / <see cref="TargetKind.MainClone"/>) has a file
    /// on disk to read; a placement offer has only refs, and for <see cref="TargetKind.SwitchMainClone"/>
    /// the folder on disk is another branch entirely, so reading it would answer the wrong question. Either
    /// way <c>origin</c>'s copy is consulted too and preferred when it differs, because what's here can be
    /// older than the branch — the whole point of the order the class comment sets out.
    /// </para>
    /// </summary>
    public async Task<RepoConfigRead?> ReadAsync(DiscoveredTarget target, string branch, CancellationToken ct = default)
    {
        var checkedOut = target.Kind is TargetKind.Worktree or TargetKind.MainClone;

        // 1. The local edit: a file in the tree that isn't committed as it stands. Asking git costs a
        //    process, so only ask when there's actually a file here to be in that state.
        var onDisk = checkedOut ? await Task.Run(() => ReadFileText(PathIn(target.Path)), ct) : null;
        if (onDisk is not null
            && await _git.HasUncommittedChangesAsync(target.Path, RepoRelativePath, ct)
            && Applies(onDisk) is { } edited)
            return new RepoConfigRead(edited, RepoConfigSource.LocalEdit);

        // 2. origin's copy, when it says something different to the one here. Identical text means the
        //    checkout is level on this file, and there's nothing to report — it reads as the local copy.
        var local = checkedOut ? onDisk : await LocalRefTextAsync(target, branch, ct);
        var onOrigin = await _git.ShowFileAsync(target.MainPath, OriginRef(branch), RepoRelativePath, ct);
        if (onOrigin is not null && !SameText(onOrigin, local) && Applies(onOrigin) is { } theirs)
            return new RepoConfigRead(theirs, RepoConfigSource.Origin);

        // 3. What this machine has.
        return local is not null && Applies(local) is { } ours
            ? new RepoConfigRead(ours, RepoConfigSource.Local)
            : null;
    }

    /// <summary>
    /// The committed copy for a placement offer, read off the local branch ref — null when this clone knows
    /// the branch only from <c>origin</c> (there's no local ref to read, and origin's copy is step 2's job).
    /// </summary>
    private async Task<string?> LocalRefTextAsync(DiscoveredTarget target, string branch, CancellationToken ct) =>
        target.BranchOnOriginOnly
            ? null
            : await _git.ShowFileAsync(target.MainPath, branch, RepoRelativePath, ct);

    /// <summary>The scripts sitting in <paramref name="folder"/> itself, sorted by name — the starter
    /// file's hint at what the commands list might want to run.</summary>
    private static IReadOnlyList<string> RootScripts(string folder) =>
        [.. EnumerateRootFiles(folder).Where(IsScript).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];

    /// <summary>The branch's tracking ref — what this clone last fetched from <c>origin</c>.</summary>
    public static string OriginRef(string branch) => "origin/" + branch;

    /// <summary>Whether <paramref name="name"/> is one of the script kinds Fido recognises. Internal so
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
    /// the commands list can be filled in without going looking.
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

                # Commands offered under the Console button, in the order given — each one run in a
                # terminal at the selected location.  e.g.  commands: [build.ps1, aspire start]
                {found}commands: []

                """.ReplaceLineEndings();
    }

    /// <summary>The text of the file at <paramref name="path"/>; null when it isn't there or can't be read.</summary>
    private static string? ReadFileText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch { return null; }   // unreadable file -> no configuration, never a failed scan
    }

    /// <summary>
    /// The settings <paramref name="yaml"/> asks for, or null when it asks for nothing Fido acts on. That
    /// second case is what lets the read fall through to the next copy: a file present but inert — a starter
    /// file nobody has edited yet — is "no config here", exactly as a missing one is.
    /// </summary>
    private static RepoConfig? Applies(string yaml) => Parse(yaml) is { IsEmpty: false } config ? config : null;

    /// <summary>
    /// Whether two copies of the file say the same thing, line endings aside — a checkout with git's
    /// CRLF translation on holds the same file as the blob on <c>origin</c>, and calling that a difference
    /// would report every Windows checkout as out of date.
    /// </summary>
    private static bool SameText(string? left, string? right) =>
        left is null
            ? right is null
            : right is not null && string.Equals(
                left.ReplaceLineEndings("\n"), right.ReplaceLineEndings("\n"), StringComparison.Ordinal);

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
            Commands = Listed("commands"),
        };

        // A command list can arrive as a sequence, or as a lone scalar (`commands: aspire start`).
        List<string> Listed(string name) =>
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

