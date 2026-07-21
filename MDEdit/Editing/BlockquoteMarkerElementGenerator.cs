using System.Windows.Shapes;
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
    // with the blank space reserved here.
    internal const double LeadingGap = 10.0;
    internal const double BarWidth = 3.0;
    internal const double TrailingGap = 4.0;
    internal const double IndentPerLevel = LeadingGap + BarWidth + TrailingGap;

    public bool Enabled { get; set; }
    public int CaretLine { get; set; } = -1;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!Enabled) return -1;

        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(startOffset);
        if (line.LineNumber == CaretLine) return -1;

        // The marker only ever occupies the very start of the line; once startOffset has moved
        // past it there is nothing further on this line for this generator to hide.
        if (startOffset > line.Offset) return -1;

        return MarkdownSyntax.TryGetBlockquoteMarkerLength(doc, line, out _, out _) ? line.Offset : -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(offset);
        MarkdownSyntax.TryGetBlockquoteMarkerLength(doc, line, out int markerLength, out int depth);

        // Height 0: this element only reserves horizontal space (Width), same zero-visual-height
        // technique the other generators use for a plain hide — the visible bar comes from
        // BlockquoteAccentBarRenderer instead, so nothing needs to be drawn here.
        return new InlineObjectElement(markerLength, new Rectangle { Width = depth * IndentPerLevel, Height = 0 });
    }
}
