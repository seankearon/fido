using System.Diagnostics;

namespace Fido.Services;

/// <summary>Opens a web URL in the OS default browser. Best-effort; returns false on failure.</summary>
public static class UrlLauncher
{
    /// <summary>
    /// Whether <paramref name="url"/> is an absolute <c>http</c> or <c>https</c> URL — the only kind
    /// <see cref="Open"/> will hand to the OS.
    ///
    /// The guard lives here rather than at each call site because of what "open" means underneath: on
    /// Windows <c>UseShellExecute</c> asks the shell to do whatever it would do with that string, and for
    /// a scheme other than http(s) that can be a registered program rather than a page. Fido's own links
    /// are all web pages, so the rule costs them nothing — but a link clicked in the Console tab comes off
    /// the screen of whatever the shell just ran, and an OSC 8 hyperlink need not show where it points at
    /// all. Nothing arriving from there is worth handing to the shell unread.
    /// </summary>
    public static bool IsWebUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static bool Open(string url)
    {
        if (!IsWebUrl(url)) return false;
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
