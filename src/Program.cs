using Avalonia;

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
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
