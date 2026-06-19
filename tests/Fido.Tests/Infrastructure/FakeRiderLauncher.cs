using Fido.Models;
using Fido.Services;

namespace Fido.Tests.Infrastructure;

/// <summary>
/// A real <see cref="IRiderLauncher"/> that records launches instead of spawning an IDE, and returns
/// a configurable locate result — set <see cref="LocateResult"/> to null to model "Rider not installed".
/// A fake instance, not a mock: no expectations, just observable state.
/// </summary>
public sealed class FakeRiderLauncher : IRiderLauncher
{
    /// <summary>What <see cref="Locate"/> returns; null models a machine with no Rider.</summary>
    public string? LocateResult { get; set; } = "/fake/rider/bin/rider";

    public List<(string Executable, string Target)> Launches { get; } = new();

    public (string Executable, string Target)? LastLaunch =>
        Launches.Count > 0 ? Launches[^1] : null;

    private readonly TaskCompletionSource _firstLaunch = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes on the first launch — lets a test await the async-void button click deterministically.</summary>
    public Task FirstLaunch => _firstLaunch.Task;

    public string? Locate(AppConfig config) => LocateResult;

    public void Launch(string riderExecutable, string targetPath)
    {
        Launches.Add((riderExecutable, targetPath));
        _firstLaunch.TrySetResult();
    }
}
