using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Fido.Models;

namespace Fido;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Views.MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The theme that is on screen right now: the saved preference, unless the header's toggle — or the
    /// Settings dialog's live preview — has moved it for this run.
    ///
    /// Worth keeping separate from <see cref="Models.AppConfig.Theme"/>, because anything that restores a
    /// theme means "put back what was showing", not "put back what is saved": with a session toggle in
    /// play those are two different answers, and reaching for the saved one would quietly undo the toggle.
    /// </summary>
    public static AppTheme CurrentTheme { get; private set; } = AppTheme.System;

    /// <summary>Applies the theme preference; <see cref="AppTheme.System"/> follows the OS.</summary>
    public static void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        Current!.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    /// <summary>
    /// Flips the screen between light and dark, and reports which it landed on. For this run only —
    /// nothing is written to the config, so the next launch is back to the Settings preference.
    ///
    /// The decision is taken from <c>ActualThemeVariant</c> rather than from <see cref="CurrentTheme"/>,
    /// because under <see cref="AppTheme.System"/> the preference doesn't say which of the two is showing:
    /// the OS does. A toggle that read the preference would have to guess, and would need two presses to
    /// leave a system-dark desktop.
    /// </summary>
    public static AppTheme ToggleTheme()
    {
        var next = Current!.ActualThemeVariant == ThemeVariant.Dark ? AppTheme.Light : AppTheme.Dark;
        ApplyTheme(next);
        return next;
    }
}
