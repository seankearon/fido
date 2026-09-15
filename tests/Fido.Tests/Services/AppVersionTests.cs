using System;
using System.IO;
using Fido.Services;

namespace Fido.Tests.Services;

/// <summary>
/// The version the header badge shows: how it is derived from the assembly's attributes, and that an
/// ordinary build actually carries one.
/// </summary>
public class AppVersionTests
{
    [Test]
    public async Task Prefers_the_informational_version()
    {
        await Assert.That(AppVersion.Format("0.9.3", new Version(9, 9, 9, 9))).IsEqualTo("0.9.3");
    }

    [Test]
    public async Task Drops_the_commit_suffix_a_source_linked_build_appends()
    {
        await Assert.That(AppVersion.Format("0.9.3+8f2c1ad", null)).IsEqualTo("0.9.3");
    }

    [Test]
    public async Task Keeps_a_pre_release_suffix()
    {
        // Only the informational version can carry one, which is the reason it is preferred.
        await Assert.That(AppVersion.Format("1.0.0-rc.2+8f2c1ad", null)).IsEqualTo("1.0.0-rc.2");
    }

    [Test]
    public async Task Falls_back_to_the_assembly_version_trimmed_of_its_padding()
    {
        await Assert.That(AppVersion.Format(null, new Version(0, 9, 3, 0))).IsEqualTo("0.9.3");
        await Assert.That(AppVersion.Format("   ", new Version(0, 9))).IsEqualTo("0.9");
    }

    [Test]
    public async Task Is_empty_when_the_assembly_carries_nothing_usable()
    {
        // The badge hides itself rather than showing a "v" with nothing after it.
        await Assert.That(AppVersion.Format(null, null)).IsEmpty();
        await Assert.That(AppVersion.Format("+8f2c1ad", null)).IsEmpty();
    }

    [Test]
    public async Task Label_prefixes_the_version_with_a_v()
    {
        await Assert.That(AppVersion.Label).IsEqualTo($"v{AppVersion.Current}");
    }

    [Test]
    public async Task An_ordinary_build_takes_its_version_from_ver_txt()
    {
        // src/Fido.csproj reads ver.txt whenever nothing else set Version, so a build from the IDE or
        // CI shows the real number instead of the SDK's 1.0.0 placeholder. The release build overrides
        // it with the generated Directory.Build.props — and runs its Test stage before writing that
        // file, so this holds there too.
        var declared = File.ReadAllText(VerFile()).Trim();

        await Assert.That(AppVersion.Current).IsEqualTo(declared);
    }

    /// <summary>Walks out of bin/ to the repo root, which is wherever ver.txt sits.</summary>
    private static string VerFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "ver.txt");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("No ver.txt above the test output folder — is the repo layout intact?");
    }
}
