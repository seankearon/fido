using System.IO;
using Fido.Services;

namespace Fido.Tests.Services;

/// <summary>
/// The handoff between a launch and the open windows, over real pipes and a real registry folder — just not
/// the user's: every test gets a temp folder of its own. The window's half is stood in for by a callback, so
/// this pins the plumbing; what a window does with a command line is pinned by the E2E handoff tests.
/// </summary>
public class InstanceHandoffTests
{
    private static InstanceHandoff NewRegistry(TimeSpan? connectTimeout = null) =>
        new(Path.Combine(Path.GetTempPath(), "fido-handoff-tests", Guid.NewGuid().ToString("N")), connectTimeout);

    private static string[] Markers(InstanceHandoff handoff) =>
        Directory.Exists(handoff.RegistryDirectory) ? Directory.GetFiles(handoff.RegistryDirectory) : [];

    private static void Cleanup(InstanceHandoff handoff)
    {
        try { Directory.Delete(handoff.RegistryDirectory, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    [Test]
    public async Task With_no_window_registered_there_is_nobody_to_hand_off_to()
    {
        var handoff = NewRegistry();

        await Assert.That(await handoff.TryHandOffAsync(["feature/x"])).IsFalse();
    }

    [Test]
    public async Task A_window_that_takes_the_command_line_receives_it_intact()
    {
        var handoff = NewRegistry();
        string[]? received = null;
        using var registration = handoff.Listen(args =>
        {
            received = args;
            return Task.FromResult(true);
        });
        try
        {
            await Assert.That(registration).IsNotNull();
            await Assert.That(Markers(handoff).Length).IsEqualTo(1);

            var taken = await handoff.TryHandOffAsync(["feature/größe-✓", "-s", "My App", ""]);

            await Assert.That(taken).IsTrue();
            await Assert.That(received).IsEquivalentTo(new[] { "feature/größe-✓", "-s", "My App", "" });
        }
        finally
        {
            Cleanup(handoff);
        }
    }

    [Test]
    public async Task A_window_that_declines_leaves_the_launch_to_open_its_own()
    {
        var handoff = NewRegistry();
        using var registration = handoff.Listen(_ => Task.FromResult(false));
        try
        {
            await Assert.That(await handoff.TryHandOffAsync(["feature/x"])).IsFalse();

            // Declining costs the window nothing: it's still registered, and still answers the next launch.
            await Assert.That(Markers(handoff).Length).IsEqualTo(1);
            await Assert.That(await handoff.TryHandOffAsync(["feature/x"])).IsFalse();
        }
        finally
        {
            Cleanup(handoff);
        }
    }

    [Test]
    public async Task The_offer_moves_past_a_window_that_declines_to_one_that_takes_it()
    {
        var handoff = NewRegistry();
        var asked = new List<string>();
        using var takes = handoff.Listen(_ => { lock (asked) asked.Add("takes"); return Task.FromResult(true); });
        var takersMarker = Markers(handoff).Single();
        using var declines = handoff.Listen(_ => { lock (asked) asked.Add("declines"); return Task.FromResult(false); });

        // Windows are asked newest first: make the decliner's marker certainly the newer one.
        File.SetLastWriteTimeUtc(Markers(handoff).Single(m => m != takersMarker), DateTime.UtcNow.AddMinutes(1));
        try
        {
            await Assert.That(await handoff.TryHandOffAsync(["feature/x"])).IsTrue();
            await Assert.That(string.Join(", ", asked)).IsEqualTo("declines, takes");
        }
        finally
        {
            Cleanup(handoff);
        }
    }

    [Test]
    public async Task A_closed_window_takes_its_marker_with_it_and_stops_answering()
    {
        var handoff = NewRegistry();
        var registration = handoff.Listen(_ => Task.FromResult(true));
        try
        {
            registration!.Dispose();

            await Assert.That(Markers(handoff).Length).IsEqualTo(0);
            await Assert.That(await handoff.TryHandOffAsync(["feature/x"])).IsFalse();
        }
        finally
        {
            Cleanup(handoff);
        }
    }

    [Test]
    public async Task A_marker_left_by_a_Fido_that_is_no_longer_running_is_cleared_away()
    {
        var handoff = NewRegistry();
        Directory.CreateDirectory(handoff.RegistryDirectory);
        var stale = Path.Combine(handoff.RegistryDirectory, $"{int.MaxValue}-0123456789ab.instance");
        File.WriteAllBytes(stale, []);
        try
        {
            await Assert.That(await handoff.TryHandOffAsync(["feature/x"])).IsFalse();
            await Assert.That(File.Exists(stale)).IsFalse();
        }
        finally
        {
            Cleanup(handoff);
        }
    }

    [Test]
    public async Task A_marker_whose_process_id_now_belongs_to_a_newer_process_is_cleared_away()
    {
        // A Fido that went without closing, and whose process id has since been handed to another process — this
        // one, which started after the marker was written. It can't be the window that wrote it.
        var handoff = NewRegistry();
        Directory.CreateDirectory(handoff.RegistryDirectory);
        var recycled = Path.Combine(handoff.RegistryDirectory, $"{Environment.ProcessId}-0123456789ab.instance");
        File.WriteAllBytes(recycled, []);
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        File.SetLastWriteTimeUtc(recycled, self.StartTime.ToUniversalTime().AddHours(-1));
        try
        {
            await Assert.That(await handoff.TryHandOffAsync(["feature/x"])).IsFalse();
            await Assert.That(File.Exists(recycled)).IsFalse();
        }
        finally
        {
            Cleanup(handoff);
        }
    }

    [Test]
    public async Task A_running_process_whose_pipe_never_answers_is_skipped_not_waited_on()
    {
        // This process is alive and older than the marker, but nothing listens on the pipe the marker names — a
        // window that stopped listening, say. The launch gives up on it after the connect timeout.
        var handoff = NewRegistry(connectTimeout: TimeSpan.FromMilliseconds(200));
        Directory.CreateDirectory(handoff.RegistryDirectory);
        var silent = Path.Combine(handoff.RegistryDirectory, $"{Environment.ProcessId}-0123456789ab.instance");
        File.WriteAllBytes(silent, []);
        try
        {
            var started = DateTime.UtcNow;

            await Assert.That(await handoff.TryHandOffAsync(["feature/x"])).IsFalse();
            await Assert.That(DateTime.UtcNow - started).IsLessThan(TimeSpan.FromSeconds(5));

            // The process is running, so the marker isn't this launch's to judge stale.
            await Assert.That(File.Exists(silent)).IsTrue();
        }
        finally
        {
            Cleanup(handoff);
        }
    }

    [Test]
    public async Task Markers_that_name_no_process_are_ignored()
    {
        var handoff = NewRegistry();
        Directory.CreateDirectory(handoff.RegistryDirectory);
        File.WriteAllBytes(Path.Combine(handoff.RegistryDirectory, "not-a-pid.instance"), []);
        File.WriteAllBytes(Path.Combine(handoff.RegistryDirectory, "readme.txt"), []);
        try
        {
            await Assert.That(await handoff.TryHandOffAsync(["feature/x"])).IsFalse();
        }
        finally
        {
            Cleanup(handoff);
        }
    }
}
