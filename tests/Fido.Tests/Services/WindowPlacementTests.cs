using System.IO;
using Avalonia;
using Fido.Services;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.Services;

/// <summary>
/// The main window's remembered place: <see cref="WindowPlacementStore"/> keeping it between runs, and
/// <see cref="WindowPlacementPolicy"/> fitting it to the screens there are now — which is where the
/// multi-monitor and HiDPI cases live that a headless window, with its one unscaled screen, can't reach.
/// </summary>
public class WindowPlacementTests
{
    private static readonly Size Minimum = new(560, 420);

    /// <summary>A 1080p laptop panel with a 40px taskbar along the bottom.</summary>
    private static readonly ScreenArea Laptop = new(new PixelRect(0, 0, 1920, 1040), 1.0);

    /// <summary>A second monitor to the laptop's right.</summary>
    private static readonly ScreenArea RightHand = new(new PixelRect(1920, 0, 2560, 1400), 1.0);

    private static WindowPlacement Left(int x, int y, double width = 720, double? height = null) =>
        new() { X = x, Y = y, Width = width, Height = height };

    // --- The store ------------------------------------------------------------------------

    [Test]
    public async Task A_saved_placement_reads_back_as_it_was_written()
    {
        using var world = new TestRepoWorld();
        var store = new WindowPlacementStore(Path.Combine(world.Root, "local", "window.json"));
        var placement = Left(-1700, 120, 812.5, 640);

        store.Save(placement);

        await Assert.That(store.Load()).IsEqualTo(placement);
    }

    [Test]
    public async Task A_content_sized_height_reads_back_as_none()
    {
        using var world = new TestRepoWorld();
        var store = new WindowPlacementStore(Path.Combine(world.Root, "window.json"));

        store.Save(Left(40, 60));

        await Assert.That(store.Load()!.Height).IsNull();
    }

    [Test]
    public async Task No_file_or_an_unreadable_one_is_nothing_remembered()
    {
        using var world = new TestRepoWorld();
        var path = Path.Combine(world.Root, "window.json");
        var store = new WindowPlacementStore(path);
        await Assert.That(store.Load()).IsNull();

        File.WriteAllText(path, "{ not json");
        await Assert.That(store.Load()).IsNull();

        // Parses, but describes no window: a hand edit, or a window saved before it was laid out.
        File.WriteAllText(path, """{ "X": 10, "Y": 10, "Width": 0 }""");
        await Assert.That(store.Load()).IsNull();
    }

    // --- The policy -----------------------------------------------------------------------

    [Test]
    public async Task A_place_still_on_a_screen_is_kept_exactly()
    {
        var restored = WindowPlacementPolicy.Restore(Left(100, 80, 900, 700), Minimum, [Laptop], Laptop);

        await Assert.That(restored.Position).IsEqualTo(new PixelPoint(100, 80));
        await Assert.That(restored.Width).IsEqualTo(900);
        await Assert.That(restored.Height).IsEqualTo(700);
    }

    [Test]
    public async Task A_place_on_the_second_monitor_is_kept_while_it_is_plugged_in()
    {
        var restored = WindowPlacementPolicy.Restore(Left(3000, 200), Minimum, [Laptop, RightHand], Laptop);

        await Assert.That(restored.Position).IsEqualTo(new PixelPoint(3000, 200));
    }

    [Test]
    public async Task A_place_on_a_monitor_since_unplugged_is_given_up_for_the_centre()
    {
        var restored = WindowPlacementPolicy.Restore(Left(3000, 200), Minimum, [Laptop], Laptop);

        await Assert.That(restored.Position).IsNull();   // the window's own CenterScreen takes over
        await Assert.That(restored.Width).IsEqualTo(720);
    }

    [Test]
    public async Task A_window_counts_as_on_screen_only_with_enough_title_bar_to_grab()
    {
        var sliver = WindowPlacementPolicy.Restore(Left(1920 - 40, 200), Minimum, [Laptop], Laptop);
        await Assert.That(sliver.Position).IsNull();

        var grip = WindowPlacementPolicy.Restore(Left(1920 - 200, 200), Minimum, [Laptop], Laptop);
        await Assert.That(grip.Position).IsEqualTo(new PixelPoint(1920 - 200, 200));

        // Its title bar above the top of every screen: there is nothing to take hold of.
        var above = WindowPlacementPolicy.Restore(Left(100, -30), Minimum, [Laptop], Laptop);
        await Assert.That(above.Position).IsNull();
    }

    [Test]
    public async Task A_hand_edited_size_below_the_minimum_is_held_to_it()
    {
        var restored = WindowPlacementPolicy.Restore(Left(100, 80, 200, 100), Minimum, [Laptop], Laptop);

        await Assert.That(restored.Width).IsEqualTo(Minimum.Width);
        await Assert.That(restored.Height).IsEqualTo(Minimum.Height);
    }

    [Test]
    public async Task A_window_bigger_than_its_screen_shrinks_to_fit_and_is_pulled_back_on()
    {
        var small = new ScreenArea(new PixelRect(0, 0, 1600, 900), 1.0);
        var restored = WindowPlacementPolicy.Restore(Left(500, 100, 2400, 1300), Minimum, [small], small);

        await Assert.That(restored.Width).IsEqualTo(1600);
        await Assert.That(restored.Height).IsEqualTo(900);
        await Assert.That(restored.Position).IsEqualTo(new PixelPoint(0, 0));
    }

    [Test]
    public async Task A_content_sized_window_keeps_sizing_itself()
    {
        var restored = WindowPlacementPolicy.Restore(Left(100, 80), Minimum, [Laptop], Laptop);

        await Assert.That(restored.Height).IsNull();
        await Assert.That(restored.Position).IsEqualTo(new PixelPoint(100, 80));
    }

    [Test]
    public async Task The_title_bar_is_measured_at_the_scaling_of_the_screen_it_is_on()
    {
        // At 200% the 32px title bar is 64 physical pixels: 40 of them above the top is more than half gone,
        // 20 is not.
        var hiDpi = new ScreenArea(new PixelRect(0, 0, 3840, 2100), 2.0);

        var gone = WindowPlacementPolicy.Restore(Left(200, -40), Minimum, [hiDpi], hiDpi);
        await Assert.That(gone.Position).IsNull();

        var kept = WindowPlacementPolicy.Restore(Left(200, -20), Minimum, [hiDpi], hiDpi);
        await Assert.That(kept.Position).IsEqualTo(new PixelPoint(200, -20));
    }
}
