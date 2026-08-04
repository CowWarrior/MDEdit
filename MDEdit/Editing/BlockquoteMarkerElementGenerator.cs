using System.Windows.Shapes;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit.Editing;

/// <summary>
/// Live-preview element generator: hides a blockquote's leading "&gt; " marker (including nested
/// "&gt; &gt; " runs) on any line other than the one the caret is currently on — the same
/// per-line reveal convention as <see cref="HeadingMarkerElementGenerator"/>, since a blockquote
/// marker only ever occupies the very start of a line. Unlike code-block fences, a multi-line
/// blockquote does not reveal as a whole block when the caret enters it: only the caret's own
/// line shows its "&gt;", matching CLAUDE.md's guidance that this construct follows the simpler
/// per-line pattern, not the fence pair's per-block one. The marker characters still occupy
/// their document offsets — selection/undo/the saved file are unaffected — only the visual
/// rendering changes: instead of a zero-size hide, the marker is replaced by a blank horizontal
/// spacer wide enough for the accent bar(s) that <see cref="BlockquoteAccentBarRenderer"/> draws
/// separately (this class only reserves the space; it doesn't paint anything itself). Splitting
/// it this way — rather than drawing the bar directly in this generator's replacement element,
/// as an earlier version did — is what lets the bar span multiple lines with no gap: an
/// InlineObjectElement is confined to its own line's content flow, but a background renderer
/// draws in the TextView's shared pixel space and isn't limited that way. See
/// <see cref="BlockquoteAccentBarRenderer"/> for the actual drawing and the reasoning behind it.
/// <see cref="MarkdownLineColorizer"/>'s italic styling for the line is not live-preview-gated
/// and is unaffected either way.
/// </summary>
internal sealed class BlockquoteMarkerElementGenerator : VisualLineElementGenerator
{
    // The layout of the reserved indent, per nesting level — tweak these to adjust the look.
    // BlockquoteAccentBarRenderer reads the same constants so the bar it draws always lines up
    // with the blank space reserved here. LeadingIndent is a one-time (not per-level) shift of
    // the whole construct, added so the level-1 bar lines up with the "•" glyphs the list
    // generators draw.
    internal const double LeadingIndent = 11.0;
    internal const double LeadingGap = 10.0;
    internal const double BarWidth = 3.0;
    internal const double TrailingGap = 4.0;
    internal const double IndentPerLevel = LeadingGap + BarWidth + TrailingGap;

    public bool Enabled { get; set; }
    public int CaretLine { get; set; } = -1;

    /// <summary>
    /// Editor zoom (Requirements.md §6), multiplying the constants above. 1.0 is unzoomed.
    /// </summary>
    /// <remarks>
    /// Text sizes follow zoom on their own — everything resolves from <c>ModeStyles.BaseFontSize</c>,
    /// which zoom scales. These indents are fixed pixel constants, so they are the one part of the
    /// layout that has to be told. Every class that reads these constants carries the same property,
    /// all pushed from <c>MainWindow.UpdateLivePreviewState</c>; without it a 300% document would
    /// draw large text against an unchanged 17px indent.
    /// </remarks>
    public double Zoom { get; set; } = 1.0;

    /// <summary>
    /// Whether <paramref name="lineNumber"/> is currently being <i>rendered</i> as a blockquote, and
    /// at what nesting depth — the single gate this generator and
    /// <see cref="BlockquoteAccentBarRenderer"/> both decide from.
    /// </summary>
    /// <remarks>
    /// <b>Shared rather than duplicated, because the two halves have already drifted apart once.</b>
    /// This generator reveals the caret's line (returning −1 for it, so the raw <c>&gt;</c> shows and
    /// no indent is reserved), but the renderer used to decide purely from
    /// <c>MarkdownSyntax.TryGetBlockquoteMarkerLength</c> with no caret state at all — so it kept
    /// painting a bar for that line, at the x the now-absent spacer would have created, leaving the
    /// bar sitting over the revealed marker. Routing both through one method is what the other two
    /// generator/renderer pairs already do (<c>HorizontalRuleElementGenerator.IsRendered</c>,
    /// <c>TableRowElementGenerator.TryGetRenderedTable</c>); this pair shared only layout constants,
    /// which is exactly how it got out of step.
    /// <para>
    /// Returning false for the caret line also splits the renderer's contiguous-run scan around it,
    /// which a check confined to the depth lookup would not have done — a bar would still have been
    /// drawn straight across the revealed line as part of a longer run.
    /// </para>
    /// </remarks>
    public bool TryGetRenderedDepth(TextDocument doc, int lineNumber, out int depth)
    {
        depth = 0;
        if (!Enabled || lineNumber == CaretLine) return false;
        if (lineNumber < 1 || lineNumber > doc.LineCount) return false;

        var line = doc.GetLineByNumber(lineNumber);
        return MarkdownSyntax.TryGetBlockquoteMarkerLength(doc, line, out _, out depth);
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(startOffset);

        // The marker only ever occupies the very start of the line; once startOffset has moved
        // past it there is nothing further on this line for this generator to hide.
        if (startOffset > line.Offset) return -1;

        // Enabled and the caret-line reveal are both folded into the gate, so there is one place
        // that decides and the renderer cannot disagree with it.
        return TryGetRenderedDepth(doc, line.LineNumber, out _) ? line.Offset : -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(offset);
        MarkdownSyntax.TryGetBlockquoteMarkerLength(doc, line, out int markerLength, out int depth);

        // Height 0: this element only reserves horizontal space (Width), same zero-visual-height
        // technique the other generators use for a plain hide — the visible bar comes from
        // BlockquoteAccentBarRenderer instead, so nothing needs to be drawn here.
        return new InlineObjectElement(markerLength,
            new Rectangle { Width = (LeadingIndent + depth * IndentPerLevel) * Zoom, Height = 0 });
    }
}
