using Avalonia.Controls;
using Fido.Models;
using Fido.ViewModels;

namespace Fido.Views;

/// <summary>
/// Confirmation for the destructive "delete this worktree" action. Returns the user's
/// <see cref="WorktreeDeletionChoice"/> (which targets to delete) when they click Delete; Cancel, Enter, Esc,
/// and the window chrome all return <c>null</c> — the safe default for an irreversible action (Cancel is both
/// the default and the cancel button).
/// </summary>
public partial class DeleteWorktreeDialog : Window
{
    private readonly DeleteWorktreeDialogViewModel? _vm;

    public DeleteWorktreeDialog()
    {
        InitializeComponent();
        SystemMenu.EnableAltSpace(this);   // Alt+Space → native system menu
    }

    public DeleteWorktreeDialog(WorktreeDeletion plan) : this()
    {
        _vm = new DeleteWorktreeDialogViewModel(plan);
        DataContext = _vm;
    }

    private void OnDeleteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close(_vm?.ToChoice());

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close((WorktreeDeletionChoice?)null);
}
