using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit.Editing;

/// <summary>
/// Live-preview element generator: renders a task list item's "- [ ]" / "- [x]" marker as a single
/// checkbox glyph on any line other than the one the caret is currently on — the same per-line
/// reveal convention and visible-replacement approach as
/// <see cref="BulletListMarkerElementGenerator"/>, which this is a variant of.
/// </summary>
/// <remarks>
/// The bullet character and the box are replaced together as one element, rather than letting the
/// bullet generator draw a "•" and this one draw a box beside it — "• ☐ todo" reads as two markers
/// for one construct. <see cref="BulletListMarkerElementGenerator"/> stands aside on task lines for
/// exactly this reason.
///
/// The marker characters keep their document offsets, so selection, undo, and the saved file are
/// unaffected — only the rendering changes. Toggling a box is a document edit and belongs to
/// <see cref="MarkdownFormatter.TaskListItem"/>, not here; this class never mutates anything.
/// </remarks>
internal sealed class TaskListMarkerElementGenerator : VisualLineElementGenerator
{
    public bool Enabled { get; set; }

    /// <summary>Editor zoom (Requirements.md §6) — scales the checkbox's indent; the box itself
    /// already follows the editor's font size.</summary>
    public double Zoom { get; set; } = 1.0;
    public int CaretLine { get; set; } = -1;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!Enabled) return -1;

        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(startOffset);
        if (line.LineNumber == CaretLine) return -1;

        if (!MarkdownSyntax.TryGetTaskListMarker(doc, line, out int markerOffset, out _, out _)) return -1;
        return startOffset <= markerOffset ? markerOffset : -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(offset);

        if (!MarkdownSyntax.TryGetTaskListMarker(doc, line, out int markerOffset, out int markerLength, out bool isChecked)
            || offset != markerOffset)
        {
            // GetFirstInterestedOffset only ever returns the marker offset, so this is unreachable
            // in practice — return a harmless zero-length element rather than throw.
            return new InlineObjectElement(0, new TextBlock());
        }

        var props = CurrentContext.GlobalTextRunProperties;
        return new InlineObjectElement(markerLength,
            BuildCheckbox(props.FontRenderingEmSize, props.ForegroundBrush, isChecked, Zoom));
    }

    // Drawn rather than rendered from a glyph. U+2610/U+2611 (☐/☑) were the obvious choice, but the
    // editor font carries neither, so WPF font fallback resolved each character independently and
    // the two states came out visibly different sizes. Drawing makes both states identical by
    // construction, removes the dependency on any font happening to have the glyphs, and still
    // scales with the editor's font size and follows the theme's foreground brush.
    // The box itself already follows zoom through emSize; only the indent needs telling.
    private static FrameworkElement BuildCheckbox(double emSize, Brush foreground, bool isChecked, double zoom)
    {
        double side = Math.Round(emSize * 0.78);

        var container = new Grid
        {
            Width  = side,
            Height = side,
            // Same indent as bullets and blockquote content, read from the one place it's defined.
            Margin = new Thickness(BlockquoteMarkerElementGenerator.IndentPerLevel * zoom, 0, 0, 0),
            SnapsToDevicePixels = true,
        };

        container.Children.Add(new Border
        {
            BorderBrush     = foreground,
            BorderThickness = new Thickness(Math.Max(1.0, Math.Round(side / 12))),
            CornerRadius    = new CornerRadius(Math.Max(1.0, side / 8)),
            Background      = Brushes.Transparent,
        });

        if (isChecked)
        {
            double inset = side * 0.24;
            container.Children.Add(new Polyline
            {
                Points =
                [
                    new Point(inset, side * 0.52),
                    new Point(side * 0.42, side - inset),
                    new Point(side - inset, inset),
                ],
                Stroke             = foreground,
                StrokeThickness    = Math.Max(1.2, side / 7),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap   = PenLineCap.Round,
                StrokeLineJoin     = PenLineJoin.Round,
            });
        }

        return container;
    }
}
