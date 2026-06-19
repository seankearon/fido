using System;

namespace Fido.ViewModels;

public enum LogLevel { Muted, Accent, Secondary, Error }

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
    public bool IsSecondary => Level == LogLevel.Secondary;
    public bool IsError => Level == LogLevel.Error;

    /// <summary>Picks a level from the line's marker prefix so callers can keep logging plain strings.</summary>
    public static LogLine Infer(string text)
    {
        var t = text.TrimStart();
        var level =
            t.StartsWith("🚀", StringComparison.Ordinal) ? LogLevel.Accent
            : t.StartsWith("[✗]", StringComparison.Ordinal) || t.StartsWith("[!]", StringComparison.Ordinal) ? LogLevel.Error
            : t.StartsWith("[✓]", StringComparison.Ordinal) ? LogLevel.Secondary
            : t.StartsWith("Fido? GO", StringComparison.Ordinal) || t.StartsWith("Launching", StringComparison.Ordinal) ? LogLevel.Accent
            : LogLevel.Muted;
        return new LogLine(text, level);
    }
}
