namespace Fido.Models;

/// <summary>
/// Where the main screen's discovery scan is in its lifecycle. Drives the status chip, the results
/// body, the open-actions lock (open is enabled iff <see cref="Found"/>), and delete visibility.
/// </summary>
public enum DiscoveryPhase
{
    /// <summary>No branch entered; nothing scanned yet.</summary>
    Idle,

    /// <summary>A scan is running; open actions stay locked until it resolves.</summary>
    Scanning,

    /// <summary>The branch is checked out in at least one working tree; open actions unlock.</summary>
    Found,

    /// <summary>No working tree or clone has the branch.</summary>
    NotFound,
}
