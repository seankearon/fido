using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace Fido.Services;

/// <summary>
/// Lets a <c>fido &lt;branch&gt;</c> hand itself to a Fido window that already has that branch, instead of
/// opening a second window on it.
///
/// Every open window <see cref="Listen">listens</see> on a named pipe of its own (a Unix domain socket off
/// Windows) and leaves a marker naming it in <see cref="RegistryDirectory"/>. A launch with a branch
/// <see cref="TryHandOffAsync">offers its command line</see> to each in turn; the first window whose branch box
/// holds that branch takes it and comes to the front, and the launch exits without a window of its own. When
/// no window takes it — or anything along the way goes wrong — the launch opens a window just as it always has:
/// the handoff can save a window, never cost one.
///
/// Both ends are scoped to the current user (<see cref="PipeOptions.CurrentUserOnly"/>), so another account on
/// the machine can neither answer as a Fido window nor hand one a command line.
/// </summary>
internal sealed class InstanceHandoff
{
    private const string MarkerExtension = ".instance";
    private const string PipePrefix = "fido-";
    private const byte ProtocolVersion = 1;
    private const byte Taken = 1;
    private const byte NotTaken = 0;
    private const int MaxRequestBytes = 64 * 1024;
    private const int MaxArgs = 256;

    /// <summary>How much later than its marker a process may seem to have started and still be the one that wrote
    /// it. A marker is written once the window is open, well after the process started, but a process's start time
    /// is only an estimate off Windows — derived from the boot time, to the second.</summary>
    private static readonly TimeSpan StartTimeSlack = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _answerTimeout;

    /// <summary>Where the open windows leave their markers: one empty file each, named for its pipe.</summary>
    public string RegistryDirectory { get; }

    /// <param name="registryDirectory">Where markers live; tests point it at a temp folder.</param>
    /// <param name="connectTimeout">How long to wait for a registered window's pipe to accept.</param>
    /// <param name="answerTimeout">How long a window gets to say whether it takes the command line.</param>
    public InstanceHandoff(string registryDirectory, TimeSpan? connectTimeout = null, TimeSpan? answerTimeout = null)
    {
        RegistryDirectory = registryDirectory;
        _connectTimeout = connectTimeout ?? TimeSpan.FromMilliseconds(500);
        _answerTimeout = answerTimeout ?? TimeSpan.FromSeconds(3);
    }

    /// <summary>
    /// The current user's registry, under local (not roaming) app data: which windows are open is a fact about
    /// this machine. Null when the OS offers no such folder — there is then nowhere to meet, and every launch
    /// opens its own window.
    /// </summary>
    public static InstanceHandoff? ForCurrentUser()
    {
        // DoNotVerify: the folder may not exist yet — a fresh Linux home has no ~/.local/share — and
        // Listen creates it. Verifying would answer "" and quietly turn the handoff off.
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        return string.IsNullOrEmpty(local) ? null : new InstanceHandoff(Path.Combine(local, "Fido", "instances"));
    }

    // --- The window's side --------------------------------------------------------------

    /// <summary>
    /// Registers a window and answers every command line offered to it until the returned registration is
    /// disposed. <paramref name="take"/> is asked, off the UI thread, whether the window takes a command line,
    /// and answers true when it did. Null when the window can't be registered: it works exactly as before, it
    /// just can't be found.
    /// </summary>
    public IDisposable? Listen(Func<string[], Task<bool>> take)
    {
        NamedPipeServerStream? server = null;
        try
        {
            Directory.CreateDirectory(RegistryDirectory);

            // Short on purpose: off Windows the pipe is a socket file in the temp folder, and macOS caps a
            // socket's path at 104 bytes — a temp folder there already spends half of that.
            var name = $"{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..12]}";
            var marker = Path.Combine(RegistryDirectory, name + MarkerExtension);

            // The pipe is up before the marker names it, so anyone who finds the marker can connect.
            server = CreateServer(PipePrefix + name);
            File.WriteAllBytes(marker, []);
            return new Registration(this, PipePrefix + name, marker, server, take);
        }
        catch (Exception)
        {
            server?.Dispose();
            return null;
        }
    }

    /// <summary>Two instances: the one answering a caller, and the next one, already listening.</summary>
    private static NamedPipeServerStream CreateServer(string pipeName) =>
        new(pipeName, PipeDirection.InOut, 2, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    /// <summary>One window's listener: its marker, and the loop answering whoever connects.</summary>
    private sealed class Registration : IDisposable
    {
        private readonly string _marker;
        private readonly CancellationTokenSource _cts = new();

        public Registration(InstanceHandoff owner, string pipeName, string marker, NamedPipeServerStream first,
            Func<string[], Task<bool>> take)
        {
            _marker = marker;
            _ = Task.Run(() => owner.AnswerCallersAsync(pipeName, first, take, DeleteMarker, _cts.Token));
        }

        /// <summary>Stops the window being found — the marker goes first, so nobody new comes looking — then
        /// stops listening.</summary>
        public void Dispose()
        {
            DeleteMarker();
            _cts.Cancel();
        }

        private void DeleteMarker() => TryDelete(_marker);
    }

    private async Task AnswerCallersAsync(string pipeName, NamedPipeServerStream first,
        Func<string[], Task<bool>> take, Action unregister, CancellationToken ct)
    {
        var server = first;
        try
        {
            while (true)
            {
                try
                {
                    await server.WaitForConnectionAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // This pipe instance is broken: put up a fresh one, after a pause so that one failing
                    // straight away can't spin the loop.
                    server.Dispose();
                    await Task.Delay(100, ct);
                    server = CreateServer(pipeName);
                    continue;
                }

                // The next caller's pipe goes up before this one comes down, so something is always listening.
                // Off Windows a pipe is a socket, and a caller that connected while only the spent one was up
                // would be dropped along with it — a second `fido` right behind the first would open a window.
                var next = CreateServer(pipeName);
                try
                {
                    await AnswerAsync(server, take, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    // A caller that hung up early or sent something unreadable costs its own answer, nothing more.
                }
                finally
                {
                    server.Dispose();
                    server = next;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // The pipe can't be put back up: stop advertising a window nobody can reach.
            unregister();
        }
        finally
        {
            server.Dispose();
        }
    }

    private async Task AnswerAsync(NamedPipeServerStream pipe, Func<string[], Task<bool>> take, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_answerTimeout);

        var args = await ReadRequestAsync(pipe, timeout.Token);
        var taken = args is not null && await take(args).WaitAsync(timeout.Token);
        await pipe.WriteAsync(new[] { taken ? Taken : NotTaken }, timeout.Token);
        await pipe.FlushAsync(timeout.Token);

        // Hold the pipe until the caller has read the answer and hung up: tearing it down first can take the
        // unread byte with it.
        try
        {
            var drain = new byte[1];
            while (await pipe.ReadAsync(drain, timeout.Token) > 0) { }
        }
        catch (IOException)
        {
            // the caller hung up hard — which is all we were waiting for
        }
    }

    private static async Task<string[]?> ReadRequestAsync(Stream pipe, CancellationToken ct)
    {
        var header = new byte[sizeof(int)];
        await pipe.ReadExactlyAsync(header, ct);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaxRequestBytes) return null;

        var body = new byte[length];
        await pipe.ReadExactlyAsync(body, ct);
        try
        {
            using var reader = new BinaryReader(new MemoryStream(body), Encoding.UTF8);
            if (reader.ReadByte() != ProtocolVersion) return null;
            var count = reader.ReadInt32();
            if (count is < 0 or > MaxArgs) return null;
            var args = new string[count];
            for (var i = 0; i < count; i++) args[i] = reader.ReadString();
            return args;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException)
        {
            return null;   // not a command line: decline it rather than leave the caller waiting
        }
    }

    // --- The launch's side --------------------------------------------------------------

    /// <summary>
    /// Offers <paramref name="args"/> to each registered window, newest first, until one takes it; true when
    /// one did. Markers left behind by a Fido that is no longer running are cleared away on the way past.
    /// </summary>
    public async Task<bool> TryHandOffAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        List<FileInfo> markers;
        try
        {
            markers = new DirectoryInfo(RegistryDirectory)
                .GetFiles("*" + MarkerExtension)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
        }
        catch (Exception)
        {
            return false;   // no registry yet, so no window to ask
        }

        var request = EncodeRequest(args);
        foreach (var marker in markers)
        {
            var name = Path.GetFileNameWithoutExtension(marker.Name);
            var dash = name.IndexOf('-');
            if (dash <= 0 || !int.TryParse(name.AsSpan(0, dash), out var pid)) continue;

            if (!IsRunning(pid, marker.LastWriteTimeUtc))
            {
                TryDelete(marker.FullName);   // that Fido went without closing its window — a crash, a kill, a reboot
                continue;
            }
            if (await TryOfferAsync(pid, PipePrefix + name, request, ct)) return true;
        }
        return false;
    }

    private async Task<bool> TryOfferAsync(int pid, string pipeName, byte[] request, CancellationToken ct)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync((int)_connectTimeout.TotalMilliseconds, ct);

            // The window that takes this will ask for the foreground, and Windows only lets a process that has
            // it pass it on. This one does: it was launched from wherever the user just typed.
            AllowForeground(pid);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_answerTimeout);
            await pipe.WriteAsync(request, timeout.Token);
            await pipe.FlushAsync(timeout.Token);

            var answer = new byte[1];
            return await pipe.ReadAsync(answer, timeout.Token) == 1 && answer[0] == Taken;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;   // not listening, or too slow to answer: move on to the next window
        }
    }

    /// <summary>The request on the wire: a length prefix, then the version, the count and each argument.</summary>
    private static byte[] EncodeRequest(IReadOnlyList<string> args)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0);   // the length, filled in below once it's known
            writer.Write(ProtocolVersion);
            writer.Write(args.Count);
            foreach (var arg in args) writer.Write(arg);
        }
        var bytes = stream.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes, bytes.Length - sizeof(int));
        return bytes;
    }

    /// <summary>
    /// Whether the process that wrote a marker at <paramref name="markedAtUtc"/> is still running as
    /// <paramref name="pid"/>. A process id is recycled once its process is gone — after a reboot, readily — so a
    /// process that started after the marker was written can't be the one that wrote it. Left unchecked, such a
    /// marker would never be cleared, and every launch would wait on a pipe nobody is listening on.
    /// </summary>
    private static bool IsRunning(int pid, DateTime markedAtUtc)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited && process.StartTime.ToUniversalTime() <= markedAtUtc + StartTimeSlack;
        }
        catch (Exception)
        {
            // No such process, gone while we asked, or one we aren't allowed to look at — which makes it another
            // account's, and so not a Fido window of ours either.
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // best effort: a marker left behind is cleared by the next launch that finds its process gone
        }
    }

    private static void AllowForeground(int pid)
    {
        if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(pid);
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int AllowSetForegroundWindow(int dwProcessId);
}
