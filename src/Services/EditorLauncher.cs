using System;
using System.Diagnostics;
using System.IO;
using Fido.Models;

namespace Fido.Services;

/// <summary>
/// Locates configured editors/IDEs and launches them (detached) against a target path. Each known
/// <see cref="EditorKind"/> carries its own probing (explicit path → PATH → common install locations);
/// platform-specific resolution is kept isolated so macOS can be extended cleanly.
/// </summary>
public sealed class EditorLauncher : IEditorLauncher
{
    /// <summary>Returns the editor's executable/app-bundle path, or null if none is found.</summary>
    public string? Locate(Editor editor)
    {
        // An explicit, existing path always wins — for any kind, including Custom.
        if (!string.IsNullOrWhiteSpace(editor.Path) && PathExists(editor.Path))
            return editor.Path;

        return editor.Kind switch
        {
            EditorKind.Rider => LocateRider(),
            EditorKind.WebStorm => LocateWebStorm(),
            EditorKind.VsCode => LocateVsCode(),
            EditorKind.VisualStudio => LocateVisualStudio(),
            EditorKind.Zed => LocateZed(),
            _ => null,   // Custom with no (or a missing) path → not found
        };
    }

    /// <summary>Starts <paramref name="editor"/> on <paramref name="targetPath"/> without waiting for it.</summary>
    public void Launch(Editor editor, string executable, string targetPath)
    {
        var extra = SplitArguments(editor.Arguments);

        if (OperatingSystem.IsMacOS() && executable.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            Start("open", ["-na", executable, "--args", .. extra, targetPath]);
            return;
        }

        var ext = Path.GetExtension(executable).ToLowerInvariant();
        if (ext is ".cmd" or ".bat")
            Start("cmd.exe", ["/c", executable, .. extra, targetPath]);   // run the shim via the batch interpreter
        else
            Start(executable, [.. extra, targetPath]);                    // plain exe / unix binary
    }

    /// <summary>Splits the user's extra-arguments string on whitespace; null/blank yields nothing.</summary>
    private static string[] SplitArguments(string? arguments) =>
        string.IsNullOrWhiteSpace(arguments)
            ? []
            : arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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

    // --- Rider --------------------------------------------------------------------------

    private static string? LocateRider()
    {
        if (FindOnPath(OperatingSystem.IsWindows()
                ? ["rider64.exe", "rider.cmd", "rider.exe"]
                : ["rider"]) is { } onPath)
            return onPath;

        if (OperatingSystem.IsWindows()) return LocateRiderWindows();
        if (OperatingSystem.IsMacOS()) return LocateRiderMac();
        return null;
    }

    private static string? LocateRiderWindows()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var direct = Path.Combine(localAppData, "Programs", "Rider", "bin", "rider64.exe");
        if (File.Exists(direct)) return direct;

        var toolboxApps = Path.Combine(localAppData, "JetBrains", "Toolbox", "apps");
        if (NewestFile(toolboxApps, "rider64.exe") is { } toolboxExe)
            return toolboxExe;

        var shim = Path.Combine(localAppData, "JetBrains", "Toolbox", "scripts", "rider.cmd");
        if (File.Exists(shim)) return shim;

        foreach (var programFiles in ProgramFilesDirs())
        {
            var jetbrains = Path.Combine(programFiles, "JetBrains");
            foreach (var dir in SafeEnumerateDirectories(jetbrains, "JetBrains Rider*"))
            {
                var exe = Path.Combine(dir, "bin", "rider64.exe");
                if (File.Exists(exe)) return exe;
            }
        }
        return null;
    }

    private static string? LocateRiderMac()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var app in new[] { "/Applications/Rider.app", Path.Combine(home, "Applications", "Rider.app") })
            if (Directory.Exists(app)) return app;

        var toolboxApps = Path.Combine(home, "Library", "Application Support", "JetBrains", "Toolbox", "apps");
        if (NewestAppBundle(toolboxApps, "Rider") is { } bundle)
            return bundle;

        var shim = Path.Combine(home, "Library", "Application Support", "JetBrains", "Toolbox", "scripts", "rider");
        if (File.Exists(shim)) return shim;

        return null;
    }

    // --- WebStorm -----------------------------------------------------------------------

    private static string? LocateWebStorm()
    {
        if (FindOnPath(OperatingSystem.IsWindows()
                ? ["webstorm64.exe", "webstorm.cmd", "webstorm.exe"]
                : ["webstorm"]) is { } onPath)
            return onPath;

        if (OperatingSystem.IsWindows()) return LocateWebStormWindows();
        if (OperatingSystem.IsMacOS()) return LocateWebStormMac();
        return null;
    }

    private static string? LocateWebStormWindows()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var direct = Path.Combine(localAppData, "Programs", "WebStorm", "bin", "webstorm64.exe");
        if (File.Exists(direct)) return direct;

        var toolboxApps = Path.Combine(localAppData, "JetBrains", "Toolbox", "apps");
        if (NewestFile(toolboxApps, "webstorm64.exe") is { } toolboxExe)
            return toolboxExe;

        var shim = Path.Combine(localAppData, "JetBrains", "Toolbox", "scripts", "webstorm.cmd");
        if (File.Exists(shim)) return shim;

        foreach (var programFiles in ProgramFilesDirs())
        {
            var jetbrains = Path.Combine(programFiles, "JetBrains");
            foreach (var dir in SafeEnumerateDirectories(jetbrains, "WebStorm*"))
            {
                var exe = Path.Combine(dir, "bin", "webstorm64.exe");
                if (File.Exists(exe)) return exe;
            }
        }
        return null;
    }

    private static string? LocateWebStormMac()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var app in new[] { "/Applications/WebStorm.app", Path.Combine(home, "Applications", "WebStorm.app") })
            if (Directory.Exists(app)) return app;

        var toolboxApps = Path.Combine(home, "Library", "Application Support", "JetBrains", "Toolbox", "apps");
        if (NewestAppBundle(toolboxApps, "WebStorm") is { } bundle)
            return bundle;

        var shim = Path.Combine(home, "Library", "Application Support", "JetBrains", "Toolbox", "scripts", "webstorm");
        if (File.Exists(shim)) return shim;

        return null;
    }

    // --- Visual Studio Code -------------------------------------------------------------

    private static string? LocateVsCode()
    {
        if (FindOnPath(OperatingSystem.IsWindows()
                ? ["code.cmd", "code.exe", "code"]
                : ["code"]) is { } onPath)
            return onPath;

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userShim = Path.Combine(localAppData, "Programs", "Microsoft VS Code", "bin", "code.cmd");
            if (File.Exists(userShim)) return userShim;

            foreach (var programFiles in ProgramFilesDirs())
            {
                var shim = Path.Combine(programFiles, "Microsoft VS Code", "bin", "code.cmd");
                if (File.Exists(shim)) return shim;
            }
            return null;
        }

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var app in new[]
                     {
                         "/Applications/Visual Studio Code.app",
                         Path.Combine(home, "Applications", "Visual Studio Code.app"),
                     })
                if (Directory.Exists(app)) return app;
        }
        return null;
    }

    // --- Visual Studio (Windows only) ---------------------------------------------------

    private static string? LocateVisualStudio()
    {
        if (!OperatingSystem.IsWindows()) return null;

        if (FindOnPath(["devenv.exe", "devenv.com"]) is { } onPath)
            return onPath;

        foreach (var programFiles in ProgramFilesDirs())
        {
            var vsRoot = Path.Combine(programFiles, "Microsoft Visual Studio");
            foreach (var yearDir in SafeEnumerateDirectories(vsRoot, "*"))            // 2022, 2019, …
            foreach (var editionDir in SafeEnumerateDirectories(yearDir, "*"))        // Community, Professional, Enterprise, Preview
            {
                var exe = Path.Combine(editionDir, "Common7", "IDE", "devenv.exe");
                if (File.Exists(exe)) return exe;
            }
        }
        return null;
    }

    // --- Zed ----------------------------------------------------------------------------

    private static string? LocateZed()
    {
        if (FindOnPath(OperatingSystem.IsWindows() ? ["zed.exe", "zed"] : ["zed"]) is { } onPath)
            return onPath;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsMacOS())
        {
            foreach (var app in new[] { "/Applications/Zed.app", Path.Combine(home, "Applications", "Zed.app") })
                if (Directory.Exists(app)) return app;
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userExe = Path.Combine(localAppData, "Programs", "Zed", "Zed.exe");
            if (File.Exists(userExe)) return userExe;
            return null;
        }

        // Linux: a few conventional spots if it isn't already on PATH.
        foreach (var candidate in new[]
                 {
                     Path.Combine(home, ".local", "bin", "zed"),
                     "/usr/local/bin/zed",
                     "/usr/bin/zed",
                 })
            if (File.Exists(candidate)) return candidate;
        return null;
    }

    // --- Shared helpers -----------------------------------------------------------------

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static IEnumerable<string> ProgramFilesDirs()
    {
        foreach (var dir in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
            if (!string.IsNullOrEmpty(dir)) yield return dir;
    }

    private static string? FindOnPath(string[] names)
    {
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

    private static string? NewestAppBundle(string baseDir, string nameContains)
    {
        if (!Directory.Exists(baseDir)) return null;
        try
        {
            return Directory.EnumerateDirectories(baseDir, "*.app", SearchOption.AllDirectories)
                .Where(d => Path.GetFileName(d).Contains(nameContains, StringComparison.OrdinalIgnoreCase))
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
