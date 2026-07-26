using ICSharpCode.AvalonEdit.Document;
using MDEdit.Editing;

namespace MDEdit.Tests;

public class MarkdownSyntaxScriptTests
{
    private static IReadOnlyList<ScriptSpan> Scripts(string text)
    {
        var doc = new TextDocument(text);
        return MarkdownSyntax.FindScriptSpans(doc, doc.GetLineByNumber(1));
    }

    [Fact]
    public void FindScriptSpans_Superscript_ReturnsContentBetweenMarkers()
    {
        var span = Assert.Single(Scripts("X^2^"));

        Assert.True(span.IsSuperscript);
        Assert.Equal(2, span.ContentStart);   // the "2", not the surrounding "^"
        Assert.Equal(3, span.ContentEnd);
    }

    [Fact]
    public void FindScriptSpans_Subscript_ReturnsContentBetweenMarkers()
    {
        var span = Assert.Single(Scripts("H~2~O"));

        Assert.False(span.IsSuperscript);
        Assert.Equal(2, span.ContentStart);
        Assert.Equal(3, span.ContentEnd);
    }

    [Fact]
    public void FindScriptSpans_BothOnOneLine_ReturnsEachInOrder()
    {
        var spans = Scripts("X^2^ and H~2~O");

        Assert.Equal(2, spans.Count);
        Assert.True(spans[0].IsSuperscript);
        Assert.False(spans[1].IsSuperscript);
    }

    [Fact]
    public void FindScriptSpans_MultiCharacterContent_SpansWholeContent()
    {
        var span = Assert.Single(Scripts("E = mc^237^"));

        Assert.Equal("237", "E = mc^237^"[span.ContentStart..span.ContentEnd]);
    }

    // The regression that matters most: '~' opens both strikethrough and subscript, so the
    // two-character delimiter has to keep winning at a position that starts "~~".
    [Theory]
    [InlineData("~~struck~~")]
    [InlineData("one ~~struck~~ two")]
    [InlineData("~~struck~~ and ~~struck again~~")]
    public void FindScriptSpans_Strikethrough_IsNotTreatedAsSubscript(string text)
    {
        Assert.Empty(Scripts(text));
    }

    [Fact]
    public void FindEmphasisSpans_Strikethrough_StillMatchesAsOneRunWithTwoCharMarker()
    {
        var doc = new TextDocument("~~struck~~");
        var span = Assert.Single(MarkdownSyntax.FindEmphasisSpans(doc, doc.GetLineByNumber(1)));

        Assert.Equal(2, span.MarkerLength);
        Assert.Equal(0, span.Start);
        Assert.Equal(10, span.End);
    }

    [Theory]
    [InlineData("no scripts here")]
    [InlineData("")]
    [InlineData("a single ^ caret")]
    [InlineData("a single ~ tilde")]
    [InlineData("trailing unmatched ^super")]
    [InlineData("trailing unmatched ~sub")]
    [InlineData("^^")]
    [InlineData("~~")]
    public void FindScriptSpans_NoValidRun_ReturnsEmpty(string text)
    {
        Assert.Empty(Scripts(text));
    }

    [Fact]
    public void FindScriptSpans_InsideInlineCode_IsLiteral()
    {
        Assert.Empty(Scripts("`H~2~O`"));
    }

    [Fact]
    public void FindScriptSpans_OtherEmphasis_IsNotReported()
    {
        Assert.Empty(Scripts("**bold** _italic_ ==highlight== `code`"));
    }

    [Fact]
    public void FindScriptSpans_NestedInsideEmphasis_IsStillFound()
    {
        var span = Assert.Single(Scripts("**X^2^**"));

        Assert.True(span.IsSuperscript);
        Assert.Equal("2", "**X^2^**"[span.ContentStart..span.ContentEnd]);
    }

    // Documents an accepted false positive rather than asserting desired behavior: a single-character
    // delimiter over arbitrary content cannot tell "3^4 and 5^6" from a genuine superscript run, the
    // same ambiguity '*' has in prose. Pinned here so a future change to the pattern is a visible
    // decision rather than an accident.
    [Theory]
    [InlineData("3^4 and 5^6")]
    [InlineData("roughly ~5 to ~10 minutes")]
    public void FindScriptSpans_AmbiguousProse_MatchesBetweenTheDelimiters(string text)
    {
        Assert.Single(Scripts(text));
    }
}
