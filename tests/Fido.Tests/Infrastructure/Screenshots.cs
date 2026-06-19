using System.IO;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Fido.Tests.Infrastructure;

/// <summary>
/// Captures rendered frames of headless windows as PNGs for human review (uploaded as CI artifacts).
/// Best-effort: a capture failure never fails a test — the logical assertions are the source of truth.
/// </summary>
public static class Screenshots
{
    /// <summary>Where PNGs land; overridable in CI so the workflow can upload a stable folder.</summary>
    public static readonly string Directory =
        Environment.GetEnvironmentVariable("FIDO_SCREENSHOT_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "screenshots");

    /// <summary>Renders <paramref name="window"/> to a PNG named <paramref name="name"/>; returns the frame (or null).</summary>
    public static WriteableBitmap? Save(TopLevel window, string name)
    {
        try
        {
            Dispatcher.UIThread.RunJobs();
            var frame = window.CaptureRenderedFrame();
            if (frame is null) return null;

            System.IO.Directory.CreateDirectory(Directory);
            frame.Save(Path.Combine(Directory, Sanitize(name) + ".png"));
            return frame;
        }
        catch
        {
            return null;   // capturing is a bonus, never a gate
        }
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '-');
        return name;
    }
}
