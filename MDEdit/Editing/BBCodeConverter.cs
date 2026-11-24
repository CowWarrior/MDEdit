using System.Text;
using ICSharpCode.AvalonEdit.Document;

namespace MDEdit.Editing;

/// <summary>
/// Converts Markdown to World Anvil's BBCode dialect, for Edit → Copy as BBCode (Requirements.md §2).
/// Export only: Markdown stays the single source of truth, and nothing here touches the document.
/// </summary>
/// <remarks>
/// <b>One dialect, deliberately.</b> Every tag MDEdit emits is written in this file and nowhere else, so
/// a second dialect would be a refactor of one class rather than a hunt — but no abstraction is built for
/// a consumer that doesn't exist. Tag syntax was taken from World Anvil's BBCode tutorials (scraped copies
/// live in the git-ignored <c>HTTPScrape/WorldAnvil/</c> folder, never committed — they are copyrighted),
/// plus the owner's own usage for the undocumented checkbox tags <c>[]</c>/<c>[c]</c>.
/// <para>
/// <b>Detection is never re-implemented here.</b> Block constructs come from <see cref="MarkdownSyntax"/>'s
/// line detectors and inline ones from <see cref="MarkdownSyntax.TryMatchInline"/>, so a construct converts
/// exactly when the editor renders it, and anything the editor shows as plain text stays plain text.
/// </para>
/// <para>
/// Output uses "\n" line endings; the clipboard consumer is a browser text area, which normalizes them.
/// </para>
/// </remarks>
internal static class BBCodeConverter
{
    /// <summary>Converts a whole Markdown string.</summary>
    public static string Convert(string markdown)
    {
        var doc = new TextDocument(markdown);
        return Convert(doc, 1, doc.LineCount);
    }

    /// <summary>
    /// Converts lines <paramref name="firstLine"/>..<paramref name="lastLine"/> (1-based, inclusive),
    /// first widened so neither end cuts through a fenced code block or a table — half a table has no
    /// header row to pair with, and half a fence would emit code as prose.
    /// </summary>
    public static string Convert(TextDocument doc, int firstLine, int lastLine)
    {
        (firstLine, lastLine) = ExpandToWholeBlocks(doc, firstLine, lastLine);

        var output = new List<string>();
        var lists  = new Stack<(int Indent, bool Ordered)>();

        int n = firstLine;
        while (n <= lastLine)
        {
            var line = doc.GetLineByNumber(n);
            var text = doc.GetText(line);

            if (TryGetListItem(doc, line, text, out var item))
            {
                OpenListFor(item, lists, output);
                output.Add((item.Prefix + Inline(item.Content)).TrimEnd());
                n++;
                continue;
            }

            // A blank line between two items is a loose list, not two lists — closing here would
            // restart an [ol]'s numbering at 1.
            if (lists.Count > 0 && IsBlank(text) && NextNonBlankIsListItem(doc, n, lastLine))
            {
                n++;
                continue;
            }
            CloseLists(lists, output, -1);

            if (MarkdownSyntax.IsFenceDelimiterLine(doc, line)
                && MarkdownSyntax.TryGetEnclosingFenceBlock(doc, n, out int fenceStart, out int fenceEnd)
                && fenceStart == n)
            {
                bool closed = fenceEnd > n && MarkdownSyntax.IsFenceDelimiterLine(doc, doc.GetLineByNumber(fenceEnd));
                int contentEnd = closed ? fenceEnd - 1 : fenceEnd;
                var code = new List<string>();
                for (int c = n + 1; c <= contentEnd; c++) code.Add(EmojiToShortcodes(doc.GetText(doc.GetLineByNumber(c))));
                // Content verbatim — no inline conversion and no [noparse], brackets being fine inside
                // [code] — except emoji, which become shortcodes rather than icons a code block can't hold.
                output.Add("[code]" + string.Join("\n", code) + "[/code]");
                n = fenceEnd + 1;
                continue;
            }

            if (MarkdownSyntax.TryGetTableBlock(doc, n, out int tableStart, out int tableEnd) && tableStart == n)
            {
                AppendTable(doc, tableStart, tableEnd, output);
                n = tableEnd + 1;
                continue;
            }

            if (MarkdownSyntax.TryGetHeadingLevel(doc, line, out int level, out int headingMarker))
            {
                // Every level passes through, h5/h6 included — World Anvil documents h1–h4, but the owner
                // styles the rest with CSS.
                output.Add($"[h{level}]{Inline(text[headingMarker..].Trim())}[/h{level}]");
                n++;
                continue;
            }

            if (MarkdownSyntax.IsHorizontalRule(text))
            {
                output.Add("[hr]");
                n++;
                continue;
            }

            if (MarkdownSyntax.TryGetBlockquoteMarkerLength(doc, line, out _, out _))
            {
                int end = n;
                while (end < lastLine
                       && MarkdownSyntax.TryGetBlockquoteMarkerLength(doc, doc.GetLineByNumber(end + 1), out _, out _))
                    end++;

                // Strip one level and convert what's inside as a document of its own, which handles
                // nested quotes, lists and headings within a quote with no extra code.
                var inner = new List<string>();
                for (int q = n; q <= end; q++) inner.Add(StripOneQuoteLevel(doc.GetText(doc.GetLineByNumber(q))));
                output.Add("[quote]" + Convert(string.Join("\n", inner)) + "[/quote]");
                n = end + 1;
                continue;
            }

            output.Add(ParagraphLine(doc, n, lastLine, text));
            n++;
        }

        CloseLists(lists, output, -1);
        return StripUnstorable(string.Join("\n", output));
    }

    /// <summary>
    /// Converts one line's worth of inline Markdown. Recursive, unlike the editor's flat span scanners,
    /// so nesting the editor renders as two runs — a link inside bold, bold inside a link — keeps both.
    /// </summary>
    internal static string Inline(string text)
    {
        var result = new StringBuilder();
        var plain  = new StringBuilder();

        int pos = 0;
        while (pos < text.Length)
        {
            int escape = MarkdownSyntax.EscapeLength(text, pos);
            if (escape > 0)
            {
                plain.Append(text[pos + 1]);
                pos += escape;
                continue;
            }

            if (MarkdownSyntax.TryMatchInline(text, pos, out var match))
            {
                FlushPlain(plain, result);
                result.Append(Render(text, match));
                pos += match.Length;
                continue;
            }

            // A literal emoji character exports exactly as its shortcode would.
            int emoji = MarkdownSyntax.EmojiCharacterLength(text, pos, out var shortcode);
            if (emoji > 0)
            {
                FlushPlain(plain, result);
                result.Append(ExportEmoji(shortcode, shortcode));
                pos += emoji;
                continue;
            }

            plain.Append(text[pos]);
            pos++;
        }

        FlushPlain(plain, result);
        return result.ToString();
    }

    private static string Render(string text, InlineMatch m)
    {
        var content = text.Substring(m.ContentStart, m.ContentLength);
        switch (m.Kind)
        {
            case InlineConstruct.Code:
                // Code content is literal, so it is neither converted nor [noparse]-wrapped — emoji aside,
                // as in a fenced block. World Anvil renders [code] as a block even mid-sentence; accepted.
                return "[code]" + EmojiToShortcodes(content) + "[/code]";

            case InlineConstruct.Emphasis:
                return Wrap(m.Style, Inline(content));

            case InlineConstruct.Underline:
                return "[u]" + Inline(content) + "[/u]";

            case InlineConstruct.Link:
                return $"[url:{ImagePathResolver.Normalize(m.Url!)}]{Inline(content)}[/url]";

            case InlineConstruct.ReferenceLink:
                // No inline URL to carry; reference definitions aren't resolved, so only the label survives.
                return Inline(content);

            case InlineConstruct.Image:
                var target = ImagePathResolver.Normalize(m.Url!);
                if (ImagePathResolver.TryResolveRemote(m.Url!, out _))
                    return $"[img:{target}]";
                // A local image has to be uploaded to World Anvil by hand, which hands back a complete
                // [img:ID] tag to paste — so the placeholder is visible text to replace whole, not a
                // half-built tag.
                return content.Length > 0
                    ? $"[b]IMAGE: {target} ({content})[/b]"
                    : $"[b]IMAGE: {target}[/b]";

            case InlineConstruct.Emoji:
                // Through the emoji, not the written name, so an alias (:+1:) finds the icon mapped
                // under the catalogue's first-listed name (thumbsup).
                var canonical = EmojiCatalog.TryGetShortcode(m.Emoji!, out var first) ? first : content;
                return ExportEmoji(canonical, content);

            default:
                return content;
        }
    }

    /// <summary>
    /// World Anvil refuses to save an article containing emoji, so each one exports as an icon: a Font
    /// Awesome / RPG-Awesome class in a [section] tag (Grandmaster tier — the trailing space before
    /// [/section] is required, or nothing displays), or one of World Anvil's free checklist tags. An emoji
    /// with no icon keeps the shortcode the author wrote, as text: nothing lost, easy to fix by hand.
    /// </summary>
    private static string ExportEmoji(string canonicalShortcode, string writtenShortcode)
    {
        if (!WorldAnvilIcons.TryGet(canonicalShortcode, out var icon)) return $":{writtenShortcode}:";
        return icon.StartsWith('[') ? icon : $"[section:{icon}] [/section]";
    }

    /// <summary>
    /// Code keeps its text literally, but an icon tag can't go inside [code] — so a catalogued emoji
    /// there becomes its shortcode instead.
    /// </summary>
    private static string EmojiToShortcodes(string code)
    {
        StringBuilder? result = null;
        int copied = 0;
        for (int pos = 0; pos < code.Length; pos++)
        {
            int length = MarkdownSyntax.EmojiCharacterLength(code, pos, out var shortcode);
            if (length == 0) continue;

            result ??= new StringBuilder(code.Length + 16);
            result.Append(code, copied, pos - copied).Append(':').Append(shortcode).Append(':');
            pos += length - 1;
            copied = pos + 1;
        }
        return result is null ? code : result.Append(code, copied, code.Length - copied).ToString();
    }

    /// <summary>
    /// The last line of defence: removes whatever World Anvil cannot store that the conversion above
    /// left behind — an emoji the catalogue doesn't know. World Anvil's refusal names UTF-16 and emoji,
    /// read here as characters outside the Basic Multilingual Plane (every surrogate pair, which is
    /// where nearly all emoji live), plus the variation selectors and zero-width joiner that only
    /// exist to build emoji. Other BMP symbols (★, ✓, →) are ordinary text and are kept.
    /// </summary>
    internal static string StripUnstorable(string text)
    {
        static bool Unstorable(char c) => char.IsSurrogate(c) || c is '︎' or '️' or '‍';

        if (!text.Any(Unstorable)) return text;
        var kept = new StringBuilder(text.Length);
        foreach (char c in text)
            if (!Unstorable(c)) kept.Append(c);
        return kept.ToString();
    }

    // Bold+italic is one run in Markdown (***x***) and two tags here, nested in a fixed order.
    private static string Wrap(InlineStyles style, string inner) => style switch
    {
        InlineStyles.Bold | InlineStyles.Italic => $"[b][i]{inner}[/i][/b]",
        InlineStyles.Bold          => $"[b]{inner}[/b]",
        InlineStyles.Italic        => $"[i]{inner}[/i]",
        InlineStyles.Strikethrough => $"[s]{inner}[/s]",
        // World Anvil restricts [mark] to its Grandmaster tier — emitted regardless, by the owner's choice.
        InlineStyles.Highlight     => $"[mark]{inner}[/mark]",
        InlineStyles.Superscript   => $"[sup]{inner}[/sup]",
        InlineStyles.Subscript     => $"[sub]{inner}[/sub]",
        _                          => inner,
    };

    /// <summary>
    /// Writes out a stretch of plain text. HTML comments become World Anvil's hidden comments, and any
    /// stretch containing '[' is wrapped in [noparse] — "[c]" or "[]" in ordinary prose would otherwise
    /// render as a checkbox, and an unknown tag's fate is World Anvil's to decide.
    /// </summary>
    private static void FlushPlain(StringBuilder plain, StringBuilder result)
    {
        if (plain.Length == 0) return;
        var text = plain.ToString().Replace("<!--", "/*").Replace("-->", "*/");
        plain.Clear();
        result.Append(text.Contains('[') ? $"[noparse]{text}[/noparse]" : text);
    }

    /// <summary>
    /// A paragraph line, with a Markdown hard break — two trailing spaces, or a trailing backslash —
    /// becoming [br] when another non-blank line follows. Any other line break is copied as it is.
    /// </summary>
    private static string ParagraphLine(TextDocument doc, int n, int lastLine, string text)
    {
        if (IsBlank(text)) return "";

        bool nextIsText = n < lastLine && !IsBlank(doc.GetText(doc.GetLineByNumber(n + 1)));
        var trimmed = text.TrimEnd();

        if (nextIsText && text.EndsWith("  ", StringComparison.Ordinal))
            return Inline(trimmed) + "[br]";
        // A lone trailing backslash; "\\" at the end is an escaped backslash, not a break.
        if (nextIsText && trimmed.EndsWith('\\') && !trimmed.EndsWith(@"\\", StringComparison.Ordinal))
            return Inline(trimmed[..^1]) + "[br]";

        return Inline(trimmed);
    }

    private static void AppendTable(TextDocument doc, int start, int end, List<string> output)
    {
        output.Add("[table]");
        for (int r = start; r <= end; r++)
        {
            if (r == start + 1) continue;   // the delimiter row; World Anvil has no column alignment
            var text = doc.GetText(doc.GetLineByNumber(r));
            var tag  = r == start ? "th" : "td";

            output.Add("[tr]");
            foreach (var (cellStart, length) in MarkdownSyntax.GetTableCells(text))
                output.Add($"[{tag}]{Inline(text.Substring(cellStart, length))}[/{tag}]");
            output.Add("[/tr]");
        }
        output.Add("[/table]");
    }

    private readonly record struct ListItem(int Indent, bool Ordered, string Prefix, string Content);

    // Task before bullet: a task line is also a bullet line, as the generators know too.
    private static bool TryGetListItem(TextDocument doc, DocumentLine line, string text, out ListItem item)
    {
        int indent = IndentWidth(text);

        if (MarkdownSyntax.TryGetTaskListMarker(doc, line, out int taskOffset, out int taskLength, out bool isChecked))
        {
            // [c], never [x]: World Anvil's [x] draws a "times" cross, so a passed-through Markdown x
            // would turn every finished task into a failed one.
            var box = isChecked ? "[c]" : "[]";
            item = new ListItem(indent, false, $"- {box} ", text[(taskOffset - line.Offset + taskLength)..].TrimStart());
            return true;
        }

        if (MarkdownSyntax.TryGetBulletListMarker(doc, line, out int bulletOffset))
        {
            item = new ListItem(indent, false, "- ", text[(bulletOffset - line.Offset + 1)..].TrimStart());
            return true;
        }

        if (MarkdownSyntax.TryGetNumberedListMarker(doc, line, out int numberOffset, out int numberLength))
        {
            // World Anvil numbers [ol] items itself, so the source's own numbers are dropped.
            item = new ListItem(indent, true, "- ", text[(numberOffset - line.Offset + numberLength)..].TrimStart());
            return true;
        }

        item = default;
        return false;
    }

    // Nesting follows indentation: deeper opens a nested list, shallower closes back to it, and a
    // different list type at the same depth closes one list and opens the other.
    private static void OpenListFor(ListItem item, Stack<(int Indent, bool Ordered)> lists, List<string> output)
    {
        CloseLists(lists, output, item.Indent);
        if (lists.Count > 0 && lists.Peek().Indent == item.Indent && lists.Peek().Ordered != item.Ordered)
            output.Add(CloseTag(lists.Pop().Ordered));

        if (lists.Count == 0 || lists.Peek().Indent < item.Indent)
        {
            lists.Push((item.Indent, item.Ordered));
            output.Add(item.Ordered ? "[ol]" : "[ul]");
        }
    }

    /// <summary>Closes every open list deeper than <paramref name="indent"/>; -1 closes them all.</summary>
    private static void CloseLists(Stack<(int Indent, bool Ordered)> lists, List<string> output, int indent)
    {
        while (lists.Count > 0 && lists.Peek().Indent > indent)
            output.Add(CloseTag(lists.Pop().Ordered));
    }

    private static string CloseTag(bool ordered) => ordered ? "[/ol]" : "[/ul]";

    private static bool NextNonBlankIsListItem(TextDocument doc, int n, int lastLine)
    {
        for (int next = n + 1; next <= lastLine; next++)
        {
            var line = doc.GetLineByNumber(next);
            var text = doc.GetText(line);
            if (IsBlank(text)) continue;
            return TryGetListItem(doc, line, text, out _);
        }
        return false;
    }

    private static (int First, int Last) ExpandToWholeBlocks(TextDocument doc, int first, int last)
    {
        foreach (var edge in new[] { first, last })
        {
            if (MarkdownSyntax.TryGetEnclosingFenceBlock(doc, edge, out int fs, out int fe)
                || MarkdownSyntax.TryGetTableBlock(doc, edge, out fs, out fe))
            {
                first = Math.Min(first, fs);
                last  = Math.Max(last, fe);
            }
        }
        return (first, last);
    }

    // One '>' and at most one following space — the rest of the line, deeper markers included,
    // belongs to the quote's content.
    private static string StripOneQuoteLevel(string text)
    {
        var rest = text[1..];
        return rest.StartsWith(' ') ? rest[1..] : rest;
    }

    private static int IndentWidth(string text)
    {
        int width = 0;
        foreach (char c in text)
        {
            if (c == ' ') width++;
            else if (c == '\t') width += 4;
            else break;
        }
        return width;
    }

    private static bool IsBlank(string text) => string.IsNullOrWhiteSpace(text);
}
