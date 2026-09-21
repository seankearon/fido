using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
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

    /// <summary>
    /// Raised when a link on screen is Ctrl+Clicked, carrying the URL as the terminal read it.
    ///
    /// Forwarded rather than followed here: what to do with a URL is the window's business — it owns the
    /// flight log the answer belongs in, and the rule about which URLs Fido will open at all
    /// (<see cref="Services.UrlLauncher.IsWebUrl"/>) is the same one its pull-request link goes through.
    /// </summary>
    public event EventHandler<string>? UrlClicked;

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

        var shell = RunnerShell.For(command);
        Terminal.LaunchProcess(folder, shell.Executable, shell.Args);
        Terminal.Focus();

        // The control builds its Options — and the emulator that reads them — inside LaunchProcess, so
        // there is nothing to colour until after it returns.
        ApplyPalette();

        // The pane is usually still mid-layout when this runs (the tab was revealed a beat ago), so the
        // terminal sizes its grid against a height it is about to grow out of. A repaint once layout has
        // settled costs a frame.
        //
        // KNOWN ISSUE: under Avalonia.Headless the pane renders blank regardless — the shell runs and the
        // control reports a correct grid (81x8 against real bounds), but nothing is painted. The same
        // control in a top-level window paints fine, so this is specific to the embedded pane and/or the
        // headless renderer, and is NOT yet confirmed either way in a real window. See ConsoleTabTests.
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
    /// A link Ctrl+Clicked in the terminal — hover already underlines one and shows the hand cursor, so
    /// the gesture is the one every other terminal uses and a plain click still selects text.
    ///
    /// The terminal reports two kinds and this treats them alike: text that <em>looks</em> like a URL
    /// (matched as <c>http(s)://…</c>, so a scheme it found is one the user can read on screen), and one
    /// a program <em>declared</em> with an OSC 8 escape — which may point anywhere at all, and needn't
    /// show where. Telling them apart would only change how much the URL is trusted, and Fido trusts
    /// neither: the window checks the scheme before anything is launched, and names what it opened.
    /// </summary>
    private void OnUrlClicked(object? sender, Iciclecreek.Terminal.UrlClickedEventArgs e) =>
        UrlClicked?.Invoke(this, e.Url);

    /// <summary>
    /// Puts Fido's own ANSI palette on the terminal, in place.
    ///
    /// Both <c>Options</c> and its <c>Theme</c> are mutated rather than replaced: the control keeps its
    /// own reference to each and reads through it, so handing it a fresh object leaves it reading the old
    /// one and the renderer draws nothing at all.
    /// </summary>
    private void ApplyPalette()
    {
        if (Terminal.Options?.Theme is not { } theme) return;
        TerminalPalette.Apply(theme, Application.Current?.ActualThemeVariant ?? ThemeVariant.Light);
        Terminal.Options.MinimumContrastRatio = TerminalPalette.MinimumContrast;
    }
}
