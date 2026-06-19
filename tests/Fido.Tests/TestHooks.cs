using static TUnit.Core.HookType;

namespace Fido.Tests;

/// <summary>
/// The headless <see cref="Ui"/> session runs its dispatcher on a foreground thread; without an
/// explicit shutdown the test process would never exit (and Microsoft.Testing.Platform's buffered
/// output would never flush). Dispose it once, after the whole session.
/// </summary>
public static class TestHooks
{
    [After(TestSession)]
    public static void ShutdownHeadlessSession() => Ui.Shutdown();
}
