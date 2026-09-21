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

    /// <summary>
    /// Raised when a link on screen is Ctrl+Clicked, carrying the URL as the terminal read it.
    ///
    /// Forwarded rather than followed here: what to do with a URL is the window's business — it owns the
    /// flight log the answer belongs in, and the rule about which URLs Fido will open at all
    /// (<see cref="Services.UrlLauncher.IsWebUrl"/>) is the same one its pull-request link goes through.
    /// </summary>
    public event EventHandler<string>? UrlClicked;

    private bool _started;
    private bool _useFidoPalette;

    /// <summary>
    /// The emulator's own palette, taken the first time it is there to take and never written to again.
    ///
    /// Everything here mutates one live <c>ThemeOptions</c> instance (see <see cref="ApplyPalette"/>), so
    /// once Fido's colours have gone on, the emulator's are gone unless they were kept. This is that copy,
    /// and it is what turning the setting back off restores.
    /// </summary>
    private XTerm.Options.ThemeOptions? _stockTheme;

    public ConsolePane()
    {
        InitializeComponent();

        // The palette is per theme variant — and the variant moves under the pane's feet: Fido's default
        // follows the OS, the Settings dialog previews live, and the header's toggle flips it outright.
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += (_, _) => ApplyPalette();
    }

    /// <summary>
    /// Colours the pane as soon as it is in the tree, whichever mode it is in.
    ///
    /// Needed because "not Fido's palette" is now a scheme of its own rather than an absence of one: a Fido
    /// that starts light, with the setting off and nobody touching the theme, would otherwise never paint
    /// the console at all and leave the control on its built-in black. Attachment rather than the
    /// constructor, because the template — and so <c>Options</c> — only resolves once the control has a
    /// place in the tree.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyPalette();
    }

    /// <summary>
    /// Whether the terminal wears Fido's palette rather than the emulator's own sixteen colours
    /// (<see cref="Models.AppConfig.ConsoleUsesFidoPalette"/>, off by default). The host sets it from the
    /// config at startup and again whenever settings are saved.
    ///
    /// Either way the ground follows the theme — see <see cref="ApplyPalette"/>. Turning the setting off
    /// puts the emulator's own sixteen back, rather than leaving Fido's last word sitting in the theme.
    /// </summary>
    public bool UseFidoPalette
    {
        get => _useFidoPalette;
        set
        {
            if (_useFidoPalette == value) return;
            _useFidoPalette = value;
            ApplyPalette();
        }
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

        // Settle the colours *before* the shell starts. The emulator seeds itself from the control's
        // brushes at launch, so doing this after would paint the first frames in whatever it defaulted to
        // and correct them a beat later. Measured, not assumed — see
        // ConsoleTabTests.The_console_takes_Fidos_palette_before_the_shell_starts.
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
    /// Colours the terminal for the theme on screen. Both modes are theme-aware:
    ///
    /// <list type="bullet">
    /// <item><description><see cref="UseFidoPalette"/> on — Fido's own ground, ink and sixteen ANSI
    /// colours, from <see cref="TerminalPalette.Apply"/>.</description></item>
    /// <item><description>Off, the default — the terminal's own black-and-white, the right way up for the
    /// theme (<see cref="TerminalPalette.ApplyPlain"/>), with its sixteen colours left exactly as it ships
    /// them. The setting chooses between <em>plain</em> and <em>Fido's</em>, not between
    /// <em>fixed</em> and <em>follows the theme</em>: a black box sitting in a cream window doesn't read
    /// as a plain console, it reads as a bug.</description></item>
    /// </list>
    ///
    /// Two mechanical details, both measured rather than read in the docs:
    ///
    /// <list type="number">
    /// <item><description>Both <c>Options</c> and its <c>Theme</c> are mutated rather than replaced. The
    /// control keeps its own reference to each and reads through it, so handing it a fresh object leaves
    /// it reading the old one and the renderer draws nothing at all. That same live reference is what lets
    /// a <em>running</em> shell repaint on a theme change rather than waiting for the next
    /// one.</description></item>
    /// <item><description>The control's own <c>Background</c>/<c>Foreground</c> brushes are set to match.
    /// The emulator seeds itself from those when a process launches, and with them left at their defaults
    /// it ignores <c>Options.Theme</c> entirely — stock black ground, stock colours — however carefully
    /// that object was filled in. Both come out of the same <see cref="TerminalPalette"/> lookup, so the
    /// seeds and the palette can't disagree.</description></item>
    /// </list>
    ///
    /// <see cref="TerminalPalette.MinimumContrast"/> goes on in both modes. It matters most in the plain
    /// one on a light theme: those sixteen colours were chosen against black, and the floor is the only
    /// thing standing between them and an unreadable pale ground.
    /// </summary>
    private void ApplyPalette()
    {
        var variant = Application.Current?.ActualThemeVariant ?? ThemeVariant.Light;
        var fido = TerminalPalette.For(variant);
        var (background, foreground) = _useFidoPalette
            ? (fido.Background, fido.Foreground)
            : TerminalPalette.Plain(variant);

        // The brushes first, and unconditionally: they are what the emulator seeds itself from, and they
        // are on the control rather than on anything it builds later.
        Terminal.Background = Brush(background) ?? Terminal.Background;
        Terminal.Foreground = Brush(foreground) ?? Terminal.Foreground;

        // A pane that has never been shown has never been measured, and an unmeasured templated control
        // has no Options to colour. Applying the template here costs nothing when it already has one and
        // means a run started from the flight-log tab is coloured like any other.
        Terminal.ApplyTemplate();

        if (Terminal.Options?.Theme is not { } theme) return;

        // Before the first write, not after: this is the only moment the emulator's own colours are still
        // in there to be kept.
        _stockTheme ??= TerminalPalette.Snapshot(theme);

        if (_useFidoPalette)
            TerminalPalette.Apply(theme, variant);
        else
            TerminalPalette.ApplyPlain(theme, variant, _stockTheme);

        Terminal.Options.MinimumContrastRatio = TerminalPalette.MinimumContrast;
    }

    /// <summary>A brush for a palette entry, or null for one the palette somehow left unset.</summary>
    private static IBrush? Brush(string? hex) =>
        Color.TryParse(hex, out var colour) ? new SolidColorBrush(colour) : null;
}
