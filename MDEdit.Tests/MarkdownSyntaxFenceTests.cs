using ICSharpCode.AvalonEdit.Document;
using MDEdit.Editing;

namespace MDEdit.Tests;

public class MarkdownSyntaxFenceTests
{
    [Theory]
    [InlineData("```")]
    [InlineData("```csharp")]
    [InlineData("~~~")]
    [InlineData("~~~~")]
    public void IsFenceDelimiterLine_FenceLine_ReturnsTrue(string text)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        Assert.True(MarkdownSyntax.IsFenceDelimiterLine(doc, line));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("``")]
    [InlineData("~~")]
    [InlineData("``x")]
    [InlineData("   ```")] // indented — Markdown.xshd's ^``` requires the fence at column 0
    public void IsFenceDelimiterLine_NotAFence_ReturnsFalse(string text)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        Assert.False(MarkdownSyntax.IsFenceDelimiterLine(doc, line));
    }

    [Fact]
    public void TryGetEnclosingFenceBlock_TerminatedBlock_FindsRangeForFenceAndContentLines()
    {
        var doc = new TextDocument("```csharp\ncode line\n```\nafter");

        for (int line = 1; line <= 3; line++)
        {
            var result = MarkdownSyntax.TryGetEnclosingFenceBlock(doc, line, out int start, out int end);
            Assert.True(result);
            Assert.Equal(1, start);
            Assert.Equal(3, end);
        }
    }

    [Fact]
    public void TryGetEnclosingFenceBlock_LineOutsideAnyBlock_ReturnsFalse()
    {
        var doc = new TextDocument("```\ncode\n```\nafter");

        var result = MarkdownSyntax.TryGetEnclosingFenceBlock(doc, 4, out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryGetEnclosingFenceBlock_UnterminatedBlock_ExtendsToEndOfDocument()
    {
        var doc = new TextDocument("before\n```\ncode\nmore code");

        var result = MarkdownSyntax.TryGetEnclosingFenceBlock(doc, 4, out int start, out int end);

        Assert.True(result);
        Assert.Equal(2, start);
        Assert.Equal(4, end);
    }

    [Fact]
    public void TryGetEnclosingFenceBlock_MismatchedFenceKindInside_DoesNotClose()
    {
        // A "~~~" line inside a "```"-delimited block doesn't close it (matches Markdown.xshd:
        // the backtick Span's End is "^```", not "^~~~") — it's just content, and the block only
        // closes at the next "```" line.
        var doc = new TextDocument("```\n~~~\ncode\n```");

        var result = MarkdownSyntax.TryGetEnclosingFenceBlock(doc, 2, out int start, out int end);

        Assert.True(result);
        Assert.Equal(1, start);
        Assert.Equal(4, end);
    }

    [Fact]
    public void TryGetEnclosingFenceBlock_TwoSeparateBlocks_PairsByParity()
    {
        var doc = new TextDocument("```\nfirst\n```\ntext between\n```\nsecond\n```");

        var first  = MarkdownSyntax.TryGetEnclosingFenceBlock(doc, 2, out int firstStart, out int firstEnd);
        var between = MarkdownSyntax.TryGetEnclosingFenceBlock(doc, 4, out _, out _);
        var second  = MarkdownSyntax.TryGetEnclosingFenceBlock(doc, 6, out int secondStart, out int secondEnd);

        Assert.True(first);
        Assert.Equal((1, 3), (firstStart, firstEnd));
        Assert.False(between);
        Assert.True(second);
        Assert.Equal((5, 7), (secondStart, secondEnd));
    }

    [Fact]
    public void TryGetEnclosingFenceBlock_TildeFence_WorksLikeBacktickFence()
    {
        var doc = new TextDocument("~~~\ncode\n~~~");

        var result = MarkdownSyntax.TryGetEnclosingFenceBlock(doc, 2, out int start, out int end);

        Assert.True(result);
        Assert.Equal(1, start);
        Assert.Equal(3, end);
    }
}
