using System.Windows.Shapes;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit.Editing;

/// <summary>
/// Live-preview element generator: hides an entire fenced-code-block delimiter line ("```" /
/// "~~~", with or without a language tag) when the caret is outside that block, revealing both
/// its opening and closing fence lines whenever the caret is anywhere inside the block —
/// including on a content line between them, not just on a fence line itself, so the fences
/// don't flicker away while the caret is simply moving through the code in the middle of it.
/// Unlike <see cref="HeadingMarkerElementGenerator"/> (marker confined to the caret's own line)
/// and <see cref="EmphasisMarkerElementGenerator"/> (span confined to one line), a fence pair can
/// sit many lines away from the caret's line, so MainWindow additionally redraws a block's fence
/// lines whenever the caret moves into or out of that block, not just the caret's old/new line.
/// As with the other generators, the fence line's characters keep their document offsets — only
/// the rendering collapses the whole line via a zero-size <see cref="InlineObjectElement"/>.
/// </summary>
internal sealed class CodeBlockFenceElementGenerator : VisualLineElementGenerator
{
    public bool Enabled { get; set; }
    public int CaretLine { get; set; } = -1;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!Enabled) return -1;

        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(startOffset);

        // The "marker" is the entire line, which only ever starts at the line's own start.
        if (startOffset > line.Offset) return -1;
        if (!MarkdownSyntax.IsFenceDelimiterLine(doc, line)) return -1;
        if (!MarkdownSyntax.TryGetEnclosingFenceBlock(doc, line.LineNumber, out int start, out int end)) return -1;
        if (CaretLine >= start && CaretLine <= end) return -1; // caret is inside this block: reveal

        return line.Offset;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(offset);
        return new InlineObjectElement(line.Length, new Rectangle { Width = 0, Height = 0 });
    }
}
