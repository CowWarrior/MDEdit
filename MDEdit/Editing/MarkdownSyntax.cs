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

/// <summary>
/// Which construct an emphasis run is. Only the cases a consumer has to tell apart are named:
/// superscript/subscript need baseline styling from <c>MarkdownLineColorizer</c>, and their
/// single-character markers make them indistinguishable from italic by <see cref="EmphasisSpan.MarkerLength"/>
/// alone. Everything else is <see cref="Other"/> — the marker-hiding generator treats all runs alike.
/// </summary>
internal enum EmphasisKind { Other, Superscript, Subscript }

/// <summary>
/// A superscript or subscript run's <em>content</em> — the text between the markers, which is what
/// gets raised or lowered; the markers themselves stay on the baseline.
/// </summary>
internal readonly record struct ScriptSpan(int ContentStart, int ContentEnd, bool IsSuperscript);

/// <summary>
/// An underline run: "&lt;u&gt;" occupies [Start, ContentStart), the underlined text occupies
/// [ContentStart, ContentEnd), and "&lt;/u&gt;" occupies [ContentEnd, End). Like
/// <see cref="LinkSpan"/> and unlike <see cref="EmphasisSpan"/>, the two markers are different
/// lengths, so both ends are carried explicitly rather than derived from one marker length.
/// </summary>
internal readonly record struct UnderlineSpan(int Start, int ContentStart, int ContentEnd, int End);

/// <summary>
/// A recognized emoji shortcode run: <c>:joy:</c> occupies [Start, End), and <see cref="Emoji"/> is
/// the character it stands for. Carries the replacement text so the generator never re-does the
/// catalogue lookup.
/// </summary>
internal readonly record struct EmojiSpan(int Start, int End, string Emoji);

/// <summary>
/// A literal emoji <em>character</em> run and the shortcode it should be written as — the reverse of
/// <see cref="EmojiSpan"/>, for Format → Convert Emoji to Shortcodes (Requirements.md §3).
/// </summary>
internal readonly record struct EmojiCharacterSpan(int Start, int End, string Shortcode);

/// <summary>
/// A link or image found on a single line: "[text](url)", "![alt](url)", or "[text][ref]".
/// Start/End are absolute document offsets (End exclusive) for the whole construct; TextStart/
/// TextEnd bound the visible label ("text"/"alt") — live preview keeps [TextStart, TextEnd)
/// visible and hides the rest: [Start, TextStart) is the "[" or "![" prefix, [TextEnd, End) is
/// the "](url)" or "][ref]" suffix. Unlike <see cref="EmphasisSpan"/>'s symmetric MarkerLength,
/// prefix and suffix lengths differ — the URL/reference portion has no fixed width.
/// IsImage distinguishes "![alt](url)" (rendered as a picture by ImageElementGenerator when the
/// target is a resolvable local file); Url carries the raw parenthesized target for the two
/// inline forms — the <see cref="EmojiSpan"/> carry-the-payload precedent — and is null for
/// reference links, which have no inline URL to resolve.
/// </summary>
internal readonly record struct LinkSpan(int Start, int TextStart, int TextEnd, int End, bool IsImage, string? Url);

/// <summary>
/// The inline constructs that apply to one run of rendered text. A flags enum because they compose:
/// <c>**bold `code`**</c> yields a run carrying both <see cref="Bold"/> and <see cref="Code"/>.
/// </summary>
internal enum InlineStyles
{
    None          = 0,
    Bold          = 1 << 0,
    Italic        = 1 << 1,
    Code          = 1 << 2,
    Strikethrough = 1 << 3,
    Highlight     = 1 << 4,
    Underline     = 1 << 5,
    Link          = 1 << 6,
    Superscript   = 1 << 7,
    Subscript     = 1 << 8,
}

/// <summary>
/// A stretch of text to render with one set of styles, markers already removed and emoji shortcodes
/// already substituted — see <see cref="MarkdownSyntax.FormatInline"/>.
/// </summary>
internal readonly record struct InlineRun(string Text, InlineStyles Styles);

internal enum FenceKind { None, Backtick, Tilde }

/// <summary>
/// A table column's alignment, from the delimiter row's colons: ":---" and "---" are
/// <see cref="Left"/> (GFM's default alignment is left, so the two render the same),
/// "---:" is <see cref="Right"/>, ":---:" is <see cref="Center"/>.
/// </summary>
internal enum TableColumnAlignment { Left, Center, Right }

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

    /// <summary>
    /// Detects a blockquote marker at the start of a line: one or more '&gt;' characters, each
    /// optionally followed by a single space — so "&gt; text", "&gt;text", "&gt;&gt; text", and
    /// "&gt; &gt; text" (nested blockquotes) all count. This generalizes the simple "first
    /// character is '&gt;'" rule <see cref="MarkdownLineColorizer"/> has always used for its
    /// (always-on, not live-preview-gated) italic styling, so both now share one definition.
    /// <paramref name="markerLength"/> is the length of that leading run — the portion live
    /// preview hides on every line except the one the caret is on. <paramref name="depth"/> is
    /// the number of '&gt;' characters found (nesting level) — used by
    /// <see cref="BlockquoteMarkerElementGenerator"/> to draw one indent bar per level.
    /// </summary>
    public static bool TryGetBlockquoteMarkerLength(TextDocument doc, DocumentLine line, out int markerLength, out int depth)
    {
        markerLength = 0;
        depth = 0;
        if (line.Length == 0 || doc.GetCharAt(line.Offset) != '>') return false;

        int pos = 0;
        while (pos < line.Length && doc.GetCharAt(line.Offset + pos) == '>')
        {
            depth++;
            pos++;
            if (pos < line.Length && doc.GetCharAt(line.Offset + pos) == ' ') pos++;
        }

        markerLength = pos;
        return true;
    }

    /// <summary>
    /// Detects a bullet list marker: optional leading whitespace (nesting indent), then '-', '*',
    /// or '+' followed by a space or tab. <paramref name="markerOffset"/> is the absolute document
    /// offset of the bullet character itself — live preview replaces just that one character with
    /// a rendered, indented "•" glyph (the following space and any leading indent stay as real
    /// text, so spacing and nested-item indentation come from the document itself). Unlike Markdown.xshd's
    /// unanchored ListMarker rule ("[-\*\+]\s" anywhere on a line), this is line-start-only —
    /// replacing a mid-sentence "a - b" dash with a bullet would corrupt prose display, so the
    /// marker replacement is deliberately stricter than the coloring. Horizontal-rule lines
    /// ("---", "* * *") are excluded via the same <see cref="IsHorizontalRule"/> check
    /// <see cref="MarkdownLineColorizer"/> uses, so the two can never disagree about whether a
    /// line is a rule or a one-item list.
    /// </summary>
    /// <summary>
    /// Detects a task list item — a bullet item whose content starts with "[ ]" or "[x]".
    /// <paramref name="markerOffset"/>/<paramref name="markerLength"/> bound the bullet character
    /// through the closing ']' (e.g. all five characters of "- [ ]"), not the following space:
    /// live preview replaces that whole range with a single checkbox glyph, so the bullet and the
    /// box are one element rather than a "•" followed by a box.
    /// </summary>
    /// <remarks>
    /// A task line is also a bullet line by <see cref="TryGetBulletListMarker"/>'s definition — that
    /// is deliberate and correct, since it *is* one — so any caller that renders bullet markers has
    /// to check this first and stand aside (see <see cref="BulletListMarkerElementGenerator"/>).
    /// The trailing space is optional so that a just-inserted "- [ ]" whose trailing space has been
    /// trimmed can still be toggled.
    /// </remarks>
    public static bool TryGetTaskListMarker(TextDocument doc, DocumentLine line,
        out int markerOffset, out int markerLength, out bool isChecked)
    {
        markerLength = 0;
        isChecked    = false;
        if (!TryGetBulletListMarker(doc, line, out markerOffset)) return false;

        // The bullet char is followed by whitespace (TryGetBulletListMarker guarantees it); skip it.
        int pos = markerOffset - line.Offset + 1;
        while (pos < line.Length && doc.GetCharAt(line.Offset + pos) is ' ' or '\t') pos++;

        if (pos + 2 >= line.Length) return false;
        if (doc.GetCharAt(line.Offset + pos) != '[') return false;
        if (doc.GetCharAt(line.Offset + pos + 2) != ']') return false;

        switch (doc.GetCharAt(line.Offset + pos + 1))
        {
            case ' ':          isChecked = false; break;
            case 'x' or 'X':   isChecked = true;  break;
            default:           return false;
        }

        // Whitespace after the box, or end of line for an item with no text yet.
        if (pos + 3 < line.Length && doc.GetCharAt(line.Offset + pos + 3) is not (' ' or '\t')) return false;

        markerLength = pos + 3 - (markerOffset - line.Offset);
        return true;
    }

    public static bool TryGetBulletListMarker(TextDocument doc, DocumentLine line, out int markerOffset)
    {
        markerOffset = 0;
        int pos = 0;
        while (pos < line.Length && doc.GetCharAt(line.Offset + pos) is ' ' or '\t') pos++;
        if (pos + 1 >= line.Length) return false;

        char c = doc.GetCharAt(line.Offset + pos);
        if (c != '-' && c != '*' && c != '+') return false;
        if (doc.GetCharAt(line.Offset + pos + 1) is not (' ' or '\t')) return false;
        if (IsHorizontalRule(doc.GetText(line))) return false;

        markerOffset = line.Offset + pos;
        return true;
    }

    /// <summary>
    /// Detects a numbered list marker: optional leading whitespace (nesting indent), then one or
    /// more digits and a '.', followed by a space or tab — mirroring Markdown.xshd's second
    /// ListMarker rule ("\d+\.\s") but line-start-only, same reasoning as
    /// <see cref="TryGetBulletListMarker"/> ("version 1. note" mid-prose must not be treated as
    /// a marker). <paramref name="markerOffset"/>/<paramref name="markerLength"/> bound the
    /// digits + '.' only (not the following space) — live preview replaces that range with an
    /// identical-text element that just adds leading indent, keeping the number visible: a "1. "
    /// marker's rendered form is its source form, so unlike bullets there's no glyph substitution,
    /// only the indent.
    /// </summary>
    public static bool TryGetNumberedListMarker(TextDocument doc, DocumentLine line, out int markerOffset, out int markerLength)
    {
        markerOffset = 0;
        markerLength = 0;
        int pos = 0;
        while (pos < line.Length && doc.GetCharAt(line.Offset + pos) is ' ' or '\t') pos++;

        int digits = 0;
        while (pos + digits < line.Length && char.IsAsciiDigit(doc.GetCharAt(line.Offset + pos + digits))) digits++;
        if (digits == 0) return false;

        int dot = pos + digits;
        if (dot + 1 >= line.Length) return false;
        if (doc.GetCharAt(line.Offset + dot) != '.') return false;
        if (doc.GetCharAt(line.Offset + dot + 1) is not (' ' or '\t')) return false;

        markerOffset = line.Offset + pos;
        markerLength = digits + 1;
        return true;
    }

    /// <summary>
    /// Whether an entire line is a horizontal rule: three or more characters, all the same one
    /// of '-'/'*'/'_' optionally interleaved with spaces, starting at column 0. Shared by
    /// <see cref="MarkdownLineColorizer"/> (gray hrule styling — this used to be its private
    /// helper) and <see cref="TryGetBulletListMarker"/> (a "- - -" line is a rule, not a
    /// one-item bullet list, even though it starts with "- ").
    /// </summary>
    public static bool IsHorizontalRule(string text)
    {
        if (text.Length < 3) return false;
        char c = text[0];
        if (c != '-' && c != '*' && c != '_') return false;
        foreach (char ch in text)
            if (ch != c && ch != ' ') return false;
        return true;
    }

    // Mirrors Markdown.xshd's rule order, where the ListMarker rules precede every emphasis and
    // link rule: on a bullet line like "* item*", the leading "* " is a list marker, never an
    // italic opener, so both scanners below start past it. The skipped length is the marker
    // character plus its following whitespace char — the exact 2 characters the XSHD's
    // "[-\*\+]\s" rule consumes (leading indent is included in the skip; nothing in it could
    // match anyway).
    private static int LeadingBulletMarkerLength(TextDocument doc, DocumentLine line)
        => TryGetBulletListMarker(doc, line, out int markerOffset) ? markerOffset - line.Offset + 2 : 0;

    // CommonMark's escapable set: any ASCII punctuation character. A backslash before anything else
    // (a letter, a digit, a space) is a literal backslash and escapes nothing.
    private const string EscapablePunctuation = @"!""#$%&'()*+,-./:;<=>?@[\]^_`{|}~";

    /// <summary>
    /// The number of characters a backslash escape occupies at <paramref name="pos"/> — 2 for a
    /// real escape, 0 when there is none.
    /// </summary>
    /// <remarks>
    /// <b>Every scanner in this file consumes this before trying its patterns</b>, which is what
    /// stops an escaped character opening a run: on <c>\*not emphasis\*</c> the scan steps over both
    /// backslash pairs, so neither <c>*</c> is ever offered to a pattern as a start position. That
    /// single rule covers openers for emphasis, links, underline and emoji alike.
    /// <para>
    /// Escaped <i>closers</i> need the patterns' own content classes instead (see
    /// <see cref="EmphasisPatterns"/>), since a closing delimiter is matched by the pattern rather
    /// than visited by the scan.
    /// </para>
    /// <para>
    /// Inline code is the deliberate exception: CommonMark gives code spans precedence over
    /// backslash escapes, so a backslash inside backticks is literal and
    /// <see cref="InlineCodePattern"/> stays escape-blind.
    /// </para>
    /// </remarks>
    private static int EscapeLength(string text, int pos)
        => text[pos] == '\\'
        && pos + 1 < text.Length
        && EscapablePunctuation.Contains(text[pos + 1])
            ? 2 : 0;

    // Same patterns and precedence as Markdown.xshd (bold+italic before bold before italic; no
    // wildcard quantifiers, character-class exclusion instead, to avoid catastrophic backtracking —
    // see the comments there). \G anchors each pattern to the exact scan position passed to Match,
    // so this doubles as a tiny non-overlapping lexer: at each offset, try patterns in priority
    // order, take the first that matches there, otherwise advance one character and retry.
    // Strikethrough, highlight, and inline code sit last purely to mirror the XSHD's rule order —
    // their '~'/'='/'`' delimiters can't collide with the star/underscore families, so their
    // position in the list is immaterial. RecurseIntoContent is false for inline code because a code span's
    // content is literal text (CommonMark gives code spans precedence over emphasis — the "**"
    // in "`**not bold**`" is just two asterisks); the lexer's leftmost-wins scan already gives
    // an earlier-opening backtick that precedence at the top level, matching how AvalonEdit
    // resolves the XSHD's rules by earliest match position.
    // Declared before EmphasisPatterns on purpose: static fields initialize in declaration order, so
    // putting this after the table would leave the table's entry holding null at runtime, not merely
    // warn. Named and shared rather than inlined because FindEmojiSpans needs the same definition to
    // skip code spans — a shortcode inside backticks is literal text.
    private static readonly Regex InlineCodePattern = new(@"\G`[^`\n]+`");

    // Lowercase only, matching the catalogue's spelling; '+' and '-' are allowed for ":+1:"/":-1:".
    // A match is only an emoji if EmojiCatalog recognizes the name, so this pattern being loose
    // costs nothing — "10:30:45" contains ":30:" but "30" is not a shortcode.
    private static readonly Regex EmojiPattern = new(@"\G:([a-z0-9_+-]+):");

    // Two CommonMark rules are folded into the patterns themselves, both matched by Markdown.xshd:
    //
    // 1. ESCAPED CLOSERS. Each content class is "(?:\\.|[^\\<delim>\n])+" — an escape pair, or any
    //    character that is neither the delimiter nor a backslash. So "*a \* b*" runs to the real
    //    closer instead of stopping at the escaped one. The two branches are deliberately DISJOINT
    //    (the plain branch excludes '\\'): overlapping branches under a '+' is the classic
    //    catastrophic-backtracking shape, and a run of backslashes with no closer would hit it. The
    //    escaped-OPENER half is not here — the scanners step over escapes before matching, see
    //    EscapeLength.
    //
    // 2. INTRAWORD '_'. The three '_' patterns carry "(?<!\w)" and "(?!\w)" so a '_' flanked by word
    //    characters can neither open nor close, which is what keeps "snake_case_name" plain text.
    //    '*' deliberately has no such guard: CommonMark restricts only '_' this way, and "foo*bar*"
    //    IS emphasis. This is a simplification of the full left/right-flanking algorithm, which also
    //    weighs punctuation on each side — it fixes the case that silently misrenders ordinary
    //    identifiers without pulling a whole delimiter-run parser into a line-local lexer.
    //    The lookbehind reads real text: Regex.Match(input, startat) can inspect before startat,
    //    unlike Match(input, beginning, length). ScanEmphasis's recursion passes a SUBSTRING, where
    //    position 0 has nothing to look behind at — harmless, because inner content always begins
    //    right after a delimiter character, which is never a word character anyway.
    private static readonly (Regex Pattern, int MarkerLength, bool RecurseIntoContent, EmphasisKind Kind)[] EmphasisPatterns =
    [
        (new Regex(@"\G\*{3}(?:\\.|[^\\\*\n])+\*{3}"), 3, true, EmphasisKind.Other),
        (new Regex(@"\G(?<!\w)_{3}(?:\\.|[^\\_\n])+_{3}(?!\w)"), 3, true, EmphasisKind.Other),
        (new Regex(@"\G\*{2}(?:\\.|[^\\\*\n])+\*{2}"), 2, true, EmphasisKind.Other),
        (new Regex(@"\G(?<!\w)_{2}(?:\\.|[^\\_\n])+_{2}(?!\w)"), 2, true, EmphasisKind.Other),
        (new Regex(@"\G\*(?:\\.|[^\\\*\n])+\*"), 1, true, EmphasisKind.Other),
        (new Regex(@"\G(?<!\w)_(?:\\.|[^\\_\n])+_(?!\w)"), 1, true, EmphasisKind.Other),
        (new Regex(@"\G~{2}(?:\\.|[^\\~\n])+~{2}"), 2, true, EmphasisKind.Other),
        // Subscript MUST stay below strikethrough: both open with '~', and at a position starting
        // "~~" the two-character pattern has to win — the same precedence bold has over italic.
        (new Regex(@"\G~(?:\\.|[^\\~\n])+~"), 1, true, EmphasisKind.Subscript),
        (new Regex(@"\G\^(?:\\.|[^\\\^\n])+\^"), 1, true, EmphasisKind.Superscript),
        (new Regex(@"\G={2}(?:\\.|[^\\=\n])+={2}"), 2, true, EmphasisKind.Other),
        (InlineCodePattern, 1, false, EmphasisKind.Other),
    ];

    // Same order as Markdown.xshd (images before plain links, since an "![...]" run must not
    // also be considered starting one character later at its "["; inline links before reference
    // links, since both start with "[" at the same position and Markdown.xshd tries the
    // parenthesized form first). Each pattern's single capturing group is the visible
    // text/alt label, used to compute LinkSpan.TextStart/TextEnd from the match.
    // Underline is the one construct here that isn't Markdown at all: no dialect has an underline
    // syntax (underlining is reserved for links by convention), so Requirements.md §3 specs it as
    // literal inline HTML, riding on Markdown's pass-through of inline HTML. Deliberately strict —
    // lowercase "<u>" only, no attributes, no whitespace inside the tag — because anything looser
    // starts guessing at arbitrary HTML, which this editor does not parse. The content class
    // excludes '<' (so a nested tag ends the run) and '\n', same no-wildcard-quantifier discipline
    // as Markdown.xshd's rules.
    private static readonly Regex UnderlinePattern = new(@"\G<u>[^<\n]+</u>");

    private const int UnderlineOpenLength  = 3;   // "<u>"
    private const int UnderlineCloseLength = 4;   // "</u>"

    private static readonly (Regex Pattern, bool IsImage)[] LinkPatterns =
    [
        (new Regex(@"\G!\[([^\]\n]+)\]\(([^\)\n]+)\)"), true),
        (new Regex(@"\G\[([^\]\n]+)\]\(([^\)\n]+)\)"), false),
        (new Regex(@"\G\[([^\]\n]+)\]\[[^\]\n]+\]"), false),   // reference form — no URL group
    ];

    private static bool TryMatchLinkPattern(string text, int pos, out int length)
    {
        foreach (var (pattern, _) in LinkPatterns)
        {
            var m = pattern.Match(text, pos);
            if (m.Success) { length = m.Length; return true; }
        }
        length = 0;
        return false;
    }

    private static bool TryMatchEmphasisPattern(string text, int pos, out int length)
    {
        foreach (var (pattern, _, _, _) in EmphasisPatterns)
        {
            var m = pattern.Match(text, pos);
            if (m.Success) { length = m.Length; return true; }
        }
        length = 0;
        return false;
    }

    /// <summary>
    /// Scans a line for bold/italic/bold+italic/strikethrough/inline-code runs, including
    /// mixed-delimiter nesting (e.g. "_**bold**_" or "**_italic_**" — the standard, unambiguous
    /// way to combine bold and italic as two nested runs, since CommonMark forbids nesting the
    /// same delimiter inside itself — and likewise "~~**text**~~" or "**a `code` b**"). Inline
    /// code content is never recursed into: it's literal text, so "`**x**`" is one code span
    /// with no nested bold. Links/images are skipped over rather than matched into — "[**not
    /// bold**](url)" is one opaque Link run to Markdown.xshd (a flat Rule with no nested
    /// RuleSet, unlike Bold/Italic's Span), so this never reports a Bold span starting inside a
    /// link's "[...]" text; see <see cref="FindLinkSpans"/> for the reverse case. A leading
    /// bullet list marker is skipped too (see <see cref="LeadingBulletMarkerLength"/> — on
    /// "* item*" the leading "* " is a list marker, not an italic opener). Emphasis never
    /// crosses lines (matching Markdown.xshd's rules, which exclude '\n' from the content
    /// class), so this is line-scoped.
    /// </summary>
    public static IReadOnlyList<EmphasisSpan> FindEmphasisSpans(TextDocument doc, DocumentLine line)
    {
        var spans = new List<EmphasisSpan>();
        foreach (var (span, _) in ScanLine(doc, line))
            spans.Add(span);
        return spans;
    }

    /// <summary>
    /// The superscript and subscript runs on a line, as the content between their markers.
    /// Shares <see cref="FindEmphasisSpans"/>'s single scan rather than re-parsing, so the
    /// baseline styling and the marker hiding can never disagree about what a run is.
    /// </summary>
    public static IReadOnlyList<ScriptSpan> FindScriptSpans(TextDocument doc, DocumentLine line)
    {
        var spans = new List<ScriptSpan>();
        foreach (var (span, kind) in ScanLine(doc, line))
        {
            if (kind is EmphasisKind.Other) continue;
            // Markers are one character on both sides for both constructs.
            spans.Add(new ScriptSpan(span.Start + 1, span.End - 1, kind is EmphasisKind.Superscript));
        }
        return spans;
    }

    /// <summary>
    /// The underline (<c>&lt;u&gt;…&lt;/u&gt;</c>) runs on a line.
    /// </summary>
    /// <remarks>
    /// Deliberately *not* skip-aware of the emphasis and link scanners, unlike those two are of each
    /// other. Underline is a container rather than an opaque run: the whole point of
    /// <c>&lt;u&gt;**bold**&lt;/u&gt;</c> is that the bold still applies, so the scanners are meant to
    /// find their own runs inside it independently. Their markers never sit at the same offset as
    /// <c>&lt;u&gt;</c>/<c>&lt;/u&gt;</c>, so the generators can't collide either.
    /// </remarks>
    public static IReadOnlyList<UnderlineSpan> FindUnderlineSpans(TextDocument doc, DocumentLine line)
    {
        var results = new List<UnderlineSpan>();
        ScanUnderlines(doc.GetText(line), LeadingBulletMarkerLength(doc, line), line.Offset, results);
        return results;
    }

    private static void ScanUnderlines(string text, int startPos, int baseOffset, List<UnderlineSpan> results)
    {
        int pos = startPos;
        while (pos < text.Length)
        {
            int escape = EscapeLength(text, pos);
            if (escape > 0) { pos += escape; continue; }

            var m = UnderlinePattern.Match(text, pos);
            if (m.Success)
            {
                results.Add(new UnderlineSpan(
                    baseOffset + pos,
                    baseOffset + pos + UnderlineOpenLength,
                    baseOffset + pos + m.Length - UnderlineCloseLength,
                    baseOffset + pos + m.Length));
                pos += m.Length;
                continue;
            }
            pos++;
        }
    }

    /// <summary>
    /// The recognized emoji shortcode runs on a line. A <c>:name:</c> whose name is not in
    /// <see cref="EmojiCatalog"/> is ordinary text and is not reported.
    /// </summary>
    /// <remarks>
    /// Skips inline code spans, since a shortcode inside backticks is literal — the one skip this
    /// scanner needs. It deliberately does *not* skip emphasis or links: emoji inside bold should
    /// still be emoji, the same reasoning as <see cref="FindUnderlineSpans"/>.
    /// </remarks>
    public static IReadOnlyList<EmojiSpan> FindEmojiSpans(TextDocument doc, DocumentLine line)
    {
        var results = new List<EmojiSpan>();
        ScanEmoji(doc.GetText(line), LeadingBulletMarkerLength(doc, line), line.Offset, results);
        return results;
    }

    private static void ScanEmoji(string text, int startPos, int baseOffset, List<EmojiSpan> results)
    {
        int pos = startPos;
        while (pos < text.Length)
        {
            var code = InlineCodePattern.Match(text, pos);
            if (code.Success)
            {
                pos += code.Length;
                continue;
            }

            // After the code-span skip, not before: a backslash inside backticks is literal, so the
            // escape rule must not reach into a code span. Same ordering reason everywhere else.
            int escape = EscapeLength(text, pos);
            if (escape > 0) { pos += escape; continue; }

            var m = EmojiPattern.Match(text, pos);
            if (m.Success && EmojiCatalog.TryGet(m.Groups[1].Value, out var emoji))
            {
                results.Add(new EmojiSpan(baseOffset + pos, baseOffset + pos + m.Length, emoji));
                pos += m.Length;
                continue;
            }

            pos++;
        }
    }

    /// <summary>
    /// Resolves a standalone string — a table cell — into the styled runs it should render as, with
    /// every marker removed and every recognized emoji shortcode substituted.
    /// </summary>
    /// <remarks>
    /// String-based rather than line-based because a cell is a *substring* of a line, so no document
    /// offset describes it. It reuses the very same scanners the line-based entry points do (they
    /// were split into string cores for exactly this), which is what stops a cell disagreeing with
    /// the identical text written outside a table.
    /// <para>
    /// <b>Both halves of table rendering must go through this.</b> Column widths are measured from
    /// the runs, not from the raw text — that is the whole reason cells could not render formatting
    /// before: measuring raw text while displaying marker-free text misaligns every column by the
    /// width of the hidden markers.
    /// </para>
    /// <para>
    /// Nesting composes for free. The emphasis scan reports nested runs as a flat list, and each
    /// one ORs its style over its own content range, so <c>**_x_**</c> lands as Bold|Italic on the
    /// characters both cover without anything having to reconstruct a tree.
    /// </para>
    /// <para>
    /// Two deliberate boundaries. An image renders as its alt text rather than a picture — a decoded
    /// bitmap inside a fixed-width measured cell is a separate problem. And backslash escapes keep
    /// their backslash visible, matching how they render everywhere else in the editor today;
    /// hiding it is a whole-editor change, not a table one.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<InlineRun> FormatInline(string text)
    {
        if (text.Length == 0) return [];

        var styles = new InlineStyles[text.Length];
        var hidden = new bool[text.Length];
        var substitution = new string?[text.Length];

        var emphasis = new List<(EmphasisSpan Span, EmphasisKind Kind)>();
        ScanEmphasis(text, 0, emphasis);
        foreach (var (span, kind) in emphasis)
        {
            var style = ClassifyEmphasis(text[span.Start], span.MarkerLength, kind);
            Hide(hidden, span.Start, span.Start + span.MarkerLength);
            Hide(hidden, span.End - span.MarkerLength, span.End);
            Apply(styles, span.Start + span.MarkerLength, span.End - span.MarkerLength, style);
        }

        var links = new List<LinkSpan>();
        ScanLinks(text, 0, 0, links);
        foreach (var link in links)
        {
            Hide(hidden, link.Start, link.TextStart);
            Hide(hidden, link.TextEnd, link.End);
            // An image's alt text is a description, not a link, so it carries no link styling.
            if (!link.IsImage) Apply(styles, link.TextStart, link.TextEnd, InlineStyles.Link);
        }

        var underlines = new List<UnderlineSpan>();
        ScanUnderlines(text, 0, 0, underlines);
        foreach (var u in underlines)
        {
            Hide(hidden, u.Start, u.ContentStart);
            Hide(hidden, u.ContentEnd, u.End);
            Apply(styles, u.ContentStart, u.ContentEnd, InlineStyles.Underline);
        }

        var emoji = new List<EmojiSpan>();
        ScanEmoji(text, 0, 0, emoji);
        foreach (var e in emoji)
        {
            // The replacement is emitted at the span's first index and the whole run hidden, so the
            // shortcode's own characters never reach the output.
            substitution[e.Start] = e.Emoji;
            Hide(hidden, e.Start, e.End);
        }

        var runs = new List<InlineRun>();
        var buffer = new System.Text.StringBuilder();
        var current = InlineStyles.None;

        for (int i = 0; i < text.Length; i++)
        {
            var replacement = substitution[i];
            if (replacement is null && hidden[i]) continue;

            if (styles[i] != current)
            {
                if (buffer.Length > 0) { runs.Add(new InlineRun(buffer.ToString(), current)); buffer.Clear(); }
                current = styles[i];
            }
            buffer.Append(replacement ?? text[i].ToString());
        }
        if (buffer.Length > 0) runs.Add(new InlineRun(buffer.ToString(), current));

        return runs;
    }

    private static void Hide(bool[] hidden, int start, int end)
    {
        for (int i = start; i < end; i++) hidden[i] = true;
    }

    private static void Apply(InlineStyles[] styles, int start, int end, InlineStyles style)
    {
        for (int i = start; i < end; i++) styles[i] |= style;
    }

    // The delimiter character plus its length identifies the construct — EmphasisSpan carries only a
    // marker length, which cannot tell '*' from '`' or '='. Superscript and subscript are the two
    // the scanner already distinguishes, since their single-character markers make them
    // indistinguishable from italic by length alone.
    private static InlineStyles ClassifyEmphasis(char delimiter, int markerLength, EmphasisKind kind)
    {
        if (kind is EmphasisKind.Superscript) return InlineStyles.Superscript;
        if (kind is EmphasisKind.Subscript) return InlineStyles.Subscript;

        return delimiter switch
        {
            '`' => InlineStyles.Code,
            '=' => InlineStyles.Highlight,
            '~' => InlineStyles.Strikethrough,
            _   => markerLength switch
            {
                3 => InlineStyles.Bold | InlineStyles.Italic,
                2 => InlineStyles.Bold,
                _ => InlineStyles.Italic,
            },
        };
    }

    /// <summary>
    /// The literal emoji characters on a line that have a catalogue shortcode, with the shortcode to
    /// write for each. The detection half of Convert Emoji to Shortcodes (Requirements.md §3).
    /// </summary>
    /// <remarks>
    /// The mirror image of <see cref="FindEmojiSpans"/>, and it skips inline code for the same
    /// reason: a shortcode inside backticks is literal, so an emoji inside backticks must stay one —
    /// converting it would produce a shortcode that this editor then refuses to render back.
    /// Symmetry between the two directions is the rule; <c>MarkdownFormatter</c> extends it to
    /// fenced code blocks, which are a block property this line-local scan cannot see.
    /// <para>
    /// <b>Longest match wins at each position</b>, which matters because a variation-selector form
    /// (❤️, two code units) has its bare base character (❤) as a prefix — shortest-first would
    /// convert the base and strand the selector. Positions are rejected in O(1) via
    /// <see cref="EmojiCatalog.CouldStartEmoji"/> before any substring is built, since almost no
    /// character in a document starts an emoji.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<EmojiCharacterSpan> FindEmojiCharacterSpans(TextDocument doc, DocumentLine line)
    {
        var results = new List<EmojiCharacterSpan>();
        var text = doc.GetText(line);

        // From 0, deliberately unlike the scanners above: the leading-bullet skip exists so "* " is
        // not read as an italic opener, which has no bearing on finding emoji characters.
        int pos = 0;
        while (pos < text.Length)
        {
            var code = InlineCodePattern.Match(text, pos);
            if (code.Success) { pos += code.Length; continue; }

            if (!EmojiCatalog.CouldStartEmoji(text[pos])) { pos++; continue; }

            bool matched = false;
            int longest = Math.Min(EmojiCatalog.MaxEmojiLength, text.Length - pos);
            for (int length = longest; length >= 1; length--)
            {
                if (!EmojiCatalog.TryGetShortcode(text.Substring(pos, length), out var shortcode)) continue;

                results.Add(new EmojiCharacterSpan(line.Offset + pos, line.Offset + pos + length, shortcode));
                pos += length;
                matched = true;
                break;
            }
            if (!matched) pos++;
        }
        return results;
    }

    private static List<(EmphasisSpan Span, EmphasisKind Kind)> ScanLine(TextDocument doc, DocumentLine line)
    {
        var results = new List<(EmphasisSpan, EmphasisKind)>();
        int skip = LeadingBulletMarkerLength(doc, line);
        ScanEmphasis(doc.GetText(line)[skip..], line.Offset + skip, results);
        return results;
    }

    // Recurses into each match's inner content (between its opening and closing markers) to
    // find a nested run using a different delimiter family. This falls out for free rather
    // than needing special-casing: a match's content can never itself contain its own delimiter
    // (the pattern's own content class excludes it), so re-scanning that content with the same
    // pattern list can only ever find an other-delimiter — i.e. genuinely nested — run.
    private static void ScanEmphasis(string text, int baseOffset, List<(EmphasisSpan, EmphasisKind)> results)
    {
        int pos = 0;
        while (pos < text.Length)
        {
            // Before any pattern is tried: an escaped delimiter is literal text and must never be
            // offered as a run opener. See EscapeLength.
            int escape = EscapeLength(text, pos);
            if (escape > 0) { pos += escape; continue; }

            var matched = false;
            foreach (var (pattern, markerLength, recurseIntoContent, kind) in EmphasisPatterns)
            {
                var m = pattern.Match(text, pos);
                if (!m.Success) continue;

                results.Add((new EmphasisSpan(baseOffset + pos, baseOffset + pos + m.Length, markerLength), kind));

                var innerStart  = pos + markerLength;
                var innerLength = m.Length - 2 * markerLength;
                if (recurseIntoContent && innerLength > 0)
                    ScanEmphasis(text.Substring(innerStart, innerLength), baseOffset + innerStart, results);

                pos += m.Length;
                matched = true;
                break;
            }

            if (!matched && TryMatchLinkPattern(text, pos, out int linkLength))
            {
                pos += linkLength;
                matched = true;
            }

            if (!matched) pos++;
        }
    }

    /// <summary>
    /// Scans a line for links and images, in Markdown.xshd's precedence order (see
    /// <see cref="LinkPatterns"/>). Bold/italic/strikethrough/inline-code runs are skipped over
    /// rather than searched inside — a "**[not-a-link](url)**" run is entirely swallowed by the
    /// Bold rule before a Link rule ever gets a chance to match starting partway through it, so
    /// this never reports a link starting inside one of those. This is the asymmetric-marker
    /// counterpart to <see cref="FindEmphasisSpans"/>: it does not recurse into a matched link's
    /// own text (Markdown.xshd's Link rule is flat, so "[**bold**](url)" is one opaque link, not
    /// a link containing a separately-colored Bold run). A leading bullet list marker is skipped
    /// the same way (see <see cref="LeadingBulletMarkerLength"/>) — without it, a line like
    /// "* [x](url) *note*" would be swallowed whole by the italic skip before the link is found.
    /// </summary>
    public static IReadOnlyList<LinkSpan> FindLinkSpans(TextDocument doc, DocumentLine line)
    {
        var results = new List<LinkSpan>();
        ScanLinks(doc.GetText(line), LeadingBulletMarkerLength(doc, line), line.Offset, results);
        return results;
    }

    private static void ScanLinks(string text, int startPos, int baseOffset, List<LinkSpan> results)
    {
        int pos = startPos;
        while (pos < text.Length)
        {
            if (TryMatchEmphasisPattern(text, pos, out int skipLength))
            {
                pos += skipLength;
                continue;
            }

            // An escaped '[' is literal, so "\[not a link\](url)" stays text. Note the link patterns
            // themselves keep their plain content classes: an escaped ']' inside link text is a
            // rarer case, and widening those classes would change what LinkSpan.Url carries, which
            // ImagePathResolver and three generators read. Deliberate boundary, not an oversight.
            int escape = EscapeLength(text, pos);
            if (escape > 0) { pos += escape; continue; }

            var matched = false;
            foreach (var (pattern, isImage) in LinkPatterns)
            {
                var m = pattern.Match(text, pos);
                if (!m.Success) continue;

                var label = m.Groups[1];
                results.Add(new LinkSpan(
                    baseOffset + pos,
                    baseOffset + label.Index,
                    baseOffset + label.Index + label.Length,
                    baseOffset + pos + m.Length,
                    isImage,
                    m.Groups[2].Success ? m.Groups[2].Value : null));

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

    /// <summary>
    /// Whether a line is shaped like a table row: '|' at column 0, and the last non-whitespace
    /// character also '|'. Deliberately stricter than GFM, which allows the outer pipes to be
    /// omitted and the row to be indented — the same reasoning as the line-start-only list
    /// markers: "a | b" prose must never render as a table row, and requiring both outer pipes
    /// makes an accidental table nearly impossible.
    /// </summary>
    public static bool IsTableRowLine(TextDocument doc, DocumentLine line)
    {
        if (line.Length < 2) return false;
        if (doc.GetCharAt(line.Offset) != '|') return false;
        int end = line.Length - 1;
        while (end > 0 && doc.GetCharAt(line.Offset + end) is ' ' or '\t') end--;
        return end > 0 && doc.GetCharAt(line.Offset + end) == '|';
    }

    /// <summary>
    /// The offsets (relative to the line's text) of every cell-splitting '|'. A pipe preceded
    /// by a backslash ("\|") is escaped — a literal '|' inside cell content, GFM's one
    /// table-specific escape — and does not split. Shared by <see cref="GetTableCells"/> and
    /// <see cref="MarkdownLineColorizer"/>'s pipe dimming so the two can never disagree about
    /// which pipes are structure and which are content.
    /// </summary>
    public static IReadOnlyList<int> GetTablePipeOffsets(string text)
    {
        var pipes = new List<int>();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == '|') pipes.Add(i);
        }
        return pipes;
    }

    /// <summary>
    /// Splits a table row line's text into its cells: the trimmed segments between consecutive
    /// unescaped '|' characters. Start/Length are relative to <paramref name="text"/>; a cell's
    /// raw content may still contain "\|" escapes — display code unescapes them, detection code
    /// never needs to. Text before the first pipe or after the last is not a cell
    /// (<see cref="IsTableRowLine"/> requires both outer pipes, so on a real row that text is
    /// empty or trailing whitespace).
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> GetTableCells(string text)
    {
        var pipes = GetTablePipeOffsets(text);
        var cells = new List<(int, int)>();
        for (int p = 0; p + 1 < pipes.Count; p++)
        {
            int start = pipes[p] + 1;
            int end   = pipes[p + 1];
            while (start < end && text[start] is ' ' or '\t') start++;
            while (end > start && text[end - 1] is ' ' or '\t') end--;
            cells.Add((start, end - start));
        }
        return cells;
    }

    /// <summary>
    /// Whether a line is a table delimiter row: a row line whose every cell is ":---"-shaped —
    /// optional colon, three or more dashes, optional colon. Three dashes minimum rather than
    /// GFM's one, the same "min N chars" discipline as Markdown.xshd's patterns and
    /// <see cref="IsHorizontalRule"/>'s three-character floor.
    /// </summary>
    public static bool IsTableDelimiterLine(TextDocument doc, DocumentLine line)
    {
        if (!IsTableRowLine(doc, line)) return false;
        var text  = doc.GetText(line);
        var cells = GetTableCells(text);
        if (cells.Count == 0) return false;
        foreach (var (start, length) in cells)
        {
            if (!IsDelimiterCell(text, start, length)) return false;
        }
        return true;
    }

    private static bool IsDelimiterCell(string text, int start, int length)
    {
        int i = start, end = start + length;
        if (i < end && text[i] == ':') i++;
        int dashes = 0;
        while (i < end && text[i] == '-') { dashes++; i++; }
        if (dashes < 3) return false;
        if (i < end && text[i] == ':') i++;
        return i == end;
    }

    /// <summary>
    /// The per-column alignments declared by a delimiter row's colons (see
    /// <see cref="TableColumnAlignment"/>). Callers pass the delimiter line's text; a body row
    /// with more cells than the delimiter has columns simply has no declared alignment for the
    /// extras, and renderers default those to left.
    /// </summary>
    public static IReadOnlyList<TableColumnAlignment> GetTableAlignments(string delimiterText)
    {
        var cells  = GetTableCells(delimiterText);
        var result = new TableColumnAlignment[cells.Count];
        for (int i = 0; i < cells.Count; i++)
        {
            var (start, length) = cells[i];
            if (length == 0) continue;
            bool left  = delimiterText[start] == ':';
            bool right = delimiterText[start + length - 1] == ':';
            result[i] = left && right ? TableColumnAlignment.Center
                      : right         ? TableColumnAlignment.Right
                      :                 TableColumnAlignment.Left;
        }
        return result;
    }

    /// <summary>
    /// Finds the table (1-based, inclusive line numbers) that <paramref name="lineNumber"/>
    /// falls within. A table is a header row line, a delimiter line directly below it with the
    /// same cell count (GFM's pairing rule — a mismatch means no table at all), and every
    /// contiguous row line after that; a blank or non-row line ends it. Like fences the block
    /// spans lines, but unlike fence pairing it's a contiguity property, so this walks the
    /// line's neighborhood rather than the whole document. The header is the *topmost* row
    /// line in the contiguous run whose next line is a matching delimiter — row-shaped prose
    /// sitting directly above a real table is left out rather than poisoning the pairing.
    /// </summary>
    public static bool TryGetTableBlock(TextDocument doc, int lineNumber, out int startLine, out int endLine)
    {
        startLine = endLine = 0;
        if (lineNumber < 1 || lineNumber > doc.LineCount) return false;
        if (!IsTableRowLine(doc, doc.GetLineByNumber(lineNumber))) return false;

        int top = lineNumber;
        while (top > 1 && IsTableRowLine(doc, doc.GetLineByNumber(top - 1))) top--;

        for (int h = top; h <= lineNumber && h < doc.LineCount; h++)
        {
            var delimiter = doc.GetLineByNumber(h + 1);
            if (!IsTableDelimiterLine(doc, delimiter)) continue;
            if (GetTableCells(doc.GetText(doc.GetLineByNumber(h))).Count
                != GetTableCells(doc.GetText(delimiter)).Count) continue;

            int end = h + 1;
            while (end < doc.LineCount && IsTableRowLine(doc, doc.GetLineByNumber(end + 1))) end++;

            // lineNumber ≥ h by the loop bounds, and end ≥ lineNumber because every line from
            // h down to lineNumber is a row line (that's how `top` was found), so the block
            // always contains the queried line.
            startLine = h;
            endLine   = end;
            return true;
        }
        return false;
    }
}
