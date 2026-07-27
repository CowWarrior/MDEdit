using ICSharpCode.AvalonEdit.Document;
using MDEdit.Editing;

namespace MDEdit.Tests;

public class MarkdownSyntaxTableTests
{
    private static TextDocument Doc(params string[] lines) => new(string.Join("\n", lines));

    private static bool IsRow(TextDocument doc, int lineNumber)
        => MarkdownSyntax.IsTableRowLine(doc, doc.GetLineByNumber(lineNumber));

    private static bool IsDelimiter(TextDocument doc, int lineNumber)
        => MarkdownSyntax.IsTableDelimiterLine(doc, doc.GetLineByNumber(lineNumber));

    // ── IsTableRowLine ────────────────────────────────────────────────────

    [Theory]
    [InlineData("| a | b |")]
    [InlineData("| a | b |   ")]     // trailing whitespace after the final pipe is fine
    [InlineData("||")]               // one empty cell is still a row shape
    [InlineData("| --- | --- |")]    // a delimiter line is a row line by shape
    public void IsTableRowLine_BothOuterPipes_IsRow(string text)
        => Assert.True(IsRow(Doc(text), 1));

    [Theory]
    [InlineData("a | b |")]          // no leading pipe — "a | b" prose must never be a row
    [InlineData("| a | b")]          // no trailing pipe — stricter than GFM, deliberately
    [InlineData("  | a |")]          // indented — line-start-only, like the list markers
    [InlineData("|")]
    [InlineData("plain text")]
    [InlineData("")]
    public void IsTableRowLine_MissingOuterPipe_IsNotRow(string text)
        => Assert.False(IsRow(Doc(text), 1));

    // ── IsTableDelimiterLine ──────────────────────────────────────────────

    [Theory]
    [InlineData("|---|")]
    [InlineData("| --- |")]
    [InlineData("|:---|")]
    [InlineData("|---:|")]
    [InlineData("|:---:|")]
    [InlineData("| :--- | ---: | :---: | --- |")]
    [InlineData("|----------|")]
    public void IsTableDelimiterLine_DashCells_IsDelimiter(string text)
        => Assert.True(IsDelimiter(Doc(text), 1));

    [Theory]
    [InlineData("|--|")]             // two dashes — three is the floor, same as IsHorizontalRule
    [InlineData("|:-:|")]
    [InlineData("| - - - |")]
    [InlineData("| abc |")]
    [InlineData("| --- | abc |")]    // every cell must be delimiter-shaped
    [InlineData("||")]               // an empty cell is not a delimiter cell
    [InlineData("--- | ---")]        // no outer pipes — not even a row
    public void IsTableDelimiterLine_NotDashCells_IsNotDelimiter(string text)
        => Assert.False(IsDelimiter(Doc(text), 1));

    // ── GetTableCells / GetTablePipeOffsets ───────────────────────────────

    [Fact]
    public void GetTableCells_TwoCells_ReportsTrimmedOffsets()
    {
        var cells = MarkdownSyntax.GetTableCells("| a | bc |");
        Assert.Equal(2, cells.Count);
        Assert.Equal((2, 1), cells[0]); // "a"
        Assert.Equal((6, 2), cells[1]); // "bc"
    }

    [Fact]
    public void GetTableCells_EscapedPipe_StaysInsideCell()
    {
        var text  = @"| a \| b | c |";
        var cells = MarkdownSyntax.GetTableCells(text);
        Assert.Equal(2, cells.Count);
        Assert.Equal(@"a \| b", text.Substring(cells[0].Start, cells[0].Length));
        Assert.Equal("c", text.Substring(cells[1].Start, cells[1].Length));
    }

    [Fact]
    public void GetTableCells_EmptyCells_ReportZeroLength()
    {
        var cells = MarkdownSyntax.GetTableCells("|| a ||");
        Assert.Equal(3, cells.Count);
        Assert.Equal(0, cells[0].Length);
        Assert.Equal(1, cells[1].Length);
        Assert.Equal(0, cells[2].Length);
    }

    [Fact]
    public void GetTablePipeOffsets_EscapedPipe_NotReported()
    {
        var pipes = MarkdownSyntax.GetTablePipeOffsets(@"| a \| b |");
        Assert.Equal([0, 9], pipes);
    }

    // ── GetTableAlignments ────────────────────────────────────────────────

    [Fact]
    public void GetTableAlignments_ColonPlacement_MapsToAlignment()
    {
        var alignments = MarkdownSyntax.GetTableAlignments("|:---|:---:|---:|---|");
        Assert.Equal(
            [TableColumnAlignment.Left, TableColumnAlignment.Center, TableColumnAlignment.Right, TableColumnAlignment.Left],
            alignments);
    }

    // ── TryGetTableBlock ──────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void TryGetTableBlock_FromEveryLine_FindsSameBounds(int lineNumber)
    {
        var doc = Doc("| a | b |", "| --- | --- |", "| 1 | 2 |", "| 3 | 4 |");
        Assert.True(MarkdownSyntax.TryGetTableBlock(doc, lineNumber, out int start, out int end));
        Assert.Equal(1, start);
        Assert.Equal(4, end);
    }

    [Fact]
    public void TryGetTableBlock_BlankLineEndsTable()
    {
        var doc = Doc("| a |", "| --- |", "| 1 |", "", "| x |");
        Assert.True(MarkdownSyntax.TryGetTableBlock(doc, 2, out _, out int end));
        Assert.Equal(3, end);
        // "| x |" after the blank line has no delimiter of its own — not a table.
        Assert.False(MarkdownSyntax.TryGetTableBlock(doc, 5, out _, out _));
    }

    [Fact]
    public void TryGetTableBlock_HeaderDelimiterCellCountMismatch_NotATable()
    {
        var doc = Doc("| a | b |", "| --- |", "| c |");
        for (int n = 1; n <= 3; n++)
            Assert.False(MarkdownSyntax.TryGetTableBlock(doc, n, out _, out _));
    }

    [Fact]
    public void TryGetTableBlock_RowShapedLineAboveHeader_ExcludedButTableStillFound()
    {
        // "| x |" is row-shaped prose directly above a real 1-column table; it must neither
        // become the table's header (its cell count differs) nor stop the table being found.
        var doc = Doc("| x | y |", "| a |", "| --- |", "| 1 |");
        Assert.False(MarkdownSyntax.TryGetTableBlock(doc, 1, out _, out _));
        Assert.True(MarkdownSyntax.TryGetTableBlock(doc, 3, out int start, out int end));
        Assert.Equal(2, start);
        Assert.Equal(4, end);
    }

    [Fact]
    public void TryGetTableBlock_HeaderAndDelimiterOnly_IsATable()
    {
        var doc = Doc("| a |", "| --- |");
        Assert.True(MarkdownSyntax.TryGetTableBlock(doc, 1, out int start, out int end));
        Assert.Equal(1, start);
        Assert.Equal(2, end);
    }

    [Fact]
    public void TryGetTableBlock_DelimiterAtDocumentStart_NoHeader_NotATable()
    {
        var doc = Doc("| --- |", "| a |");
        Assert.False(MarkdownSyntax.TryGetTableBlock(doc, 1, out _, out _));
    }

    [Fact]
    public void TryGetTableBlock_BodyRowWithDifferentCellCount_StillInBlock()
    {
        // GFM pads/truncates mismatched body rows; here the row stays in the block and the
        // renderer gives extra cells extra columns rather than ever hiding content.
        var doc = Doc("| a | b |", "| --- | --- |", "| only |");
        Assert.True(MarkdownSyntax.TryGetTableBlock(doc, 3, out int start, out int end));
        Assert.Equal(1, start);
        Assert.Equal(3, end);
    }

    [Fact]
    public void TryGetTableBlock_PlainAndHeadingLines_NotTables()
    {
        var doc = Doc("plain", "# heading", "> quote");
        for (int n = 1; n <= 3; n++)
            Assert.False(MarkdownSyntax.TryGetTableBlock(doc, n, out _, out _));
    }
}
