using System.Windows.Shapes;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit.Editing;

/// <summary>
/// Live-preview element generator: hides an entire horizontal-rule line ("---", "* * *", etc.)
/// when the caret is elsewhere, revealing the raw syntax on the caret's own line so it can be
/// edited — the same **per-line** reveal convention as <see cref="HeadingMarkerElementGenerator"/>
/// and <see cref="BlockquoteMarkerElementGenerator"/>. Unlike those two, a horizontal rule has no
/// separate marker-prefix-vs-content split — the entire line *is* the marker — so the hidden
/// range is the whole line, the same whole-line idiom <see cref="CodeBlockFenceElementGenerator"/>
/// uses for fence delimiter lines. The actual rendered line is drawn separately by
/// <see cref="HorizontalRuleRenderer"/>: a horizontal rule has no natural width of its own (unlike
/// a bullet's "•" or a table's measured columns, there's no content to size it from) — it has to
/// span the visible width of the editor, which only a background renderer drawing in the
/// <c>TextView</c>'s own pixel space can do; no single inline element can. This is the
/// reserve/hide-here-draw-there split blockquotes and tables also use, but for a different
/// reason: there this class reserves nothing, it hides the whole line outright, matching the
/// fence idiom exactly. As with every other generator, the line's characters keep their document
/// offsets — selection, undo, and the saved file are unaffected; only the rendering changes.
/// </summary>
internal sealed class HorizontalRuleElementGenerator : VisualLineElementGenerator
{
    public bool Enabled { get; set; }
    public int CaretLine { get; set; } = -1;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!Enabled) return -1;

        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(startOffset);
        if (line.LineNumber == CaretLine) return -1;

        // The "marker" is the entire line, which only ever starts at the line's own start.
        if (startOffset > line.Offset) return -1;
        return IsRendered(doc, line) ? line.Offset : -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(offset);
        return new InlineObjectElement(line.Length, new Rectangle { Width = 0, Height = 0 });
    }

    /// <summary>
    /// Whether <paramref name="line"/> is currently rendered as a drawn rule (live preview on,
    /// caret elsewhere) rather than shown as raw source. Shared with
    /// <see cref="HorizontalRuleRenderer"/> so the drawn line and the hidden text can never
    /// disagree about which lines are rendered.
    /// </summary>
    public bool IsRendered(TextDocument doc, DocumentLine line)
        => Enabled && line.LineNumber != CaretLine && MarkdownSyntax.IsHorizontalRule(doc.GetText(line));
}
