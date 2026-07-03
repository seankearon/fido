using System.IO;
using Fido.Models;
using Fido.Services;

namespace Fido.Tests.Infrastructure;

/// <summary>
/// Builds a realistic on-disk git world in a temp folder using the <em>real</em> git CLI (through the
/// app's own <see cref="ProcessRunner"/>) and real <c>.sln</c>/<c>.csproj</c> files — the "fakes" here
/// are genuine repositories, clones and worktrees, not stubbed git output. Hermetic: the machine's
/// global/system git config is ignored and a deterministic identity is supplied, so it works on a
/// bare CI runner and never touches the developer's repos or <c>~/.gitconfig</c>.
/// </summary>
public sealed class TestRepoWorld : IDisposable
{
    public string Root { get; }
    private readonly string _originsDir;

    static TestRepoWorld()
    {
        var empty = Path.Combine(Path.GetTempPath(), "fido-empty-gitconfig");
        if (!File.Exists(empty)) File.WriteAllText(empty, "");

        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", empty);
        Environment.SetEnvironmentVariable("GIT_CONFIG_SYSTEM", empty);
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
        Environment.SetEnvironmentVariable("GIT_AUTHOR_NAME", "Fido Test");
        Environment.SetEnvironmentVariable("GIT_AUTHOR_EMAIL", "fido@test.local");
        Environment.SetEnvironmentVariable("GIT_COMMITTER_NAME", "Fido Test");
        Environment.SetEnvironmentVariable("GIT_COMMITTER_EMAIL", "fido@test.local");
    }

    public TestRepoWorld()
    {
        Root = Path.Combine(Path.GetTempPath(), "fido-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        _originsDir = Path.Combine(Root, "origins");
        Directory.CreateDirectory(_originsDir);
    }

    /// <summary>A clean search-root directory under the world (clone targets live here).</summary>
    public string SearchRoot(string name)
    {
        var dir = Path.Combine(Root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Creates a bare origin seeded with <c>{solution}.sln</c> + a project, on <paramref name="defaultBranch"/>.</summary>
    public string CreateOrigin(string name, string solutionName, string defaultBranch = "main")
    {
        var seed = Path.Combine(Root, "seed", name);
        Directory.CreateDirectory(seed);
        Git(seed, "init", "-b", defaultBranch);
        WriteSolution(seed, solutionName);
        Git(seed, "add", "-A");
        Git(seed, "commit", "-m", "seed");

        var origin = Path.Combine(_originsDir, name + ".git");
        Git(Root, "clone", "--bare", seed, origin);
        return origin;
    }

    /// <summary>Clones <paramref name="origin"/> into a search root as <paramref name="name"/> (checked out on main).</summary>
    public string Clone(string origin, string intoRoot, string name)
    {
        var dest = Path.Combine(intoRoot, name);
        Git(intoRoot, "clone", origin, dest);
        return dest;
    }

    /// <summary>Switches a clone's main working tree onto a new branch.</summary>
    public void CreateBranch(string repoPath, string branch) => Git(repoPath, "switch", "-c", branch);

    /// <summary>
    /// Publishes <paramref name="branch"/> to <paramref name="origin"/> from a throwaway clone (then discards
    /// it), so a clone made <em>earlier</em> has the branch on origin but has never fetched it — mirroring a
    /// teammate pushing a branch after you cloned. The publisher lives outside the search roots and is deleted,
    /// so it never shows up as a working tree.
    /// </summary>
    public void PublishBranchToOrigin(string origin, string branch)
    {
        var pub = Path.Combine(Root, "publishers", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(pub)!);
        Git(Root, "clone", origin, pub);
        Git(pub, "switch", "-c", branch);
        File.WriteAllText(Path.Combine(pub, "published.txt"), branch);
        Git(pub, "add", "-A");
        Git(pub, "commit", "-m", $"publish {branch}");
        Git(pub, "push", "-u", "origin", branch);
        ForceDelete(pub);   // discard the publisher so it's never scanned as a working tree
    }

    /// <summary>Adds a linked worktree on a new branch; returns the worktree path.</summary>
    public string AddWorktree(string clonePath, string branch)
    {
        var path = Path.Combine(clonePath + ".worktrees", Sanitize(branch));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Git(clonePath, "worktree", "add", "-b", branch, path);
        return path;
    }

    /// <summary>Adds a linked worktree that checks out an <em>existing</em> branch; returns the worktree path.</summary>
    public string AddWorktreeExisting(string clonePath, string branch)
    {
        var path = Path.Combine(clonePath + ".worktrees", Sanitize(branch));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Git(clonePath, "worktree", "add", path, branch);
        return path;
    }

    /// <summary>Pushes <paramref name="branch"/> from <paramref name="repoOrWorktreePath"/> to origin (sets upstream).</summary>
    public void PushBranch(string repoOrWorktreePath, string branch) =>
        Git(repoOrWorktreePath, "push", "-u", "origin", branch);

    /// <summary>Adds a commit in <paramref name="dir"/> (a new file), leaving a real commit on its current branch.</summary>
    public void CommitFile(string dir, string fileName, string contents = "x")
    {
        File.WriteAllText(Path.Combine(dir, fileName), contents);
        Git(dir, "add", "-A");
        Git(dir, "commit", "-m", $"add {fileName}");
    }

    /// <summary>
    /// Writes a solution-style file (e.g. a <c>.slnx</c> or a <c>.slnf</c> filter) at
    /// <paramref name="relativePath"/> under <paramref name="dir"/>, creating any parent
    /// folders, and returns its full path. Contents are irrelevant to discovery — Fido
    /// matches on the file name — so a placeholder is fine.
    /// </summary>
    public static string WriteSolutionFile(string dir, string relativePath, string contents = "")
    {
        var path = Path.Combine(dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    /// <summary>Leaves an uncommitted file so <c>git status</c> reports the tree dirty.</summary>
    public void MakeDirty(string repoPath) =>
        File.WriteAllText(Path.Combine(repoPath, "uncommitted.txt"), "work in progress");

    /// <summary>An in-memory <see cref="AppConfig"/> over the given search roots (for service-level tests).</summary>
    public AppConfig Config(params string[] searchRoots) => new()
    {
        SearchRoots = searchRoots.ToList(),
        MainBranchNames = new() { "main", "master" },
        SearchDepth = 8,
    };

    /// <summary>
    /// Wires a <see cref="FidoServices"/> bundle: config (search roots, worktree root) saved under a temp
    /// folder via <see cref="ConfigService"/>, plus the supplied fakes for the editor launcher and dialogs.
    /// </summary>
    internal FidoServices BuildServices(
        IReadOnlyList<string> searchRoots,
        FakeEditorLauncher launcher,
        FakeDialogService dialogs,
        string? worktreeRoot = null,
        CloseAfterOpen closeAfterOpen = CloseAfterOpen.CommandLine,
        int closeAfterOpenDelaySeconds = 0,
        GitService? git = null)
    {
        var config = new AppConfig
        {
            SearchRoots = searchRoots.ToList(),
            MainBranchNames = new() { "main", "master" },
            SearchDepth = 8,
            WorktreeRoot = worktreeRoot,
            CloseAfterOpen = closeAfterOpen,
            CloseAfterOpenDelaySeconds = closeAfterOpenDelaySeconds,
        };

        var configDir = Path.Combine(Root, "config", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configDir);
        var configService = new ConfigService(configDir);
        configService.Save(config);

        return new FidoServices
        {
            ConfigService = configService,
            Launcher = launcher,
            Dialogs = dialogs,
            Git = git ?? new GitService(),
        };
    }

    // --- git plumbing -------------------------------------------------------------------

    /// <summary>Runs git synchronously (on the test thread, never the UI thread) and throws on failure.</summary>
    public static void Git(string workingDir, params string[] args)
    {
        var result = ProcessRunner.RunAsync("git", args, workingDir).GetAwaiter().GetResult();
        if (!result.Success)
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} (in {workingDir}) failed: {result.Message}");
    }

    private static void WriteSolution(string dir, string solutionName)
    {
        File.WriteAllText(
            Path.Combine(dir, solutionName + ".sln"),
            $"Microsoft Visual Studio Solution File, Format Version 12.00{Environment.NewLine}# {solutionName}{Environment.NewLine}");

        var projDir = Path.Combine(dir, "src", solutionName);
        Directory.CreateDirectory(projDir);
        File.WriteAllText(
            Path.Combine(projDir, solutionName + ".csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-');
        return s.Replace('/', '-');
    }

    public void Dispose()
    {
        try { ForceDelete(Root); }
        catch { /* temp dir, best effort — git pack files can linger briefly */ }
    }

    private static void ForceDelete(string dir)
    {
        if (!Directory.Exists(dir)) return;

        // git marks pack files read-only; clear attributes so the recursive delete succeeds on Windows.
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); }
            catch { /* ignore */ }
        }
        Directory.Delete(dir, recursive: true);
    }
}
