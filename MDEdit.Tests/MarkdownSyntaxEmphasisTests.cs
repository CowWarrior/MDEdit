using ICSharpCode.AvalonEdit.Document;
using MDEdit.Editing;

namespace MDEdit.Tests;

public class MarkdownSyntaxEmphasisTests
{
    private static EmphasisSpan Single(string text)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);
        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);
        return Assert.Single(spans);
    }

    [Theory]
    [InlineData("**bold**", 2)]
    [InlineData("__bold__", 2)]
    [InlineData("*italic*", 1)]
    [InlineData("_italic_", 1)]
    [InlineData("***both***", 3)]
    [InlineData("___both___", 3)]
    [InlineData("~~strike~~", 2)]
    [InlineData("`code`", 1)]
    public void FindEmphasisSpans_WholeLineIsOneRun_MatchesEntireLineWithExpectedMarkerLength(string text, int expectedMarkerLength)
    {
        var span = Single(text);

        Assert.Equal(0, span.Start);
        Assert.Equal(text.Length, span.End);
        Assert.Equal(expectedMarkerLength, span.MarkerLength);
    }

    [Fact]
    public void FindEmphasisSpans_BoldTakesPrecedenceOverItalic()
    {
        // Without bold-before-italic precedence, "*" would greedily match italic first and
        // never recognize the "**...**" run as bold — this locks in the same ordering as
        // Markdown.xshd (BoldItalic, then Bold, then Italic).
        var span = Single("**bold**");
        Assert.Equal(2, span.MarkerLength);
    }

    [Fact]
    public void FindEmphasisSpans_MultipleRunsOnOneLine_FindsBothInOrder()
    {
        var text = "one **two** three *four*";
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Equal(2, spans.Count);
        Assert.Equal(new EmphasisSpan(4, 11, 2), spans[0]);   // "**two**"
        Assert.Equal(new EmphasisSpan(18, 24, 1), spans[1]);  // "*four*"
    }

    [Theory]
    [InlineData("no emphasis here")]
    [InlineData("")]
    [InlineData("a single * asterisk")]
    [InlineData("trailing unmatched **bold")]
    [InlineData("trailing unmatched ~~strike")]
    [InlineData("~~~")]   // fenced-code-block delimiter line, not strikethrough
    [InlineData("a single ` backtick")]
    [InlineData("trailing unmatched `code")]
    public void FindEmphasisSpans_NoValidRun_ReturnsEmpty(string text)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Empty(spans);
    }

    // Mixed-delimiter nesting: CommonMark forbids nesting the same delimiter inside itself, so
    // "_**text**_" (italic wrapping bold) and "**_text_**" (bold wrapping italic) are the
    // standard way authors combine the two as two distinct nested runs, rather than "***text***".
    [Theory]
    [InlineData("_**example one**_")]   // italic (underscore) wrapping bold (star)
    [InlineData("*__example two__*")]   // italic (star) wrapping bold (underscore)
    public void FindEmphasisSpans_ItalicWrappingBold_FindsBothOuterAndInnerSpans(string text)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Equal(2, spans.Count);
        var outer = spans[0];
        var inner = spans[1];
        Assert.Equal(new EmphasisSpan(0, text.Length, 1), outer);        // whole line, italic marker
        Assert.Equal(new EmphasisSpan(1, text.Length - 1, 2), inner);    // inside the outer markers, bold marker
    }

    [Theory]
    [InlineData("**_example three_**")]  // bold (star) wrapping italic (underscore)
    [InlineData("__*example four*__")]   // bold (underscore) wrapping italic (star)
    public void FindEmphasisSpans_BoldWrappingItalic_FindsBothOuterAndInnerSpans(string text)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Equal(2, spans.Count);
        var outer = spans[0];
        var inner = spans[1];
        Assert.Equal(new EmphasisSpan(0, text.Length, 2), outer);        // whole line, bold marker
        Assert.Equal(new EmphasisSpan(2, text.Length - 2, 1), inner);    // inside the outer markers, italic marker
    }

    // Strikethrough's '~' delimiter is a third family alongside stars and underscores, so it
    // nests with either of them the same way they nest with each other.
    [Theory]
    [InlineData("~~**text one**~~", 2)]   // strike wrapping bold
    [InlineData("~~_text two_~~", 1)]     // strike wrapping italic
    public void FindEmphasisSpans_StrikeWrappingEmphasis_FindsBothOuterAndInnerSpans(string text, int innerMarkerLength)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Equal(2, spans.Count);
        Assert.Equal(new EmphasisSpan(0, text.Length, 2), spans[0]);                                // whole line, ~~ marker
        Assert.Equal(new EmphasisSpan(2, text.Length - 2, innerMarkerLength), spans[1]);            // nested run inside
    }

    [Fact]
    public void FindEmphasisSpans_EmphasisWrappingStrike_FindsBothOuterAndInnerSpans()
    {
        var text = "**~~text~~**";
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Equal(2, spans.Count);
        Assert.Equal(new EmphasisSpan(0, text.Length, 2), spans[0]);              // whole line, bold marker
        Assert.Equal(new EmphasisSpan(2, text.Length - 2, 2), spans[1]);          // nested strike inside
    }

    // A code span's content is literal (CommonMark gives code spans precedence over emphasis),
    // so emphasis markers inside backticks must NOT produce nested spans — otherwise live
    // preview would hide the "**" of "`**not bold**`" even though it renders as plain text.
    [Theory]
    [InlineData("`**not bold**`")]
    [InlineData("`_not italic_`")]
    [InlineData("`~~not struck~~`")]
    public void FindEmphasisSpans_EmphasisInsideCode_IsLiteralSingleCodeSpan(string text)
    {
        var span = Single(text);

        Assert.Equal(new EmphasisSpan(0, text.Length, 1), span);
    }

    [Fact]
    public void FindEmphasisSpans_CodeInsideBold_FindsBothOuterAndInnerSpans()
    {
        var text = "**bold `code` here**";
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Equal(2, spans.Count);
        Assert.Equal(new EmphasisSpan(0, 20, 2), spans[0]);   // whole line, bold marker
        Assert.Equal(new EmphasisSpan(7, 13, 1), spans[1]);   // "`code`" nested inside
    }

    [Fact]
    public void FindEmphasisSpans_StrikeAndBoldOnOneLine_FindsBothInOrder()
    {
        var text = "one ~~two~~ three **four**";
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Equal(2, spans.Count);
        Assert.Equal(new EmphasisSpan(4, 11, 2), spans[0]);   // "~~two~~"
        Assert.Equal(new EmphasisSpan(18, 26, 2), spans[1]);  // "**four**"
    }
}
