using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Fido.ViewModels;

namespace Fido.Views;

/// <summary>Generic single-select dialog. Returns the chosen index (int) or null if cancelled.</summary>
public partial class ChooserDialog : Window
{
    /// <summary>
    /// Close value returned when the optional delete action is chosen. Negative so it can never collide
    /// with a real selection (indices are ≥ 0) or a cancel (<c>null</c>): <see cref="Accept"/> only ever
    /// closes with a non-negative index or null, so the delete button is the sole source of this value.
    /// </summary>
    public const int DeleteRequested = -1;

    private readonly ChooserViewModel? _vm;

    public ChooserDialog()
    {
        InitializeComponent();
        SystemMenu.EnableAltSpace(this);   // Alt+Space → native system menu
    }

    public ChooserDialog(string windowTitle, string prompt, IReadOnlyList<ChooserItem> items, string? deleteLabel = null) : this()
    {
        Title = windowTitle;
        _vm = new ChooserViewModel(prompt, items, deleteLabel);
        DataContext = _vm;
        Opened += (_, _) => Chooser.Focus();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Accept();
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
    private void OnDeleteClick(object? sender, RoutedEventArgs e) => Close(DeleteRequested);
    private void OnListDoubleTapped(object? sender, TappedEventArgs e) => Accept();

    // Raw key handling so the shortcuts work wherever focus sits (the list, a button, the window).
    // Up/Down only reach here when the focused ListBox hasn't already moved the selection itself —
    // it marks navigation keys handled, and class handlers skip handled events — so there's no double step.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(+1);
                e.Handled = true;
                break;
            case Key.Enter:
                Accept();
                e.Handled = true;
                break;
            case Key.Escape:
                Close(null);
                e.Handled = true;
                break;
            default:
                base.OnKeyDown(e);
                break;
        }
    }

    /// <summary>Moves the highlighted row by <paramref name="delta"/>, clamped to the list bounds.</summary>
    private void MoveSelection(int delta)
    {
        var count = Chooser.ItemCount;
        if (count == 0) return;
        var current = Chooser.SelectedIndex < 0 ? 0 : Chooser.SelectedIndex;
        Chooser.SelectedIndex = Math.Clamp(current + delta, 0, count - 1);
    }

    private void Accept()
    {
        var index = _vm?.SelectedIndex ?? -1;
        Close(index >= 0 ? index : null);
    }
}
