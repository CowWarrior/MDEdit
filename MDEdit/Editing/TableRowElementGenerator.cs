using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit.Editing;

/// <summary>
/// A rendered table's computed layout: its line extent and the pixel width of each column —
/// the widest measured cell in that column across every row. Column widths are the reason
/// tables never fit the per-line/per-span generator shapes: they are a whole-table property,
/// so every row element and the grid lines have to be derived from one shared computation.
/// <see cref="ColumnCount"/> is the maximum cell count across all rows, not the delimiter's
/// count — a body row with extra cells gets extra columns rather than having content hidden;
/// rendering must never hide cell text. <see cref="Alignments"/> comes from the delimiter row
/// and may be shorter than <see cref="ColumnCount"/>; extras default to left.
/// </summary>
internal sealed record TableLayout(
    int StartLine, int EndLine, int ColumnCount,
    double[] ColumnWidths, TableColumnAlignment[] Alignments)
{
    /// <summary>Total pixel width of the rendered grid, including per-cell padding.</summary>
    public double TotalWidth
        => ColumnWidths.Sum() + ColumnCount * 2 * TableRowElementGenerator.CellPadding;
}

/// <summary>
/// Live-preview element generator for tables: whenever the caret is outside a table block,
/// each of its row lines is replaced whole by a run of fixed-width cells (header bold), and
/// the delimiter row is hidden entirely — the caret anywhere inside the block reveals the
/// whole table as raw source, the same per-block reveal as
/// <see cref="CodeBlockFenceElementGenerator"/>, so editing always happens on raw text and
/// column widths never shift under the caret. The grid lines and header shading are drawn
/// separately by <see cref="TableGridRenderer"/> — the same split as blockquotes
/// (<see cref="BlockquoteAccentBarRenderer"/>), and for the same reason: lines that must run
/// continuously across visual lines can't be produced by per-line elements. As with every
/// other generator, the row's characters keep their document offsets — selection, undo, and
/// the saved file are unaffected; only the rendering changes.
/// </summary>
/// <remarks>
/// Cell text is shown verbatim (markers of inline constructs inside a cell stay visible when
/// the row is rendered) — the whole line is consumed by the row element, so the inline
/// generators never run on it, and the measurement below is exact for the text actually
/// displayed. Rendering inline formatting inside cells is a follow-up, not an oversight.
/// A row wider than the viewport does not word-wrap (a single inline object can't) — known
/// limitation, same per-block reveal makes the raw text reachable regardless.
/// </remarks>
internal sealed class TableRowElementGenerator : VisualLineElementGenerator
{
    // Layout knobs, referenced by TableGridRenderer so the drawn lines always match the
    // space the row elements occupy (the BlockquoteMarkerElementGenerator constant-sharing
    // convention).
    internal const double CellPadding    = 6.0;  // horizontal padding inside each cell
    internal const double MinColumnWidth = 8.0;  // an all-empty column still gets a visible cell

    public bool Enabled { get; set; }
    public int CaretLine { get; set; } = -1;

    // Column widths are measured with the editor's own typeface, once per table, and cached.
    // Any document change clears the cache wholesale (ReferenceEquals on the version object)
    // rather than tracking which table a change touched — recomputing is cheap and only
    // happens on redraw, and a stale width can then never survive an edit.
    private readonly Dictionary<int, TableLayout> _layouts = [];
    private ITextSourceVersion? _layoutsVersion;

    // Captured on every GetFirstInterestedOffset call: ComputeLayout can be reached from
    // TableGridRenderer.Draw, where no CurrentContext exists. Draw always runs after the
    // visual lines it covers were constructed, so by then these hold the current values.
    private Typeface? _typeface;
    private double _emSize = 13.0;
    private double _pixelsPerDip = 1.0;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!Enabled) return -1;

        var props = CurrentContext.GlobalTextRunProperties;
        // A font change (the WYSIWYG document-font flip) invalidates every measured width
        // even though the document hasn't changed, which the version-keyed cache can't see —
        // so compare the captured properties themselves. Typeface has value equality.
        if (!Equals(_typeface, props.Typeface) || _emSize != props.FontRenderingEmSize)
        {
            _layouts.Clear();
            _layoutsVersion = null;
        }
        _typeface     = props.Typeface;
        _emSize       = props.FontRenderingEmSize;
        _pixelsPerDip = props.PixelsPerDip;

        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(startOffset);

        // Whole-line replacement, so only the line's own start is interesting (fence idiom).
        if (startOffset > line.Offset) return -1;
        if (!TryGetRenderedTable(doc, line.LineNumber, out _)) return -1;
        return line.Offset;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(offset);

        if (offset != line.Offset || !TryGetRenderedTable(doc, line.LineNumber, out var layout))
        {
            // GetFirstInterestedOffset only ever returns a rendered table line's start, so this
            // is unreachable in practice — return a harmless zero-length element rather than throw.
            return new InlineObjectElement(0, new TextBlock());
        }

        // The delimiter row is pure syntax with no content of its own; hide it whole (the
        // fence-line idiom). Its band stays a blank strip inside the grid — TableGridRenderer
        // draws horizontal lines at both its edges, so it reads as the separator zone under
        // the header rather than as an empty row.
        if (line.LineNumber == layout.StartLine + 1)
            return new InlineObjectElement(line.Length, new Rectangle { Width = 0, Height = 0 });

        return new InlineObjectElement(line.Length,
            BuildRow(doc, line, layout, isHeader: line.LineNumber == layout.StartLine));
    }

    /// <summary>
    /// Whether <paramref name="lineNumber"/> is a line of a table currently rendered as a grid
    /// (live preview on, caret outside the block), and if so that table's layout. Shared with
    /// <see cref="TableGridRenderer"/> so the grid lines and the row elements can never
    /// disagree about whether a table is rendered or how wide its columns are.
    /// </summary>
    public bool TryGetRenderedTable(TextDocument doc, int lineNumber, [NotNullWhen(true)] out TableLayout? layout)
    {
        layout = null;
        if (!Enabled || _typeface is null) return false;
        if (lineNumber < 1 || lineNumber > doc.LineCount) return false;
        if (!MarkdownSyntax.IsTableRowLine(doc, doc.GetLineByNumber(lineNumber))) return false;
        if (!MarkdownSyntax.TryGetTableBlock(doc, lineNumber, out int start, out int end)) return false;
        if (CaretLine >= start && CaretLine <= end) return false; // caret inside: whole table reveals

        layout = GetLayout(doc, start, end);
        return true;
    }

    private TableLayout GetLayout(TextDocument doc, int startLine, int endLine)
    {
        if (!ReferenceEquals(_layoutsVersion, doc.Version))
        {
            _layouts.Clear();
            _layoutsVersion = doc.Version;
        }
        if (_layouts.TryGetValue(startLine, out var cached)) return cached;

        var layout = ComputeLayout(doc, startLine, endLine);
        _layouts[startLine] = layout;
        return layout;
    }

    private TableLayout ComputeLayout(TextDocument doc, int startLine, int endLine)
    {
        var delimiterText = doc.GetText(doc.GetLineByNumber(startLine + 1));
        var alignments    = MarkdownSyntax.GetTableAlignments(delimiterText).ToArray();

        var typeface     = _typeface!;
        var boldTypeface = new Typeface(typeface.FontFamily, typeface.Style, FontWeights.Bold, typeface.Stretch);

        var widths = new List<double>();
        for (int n = startLine; n <= endLine; n++)
        {
            if (n == startLine + 1) continue; // the delimiter row has no content cells

            var text  = doc.GetText(doc.GetLineByNumber(n));
            var cells = MarkdownSyntax.GetTableCells(text);
            for (int i = 0; i < cells.Count; i++)
            {
                while (widths.Count <= i) widths.Add(MinColumnWidth);
                var display = CellDisplayText(text, cells[i]);
                if (display.Length == 0) continue;

                // The brush is required by the constructor but irrelevant to measurement.
                var formatted = new FormattedText(display, CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, n == startLine ? boldTypeface : typeface,
                    _emSize, Brushes.Black, _pixelsPerDip);
                widths[i] = Math.Max(widths[i], formatted.WidthIncludingTrailingWhitespace);
            }
        }

        // The header/delimiter pair always agrees on cell count (TryGetTableBlock requires it),
        // but guard anyway so ColumnCount covers every declared column.
        while (widths.Count < alignments.Length) widths.Add(MinColumnWidth);

        return new TableLayout(startLine, endLine, widths.Count, [.. widths], alignments);
    }

    private FrameworkElement BuildRow(TextDocument doc, DocumentLine line, TableLayout layout, bool isHeader)
    {
        var props    = CurrentContext.GlobalTextRunProperties;
        var typeface = props.Typeface;
        var text     = doc.GetText(line);
        var cells    = MarkdownSyntax.GetTableCells(text);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            // Same indent as bullets, checkboxes, and blockquote content, read from the one
            // place it's defined — so a table sits at the same pixel depth as every other
            // indented construct instead of tight against the page edge.
            Margin = new Thickness(BlockquoteMarkerElementGenerator.IndentPerLevel, 0, 0, 0),
        };
        for (int i = 0; i < layout.ColumnCount; i++)
        {
            row.Children.Add(new TextBlock
            {
                Text        = i < cells.Count ? CellDisplayText(text, cells[i]) : string.Empty,
                FontFamily  = typeface.FontFamily,
                FontStyle   = typeface.Style,
                FontWeight  = isHeader ? FontWeights.Bold : typeface.Weight,
                FontStretch = typeface.Stretch,
                FontSize    = props.FontRenderingEmSize,
                Foreground  = props.ForegroundBrush,
                Width       = layout.ColumnWidths[i] + 2 * CellPadding,
                Padding     = new Thickness(CellPadding, 0, CellPadding, 0),
                TextAlignment = i < layout.Alignments.Length
                    ? ToTextAlignment(layout.Alignments[i])
                    : TextAlignment.Left,
            });
        }
        return row;
    }

    // "\|" is a literal pipe in cell content (the split already honored the escape); everything
    // else displays as-is.
    private static string CellDisplayText(string lineText, (int Start, int Length) cell)
        => lineText.Substring(cell.Start, cell.Length).Replace(@"\|", "|");

    private static TextAlignment ToTextAlignment(TableColumnAlignment alignment) => alignment switch
    {
        TableColumnAlignment.Center => TextAlignment.Center,
        TableColumnAlignment.Right  => TextAlignment.Right,
        _                           => TextAlignment.Left,
    };
}
