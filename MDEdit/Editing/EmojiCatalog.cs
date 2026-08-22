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
    private static readonly Lazy<CatalogData> Data = new(Load);

    private sealed record CatalogData(
        Dictionary<string, string> ByShortcode,
        List<(string Shortcode, string Emoji)> Ordered,
        Dictionary<string, string> ByEmoji,
        HashSet<char> EmojiFirstChars,
        int MaxEmojiLength);

    /// <summary>Number of shortcodes in the catalogue. Exposed for tests and diagnostics.</summary>
    public static int Count => Data.Value.Ordered.Count;

    /// <summary>Every shortcode/emoji pair, in <c>Emoji.txt</c>'s file order. For the emoji picker.</summary>
    public static IReadOnlyList<(string Shortcode, string Emoji)> All => Data.Value.Ordered;

    public static bool TryGet(string shortcode, out string emoji)
        => Data.Value.ByShortcode.TryGetValue(shortcode, out emoji!);

    /// <summary>
    /// The reverse lookup: the shortcode to write for a literal emoji character (Requirements.md §3,
    /// Format → Convert Emoji to Shortcodes).
    /// </summary>
    /// <remarks>
    /// <b>Not simply the forward map inverted</b>, because aliases collide — <c>thumbsup</c> and
    /// <c>+1</c> are both 👍, <c>thumbsdown</c> and <c>-1</c> both 👎 — so one shortcode per
    /// character has to win, deterministically. <b>First listed in <c>Emoji.txt</c> wins</b>: the
    /// file is hand-ordered with the preferred name first, so that rule needs no second list to
    /// express the preference, and reordering the file is how you change the answer. Decided here
    /// rather than at the call site so every consumer gets the same one.
    /// </remarks>
    public static bool TryGetShortcode(string emoji, out string shortcode)
        => Data.Value.ByEmoji.TryGetValue(emoji, out shortcode!);

    /// <summary>
    /// Longest catalogue emoji in UTF-16 code units — a scanner must try lengths from this downward
    /// so a variation-selector form (❤️) wins over its bare base character (❤).
    /// </summary>
    public static int MaxEmojiLength => Data.Value.MaxEmojiLength;

    /// <summary>
    /// Whether any catalogue emoji begins with this character. Lets a scan reject the overwhelming
    /// majority of positions in O(1), instead of building a substring per candidate length at every
    /// character of the document.
    /// </summary>
    public static bool CouldStartEmoji(char c) => Data.Value.EmojiFirstChars.Contains(c);

    private static CatalogData Load()
    {
        var byShortcode = new Dictionary<string, string>(StringComparer.Ordinal);
        var ordered = new List<(string, string)>();
        var orderedIndex = new Dictionary<string, int>(StringComparer.Ordinal); // name -> its slot in `ordered`

        using var stream = typeof(EmojiCatalog).Assembly
            .GetManifestResourceStream("MDEdit.Resources.Emoji.txt");
        // Missing resource shouldn't take the editor down.
        if (stream is null) return new CatalogData(byShortcode, ordered, new(StringComparer.Ordinal), [], 0);

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

        // The reverse map is built from `ordered` after the forward pass rather than alongside it,
        // so a shortcode redefined later in the file contributes its final emoji, and file order —
        // which is what decides alias collisions — is the order actually seen here.
        var byEmoji = new Dictionary<string, string>(StringComparer.Ordinal);
        var firstChars = new HashSet<char>();
        int maxEmojiLength = 0;

        foreach (var (name, emoji) in ordered)
        {
            // First listed wins; see TryGetShortcode.
            if (!byEmoji.ContainsKey(emoji)) byEmoji[emoji] = name;
            firstChars.Add(emoji[0]);
            if (emoji.Length > maxEmojiLength) maxEmojiLength = emoji.Length;
        }

        return new CatalogData(byShortcode, ordered, byEmoji, firstChars, maxEmojiLength);
    }
}
