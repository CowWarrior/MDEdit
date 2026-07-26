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
    private static readonly Lazy<Dictionary<string, string>> Entries = new(Load);

    /// <summary>Number of shortcodes in the catalogue. Exposed for tests and diagnostics.</summary>
    public static int Count => Entries.Value.Count;

    public static bool TryGet(string shortcode, out string emoji)
        => Entries.Value.TryGetValue(shortcode, out emoji!);

    private static Dictionary<string, string> Load()
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);

        using var stream = typeof(EmojiCatalog).Assembly
            .GetManifestResourceStream("MDEdit.Resources.Emoji.txt");
        if (stream is null) return entries;   // Missing resource shouldn't take the editor down.

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

            entries[name] = emoji;   // Last definition wins, so a duplicate line is an override.
        }

        return entries;
    }
}
