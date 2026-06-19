using System;
using System.Diagnostics;
using System.IO;
using Fido.Models;

namespace Fido.Services;

/// <summary>
/// Locates a JetBrains Rider install and launches it (detached) against a target path.
/// Platform-specific resolution is kept isolated so macOS can be extended cleanly.
/// </summary>
public sealed class RiderLauncher : IRiderLauncher
{
    /// <summary>Returns a Rider executable/app-bundle path, or null if none is found.</summary>
    public string? Locate(AppConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.RiderPath) && PathExists(config.RiderPath))
            return config.RiderPath;

        if (FindOnPath() is { } onPath)
            return onPath;

        if (OperatingSystem.IsWindows()) return LocateWindows();
        if (OperatingSystem.IsMacOS()) return LocateMac();
        return null;
    }

    /// <summary>Starts Rider on <paramref name="targetPath"/> without waiting for it.</summary>
    public void Launch(string riderExecutable, string targetPath)
    {
        if (OperatingSystem.IsMacOS() && riderExecutable.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            Start("open", ["-na", riderExecutable, "--args", targetPath]);
            return;
        }

        var ext = Path.GetExtension(riderExecutable).ToLowerInvariant();
        if (ext is ".cmd" or ".bat")
            Start("cmd.exe", ["/c", riderExecutable, targetPath]);   // run the shim via the batch interpreter
        else
            Start(riderExecutable, [targetPath]);                    // plain exe / unix binary
    }

    private static void Start(string fileName, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        _ = Process.Start(psi);   // detached — we intentionally don't hold or await the handle
    }

    // --- Windows ------------------------------------------------------------------------

    private static string? LocateWindows()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var direct = Path.Combine(localAppData, "Programs", "Rider", "bin", "rider64.exe");
        if (File.Exists(direct)) return direct;

        var toolboxApps = Path.Combine(localAppData, "JetBrains", "Toolbox", "apps");
        if (NewestFile(toolboxApps, "rider64.exe") is { } toolboxExe)
            return toolboxExe;

        var shim = Path.Combine(localAppData, "JetBrains", "Toolbox", "scripts", "rider.cmd");
        if (File.Exists(shim)) return shim;

        foreach (var programFiles in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            if (string.IsNullOrEmpty(programFiles)) continue;
            var jetbrains = Path.Combine(programFiles, "JetBrains");
            foreach (var dir in SafeEnumerateDirectories(jetbrains, "JetBrains Rider*"))
            {
                var exe = Path.Combine(dir, "bin", "rider64.exe");
                if (File.Exists(exe)) return exe;
            }
        }
        return null;
    }

    // --- macOS --------------------------------------------------------------------------

    private static string? LocateMac()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var app in new[] { "/Applications/Rider.app", Path.Combine(home, "Applications", "Rider.app") })
            if (Directory.Exists(app)) return app;

        var toolboxApps = Path.Combine(home, "Library", "Application Support", "JetBrains", "Toolbox", "apps");
        if (NewestAppBundle(toolboxApps) is { } bundle)
            return bundle;

        var shim = Path.Combine(home, "Library", "Application Support", "JetBrains", "Toolbox", "scripts", "rider");
        if (File.Exists(shim)) return shim;

        return null;
    }

    // --- Shared helpers -----------------------------------------------------------------

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static string? FindOnPath()
    {
        var names = OperatingSystem.IsWindows()
            ? new[] { "rider64.exe", "rider.cmd", "rider.exe" }
            : new[] { "rider" };

        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar)) return null;

        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var name in names)
            {
                try
                {
                    var candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { /* malformed PATH entry */ }
            }
        }
        return null;
    }

    private static string? NewestFile(string baseDir, string fileName)
    {
        if (!Directory.Exists(baseDir)) return null;
        try
        {
            return Directory.EnumerateFiles(baseDir, fileName, SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .Select(fi => fi.FullName)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private static string? NewestAppBundle(string baseDir)
    {
        if (!Directory.Exists(baseDir)) return null;
        try
        {
            return Directory.EnumerateDirectories(baseDir, "*.app", SearchOption.AllDirectories)
                .Where(d => Path.GetFileName(d).Contains("Rider", StringComparison.OrdinalIgnoreCase))
                .Select(d => new DirectoryInfo(d))
                .OrderByDescending(di => di.LastWriteTimeUtc)
                .Select(di => di.FullName)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string dir, string pattern)
    {
        if (!Directory.Exists(dir)) return [];
        try { return Directory.EnumerateDirectories(dir, pattern); }
        catch { return []; }
    }
}
