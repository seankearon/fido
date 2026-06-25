using System.IO;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.Services;

/// <summary>Editor seeding/migration that <see cref="ConfigService.Load"/> applies via Normalize.</summary>
public class ConfigServiceTests
{
    private static ConfigService InTempDir(TestRepoWorld world)
    {
        var dir = Path.Combine(world.Root, "config", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new ConfigService(dir);
    }

    [Test]
    public async Task A_config_with_no_editors_is_seeded_with_the_defaults()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        svc.Save(new AppConfig());   // no editors configured

        var loaded = svc.Load();

        await Assert.That(loaded.Editors.Count).IsGreaterThan(0);
        await Assert.That(loaded.Editors[0].Kind).IsEqualTo(EditorKind.Rider);   // Rider is the default
        await Assert.That(loaded.DefaultEditor!.Kind).IsEqualTo(EditorKind.Rider);
    }

    [Test]
    public async Task A_legacy_rider_path_is_migrated_onto_the_rider_editor()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        svc.Save(new AppConfig { RiderPath = @"C:\tools\rider64.exe" });

        var loaded = svc.Load();

        var rider = loaded.Editors.First(e => e.Kind == EditorKind.Rider);
        await Assert.That(rider.Path).IsEqualTo(@"C:\tools\rider64.exe");
    }

    [Test]
    public async Task An_existing_editor_list_is_left_intact()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        svc.Save(new AppConfig
        {
            Editors = new() { new Editor { Name = "Zed", Kind = EditorKind.Zed } },
            DefaultEditorIndex = 0,
        });

        var loaded = svc.Load();

        await Assert.That(loaded.Editors.Count).IsEqualTo(1);
        await Assert.That(loaded.Editors[0].Kind).IsEqualTo(EditorKind.Zed);
    }

    [Test]
    public async Task An_out_of_range_default_index_is_clamped()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        svc.Save(new AppConfig
        {
            Editors = new() { new Editor { Name = "Rider", Kind = EditorKind.Rider } },
            DefaultEditorIndex = 7,
        });

        var loaded = svc.Load();

        await Assert.That(loaded.DefaultEditorIndex).IsEqualTo(0);
    }
}
