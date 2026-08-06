using Avalonia.Controls;
using Fido.Services;
using Fido.Views;

namespace Fido.Tests.Infrastructure;

/// <summary>Shared scaffolding for end-to-end tests: open a real <see cref="MainWindow"/> on the UI thread.</summary>
internal static class Harness
{
    /// <summary>Runs <paramref name="body"/> on the UI thread with a shown owner window (for ShowDialog tests).</summary>
    public static Task OnUi(Func<Window, Task> body) =>
        Ui.On(async () =>
        {
            var owner = new Window { Width = 480, Height = 360 };
            owner.Show();
            UiTestExtensions.Pump();
            try
            {
                await body(owner);
            }
            finally
            {
                owner.Close();
                UiTestExtensions.Pump();
            }
        });

    /// <summary>
    /// Builds and shows a real MainWindow with the injected services, runs <paramref name="body"/>, then
    /// closes it. <paramref name="beforeShow"/> sees the window before it is shown — the hook for anything
    /// that must be watched from the first frame, since a CLI-driven run can launch its tool and close the
    /// window while the window is being shown, before <paramref name="body"/> is ever reached.
    /// </summary>
    public static Task WithWindow(FidoServices services, Func<MainWindow, Task> body,
        Action<MainWindow>? beforeShow = null) =>
        Ui.On(async () =>
        {
            var window = new MainWindow(services);
            beforeShow?.Invoke(window);
            window.Show();
            UiTestExtensions.Pump();
            try
            {
                await body(window);
            }
            finally
            {
                window.Close();
                UiTestExtensions.Pump();
            }
        });
}
