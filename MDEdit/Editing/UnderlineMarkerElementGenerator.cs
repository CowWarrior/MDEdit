using System.Windows.Shapes;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit.Editing;

/// <summary>
/// Live-preview element generator: hides the "&lt;u&gt;" prefix and "&lt;/u&gt;" suffix of an
/// underline run, leaving only the underlined text, except for whichever run the caret is currently
/// inside — revealing the raw tags so they can be edited. Same per-span reveal convention as
/// <see cref="EmphasisMarkerElementGenerator"/> (inclusive of both edges, so landing exactly on a
/// boundary doesn't flicker), and the same two-independently-sized-regions approach as
/// <see cref="LinkMarkerElementGenerator"/>, since an underline's opening and closing tags are
/// different lengths. The underline itself comes from Markdown.xshd's Underline color, not from
/// here — this class only hides the tags; the hidden characters keep their document offsets.
/// </summary>
internal sealed class UnderlineMarkerElementGenerator : VisualLineElementGenerator
{
    public bool Enabled { get; set; }
    public int CaretOffset { get; set; } = -1;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!Enabled) return -1;

        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(startOffset);

        foreach (var span in MarkdownSyntax.FindUnderlineSpans(doc, line))
        {
            if (IsCaretInside(span)) continue;

            if (span.Start >= startOffset) return span.Start;
            if (span.ContentEnd >= startOffset) return span.ContentEnd;
        }

        return -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(offset);

        foreach (var span in MarkdownSyntax.FindUnderlineSpans(doc, line))
        {
            if (IsCaretInside(span)) continue;

            if (offset == span.Start)
                return new InlineObjectElement(span.ContentStart - span.Start, new Rectangle { Width = 0, Height = 0 });
            if (offset == span.ContentEnd)
                return new InlineObjectElement(span.End - span.ContentEnd, new Rectangle { Width = 0, Height = 0 });
        }

        // GetFirstInterestedOffset only ever returns offsets this method recognizes, so this
        // is unreachable in practice — return a harmless zero-length element rather than throw.
        return new InlineObjectElement(0, new Rectangle { Width = 0, Height = 0 });
    }

    private bool IsCaretInside(UnderlineSpan span) => CaretOffset >= span.Start && CaretOffset <= span.End;
}
