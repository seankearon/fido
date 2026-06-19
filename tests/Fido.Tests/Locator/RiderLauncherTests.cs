using System.IO;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.Locator;

/// <summary>
/// The real <see cref="RiderLauncher.Locate"/> probing, against a fake rider executable on disk.
/// PATH is prepended (never cleared) and restored, so concurrent git-using tests keep working.
/// </summary>
[NotInParallel]
public class RiderLauncherTests
{
    private static string RiderFileName => OperatingSystem.IsWindows() ? "rider64.exe" : "rider";

    [Test]
    public async Task An_explicit_existing_config_path_is_returned()
    {
        using var world = new TestRepoWorld();
        var exe = Path.Combine(world.Root, RiderFileName);
        File.WriteAllText(exe, "");

        var found = new RiderLauncher().Locate(new AppConfig { RiderPath = exe });

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
            var found = new RiderLauncher().Locate(new AppConfig());
            await Assert.That(found).IsEqualTo(exe);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Test]
    public async Task A_nonexistent_config_path_is_never_returned_verbatim()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "definitely", "not", "rider");

        var found = new RiderLauncher().Locate(new AppConfig { RiderPath = bogus });

        // It may fall through to a real install or to null — but never hands back the missing path.
        await Assert.That(found).IsNotEqualTo(bogus);
    }
}
