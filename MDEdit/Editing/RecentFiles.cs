using System.IO;

namespace MDEdit.Editing;

/// <summary>
/// Most-recently-used file list logic. These are pure list transformations returning a new list,
/// kept out of MainWindow so they can be unit-tested without a window; the caller owns persistence
/// (see <c>AppSettings.RecentFiles</c>) and the menu it drives.
/// </summary>
internal static class RecentFiles
{
    /// <summary>Maximum entries kept. Beyond this the oldest entries fall off the end.</summary>
    public const int MaxEntries = 10;

    /// <summary>
    /// Returns <paramref name="existing"/> with <paramref name="path"/> promoted to the front,
    /// de-duplicated, and capped at <see cref="MaxEntries"/>.
    /// </summary>
    public static List<string> Add(IEnumerable<string>? existing, string? path)
    {
        var sanitized = Sanitize(existing);
        if (Normalize(path) is not string added) return sanitized;

        var result = new List<string>(sanitized.Count + 1) { added };
        result.AddRange(sanitized.Where(p => !SamePath(p, added)));
        if (result.Count > MaxEntries)
            result.RemoveRange(MaxEntries, result.Count - MaxEntries);
        return result;
    }

    /// <summary>Returns <paramref name="existing"/> without <paramref name="path"/>.</summary>
    public static List<string> Remove(IEnumerable<string>? existing, string? path)
    {
        var sanitized = Sanitize(existing);
        return Normalize(path) is string removed
            ? sanitized.Where(p => !SamePath(p, removed)).ToList()
            : sanitized;
    }

    /// <summary>
    /// Drops blank, malformed, and duplicate entries and applies the cap. Run over the persisted
    /// list at startup: settings.json is plain JSON a user can hand-edit, and may also have been
    /// written by a version with a different cap.
    /// </summary>
    public static List<string> Sanitize(IEnumerable<string>? existing)
    {
        var result = new List<string>();
        if (existing is null) return result;

        foreach (var entry in existing)
        {
            if (Normalize(entry) is not string p) continue;
            if (result.Any(e => SamePath(e, p))) continue;
            result.Add(p);
            if (result.Count == MaxEntries) break;
        }
        return result;
    }

    // Entries are stored fully qualified so the same file reached by different spellings — a
    // relative command-line argument, a path containing "." or ".." — collapses to one entry.
    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Malformed (hand-edited settings.json, a path from a since-removed drive mapping):
            // drop the entry rather than let it break the whole list.
            return null;
        }
    }

    // Windows paths are case-insensitive, so "C:\A\B.md" and "c:\a\b.md" are one entry.
    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
