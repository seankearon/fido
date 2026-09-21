namespace Fido.Tests.Infrastructure;

/// <summary>
/// Stands in for the OS default browser: records what the window asked to open and never launches
/// anything, so a suite that clicks a link doesn't leave tabs behind on the machine running it.
/// Construct with <c>succeeds: false</c> to model a machine with nothing registered for http.
/// </summary>
public sealed class FakeBrowser
{
    private readonly bool _succeeds;

    public FakeBrowser(bool succeeds = true) => _succeeds = succeeds;

    /// <summary>Every URL handed over, in order.</summary>
    public List<string> Opened { get; } = new();

    public string? LastOpened => Opened.Count > 0 ? Opened[^1] : null;

    public bool Open(string url)
    {
        Opened.Add(url);
        return _succeeds;
    }
}
