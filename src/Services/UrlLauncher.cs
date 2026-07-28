using System.Diagnostics;

namespace Fido.Services;

/// <summary>Opens a web URL in the OS default browser. Best-effort; returns false on failure.</summary>
public static class UrlLauncher
{
    public static bool Open(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            var psi =
                OperatingSystem.IsWindows() ? new ProcessStartInfo(url) { UseShellExecute = true }
                : OperatingSystem.IsMacOS() ? new ProcessStartInfo("open", url)
                : new ProcessStartInfo("xdg-open", url);
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
