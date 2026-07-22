using System;

namespace Fido.ViewModels;

/// <summary>
/// Flight-log line kinds from the redesign handoff: <see cref="Accent"/> for mission-control voice
/// (🚀 / Fido? GO!), <see cref="Ok"/> for confirmations (✓), <see cref="Warn"/> for failures (⚠/✗),
/// <see cref="Plain"/> for action lines (▸ Opening …), and <see cref="Muted"/> for everything else.
/// </summary>
public enum LogLevel { Muted, Accent, Ok, Warn, Plain }

/// <summary>One color-coded line in the flight log.</summary>
public sealed class LogLine
{
    public LogLine(string text, LogLevel level)
    {
        Text = text;
        Level = level;
    }

    public string Text { get; }
    public LogLevel Level { get; }

    public bool IsMuted => Level == LogLevel.Muted;
    public bool IsAccent => Level == LogLevel.Accent;
    public bool IsOk => Level == LogLevel.Ok;
    public bool IsWarn => Level == LogLevel.Warn;
    public bool IsPlain => Level == LogLevel.Plain;

    /// <summary>Picks a level from the line's marker prefix so callers can keep logging plain strings.
    /// Recognises both the redesign's bare markers (✓ ⚠ ▸ 🗑) and the older bracketed ones ([✓]/[✗]/[!]).</summary>
    public static LogLine Infer(string text)
    {
        var t = text.TrimStart();
        var level =
            t.StartsWith("🚀", StringComparison.Ordinal) || t.StartsWith("🗑", StringComparison.Ordinal) ? LogLevel.Accent
            : t.StartsWith("[✗]", StringComparison.Ordinal) || t.StartsWith("[!]", StringComparison.Ordinal)
              || t.StartsWith("⚠", StringComparison.Ordinal) ? LogLevel.Warn
            : t.StartsWith("[✓]", StringComparison.Ordinal) || t.StartsWith("✓", StringComparison.Ordinal) ? LogLevel.Ok
            : t.StartsWith("▸", StringComparison.Ordinal) ? LogLevel.Plain
            : t.StartsWith("Fido? GO", StringComparison.Ordinal) || t.StartsWith("Launching", StringComparison.Ordinal) ? LogLevel.Accent
            : LogLevel.Muted;
        return new LogLine(text, level);
    }
}
