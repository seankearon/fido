using Avalonia.Controls;
using Fido.Models;
using Fido.ViewModels;

namespace Fido.Views;

/// <summary>
/// Confirmation for the destructive "delete this worktree" action. Returns <c>true</c> only when the user
/// clicks Delete; Cancel, Enter, Esc, and the window chrome all return <c>false</c> — the safe default for
/// an irreversible action (Cancel is both the default and the cancel button).
/// </summary>
public partial class DeleteWorktreeDialog : Window
{
    public DeleteWorktreeDialog()
    {
        InitializeComponent();
        SystemMenu.EnableAltSpace(this);   // Alt+Space → native system menu
    }

    public DeleteWorktreeDialog(WorktreeDeletion plan) : this()
    {
        DataContext = new DeleteWorktreeDialogViewModel(plan);
    }

    private void OnDeleteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(true);
    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(false);
}
