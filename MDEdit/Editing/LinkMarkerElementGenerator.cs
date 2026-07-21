using System.Windows.Shapes;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit.Editing;

/// <summary>
/// Live-preview element generator: hides the "[" (or "![") prefix and "](url)"/"][ref]" suffix
/// of a link or image, leaving only its visible text/alt label, except for whichever link the
/// caret is currently inside — revealing the full raw syntax so it can be edited, the same
/// per-span reveal convention as <see cref="EmphasisMarkerElementGenerator"/> (inclusive of both
/// edges, so landing exactly on a boundary doesn't flicker). Unlike emphasis markers, a link's
/// prefix and suffix are different lengths — the URL/reference portion has no fixed width — so
/// this hides two independently-sized regions per link rather than reusing one marker length at
/// both ends. As with the other generators, the hidden characters keep their document offsets;
/// only the rendering collapses them via a zero-size <see cref="InlineObjectElement"/>.
/// </summary>
internal sealed class LinkMarkerElementGenerator : VisualLineElementGenerator
{
    public bool Enabled { get; set; }
    public int CaretOffset { get; set; } = -1;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!Enabled) return -1;

        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(startOffset);

        foreach (var span in MarkdownSyntax.FindLinkSpans(doc, line))
        {
            if (IsCaretInside(span)) continue;

            if (span.Start >= startOffset) return span.Start;
            if (span.TextEnd >= startOffset) return span.TextEnd;
        }

        return -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(offset);

        foreach (var span in MarkdownSyntax.FindLinkSpans(doc, line))
        {
            if (IsCaretInside(span)) continue;

            if (offset == span.Start)
                return new InlineObjectElement(span.TextStart - span.Start, new Rectangle { Width = 0, Height = 0 });
            if (offset == span.TextEnd)
                return new InlineObjectElement(span.End - span.TextEnd, new Rectangle { Width = 0, Height = 0 });
        }

        // GetFirstInterestedOffset only ever returns offsets this method recognizes, so this
        // is unreachable in practice — return a harmless zero-length element rather than throw.
        return new InlineObjectElement(0, new Rectangle { Width = 0, Height = 0 });
    }

    private bool IsCaretInside(LinkSpan span) => CaretOffset >= span.Start && CaretOffset <= span.End;
}
