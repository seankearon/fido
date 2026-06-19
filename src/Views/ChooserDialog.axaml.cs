using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Fido.ViewModels;

namespace Fido.Views;

/// <summary>Generic single-select dialog. Returns the chosen index (int) or null if cancelled.</summary>
public partial class ChooserDialog : Window
{
    private readonly ChooserViewModel? _vm;

    public ChooserDialog()
    {
        InitializeComponent();
    }

    public ChooserDialog(string windowTitle, string prompt, IReadOnlyList<ChooserItem> items) : this()
    {
        Title = windowTitle;
        _vm = new ChooserViewModel(prompt, items);
        DataContext = _vm;
        Opened += (_, _) => Chooser.Focus();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Accept();
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
    private void OnListDoubleTapped(object? sender, TappedEventArgs e) => Accept();

    private void Accept()
    {
        var index = _vm?.SelectedIndex ?? -1;
        Close(index >= 0 ? index : null);
    }
}
