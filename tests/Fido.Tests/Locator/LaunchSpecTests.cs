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
