using Fido.Models;
using Fido.Services;

namespace Fido.Tests.Infrastructure;

/// <summary>
/// A real <see cref="IDialogService"/> that returns scripted choices and records every request, so the
/// end-to-end flow can be driven without real modal windows. Discovery, target choice, and worktree
/// deletion render inline on the main screen now; only the settings dialog and the exceptional
/// force-delete recovery remain modal. A fake, not a mock — assert against the recorded requests.
/// </summary>
public sealed class FakeDialogService : IDialogService
{
    /// <summary>
    /// Force-delete-folder responder used when git couldn't remove the worktree; defaults to declining (the
    /// safe default for a Recycle-Bin-bypassing delete). Return true to accept the fallback.
    /// </summary>
    public Func<WorktreeForceDelete, bool> OnConfirmForceDelete { get; set; } = _ => false;

    /// <summary>
    /// Flight-log save responder, handed the suggested file name; defaults to returning null — the user
    /// cancelled the picker. Return a path to have the log written there.
    /// </summary>
    public Func<string, string?> OnPickFlightLogPath { get; set; } = _ => null;

    public List<WorktreeForceDelete> ForceDeleteConfirmations { get; } = new();

    /// <summary>Every suggested file name the flight-log save picker was opened with, in order.</summary>
    public List<string> FlightLogSaveRequests { get; } = new();

    public int SettingsShownCount { get; private set; }

    public Task<bool> ConfirmForceDeleteWorktreeFolderAsync(WorktreeForceDelete request)
    {
        ForceDeleteConfirmations.Add(request);
        return Task.FromResult(OnConfirmForceDelete(request));
    }

    public Task ShowSettingsAsync(AppConfig config, ConfigService configService)
    {
        SettingsShownCount++;
        return Task.CompletedTask;
    }

    public Task<string?> PickFlightLogPathAsync(string suggestedFileName)
    {
        FlightLogSaveRequests.Add(suggestedFileName);
        return Task.FromResult(OnPickFlightLogPath(suggestedFileName));
    }
}
