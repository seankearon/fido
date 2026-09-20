using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Fido.Services;
using Fido.Theme;

namespace Fido.Views;

/// <summary>
/// The Console tab's terminal: a real shell, over a real pseudo-terminal, at the selected location.
///
/// A PTY rather than redirected pipes, because the runner's value is the output. Every tool that checks
/// <c>isatty</c> — which is most of them — drops its colour and progress rendering over a pipe, and
/// anything that prompts hangs with nowhere to type (the trap ProcessRunner's credential-prompt
/// suppression exists to dodge on the git path). With a terminal underneath, a script behaves as it does
/// in the user's own shell and <c>Ctrl+C</c> reaches it.
///
/// The shell stays interactive after a command finishes (see <see cref="RunnerShell"/>), so a failed
/// script leaves its output on screen with a live prompt beneath it rather than vanishing with an exit
/// code — which is the whole reason for running it here rather than in a window that closes.
/// </summary>
public partial class ConsolePane : UserControl
{
    /// <summary>Raised when the shell exits, so the host can reflect that.</summary>
    public event EventHandler? ProcessExited;

    private bool _started;

    public ConsolePane()
    {
        InitializeComponent();

        // The palette is per theme variant, and Fido's default follows the OS — so it can change while
        // the app is running, not just when the user picks one in Settings.
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += (_, _) => ApplyPalette();
    }

    /// <summary>Whether a shell has been started (and so whether there is anything to kill or re-run).</summary>
    public bool IsRunning => _started;

    /// <summary>
    /// Starts a plain shell at <paramref name="folder"/> if nothing is running yet. Called when the
    /// Console tab is opened: the tab is what asks for a shell, so merely having the tab present costs
    /// nothing.
    /// </summary>
    public void EnsureStarted(string? folder)
    {
        if (_started || string.IsNullOrEmpty(folder)) return;
        Run(folder, null);
    }

    /// <summary>
    /// Runs <paramref name="command"/> at <paramref name="folder"/>, replacing whatever was running. A
    /// fresh shell each time rather than typing into the live one: the folder may have changed with the
    /// selection, and a shell left mid-command — or sitting in a pager — would swallow the input.
    /// </summary>
    public void Run(string folder, string? command)
    {
        if (string.IsNullOrEmpty(folder)) return;

        if (_started)
        {
            try { Terminal.Kill(); }
            catch (Exception) { /* already exited — the relaunch below is what matters */ }
        }

        // TERM is what the shell and everything it runs read to decide which escape sequences they may
        // emit. The emulator handles 256 colour, so say so rather than letting a tool guess "dumb" and
        // drop its colour entirely.
        Terminal.EnvironmentVariables = new Dictionary<string, string> { ["TERM"] = "xterm-256color" };

        Placeholder.IsVisible = false;
        Terminal.IsVisible = true;
        _started = true;

        // Colour it *before* the shell starts. The emulator takes its colours when the process is
        // launched and keeps that copy: a palette applied afterwards leaves Options.Theme holding
        // Fido's values while the screen still paints xterm's stock black. Measured, not assumed —
        // see ConsoleTabTests.The_console_takes_Fidos_palette_before_the_shell_starts.
        ApplyPalette();

        var shell = RunnerShell.For(command);
        Terminal.LaunchProcess(folder, shell.Executable, shell.Args);
        Terminal.Focus();

        // The pane is usually still mid-layout when this runs (the tab was revealed a beat ago), so the
        // terminal sizes its grid against a height it is about to grow out of. A repaint once layout has
        // settled costs a frame.
        Dispatcher.UIThread.Post(() =>
        {
            Terminal.Refresh();
        }, DispatcherPriority.Background);
    }

    /// <summary>Stops the shell, if one is running. Idempotent — the host calls it on window close.</summary>
    public void Stop()
    {
        if (!_started) return;
        _started = false;
        try { Terminal.Kill(); }
        catch (Exception) { /* already gone, or never fully started — nothing left to do */ }
    }

    private void OnProcessExited(object? sender, Iciclecreek.Terminal.ProcessExitedEventArgs e) =>
        Dispatcher.UIThread.Post(() => ProcessExited?.Invoke(this, EventArgs.Empty));

    /// <summary>
    /// Puts Fido's own ANSI palette on the terminal, ready for the next shell.
    ///
    /// Two things have to happen together, and both are the result of measurement rather than the docs:
    ///
    /// <list type="number">
    /// <item><description>Both <c>Options</c> and its <c>Theme</c> are mutated rather than replaced. The
    /// control keeps its own reference to each and reads through it, so handing it a fresh object leaves
    /// it reading the old one and the renderer draws nothing at all.</description></item>
    /// <item><description>The control's own <c>Background</c>/<c>Foreground</c> brushes are set to match.
    /// The emulator seeds itself from those when a process launches, and with them left at their defaults
    /// it ignores <c>Options.Theme</c> entirely — stock black ground, stock colours — however carefully
    /// that object was filled in. Both come out of the same <see cref="TerminalPalette"/> lookup, so the
    /// seeds and the palette can't disagree.</description></item>
    /// </list>
    ///
    /// Called before <see cref="Terminal"/> launches anything, because that is when the colours are taken.
    /// A shell already running keeps the palette it started with; the next run picks the new one up, which
    /// is why a theme change here only repaints the pane's own ground.
    /// </summary>
    private void ApplyPalette()
    {
        var variant = Application.Current?.ActualThemeVariant ?? ThemeVariant.Light;
        var palette = TerminalPalette.For(variant);

        // The brushes first, and unconditionally: they are what the emulator seeds itself from, and they
        // are on the control rather than on anything it builds later.
        Terminal.Background = Brush(palette.Background) ?? Terminal.Background;
        Terminal.Foreground = Brush(palette.Foreground) ?? Terminal.Foreground;

        // A pane that has never been shown has never been measured, and an unmeasured templated control
        // has no Options to colour. Applying the template here costs nothing when it already has one and
        // means a run started from the flight-log tab is coloured like any other.
        Terminal.ApplyTemplate();

        if (Terminal.Options?.Theme is not { } theme) return;
        TerminalPalette.Apply(theme, variant);
        Terminal.Options.MinimumContrastRatio = TerminalPalette.MinimumContrast;
    }

    /// <summary>A brush for a palette entry, or null for one the palette somehow left unset.</summary>
    private static IBrush? Brush(string? hex) =>
        Color.TryParse(hex, out var colour) ? new SolidColorBrush(colour) : null;
}
