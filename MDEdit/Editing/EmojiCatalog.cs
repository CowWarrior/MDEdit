using System.IO;
using System.Text;

namespace MDEdit.Editing;

/// <summary>
/// Shortcode-to-emoji lookup, loaded once from the embedded <c>Resources/Emoji.txt</c> catalogue.
/// </summary>
/// <remarks>
/// The .NET BCL has no emoji shortcode table, so one has to be shipped. It lives in a text resource
/// rather than in code so that extending it is a data edit, and it is a curated subset rather than
/// the full ~1,800-entry set — everything here is versioned in git and carried in the ClickOnce
/// payload. Nothing in the code depends on the size of the set.
///
/// This lookup is also what keeps emoji detection unambiguous: only names actually in the catalogue
/// are treated as emoji, so ordinary text like "10:30:45" can never be mistaken for one. That is a
/// meaningfully stronger guarantee than the pattern-only constructs get (see the accepted false
/// positives on '^' and '~' in MarkdownSyntaxScriptTests).
/// </remarks>
internal static class EmojiCatalog
{
    // Dictionary for TryGet's O(1) lookup, plus the same entries as an ordered list for the emoji
    // picker to browse — Dictionary enumeration order is not a documented guarantee, so the picker
    // needs a real list rather than iterating Entries.Value directly. Built in one pass over the
    // same file so the two views can never drift apart.
    private static readonly Lazy<(Dictionary<string, string> ByShortcode, List<(string Shortcode, string Emoji)> Ordered)> Data = new(Load);

    /// <summary>Number of shortcodes in the catalogue. Exposed for tests and diagnostics.</summary>
    public static int Count => Data.Value.Ordered.Count;

    /// <summary>Every shortcode/emoji pair, in <c>Emoji.txt</c>'s file order. For the emoji picker.</summary>
    public static IReadOnlyList<(string Shortcode, string Emoji)> All => Data.Value.Ordered;

    public static bool TryGet(string shortcode, out string emoji)
        => Data.Value.ByShortcode.TryGetValue(shortcode, out emoji!);

    private static (Dictionary<string, string>, List<(string, string)>) Load()
    {
        var byShortcode = new Dictionary<string, string>(StringComparer.Ordinal);
        var ordered = new List<(string, string)>();
        var orderedIndex = new Dictionary<string, int>(StringComparer.Ordinal); // name -> its slot in `ordered`

        using var stream = typeof(EmojiCatalog).Assembly
            .GetManifestResourceStream("MDEdit.Resources.Emoji.txt");
        if (stream is null) return (byShortcode, ordered);   // Missing resource shouldn't take the editor down.

        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is string line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;

            // Split on the first whitespace run, so the file tolerates either spaces or tabs.
            var split = trimmed.IndexOfAny([' ', '\t']);
            if (split <= 0) continue;

            var name  = trimmed[..split];
            var emoji = trimmed[(split + 1)..].Trim();
            if (emoji.Length == 0) continue;

            byShortcode[name] = emoji;   // Last definition wins, so a duplicate line is an override.

            // Duplicate shortcode: update its existing slot in place (at its original position)
            // rather than appending a second entry, so `ordered` never disagrees with `byShortcode`.
            if (orderedIndex.TryGetValue(name, out var index))
                ordered[index] = (name, emoji);
            else
            {
                orderedIndex[name] = ordered.Count;
                ordered.Add((name, emoji));
            }
        }

        return (byShortcode, ordered);
    }
}
