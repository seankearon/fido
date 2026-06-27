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
    public async Task A_current_version_editor_list_is_left_intact()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        svc.Save(new AppConfig
        {
            ConfigVersion = AppConfig.CurrentConfigVersion,   // already migrated → nothing is appended
            Editors = new() { new Editor { Name = "Zed", Kind = EditorKind.Zed } },
            DefaultEditorIndex = 0,
        });

        var loaded = svc.Load();

        await Assert.That(loaded.Editors.Count).IsEqualTo(1);
        await Assert.That(loaded.Editors[0].Kind).IsEqualTo(EditorKind.Zed);
    }

    [Test]
    public async Task The_newer_built_in_targets_are_appended_to_a_config_that_predates_them()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        svc.Save(new AppConfig
        {
            // A pre-WebStorm config (ConfigVersion defaults to 0) with the old built-in line-up.
            Editors = new()
            {
                new Editor { Name = "Rider", Kind = EditorKind.Rider },
                new Editor { Name = "VS Code", Kind = EditorKind.VsCode },
                new Editor { Name = "Visual Studio", Kind = EditorKind.VisualStudio },
                new Editor { Name = "Zed", Kind = EditorKind.Zed },
            },
            DefaultEditorIndex = 1,   // VS Code is the default
        });

        var loaded = svc.Load();

        // WebStorm, then Console and File Explorer, are appended (not inserted), so existing positions
        // and the default pointer are preserved.
        await Assert.That(loaded.Editors.Count).IsEqualTo(7);
        await Assert.That(loaded.Editors[0].Kind).IsEqualTo(EditorKind.Rider);
        await Assert.That(loaded.Editors[4].Kind).IsEqualTo(EditorKind.WebStorm);
        await Assert.That(loaded.Editors[5].Kind).IsEqualTo(EditorKind.Console);
        await Assert.That(loaded.Editors[6].Kind).IsEqualTo(EditorKind.FileExplorer);
        await Assert.That(loaded.DefaultEditor!.Kind).IsEqualTo(EditorKind.VsCode);
        await Assert.That(loaded.ConfigVersion).IsEqualTo(AppConfig.CurrentConfigVersion);
    }

    [Test]
    public async Task The_built_in_target_migration_does_not_add_duplicates()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        svc.Save(new AppConfig
        {
            // Predates versioning, but the user already has WebStorm and a Console → don't add a second of either.
            Editors = new()
            {
                new Editor { Name = "Rider", Kind = EditorKind.Rider },
                new Editor { Name = "WebStorm", Kind = EditorKind.WebStorm },
                new Editor { Name = "Console", Kind = EditorKind.Console },
            },
            DefaultEditorIndex = 0,
        });

        var loaded = svc.Load();

        await Assert.That(loaded.Editors.Count(e => e.Kind == EditorKind.WebStorm)).IsEqualTo(1);
        await Assert.That(loaded.Editors.Count(e => e.Kind == EditorKind.Console)).IsEqualTo(1);
        // Only the still-missing File Explorer is appended → 3 originals + 1.
        await Assert.That(loaded.Editors.Count).IsEqualTo(4);
        await Assert.That(loaded.Editors[^1].Kind).IsEqualTo(EditorKind.FileExplorer);
    }

    [Test]
    public async Task An_editor_slug_survives_a_save_and_load_round_trip()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        svc.Save(new AppConfig
        {
            Editors = new() { new Editor { Name = "Zed", Kind = EditorKind.Zed, Slug = "z" } },
            DefaultEditorIndex = 0,
        });

        var loaded = svc.Load();

        await Assert.That(loaded.Editors[0].Slug).IsEqualTo("z");
        await Assert.That(loaded.FindEditorBySlug("z")!.Kind).IsEqualTo(EditorKind.Zed);
    }

    [Test]
    public async Task Seeded_default_editors_carry_their_slugs()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        svc.Save(new AppConfig());   // no editors → seeded with the defaults on load

        var loaded = svc.Load();

        await Assert.That(loaded.FindEditorBySlug("rider")!.Kind).IsEqualTo(EditorKind.Rider);
        await Assert.That(loaded.FindEditorBySlug("zed")!.Kind).IsEqualTo(EditorKind.Zed);
    }

    [Test]
    public async Task An_out_of_range_default_index_is_clamped()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        svc.Save(new AppConfig
        {
            ConfigVersion = AppConfig.CurrentConfigVersion,   // isolate the clamp from the WebStorm migration
            Editors = new() { new Editor { Name = "Rider", Kind = EditorKind.Rider } },
            DefaultEditorIndex = 7,
        });

        var loaded = svc.Load();

        await Assert.That(loaded.DefaultEditorIndex).IsEqualTo(0);
    }
}
