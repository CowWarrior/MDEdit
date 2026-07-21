using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Document;

namespace MDEdit.Editing;

/// <summary>
/// An inline emphasis-style run (bold/italic/bold+italic/strikethrough/inline code) found on a
/// single line. Start/End are
/// absolute document offsets; End is exclusive (one past the closing marker's last character).
/// The opening marker occupies [Start, Start+MarkerLength) and the closing marker occupies
/// [End-MarkerLength, End) — both the same length since Markdown emphasis delimiters are symmetric.
/// </summary>
internal readonly record struct EmphasisSpan(int Start, int End, int MarkerLength);

internal enum FenceKind { None, Backtick, Tilde }

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
    // Strikethrough and inline code sit last purely to mirror the XSHD's rule order — their
    // '~'/'`' delimiters can't collide with the star/underscore families, so their position in
    // the list is immaterial. RecurseIntoContent is false for inline code because a code span's
    // content is literal text (CommonMark gives code spans precedence over emphasis — the "**"
    // in "`**not bold**`" is just two asterisks); the lexer's leftmost-wins scan already gives
    // an earlier-opening backtick that precedence at the top level, matching how AvalonEdit
    // resolves the XSHD's rules by earliest match position.
    private static readonly (Regex Pattern, int MarkerLength, bool RecurseIntoContent)[] EmphasisPatterns =
    [
        (new Regex(@"\G\*{3}[^\*\n]+\*{3}"), 3, true),
        (new Regex(@"\G_{3}[^_\n]+_{3}"), 3, true),
        (new Regex(@"\G\*{2}[^\*\n]+\*{2}"), 2, true),
        (new Regex(@"\G_{2}[^_\n]+_{2}"), 2, true),
        (new Regex(@"\G\*[^\*\n]+\*"), 1, true),
        (new Regex(@"\G_[^_\n]+_"), 1, true),
        (new Regex(@"\G~{2}[^~\n]+~{2}"), 2, true),
        (new Regex(@"\G`[^`\n]+`"), 1, false),
    ];

    /// <summary>
    /// Scans a line for bold/italic/bold+italic/strikethrough/inline-code runs, including
    /// mixed-delimiter nesting (e.g. "_**bold**_" or "**_italic_**" — the standard, unambiguous
    /// way to combine bold and italic as two nested runs, since CommonMark forbids nesting the
    /// same delimiter inside itself — and likewise "~~**text**~~" or "**a `code` b**"). Inline
    /// code content is never recursed into: it's literal text, so "`**x**`" is one code span
    /// with no nested bold. Emphasis never crosses lines (matching Markdown.xshd's rules, which
    /// exclude '\n' from the content class), so this is line-scoped.
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
            foreach (var (pattern, markerLength, recurseIntoContent) in EmphasisPatterns)
            {
                var m = pattern.Match(text, pos);
                if (!m.Success) continue;

                results.Add(new EmphasisSpan(baseOffset + pos, baseOffset + pos + m.Length, markerLength));

                var innerStart  = pos + markerLength;
                var innerLength = m.Length - 2 * markerLength;
                if (recurseIntoContent && innerLength > 0)
                    ScanEmphasis(text.Substring(innerStart, innerLength), baseOffset + innerStart, results);

                pos += m.Length;
                matched = true;
                break;
            }
            if (!matched) pos++;
        }
    }

    // Matches Markdown.xshd's fenced-code-block Begin/End patterns ("^```" / "^~~~") exactly: a
    // literal 3-char prefix at the very start of the line, not a minimum-length regex — so this
    // deliberately doesn't implement the full CommonMark fence spec (longer fences, indentation),
    // same scope tradeoff as the rest of this file.
    private static FenceKind GetFenceKind(TextDocument doc, DocumentLine line)
    {
        if (line.Length < 3) return FenceKind.None;
        char c0 = doc.GetCharAt(line.Offset);
        if (c0 != '`' && c0 != '~') return FenceKind.None;
        if (doc.GetCharAt(line.Offset + 1) != c0 || doc.GetCharAt(line.Offset + 2) != c0) return FenceKind.None;
        return c0 == '`' ? FenceKind.Backtick : FenceKind.Tilde;
    }

    /// <summary>
    /// Whether <paramref name="line"/> is itself a fenced-code-block delimiter line (opening or
    /// closing). Live preview's fence-hiding generator checks this first, before the document-wide
    /// walk in <see cref="TryGetEnclosingFenceBlock"/>, so the common case — a line that's neither
    /// a fence nor inside one — stays a cheap O(1) check.
    /// </summary>
    public static bool IsFenceDelimiterLine(TextDocument doc, DocumentLine line) => GetFenceKind(doc, line) != FenceKind.None;

    /// <summary>
    /// Finds the fenced code block (1-based, inclusive start/end line numbers) that
    /// <paramref name="lineNumber"/> falls within — whether that line is the opening fence, the
    /// closing fence, or a content line in between. An unterminated fence (no matching closing
    /// line before the end of the document) is treated as extending to the document's last line,
    /// matching how an unclosed Markdown.xshd Span still colors the rest of the document.
    /// Unlike every other construct in this file, fence pairing is a document-wide property (the
    /// Nth same-kind fence line closes the block opened by the (N-1)th) rather than something
    /// determinable from a single line in isolation, so this walks from the start of the document
    /// — callers on the live-preview render path use <see cref="IsFenceDelimiterLine"/> first to
    /// avoid paying that cost for the vast majority of lines, which are neither a fence nor
    /// (for the purposes of this method's callers) need this at all.
    /// </summary>
    public static bool TryGetEnclosingFenceBlock(TextDocument doc, int lineNumber, out int startLine, out int endLine)
    {
        startLine = endLine = 0;
        var openKind = FenceKind.None;
        int openStart = 0;

        for (int n = 1; n <= doc.LineCount; n++)
        {
            var kind = GetFenceKind(doc, doc.GetLineByNumber(n));
            if (openKind == FenceKind.None)
            {
                if (kind != FenceKind.None) { openKind = kind; openStart = n; }
            }
            else if (kind == openKind)
            {
                if (lineNumber >= openStart && lineNumber <= n)
                {
                    startLine = openStart;
                    endLine = n;
                    return true;
                }
                openKind = FenceKind.None;
            }
        }

        if (openKind != FenceKind.None && lineNumber >= openStart)
        {
            startLine = openStart;
            endLine = doc.LineCount;
            return true;
        }

        return false;
    }
}
