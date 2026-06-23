using System.IO;
using Fido;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;
using Fido.ViewModels;
using Fido.Views;

namespace Fido.Tests.Dialogs;

/// <summary>The real SettingsDialog window: editing inputs and persisting (or discarding) via ConfigService.</summary>
[NotInParallel]
public class SettingsDialogTests
{
    private static (ConfigService Service, AppConfig Config, string Dir) NewConfig(TestRepoWorld world)
    {
        var dir = Path.Combine(world.Root, "config", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var service = new ConfigService(dir);
        var config = AppConfig.CreateDefault();
        config.Theme = AppTheme.System;
        service.Save(config);
        return (service, config, dir);
    }

    [Test]
    public async Task Save_persists_edited_search_roots_and_theme()
    {
        using var world = new TestRepoWorld();
        var (service, config, dir) = NewConfig(world);
        var newRoots = $"C:{Path.DirectorySeparatorChar}work{Path.DirectorySeparatorChar}one"
                       + "\n" + $"C:{Path.DirectorySeparatorChar}work{Path.DirectorySeparatorChar}two";

        await Harness.OnUi(async owner =>
        {
            var dialog = new SettingsDialog(config, service);
            var resultTask = dialog.ShowDialog(owner);
            UiTestExtensions.Pump();
            Screenshots.Save(dialog, "settings-dialog");

            dialog.SetText("SearchRootsBox", newRoots);     // drive the real text box
            ((SettingsViewModel)dialog.DataContext!).SelectedTheme = AppTheme.Dark;
            UiTestExtensions.Pump();

            dialog.ClickButton("SaveButton");
            await resultTask;
            App.ApplyTheme(AppTheme.System);   // don't let the saved theme bleed into other tests
        });

        var reloaded = new ConfigService(dir).Load();
        await Assert.That(reloaded.SearchRoots.Count).IsEqualTo(2);
        await Assert.That(reloaded.SearchRoots.Contains($"C:{Path.DirectorySeparatorChar}work{Path.DirectorySeparatorChar}one")).IsTrue();
        await Assert.That(reloaded.Theme).IsEqualTo(AppTheme.Dark);
    }

    [Test]
    public async Task Save_persists_the_close_delay()
    {
        using var world = new TestRepoWorld();
        var (service, config, dir) = NewConfig(world);

        await Harness.OnUi(async owner =>
        {
            var dialog = new SettingsDialog(config, service);
            var resultTask = dialog.ShowDialog(owner);
            UiTestExtensions.Pump();

            dialog.SetText("CloseDelayBox", "3");   // drive the real delay text box
            UiTestExtensions.Pump();

            dialog.ClickButton("SaveButton");
            await resultTask;
            App.ApplyTheme(AppTheme.System);
        });

        var reloaded = new ConfigService(dir).Load();
        await Assert.That(reloaded.CloseAfterOpenDelaySeconds).IsEqualTo(3);
    }

    [Test]
    public async Task Cancel_discards_edits()
    {
        using var world = new TestRepoWorld();
        var (service, config, dir) = NewConfig(world);
        var rootsBefore = config.SearchRoots.Count;

        await Harness.OnUi(async owner =>
        {
            var dialog = new SettingsDialog(config, service);
            var resultTask = dialog.ShowDialog(owner);
            UiTestExtensions.Pump();

            dialog.SetText("SearchRootsBox", "C:\\changed");
            UiTestExtensions.Pump();

            dialog.ClickButton("CancelButton");
            await resultTask;
            App.ApplyTheme(AppTheme.System);
        });

        var reloaded = new ConfigService(dir).Load();
        await Assert.That(reloaded.SearchRoots.Count).IsEqualTo(rootsBefore);
        await Assert.That(reloaded.SearchRoots.Contains("C:\\changed")).IsFalse();
    }

    [Test]
    public async Task Save_persists_ticked_new_branch_repos()
    {
        using var world = new TestRepoWorld();
        var (service, config, dir) = NewConfig(world);

        await Harness.OnUi(async owner =>
        {
            var dialog = new SettingsDialog(config, service);
            var resultTask = dialog.ShowDialog(owner);
            UiTestExtensions.Pump();

            var vm = (SettingsViewModel)dialog.DataContext!;
            vm.MergeDetected(new[] { @"C:\src\widget", @"C:\src\gadget" });
            vm.NewBranchRepos.First(r => r.Path == @"C:\src\widget").IsEnabled = true;   // tick one, leave the other
            UiTestExtensions.Pump();

            dialog.ClickButton("SaveButton");
            await resultTask;
            App.ApplyTheme(AppTheme.System);
        });

        var reloaded = new ConfigService(dir).Load();
        await Assert.That(reloaded.NewBranchRepos.Count).IsEqualTo(1);
        await Assert.That(reloaded.NewBranchRepos.Contains(@"C:\src\widget")).IsTrue();
    }
}
