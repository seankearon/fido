using Avalonia.Controls;
using Avalonia.Interactivity;
using Fido.Models;

namespace Fido.Views;

/// <summary>
/// Confirmation for permanently deleting a worktree folder from disk after <c>git worktree remove</c> couldn't
/// (typically a path too long for the OS). Returns <c>true</c> only when the user clicks Delete folder; Cancel,
/// Enter, Esc, and the window chrome all return <c>false</c> — the safe default for an irreversible action
/// (Cancel is both the default and the cancel button, and the destructive button is a plain click).
/// </summary>
public partial class ForceDeleteDialog : Window
{
    public ForceDeleteDialog()
    {
        InitializeComponent();
        SystemMenu.EnableAltSpace(this);   // Alt+Space → native system menu
    }

    public ForceDeleteDialog(WorktreeForceDelete request) : this()
    {
        PathText.Text = request.WorktreePath;
        ReasonText.Text = request.Reason;
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
