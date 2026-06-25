using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Fido.Models;
using Fido.Mvvm;

namespace Fido.ViewModels;

/// <summary>Editable copy of the persisted settings, shown in the settings dialog.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private string _searchRootsText = "";
    private string _worktreeRoot = "";
    private AppTheme _selectedTheme = AppTheme.System;
    private CloseAfterOpen _closeAfterOpen = CloseAfterOpen.CommandLine;
    private string _closeAfterOpenDelayText = AppConfig.DefaultCloseAfterOpenDelaySeconds.ToString(CultureInfo.InvariantCulture);

    public string SearchRootsText
    {
        get => _searchRootsText;
        set => SetField(ref _searchRootsText, value);
    }

    /// <summary>The configured editors; exactly one is <see cref="EditorChoice.IsDefault"/> at a time.</summary>
    public ObservableCollection<EditorChoice> Editors { get; } = new();

    /// <summary>Appends a new Custom editor row and makes it the default if it's the only one.</summary>
    public void AddEditor()
    {
        var choice = new EditorChoice(new Editor { Name = "New editor", Kind = EditorKind.Custom }, isDefault: Editors.Count == 0);
        choice.PropertyChanged += OnEditorChoiceChanged;
        Editors.Add(choice);
    }

    /// <summary>Removes an editor row, ensuring a default and at least... nothing — an empty list is allowed.</summary>
    public void RemoveEditor(EditorChoice choice)
    {
        choice.PropertyChanged -= OnEditorChoiceChanged;
        var wasDefault = choice.IsDefault;
        Editors.Remove(choice);
        if (wasDefault && Editors.Count > 0)
            Editors[0].IsDefault = true;   // never leave the list without a default
    }

    // Keep the default single-select: ticking one row clears the others.
    private bool _syncingDefault;
    private void OnEditorChoiceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_syncingDefault || e.PropertyName != nameof(EditorChoice.IsDefault)) return;
        if (sender is not EditorChoice changed || !changed.IsDefault) return;

        _syncingDefault = true;
        foreach (var other in Editors)
            if (!ReferenceEquals(other, changed)) other.IsDefault = false;
        _syncingDefault = false;
    }

    public string WorktreeRoot
    {
        get => _worktreeRoot;
        set => SetField(ref _worktreeRoot, value);
    }

    public AppTheme SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetField(ref _selectedTheme, value))
            {
                OnPropertyChanged(nameof(IsThemeSystem));
                OnPropertyChanged(nameof(IsThemeLight));
                OnPropertyChanged(nameof(IsThemeDark));
            }
        }
    }

    public bool IsThemeSystem
    {
        get => _selectedTheme == AppTheme.System;
        set { if (value) SelectedTheme = AppTheme.System; }
    }

    public bool IsThemeLight
    {
        get => _selectedTheme == AppTheme.Light;
        set { if (value) SelectedTheme = AppTheme.Light; }
    }

    public bool IsThemeDark
    {
        get => _selectedTheme == AppTheme.Dark;
        set { if (value) SelectedTheme = AppTheme.Dark; }
    }

    public CloseAfterOpen CloseAfterOpen
    {
        get => _closeAfterOpen;
        set
        {
            if (SetField(ref _closeAfterOpen, value))
            {
                OnPropertyChanged(nameof(IsCloseCommandLine));
                OnPropertyChanged(nameof(IsCloseAlways));
                OnPropertyChanged(nameof(IsCloseNever));
                OnPropertyChanged(nameof(IsAutoCloseEnabled));
            }
        }
    }

    public bool IsCloseCommandLine
    {
        get => _closeAfterOpen == CloseAfterOpen.CommandLine;
        set { if (value) CloseAfterOpen = CloseAfterOpen.CommandLine; }
    }

    public bool IsCloseAlways
    {
        get => _closeAfterOpen == CloseAfterOpen.Always;
        set { if (value) CloseAfterOpen = CloseAfterOpen.Always; }
    }

    public bool IsCloseNever
    {
        get => _closeAfterOpen == CloseAfterOpen.Never;
        set { if (value) CloseAfterOpen = CloseAfterOpen.Never; }
    }

    /// <summary>False when auto-close is off (<see cref="CloseAfterOpen.Never"/>); disables the delay input.</summary>
    public bool IsAutoCloseEnabled => _closeAfterOpen != CloseAfterOpen.Never;

    /// <summary>
    /// The close delay as edited (seconds). Free text so an in-progress edit isn't clobbered;
    /// <see cref="ApplyTo"/> parses and clamps it. <c>0</c> means close immediately.
    /// </summary>
    public string CloseAfterOpenDelayText
    {
        get => _closeAfterOpenDelayText;
        set => SetField(ref _closeAfterOpenDelayText, value);
    }

    /// <summary>Repos offered when a typed branch isn't checked out anywhere; ticked ones are persisted.</summary>
    public ObservableCollection<RepoChoice> NewBranchRepos { get; } = new();

    public void LoadFrom(AppConfig config)
    {
        SearchRootsText = string.Join(Environment.NewLine, config.SearchRoots);
        WorktreeRoot = config.WorktreeRoot ?? "";

        foreach (var existing in Editors) existing.PropertyChanged -= OnEditorChoiceChanged;
        Editors.Clear();
        var defaultIndex = config.Editors.Count == 0
            ? -1
            : Math.Clamp(config.DefaultEditorIndex, 0, config.Editors.Count - 1);
        for (var i = 0; i < config.Editors.Count; i++)
        {
            var choice = new EditorChoice(config.Editors[i], isDefault: i == defaultIndex);
            choice.PropertyChanged += OnEditorChoiceChanged;
            Editors.Add(choice);
        }
        SelectedTheme = config.Theme;
        CloseAfterOpen = config.CloseAfterOpen;
        CloseAfterOpenDelayText = config.CloseAfterOpenDelaySeconds.ToString(CultureInfo.InvariantCulture);

        NewBranchRepos.Clear();
        foreach (var path in config.NewBranchRepos)
            NewBranchRepos.Add(new RepoChoice(path, isEnabled: true));
    }

    public void ApplyTo(AppConfig config)
    {
        config.SearchRoots = SplitRoots(SearchRootsText);
        config.WorktreeRoot = string.IsNullOrWhiteSpace(WorktreeRoot) ? null : WorktreeRoot.Trim();

        config.Editors = Editors.Select(e => e.ToEditor()).ToList();
        var defaultIndex = -1;
        for (var i = 0; i < Editors.Count; i++)
            if (Editors[i].IsDefault) { defaultIndex = i; break; }
        config.DefaultEditorIndex = defaultIndex < 0 ? 0 : defaultIndex;
        config.RiderPath = null;   // superseded by Editors; clear the migrated legacy value
        config.Theme = SelectedTheme;
        config.CloseAfterOpen = CloseAfterOpen;
        config.CloseAfterOpenDelaySeconds = ParseDelaySeconds(CloseAfterOpenDelayText);
        config.NewBranchRepos = NewBranchRepos.Where(r => r.IsEnabled).Select(r => r.Path).ToList();
    }

    /// <summary>Parses the delay text to whole seconds, clamped to a sane range; unreadable input falls back to the default.</summary>
    private static int ParseDelaySeconds(string text) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? Math.Clamp(seconds, 0, AppConfig.MaxCloseAfterOpenDelaySeconds)
            : AppConfig.DefaultCloseAfterOpenDelaySeconds;

    /// <summary>The search roots as currently typed (honors unsaved edits) — drives repo detection.</summary>
    public IReadOnlyList<string> CurrentSearchRoots() => SplitRoots(SearchRootsText);

    /// <summary>
    /// Folds freshly detected repo paths into the checklist: existing entries keep their tick state,
    /// newly discovered repos are added unticked for the user to opt in.
    /// </summary>
    public void MergeDetected(IEnumerable<string> mainPaths)
    {
        foreach (var path in mainPaths)
        {
            if (NewBranchRepos.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            NewBranchRepos.Add(new RepoChoice(path, isEnabled: false));
        }
    }

    private static List<string> SplitRoots(string text) => text
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();
}
