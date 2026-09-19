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
        if (!string.IsNullOrWhiteSpace(editor.Path))
        {
            // An explicit, existing path always wins — for any kind, including Custom.
            if (PathExists(editor.Path)) return editor.Path;

            // A bare command name the user typed into the path field — e.g. "wt", "pwsh",
            // "gnome-terminal" — rather than a full path: resolve it on PATH (and the Windows
            // Store-app aliases) so a name, not just a full path, can be configured for a terminal.
            if (ResolveCommandName(editor.Path) is { } resolved) return resolved;
        }

        return editor.Kind switch
        {
            EditorKind.Rider => LocateRider(),
            EditorKind.WebStorm => LocateWebStorm(),
            EditorKind.VsCode => LocateVsCode(),
            EditorKind.VisualStudio => LocateVisualStudio(),
            EditorKind.Zed => LocateZed(),
            EditorKind.Console => LocateConsole(),
            EditorKind.FileExplorer => LocateFileExplorer(),
            _ => null,   // Custom with no (or a missing) path → not found
        };
    }

    /// <summary>Starts <paramref name="editor"/> on <paramref name="targetPath"/> without waiting for it.</summary>
    public void Launch(Editor editor, string executable, string targetPath, string? consoleCommand = null)
        => Run(BuildLaunchSpec(editor, executable, targetPath, consoleCommand));

    /// <summary>
    /// Resolves how to invoke <paramref name="executable"/> for <paramref name="targetPath"/>: editors take the
    /// target as an argument, while <see cref="EditorKind.Console"/> / <see cref="EditorKind.FileExplorer"/>
    /// open the folder via the platform's terminal / file-manager conventions. A
    /// <paramref name="consoleCommand"/> (a run file or <c>aspire start</c> from the branch's
    /// <c>.fido/cfg.yaml</c>) makes the terminal <em>run</em> that command at the folder instead of just
    /// opening there; it means nothing to the other kinds and is ignored by them. Pure (no process is
    /// started) so the per-platform command construction can be unit-tested.
    /// </summary>
    internal static LaunchSpec BuildLaunchSpec(Editor editor, string executable, string targetPath,
        string? consoleCommand = null, Func<string, string?>? onPath = null) =>
        editor.Kind switch
        {
            EditorKind.Console when !string.IsNullOrWhiteSpace(consoleCommand) =>
                BuildConsoleRunSpec(editor, executable, targetPath, consoleCommand.Trim(), onPath ?? ResolveCommandName),
            EditorKind.Console => BuildConsoleSpec(editor, executable, targetPath),
            EditorKind.FileExplorer => BuildFileExplorerSpec(executable, targetPath),
            _ => BuildEditorSpec(editor, executable, targetPath),
        };

    /// <summary>An editor: the target path is passed as the final argument (after any extra args).</summary>
    private static LaunchSpec BuildEditorSpec(Editor editor, string executable, string targetPath)
    {
        var extra = SplitArguments(editor.Arguments);

        if (OperatingSystem.IsMacOS() && executable.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            return new LaunchSpec("open", ["-na", executable, "--args", .. extra, targetPath]);

        var ext = Path.GetExtension(executable).ToLowerInvariant();
        return ext is ".cmd" or ".bat"
            ? new LaunchSpec("cmd.exe", ["/c", executable, .. extra, targetPath])   // shim via the batch interpreter
            : new LaunchSpec(executable, [.. extra, targetPath]);                   // plain exe / unix binary
    }

    /// <summary>
    /// A terminal opened at <paramref name="folder"/>. Most terminals start in the inherited working directory,
    /// so the folder is set as the process's working directory rather than passed as an argument — except
    /// Windows Terminal (<c>wt</c>), which ignores the inherited directory and needs an explicit <c>-d</c>, and
    /// macOS, where terminals are app bundles launched through <c>open -a</c>.
    /// </summary>
    private static LaunchSpec BuildConsoleSpec(Editor editor, string executable, string folder)
    {
        var extra = SplitArguments(editor.Arguments);

        if (OperatingSystem.IsMacOS())
            return executable.EndsWith(".app", StringComparison.OrdinalIgnoreCase) || !executable.Contains('/')
                // `open -a <app> <folder>` opens the folder in the terminal; extra args go after `--args` (which
                // forwards them to the app), mirroring the editor `.app` arm. Omit `--args` when there are none.
                ? new LaunchSpec("open", extra.Length > 0 ? ["-a", executable, folder, "--args", .. extra] : ["-a", executable, folder])
                : new LaunchSpec(executable, [.. extra], WorkingDirectory: folder);

        // Windows: shell-execute so a console window opens (and the Windows Terminal Store alias resolves).
        // Windows Terminal ignores the inherited directory, so it gets an explicit -d <folder>; cmd /
        // PowerShell (or a custom shell) start in the folder via the working directory.
        if (OperatingSystem.IsWindows())
            return string.Equals(Path.GetFileNameWithoutExtension(executable), "wt", StringComparison.OrdinalIgnoreCase)
                ? new LaunchSpec(executable, [.. extra, "-d", folder], WorkingDirectory: folder, UseShellExecute: true)
                : new LaunchSpec(executable, [.. extra], WorkingDirectory: folder, UseShellExecute: true);

        // Linux: virtually every terminal emulator opens in the inherited working directory.
        return new LaunchSpec(executable, [.. extra], WorkingDirectory: folder);
    }

    /// <summary>
    /// A terminal at <paramref name="folder"/> that <em>runs</em> <paramref name="command"/> and stays open
    /// afterwards, so its output can be read. Each platform has exactly one dependable way to hand a command
    /// to a terminal: Windows starts the shell itself (hosted in a Windows Terminal tab when that's the
    /// configured console), macOS asks the terminal app through AppleScript's <c>do script</c>, and Linux uses
    /// the emulators' <c>-e</c> convention.
    /// </summary>
    private static LaunchSpec BuildConsoleRunSpec(Editor editor, string executable, string folder, string command,
        Func<string, string?> onPath)
    {
        if (OperatingSystem.IsWindows()) return BuildWindowsRunSpec(editor, executable, folder, command, onPath);
        if (OperatingSystem.IsMacOS()) return BuildMacRunSpec(executable, folder, command);
        return BuildLinuxRunSpec(editor, executable, folder, command);
    }

    /// <summary>
    /// Windows: the command runs in a shell window whose working directory is the folder. Windows Terminal
    /// can host that shell, so a <c>wt</c> console keeps its tab; any other console is bypassed in favour of
    /// the shell itself, since an arbitrary terminal has no portable way to be handed a command. The console
    /// row's own arguments therefore only ride along when the configured console <em>is</em> the shell used.
    /// </summary>
    private static LaunchSpec BuildWindowsRunSpec(Editor editor, string executable, string folder, string command,
        Func<string, string?> onPath)
    {
        var extra = SplitArguments(editor.Arguments);
        var shell = WindowsShellFor(executable, command, onPath);

        if (IsProgram(executable, "wt"))
            return new LaunchSpec(executable, [.. extra, "-d", folder, .. shell], folder, UseShellExecute: true);

        string[] leading = string.Equals(shell[0], executable, StringComparison.OrdinalIgnoreCase) ? extra : [];
        return new LaunchSpec(shell[0], [.. leading, .. shell[1..]], folder, UseShellExecute: true);
    }

    /// <summary>
    /// How to run <paramref name="command"/> in a Windows shell, keeping the window open when it finishes,
    /// as the program followed by its arguments. A <c>.ps1</c> goes to PowerShell as <c>-NoExit -File</c>
    /// (PowerShell won't run a bare script name from the current directory, and <c>cmd</c> would hand a
    /// <c>.ps1</c> to whatever the extension is associated with); a PowerShell takes anything else as
    /// <c>-NoExit -Command</c>, and <c>cmd</c> as <c>/k</c>. Which shell that is, is
    /// <see cref="WindowsShell"/>'s decision. Internal so the choice can be tested directly on any OS —
    /// it's the part that decides whether a repo's command can resolve at all.
    /// </summary>
    internal static string[] WindowsShellFor(string executable, string command, Func<string, string?> onPath)
    {
        var tokens = SplitCommand(command);
        var needsPowerShell = tokens.Length > 0 && tokens[0].EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);
        var shell = WindowsShell(executable, needsPowerShell, onPath);

        if (needsPowerShell) return [shell, "-NoExit", "-File", .. tokens];

        return IsProgram(shell, "cmd")
            ? [shell, "/k", .. tokens]
            : [shell, "-NoExit", "-Command", command];
    }

    /// <summary>
    /// Which Windows shell actually runs a console command.
    /// <para>
    /// A console that <em>is</em> a shell — <c>cmd</c>, <c>powershell</c>, <c>pwsh</c> — was chosen
    /// deliberately, so it's used as configured. Windows Terminal and third-party emulators are different:
    /// they only <em>host</em> a shell and say nothing about which one, so the best on the machine is picked
    /// rather than the oldest. <c>pwsh</c> leads, because a machine with PowerShell 7 installed is one whose
    /// <c>PATH</c>, profile and execution policy are set up there — and <c>-Command</c> loads that profile
    /// before running, so a tool the profile puts on <c>PATH</c> resolves. Windows PowerShell is the
    /// fallback, being present on every Windows machine.
    /// </para>
    /// <para>
    /// <c>cmd</c> is never chosen for a host console, only honoured when it's what the user configured: it
    /// resolves the least of the three, finding neither what a PowerShell profile adds to <c>PATH</c> nor a
    /// function or alias — which is how <c>aspire start</c> ends up as "not recognized" in a terminal where
    /// it plainly works. A <c>.ps1</c> overrides even an explicit <c>cmd</c>, which can't run one at all.
    /// </para>
    /// </summary>
    private static string WindowsShell(string executable, bool needsPowerShell, Func<string, string?> onPath)
    {
        if (IsProgram(executable, "pwsh") || IsProgram(executable, "powershell")) return executable;
        if (IsProgram(executable, "cmd") && !needsPowerShell) return executable;
        return onPath("pwsh") ?? onPath("powershell") ?? "powershell.exe";
    }

    /// <summary>
    /// macOS: <c>osascript</c>'s <c>do script</c> is the only reliable way to run a command <em>in</em> a mac
    /// terminal window, and both Terminal and iTerm answer it. The app name comes from the configured console
    /// (its <c>.app</c> bundle, or the bare name auto-detection returns); the window then keeps the shell that
    /// ran the command, so the output stays on screen.
    /// </summary>
    private static LaunchSpec BuildMacRunSpec(string executable, string folder, string command)
    {
        var app = MacTerminalApp(executable);
        var shellCommand = $"cd {ShellQuote(folder)} && {UnixCommand(command)}";
        return new LaunchSpec("osascript",
        [
            "-e", $"tell application \"{app}\" to do script \"{AppleScriptEscape(shellCommand)}\"",
            "-e", $"tell application \"{app}\" to activate",
        ]);
    }

    /// <summary>The AppleScript-addressable terminal app: a configured <c>.app</c> bundle's name (iTerm,
    /// Terminal…), or Terminal for anything that isn't one — a bare unix shell can't host a window.</summary>
    private static string MacTerminalApp(string executable)
    {
        if (executable.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileNameWithoutExtension(executable);
        return executable.Contains('/') ? "Terminal" : executable;
    }

    /// <summary>
    /// Linux: the command is run by <c>bash -c</c> with a trailing <c>exec bash</c>, which leaves the window on
    /// an interactive shell once it finishes (the equivalent of <c>/k</c> / <c>-NoExit</c>). The emulator is
    /// handed that program its own way — <c>gnome-terminal</c> after <c>--</c>, <c>kitty</c> directly, and the
    /// rest via the conventional <c>-e</c>.
    /// </summary>
    private static LaunchSpec BuildLinuxRunSpec(Editor editor, string executable, string folder, string command)
    {
        var extra = SplitArguments(editor.Arguments);
        string[] shell = ["bash", "-c", $"{UnixCommand(command)}; exec bash"];

        string[] args = Path.GetFileNameWithoutExtension(executable) switch
        {
            "gnome-terminal" => [.. extra, $"--working-directory={folder}", "--", .. shell],
            "kitty" => [.. extra, .. shell],
            _ => [.. extra, "-e", .. shell],
        };
        return new LaunchSpec(executable, args, folder);
    }

    /// <summary>
    /// The shell command a unix terminal is handed: a <c>.sh</c> in the tree root is invoked as <c>./name</c>
    /// (the working directory isn't on <c>PATH</c>), a <c>.ps1</c> is handed to <c>pwsh</c>, and anything else
    /// — <c>aspire start</c>, an npm script — is passed through untouched.
    /// </summary>
    internal static string UnixCommand(string command)
    {
        var tokens = SplitCommand(command);
        if (tokens.Length == 0) return command;

        var arguments = string.Join(' ', tokens[1..].Select(ShellQuote));
        var tail = arguments.Length > 0 ? " " + arguments : "";
        var first = tokens[0];

        if (first.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            return $"pwsh {ShellQuote(first)}{tail}";
        if (first.EndsWith(".sh", StringComparison.OrdinalIgnoreCase) && !first.Contains('/'))
            return $"./{ShellQuote(first)}{tail}";
        return command;
    }

    /// <summary>True when <paramref name="executable"/> is the program called <paramref name="name"/>,
    /// whatever directory it came from and whatever extension it carries.</summary>
    private static bool IsProgram(string executable, string name) =>
        string.Equals(Path.GetFileNameWithoutExtension(executable), name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Splits a command line into its tokens, honouring double quotes so a script name containing spaces
    /// survives as one argument. Deliberately simpler than a shell — no escapes, no globbing — because the
    /// commands here come from a repo's <c>.fido/cfg.yaml</c>, not from a prompt.
    /// </summary>
    internal static string[] SplitCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return [];

        var tokens = new List<string>();
        var token = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in command)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (!quoted && char.IsWhiteSpace(c))
            {
                if (token.Length > 0) { tokens.Add(token.ToString()); token.Clear(); }
                continue;
            }
            token.Append(c);
        }
        if (token.Length > 0) tokens.Add(token.ToString());
        return [.. tokens];
    }

    /// <summary>Single-quotes a token for a unix shell, leaving plain ones (the usual case) alone.</summary>
    internal static string ShellQuote(string token) =>
        token.Length > 0 && token.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '/' or '+' or '=' or ':')
            ? token
            : "'" + token.Replace("'", "'\\''") + "'";

    /// <summary>Escapes a string for an AppleScript literal (backslashes and double quotes).</summary>
    private static string AppleScriptEscape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>
    /// The OS file manager revealing <paramref name="folder"/>: <c>explorer.exe &lt;folder&gt;</c> on Windows,
    /// <c>xdg-open</c> / a file manager on Linux, and Finder via <c>open</c> on macOS (a custom <c>.app</c>
    /// file manager goes through <c>open -a</c>).
    /// </summary>
    private static LaunchSpec BuildFileExplorerSpec(string executable, string folder) =>
        OperatingSystem.IsMacOS() && executable.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            ? new LaunchSpec("open", ["-a", executable, folder])
            : new LaunchSpec(executable, [folder]);

    /// <summary>Splits the user's extra-arguments string on whitespace; null/blank yields nothing.</summary>
    private static string[] SplitArguments(string? arguments) =>
        string.IsNullOrWhiteSpace(arguments)
            ? []
            : arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Starts the planned process detached — we intentionally don't hold or await the handle.</summary>
    private static void Run(LaunchSpec spec)
    {
        var psi = new ProcessStartInfo
        {
            FileName = spec.FileName,
            UseShellExecute = spec.UseShellExecute,
            // A shell-executed console must keep its window; everything else launches quietly.
            CreateNoWindow = !spec.UseShellExecute,
        };
        if (!string.IsNullOrEmpty(spec.WorkingDirectory))
            psi.WorkingDirectory = spec.WorkingDirectory;
        foreach (var arg in spec.Arguments) psi.ArgumentList.Add(arg);
        _ = Process.Start(psi);
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

    // --- Console (terminal) -------------------------------------------------------------

    private static string? LocateConsole()
    {
        if (OperatingSystem.IsWindows())
        {
            // Prefer Windows Terminal, then PowerShell, falling back to cmd (which always exists).
            if (FindOnPath(["wt.exe", "pwsh.exe", "powershell.exe", "cmd.exe"]) is { } onPath)
                return onPath;

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var wt = Path.Combine(localAppData, "Microsoft", "WindowsApps", "wt.exe");
            if (File.Exists(wt)) return wt;

            var comSpec = Environment.GetEnvironmentVariable("ComSpec");
            if (!string.IsNullOrEmpty(comSpec) && File.Exists(comSpec)) return comSpec;

            var cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            return File.Exists(cmd) ? cmd : null;
        }

        if (OperatingSystem.IsMacOS())
        {
            foreach (var app in new[] { "/System/Applications/Utilities/Terminal.app", "/Applications/Utilities/Terminal.app" })
                if (Directory.Exists(app)) return app;
            return "Terminal";   // resolved by name through `open -a` even if the bundle lives elsewhere
        }

        // Linux: honour the Debian terminal alternative, then the common emulators. Both lookups derive from
        // the one list so the PATH probe and the /usr/bin fallback can't drift apart.
        return FindOnPath(LinuxTerminals)
            ?? FirstExisting([.. LinuxTerminals.Select(t => "/usr/bin/" + t)]);
    }

    /// <summary>Linux terminal emulators probed for the Console target, in preference order.</summary>
    private static readonly string[] LinuxTerminals =
        ["x-terminal-emulator", "gnome-terminal", "konsole", "xfce4-terminal", "kitty", "alacritty", "tilix", "xterm"];

    // --- File explorer ------------------------------------------------------------------

    private static string? LocateFileExplorer()
    {
        if (OperatingSystem.IsWindows())
        {
            var windows = Environment.GetEnvironmentVariable("WINDIR")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var explorer = Path.Combine(windows, "explorer.exe");
            return File.Exists(explorer) ? explorer : FindOnPath(["explorer.exe"]);
        }

        if (OperatingSystem.IsMacOS())
            return File.Exists("/usr/bin/open") ? "/usr/bin/open" : "open";

        // Linux: xdg-open honours the user's default file manager; fall back to common ones. Both lookups
        // derive from the one list so the PATH probe and the /usr/bin fallback can't drift apart.
        return FindOnPath(LinuxFileManagers)
            ?? FirstExisting([.. LinuxFileManagers.Select(m => "/usr/bin/" + m)]);
    }

    /// <summary>Linux file managers probed for the File Explorer target; xdg-open (the default) first.</summary>
    private static readonly string[] LinuxFileManagers =
        ["xdg-open", "nautilus", "dolphin", "thunar", "nemo", "pcmanfm"];

    // --- Shared helpers -----------------------------------------------------------------

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>The first of <paramref name="paths"/> that exists as a file, or null.</summary>
    private static string? FirstExisting(params string[] paths)
    {
        foreach (var path in paths)
            if (File.Exists(path)) return path;
        return null;
    }

    private static IEnumerable<string> ProgramFilesDirs()
    {
        foreach (var dir in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
            if (!string.IsNullOrEmpty(dir)) yield return dir;
    }

    /// <summary>
    /// Finds the first of <paramref name="names"/> present in any <c>PATH</c> directory. Names are tried in the
    /// caller's preference order <em>across all directories</em> before the next name — so a preferred name in a
    /// later directory still wins over a less-preferred name in an earlier one (e.g. Windows Terminal in
    /// <c>%LOCALAPPDATA%\Microsoft\WindowsApps</c> beats <c>cmd.exe</c> in <c>System32</c>, which sits earlier on PATH).
    /// </summary>
    internal static string? FindOnPath(string[] names)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar)) return null;

        var dirs = pathVar.Split(Path.PathSeparator);
        foreach (var name in names)
        {
            foreach (var dir in dirs)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
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

    /// <summary>
    /// Resolves a bare command name (no directory separator) — e.g. "wt", "pwsh", "gnome-terminal" — to a full
    /// executable path via <c>PATH</c>, plus the Windows Store-app alias folder. Returns null when the value
    /// looks like a (missing) full path, or can't be found, so the caller falls back to kind-based auto-detection.
    /// </summary>
    private static string? ResolveCommandName(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Contains('/') || name.Contains('\\')) return null;   // a path, not a name

        if (!OperatingSystem.IsWindows())
            return FindOnPath([name]);

        // On Windows, a bare name usually omits the extension; try the common executable extensions.
        string[] candidates = Path.HasExtension(name)
            ? [name]
            : [name + ".exe", name + ".cmd", name + ".bat", name];
        if (FindOnPath(candidates) is { } onPath) return onPath;

        // Windows Terminal and other Store apps live here as execution aliases (which File.Exists sees).
        var windowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
        return FirstExisting([.. candidates.Select(c => Path.Combine(windowsApps, c))]);
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

/// <summary>
/// A planned process launch: the program to run, its arguments, an optional working directory, and whether
/// to shell-execute (used to give a terminal its own console window). Produced by
/// <see cref="EditorLauncher.BuildLaunchSpec"/> so the per-platform command construction is unit-testable.
/// </summary>
internal readonly record struct LaunchSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    bool UseShellExecute = false);
