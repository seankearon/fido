using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Fido.ViewModels;
using Fido.Views;

namespace Fido.Tests.Infrastructure;

/// <summary>Helpers for driving the real controls of a headless window and reading back its state.</summary>
public static class UiTestExtensions
{
    /// <summary>The window's bound view model (DataContext).</summary>
    public static MainWindowViewModel Vm(this MainWindow window) =>
        (MainWindowViewModel)window.DataContext!;

    /// <summary>Flush queued dispatcher work (layout, bindings, posted callbacks).</summary>
    public static void Pump() => Dispatcher.UIThread.RunJobs();

    /// <summary>Types into a named AutoCompleteBox/TextBox, driving its two-way binding to the VM.</summary>
    public static void SetText(this Window window, string controlName, string text)
    {
        switch (window.FindControl<Control>(controlName))
        {
            case AutoCompleteBox box: box.Text = text; break;
            case TextBox box: box.Text = text; break;
            case null: throw new InvalidOperationException($"No control named '{controlName}'.");
            default: throw new InvalidOperationException($"Control '{controlName}' is not a text input.");
        }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Sets a named CheckBox's state, driving its two-way binding to the VM.</summary>
    public static void SetChecked(this Window window, string controlName, bool value)
    {
        var box = window.FindControl<CheckBox>(controlName)
                  ?? throw new InvalidOperationException($"No CheckBox named '{controlName}'.");
        box.IsChecked = value;
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Raises a real Click on a named button, invoking its XAML Click handler.</summary>
    public static void ClickButton(this Window window, string controlName)
    {
        var button = window.FindControl<Button>(controlName)
                     ?? throw new InvalidOperationException($"No Button named '{controlName}'.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Sends a key-down through the routed-event pipeline (e.g. dialog keyboard shortcuts).</summary>
    public static void PressKey(this Window window, Key key)
    {
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Sends a key-down originating at a named control, so its own handlers see it (e.g. Enter in a box).</summary>
    public static void PressKeyOn(this Window window, string controlName, Key key)
    {
        var control = window.FindControl<Control>(controlName)
                      ?? throw new InvalidOperationException($"No control named '{controlName}'.");
        control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The whole flight log as one newline-joined string for easy substring assertions.</summary>
    public static string LogText(this MainWindow window) =>
        string.Join("\n", window.Vm().Log.Select(line => line.Text));

    /// <summary>Types the branch/solution into the real boxes and runs the open flow to completion.</summary>
    public static async Task Open(this MainWindow window, string branch, string solution = "")
    {
        window.SetText("BranchBox", branch);
        window.SetText("SolutionBox", solution);
        await window.RunOpenAsync();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Index of the first chooser item whose title contains <paramref name="needle"/>, comparing with
    /// separators normalized (chooser titles may be git-porcelain paths with forward slashes).
    /// </summary>
    public static int PickTitleContaining(this ChooserRequest request, string needle)
    {
        var unified = needle.Replace('\\', '/');
        for (var i = 0; i < request.Items.Count; i++)
            if (request.Items[i].Title.Replace('\\', '/').Contains(unified, StringComparison.OrdinalIgnoreCase))
                return i;
        throw new InvalidOperationException($"No chooser item title contains '{needle}'.");
    }
}
