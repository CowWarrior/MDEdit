using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit.Editing;

/// <summary>
/// Live-preview element generator: renders a bullet list item's '-'/'*'/'+' marker character as
/// an indented "•" glyph on any line other than the one the caret is currently on — the same
/// per-line reveal convention as <see cref="HeadingMarkerElementGenerator"/>, but the replacement
/// is a visible glyph rather than a zero-size hide: unlike a heading's "# ", the bullet marker
/// *is* the construct's visual, so collapsing it to nothing would leave list items
/// indistinguishable from plain text. The glyph carries a left margin of
/// <see cref="BlockquoteMarkerElementGenerator.IndentPerLevel"/> — read from that class's
/// constants, not duplicated — so list items sit at the same pixel depth as blockquote content
/// and the two can never drift apart. Only the single marker character is replaced — the
/// following space and any leading nesting indent stay as real rendered text, so spacing and
/// nested-item indentation come from the document itself (nested items get the source
/// whitespace's natural width on top of the fixed indent, not a quantized per-level depth).
/// The marker character still occupies its document offset (selection/undo/the saved file are
/// unaffected) — only the visual rendering swaps it for the glyph, via
/// <see cref="InlineObjectElement"/> hosting a TextBlock styled from the editor's own global
/// text run properties (typeface/size/foreground), so it follows the current theme and font
/// with no per-theme wiring in MainWindow. <see cref="NumberedListMarkerElementGenerator"/> is
/// this class's ordered-list counterpart (indent only, no glyph substitution).
/// </summary>
internal sealed class BulletListMarkerElementGenerator : VisualLineElementGenerator
{
    public bool Enabled { get; set; }
    public int CaretLine { get; set; } = -1;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!Enabled) return -1;

        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(startOffset);
        if (line.LineNumber == CaretLine) return -1;

        // Unlike heading/blockquote markers, the bullet char can sit after leading indent, so
        // the interesting offset is markerOffset rather than line.Offset — but it's still the
        // only thing on the line for this generator, so anything past it returns -1.
        if (!MarkdownSyntax.TryGetBulletListMarker(doc, line, out int markerOffset)) return -1;
        // A task item is a bullet item too, but TaskListMarkerElementGenerator replaces the bullet
        // and the box together with one checkbox glyph — drawing a "•" here as well would render
        // "• ☐ todo", two markers for one construct.
        if (MarkdownSyntax.TryGetTaskListMarker(doc, line, out _, out _, out _)) return -1;
        return startOffset <= markerOffset ? markerOffset : -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var glyph = ListMarkerStyling.CreateMarkerBlock("•", CurrentContext, MarkerStyle);
        return new InlineObjectElement(1, glyph);
    }

    /// <summary>
    /// The <c>listMarker</c> element's resolved style (Requirements.md §6), pushed by
    /// <c>MainWindow.ApplyActiveModeStyles</c>. Default (all-null) inherits the editor's own text
    /// properties, which is exactly how this rendered before the setting existed.
    /// </summary>
    /// <remarks>
    /// Needed because the marker is <i>replaced</i> here rather than coloured in place: the XSHD
    /// <c>ListMarker</c> colour styles the raw "-" in source mode, but never reaches this drawn "•".
    /// Without this the element's settings would appear to do nothing in WYSIWYG.
    /// </remarks>
    public ResolvedStyle MarkerStyle { get; set; }
}
