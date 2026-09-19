using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Fido.Services;

namespace Fido.Views;

/// <summary>
/// Fido's own console: the branch's run file (or <c>aspire start</c>) executed at the selected worktree,
/// in a window Fido owns, over a real pseudo-terminal.
///
/// The PTY is what makes this a runner rather than a log pane. A redirected pipe would be simpler, but
/// every tool that checks <c>isatty</c> — which is most of them — drops its colour and its progress
/// rendering, and anything that prompts would hang with nowhere to type (the failure
/// ProcessRunner's credential-prompt suppression exists to dodge on the git path). With a terminal
/// underneath, the script behaves exactly as it does in the user's own shell, and <c>Ctrl+C</c> reaches it.
///
/// <c>CloseOnProcessExit</c> is deliberately off: the window's whole job is that the output is still
/// there after a script fails. The shell stays interactive underneath it (see <see cref="RunnerShell"/>).
/// </summary>
public partial class RunnerWindow : Window
{
    private readonly Action? _handOff;

    /// <summary>Whether <see cref="OnOpened"/> got as far as starting the shell — see <see cref="OnClosed"/>.</summary>
    private bool _launched;

    public RunnerWindow()
    {
        InitializeComponent();
        SystemMenu.EnableAltSpace(this);   // Alt+Space → native system menu, as every Fido window does
    }

    /// <param name="branch">The scanned branch, for the header chip.</param>
    /// <param name="folder">The working tree the shell opens at.</param>
    /// <param name="command">The run-menu pick, or null for a plain shell at the folder.</param>
    /// <param name="handOff">Re-runs the same command in the user's configured terminal; null hides the button.</param>
    public RunnerWindow(string branch, string folder, string? command, Action? handOff = null) : this()
    {
        _handOff = handOff;
        Title = command is null ? $"Fido — {folder}" : $"Fido — {command}";
        BranchChip.Text = branch;
        CommandText.Text = command ?? "shell";
        FolderText.Text = folder;
        HandOffButton.IsVisible = handOff is not null;

        var shell = RunnerShell.For(command);
        Terminal.StartingDirectory = folder;
        Terminal.Process = shell.Executable;
        Terminal.ProcessArgs = shell.Args;

        // TERM is what the shell and everything it runs read to decide what escape sequences they may
        // emit. The emulator handles 256 colour, so say so rather than letting a tool guess "dumb" and
        // fall back to no colour at all.
        Terminal.EnvironmentVariables = new Dictionary<string, string>
        {
            ["TERM"] = "xterm-256color",
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // Launching after the window is up means the PTY is sized from a laid-out control, so the shell's
        // first prompt is drawn at the real width instead of an 80x24 default it would have to reflow.
        Terminal.LaunchProcess();
        _launched = true;
        Terminal.Focus();
    }

    /// <summary>
    /// The shell exited — the user typed <c>exit</c>, or it died. The window stays: the point of running
    /// here rather than in a terminal that closes is that the output outlives the process.
    /// </summary>
    private void OnProcessExited(object? sender, Iciclecreek.Terminal.ProcessExitedEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            StatePill.Classes.Remove("scan");
            StatePill.Classes.Add("idle");
            StateText.Text = "exited";
        });

    private void OnHandOffClick(object? sender, RoutedEventArgs e)
    {
        _handOff?.Invoke();
        Close();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Closing the window must take the shell with it. The PTY holds a live child process and Fido may
    /// well be about to exit itself; an orphaned pwsh with no terminal attached would linger.
    ///
    /// Guarded twice over, because this is teardown and there is no useful way to fail here. The window is
    /// shown owned by the main window, so closing Fido closes this one too — possibly before
    /// <see cref="OnOpened"/> ever ran, leaving nothing to kill. <see cref="_launched"/> covers that;
    /// the catch covers a shell that has already exited, which the control reports by throwing.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        if (_launched)
        {
            try { Terminal.Kill(); }
            catch (Exception) { /* already gone, or never fully started — nothing left to do */ }
        }
        base.OnClosed(e);
    }
}
