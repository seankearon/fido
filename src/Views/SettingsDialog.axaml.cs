using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Fido.Models;
using Fido.Services;
using Fido.ViewModels;

namespace Fido.Views;

public partial class SettingsDialog : Window
{
    private readonly SettingsViewModel _vm = new();
    private readonly AppConfig _config = null!;
    private readonly ConfigService _configService = null!;
    private readonly AppTheme _originalTheme;
    private bool _saved;

    public SettingsDialog()
    {
        InitializeComponent();
    }

    public SettingsDialog(AppConfig config, ConfigService configService) : this()
    {
        _config = config;
        _configService = configService;
        _originalTheme = config.Theme;

        _vm.LoadFrom(config);
        DataContext = _vm;

        // Live-preview theme changes; the initial load happened before this subscription.
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SelectedTheme))
            App.ApplyTheme(_vm.SelectedTheme);
    }

    // Detect git repos under the currently-entered search roots and fold them into the checklist.
    private async void OnDetectClick(object? sender, RoutedEventArgs e)
    {
        DetectReposButton.IsEnabled = false;
        try
        {
            var cfg = new AppConfig
            {
                SearchRoots = _vm.CurrentSearchRoots().ToList(),
                SearchDepth = _config.SearchDepth,
            };
            var opener = new OpenerService(new GitService(), new SolutionFinder(), new WorkingTreeFinder());

            // The directory walk is synchronous — keep it off the UI thread; the continuation
            // resumes here on the UI thread, so mutating the bound collection is safe.
            var repos = await Task.Run(() => opener.FindAllRepositoriesAsync(cfg));
            _vm.MergeDetected(repos.Select(r => r.MainWorktreePath));
        }
        catch
        {
            // Best-effort detection; leave the existing list untouched on failure.
        }
        finally
        {
            DetectReposButton.IsEnabled = true;
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        _vm.ApplyTo(_config);
        try
        {
            _configService.Save(_config);
        }
        catch
        {
            // best-effort; theme/preview still applies
        }
        App.ApplyTheme(_config.Theme);
        _saved = true;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    protected override void OnClosed(EventArgs e)
    {
        // Revert the live theme preview if the dialog was dismissed without saving.
        if (!_saved)
            App.ApplyTheme(_originalTheme);
        base.OnClosed(e);
    }
}
