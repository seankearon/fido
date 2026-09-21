using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Fido;
using Fido.Models;
using Fido.Tests.Infrastructure;
using Fido.Theme;
using Fido.Views;

namespace Fido.Tests.E2E;

/// <summary>
/// The header's sun/moon toggle: light ↔ dark for the screen in front of you, and nothing else.
///
/// The point of it is what it does *not* do — the saved preference is the default and stays the default,
/// so a flip lives and dies with the window. That is the half a user can't see, and so the half these
/// tests spend most of their time on.
/// </summary>
[NotInParallel]
public class ThemeToggleTests
{
    [Test]
    public async Task The_toggle_flips_the_screen_and_flips_it_back()
    {
        using var world = new TestRepoWorld();
        var services = world.BuildServices([world.SearchRoot("root")], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            App.ApplyTheme(AppTheme.Light);
            UiTestExtensions.Pump();
            Screenshots.Save(window, "theme-toggle-light");

            window.ClickButton("ThemeToggleButton");
            await Assert.That(Application.Current!.ActualThemeVariant).IsEqualTo(ThemeVariant.Dark);
            Screenshots.Save(window, "theme-toggle-dark");

            window.ClickButton("ThemeToggleButton");
            await Assert.That(Application.Current!.ActualThemeVariant).IsEqualTo(ThemeVariant.Light);

            App.ApplyTheme(AppTheme.System);
        });
    }

    /// <summary>
    /// A press moves the screen, not the config. Nothing is written — so the preference the Settings
    /// dialog shows, and the one the next launch reads, is the one that was there before the press.
    /// </summary>
    [Test]
    public async Task The_toggle_leaves_the_saved_default_alone()
    {
        using var world = new TestRepoWorld();
        var services = world.BuildServices([world.SearchRoot("root")], new FakeEditorLauncher(), new FakeDialogService());
        await Assert.That(services.ConfigService.Load().Theme).IsEqualTo(AppTheme.System);   // out of the box

        await Harness.WithWindow(services, async window =>
        {
            App.ApplyTheme(AppTheme.Light);
            UiTestExtensions.Pump();

            window.ClickButton("ThemeToggleButton");

            await Assert.That(App.CurrentTheme).IsEqualTo(AppTheme.Dark);            // the screen moved
            await Assert.That(services.ConfigService.Load().Theme)                   // …and the default did not
                .IsEqualTo(AppTheme.System);

            App.ApplyTheme(AppTheme.System);
        });
    }

    /// <summary>
    /// Under <see cref="AppTheme.System"/> the preference doesn't say which theme is showing — the OS
    /// does. So the toggle reads the variant on screen, and one press is always enough to leave it.
    /// </summary>
    [Test]
    public async Task One_press_is_enough_to_leave_the_system_theme()
    {
        using var world = new TestRepoWorld();
        var services = world.BuildServices([world.SearchRoot("root")], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            App.ApplyTheme(AppTheme.System);
            UiTestExtensions.Pump();
            var showing = Application.Current!.ActualThemeVariant;

            window.ClickButton("ThemeToggleButton");

            await Assert.That(Application.Current!.ActualThemeVariant).IsNotEqualTo(showing);
            App.ApplyTheme(AppTheme.System);
        });
    }

    /// <summary>
    /// The Console tab goes with it, in Fido's palette. The pane watches the application's variant (it has
    /// to — Fido's default follows the OS, which can change mid-run), so the toggle reaches the terminal by
    /// the same road a system theme change does.
    /// </summary>
    [Test]
    public async Task The_console_tab_follows_the_toggle_in_Fidos_palette()
    {
        using var world = new TestRepoWorld();
        var services = world.BuildServices([world.SearchRoot("root")], new FakeEditorLauncher(), new FakeDialogService(),
            consoleUsesFidoPalette: true);

        await Harness.WithWindow(services, async window =>
        {
            App.ApplyTheme(AppTheme.Light);
            UiTestExtensions.Pump();

            var terminal = window.FindControl<ConsolePane>("ConsoleView")!
                .FindControl<Iciclecreek.Terminal.TerminalControl>("Terminal")!;
            await Assert.That((terminal.Background as ISolidColorBrush)?.Color)
                .IsEqualTo(Color.Parse(TerminalPalette.For(ThemeVariant.Light).Background!));

            window.ClickButton("ThemeToggleButton");
            UiTestExtensions.Pump();

            await Assert.That((terminal.Background as ISolidColorBrush)?.Color)
                .IsEqualTo(Color.Parse(TerminalPalette.For(ThemeVariant.Dark).Background!));
            await Assert.That((terminal.Foreground as ISolidColorBrush)?.Color)
                .IsEqualTo(Color.Parse(TerminalPalette.For(ThemeVariant.Dark).Foreground!));

            App.ApplyTheme(AppTheme.System);
        });
    }

    /// <summary>
    /// …and it goes with it on the default setting too, where the console wears the plain scheme. That is
    /// the case that matters most: with Fido's palette off — which is how Fido ships — a console that did
    /// not follow would leave a black box sitting in a cream window every time the toggle was pressed.
    /// </summary>
    [Test]
    public async Task The_console_tab_follows_the_toggle_in_the_plain_scheme_too()
    {
        using var world = new TestRepoWorld();
        var services = world.BuildServices([world.SearchRoot("root")], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            App.ApplyTheme(AppTheme.Light);
            UiTestExtensions.Pump();

            var pane = window.FindControl<ConsolePane>("ConsoleView")!;
            var terminal = pane.FindControl<Iciclecreek.Terminal.TerminalControl>("Terminal")!;
            await Assert.That(pane.UseFidoPalette).IsFalse();   // the shipped default
            await Assert.That((terminal.Background as ISolidColorBrush)?.Color).IsEqualTo(Colors.White);

            window.ClickButton("ThemeToggleButton");
            UiTestExtensions.Pump();

            await Assert.That((terminal.Background as ISolidColorBrush)?.Color).IsEqualTo(Colors.Black);
            await Assert.That((terminal.Foreground as ISolidColorBrush)?.Color).IsEqualTo(Colors.White);

            window.ClickButton("ThemeToggleButton");
            UiTestExtensions.Pump();

            await Assert.That((terminal.Background as ISolidColorBrush)?.Color).IsEqualTo(Colors.White);

            App.ApplyTheme(AppTheme.System);
        });
    }

    /// <summary>
    /// Where it sits: in the header, on the same row as the gear and to its left — the place the request
    /// named, and the one that keeps the two window-level controls together instead of scattering them.
    /// </summary>
    [Test]
    public async Task The_toggle_sits_in_the_header_to_the_left_of_the_gear()
    {
        using var world = new TestRepoWorld();
        var services = world.BuildServices([world.SearchRoot("root")], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            var toggle = window.FindControl<Button>("ThemeToggleButton")!;
            var gear = window.FindControl<Button>("GearButton")!;

            await Assert.That(toggle.IsVisible).IsTrue();
            await Assert.That(toggle.Bounds.Width).IsGreaterThan(0);
            await Assert.That(toggle.Bounds.Right).IsLessThanOrEqualTo(gear.Bounds.Left);
            await Assert.That(toggle.Bounds.Top).IsEqualTo(gear.Bounds.Top);
        });
    }

    /// <summary>
    /// A cancelled Settings dialog puts back what was on screen, not what is saved. Without that, opening
    /// Settings and backing out would quietly undo the toggle — the one move a cancel is supposed never to
    /// make.
    /// </summary>
    [Test]
    public async Task Cancelling_settings_keeps_the_toggled_theme()
    {
        using var world = new TestRepoWorld();
        var services = world.BuildServices([world.SearchRoot("root")], new FakeEditorLauncher(), new FakeDialogService());
        var config = services.ConfigService.Load();
        config.Theme = AppTheme.Light;
        services.ConfigService.Save(config);

        await Harness.OnUi(async owner =>
        {
            App.ApplyTheme(AppTheme.Light);
            App.ToggleTheme();                                  // …as the header's button would
            await Assert.That(App.CurrentTheme).IsEqualTo(AppTheme.Dark);

            var dialog = new SettingsDialog(config, services.ConfigService);
            var resultTask = dialog.ShowDialog(owner);
            UiTestExtensions.Pump();

            dialog.ClickButton("CancelButton");
            await resultTask;

            await Assert.That(Application.Current!.ActualThemeVariant).IsEqualTo(ThemeVariant.Dark);
            App.ApplyTheme(AppTheme.System);
        });
    }
}
