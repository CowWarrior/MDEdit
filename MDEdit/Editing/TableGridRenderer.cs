using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit.Editing;

/// <summary>
/// Draws a rendered table's grid — header shading, one horizontal line at each row boundary,
/// and vertical lines at every column boundary — as continuous strokes spanning the table's
/// visible lines. This is a background renderer for the same reason
/// <see cref="BlockquoteAccentBarRenderer"/> is: per-line elements can't make lines touch
/// across the inter-line space TextView owns, so anything that must run unbroken through
/// several visual lines has to be drawn in the TextView's shared pixel space. All geometry
/// comes from <see cref="TableRowElementGenerator.TryGetRenderedTable"/> and that class's
/// layout constants, so the drawn grid always matches the space the row elements occupy —
/// including the reveal decision itself: when the caret is inside a table (raw source shown),
/// the same call answers false here and nothing is drawn over the text.
/// </summary>
internal sealed class TableGridRenderer : IBackgroundRenderer
{
    // Border/fill pairs tuned against the editor backgrounds the same way the dark Highlight
    // color was: light against white (GitHub-ish hairline + header wash), dark against the
    // #1E1E1E editor using the chrome divider gray so the grid reads as structure, not content.
    private static readonly SolidColorBrush LightLineBrush   = Freeze(Color.FromRgb(0xD0, 0xD7, 0xDE));
    private static readonly SolidColorBrush DarkLineBrush    = Freeze(Color.FromRgb(0x3F, 0x3F, 0x46));
    private static readonly SolidColorBrush LightHeaderBrush = Freeze(Color.FromRgb(0xF6, 0xF8, 0xFA));
    private static readonly SolidColorBrush DarkHeaderBrush  = Freeze(Color.FromRgb(0x2D, 0x2D, 0x30));

    private const double LineThickness = 1.0;

    private readonly TableRowElementGenerator _generator;

    public TableGridRenderer(TableRowElementGenerator generator) => _generator = generator;

    public bool Enabled { get; set; }
    public bool IsDark { get; set; }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!Enabled) return;

        var doc = textView.Document;
        if (doc == null) return;

        var visualLines = textView.VisualLines;
        if (visualLines.Count == 0) return;

        var lineBrush   = IsDark ? DarkLineBrush : LightLineBrush;
        var headerBrush = IsDark ? DarkHeaderBrush : LightHeaderBrush;

        // Group consecutive visual lines belonging to the same rendered table. Two distinct
        // tables can never be adjacent (contiguous row lines merge into one block), so a
        // matching StartLine is enough to identify the run.
        int i = 0;
        while (i < visualLines.Count)
        {
            if (!_generator.TryGetRenderedTable(doc, visualLines[i].FirstDocumentLine.LineNumber, out var layout))
            {
                i++;
                continue;
            }

            int runStart = i;
            while (i < visualLines.Count
                && _generator.TryGetRenderedTable(doc, visualLines[i].FirstDocumentLine.LineNumber, out var next)
                && next.StartLine == layout.StartLine)
            {
                i++;
            }
            DrawTable(textView, drawingContext, runStart, i - 1, layout, lineBrush, headerBrush);
        }
    }

    private static void DrawTable(TextView textView, DrawingContext drawingContext,
        int runStart, int runEnd, TableLayout layout, Brush lineBrush, Brush headerBrush)
    {
        var visualLines = textView.VisualLines;

        // The grid's left edge matches the row elements' left margin — the shared indent every
        // indented construct uses, read from where it's defined so the drawn lines always sit
        // exactly under the cells. Coordinates are rounded so the 1px lines land on whole pixels.
        double x0     = Math.Round(BlockquoteMarkerElementGenerator.IndentPerLevel - textView.HorizontalOffset);
        double width  = layout.TotalWidth;
        double top    = Math.Round(visualLines[runStart].VisualTop - textView.VerticalOffset);
        double bottom = Math.Round(visualLines[runEnd].VisualTop + visualLines[runEnd].Height - textView.VerticalOffset);

        // Header shading first, so the lines draw over it.
        for (int v = runStart; v <= runEnd; v++)
        {
            if (visualLines[v].FirstDocumentLine.LineNumber != layout.StartLine) continue;
            double bandTop = Math.Round(visualLines[v].VisualTop - textView.VerticalOffset);
            drawingContext.DrawRectangle(headerBrush, null,
                new Rect(x0, bandTop, width, Math.Round(visualLines[v].Height)));
        }

        // A horizontal line at the top edge of every visible band (the topmost doubles as the
        // table's outer top when the header is visible, and as an interior boundary when the
        // table is scrolled mid-block), plus the outer bottom when the last row is visible.
        // The hidden delimiter band gets a line at both edges this way, reading as the
        // separator zone under the header.
        for (int v = runStart; v <= runEnd; v++)
        {
            double y = Math.Round(visualLines[v].VisualTop - textView.VerticalOffset);
            drawingContext.DrawRectangle(lineBrush, null, new Rect(x0, y, width, LineThickness));
        }
        if (visualLines[runEnd].FirstDocumentLine.LineNumber == layout.EndLine)
            drawingContext.DrawRectangle(lineBrush, null, new Rect(x0, bottom - LineThickness, width, LineThickness));

        // Vertical lines at every column boundary, spanning the visible extent.
        double x = x0;
        drawingContext.DrawRectangle(lineBrush, null, new Rect(x, top, LineThickness, bottom - top));
        for (int c = 0; c < layout.ColumnCount; c++)
        {
            x += layout.ColumnWidths[c] + 2 * TableRowElementGenerator.CellPadding;
            drawingContext.DrawRectangle(lineBrush, null, new Rect(Math.Round(x), top, LineThickness, bottom - top));
        }
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }
}
