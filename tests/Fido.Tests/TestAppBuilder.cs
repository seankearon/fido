using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Fido;
using Fido.Tests;

// Tells Avalonia.Headless how to build the application under test.
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Fido.Tests;

/// <summary>Builds the real <see cref="App"/> on the headless platform (Skia drawing → real frames).</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            // Explicit UseSkia() before UseHeadless is required: with UseHeadlessDrawing=false but no
            // Skia backend wired up, the session thread deadlocks on first dispatch. With it, frames render.
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}

/// <summary>
/// Runs a test body on the Avalonia UI thread. There is no official <c>Avalonia.Headless.TUnit</c>
/// package, so we drive the same engine the xUnit/NUnit integrations use: a single
/// <see cref="HeadlessUnitTestSession"/> per assembly, with each body dispatched onto its UI thread.
/// </summary>
public static class Ui
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(Ui).Assembly);

    // Route through the generic Dispatch<T>, which must await the inner task to produce its result —
    // the non-generic Dispatch(Func<Task>) overload runs the body fire-and-forget and returns before
    // the first await resumes, which would silently abandon assertions after any real async work.
    public static Task On(Func<Task> body) =>
        Session.Dispatch(async () => { await body(); return true; }, CancellationToken.None);

    public static Task<T> On<T>(Func<Task<T>> body) => Session.Dispatch(body, CancellationToken.None);

    /// <summary>Stops the headless session's UI thread so the test process can exit (see <see cref="TestHooks"/>).</summary>
    public static void Shutdown() => Session.Dispose();
}

