using System.IO;
using Avalonia.Controls;
using Avalonia.Layout;
using Fido.Models;
using Fido.Services;
using Fido.Tests.Infrastructure;
using Fido.Views;

namespace Fido.Tests.E2E;

/// <summary>
/// Regenerates the documentation gallery under <c>docs/assets/screenshots</c> — not a test of behaviour.
///
/// Gated behind <c>FIDO_GALLERY=1</c> so ordinary runs skip it: it writes the exact file names the
/// documentation references (dark/light pairs per state, plus the README hero), and it must be
/// <strong>run alone</strong>, with <c>FIDO_SCREENSHOT_DIR</c> pointing at the gallery folder:
///
/// <code>
/// dotnet run --project tests/Fido.Tests -- --treenode-filter "/*/*/GalleryScreenshotTests/*"
/// </code>
///
/// Alone for two reasons. No other test's frames should land in the gallery folder — and the Console
/// tab only paints in the <em>first</em> window a headless process shows, so a run sharing a process
/// with the rest of the suite produces an empty console pane (see <c>ConsoleTabTests</c>).
///
/// A readable demo world is built under <c>%TEMP%\fido-demo</c> (short paths beat the usual GUID soup
/// in the card headlines), carrying its own <c>.fido/cfg.yaml</c> and a pair of scripts so the Console
/// tab's run menu has something real in it.
/// </summary>
[NotInParallel]
public class GalleryScreenshotTests
{
    /// <summary>Every state is shot in both themes, in this order — the file-name suffix goes with it.</summary>
    private static readonly (AppTheme Theme, string Suffix)[] Themes =
        [(AppTheme.Dark, "dark"), (AppTheme.Light, "light")];

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
        SeedRepoConfig(seed);
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
            OpenUrl = new FakeBrowser().Open,
            // Scripted, like every other outside world here: the demo's origin is a folder on disk, so a
            // real gh would only ever fail — and the gallery would show the branch's pull-request row as
            // an apology instead of as the feature it is. Answered per branch, so the shots carry both
            // states: feature/checkout-flow has a PR open, feature/api-cleanup doesn't.
            GitHub = new GitHubCli((_, args, _) => Task.FromResult(new ProcessResult(0,
                args.SkipWhile(a => a != "--head").Skip(1).FirstOrDefault() == "feature/checkout-flow"
                    ? """[{"number":128,"title":"Checkout flow: address review","url":"https://github.com/acme/platform/pull/128"}]"""
                    : "[]", ""))),
        };

        await Harness.WithWindow(services, async window =>
        {
            var vm = window.Vm();

            async Task CapturePairAsync(string name, Func<Task>? arrange = null)
            {
                foreach (var (theme, suffix) in Themes)
                {
                    App.ApplyTheme(theme);
                    UiTestExtensions.Pump();
                    if (arrange is not null) await arrange();
                    UiTestExtensions.Pump();
                    FitWindowToContent(window);
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

            // Back to the checked-out branch: the console runs at the selected location.
            await window.Discover("feature/checkout-flow");
            vm.SelectedTarget = window.CardWithPath("platform.worktrees");
            UiTestExtensions.Pump();

            // 6. The Console tab: the branch's own build script, run over a real pseudo-terminal — and
            //    the Run menu that started it. Its own theme loop rather than CapturePairAsync, because
            //    the script has to be re-run for each shot: the emulator takes its colours when the shell
            //    launches and keeps them, so neither the theme nor the palette setting reaches a shell
            //    that is already up.
            var script = window.Vm().ConsoleTabRuns.First(run => run.Label.StartsWith("build."));
            var runMenu = window.FindControl<Button>("ConsoleRunsButton")!;
            var pane = window.FindControl<ConsolePane>("ConsoleView")!;
            foreach (var (theme, suffix) in Themes)
            {
                App.ApplyTheme(theme);
                UiTestExtensions.Pump();

                // The default: the terminal's own scheme, which is what a fresh install shows.
                pane.UseFidoPalette = false;
                await window.RunConsoleOptionAsync(script);
                await SettleAsync(TimeSpan.FromSeconds(12));   // let the shell start, run and paint
                FitWindowToContent(window);
                Screenshots.Save(window, $"console-tab-{suffix}");

                runMenu.Flyout?.ShowAt(runMenu);
                UiTestExtensions.Pump();
                await SettleAsync(TimeSpan.FromSeconds(1));
                FitWindowToContent(window);
                Screenshots.Save(window, $"console-run-menu-{suffix}");
                runMenu.Flyout?.Hide();
                UiTestExtensions.Pump();

                // And the same run with Settings → Theme → "Colour the Console tab to match" ticked.
                pane.UseFidoPalette = true;
                await window.RunConsoleOptionAsync(script);
                await SettleAsync(TimeSpan.FromSeconds(12));
                FitWindowToContent(window);
                Screenshots.Save(window, $"console-palette-{suffix}");
                pane.UseFidoPalette = false;
            }

            // Back to the narration, and rescan so the log holds one clean flight rather than the
            // console run that came before it — every scan starts the poll again.
            vm.IsFlightLogTab = true;
            await window.Discover("feature/checkout-flow");
            vm.SelectedTarget = window.CardWithPath("platform.worktrees");
            UiTestExtensions.Pump();

            // 7. The README hero — "The Eagle has landed", in the redesign's signature warm light.
            App.ApplyTheme(AppTheme.Light);
            UiTestExtensions.Pump();
            await window.OpenWithAsync(new Editor { Name = "WebStorm", Kind = EditorKind.WebStorm });
            UiTestExtensions.Pump();
            FitWindowToContent(window);
            Screenshots.Save(window, "the-eagle-has-landed");

            App.ApplyTheme(AppTheme.System);
        });

        // 8. The full settings dialog, both themes.
        var config = configService.Load();
        await Harness.OnUi(async owner =>
        {
            foreach (var (theme, suffix) in Themes)
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

    /// <summary>
    /// Grows the window until its upper stack fits, so a gallery shot shows the screen whole.
    ///
    /// <para><c>SizeToContent="Height"</c> stops at the screen's working area, and the headless screen is
    /// 1080 tall — so the taller states (the delete confirm, anything carrying the pull-request row) used
    /// to be captured mid-scroll, with the confirm's own buttons sliced off the bottom. The window is
    /// resizable and a real screen is usually taller, so the clipping is the harness's, not Fido's, and
    /// the gallery shouldn't hand it on to the documentation. The overflow is read off the scroller
    /// itself rather than guessed at, and re-read after each growth (a taller window gives the flight log
    /// more room, which can change the sum), so this settles at the height the content actually wants.</para>
    /// </summary>
    private static void FitWindowToContent(MainWindow window)
    {
        var scroller = window.FindControl<ScrollViewer>("MainScroller");
        if (scroller is null) return;

        // Back to the natural height first: growth is one-way, so without this a state would inherit the
        // window a taller one left behind and be shot in a box with room to spare.
        window.ClearValue(Layoutable.HeightProperty);   // an explicit height outlives SizeToContent
        window.SizeToContent = SizeToContent.Height;
        UiTestExtensions.Pump();

        window.SizeToContent = SizeToContent.Manual;   // else the screen clamp undoes every growth
        for (var pass = 0; pass < 6; pass++)
        {
            UiTestExtensions.Pump();
            var overflow = scroller.Extent.Height - scroller.Viewport.Height;
            if (overflow <= 0.5) return;
            // Grown from the height the window actually has: the Height property reads NaN until
            // something sets it, so adding to it would only ever produce NaN.
            window.Height = window.Bounds.Height + Math.Ceiling(overflow);
        }
    }

    /// <summary>
    /// Gives the demo repo the in-repo config a real one would carry, plus the scripts it nominates —
    /// so the Console tab's run menu, the tool button's caret and the OPEN strip's config button are all
    /// showing something real rather than an empty branch's defaults.
    ///
    /// The scripts are written for the platform the gallery is generated on (a <c>.ps1</c> on Windows,
    /// a <c>.sh</c> elsewhere), because the console actually runs one — so the <c>commands</c> list names
    /// that platform's pair too: a <c>build.ps1</c> handed to a Linux box would only ever produce
    /// "pwsh: not found" in the screenshot.
    ///
    /// The build script pauses and then prints a second block. That is for the capture, not for realism:
    /// the terminal emulator writes a line of its own about the process it just launched, and the pause
    /// puts enough output after it that the pane's last few rows — which is all a screenshot of it shows —
    /// are the script's own.
    /// </summary>
    private static void SeedRepoConfig(string repo)
    {
        var scripts = OperatingSystem.IsWindows() ? "build.ps1, test.ps1" : "build.sh, test.sh";
        Directory.CreateDirectory(Path.Combine(repo, ".fido"));
        File.WriteAllText(Path.Combine(repo, ".fido", "cfg.yaml"),
            $"""
            # What this branch offers Fido.
            prefer main clone: false
            commands: [{scripts}, aspire start]   # on the Console run menus, in this order

            """);

        if (OperatingSystem.IsWindows())
        {
            WriteScript(repo, "build.ps1", """
                Write-Host 'platform · build' -ForegroundColor White
                Write-Host '  restore    ' -NoNewline; Write-Host 'ok' -ForegroundColor Green -NoNewline; Write-Host '   1.4s'
                Write-Host '  Platform.Core       ' -NoNewline; Write-Host 'ok' -ForegroundColor Green
                Write-Host '  Platform.Api        ' -NoNewline; Write-Host 'ok' -ForegroundColor Green
                Write-Host '  Platform.Checkout   ' -NoNewline; Write-Host 'ok' -ForegroundColor Green
                Write-Host '  1 warning' -ForegroundColor Yellow
                Write-Host '    CheckoutFlow.cs(42,9): unused variable "retries"' -ForegroundColor Yellow
                Write-Host 'Build succeeded in 6.8s' -ForegroundColor Green
                Start-Sleep -Seconds 2
                Write-Host ''
                Write-Host 'platform · test' -ForegroundColor White
                Write-Host '  Platform.Core.Tests       ' -NoNewline; Write-Host '12 passed' -ForegroundColor Green
                Write-Host '  Platform.Api.Tests        ' -NoNewline; Write-Host '31 passed' -ForegroundColor Green
                Write-Host '  Platform.Checkout.Tests   ' -NoNewline; Write-Host '9 passed' -ForegroundColor Green
                Write-Host 'All 52 tests passed in 3.1s' -ForegroundColor Green
                """);
            WriteScript(repo, "test.ps1", "Write-Host '48 passed, 0 failed' -ForegroundColor Green\n");
        }
        else
        {
            WriteScript(repo, "build.sh", """
                #!/usr/bin/env bash
                printf 'platform · build\n'
                printf '  restore             \033[32mok\033[0m   1.4s\n'
                printf '  Platform.Core       \033[32mok\033[0m\n'
                printf '  Platform.Api        \033[32mok\033[0m\n'
                printf '  Platform.Checkout   \033[32mok\033[0m\n'
                printf '\033[33m  1 warning\033[0m\n'
                printf '\033[33m    CheckoutFlow.cs(42,9): unused variable "retries"\033[0m\n'
                printf '\033[32mBuild succeeded in 6.8s\033[0m\n'
                sleep 2
                printf '\nplatform · test\n'
                printf '  Platform.Core.Tests       \033[32m12 passed\033[0m\n'
                printf '  Platform.Api.Tests        \033[32m31 passed\033[0m\n'
                printf '  Platform.Checkout.Tests   \033[32m9 passed\033[0m\n'
                printf '\033[32mAll 52 tests passed in 3.1s\033[0m\n'
                """);
            WriteScript(repo, "test.sh", "#!/usr/bin/env bash\nprintf '48 passed, 0 failed\\n'\n");
        }
    }

    /// <summary>Writes a script, executable on Unix so the console can invoke it as <c>./name</c>.</summary>
    private static void WriteScript(string repo, string name, string body)
    {
        var path = Path.Combine(repo, name);
        File.WriteAllText(path, body);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    /// <summary>
    /// Keeps the UI thread pumping for <paramref name="budget"/>. A shell over a pseudo-terminal is a
    /// child process on the other side of a pipe: nothing about it is synchronous with the capture, so
    /// the only honest wait is to keep the dispatcher running and let the frames arrive.
    /// </summary>
    private static async Task SettleAsync(TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            UiTestExtensions.Pump();
            await Task.Delay(100);
        }
        UiTestExtensions.Pump();
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
