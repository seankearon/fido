using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Fido.Models;
using Fido.ViewModels;

namespace Fido.Views;

public partial class DecisionDialog : Window
{
    public DecisionDialog()
    {
        InitializeComponent();
    }

    public DecisionDialog(RepositoryInfo repo, string branch, MainContext context) : this()
    {
        DataContext = new DecisionDialogViewModel(repo, branch, context);
    }

    private void OnMainClick(object? sender, RoutedEventArgs e) => Close(OpenDecision.Main);
    private void OnWorktreeClick(object? sender, RoutedEventArgs e) => Close(OpenDecision.Worktree);
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(OpenDecision.Cancel);

    // Raw key handling for robustness (the dialog has no text inputs to interfere).
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.M or Key.D1 or Key.NumPad1:
                Close(OpenDecision.Main);
                e.Handled = true;
                break;
            case Key.W or Key.D2 or Key.NumPad2:
                Close(OpenDecision.Worktree);
                e.Handled = true;
                break;
            case Key.Escape:
                Close(OpenDecision.Cancel);
                e.Handled = true;
                break;
            default:
                base.OnKeyDown(e);
                break;
        }
    }
}
