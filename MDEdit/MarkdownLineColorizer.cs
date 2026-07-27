using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using MDEdit.Editing;

namespace MDEdit;

internal sealed class MarkdownLineColorizer : DocumentColorizingTransformer
{
    private static readonly SolidColorBrush LightHeadingBrush    = Freeze(Color.FromRgb(0x00, 0x57, 0xAE));
    private static readonly SolidColorBrush LightBlockquoteBrush = Freeze(Color.FromRgb(0x6A, 0x73, 0x7D));
    private static readonly SolidColorBrush LightHRuleBrush      = Freeze(Color.FromRgb(0xBB, 0xBB, 0xBB));

    private static readonly SolidColorBrush DarkHeadingBrush    = Freeze(Color.FromRgb(0x58, 0xA6, 0xFF));
    private static readonly SolidColorBrush DarkBlockquoteBrush = Freeze(Color.FromRgb(0x8B, 0x94, 0x9E));
    private static readonly SolidColorBrush DarkHRuleBrush      = Freeze(Color.FromRgb(0x48, 0x4F, 0x58));

    // Set by MainWindow.ApplyTheme; a TextView.Redraw() afterwards re-runs ColorizeLine.
    public bool IsDark { get; set; }

    // Set by MainWindow's live-preview toggle. Only affects heading font size (Typora-style
    // scaling) and the revealed-source font swap below — colors/weight apply regardless,
    // matching the pre-live-preview behavior.
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

        ApplyRevealedSourceFont(doc, line);

        if (MarkdownSyntax.TryGetHeadingLevel(doc, line, out int level, out _))
        {
            var scale = LivePreviewEnabled ? HeadingScale(level) : 1.0;
            ColorLine(line, IsDark ? DarkHeadingBrush : LightHeadingBrush,
                level <= 3 ? FontWeights.Bold : FontWeights.SemiBold, emSizeScale: scale);
            return;
        }

        if (MarkdownSyntax.TryGetBlockquoteMarkerLength(doc, line, out _, out _))
        {
            ColorLine(line, IsDark ? DarkBlockquoteBrush : LightBlockquoteBrush, FontWeights.Normal, italic: true);
            return;
        }

        var text = doc.GetText(line);
        if (MarkdownSyntax.IsHorizontalRule(text))
        {
            ColorLine(line, IsDark ? DarkHRuleBrush : LightHRuleBrush, FontWeights.Normal);
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
            var brush = IsDark ? DarkHRuleBrush : LightHRuleBrush;
            if (line.LineNumber == tableStart + 1)
            {
                ColorLine(line, brush, FontWeights.Normal);
                return;
            }
            foreach (int pipe in MarkdownSyntax.GetTablePipeOffsets(text))
            {
                ChangeLinePart(line.Offset + pipe, line.Offset + pipe + 1,
                    el => el.TextRunProperties.SetForegroundBrush(brush));
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
    private void ApplyRevealedSourceFont(TextDocument doc, DocumentLine line)
    {
        if (!LivePreviewEnabled || SourceFontFamily is null) return;

        bool wholeLine = line.LineNumber == CaretLine && HasLineScopedConstruct(doc, line);
        if (!wholeLine && doc.GetCharAt(line.Offset) == '|'
            && MarkdownSyntax.TryGetTableBlock(doc, line.LineNumber, out int start, out int end))
        {
            wholeLine = CaretLine >= start && CaretLine <= end;
        }
        if (wholeLine)
        {
            SwapToSourceFont(line.Offset, line.EndOffset);
            return;
        }

        // Span-scoped reveals are only possible on the caret's own line.
        if (line.LineNumber != CaretLine) return;

        foreach (var span in MarkdownSyntax.FindEmphasisSpans(doc, line))
            if (CaretOffset >= span.Start && CaretOffset <= span.End) SwapToSourceFont(span.Start, span.End);
        foreach (var span in MarkdownSyntax.FindLinkSpans(doc, line))
            if (CaretOffset >= span.Start && CaretOffset <= span.End) SwapToSourceFont(span.Start, span.End);
        foreach (var span in MarkdownSyntax.FindUnderlineSpans(doc, line))
            if (CaretOffset >= span.Start && CaretOffset <= span.End) SwapToSourceFont(span.Start, span.End);
        foreach (var span in MarkdownSyntax.FindEmojiSpans(doc, line))
            if (CaretOffset >= span.Start && CaretOffset <= span.End) SwapToSourceFont(span.Start, span.End);
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
        || MarkdownSyntax.TryGetNumberedListMarker(doc, line, out _, out _);

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

    // Typora-ish size ratios relative to the editor's base font size; only applied in live preview.
    private static double HeadingScale(int level) => level switch
    {
        1 => 1.6,
        2 => 1.4,
        3 => 1.25,
        4 => 1.15,
        5 => 1.05,
        _ => 1.0,
    };

    private void ColorLine(DocumentLine line, SolidColorBrush brush,
        FontWeight weight, bool italic = false, double emSizeScale = 1.0)
    {
        ChangeLinePart(line.Offset, line.EndOffset, el =>
        {
            el.TextRunProperties.SetForegroundBrush(brush);
            var old = el.TextRunProperties.Typeface;
            el.TextRunProperties.SetTypeface(new Typeface(
                old.FontFamily,
                italic ? FontStyles.Italic : old.Style,
                weight,
                old.Stretch));
            if (emSizeScale != 1.0)
                el.TextRunProperties.SetFontRenderingEmSize(el.TextRunProperties.FontRenderingEmSize * emSizeScale);
        });
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }
}
