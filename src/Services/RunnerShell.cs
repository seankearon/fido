using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Fido.Services;

/// <summary>The shell the Console tab hosts: the program and the arguments it starts with.</summary>
/// <param name="Executable">The shell binary — resolved on PATH by the PTY layer.</param>
/// <param name="Args">Arguments handed to it, already composed for the optional run command.</param>
public sealed record RunnerShellSpec(string Executable, string[] Args);

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
    /// <summary>
    /// The shell to host, and how it starts. <paramref name="command"/> — a pick from a run menu — runs first
    /// and the shell then stays interactive, so a failed script leaves its output on screen with a live prompt
    /// underneath rather than scrolling away with the exit code.
    /// </summary>
    public static RunnerShellSpec For(string? command, Func<string, string?>? onPath = null)
    {
        var run = string.IsNullOrWhiteSpace(command) ? null : command.Trim();
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Windows(run, onPath ?? Which)
            : Unix(run);
    }

    /// <summary>
    /// Windows: PowerShell 7 when it's installed, else the in-box Windows PowerShell. <c>-NoExit</c> is what
    /// keeps the prompt after the command finishes; <c>-Command</c> (not <c>-File</c>) because the menu's
    /// entries are command lines, not necessarily script paths — <c>aspire start</c> is a native command and
    /// a script may carry arguments.
    /// </summary>
    private static RunnerShellSpec Windows(string? command, Func<string, string?> onPath)
    {
        var shell = onPath("pwsh") is not null ? "pwsh" : "powershell";
        return command is null
            ? new RunnerShellSpec(shell, ["-NoLogo"])
            : new RunnerShellSpec(shell, ["-NoLogo", "-NoExit", "-Command", WindowsCommand(command)]);
    }

    /// <summary>
    /// The command line PowerShell is handed, with a script in the tree root made explicitly relative.
    ///
    /// PowerShell does not look in the current directory for anything it is asked to run — the deliberate
    /// defence against a <c>ls.ps1</c> dropped in a folder shadowing the real command. So a bare
    /// <c>build.ps1</c> comes back "not recognized as the name of a cmdlet, function, script file, or
    /// operable program" even standing in the very folder that holds it, and the path has to say so:
    /// <c>./build.ps1</c>. (The launch path into a user's own terminal sidesteps this by using
    /// <c>-File</c>, which does resolve relative to the working directory — but <c>-File</c> can only run
    /// a script, and this menu also carries native commands.)
    ///
    /// Only a bare name is touched, and only one Fido recognises as a script: a path already says where
    /// it is, and <c>aspire start</c> must not be turned into a relative path that doesn't exist. The call
    /// operator leads, because a quoted path on its own is just a string expression to PowerShell.
    /// </summary>
    internal static string WindowsCommand(string command)
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

    /// <summary>
    /// Unix: the user's own login shell from <c>SHELL</c> — zsh on a stock mac, whatever they've chosen on
    /// Linux — falling back to bash, then sh. The run command is appended with <c>exec &lt;shell&gt; -i</c> so
    /// the window survives the script and the user keeps a real interactive shell; <c>-i</c> is what makes it
    /// source their rc file, so the prompt and aliases are the ones they know.
    /// </summary>
    private static RunnerShellSpec Unix(string? command)
    {
        var shell = Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } s && File.Exists(s)
            ? s
            : File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";

        if (command is null) return new RunnerShellSpec(shell, ["-i"]);

        // The same script conventions the launch path uses, so a .ps1 or ./script.sh behaves identically
        // whether it runs here or in the user's own terminal.
        var line = EditorLauncher.UnixCommand(command);
        return new RunnerShellSpec(shell, ["-c", $"{line}; exec {EditorLauncher.ShellQuote(shell)} -i"]);
    }

    /// <summary>Whether a bare command name resolves on <c>PATH</c>, using the platform's executable extensions.</summary>
    private static string? Which(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var exts = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? new[] { ".exe", ".cmd", ".bat" } : [""];
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
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
