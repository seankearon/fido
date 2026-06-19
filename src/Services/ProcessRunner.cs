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
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
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

        using var process = new Process { StartInfo = psi };
        process.Start();

        // Read both pipes concurrently before waiting, to avoid buffer-fill deadlocks.
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(process.ExitCode, await stdOutTask, await stdErrTask);
    }
}
