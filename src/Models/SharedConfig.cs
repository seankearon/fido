using System.Collections.Generic;

namespace Fido.Models;

/// <summary>
/// The part of <see cref="AppConfig"/> that means the same on every machine — the tools, the shortcuts and
/// the preferences — written to <c>config.json</c> in <see cref="AppConfig.SharedDirectory"/> when one is set,
/// so a synced folder carries them between machines. What names this machine's disks (search roots, the
/// worktree root, the new-branch repos) and what records this machine's use (recent branches and solutions)
/// is deliberately absent: it stays in the local file, as does the pointer to the shared folder itself.
/// </summary>
public sealed class SharedConfig
{
    public List<Editor> Editors { get; set; } = new();
    public int DefaultEditorIndex { get; set; }
    public Dictionary<string, string> Shortcuts { get; set; } = new();
    public List<string> MainBranchNames { get; set; } = new();
    public AppTheme Theme { get; set; } = AppTheme.System;
    public bool ShowTargetInWindowTitle { get; set; } = true;
    public bool PullBeforeRun { get; set; } = true;
    public bool RunInFido { get; set; }
    public bool ConsoleUsesFidoPalette { get; set; }
    public bool SwitchToOpenWindow { get; set; } = true;
    public CloseAfterOpen CloseAfterOpen { get; set; } = CloseAfterOpen.CommandLine;
    public int CloseAfterOpenDelaySeconds { get; set; } = AppConfig.DefaultCloseAfterOpenDelaySeconds;

    /// <summary>The shared part of <paramref name="config"/>, as it would be written.</summary>
    public static SharedConfig From(AppConfig config) => new()
    {
        Editors = config.Editors,
        DefaultEditorIndex = config.DefaultEditorIndex,
        Shortcuts = config.Shortcuts,
        MainBranchNames = config.MainBranchNames,
        Theme = config.Theme,
        ShowTargetInWindowTitle = config.ShowTargetInWindowTitle,
        PullBeforeRun = config.PullBeforeRun,
        RunInFido = config.RunInFido,
        ConsoleUsesFidoPalette = config.ConsoleUsesFidoPalette,
        SwitchToOpenWindow = config.SwitchToOpenWindow,
        CloseAfterOpen = config.CloseAfterOpen,
        CloseAfterOpenDelaySeconds = config.CloseAfterOpenDelaySeconds,
    };

    /// <summary>Puts these values over <paramref name="config"/>'s own, leaving the local-only ones alone.
    /// Null lists from a hand edit are skipped, so the local value stands rather than an empty one.</summary>
    public void ApplyTo(AppConfig config)
    {
        if (Editors is { Count: > 0 }) config.Editors = Editors;
        config.DefaultEditorIndex = DefaultEditorIndex;
        if (Shortcuts is not null) config.Shortcuts = Shortcuts;
        if (MainBranchNames is { Count: > 0 }) config.MainBranchNames = MainBranchNames;
        config.Theme = Theme;
        config.ShowTargetInWindowTitle = ShowTargetInWindowTitle;
        config.PullBeforeRun = PullBeforeRun;
        config.RunInFido = RunInFido;
        config.ConsoleUsesFidoPalette = ConsoleUsesFidoPalette;
        config.SwitchToOpenWindow = SwitchToOpenWindow;
        config.CloseAfterOpen = CloseAfterOpen;
        config.CloseAfterOpenDelaySeconds = CloseAfterOpenDelaySeconds;
    }
}
