using ICSharpCode.AvalonEdit.Document;
using MDEdit.Editing;

namespace MDEdit.Tests;

public class MarkdownSyntaxTests
{
    [Theory]
    [InlineData("# Title", 1, 2)]
    [InlineData("## Title", 2, 3)]
    [InlineData("###### Title", 6, 7)]
    public void TryGetHeadingLevel_ValidHeading_ReturnsLevelAndMarkerLength(string text, int expectedLevel, int expectedMarkerLength)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var result = MarkdownSyntax.TryGetHeadingLevel(doc, line, out int level, out int markerLength);

        Assert.True(result);
        Assert.Equal(expectedLevel, level);
        Assert.Equal(expectedMarkerLength, markerLength);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Plain text")]
    [InlineData("#NoSpace")]
    [InlineData("####### TooManyHashes")]
    [InlineData("#")]
    public void TryGetHeadingLevel_NotAHeading_ReturnsFalse(string text)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var result = MarkdownSyntax.TryGetHeadingLevel(doc, line, out _, out _);

        Assert.False(result);
    }
}
