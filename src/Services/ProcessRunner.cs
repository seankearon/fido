using System;
using System.Diagnostics;
using System.Threading;

namespace Fido.Services;

/// <summary>Outcome of running an external process.</summary>
public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;

    /// <summary>StdErr if present, otherwise StdOut — handy for surfacing failures.</summary>
    public string Message =>
        !string.IsNullOrWhiteSpace(StdErr) ? StdErr.Trim()
        : StdOut.Trim();
}

/// <summary>Thin async wrapper over <see cref="Process"/> that captures stdout/stderr.</summary>
public static class ProcessRunner
{
    /// <summary>
    /// Backstop for a command that has wedged — not a patience limit. The prompt suppression below is what
    /// makes the common hangs fail in milliseconds; this only catches what's left, so it's set well clear of
    /// anything git might legitimately take (a first fetch of a large repo over a slow link), because killing
    /// a real transfer part-way is worse than waiting for it.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (!string.IsNullOrEmpty(workingDirectory))
            psi.WorkingDirectory = workingDirectory;
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);
        SuppressCredentialPrompts(psi);

        using var timeoutSource = new CancellationTokenSource(timeout ?? DefaultTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        using var process = new Process { StartInfo = psi };
        process.Start();

        // Read both pipes concurrently before waiting, to avoid buffer-fill deadlocks.
        var stdOutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
        var stdErrTask = process.StandardError.ReadToEndAsync(linked.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            // Abandoning the wait would leave the process running: Fido's discovery is keystroke-debounced,
            // so a cancelled scan can otherwise strand a git process per keystroke. Kill the whole tree (git
            // spawns helpers — ssh, credential managers), which also closes the pipes the reads are still
            // sitting on, then let the caller's own cancellation win over a timeout — the latter is reported
            // as an ordinary failure rather than thrown.
            Kill(process);
            await Observe(stdOutTask, stdErrTask);
            if (cancellationToken.IsCancellationRequested) throw;
            return new ProcessResult(-1, "", $"{fileName} timed out after {(timeout ?? DefaultTimeout).TotalMinutes:0.#} minute(s).");
        }

        return new ProcessResult(process.ExitCode, await stdOutTask, await stdErrTask);
    }

    /// <summary>
    /// Stops git blocking on input no one can answer. Fido runs git with <c>CreateNoWindow</c> and its pipes
    /// redirected, so a credential or host-key prompt has nowhere to appear and the command would hang until
    /// the timeout above. Failing fast instead lets the caller narrate "couldn't reach origin" and carry on.
    /// </summary>
    private static void SuppressCredentialPrompts(ProcessStartInfo psi)
    {
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_ASKPASS"] = "";
        psi.Environment["SSH_ASKPASS"] = "";

        // ssh has its own prompts (passphrase, an unknown host key) and ignores the variables above.
        // BatchMode makes it fail instead of asking — it accepts nothing it wouldn't have accepted
        // anyway, so the user's trust decisions are unchanged. A GIT_SSH_COMMAND they set themselves
        // is left alone: that's a deliberate choice about how their git reaches the network.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GIT_SSH_COMMAND")))
            psi.Environment["GIT_SSH_COMMAND"] = "ssh -o BatchMode=yes";
    }

    /// <summary>
    /// Waits out the two pipe reads and discards whatever they ended as. They were cancelled along with the
    /// wait, and a faulted task nobody ever awaits resurfaces later as an unobserved exception on the
    /// finalizer thread — a confusing crash a long way from here.
    /// </summary>
    private static async Task Observe(params Task<string>[] reads)
    {
        foreach (var read in reads)
        {
            try { await read; }
            catch { /* the command is already being reported as cancelled or timed out */ }
        }
    }

    /// <summary>Kills the process and everything it spawned, tolerating one that has already exited.</summary>
    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { /* already gone, or the OS won't let us — nothing useful left to do */ }
    }
}
