using ICSharpCode.AvalonEdit.Document;
using MDEdit.Editing;

namespace MDEdit.Tests;

/// <summary>
/// <see cref="BlockquoteMarkerElementGenerator.TryGetRenderedDepth"/> — the one gate the marker
/// generator and <c>BlockquoteAccentBarRenderer</c> both decide from.
/// </summary>
/// <remarks>
/// The generators themselves are WPF-bound and untested by the usual rule, but this gate is not: it
/// takes a <see cref="TextDocument"/> and a line number and touches no visual state, so the reveal
/// decision that the two halves must agree on <i>can</i> be pinned. It is worth pinning because they
/// disagreed in shipped code — the renderer had no caret awareness at all and kept drawing a bar
/// across the line whose raw source had been revealed.
/// </remarks>
public class BlockquoteRenderGateTests
{
    private static BlockquoteMarkerElementGenerator Generator(int caretLine = -1)
        => new() { Enabled = true, CaretLine = caretLine };

    [Fact]
    public void PlainLine_IsNotRendered()
    {
        var doc = new TextDocument("not a quote");
        Assert.False(Generator().TryGetRenderedDepth(doc, 1, out int depth));
        Assert.Equal(0, depth);
    }

    [Theory]
    [InlineData("> quoted", 1)]
    [InlineData("> > deeper", 2)]
    [InlineData(">>> deepest", 3)]
    public void QuotedLine_IsRenderedAtItsDepth(string text, int expected)
    {
        var doc = new TextDocument(text);
        Assert.True(Generator().TryGetRenderedDepth(doc, 1, out int depth));
        Assert.Equal(expected, depth);
    }

    // The bug: the caret's line reveals its raw ">" and reserves no indent, so nothing may be drawn
    // for it either.
    [Fact]
    public void CaretLine_IsNotRendered()
    {
        var doc = new TextDocument("> quoted");
        Assert.False(Generator(caretLine: 1).TryGetRenderedDepth(doc, 1, out int depth));
        Assert.Equal(0, depth);
    }

    // Only the caret's own line reveals — a blockquote is per-line, not per-block.
    [Fact]
    public void OtherLinesOfTheSameQuote_StayRendered()
    {
        var doc = new TextDocument("> one\n> two\n> three");
        var gate = Generator(caretLine: 2);

        Assert.True(gate.TryGetRenderedDepth(doc, 1, out _));
        Assert.False(gate.TryGetRenderedDepth(doc, 2, out _));
        Assert.True(gate.TryGetRenderedDepth(doc, 3, out _));
    }

    // Returning false (rather than a depth with a flag beside it) is what splits the renderer's
    // contiguous-run scan around the revealed line, instead of drawing one bar straight over it.
    [Fact]
    public void CaretLine_BreaksARunRatherThanShorteningIt()
    {
        var doc = new TextDocument("> one\n> two\n> three");
        var gate = Generator(caretLine: 2);

        var rendered = new[] { 1, 2, 3 }
            .Select(n => gate.TryGetRenderedDepth(doc, n, out int d) ? d : 0)
            .ToArray();

        Assert.Equal([1, 0, 1], rendered);
    }

    [Fact]
    public void Disabled_RendersNothing()
    {
        var doc = new TextDocument("> quoted");
        var gate = new BlockquoteMarkerElementGenerator { Enabled = false };
        Assert.False(gate.TryGetRenderedDepth(doc, 1, out _));
    }

    // Draw() converts visual lines to line numbers, and a redraw can race a document edit, so an
    // out-of-range number must decline rather than throw from inside the render loop.
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-5)]
    public void OutOfRangeLineNumber_DeclinesInsteadOfThrowing(int lineNumber)
    {
        var doc = new TextDocument("> quoted");
        Assert.False(Generator().TryGetRenderedDepth(doc, lineNumber, out _));
    }
}
