using System;
using System.IO;
using Fido.Models;

namespace Fido.Services;

/// <summary>The shell the Console tab hosts: the program and the arguments it starts with.</summary>
/// <param name="Executable">The shell binary: a full path, or a bare name the PTY layer resolves on PATH.</param>
/// <param name="Args">Arguments handed to it, already composed for the optional run command.</param>
/// <param name="NotFound">The shell the Console tool's path asked for, when Fido couldn't find it and started
/// the default instead — so the window can say why the tab isn't the shell that was set. Null otherwise.</param>
public sealed record RunnerShellSpec(string Executable, string[] Args, string? NotFound = null);

/// <summary>
/// Picks the shell for Fido's own Console tab.
///
/// This is deliberately <em>not</em> <see cref="EditorLauncher"/>'s job. That one hands a folder or a command
/// to a terminal <em>emulator</em> — Windows Terminal, iTerm, gnome-terminal — and has to work through each
/// one's conventions (<c>wt -d</c>, AppleScript <c>do script</c>, <c>gnome-terminal --</c>). Here Fido owns the
/// pseudo-terminal itself, so there is no emulator in the way and no folder argument to negotiate: the PTY is
/// opened at the working directory and the shell simply inherits it. All that's left to choose is which shell,
/// and what it runs before handing over.
///
/// The command conventions are shared with the launch path on purpose — a <c>.ps1</c> goes to <c>pwsh</c>, a
/// root <c>.sh</c> is invoked as <c>./name</c> — so a command behaves the same whichever console it lands in.
/// </summary>
public static class RunnerShell
{
    /// <summary>The shells the tab can drive on Windows. <c>cmd</c> is not one: the PTY layer quotes every
    /// argument, and <c>cmd</c> does not reliably read a quoted <c>/k</c> as a switch.</summary>
    private static readonly string[] WindowsShells = ["pwsh", "powershell"];

    /// <summary>The shells the tab can drive on macOS and Linux: PowerShell, and the ones that take
    /// <c>-c</c> and <c>-i</c> the way <see cref="Posix"/> hands them over.</summary>
    private static readonly string[] UnixShells = ["pwsh", "bash", "zsh", "fish", "sh", "dash", "ksh"];

    /// <summary>
    /// Where a shell installed outside the system folders lives, searched after <c>PATH</c>. A Fido started
    /// from the Dock or Finder doesn't get the <c>PATH</c> a terminal session builds (launchd gives a GUI app
    /// little more than <c>/usr/bin:/bin</c>), so Homebrew's shells and the PowerShell package's <c>pwsh</c>
    /// link would otherwise go unfound.
    /// </summary>
    private static readonly string[] UnixShellDirs = ["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin", "/snap/bin"];

    /// <summary>
    /// The shell to host, and how it starts. <paramref name="command"/> — a pick from a run menu — runs first
    /// and the shell then stays interactive, so a failed script leaves its output on screen with a live prompt
    /// underneath rather than scrolling away with the exit code.
    ///
    /// <paramref name="console"/> is the Console tool. When its path names a shell this platform's tab can
    /// drive (<c>pwsh</c>, <c>zsh</c>, <c>/opt/homebrew/bin/fish</c>…) that shell is the one started, with the
    /// row's arguments ahead of the ones Fido adds. When the path names a terminal app instead (Terminal,
    /// iTerm, Windows Terminal), or is blank, it says nothing about the tab, which keeps its default. A shell
    /// that is named but can't be found falls back to the default too, and says so through
    /// <see cref="RunnerShellSpec.NotFound"/>.
    /// </summary>
    public static RunnerShellSpec For(string? command, Editor? console = null, Func<string, string?>? onPath = null)
    {
        var find = onPath ?? Which;
        var named = ShellNamedBy(console?.Path);
        var chosen = named is null ? null : Locate(named, find);
        if (chosen is not null) return Start(chosen, command, console?.Arguments);

        var fallback = Start(OperatingSystem.IsWindows() ? DefaultWindowsShell(find) : DefaultUnixShell(), command);
        return named is null ? fallback : fallback with { NotFound = named };
    }

    /// <summary>
    /// The shell the Console tool's path names, found where the tab would look for it — <c>PATH</c>, then on
    /// macOS and Linux the folders a Dock-started Fido can't see. Null when the path names no shell this
    /// platform can drive, or names one that can't be found.
    /// </summary>
    internal static string? Configured(Editor? console, Func<string, string?>? onPath = null) =>
        ShellNamedBy(console?.Path) is { } named ? Locate(named, onPath ?? Which) : null;

    /// <summary>Whether <paramref name="program"/> is a shell this platform's tab can drive, rather than a
    /// terminal app or anything else.</summary>
    internal static bool IsShell(string program) => Array.Exists(PlatformShells, s => IsProgram(program, s));

    /// <summary>
    /// How <paramref name="shell"/> starts: <paramref name="command"/> first when there is one, then an
    /// interactive prompt. PowerShell gets its own flags and every other shell the <c>-c</c> / <c>-i</c> pair
    /// (see <see cref="Posix"/>). <paramref name="arguments"/>, the Console row's own, go in front of them,
    /// because some flags only count there: PowerShell takes <c>-Login</c> nowhere else. Shared with the
    /// Console button, so a shell starts the same way in either console.
    /// </summary>
    internal static RunnerShellSpec Start(string shell, string? command, string? arguments = null)
    {
        var run = string.IsNullOrWhiteSpace(command) ? null : command.Trim();
        var spec = IsProgram(shell, "pwsh") || IsProgram(shell, "powershell")
            ? PowerShell(shell, run)
            : Posix(shell, run);

        var extra = EditorLauncher.SplitArguments(arguments);
        return extra.Length == 0 ? spec : spec with { Args = [.. extra, .. spec.Args] };
    }

    private static string[] PlatformShells => OperatingSystem.IsWindows() ? WindowsShells : UnixShells;

    /// <summary>The configured value, trimmed, when its program is a shell this platform can drive; null for
    /// a blank value or one that names anything else.</summary>
    private static string? ShellNamedBy(string? preferred)
    {
        if (string.IsNullOrWhiteSpace(preferred)) return null;
        var value = preferred.Trim();
        return IsShell(value) ? value : null;
    }

    /// <summary>A named shell as a program the PTY can start: a path is taken as it stands if it exists, and a
    /// bare name is looked up, because the PTY would otherwise search only Fido's own <c>PATH</c>.</summary>
    private static string? Locate(string named, Func<string, string?> find)
    {
        if (named.Contains('/') || named.Contains('\\')) return File.Exists(named) ? named : null;
        return find(named);
    }

    /// <summary>Windows, when no shell is set: PowerShell 7 when it's installed, else the in-box Windows
    /// PowerShell.</summary>
    private static string DefaultWindowsShell(Func<string, string?> onPath) =>
        onPath("pwsh") is not null ? "pwsh" : "powershell";

    /// <summary>
    /// PowerShell on any platform: <c>-NoLogo</c>, and a command run with <c>-NoExit</c>, which is what keeps
    /// the prompt after it finishes. <c>-Command</c> (not <c>-File</c>) because the menu's entries are
    /// command lines, not necessarily script paths — <c>aspire start</c> is a native command and a script may
    /// carry arguments.
    /// </summary>
    private static RunnerShellSpec PowerShell(string shell, string? command) =>
        command is null
            ? new RunnerShellSpec(shell, ["-NoLogo"])
            : new RunnerShellSpec(shell, ["-NoLogo", "-NoExit", "-Command", PowerShellCommand(command)]);

    /// <summary>
    /// The command line PowerShell is handed, with a script in the tree root made explicitly relative.
    ///
    /// PowerShell does not look in the current directory for anything it is asked to run — the deliberate
    /// defence against a <c>ls.ps1</c> dropped in a folder shadowing the real command. So a bare
    /// <c>build.ps1</c> comes back "not recognized as the name of a cmdlet, function, script file, or
    /// operable program" even standing in the very folder that holds it, and the path has to say so:
    /// <c>./build.ps1</c>. That holds for <c>pwsh</c> on macOS and Linux as much as on Windows. (The launch
    /// path into a user's own terminal sidesteps this by using <c>-File</c>, which does resolve relative to
    /// the working directory — but <c>-File</c> can only run a script, and this menu also carries native
    /// commands.)
    ///
    /// Only a bare name is touched, and only one Fido recognises as a script: a path already says where
    /// it is, and <c>aspire start</c> must not be turned into a relative path that doesn't exist. The call
    /// operator leads, because a quoted path on its own is just a string expression to PowerShell.
    /// </summary>
    internal static string PowerShellCommand(string command)
    {
        var tokens = EditorLauncher.SplitCommand(command);
        if (tokens.Length == 0) return command;

        var first = tokens[0];
        if (first.Contains('/') || first.Contains('\\')) return command;
        if (!RepoConfigService.IsScript(first)) return command;

        var arguments = string.Join(' ', tokens[1..].Select(PowerShellQuote));
        var tail = arguments.Length > 0 ? " " + arguments : "";
        return $"& {PowerShellQuote("./" + first)}{tail}";
    }

    /// <summary>Single-quotes a token for PowerShell, leaving plain ones (the usual case) alone. PowerShell
    /// escapes a single quote inside a single-quoted string by doubling it, not with a backslash.</summary>
    private static string PowerShellQuote(string token) =>
        token.Length > 0 && token.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '/' or '+' or '=' or ':')
            ? token
            : "'" + token.Replace("'", "''") + "'";

    /// <summary>macOS and Linux, when no shell is set: the user's own login shell from <c>SHELL</c> — zsh on a
    /// stock mac, whatever they've chosen on Linux — falling back to bash, then sh.</summary>
    private static string DefaultUnixShell() =>
        Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } s && File.Exists(s)
            ? s
            : File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";

    /// <summary>
    /// A POSIX-style shell (fish too): the run command is appended with <c>exec &lt;shell&gt; -i</c> so the window
    /// survives the script and the user keeps a real interactive shell; <c>-i</c> is what makes it source their
    /// rc file, so the prompt and aliases are the ones they know.
    /// </summary>
    private static RunnerShellSpec Posix(string shell, string? command)
    {
        if (command is null) return new RunnerShellSpec(shell, ["-i"]);

        // The same script conventions the launch path uses, so a .ps1 or ./script.sh behaves identically
        // whether it runs here or in the user's own terminal.
        var line = EditorLauncher.UnixCommand(command);
        return new RunnerShellSpec(shell, ["-c", $"{line}; exec {EditorLauncher.ShellQuote(shell)} -i"]);
    }

    /// <summary>True when <paramref name="program"/> is the one called <paramref name="name"/>, whatever folder
    /// it is in and whatever extension it carries.</summary>
    private static bool IsProgram(string program, string name) =>
        string.Equals(Path.GetFileNameWithoutExtension(program), name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where a bare command name resolves: on <c>PATH</c>, with the platform's executable extensions, then on
    /// macOS and Linux in <see cref="UnixShellDirs"/>.
    /// </summary>
    private static string? Which(string name)
    {
        var windows = OperatingSystem.IsWindows();
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var dirs = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (!windows) dirs = [.. dirs, .. UnixShellDirs];

        var exts = windows && !Path.HasExtension(name) ? new[] { ".exe", ".cmd", ".bat" } : [""];
        foreach (var dir in dirs)
        {
            foreach (var ext in exts)
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim('"'), name + ext);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { /* a malformed PATH entry — skip it */ }
            }
        }
        return null;
    }
}
