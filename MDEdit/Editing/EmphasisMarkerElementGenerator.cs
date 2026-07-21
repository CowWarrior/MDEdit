using System.Windows.Shapes;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit.Editing;

/// <summary>
/// Live-preview element generator: hides the opening/closing markers of bold/italic/bold+italic,
/// strikethrough, and inline-code runs (**, __, *, _, their 3-char combinations, ~~, and `)
/// except for whichever run the caret is currently inside. Unlike <see cref="HeadingMarkerElementGenerator"/> (which reveals per *line*,
/// since a heading marker only ever exists at the very start of one), emphasis reveal is per
/// *span* — several bold/italic runs can share a line, and hiding all of them just because the
/// caret is somewhere else on that same line would be distracting while editing prose.
/// As with heading markers, the marker characters keep their document offsets; only the
/// rendering collapses them via a zero-size <see cref="InlineObjectElement"/>.
/// </summary>
internal sealed class EmphasisMarkerElementGenerator : VisualLineElementGenerator
{
    public bool Enabled { get; set; }
    public int CaretOffset { get; set; } = -1;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!Enabled) return -1;

        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(startOffset);

        foreach (var span in MarkdownSyntax.FindEmphasisSpans(doc, line))
        {
            if (IsCaretInside(span)) continue;

            if (span.Start >= startOffset) return span.Start;

            var closeStart = span.End - span.MarkerLength;
            if (closeStart >= startOffset) return closeStart;
        }

        return -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(offset);

        foreach (var span in MarkdownSyntax.FindEmphasisSpans(doc, line))
        {
            if (IsCaretInside(span)) continue;

            if (offset == span.Start || offset == span.End - span.MarkerLength)
                return new InlineObjectElement(span.MarkerLength, new Rectangle { Width = 0, Height = 0 });
        }

        // GetFirstInterestedOffset only ever returns offsets this method recognizes, so this
        // is unreachable in practice — return a harmless zero-length element rather than throw.
        return new InlineObjectElement(0, new Rectangle { Width = 0, Height = 0 });
    }

    // Inclusive of both edges: a caret sitting immediately before the opening marker or right
    // after the closing marker still counts as "editing this span", so it doesn't flicker hidden
    // the instant the caret lands on either boundary.
    private bool IsCaretInside(EmphasisSpan span) => CaretOffset >= span.Start && CaretOffset <= span.End;
}
