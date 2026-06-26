using System.IO;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.Locator;

/// <summary>
/// The real <see cref="EditorLauncher.Locate"/> probing, against a fake editor executable on disk.
/// PATH is prepended (never cleared) and restored, so concurrent git-using tests keep working.
/// </summary>
[NotInParallel]
public class EditorLauncherTests
{
    private static string RiderFileName => OperatingSystem.IsWindows() ? "rider64.exe" : "rider";

    [Test]
    public async Task An_explicit_existing_path_is_returned_for_any_kind()
    {
        using var world = new TestRepoWorld();
        var exe = Path.Combine(world.Root, RiderFileName);
        File.WriteAllText(exe, "");

        var found = new EditorLauncher().Locate(new Editor { Kind = EditorKind.Rider, Path = exe });

        await Assert.That(found).IsEqualTo(exe);
    }

    [Test]
    public async Task A_custom_editor_with_an_explicit_path_is_returned()
    {
        using var world = new TestRepoWorld();
        var exe = Path.Combine(world.Root, "my-editor");
        File.WriteAllText(exe, "");

        var found = new EditorLauncher().Locate(new Editor { Kind = EditorKind.Custom, Path = exe });

        await Assert.That(found).IsEqualTo(exe);
    }

    [Test]
    public async Task A_rider_on_PATH_is_discovered()
    {
        using var world = new TestRepoWorld();
        var binDir = Path.Combine(world.Root, "bin");
        Directory.CreateDirectory(binDir);
        var exe = Path.Combine(binDir, RiderFileName);
        File.WriteAllText(exe, "");

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", binDir + Path.PathSeparator + originalPath);
        try
        {
            var found = new EditorLauncher().Locate(new Editor { Kind = EditorKind.Rider });
            await Assert.That(found).IsEqualTo(exe);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Test]
    public async Task A_webstorm_on_PATH_is_discovered()
    {
        using var world = new TestRepoWorld();
        var binDir = Path.Combine(world.Root, "bin");
        Directory.CreateDirectory(binDir);
        var exe = Path.Combine(binDir, OperatingSystem.IsWindows() ? "webstorm64.exe" : "webstorm");
        File.WriteAllText(exe, "");

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", binDir + Path.PathSeparator + originalPath);
        try
        {
            var found = new EditorLauncher().Locate(new Editor { Kind = EditorKind.WebStorm });
            await Assert.That(found).IsEqualTo(exe);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Test]
    public async Task A_nonexistent_path_is_never_returned_verbatim()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "definitely", "not", "rider");

        var found = new EditorLauncher().Locate(new Editor { Kind = EditorKind.Rider, Path = bogus });

        // It may fall through to a real install or to null — but never hands back the missing path.
        await Assert.That(found).IsNotEqualTo(bogus);
    }

    [Test]
    public async Task A_custom_editor_without_a_path_is_not_found()
    {
        var found = new EditorLauncher().Locate(new Editor { Kind = EditorKind.Custom });

        await Assert.That(found).IsNull();
    }
}
