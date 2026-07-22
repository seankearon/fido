using System.IO;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;
using Fido.Views;

namespace Fido.Tests.E2E;

/// <summary>
/// Regenerates the marketing gallery under <c>Docs/screenshots</c> — not a test of behaviour.
/// Gated behind <c>FIDO_GALLERY=1</c> so ordinary runs skip it: it writes the exact file names the
/// gallery README references (dark/light pairs per state, plus the README hero), and it must be run
/// alone with <c>FIDO_SCREENSHOT_DIR</c> pointing at the gallery folder so no other test's frames
/// land there. A readable demo world is built under <c>%TEMP%\fido-demo</c> (short paths beat the
/// usual GUID soup in the card headlines).
/// </summary>
[NotInParallel]
public class GalleryScreenshotTests
{
    [Test]
    public async Task Regenerate_the_docs_gallery()
    {
        if (Environment.GetEnvironmentVariable("FIDO_GALLERY") != "1")
            return;   // docs tool, not a behavioural test — opt in explicitly

        // --- A small, readable demo world -------------------------------------------------
        var demo = Path.Combine(Path.GetTempPath(), "fido-demo");
        ForceDelete(demo);
        var src = Path.Combine(demo, "src");
        Directory.CreateDirectory(src);

        var seed = Path.Combine(demo, "seed", "platform");
        Directory.CreateDirectory(seed);
        TestRepoWorld.Git(seed, "init", "-b", "main");
        File.WriteAllText(Path.Combine(seed, "Platform.sln"), "Microsoft Visual Studio Solution File\n");
        File.WriteAllText(Path.Combine(seed, "Platform.Api.sln"), "Microsoft Visual Studio Solution File\n");
        TestRepoWorld.Git(seed, "add", "-A");
        TestRepoWorld.Git(seed, "commit", "-m", "seed");
        var origin = Path.Combine(demo, "origins", "platform.git");
        Directory.CreateDirectory(Path.GetDirectoryName(origin)!);
        TestRepoWorld.Git(demo, "clone", "--bare", seed, origin);

        var clone = Path.Combine(src, "platform");
        TestRepoWorld.Git(src, "clone", origin, clone);
        var worktree = Path.Combine(src, "platform.worktrees", "feature-checkout-flow");
        Directory.CreateDirectory(Path.GetDirectoryName(worktree)!);
        TestRepoWorld.Git(clone, "worktree", "add", "-b", "feature/checkout-flow", worktree);

        var clone2 = Path.Combine(src, "platform-mirror");
        TestRepoWorld.Git(src, "clone", origin, clone2);
        TestRepoWorld.Git(clone2, "switch", "-c", "feature/checkout-flow");
        File.WriteAllText(Path.Combine(worktree, "wip.txt"), "work in progress");   // delete-confirm warning
        TestRepoWorld.Git(clone, "branch", "feature/api-cleanup");   // a ref checked out nowhere → placement offer

        var configDir = Path.Combine(demo, "config");
        Directory.CreateDirectory(configDir);
        var configService = new ConfigService(configDir);
        configService.Save(new AppConfig
        {
            SearchRoots = [src],
            MainBranchNames = ["main", "master"],
            SearchDepth = 6,
            CloseAfterOpen = CloseAfterOpen.Never,
        });

        var services = new FidoServices
        {
            ConfigService = configService,
            Launcher = new FakeEditorLauncher(),
            Dialogs = new FakeDialogService(),
        };

        await Harness.WithWindow(services, async window =>
        {
            var vm = window.Vm();

            async Task CapturePairAsync(string name, Func<Task>? arrange = null)
            {
                foreach (var (theme, suffix) in new[] { (AppTheme.Dark, "dark"), (AppTheme.Light, "light") })
                {
                    App.ApplyTheme(theme);
                    UiTestExtensions.Pump();
                    if (arrange is not null) await arrange();
                    UiTestExtensions.Pump();
                    Screenshots.Save(window, $"{name}-{suffix}");
                }
            }

            // 1. Home screen — the found state in all its glory: cards, hero, grid, log.
            await window.Discover("feature/checkout-flow");
            vm.SelectedTarget = window.CardWithPath("platform.worktrees");
            UiTestExtensions.Pump();
            await CapturePairAsync("home-screen");

            // 2. Discovery results — same state; the gallery crops attention to the cards.
            await CapturePairAsync("open-dialog");

            // 3. Delete row live for the selected worktree.
            await CapturePairAsync("open-dialog-delete");

            // 4. The in-place confirm strip (fresh per theme — arming state, then cancel).
            await CapturePairAsync("delete-worktree-dialog", async () =>
            {
                if (!vm.IsConfirmingDelete) await window.RequestDeleteAsync();
            });
            vm.CancelDeleteConfirm();

            // 5. The placement offer — a branch checked out nowhere: new-worktree + switch cards.
            await window.Discover("feature/api-cleanup");
            await CapturePairAsync("placement-offer");

            // Back to the checked-out branch for the hero shot.
            await window.Discover("feature/checkout-flow");
            vm.SelectedTarget = window.CardWithPath("platform.worktrees");
            UiTestExtensions.Pump();

            // 6. The README hero — "The Eagle has landed", in the redesign's signature warm light.
            App.ApplyTheme(AppTheme.Light);
            UiTestExtensions.Pump();
            await window.OpenWithAsync(new Editor { Name = "WebStorm", Kind = EditorKind.WebStorm });
            UiTestExtensions.Pump();
            Screenshots.Save(window, "the-eagle-has-landed");

            App.ApplyTheme(AppTheme.System);
        });

        // 7. The full settings dialog, both themes.
        var config = configService.Load();
        await Harness.OnUi(async owner =>
        {
            foreach (var (theme, suffix) in new[] { (AppTheme.Dark, "dark"), (AppTheme.Light, "light") })
            {
                App.ApplyTheme(theme);
                var dialog = new SettingsDialog(config, configService);
                var shown = dialog.ShowDialog(owner);
                UiTestExtensions.Pump();
                Screenshots.Save(dialog, $"settings-dialog-{suffix}");
                dialog.Close(false);
                await shown;
            }
            App.ApplyTheme(AppTheme.System);
        });

        ForceDelete(demo);
    }

    private static void ForceDelete(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); }
            catch { /* best effort */ }
        }
        try { Directory.Delete(dir, recursive: true); }
        catch { /* temp dir, best effort */ }
    }
}
