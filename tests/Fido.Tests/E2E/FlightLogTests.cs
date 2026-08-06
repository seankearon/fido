using System.IO;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Fido.Tests.Infrastructure;

namespace Fido.Tests.E2E;

/// <summary>
/// The flight log at the bottom of the main screen: it takes whatever vertical room the window has
/// spare (so a taller window means a taller log, not a gap above it), and its narration can be lifted
/// out — the whole log to the clipboard, or saved to a text file the user picks.
/// </summary>
[NotInParallel]
public class FlightLogTests
{
    [Test]
    public async Task Copying_the_flight_log_puts_every_line_on_the_clipboard()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");
            var lines = window.Vm().Log.Count;
            await Assert.That(lines).IsGreaterThan(1);

            await window.CopyFlightLogAsync();

            var clipboard = TopLevel.GetTopLevel(window)?.Clipboard;
            await Assert.That(clipboard).IsNotNull();
            var copied = await clipboard!.TryGetTextAsync();

            // Everything the panel had — the mission-control preamble through the scan result.
            await Assert.That(copied).IsNotNull();
            await Assert.That(copied!).Contains("🚀 Going around the horn…");
            await Assert.That(copied).Contains("✓ Found 1 location(s) for 'main'.");
            await Assert.That(copied.Split('\n').Length).IsEqualTo(lines);

            // The copy itself is narrated, and doesn't ride along in what was copied.
            await Assert.That(window.LogText()).Contains($"📋 Copied {lines} flight-log line(s) to the clipboard.");
            await Assert.That(copied.Contains("flight-log line(s)")).IsFalse();
        });
    }

    [Test]
    public async Task An_empty_flight_log_has_nothing_to_copy_or_save()
    {
        using var world = new TestRepoWorld();
        var root = world.SearchRoot("root");
        var dialogs = new FakeDialogService();
        var services = world.BuildServices([root], new FakeEditorLauncher(), dialogs);

        await Harness.WithWindow(services, async window =>
        {
            // Idle: nothing has been scanned, so the log is empty and both actions are disabled.
            await Assert.That(window.Vm().HasLog).IsFalse();
            await Assert.That(window.FindControl<Button>("CopyLogButton")!.IsEnabled).IsFalse();
            await Assert.That(window.FindControl<Button>("SaveLogButton")!.IsEnabled).IsFalse();

            await window.CopyFlightLogAsync();
            await window.SaveFlightLogAsync();

            // No picker opened, and copying didn't narrate itself into the empty log.
            await Assert.That(dialogs.FlightLogSaveRequests.Count).IsEqualTo(0);
            await Assert.That(window.Vm().Log.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Saving_the_flight_log_writes_the_picked_file_and_says_where_it_went()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var target = Path.Combine(world.SearchRoot("saved"), "flight-log.txt");
        var dialogs = new FakeDialogService { OnPickFlightLogPath = _ => target };
        var services = world.BuildServices([root], new FakeEditorLauncher(), dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");
            var expected = window.Vm().LogText;

            await window.SaveFlightLogAsync();

            // The picker was offered a dated, .txt file name, and the file holds the log verbatim.
            await Assert.That(dialogs.FlightLogSaveRequests.Count).IsEqualTo(1);
            await Assert.That(dialogs.FlightLogSaveRequests[0]).StartsWith("fido-flight-log-");
            await Assert.That(dialogs.FlightLogSaveRequests[0]).EndsWith(".txt");

            await Assert.That(File.Exists(target)).IsTrue();
            var saved = await File.ReadAllTextAsync(target);
            await Assert.That(saved.TrimEnd()).IsEqualTo(expected.TrimEnd());
            await Assert.That(saved).Contains("✓ Found 1 location(s) for 'main'.");

            await Assert.That(window.LogText()).Contains($"✓ Flight log saved to {target}");
        });
    }

    [Test]
    public async Task Cancelling_the_save_picker_writes_nothing_and_stays_quiet()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var dialogs = new FakeDialogService();   // defaults to cancelling the picker
        var services = world.BuildServices([root], new FakeEditorLauncher(), dialogs);

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");
            var before = window.Vm().LogText;

            await window.SaveFlightLogAsync();

            await Assert.That(dialogs.FlightLogSaveRequests.Count).IsEqualTo(1);
            await Assert.That(window.Vm().LogText).IsEqualTo(before);   // nothing added, nothing failed
        });
    }

    [Test]
    public async Task A_taller_window_grows_the_flight_log_instead_of_the_gap_above_it()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");
            var panel = window.FindControl<Border>("LogPanel")!;
            var upper = window.FindControl<ScrollViewer>("MainScroller")!;
            var contentSized = panel.Bounds.Height;

            // Auto-sized to its content, the panel is the design's 104…150 box.
            await Assert.That(contentSized).IsGreaterThanOrEqualTo(104d);
            await Assert.That(contentSized).IsLessThanOrEqualTo(150d);

            // A user resize drops SizeToContent (Avalonia does this itself on a drag) — 400px taller.
            var grown = window.Bounds.Height + 400;
            window.SizeToContent = SizeToContent.Manual;
            window.Height = grown;
            Dispatcher.UIThread.RunJobs();

            // Every one of those pixels went to the log, and the upper section still fits unscrolled.
            await Assert.That(panel.Bounds.Height).IsGreaterThan(contentSized + 350);
            await Assert.That(upper.Extent.Height).IsLessThanOrEqualTo(upper.Viewport.Height + 1);
            Screenshots.Save(window, "flight-log-expanded");   // eyes-on artifact, like the redesign frames
        });
    }

    [Test]
    public async Task A_window_too_short_for_the_screen_keeps_the_log_and_scrolls_the_rest()
    {
        using var world = new TestRepoWorld();
        var origin = world.CreateOrigin("Foo", "Foo");
        var root = world.SearchRoot("root");
        world.Clone(origin, root, "Foo");

        var services = world.BuildServices([root], new FakeEditorLauncher(), new FakeDialogService());

        await Harness.WithWindow(services, async window =>
        {
            await window.Discover("main");
            var panel = window.FindControl<Border>("LogPanel")!;
            var upper = window.FindControl<ScrollViewer>("MainScroller")!;

            window.SizeToContent = SizeToContent.Manual;
            window.Height = 420;   // the window's minimum — far shorter than the screen needs
            Dispatcher.UIThread.RunJobs();

            // The log holds its ground at the content-sized box; the upper section takes the squeeze.
            await Assert.That(panel.Bounds.Height).IsGreaterThanOrEqualTo(104d);
            await Assert.That(panel.Bounds.Height).IsLessThanOrEqualTo(150d);
            await Assert.That(upper.Extent.Height).IsGreaterThan(upper.Viewport.Height);
        });
    }
}
