using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;

namespace Fido.Services;

/// <summary>
/// Where the main window was, and how big, when it was last closed — so the next launch opens it there
/// rather than centred on whichever screen the OS picks.
///
/// Position is in physical pixels, as screens are; size is logical, as the window's Width and Height are, so
/// a window moved to a monitor of different scaling between runs comes back the same size to the eye.
/// <see cref="Height"/> is null while the window is still sizing itself to its content: the window grows and
/// shrinks with what discovery finds, and only a height chosen by dragging an edge is worth keeping.
/// </summary>
public sealed record WindowPlacement
{
    public int X { get; init; }
    public int Y { get; init; }
    public double Width { get; init; }

    /// <summary>The height the window was dragged to, or null for one still sized to its content.</summary>
    public double? Height { get; init; }

    /// <summary>
    /// Whether these could describe a window at all. A hand-edited file may say otherwise, and a NaN width
    /// handed to Avalonia is an exception at startup.
    /// </summary>
    [JsonIgnore]
    public bool IsUsable =>
        double.IsFinite(Width) && Width > 0
        && (Height is not { } height || (double.IsFinite(height) && height > 0));
}

/// <summary>
/// Reads and writes <see cref="WindowPlacement"/> as <c>window.json</c>.
///
/// Its own file rather than a key in <c>config.json</c>: it is written every time a window closes, and
/// config.json is a file people edit by hand — rewriting it on every close would be a standing chance of
/// overwriting an edit made while Fido was running. And it lives under the <em>local</em> app-data folder,
/// beside the instance markers, rather than the roaming one config.json is in: it describes this machine's
/// monitors, and must not follow a roaming profile onto a laptop whose screens are somewhere else entirely.
/// </summary>
public sealed class WindowPlacementStore(string filePath)
{
    public string FilePath { get; } = filePath;

    /// <summary><c>%LOCALAPPDATA%\Fido\window.json</c> (the platform's equivalent elsewhere), or null when
    /// there is no such folder to name.</summary>
    public static WindowPlacementStore? ForCurrentUser()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        return string.IsNullOrEmpty(local) ? null : new WindowPlacementStore(Path.Combine(local, "Fido", "window.json"));
    }

    /// <summary>The placement last saved, or null when there is none worth using. Never throws.</summary>
    public WindowPlacement? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return JsonSerializer.Deserialize(File.ReadAllText(FilePath), WindowPlacementJsonContext.Default.WindowPlacement)
                is { IsUsable: true } loaded
                ? loaded
                : null;
        }
        catch (Exception)
        {
            // Unreadable: the window opens centred, as it did before there was a file.
            return null;
        }
    }

    /// <summary>
    /// Writes the placement, swallowing failure: remembering where a window was is a nicety, and closing the
    /// window must not fail because a disk would not take the note. Through a temporary file, so two windows
    /// closing together can't leave half of each.
    /// </summary>
    public void Save(WindowPlacement placement)
    {
        try
        {
            if (Path.GetDirectoryName(FilePath) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
            var tmp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(placement, WindowPlacementJsonContext.Default.WindowPlacement));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Next time, then.
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(WindowPlacement))]
internal sealed partial class WindowPlacementJsonContext : JsonSerializerContext;

/// <summary>
/// A screen as the placement rules need to know it: where a window may go on it, which leaves out the
/// taskbar, and how many physical pixels a logical one is there.
/// </summary>
public readonly record struct ScreenArea(PixelRect WorkingArea, double Scaling);

/// <summary>
/// What <see cref="WindowPlacementPolicy.Restore"/> makes of a remembered placement: where the window goes —
/// null to leave it centred — and its size, with a null height leaving it sized to its content.
/// </summary>
public readonly record struct RestoredPlacement(PixelPoint? Position, double Width, double? Height);

/// <summary>
/// Fits a remembered <see cref="WindowPlacement"/> to the screens there are now.
///
/// Deliberately pure — no Window, no Screens — so the multi-monitor and HiDPI arithmetic is something tests
/// can pin down. Headless Avalonia only ever reports one unscaled screen with no taskbar, so a test that went
/// through a real window could not reach a single case that actually goes wrong.
/// </summary>
public static class WindowPlacementPolicy
{
    /// <summary>The native title bar's height, near enough on every platform Fido runs on.</summary>
    public const double TitleBarHeight = 32;

    /// <summary>
    /// How much of the title bar has to be on a screen, in logical pixels across, for the window to count as
    /// left there: enough to take hold of and drag back. Less, and a window restored to where it was would be
    /// on screen in name only.
    /// </summary>
    public const double MinimumGrip = 120;

    /// <summary>
    /// The window as it was last left, fitted to the screens there are now. Where it was, if enough of its
    /// title bar is still on one of them to grab; otherwise no position at all, so the window opens centred
    /// as it always did — a monitor unplugged since last time is the usual reason, and a window restored onto
    /// a screen that is no longer there is a window you cannot see. Its size either way, held to
    /// <paramref name="minimum"/> and to the screen it lands on, so a window left filling a 4K panel still
    /// fits a laptop's.
    /// </summary>
    /// <param name="minimum">The window's own MinWidth and MinHeight, which a hand-edited file may undercut.</param>
    /// <param name="screens">Every screen there is, by working area and scaling.</param>
    /// <param name="fallback">The screen whose size a window that is on none of them is held to — the primary.</param>
    public static RestoredPlacement Restore(
        WindowPlacement saved, Size minimum, IReadOnlyList<ScreenArea> screens, ScreenArea fallback)
    {
        var width = Math.Max(saved.Width, minimum.Width);
        var height = saved.Height is { } h ? Math.Max(h, minimum.Height) : (double?)null;
        var at = new PixelPoint(saved.X, saved.Y);

        foreach (var screen in screens)
        {
            if (!ShowsTitleBar(at, width, screen)) continue;

            var (fittedWidth, fittedHeight) = Fit(width, height, minimum, screen);
            // Exactly where it was, which is the point — unless it had to shrink to fit, in which case where
            // it was may now hang off the edge it used to reach.
            var position = fittedWidth == width && fittedHeight == height
                ? at
                : Clamp(at, PixelSize.FromSize(new Size(fittedWidth, fittedHeight ?? minimum.Height), screen.Scaling),
                    screen.WorkingArea);
            return new RestoredPlacement(position, fittedWidth, fittedHeight);
        }

        var (w, ht) = Fit(width, height, minimum, fallback);
        return new RestoredPlacement(null, w, ht);
    }

    /// <summary>
    /// Whether at least half the title bar's height, and <see cref="MinimumGrip"/> of its width, are inside
    /// the screen's working area — measured at that screen's scaling, since that is the scaling the window
    /// would have there.
    /// </summary>
    private static bool ShowsTitleBar(PixelPoint at, double width, ScreenArea screen)
    {
        var bar = new PixelRect(at, PixelSize.FromSize(new Size(width, TitleBarHeight), screen.Scaling));
        var seen = bar.Intersect(screen.WorkingArea);
        var grip = Math.Min(bar.Width, (int)Math.Round(MinimumGrip * screen.Scaling));
        return seen.Width >= grip && seen.Height * 2 >= bar.Height;
    }

    /// <summary>
    /// No bigger than the screen's working area, and no smaller than the window allows — which wins if the
    /// two disagree, as on a screen smaller than the window's minimum. A content-sized height stays so: the
    /// window holds itself to the screen.
    /// </summary>
    private static (double Width, double? Height) Fit(double width, double? height, Size minimum, ScreenArea screen)
    {
        var room = screen.WorkingArea.Size.ToSize(screen.Scaling);
        return (Math.Max(minimum.Width, Math.Min(width, room.Width)),
            height is { } h ? Math.Max(minimum.Height, Math.Min(h, room.Height)) : null);
    }

    /// <summary>
    /// Holds the whole window inside the working area. The Math.Max guards a window bigger than the screen —
    /// without it the upper bound falls below the lower one and Math.Clamp throws.
    /// </summary>
    private static PixelPoint Clamp(PixelPoint desired, PixelSize size, PixelRect workingArea) =>
        new(Math.Clamp(desired.X, workingArea.X, workingArea.X + Math.Max(0, workingArea.Width - size.Width)),
            Math.Clamp(desired.Y, workingArea.Y, workingArea.Y + Math.Max(0, workingArea.Height - size.Height)));
}
