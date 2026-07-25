using ICSharpCode.AvalonEdit.Document;
using MDEdit.Editing;

namespace MDEdit.Tests;

public class MarkdownSyntaxListTests
{
    [Theory]
    [InlineData("- item", 0)]
    [InlineData("* item", 0)]
    [InlineData("+ item", 0)]
    [InlineData("- ", 0)]
    [InlineData("  - nested", 2)]
    [InlineData("    * deeply nested", 4)]
    [InlineData("\t- tab indented", 1)]
    [InlineData("-\titem", 0)]
    public void TryGetBulletListMarker_ValidBullet_ReturnsMarkerOffset(string text, int expectedOffset)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var result = MarkdownSyntax.TryGetBulletListMarker(doc, line, out int markerOffset);

        Assert.True(result);
        Assert.Equal(expectedOffset, markerOffset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Plain text")]
    [InlineData("-nospace")]
    [InlineData("-")]
    [InlineData("a - b")]           // mid-sentence dash, not at line start
    [InlineData("1. item")]         // numbered marker — deliberately not a bullet
    [InlineData("---")]             // horizontal rule
    [InlineData("- - -")]           // horizontal rule despite starting with "- "
    [InlineData("* * *")]           // horizontal rule despite starting with "* "
    [InlineData("> quoted")]
    public void TryGetBulletListMarker_NotABullet_ReturnsFalse(string text)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var result = MarkdownSyntax.TryGetBulletListMarker(doc, line, out _);

        Assert.False(result);
    }

    [Theory]
    [InlineData("1. item", 0, 2)]
    [InlineData("12. item", 0, 3)]
    [InlineData("1. ", 0, 2)]
    [InlineData("  3. nested", 2, 2)]
    [InlineData("\t2. tab indented", 1, 2)]
    [InlineData("1.\titem", 0, 2)]
    public void TryGetNumberedListMarker_ValidMarker_ReturnsOffsetAndLength(string text, int expectedOffset, int expectedLength)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var result = MarkdownSyntax.TryGetNumberedListMarker(doc, line, out int markerOffset, out int markerLength);

        Assert.True(result);
        Assert.Equal(expectedOffset, markerOffset);
        Assert.Equal(expectedLength, markerLength);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Plain text")]
    [InlineData("1.nospace")]
    [InlineData("1.")]
    [InlineData("1 item")]          // no dot
    [InlineData(". item")]          // no digits
    [InlineData("version 1. note")] // mid-sentence, not at line start
    [InlineData("- item")]          // bullet marker — deliberately not numbered
    public void TryGetNumberedListMarker_NotAMarker_ReturnsFalse(string text)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var result = MarkdownSyntax.TryGetNumberedListMarker(doc, line, out _, out _);

        Assert.False(result);
    }

    [Theory]
    [InlineData("---")]
    [InlineData("***")]
    [InlineData("___")]
    [InlineData("- - -")]
    [InlineData("* * *")]
    [InlineData("----------")]
    public void IsHorizontalRule_ValidRule_ReturnsTrue(string text)
        => Assert.True(MarkdownSyntax.IsHorizontalRule(text));

    [Theory]
    [InlineData("")]
    [InlineData("--")]
    [InlineData("- item")]
    [InlineData(" ---")]            // colorizer's rule check requires column 0
    [InlineData("-*-")]             // mixed characters
    public void IsHorizontalRule_NotARule_ReturnsFalse(string text)
        => Assert.False(MarkdownSyntax.IsHorizontalRule(text));

    // Mirrors Markdown.xshd's rule order: ListMarker precedes the emphasis rules, so on a bullet
    // line the leading "* " can never open an italic run — without the skip, the italic pattern
    // would swallow "* item*" whole as one run starting at the marker.
    [Fact]
    public void FindEmphasisSpans_BulletMarkerStar_NotTreatedAsItalicOpener()
    {
        var doc  = new TextDocument("* item*");
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Empty(spans);
    }

    [Fact]
    public void FindEmphasisSpans_EmphasisInsideBulletItem_StillFound()
    {
        var doc  = new TextDocument("- some **bold** text");
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        var span = Assert.Single(spans);
        Assert.Equal(7, span.Start);
        Assert.Equal(15, span.End);
        Assert.Equal(2, span.MarkerLength);
    }

    // Without the bullet skip, the italic-skip pass in FindLinkSpans would swallow
    // "* [x](url) *" as one emphasis run and never find the link inside it.
    [Fact]
    public void FindLinkSpans_LinkInsideStarBulletItem_StillFound()
    {
        var doc  = new TextDocument("* [x](url) *note*");
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindLinkSpans(doc, line);

        var span = Assert.Single(spans);
        Assert.Equal(2, span.Start);
        Assert.Equal(10, span.End);
    }
}
