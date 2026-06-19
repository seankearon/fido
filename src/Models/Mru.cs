using System;

namespace Fido.Models;

/// <summary>
/// Most-recently-used list helper: newest first, case-insensitively de-duplicated, capped.
/// The list is mutated in place so the same instance stored on <see cref="AppConfig"/> stays current.
/// </summary>
public static class Mru
{
    /// <summary>Most entries kept per list.</summary>
    public const int MaxItems = 12;

    /// <summary>
    /// Promotes <paramref name="value"/> to the front, removing any earlier (case-insensitive)
    /// occurrence and trimming the list to <paramref name="max"/>. Blank values are ignored.
    /// </summary>
    /// <returns><c>true</c> when the list was changed.</returns>
    public static bool Add(List<string> list, string? value, int max = MaxItems)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return false;

        var index = list.FindIndex(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
        if (index == 0) return false;   // already the newest entry

        if (index > 0) list.RemoveAt(index);
        list.Insert(0, value);
        if (list.Count > max) list.RemoveRange(max, list.Count - max);
        return true;
    }
}
