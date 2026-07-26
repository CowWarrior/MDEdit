using ICSharpCode.AvalonEdit.Document;
using MDEdit.Editing;

namespace MDEdit.Tests;

public class MarkdownSyntaxUnderlineTests
{
    private static IReadOnlyList<UnderlineSpan> Underlines(string text)
    {
        var doc = new TextDocument(text);
        return MarkdownSyntax.FindUnderlineSpans(doc, doc.GetLineByNumber(1));
    }

    [Fact]
    public void FindUnderlineSpans_WholeLine_SplitsTagsFromContent()
    {
        var text = "<u>under</u>";
        var span = Assert.Single(Underlines(text));

        Assert.Equal(0, span.Start);
        Assert.Equal(3, span.ContentStart);            // after "<u>"
        Assert.Equal(8, span.ContentEnd);              // before "</u>"
        Assert.Equal(text.Length, span.End);
        Assert.Equal("under", text[span.ContentStart..span.ContentEnd]);
    }

    [Fact]
    public void FindUnderlineSpans_MidSentence_ReportsCorrectOffsets()
    {
        var text = "a <u>b c</u> d";
        var span = Assert.Single(Underlines(text));

        Assert.Equal(2, span.Start);
        Assert.Equal("b c", text[span.ContentStart..span.ContentEnd]);
        Assert.Equal(12, span.End);
    }

    [Fact]
    public void FindUnderlineSpans_TwoRunsOnOneLine_ReturnsBothInOrder()
    {
        var text = "<u>one</u> and <u>two</u>";
        var spans = Underlines(text);

        Assert.Equal(2, spans.Count);
        Assert.Equal("one", text[spans[0].ContentStart..spans[0].ContentEnd]);
        Assert.Equal("two", text[spans[1].ContentStart..spans[1].ContentEnd]);
    }

    [Theory]
    [InlineData("no tags here")]
    [InlineData("")]
    [InlineData("<u>unclosed")]
    [InlineData("unopened</u>")]
    [InlineData("<u></u>")]              // empty content — nothing to underline
    [InlineData("<U>caps</U>")]          // lowercase tag only, by design
    [InlineData("< u >spaced</ u >")]    // no whitespace inside the tag, by design
    [InlineData("<u class='x'>attr</u>")] // no attributes, by design
    [InlineData("<b>other tag</b>")]
    public void FindUnderlineSpans_NotAnUnderlineRun_ReturnsEmpty(string text)
    {
        Assert.Empty(Underlines(text));
    }

    // Underline is a container, not an opaque run: emphasis inside it is still emphasis, and the
    // two scanners find their own runs independently. This is the deliberate difference from the
    // emphasis/link scanners, which skip past each other's matches.
    [Fact]
    public void FindUnderlineSpans_ContainingEmphasis_StillFindsTheUnderline()
    {
        var text = "<u>**bold**</u>";
        var span = Assert.Single(Underlines(text));

        Assert.Equal("**bold**", text[span.ContentStart..span.ContentEnd]);
    }

    [Fact]
    public void FindEmphasisSpans_InsideUnderline_StillFindsTheEmphasis()
    {
        var doc = new TextDocument("<u>**bold**</u>");
        var span = Assert.Single(MarkdownSyntax.FindEmphasisSpans(doc, doc.GetLineByNumber(1)));

        Assert.Equal(2, span.MarkerLength);
        Assert.Equal("**bold**", "<u>**bold**</u>"[span.Start..span.End]);
    }

    [Fact]
    public void FindUnderlineSpans_InsideEmphasis_IsStillFound()
    {
        var text = "**<u>both</u>**";
        var span = Assert.Single(Underlines(text));

        Assert.Equal("both", text[span.ContentStart..span.ContentEnd]);
    }

    // The content class stops at '<', so a nested tag terminates the run rather than the scanner
    // trying to balance arbitrary HTML — this editor does not parse HTML.
    [Fact]
    public void FindUnderlineSpans_ContainingAnotherTag_DoesNotMatch()
    {
        Assert.Empty(Underlines("<u>a <b>c</b> d</u>"));
    }

    [Fact]
    public void FindUnderlineSpans_AfterBulletMarker_IsStillFound()
    {
        var text = "- <u>item</u>";
        var span = Assert.Single(Underlines(text));

        Assert.Equal("item", text[span.ContentStart..span.ContentEnd]);
    }
}
