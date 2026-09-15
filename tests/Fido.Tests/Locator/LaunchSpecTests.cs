using Fido.Models;
using Fido.Services;

namespace Fido.Tests.Locator;

/// <summary>
/// The per-platform command construction in <see cref="EditorLauncher.BuildLaunchSpec"/>. Pure (no process is
/// started), so the launch plan for editors, the console and the file explorer can be asserted directly. The
/// CI matrix (Windows + Linux) exercises each platform branch; the macOS branch is left to manual verification.
/// </summary>
public class LaunchSpecTests
{
    private static string Folder => OperatingSystem.IsWindows() ? @"C:\work\repo" : "/work/repo";

    [Test]
    public async Task An_editor_takes_the_target_as_its_final_argument()
    {
        var spec = EditorLauncher.BuildLaunchSpec(
            new Editor { Kind = EditorKind.VsCode, Arguments = "--new-window" }, "/opt/ed/code", Folder);

        await Assert.That(spec.FileName).IsEqualTo("/opt/ed/code");
        await Assert.That(spec.Arguments[0]).IsEqualTo("--new-window");   // extra args first
        await Assert.That(spec.Arguments[^1]).IsEqualTo(Folder);          // then the target
        await Assert.That(spec.UseShellExecute).IsFalse();
        await Assert.That(string.IsNullOrEmpty(spec.WorkingDirectory)).IsTrue();
    }

    [Test]
    public async Task The_console_opens_a_terminal_at_the_folder()
    {
        var editor = new Editor { Kind = EditorKind.Console };

        if (OperatingSystem.IsWindows())
        {
            // cmd / PowerShell: the folder is the working directory, not an argument; shell-execute gives a window.
            var cmd = EditorLauncher.BuildLaunchSpec(editor, @"C:\Windows\System32\cmd.exe", Folder);
            await Assert.That(cmd.WorkingDirectory).IsEqualTo(Folder);
            await Assert.That(cmd.UseShellExecute).IsTrue();
            await Assert.That(cmd.Arguments.Contains(Folder)).IsFalse();

            // Windows Terminal ignores the inherited directory, so it gets an explicit -d <folder>.
            var wt = EditorLauncher.BuildLaunchSpec(editor, @"C:\Users\me\AppData\Local\Microsoft\WindowsApps\wt.exe", Folder);
            await Assert.That(wt.Arguments.Contains("-d")).IsTrue();
            await Assert.That(wt.Arguments.Contains(Folder)).IsTrue();
        }
        else if (OperatingSystem.IsLinux())
        {
            var term = EditorLauncher.BuildLaunchSpec(editor, "/usr/bin/gnome-terminal", Folder);
            await Assert.That(term.FileName).IsEqualTo("/usr/bin/gnome-terminal");
            await Assert.That(term.WorkingDirectory).IsEqualTo(Folder);   // terminals inherit the working directory
            await Assert.That(term.UseShellExecute).IsFalse();
            await Assert.That(term.Arguments.Contains(Folder)).IsFalse();
        }
    }

    [Test]
    public async Task The_console_forwards_the_configured_extra_arguments()
    {
        var editor = new Editor { Kind = EditorKind.Console, Arguments = "--flag" };

        if (OperatingSystem.IsWindows())
        {
            var cmd = EditorLauncher.BuildLaunchSpec(editor, @"C:\Windows\System32\cmd.exe", Folder);
            await Assert.That(cmd.Arguments.Contains("--flag")).IsTrue();

            // Extra args precede the wt -d <folder> the builder appends.
            var wt = EditorLauncher.BuildLaunchSpec(editor, @"C:\…\WindowsApps\wt.exe", Folder).Arguments.ToList();
            await Assert.That(wt.IndexOf("--flag")).IsLessThan(wt.IndexOf("-d"));
        }
        else if (OperatingSystem.IsLinux())
        {
            var term = EditorLauncher.BuildLaunchSpec(editor, "/usr/bin/gnome-terminal", Folder);
            await Assert.That(term.Arguments.Contains("--flag")).IsTrue();
        }
    }

    /// <summary>A machine with PowerShell 7 installed.</summary>
    private const string Pwsh = @"C:\Program Files\PowerShell\7\pwsh.exe";
    private static string? WithPwsh(string name) => name switch
    {
        "pwsh" => Pwsh,
        "powershell" => @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
        _ => null,
    };

    /// <summary>A machine with only Windows PowerShell.</summary>
    private static string? NoPwsh(string name) =>
        name == "powershell" ? @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe" : null;

    /// <summary>A machine where neither resolves — the last-resort literal has to carry it.</summary>
    private static string? NoShells(string name) => null;

    /// <summary>
    /// Which shell a Windows console command lands in. Asserted directly rather than through
    /// <see cref="EditorLauncher.BuildLaunchSpec"/> so it runs on every CI leg, not just the Windows one —
    /// this is the decision that governs whether a repo's command can resolve at all, and getting it wrong
    /// is invisible until a user's `aspire start` comes back "not recognized".
    /// </summary>
    [Test]
    public async Task A_console_that_only_hosts_a_shell_gets_the_best_one_on_the_machine()
    {
        const string Wt = @"C:\…\WindowsApps\wt.exe";

        // Windows Terminal is a host, not a shell — it says nothing about which shell to use, so the
        // choice is ours and PowerShell 7 wins: it's where the user's PATH, profile and execution
        // policy live. cmd would find neither a dotnet tool on a profile-set PATH nor a function.
        await Assert.That(string.Join(' ', EditorLauncher.WindowsShellFor(Wt, "aspire start", WithPwsh)))
            .IsEqualTo($"{Pwsh} -NoExit -Command aspire start");

        // Same for a script: pwsh, not the Windows PowerShell whose execution policy is Restricted
        // out of the box.
        await Assert.That(string.Join(' ', EditorLauncher.WindowsShellFor(Wt, "run-pirform.ps1", WithPwsh)))
            .IsEqualTo($"{Pwsh} -NoExit -File run-pirform.ps1");

        // Without PowerShell 7, Windows PowerShell carries it — never cmd, which resolves the least.
        await Assert.That(EditorLauncher.WindowsShellFor(Wt, "aspire start", NoPwsh)[0])
            .EndsWith("powershell.exe");
        await Assert.That(EditorLauncher.WindowsShellFor(Wt, "aspire start", NoShells)[0])
            .IsEqualTo("powershell.exe");   // the last-resort literal; present on every Windows machine
    }

    [Test]
    public async Task A_console_that_is_itself_a_shell_is_used_exactly_as_configured()
    {
        // Bare program names, because this test runs on every CI leg: a configured console is matched by
        // program name, and `Path.GetFileNameWithoutExtension` only splits on `\` when it's running on
        // Windows — so a Windows-style path here would silently match nothing on the Linux leg. Production
        // passes a full path from `Locate`, where that split works.
        const string Cmd = "cmd.exe";
        const string WinPs = "powershell.exe";

        // Configuring a shell is a deliberate choice, so it's honoured — pwsh is never substituted in,
        // even when it's sitting right there on the machine.
        await Assert.That(string.Join(' ', EditorLauncher.WindowsShellFor(Cmd, "aspire start", WithPwsh)))
            .IsEqualTo($"{Cmd} /k aspire start");
        await Assert.That(string.Join(' ', EditorLauncher.WindowsShellFor(WinPs, "aspire start", WithPwsh)))
            .IsEqualTo($"{WinPs} -NoExit -Command aspire start");

        // The one override: cmd can't run a .ps1 at all, so a script still goes to a PowerShell.
        await Assert.That(EditorLauncher.WindowsShellFor(Cmd, "build.ps1", WithPwsh)[0]).IsEqualTo(Pwsh);
    }

    [Test]
    public async Task A_console_run_command_is_hosted_by_the_platforms_shell()
    {
        var editor = new Editor { Kind = EditorKind.Console };

        if (OperatingSystem.IsWindows())
        {
            // An explicitly-configured cmd is honoured: it's what the user picked.
            var aspire = EditorLauncher.BuildLaunchSpec(editor, @"C:\Windows\System32\cmd.exe", Folder, "aspire start", NoPwsh);
            await Assert.That(Path.GetFileName(aspire.FileName)).IsEqualTo("cmd.exe");
            await Assert.That(string.Join(' ', aspire.Arguments)).IsEqualTo("/k aspire start");
            await Assert.That(aspire.WorkingDirectory).IsEqualTo(Folder);
            await Assert.That(aspire.UseShellExecute).IsTrue();

            // A .ps1 needs PowerShell's -File: cmd would hand it to its file association instead.
            var script = EditorLauncher.BuildLaunchSpec(editor, @"C:\Windows\System32\cmd.exe", Folder, "build.ps1", NoPwsh);
            await Assert.That(Path.GetFileName(script.FileName)).IsEqualTo("powershell.exe");
            await Assert.That(string.Join(' ', script.Arguments)).IsEqualTo("-NoExit -File build.ps1");

            // Windows Terminal hosts the shell instead of being bypassed, so the run lands in a wt tab —
            // and the shell it hosts is PowerShell 7 when the machine has it.
            var wt = EditorLauncher.BuildLaunchSpec(editor, @"C:\…\WindowsApps\wt.exe", Folder, "aspire start", WithPwsh);
            await Assert.That(string.Join(' ', wt.Arguments)).IsEqualTo($"-d {Folder} {Pwsh} -NoExit -Command aspire start");

            // The configured PowerShell is the host for anything else it's given.
            var pwsh = EditorLauncher.BuildLaunchSpec(editor, @"C:\Program Files\PowerShell\7\pwsh.exe", Folder, "aspire start", NoPwsh);
            await Assert.That(Path.GetFileName(pwsh.FileName)).IsEqualTo("pwsh.exe");
            await Assert.That(string.Join(' ', pwsh.Arguments)).IsEqualTo("-NoExit -Command aspire start");
        }
        else if (OperatingSystem.IsLinux())
        {
            // `bash -c "<command>; exec bash"` runs it and leaves the window on a shell afterwards.
            var run = EditorLauncher.BuildLaunchSpec(editor, "/usr/bin/xterm", Folder, "aspire start");
            await Assert.That(run.FileName).IsEqualTo("/usr/bin/xterm");
            await Assert.That(run.WorkingDirectory).IsEqualTo(Folder);
            await Assert.That(string.Join(' ', run.Arguments)).IsEqualTo("-e bash -c aspire start; exec bash");

            // A root .sh is invoked as ./name (the working directory isn't on PATH); gnome-terminal
            // takes its program after `--` rather than after -e.
            var script = EditorLauncher.BuildLaunchSpec(editor, "/usr/bin/gnome-terminal", Folder, "build.sh");
            await Assert.That(script.Arguments[0]).IsEqualTo($"--working-directory={Folder}");
            await Assert.That(script.Arguments[1]).IsEqualTo("--");
            await Assert.That(script.Arguments[^1]).IsEqualTo("./build.sh; exec bash");

            // A .ps1 is handed to pwsh rather than executed directly.
            var ps1 = EditorLauncher.BuildLaunchSpec(editor, "/usr/bin/xterm", Folder, "build.ps1");
            await Assert.That(ps1.Arguments[^1]).IsEqualTo("pwsh build.ps1; exec bash");
        }
    }

    [Test]
    public async Task A_console_with_no_command_still_just_opens_the_folder()
    {
        var editor = new Editor { Kind = EditorKind.Console };
        var exe = OperatingSystem.IsWindows() ? @"C:\Windows\System32\cmd.exe" : "/usr/bin/xterm";

        // A blank command is "no command": the plain open-a-terminal plan, unchanged.
        foreach (var blank in new[] { null, "", "   " })
        {
            var spec = EditorLauncher.BuildLaunchSpec(editor, exe, Folder, blank);
            await Assert.That(spec.FileName).IsEqualTo(exe);
            await Assert.That(spec.Arguments.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task A_run_command_means_nothing_to_the_other_targets()
    {
        var exe = OperatingSystem.IsWindows() ? @"C:\Windows\explorer.exe" : "/usr/bin/xdg-open";

        var files = EditorLauncher.BuildLaunchSpec(new Editor { Kind = EditorKind.FileExplorer }, exe, Folder, "build.ps1");
        await Assert.That(files.Arguments.Count).IsEqualTo(1);
        await Assert.That(files.Arguments[0]).IsEqualTo(Folder);

        var editor = EditorLauncher.BuildLaunchSpec(new Editor { Kind = EditorKind.VsCode }, "/opt/ed/code", Folder, "build.ps1");
        await Assert.That(editor.Arguments.Count).IsEqualTo(1);
        await Assert.That(editor.Arguments[0]).IsEqualTo(Folder);
    }

    [Test]
    public async Task The_file_explorer_passes_the_folder_to_the_file_manager()
    {
        var exe = OperatingSystem.IsWindows() ? @"C:\Windows\explorer.exe" : "/usr/bin/xdg-open";

        var spec = EditorLauncher.BuildLaunchSpec(new Editor { Kind = EditorKind.FileExplorer }, exe, Folder);

        await Assert.That(spec.FileName).IsEqualTo(exe);
        await Assert.That(spec.Arguments.Count).IsEqualTo(1);
        await Assert.That(spec.Arguments[0]).IsEqualTo(Folder);
        await Assert.That(spec.UseShellExecute).IsFalse();
    }
}
