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
    // scaling) — colors/weight apply regardless, matching the pre-live-preview behavior.
    public bool LivePreviewEnabled { get; set; }

    protected override void ColorizeLine(DocumentLine line)
    {
        var doc = CurrentContext.Document;
        if (line.Length == 0) return;

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

        StyleScriptSpans(doc, line);
    }

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
