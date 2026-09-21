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
    /// The plain console ground and ink for <paramref name="variant"/> — what the console wears when it is
    /// <em>not</em> wearing Fido's palette.
    ///
    /// It is still the terminal's own black-and-white; it is simply the right way up for the theme. A
    /// terminal emulator ships one scheme, built for a dark desktop, and a fixed black box in a cream
    /// window doesn't read as "plain", it reads as broken. So dark keeps the stock pair exactly, and light
    /// inverts it — which is what every terminal's own light profile does.
    /// </summary>
    public static (string Background, string Foreground) Plain(ThemeVariant variant) =>
        variant == ThemeVariant.Dark ? ("#000000", "#FFFFFF") : ("#FFFFFF", "#000000");

    /// <summary>
    /// Restores the emulator's own colours onto <paramref name="target"/>, then turns the ground and ink
    /// the right way up for <paramref name="variant"/>.
    ///
    /// <paramref name="stock"/> is the emulator's pristine palette, snapshotted before Fido ever wrote to
    /// the live object — restoring from it is what makes the Settings switch reversible. Without it,
    /// turning Fido's palette back off would leave Fido's sixteen ANSI colours sitting in the theme, since
    /// everything here mutates one shared instance.
    ///
    /// The sixteen therefore end up exactly as the emulator ships them; only the ground, the ink and the
    /// caret are Fido's doing. <see cref="MinimumContrast"/> is what keeps those sixteen legible once the
    /// ground is pale, since every one of them was chosen against black.
    ///
    /// In place, for the same reason as <see cref="Apply"/>.
    /// </summary>
    public static void ApplyPlain(ThemeOptions target, ThemeVariant variant, ThemeOptions stock)
    {
        CopyTo(stock, target);

        var (background, foreground) = Plain(variant);
        target.Background = background;
        target.Foreground = foreground;

        // The caret is drawn in Cursor on a CursorAccent ground. Left at the emulator's values it is white
        // on white the moment the ground turns pale — a caret you cannot find on a light theme.
        target.Cursor = foreground;
        target.CursorAccent = background;
    }

    /// <summary>
    /// A detached copy of <paramref name="source"/>, for keeping the emulator's own palette safe before
    /// anything is written over it. See <see cref="ApplyPlain"/> for why that copy has to exist.
    /// </summary>
    public static ThemeOptions Snapshot(ThemeOptions source)
    {
        var copy = new ThemeOptions();
        CopyTo(source, copy);
        return copy;
    }

    /// <summary>
    /// Copies the palette for <paramref name="variant"/> onto <paramref name="target"/>, field by field.
    ///
    /// In place, deliberately. The terminal control holds its own reference to the <see cref="ThemeOptions"/>
    /// instance it was built with and reads through that; handing it a replacement object leaves it reading
    /// the old one, and the renderer draws nothing at all. The same is true one level up for
    /// <c>TerminalOptions</c> — which is why <c>ConsolePane</c> never assigns either wholesale.
    /// </summary>
    public static void Apply(ThemeOptions target, ThemeVariant variant) => CopyTo(For(variant), target);

    /// <summary>Copies every palette entry from <paramref name="source"/> onto <paramref name="target"/>.</summary>
    private static void CopyTo(ThemeOptions source, ThemeOptions target)
    {
        target.Background = source.Background;
        target.Foreground = source.Foreground;
        target.Cursor = source.Cursor;
        target.CursorAccent = source.CursorAccent;
        target.Selection = source.Selection;
        target.SelectionInactive = source.SelectionInactive;

        target.Black = source.Black;
        target.Red = source.Red;
        target.Green = source.Green;
        target.Yellow = source.Yellow;
        target.Blue = source.Blue;
        target.Magenta = source.Magenta;
        target.Cyan = source.Cyan;
        target.White = source.White;

        target.BrightBlack = source.BrightBlack;
        target.BrightRed = source.BrightRed;
        target.BrightGreen = source.BrightGreen;
        target.BrightYellow = source.BrightYellow;
        target.BrightBlue = source.BrightBlue;
        target.BrightMagenta = source.BrightMagenta;
        target.BrightCyan = source.BrightCyan;
        target.BrightWhite = source.BrightWhite;
    }

    /// <summary>
    /// Fido's colours for <paramref name="variant"/>, as a fresh set. Callers that need a single entry —
    /// <c>ConsolePane</c> wants the ground and the ink as brushes, which is what the emulator seeds itself
    /// from — read it from here rather than keeping a second copy of the hex that could drift.
    /// </summary>
    public static ThemeOptions For(ThemeVariant variant) =>
        variant == ThemeVariant.Dark ? Dark() : Light();

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
