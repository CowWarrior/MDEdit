using ICSharpCode.AvalonEdit.Document;
using MDEdit.Editing;

namespace MDEdit.Tests;

public class MarkdownSyntaxLinkTests
{
    private static LinkSpan Single(string text)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);
        var spans = MarkdownSyntax.FindLinkSpans(doc, line);
        return Assert.Single(spans);
    }

    [Fact]
    public void FindLinkSpans_InlineLink_FindsTextBoundsAndFullExtent()
    {
        var text = "[link text](https://example.com)";
        var span = Single(text);

        Assert.Equal(0, span.Start);
        Assert.Equal(1, span.TextStart);
        Assert.Equal(10, span.TextEnd); // "link text" is 9 chars, starting at offset 1
        Assert.Equal(text.Length, span.End);
    }

    [Fact]
    public void FindLinkSpans_Image_FindsAltTextBoundsIncludingBangBracketPrefix()
    {
        var text = "![alt text](image.png)";
        var span = Single(text);

        Assert.Equal(0, span.Start);
        Assert.Equal(2, span.TextStart); // "![" is 2 chars
        Assert.Equal(10, span.TextEnd);  // "alt text" is 8 chars, starting at offset 2
        Assert.Equal(text.Length, span.End);
    }

    [Fact]
    public void FindLinkSpans_ReferenceLink_FindsTextBounds()
    {
        var text = "[link text][ref]";
        var span = Single(text);

        Assert.Equal(0, span.Start);
        Assert.Equal(1, span.TextStart);
        Assert.Equal(10, span.TextEnd);
        Assert.Equal(text.Length, span.End);
    }

    [Theory]
    [InlineData("no link here")]
    [InlineData("")]
    [InlineData("[unclosed](")]
    [InlineData("[text] not a link")]
    [InlineData("(not a link)[either]")]
    public void FindLinkSpans_NoValidLink_ReturnsEmpty(string text)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindLinkSpans(doc, line);

        Assert.Empty(spans);
    }

    [Fact]
    public void FindLinkSpans_MultipleLinksOnOneLine_FindsBothInOrder()
    {
        var text = "see [one](a) and [two](b)";
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindLinkSpans(doc, line);

        Assert.Equal(2, spans.Count);
        Assert.Equal(4, spans[0].Start);
        Assert.Equal(17, spans[1].Start);
    }

    // Markdown.xshd's Link rule is a flat Rule (no nested RuleSet, unlike Bold/Italic's Span), so
    // "**bold**" inside link text is never separately Bold-colored — it's swallowed as literal
    // content within the single Link match. FindLinkSpans mirrors that: one opaque link span,
    // not a link containing a separately-found nested emphasis span.
    [Fact]
    public void FindLinkSpans_EmphasisInsideLinkText_TreatsWholeLinkAsOneOpaqueSpan()
    {
        var text = "[**bold** link](url)";
        var span = Single(text);

        Assert.Equal(0, span.Start);
        Assert.Equal(text.Length, span.End);
    }

    // The reverse: "[link](url)" inside "**...**" is swallowed by Bold's own flat match before a
    // Link rule ever gets a chance to match starting partway through it.
    [Fact]
    public void FindLinkSpans_LinkInsideEmphasis_IsNotFoundSeparately()
    {
        var doc  = new TextDocument("**a [link](url) b**");
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindLinkSpans(doc, line);

        Assert.Empty(spans);
    }

    [Fact]
    public void FindLinkSpans_LinkInsideInlineCode_IsNotFoundSeparately()
    {
        var doc  = new TextDocument("`[link](url)`");
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindLinkSpans(doc, line);

        Assert.Empty(spans);
    }
}
