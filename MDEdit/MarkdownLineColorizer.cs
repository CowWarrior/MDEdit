using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using MDEdit.Editing;
using MDEdit.Services;

namespace MDEdit;

internal sealed class MarkdownLineColorizer : DocumentColorizingTransformer
{
    // The active editor mode's per-element styling (Requirements.md §6), pushed by
    // MainWindow.ApplyActiveModeStyles — which mode is active is MainWindow's business, not this
    // class's. Everything this colorizer draws (headings, blockquote, horizontal rule, and the
    // table's structural dimming) is resolved from here, so the same settings drive both this and
    // the XSHD-driven half through MarkdownHighlighting.
    private ModeStyles _styles = ModeStyles.SourceDefaults();
    private bool _isDark;

    // Resolution allocates brushes and typefaces, and ColorizeLine runs per visible line on every
    // redraw — so results are cached per element key and dropped whenever either input changes.
    private readonly Dictionary<string, ResolvedStyle> _resolved = new(StringComparer.Ordinal);

    public ModeStyles Styles
    {
        get => _styles;
        set { _styles = value; _resolved.Clear(); }
    }

    // Set by MainWindow.ApplyTheme; a TextView.Redraw() afterwards re-runs ColorizeLine.
    public bool IsDark
    {
        get => _isDark;
        set
        {
            if (_isDark == value) return;
            _isDark = value;
            _resolved.Clear();
        }
    }

    /// <summary>
    /// The resolved style for one element in the active mode and theme. Public so the background
    /// renderers can draw in exactly the colour their construct's text uses — see
    /// <c>HorizontalRuleRenderer</c> and <c>BlockquoteAccentBarRenderer</c>, which hold a reference
    /// to this instance rather than carrying their own copy of a colour that must agree with it.
    /// </summary>
    public ResolvedStyle Resolve(string elementKey)
    {
        if (_resolved.TryGetValue(elementKey, out var cached)) return cached;

        var resolved = StyleResolver.Resolve(elementKey, _styles, _isDark);
        _resolved[elementKey] = resolved;
        return resolved;
    }

    // Set by MainWindow's live-preview toggle. Now only affects the revealed-source font swap
    // below: heading scaling used to be gated here too, and is instead expressed as a per-mode
    // default (source mode simply doesn't scale headings), which is what retired HeadingScale.
    public bool LivePreviewEnabled { get; set; }

    // Set by MainWindow alongside the generators' caret state (live preview only): the
    // caret's line and offset, so revealed construct lines and revealed inline spans can
    // swap back to the source font. −1 when untracked.
    public int CaretLine { get; set; } = -1;
    public int CaretOffset { get; set; } = -1;

    // The editor's source-mode mono family, captured once by MainWindow from the XAML stack.
    public FontFamily? SourceFontFamily { get; set; }

    protected override void ColorizeLine(DocumentLine line)
    {
        var doc = CurrentContext.Document;
        if (line.Length == 0) return;

        // Returns whether it swapped the WHOLE line to the source font — the per-element family
        // override below must not undo that, or a revealed heading would snap back to the document
        // font the instant the caret reached it.
        bool revealed = ApplyRevealedSourceFont(doc, line);

        if (MarkdownSyntax.TryGetHeadingLevel(doc, line, out int level, out _))
        {
            ApplyLineStyle(line, StyledElements.Heading(level), revealed);
            return;
        }

        if (MarkdownSyntax.TryGetBlockquoteMarkerLength(doc, line, out _, out _))
        {
            ApplyLineStyle(line, StyledElements.Blockquote, revealed);
            return;
        }

        var text = doc.GetText(line);
        if (MarkdownSyntax.IsHorizontalRule(text))
        {
            ApplyLineStyle(line, StyledElements.HorizontalRule, revealed);
            return;
        }

        // Table lines (Requirements.md §4): the delimiter row is pure syntax and is dimmed
        // whole; on header/body rows only the pipes are dimmed, leaving cell text styled
        // normally — no return, so script spans inside cells still apply below. The hrule
        // brushes are reused deliberately: both constructs are structural syntax rather than
        // content, so they share one visual language (the accent-bar/blockquote-text pairing
        // uses the same reasoning). Gated on the cheap first-char check before the block walk,
        // same cost discipline as IsFenceDelimiterLine before TryGetEnclosingFenceBlock.
        if (text[0] == '|' && MarkdownSyntax.TryGetTableBlock(doc, line.LineNumber, out int tableStart, out _))
        {
            if (line.LineNumber == tableStart + 1)
            {
                ApplyLineStyle(line, StyledElements.HorizontalRule, revealed);
                return;
            }
            if (Resolve(StyledElements.HorizontalRule).Foreground is SolidColorBrush brush)
            {
                foreach (int pipe in MarkdownSyntax.GetTablePipeOffsets(text))
                {
                    ChangeLinePart(line.Offset + pipe, line.Offset + pipe + 1,
                        el => el.TextRunProperties.SetForegroundBrush(brush));
                }
            }
        }

        StyleScriptSpans(doc, line);
    }

    // In live preview, revealed markdown renders in the source font — the reveal means
    // "you're editing source here". Two grains, matching the reveal scopes exactly:
    //  - Whole line: the caret's own line when it carries a line-scoped construct (heading,
    //    blockquote, bullet/task/numbered item — task lines are bullet lines by
    //    TryGetBulletListMarker's definition), and every line of a table whose block contains
    //    the caret, since tables reveal whole.
    //  - Per span: whichever inline runs the caret is inside on its own line (emphasis,
    //    links, underline, emoji), using the same inclusive-of-both-edges rule as the marker
    //    generators (see EmphasisMarkerElementGenerator.IsCaretInside) so the mono region and
    //    the revealed markers can never disagree. The span's layout already shifts at that
    //    same instant — its markers appear — so the font swap adds no new movement; that's
    //    what makes per-span acceptable here where an every-caret-line swap would not be
    //    (plain prose lines deliberately stay in the document font, so paragraphs don't
    //    jiggle while arrowing through them).
    // Runs BEFORE the color/weight styling, which rebuilds each run's Typeface from its
    // current family and so preserves the swap. Code fences need nothing here:
    // Markdown.xshd's CodeBlock/InlineCode colors pin the mono family in every mode.
    /// <returns>
    /// True when the <b>whole line</b> was swapped to the source font, so the caller knows not to
    /// re-apply a per-element font family over the top of it. Span-scoped swaps return false: they
    /// only ever happen on lines with no line-scoped construct, where no family override follows.
    /// </returns>
    private bool ApplyRevealedSourceFont(TextDocument doc, DocumentLine line)
    {
        if (!LivePreviewEnabled || SourceFontFamily is null) return false;

        bool wholeLine = line.LineNumber == CaretLine && HasLineScopedConstruct(doc, line);
        if (!wholeLine && doc.GetCharAt(line.Offset) == '|'
            && MarkdownSyntax.TryGetTableBlock(doc, line.LineNumber, out int start, out int end))
        {
            wholeLine = CaretLine >= start && CaretLine <= end;
        }
        if (wholeLine)
        {
            SwapToSourceFont(line.Offset, line.EndOffset);
            return true;
        }

        // Span-scoped reveals are only possible on the caret's own line.
        if (line.LineNumber != CaretLine) return false;

        foreach (var span in MarkdownSyntax.FindEmphasisSpans(doc, line))
            if (CaretOffset >= span.Start && CaretOffset <= span.End) SwapToSourceFont(span.Start, span.End);
        foreach (var span in MarkdownSyntax.FindLinkSpans(doc, line))
            if (CaretOffset >= span.Start && CaretOffset <= span.End) SwapToSourceFont(span.Start, span.End);
        foreach (var span in MarkdownSyntax.FindUnderlineSpans(doc, line))
            if (CaretOffset >= span.Start && CaretOffset <= span.End) SwapToSourceFont(span.Start, span.End);
        foreach (var span in MarkdownSyntax.FindEmojiSpans(doc, line))
            if (CaretOffset >= span.Start && CaretOffset <= span.End) SwapToSourceFont(span.Start, span.End);

        return false;
    }

    private void SwapToSourceFont(int startOffset, int endOffset)
    {
        ChangeLinePart(startOffset, endOffset, el =>
        {
            var old = el.TextRunProperties.Typeface;
            el.TextRunProperties.SetTypeface(new Typeface(SourceFontFamily!, old.Style, old.Weight, old.Stretch));
        });
    }

    private static bool HasLineScopedConstruct(TextDocument doc, DocumentLine line)
        => MarkdownSyntax.TryGetHeadingLevel(doc, line, out _, out _)
        || MarkdownSyntax.TryGetBlockquoteMarkerLength(doc, line, out _, out _)
        || MarkdownSyntax.TryGetBulletListMarker(doc, line, out _)
        || MarkdownSyntax.TryGetNumberedListMarker(doc, line, out _, out _)
        || MarkdownSyntax.IsHorizontalRule(doc.GetText(line));

    // Superscript/subscript are raised or lowered and shrunk. Deliberately NOT gated on
    // LivePreviewEnabled: like bold rendering bold in source mode, the baseline shift *is* the
    // construct, and showing it alongside the visible markers is the same bargain. (Heading
    // scaling is gated because a heading is legible either way — size there is decoration.)
    //
    // Only the content between the markers moves; the '^'/'~' characters stay on the baseline,
    // so in source mode the markers still read as markers. In WYSIWYG they are hidden by
    // EmphasisMarkerElementGenerator and only the shifted content remains.
    //
    // Heading, blockquote, and horizontal-rule lines return before reaching here — those style the
    // whole line as one unit, and a later per-span change would fight the line-wide typeface.
    // Superscript inside a heading or blockquote therefore renders plain; accepted, not overlooked.
    private void StyleScriptSpans(TextDocument doc, DocumentLine line)
    {
        foreach (var span in MarkdownSyntax.FindScriptSpans(doc, line))
        {
            if (span.ContentEnd <= span.ContentStart) continue;

            var alignment = span.IsSuperscript ? BaselineAlignment.Superscript : BaselineAlignment.Subscript;
            ChangeLinePart(span.ContentStart, span.ContentEnd, el =>
            {
                el.TextRunProperties.SetBaselineAlignment(alignment);
                el.TextRunProperties.SetFontRenderingEmSize(el.TextRunProperties.FontRenderingEmSize * ScriptScale);
            });
        }
    }

    // Conventional typographic ratio for scripts; small enough to read as raised/lowered rather
    // than as ordinary text that happens to sit oddly.
    private const double ScriptScale = 0.75;

    // Styles a whole line as one unit from its element's resolved style. Every null member of the
    // resolved style is left untouched rather than overwritten with a default, so "inherit" in
    // Preferences means the run keeps whatever it already had.
    //
    // The old fixed signature (a brush, a weight, an optional italic flag and a size multiplier)
    // encoded each construct's styling at the call site; all of it now comes from EditorPreferences,
    // including the heading weight split at level 3 and the Typora-ish heading scaling that used to
    // live in HeadingScale.
    private void ApplyLineStyle(DocumentLine line, string elementKey, bool revealed)
    {
        var style = Resolve(elementKey);

        ChangeLinePart(line.Offset, line.EndOffset, el =>
        {
            var props = el.TextRunProperties;

            if (style.Foreground is not null) props.SetForegroundBrush(style.Foreground);
            if (style.Background is not null) props.SetBackgroundBrush(style.Background);

            var old = props.Typeface;
            // A revealed line is already in the source font because the caret is on it — the point
            // being that you're editing raw markdown there — so the element's own family must not
            // win it back. Size, weight and style still apply: only the family says "source".
            var family = revealed ? old.FontFamily : style.Family ?? old.FontFamily;
            props.SetTypeface(new Typeface(family, style.Style ?? old.Style, style.Weight ?? old.Weight, old.Stretch));

            if (style.EmSize is double em) props.SetFontRenderingEmSize(em);
            if (style.Decorations is not null) props.SetTextDecorations(style.Decorations);
        });
    }
}
