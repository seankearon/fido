using System.Linq;
using Fido.Models;
using Fido.ViewModels;

namespace Fido.Tests.ViewModels;

/// <summary>The new-branch repo checklist logic in <see cref="SettingsViewModel"/> (no UI).</summary>
public class SettingsViewModelTests
{
    [Test]
    public async Task LoadFrom_seeds_configured_repos_ticked()
    {
        var vm = new SettingsViewModel();
        vm.LoadFrom(new AppConfig { NewBranchRepos = new() { @"C:\src\a", @"C:\src\b" } });

        await Assert.That(vm.NewBranchRepos.Count).IsEqualTo(2);
        await Assert.That(vm.NewBranchRepos.All(r => r.IsEnabled)).IsTrue();
    }

    [Test]
    public async Task ApplyTo_writes_only_ticked_repo_paths()
    {
        var vm = new SettingsViewModel();
        vm.LoadFrom(new AppConfig { NewBranchRepos = new() { @"C:\src\a", @"C:\src\b" } });
        vm.NewBranchRepos.First(r => r.Path == @"C:\src\b").IsEnabled = false;

        var cfg = new AppConfig();
        vm.ApplyTo(cfg);

        await Assert.That(cfg.NewBranchRepos.Count).IsEqualTo(1);
        await Assert.That(cfg.NewBranchRepos[0]).IsEqualTo(@"C:\src\a");
    }

    [Test]
    public async Task CloseAfterOpen_round_trips_through_load_and_apply()
    {
        var vm = new SettingsViewModel();
        vm.LoadFrom(new AppConfig { CloseAfterOpen = CloseAfterOpen.Always });

        await Assert.That(vm.CloseAfterOpen).IsEqualTo(CloseAfterOpen.Always);
        await Assert.That(vm.IsCloseAlways).IsTrue();
        await Assert.That(vm.IsCloseCommandLine).IsFalse();

        vm.IsCloseNever = true;   // segmented control selects a different option

        var cfg = new AppConfig();
        vm.ApplyTo(cfg);
        await Assert.That(cfg.CloseAfterOpen).IsEqualTo(CloseAfterOpen.Never);
    }

    [Test]
    public async Task CloseDelay_round_trips_through_load_and_apply()
    {
        var vm = new SettingsViewModel();
        vm.LoadFrom(new AppConfig { CloseAfterOpenDelaySeconds = 25 });

        await Assert.That(vm.CloseAfterOpenDelayText).IsEqualTo("25");

        vm.CloseAfterOpenDelayText = "3";

        var cfg = new AppConfig();
        vm.ApplyTo(cfg);
        await Assert.That(cfg.CloseAfterOpenDelaySeconds).IsEqualTo(3);
    }

    [Test]
    public async Task CloseDelay_zero_is_preserved_as_immediate()
    {
        var vm = new SettingsViewModel { CloseAfterOpenDelayText = "0" };

        var cfg = new AppConfig();
        vm.ApplyTo(cfg);
        await Assert.That(cfg.CloseAfterOpenDelaySeconds).IsEqualTo(0);
    }

    [Test]
    [Arguments("", AppConfig.DefaultCloseAfterOpenDelaySeconds)]   // blank → default
    [Arguments("abc", AppConfig.DefaultCloseAfterOpenDelaySeconds)] // garbage → default
    [Arguments("-5", 0)]                                            // negative → clamped to immediate
    [Arguments("999999", AppConfig.MaxCloseAfterOpenDelaySeconds)]  // huge → clamped to the ceiling
    public async Task CloseDelay_apply_sanitizes_input(string text, int expected)
    {
        var vm = new SettingsViewModel { CloseAfterOpenDelayText = text };

        var cfg = new AppConfig();
        vm.ApplyTo(cfg);
        await Assert.That(cfg.CloseAfterOpenDelaySeconds).IsEqualTo(expected);
    }

    [Test]
    public async Task IsAutoCloseEnabled_tracks_the_never_option()
    {
        var vm = new SettingsViewModel();

        vm.IsCloseAlways = true;
        await Assert.That(vm.IsAutoCloseEnabled).IsTrue();

        vm.IsCloseNever = true;
        await Assert.That(vm.IsAutoCloseEnabled).IsFalse();
    }

    [Test]
    public async Task Editors_round_trip_through_load_and_apply()
    {
        var vm = new SettingsViewModel();
        vm.LoadFrom(new AppConfig
        {
            Editors = new()
            {
                new Editor { Name = "Rider", Kind = EditorKind.Rider },
                new Editor { Name = "VS Code", Kind = EditorKind.VsCode, Path = @"C:\code\code.cmd" },
            },
            DefaultEditorIndex = 1,
        });

        await Assert.That(vm.Editors.Count).IsEqualTo(2);
        await Assert.That(vm.Editors[1].IsDefault).IsTrue();
        await Assert.That(vm.Editors[0].IsDefault).IsFalse();

        var cfg = new AppConfig();
        vm.ApplyTo(cfg);

        await Assert.That(cfg.Editors.Count).IsEqualTo(2);
        await Assert.That(cfg.Editors[1].Path).IsEqualTo(@"C:\code\code.cmd");
        await Assert.That(cfg.DefaultEditorIndex).IsEqualTo(1);
    }

    [Test]
    public async Task Setting_a_row_default_clears_the_others()
    {
        var vm = new SettingsViewModel();
        vm.LoadFrom(new AppConfig
        {
            Editors = new()
            {
                new Editor { Name = "Rider", Kind = EditorKind.Rider },
                new Editor { Name = "Zed", Kind = EditorKind.Zed },
            },
            DefaultEditorIndex = 0,
        });

        vm.Editors[1].IsDefault = true;   // ticking the second row

        await Assert.That(vm.Editors[0].IsDefault).IsFalse();
        await Assert.That(vm.Editors[1].IsDefault).IsTrue();

        var cfg = new AppConfig();
        vm.ApplyTo(cfg);
        await Assert.That(cfg.DefaultEditorIndex).IsEqualTo(1);
    }

    [Test]
    public async Task Removing_the_default_promotes_another_editor()
    {
        var vm = new SettingsViewModel();
        vm.LoadFrom(new AppConfig
        {
            Editors = new()
            {
                new Editor { Name = "Rider", Kind = EditorKind.Rider },
                new Editor { Name = "Zed", Kind = EditorKind.Zed },
            },
            DefaultEditorIndex = 0,
        });

        vm.RemoveEditor(vm.Editors[0]);   // drop the current default

        await Assert.That(vm.Editors.Count).IsEqualTo(1);
        await Assert.That(vm.Editors[0].IsDefault).IsTrue();   // the survivor became default
    }

    [Test]
    public async Task MergeDetected_adds_new_unticked_preserves_state_and_dedups_case_insensitively()
    {
        var vm = new SettingsViewModel();
        vm.LoadFrom(new AppConfig { NewBranchRepos = new() { @"C:\src\a" } });   // already ticked

        vm.MergeDetected(new[] { @"C:\SRC\A", @"C:\src\c" });   // A is a case-variant dup; c is new

        await Assert.That(vm.NewBranchRepos.Count).IsEqualTo(2);
        await Assert.That(vm.NewBranchRepos.First(r => r.Path == @"C:\src\a").IsEnabled).IsTrue();
        await Assert.That(vm.NewBranchRepos.First(r => r.Path == @"C:\src\c").IsEnabled).IsFalse();
    }
}
