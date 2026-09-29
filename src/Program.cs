using Avalonia;
using Fido.Models;
using Fido.Services;

namespace Fido;

internal static class Program
{
    /// <summary>Raw command-line arguments captured at startup so the UI can pre-fill inputs.</summary>
    public static string[] StartupArgs { get; internal set; } = [];

    // Avalonia configuration; the entry point must not be touched by the visual designer.
    [STAThread]
    public static void Main(string[] args)
    {
        StartupArgs = args;
        if (HandedOff(args)) return;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// True when a Fido window that already has this command line's branch took it over: that window has come
    /// to the front, and this launch has nothing left to do. Only a command line naming a branch is offered,
    /// and never one with <c>--new-window</c>. Anything going wrong just means opening a window of our own.
    /// </summary>
    private static bool HandedOff(string[] args)
    {
        var command = StartupCommand.Parse(args);
        if (string.IsNullOrWhiteSpace(command.Branch) || command.NewWindow) return false;
        try
        {
            // No UI and no synchronisation context yet, so blocking here can't deadlock.
            return InstanceHandoff.ForCurrentUser()?.TryHandOffAsync(args).GetAwaiter().GetResult() == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
