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

    private static bool HandedOff(string[] args) =>
        HandOff(args, new ConfigService(), InstanceHandoff.ForCurrentUser());

    /// <summary>
    /// True when a Fido window that already has this command line's branch took it over: that window has come
    /// to the front, and this launch has nothing left to do. Only a command line naming a branch is offered —
    /// never one with <c>--new-window</c>, and not at all with <see cref="AppConfig.SwitchToOpenWindow"/> off.
    /// Anything going wrong just means opening a window of our own. Blocks, which is safe before any UI exists
    /// — there is no synchronisation context yet to deadlock on.
    /// </summary>
    internal static bool HandOff(IReadOnlyList<string> args, ConfigService configService, InstanceHandoff? instances)
    {
        var command = StartupCommand.Parse(args);
        if (string.IsNullOrWhiteSpace(command.Branch) || command.NewWindow || instances is null) return false;
        try
        {
            return configService.Load().SwitchToOpenWindow && instances.TryHandOffAsync(args).GetAwaiter().GetResult();
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
