using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit.Editing;

/// <summary>
/// Live-preview element generator: <see cref="BulletListMarkerElementGenerator"/>'s ordered-list
/// counterpart, same per-line reveal convention — but where bullets swap their marker character
/// for a "•" glyph, a "1. " marker's rendered form *is* its source form, so this replaces the
/// digits-plus-'.' range with a TextBlock containing that exact same text and the only visual
/// change is the left margin of <see cref="BlockquoteMarkerElementGenerator.IndentPerLevel"/>
/// (read from that class's constants, same as the bullet generator, so all three constructs
/// share one pixel depth). The following space and any leading nesting indent stay as real
/// rendered text, and the marker characters still occupy their document offsets — only the
/// rendering changes. Styled from the editor's global text run properties like the bullet glyph,
/// which does mean the number renders in the default text color off the caret's line rather
/// than Markdown.xshd's ListMarker accent — the rendered-output look, consistent with how the
/// bullet glyph renders.
/// </summary>
internal sealed class NumberedListMarkerElementGenerator : VisualLineElementGenerator
{
    public bool Enabled { get; set; }

    /// <summary>Editor zoom (Requirements.md §6) — scales the marker's indent, nothing else.</summary>
    public double Zoom { get; set; } = 1.0;
    public int CaretLine { get; set; } = -1;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!Enabled) return -1;

        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(startOffset);
        if (line.LineNumber == CaretLine) return -1;

        if (!MarkdownSyntax.TryGetNumberedListMarker(doc, line, out int markerOffset, out _)) return -1;
        return startOffset <= markerOffset ? markerOffset : -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var doc = CurrentContext.Document;
        var line = doc.GetLineByOffset(offset);
        MarkdownSyntax.TryGetNumberedListMarker(doc, line, out int markerOffset, out int markerLength);

        var marker = ListMarkerStyling.CreateMarkerBlock(
            doc.GetText(markerOffset, markerLength), CurrentContext, MarkerStyle, Zoom);
        return new InlineObjectElement(markerLength, marker);
    }

    /// <summary>
    /// The <c>listMarker</c> element's resolved style (Requirements.md §6), pushed by
    /// <c>MainWindow.ApplyActiveModeStyles</c> — shared with
    /// <see cref="BulletListMarkerElementGenerator"/>, since bullets and numbers are one element.
    /// </summary>
    public ResolvedStyle MarkerStyle { get; set; }
}
