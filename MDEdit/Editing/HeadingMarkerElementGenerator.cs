using System.Windows.Shapes;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit.Editing;

/// <summary>
/// Live-preview element generator: hides the "# " ATX heading marker on any line other than the
/// one the caret is currently on (Typora/Obsidian-style). The marker characters still occupy
/// their document offsets — selection, undo, and the underlying Markdown text are untouched —
/// they're just rendered as a zero-size element instead of text, via <see cref="InlineObjectElement"/>.
/// MainWindow keeps <see cref="CaretLine"/> in sync with the caret and calls TextView.Redraw()
/// when it changes, so the marker reappears the moment the caret enters that line.
/// </summary>
internal sealed class HeadingMarkerElementGenerator : VisualLineElementGenerator
{
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

        return MarkdownSyntax.TryGetHeadingLevel(doc, line, out _, out _) ? line.Offset : -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(offset);
        MarkdownSyntax.TryGetHeadingLevel(doc, line, out _, out int markerLength);
        return new InlineObjectElement(markerLength, new Rectangle { Width = 0, Height = 0 });
    }
}
