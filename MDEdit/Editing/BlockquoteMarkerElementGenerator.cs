using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
/// rendering changes: instead of collapsing to nothing (like the other marker generators), the
/// marker is replaced by an indent with a left accent bar per nesting level — an
/// <see cref="InlineObjectElement"/>'s replacement content isn't required to be zero-size, and a
/// blockquote reads as indented, bordered prose rather than merely "text that lost its '&gt;'".
/// <see cref="MarkdownLineColorizer"/>'s italic styling for the line is not live-preview-gated
/// and is unaffected either way.
/// </summary>
internal sealed class BlockquoteMarkerElementGenerator : VisualLineElementGenerator
{
    // Matches MarkdownLineColorizer's blockquote brush colors, so the indent bar and the
    // quoted text read as the same visual language.
    private static readonly SolidColorBrush LightBarBrush = Freeze(Color.FromRgb(0x6A, 0x73, 0x7D));
    private static readonly SolidColorBrush DarkBarBrush  = Freeze(Color.FromRgb(0x8B, 0x94, 0x9E));

    private const double BarWidth = 3.0;
    private const double GapAfterBar = 8.0;

    public bool Enabled { get; set; }
    public bool IsDark { get; set; }
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
        return new InlineObjectElement(markerLength, BuildIndent(depth));
    }

    // InlineObjectRun measures its element with an infinite constraint (it can't stretch to
    // "the current line's height" the way in-flow content does), so the bar's height has to be
    // set explicitly rather than left to auto-size — approximated from the current font size
    // rather than a hardcoded pixel value, so it scales with editor font size and (loosely) with
    // heading-scaled lines.
    private UIElement BuildIndent(int depth)
    {
        double barHeight = CurrentContext.GlobalTextRunProperties.FontRenderingEmSize * 1.4;
        var brush = IsDark ? DarkBarBrush : LightBarBrush;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < depth; i++)
        {
            panel.Children.Add(new Rectangle
            {
                Width = BarWidth,
                Height = barHeight,
                Fill = brush,
                Margin = new Thickness(0, 0, GapAfterBar, 0),
            });
        }
        return panel;
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }
}
