using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Document;

namespace MDEdit.Editing;

/// <summary>
/// An inline emphasis-style run (bold/italic/bold+italic/strikethrough) found on a single line.
/// Start/End are
/// absolute document offsets; End is exclusive (one past the closing marker's last character).
/// The opening marker occupies [Start, Start+MarkerLength) and the closing marker occupies
/// [End-MarkerLength, End) — both the same length since Markdown emphasis delimiters are symmetric.
/// </summary>
internal readonly record struct EmphasisSpan(int Start, int End, int MarkerLength);

/// <summary>
/// Shared line-level Markdown construct detection, used by both <see cref="MarkdownLineColorizer"/>
/// (coloring/sizing) and the live-preview element generators (marker hiding) so the two always
/// agree on what counts as a given construct — kept UI-free so it can be unit tested directly.
/// </summary>
internal static class MarkdownSyntax
{
    /// <summary>
    /// Detects an ATX heading ("#" through "######" followed by a space) at the start of a line.
    /// <paramref name="markerLength"/> is the length of "#…# " including the trailing space —
    /// the portion live preview hides once the caret leaves the line.
    /// </summary>
    public static bool TryGetHeadingLevel(TextDocument doc, DocumentLine line, out int level, out int markerLength)
    {
        level = 0;
        markerLength = 0;
        if (line.Length == 0) return false;

        int count = 0;
        while (count < line.Length && doc.GetCharAt(line.Offset + count) == '#') count++;
        if (count == 0 || count > 6 || count >= line.Length || doc.GetCharAt(line.Offset + count) != ' ')
            return false;

        level = count;
        markerLength = count + 1;
        return true;
    }

    // Same patterns and precedence as Markdown.xshd (bold+italic before bold before italic; no
    // wildcard quantifiers, character-class exclusion instead, to avoid catastrophic backtracking —
    // see the comments there). \G anchors each pattern to the exact scan position passed to Match,
    // so this doubles as a tiny non-overlapping lexer: at each offset, try patterns in priority
    // order, take the first that matches there, otherwise advance one character and retry.
    // Strikethrough sits last purely to mirror the XSHD's rule order — its '~' delimiter can't
    // collide with the star/underscore families, so its position in the list is immaterial.
    private static readonly (Regex Pattern, int MarkerLength)[] EmphasisPatterns =
    [
        (new Regex(@"\G\*{3}[^\*\n]+\*{3}"), 3),
        (new Regex(@"\G_{3}[^_\n]+_{3}"), 3),
        (new Regex(@"\G\*{2}[^\*\n]+\*{2}"), 2),
        (new Regex(@"\G_{2}[^_\n]+_{2}"), 2),
        (new Regex(@"\G\*[^\*\n]+\*"), 1),
        (new Regex(@"\G_[^_\n]+_"), 1),
        (new Regex(@"\G~{2}[^~\n]+~{2}"), 2),
    ];

    /// <summary>
    /// Scans a line for bold/italic/bold+italic/strikethrough runs, including mixed-delimiter
    /// nesting (e.g. "_**bold**_" or "**_italic_**" — the standard, unambiguous way to combine
    /// bold and italic as two nested runs, since CommonMark forbids nesting the same delimiter
    /// inside itself — and likewise "~~**text**~~"). Emphasis never crosses lines (matching
    /// Markdown.xshd's rules, which exclude '\n' from the content class), so this is line-scoped.
    /// </summary>
    public static IReadOnlyList<EmphasisSpan> FindEmphasisSpans(TextDocument doc, DocumentLine line)
    {
        var spans = new List<EmphasisSpan>();
        ScanEmphasis(doc.GetText(line), line.Offset, spans);
        return spans;
    }

    // Recurses into each match's inner content (between its opening and closing markers) to
    // find a nested run using a different delimiter family. This falls out for free rather
    // than needing special-casing: a match's content can never itself contain its own delimiter
    // (the pattern's own content class excludes it), so re-scanning that content with the same
    // pattern list can only ever find an other-delimiter — i.e. genuinely nested — run.
    private static void ScanEmphasis(string text, int baseOffset, List<EmphasisSpan> results)
    {
        int pos = 0;
        while (pos < text.Length)
        {
            var matched = false;
            foreach (var (pattern, markerLength) in EmphasisPatterns)
            {
                var m = pattern.Match(text, pos);
                if (!m.Success) continue;

                results.Add(new EmphasisSpan(baseOffset + pos, baseOffset + pos + m.Length, markerLength));

                var innerStart  = pos + markerLength;
                var innerLength = m.Length - 2 * markerLength;
                if (innerLength > 0)
                    ScanEmphasis(text.Substring(innerStart, innerLength), baseOffset + innerStart, results);

                pos += m.Length;
                matched = true;
                break;
            }
            if (!matched) pos++;
        }
    }
}
