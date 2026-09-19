using Avalonia.Styling;
using XTerm.Options;

namespace Fido.Theme;

/// <summary>
/// The console's 16-colour ANSI palette, in Fido's own warm scheme.
///
/// The stock xterm palette is built for a pure black or pure white background. Fido's console sits on
/// warm cream (<c>#F5F1E8</c>) or warm near-black (<c>#1C1812</c>), and the stock colours are close to
/// illegible on both — the light theme especially, where xterm's yellow and cyan all but vanish. The
/// colours below are drawn from the brushes the flight log already uses, so a script's green "ok" and
/// Fido's own ✓ are the same green.
///
/// Note that in the light theme the <c>Bright*</c> entries are <em>darker</em> than their base, not
/// lighter. Bright is a request for emphasis, and on a pale background emphasis means more contrast —
/// a literally brighter colour reads as washed out, which is the usual reason light terminal themes
/// look broken.
///
/// <see cref="MinimumContrast"/> is what makes this hold up against real programs. The palette can only
/// speak for the 16 colours; anything emitting 256-colour or truecolour SGR — a build tool's progress
/// output, a diff, a linter — picks values chosen against a black background and lands wherever it
/// lands. The renderer lifts any foreground that falls below the ratio, so that output stays readable
/// without Fido having to guess at it.
/// </summary>
public static class TerminalPalette
{
    /// <summary>
    /// The contrast floor the renderer holds foregrounds to, as a WCAG ratio against the background.
    /// 4.5 is AA for body text. Raising it further starts visibly flattening colours towards the
    /// foreground; 1.0 would turn the whole mechanism off.
    /// </summary>
    public const double MinimumContrast = 4.5;

    /// <summary>
    /// Copies the palette for <paramref name="variant"/> onto <paramref name="target"/>, field by field.
    ///
    /// In place, deliberately. The terminal control holds its own reference to the <see cref="ThemeOptions"/>
    /// instance it was built with and reads through that; handing it a replacement object leaves it reading
    /// the old one, and the renderer draws nothing at all. The same is true one level up for
    /// <c>TerminalOptions</c> — which is why <c>ConsolePane</c> never assigns either wholesale.
    /// </summary>
    public static void Apply(ThemeOptions target, ThemeVariant variant)
    {
        var p = variant == ThemeVariant.Dark ? Dark() : Light();

        target.Background = p.Background;
        target.Foreground = p.Foreground;
        target.Cursor = p.Cursor;
        target.CursorAccent = p.CursorAccent;
        target.Selection = p.Selection;
        target.SelectionInactive = p.SelectionInactive;

        target.Black = p.Black;
        target.Red = p.Red;
        target.Green = p.Green;
        target.Yellow = p.Yellow;
        target.Blue = p.Blue;
        target.Magenta = p.Magenta;
        target.Cyan = p.Cyan;
        target.White = p.White;

        target.BrightBlack = p.BrightBlack;
        target.BrightRed = p.BrightRed;
        target.BrightGreen = p.BrightGreen;
        target.BrightYellow = p.BrightYellow;
        target.BrightBlue = p.BrightBlue;
        target.BrightMagenta = p.BrightMagenta;
        target.BrightCyan = p.BrightCyan;
        target.BrightWhite = p.BrightWhite;
    }

    /// <summary>Warm cream background, ink-dark text. Colours are darkened for contrast, not lightened.</summary>
    private static ThemeOptions Light() => new()
    {
        Background = "#F5F1E8",       // FidoLogBg
        Foreground = "#211E17",       // FidoTextPrimary
        Cursor = "#DE7F17",           // FidoAccentText — the brand amber, so the caret reads as Fido's
        CursorAccent = "#F5F1E8",
        Selection = "#E3D9BE",
        SelectionInactive = "#EAE3D3",

        Black = "#211E17",
        Red = "#C0392B",              // FidoLogWarn
        Green = "#3E7C55",            // FidoLogOk
        Yellow = "#A66A0B",
        Blue = "#2A5DB0",
        Magenta = "#8A3FA0",
        Cyan = "#17707E",
        White = "#57513F",            // FidoTextSecondary — "white" is the dim normal on a pale ground

        BrightBlack = "#8A8271",      // FidoTextMuted: the one that must stay *lighter*, it means "dim"
        BrightRed = "#9E2B1E",
        BrightGreen = "#2E6341",
        BrightYellow = "#8A5606",
        BrightBlue = "#1F4489",
        BrightMagenta = "#6E2F80",
        BrightCyan = "#0F5863",
        BrightWhite = "#211E17",
    };

    /// <summary>Warm near-black background, parchment text.</summary>
    private static ThemeOptions Dark() => new()
    {
        Background = "#1C1812",       // FidoLogBg
        Foreground = "#F0EBDF",       // FidoTextPrimary
        Cursor = "#F4A62A",           // FidoAccent
        CursorAccent = "#1C1812",
        Selection = "#3D3526",
        SelectionInactive = "#2A251C",

        Black = "#3D3526",            // not #000: a true black would disappear into the warm ground
        Red = "#E06055",              // FidoLogWarn
        Green = "#5FA97C",            // FidoLogOk
        Yellow = "#F09A3E",           // FidoLogAccent
        Blue = "#6FA8DC",
        Magenta = "#C08AD6",
        Cyan = "#5FB3B8",
        White = "#C9C2B0",            // FidoLogPlain

        BrightBlack = "#8C8470",      // FidoTextMuted
        BrightRed = "#F0837A",
        BrightGreen = "#83C79C",
        BrightYellow = "#FFBA6B",
        BrightBlue = "#95C4EC",
        BrightMagenta = "#D6A8E6",
        BrightCyan = "#87CDD1",
        BrightWhite = "#F0EBDF",
    };
}
