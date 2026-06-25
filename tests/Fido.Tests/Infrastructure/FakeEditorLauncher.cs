using Fido.Models;
using Fido.Services;

namespace Fido.Tests.Infrastructure;

/// <summary>
/// A real <see cref="IEditorLauncher"/> that records launches instead of spawning an IDE, and returns
/// a configurable locate result — set <see cref="LocateResult"/> to null to model "editor not installed".
/// A fake instance, not a mock: no expectations, just observable state.
/// </summary>
public sealed class FakeEditorLauncher : IEditorLauncher
{
    /// <summary>What <see cref="Locate"/> returns; null models a machine with no such editor.</summary>
    public string? LocateResult { get; set; } = "/fake/editor/bin/editor";

    /// <summary>Records each launch as (executable, target), plus the editor it was asked to use.</summary>
    public List<(string Executable, string Target, Editor Editor)> Launches { get; } = new();

    public (string Executable, string Target, Editor Editor)? LastLaunch =>
        Launches.Count > 0 ? Launches[^1] : null;

    private readonly TaskCompletionSource _firstLaunch = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes on the first launch — lets a test await the async-void button click deterministically.</summary>
    public Task FirstLaunch => _firstLaunch.Task;

    public string? Locate(Editor editor) => LocateResult;

    public void Launch(Editor editor, string executable, string targetPath)
    {
        Launches.Add((executable, targetPath, editor));
        _firstLaunch.TrySetResult();
    }
}
