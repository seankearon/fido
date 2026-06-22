namespace Fido.Models;

/// <summary>When Fido closes itself after successfully launching Rider.</summary>
public enum CloseAfterOpen
{
    /// <summary>Only when Fido was started from the command line with a branch (one-shot launcher). The default.</summary>
    CommandLine,

    /// <summary>After every successful launch, including the interactive "Open in Rider" button.</summary>
    Always,

    /// <summary>Never close automatically; the window stays open after launching Rider.</summary>
    Never,
}
