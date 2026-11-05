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

    [Test]
    public async Task A_config_predating_the_window_title_setting_keeps_the_title_renaming_on()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        // A file written before the setting existed has no entry for it at all — the default must win,
        // rather than a missing JSON property reading back as false.
        File.WriteAllText(svc.ConfigFilePath, """{ "configVersion": 2, "searchRoots": [], "theme": "Dark" }""");

        var loaded = svc.Load();

        await Assert.That(loaded.ShowTargetInWindowTitle).IsTrue();
        await Assert.That(loaded.PullBeforeRun).IsTrue();
        await Assert.That(loaded.SwitchToOpenWindow).IsTrue();
        await Assert.That(loaded.Theme).IsEqualTo(AppTheme.Dark);   // the rest of the file still read
    }

    [Test]
    public async Task Turning_the_window_title_setting_off_survives_a_save_and_load_round_trip()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        svc.Save(new AppConfig { ShowTargetInWindowTitle = false });

        await Assert.That(svc.Load().ShowTargetInWindowTitle).IsFalse();
    }

    [Test]
    public async Task Turning_the_pre_run_pull_off_survives_a_save_and_load_round_trip()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        svc.Save(new AppConfig { PullBeforeRun = false });

        await Assert.That(svc.Load().PullBeforeRun).IsFalse();
    }

    [Test]
    public async Task Turning_the_switch_to_an_open_window_off_survives_a_save_and_load_round_trip()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        svc.Save(new AppConfig { SwitchToOpenWindow = false });

        await Assert.That(svc.Load().SwitchToOpenWindow).IsFalse();
    }
    [Test]
    public async Task Keyboard_shortcuts_round_trip_on_the_commands_and_on_the_tools()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        var config = AppConfig.CreateDefault();
        config.Shortcuts["ToggleTheme"] = "Ctrl+K, T";
        config.Shortcuts["Rescan"] = "";               // deliberately none
        config.Editors[2].Shortcut = "Ctrl+K, V";
        svc.Save(config);

        var loaded = svc.Load();

        await Assert.That(loaded.Shortcuts["ToggleTheme"]).IsEqualTo("Ctrl+K, T");
        await Assert.That(loaded.Shortcuts["Rescan"]).IsEqualTo("");
        await Assert.That(loaded.Editors[2].Shortcut).IsEqualTo("Ctrl+K, V");
        await Assert.That(loaded.Editors[0].Shortcut).IsNull();   // unset: still follows its number
    }

    [Test]
    public async Task A_config_from_before_shortcuts_were_configurable_keeps_every_default()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        Directory.CreateDirectory(svc.ConfigDirectory);
        File.WriteAllText(svc.ConfigFilePath, """
            { "ConfigVersion": 2, "Editors": [ { "Name": "Rider", "Kind": "Rider" } ], "Shortcuts": null }
            """);

        var loaded = svc.Load();

        await Assert.That(loaded.Shortcuts).IsNotNull();
        await Assert.That(loaded.Shortcuts.Count).IsEqualTo(0);
        await Assert.That(loaded.Editors[0].Shortcut).IsNull();
    }

    // --- Shared folder ----------------------------------------------------------------------

    private static string SharedDir(TestRepoWorld world) =>
        Path.Combine(world.Root, "shared", Guid.NewGuid().ToString("N"));

    [Test]
    public async Task The_shared_folder_gets_the_portable_settings_and_none_of_this_machines()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        var shared = SharedDir(world);
        var config = AppConfig.CreateDefault();
        config.SharedDirectory = shared;
        config.Theme = AppTheme.Dark;
        config.SearchRoots = new() { @"D:\work" };
        config.RecentBranches = new() { "feat/x" };
        svc.Save(config);

        var text = File.ReadAllText(Path.Combine(shared, ConfigService.FileName));

        await Assert.That(text).Contains("\"Theme\": \"Dark\"");
        await Assert.That(text).Contains("\"Editors\"");
        await Assert.That(text).DoesNotContain("SearchRoots");
        await Assert.That(text).DoesNotContain("RecentBranches");
        await Assert.That(text).DoesNotContain("SharedDirectory");
    }

    [Test]
    public async Task The_shared_file_wins_over_the_local_copy_on_load()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        var shared = SharedDir(world);
        svc.Save(new AppConfig { SharedDirectory = shared, Theme = AppTheme.Light, SearchRoots = { @"D:\work" } });
        // Another machine changes a shared setting.
        File.WriteAllText(Path.Combine(shared, ConfigService.FileName), """{ "Theme": "Dark", "PullBeforeRun": false }""");

        var loaded = new ConfigService(svc.ConfigDirectory).Load();

        await Assert.That(loaded.Theme).IsEqualTo(AppTheme.Dark);
        await Assert.That(loaded.PullBeforeRun).IsFalse();
        await Assert.That(loaded.SearchRoots.Single()).IsEqualTo(@"D:\work");   // local-only setting untouched
        await Assert.That(loaded.Editors.Count).IsGreaterThan(0);                 // missing list keeps the local one
    }

    [Test]
    public async Task A_save_that_changes_nothing_shared_leaves_the_shared_file_alone()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        var shared = SharedDir(world);
        svc.Save(new AppConfig { SharedDirectory = shared });
        var loaded = svc.Load();
        var file = Path.Combine(shared, ConfigService.FileName);
        // Another machine changes the shared file while this one is running.
        File.WriteAllText(file, """{ "Theme": "Dark" }""");

        loaded.RecentBranches.Insert(0, "feat/y");   // an MRU save, as the main window makes
        svc.Save(loaded);

        await Assert.That(File.ReadAllText(file)).IsEqualTo("""{ "Theme": "Dark" }""");
    }

    [Test]
    public async Task Changing_a_shared_setting_writes_the_shared_file()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        var shared = SharedDir(world);
        svc.Save(new AppConfig { SharedDirectory = shared });
        var loaded = svc.Load();

        loaded.Theme = AppTheme.Dark;
        svc.Save(loaded);

        await Assert.That(File.ReadAllText(Path.Combine(shared, ConfigService.FileName))).Contains("\"Theme\": \"Dark\"");
    }

    [Test]
    public async Task Pointing_at_a_folder_that_already_has_settings_joins_them_rather_than_overwriting()
    {
        using var world = new TestRepoWorld();
        var shared = SharedDir(world);
        Directory.CreateDirectory(shared);
        var file = Path.Combine(shared, ConfigService.FileName);
        File.WriteAllText(file, """{ "Theme": "Dark", "Editors": [ { "Name": "Zed", "Kind": "Zed" } ], "DefaultEditorIndex": 0 }""");

        // A second machine, with settings of its own, is pointed at the folder.
        var svc = InTempDir(world);
        svc.Save(AppConfig.CreateDefault());
        var config = svc.Load();
        config.SharedDirectory = shared;
        svc.Save(config);

        await Assert.That(config.Theme).IsEqualTo(AppTheme.Dark);
        await Assert.That(config.Editors.Single().Kind).IsEqualTo(EditorKind.Zed);
        await Assert.That(File.ReadAllText(file)).Contains("Zed");
        await Assert.That(new ConfigService(svc.ConfigDirectory).Load().Editors.Single().Kind).IsEqualTo(EditorKind.Zed);
    }

    [Test]
    public async Task An_unreachable_shared_folder_falls_back_to_the_local_copy()
    {
        using var world = new TestRepoWorld();
        var svc = InTempDir(world);
        var shared = SharedDir(world);
        svc.Save(new AppConfig { SharedDirectory = shared, Theme = AppTheme.Dark });
        Directory.Delete(shared, recursive: true);   // the synced drive isn't there today

        var loaded = new ConfigService(svc.ConfigDirectory).Load();

        await Assert.That(loaded.Theme).IsEqualTo(AppTheme.Dark);
        await Assert.That(loaded.SharedDirectory).IsEqualTo(shared);
    }

    [Test]
    public async Task A_shared_folder_expands_environment_variables_and_a_leading_tilde()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Environment.SetEnvironmentVariable("FIDO_TEST_SHARED", home);

        await Assert.That(ConfigService.ResolveSharedDirectory("~/fido")).IsEqualTo(Path.Combine(home, "fido"));
        await Assert.That(ConfigService.ResolveSharedDirectory("%FIDO_TEST_SHARED%" + Path.DirectorySeparatorChar + "fido"))
            .IsEqualTo(Path.Combine(home, "fido"));
        await Assert.That(ConfigService.ResolveSharedDirectory("  ")).IsNull();
    }
}
