using System;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit.Editing;

/// <summary>
/// Draws the blockquote accent bar(s) as one continuous vertical line per nesting level spanning
/// a run of consecutive blockquote lines, rather than one short bar per document line. A per-line
/// InlineObjectElement (the original approach) can't produce a gapless bar: the vertical space
/// between visual lines (line spacing/leading) is owned by TextView's own layout, not by any
/// single line's content, so no matter how tall a per-line element is made, adjacent bars can
/// never quite touch. An <see cref="IBackgroundRenderer"/> draws directly in the TextView's
/// shared pixel space instead of being confined to one line's content flow, so it can draw a
/// single rectangle spanning from the top of the first line in a run to the bottom of the last.
/// Registered on <c>TextView.BackgroundRenderers</c> (not <c>ElementGenerators</c>) for that
/// reason. <see cref="BlockquoteMarkerElementGenerator"/> still reserves the horizontal indent
/// (as a blank spacer); this renderer only supplies the visible bar within that reserved space,
/// using the same layout constants so the two always agree on where the bar belongs.
/// </summary>
internal sealed class BlockquoteAccentBarRenderer : IBackgroundRenderer
{
    // The bar is the blockquote's colour, so it is read from the colorizer rather than kept here and
    // pushed in — the same reference-to-the-colorizer arrangement HorizontalRuleRenderer uses, for
    // the same reason. Two copies of a colour that must agree is one copy too many, and per-element
    // styling (Requirements.md §6) made them easier to desynchronise: the colour now varies by
    // editor mode as well as by theme, and only the colorizer knows which mode is live.
    private readonly MarkdownLineColorizer _colorizer;

    // Held for the same reason TableGridRenderer holds its generator: the reveal decision and the
    // geometry must come from one place. Reading them off the generator is what stops the drawn bar
    // disagreeing with the space reserved for it — see TryGetRenderedDepth for the bug that caused.
    private readonly BlockquoteMarkerElementGenerator _generator;

    public BlockquoteAccentBarRenderer(MarkdownLineColorizer colorizer, BlockquoteMarkerElementGenerator generator)
    {
        _colorizer = colorizer;
        _generator = generator;
    }

    /// <summary>
    /// A cheap early-out only. Per-line rendering decisions belong to
    /// <see cref="BlockquoteMarkerElementGenerator.TryGetRenderedDepth"/>, which checks the
    /// generator's own <c>Enabled</c> anyway.
    /// </summary>
    public bool Enabled { get; set; }

    // Zoom is read off the generator rather than kept here: it scales the same constants, so a
    // second copy is a second chance for the bar and the reserved space to disagree.
    private double Zoom => _generator.Zoom;

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!Enabled) return;

        var doc = textView.Document;
        if (doc == null) return;

        var visualLines = textView.VisualLines;
        if (visualLines.Count == 0) return;

        int maxDepth = 0;
        foreach (var vl in visualLines)
            maxDepth = Math.Max(maxDepth, GetDepth(doc, vl.FirstDocumentLine.LineNumber));
        if (maxDepth == 0) return;

        if (_colorizer.Resolve(StyledElements.Blockquote).Foreground is not SolidColorBrush brush) return;

        // Each nesting level is its own pass so bars merge/split correctly across lines of
        // differing depth: level 1 stays continuous across a "> a" / ">> b" transition (both
        // lines are still at least depth 1), while level 2 only spans the deeper sub-run.
        for (int level = 1; level <= maxDepth; level++)
        {
            double x = (BlockquoteMarkerElementGenerator.LeadingIndent
                      + BlockquoteMarkerElementGenerator.LeadingGap
                      + (level - 1) * BlockquoteMarkerElementGenerator.IndentPerLevel) * Zoom
                     - textView.HorizontalOffset;

            int i = 0;
            while (i < visualLines.Count)
            {
                if (GetDepth(doc, visualLines[i].FirstDocumentLine.LineNumber) < level)
                {
                    i++;
                    continue;
                }

                int runStart = i;
                while (i < visualLines.Count && GetDepth(doc, visualLines[i].FirstDocumentLine.LineNumber) >= level)
                    i++;
                int runEnd = i - 1;

                double top    = visualLines[runStart].VisualTop - textView.VerticalOffset;
                double bottom = visualLines[runEnd].VisualTop + visualLines[runEnd].Height - textView.VerticalOffset;

                drawingContext.DrawRectangle(brush, null,
                    new Rect(x, top, BlockquoteMarkerElementGenerator.BarWidth * Zoom, bottom - top));
            }
        }
    }

    // Zero for a line that isn't a blockquote AND for the caret's line, whose raw source is
    // revealed — so that line both draws no bar and breaks any run passing through it.
    private int GetDepth(TextDocument doc, int lineNumber)
        => _generator.TryGetRenderedDepth(doc, lineNumber, out int depth) ? depth : 0;
}
