using Fido.Models;

namespace Fido.Services;

/// <summary>
/// Locates and launches JetBrains Rider. Abstracted from <see cref="RiderLauncher"/> so the UI flow
/// can be driven in tests with a fake that records launches and returns a configurable locate result
/// (including "not installed" → null) without spawning a real IDE.
/// </summary>
public interface IRiderLauncher
{
    /// <summary>Returns a Rider executable/app-bundle path, or null if none is found.</summary>
    string? Locate(AppConfig config);

    /// <summary>Starts Rider on <paramref name="targetPath"/> without waiting for it.</summary>
    void Launch(string riderExecutable, string targetPath);
}
