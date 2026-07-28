using ICSharpCode.AvalonEdit.Document;
using MDEdit.Editing;

namespace MDEdit.Tests;

public class MarkdownSyntaxEmojiTests
{
    private static IReadOnlyList<EmojiSpan> Emojis(string text)
    {
        var doc = new TextDocument(text);
        return MarkdownSyntax.FindEmojiSpans(doc, doc.GetLineByNumber(1));
    }

    [Fact]
    public void Catalog_LoadsFromEmbeddedResource()
    {
        // Guards the resource name and the csproj EmbeddedResource entry: a typo in either would
        // silently yield an empty catalogue and make every emoji test below vacuous.
        Assert.True(EmojiCatalog.Count > 100, $"catalogue loaded {EmojiCatalog.Count} entries");
    }

    [Theory]
    [InlineData("joy")]
    [InlineData("rocket")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("100")]
    public void Catalog_KnowsCommonShortcodes(string shortcode)
    {
        Assert.True(EmojiCatalog.TryGet(shortcode, out var emoji));
        Assert.False(string.IsNullOrWhiteSpace(emoji));
    }

    [Fact]
    public void Catalog_IsCaseSensitive()
    {
        Assert.False(EmojiCatalog.TryGet("JOY", out _));
    }

    // Guards the emoji picker's data source: All and TryGet are built from the same Load() pass
    // (see EmojiCatalog.cs), so every entry in one must round-trip through the other.
    [Fact]
    public void Catalog_All_IsNonEmptyAndMatchesTryGet()
    {
        Assert.Equal(EmojiCatalog.Count, EmojiCatalog.All.Count);
        Assert.True(EmojiCatalog.All.Count > 100);

        foreach (var (shortcode, emoji) in EmojiCatalog.All)
        {
            Assert.True(EmojiCatalog.TryGet(shortcode, out var lookedUp));
            Assert.Equal(emoji, lookedUp);
        }
    }

    [Fact]
    public void FindEmojiSpans_WholeLine_ReturnsSpanWithReplacement()
    {
        var span = Assert.Single(Emojis(":joy:"));

        Assert.Equal(0, span.Start);
        Assert.Equal(5, span.End);
        EmojiCatalog.TryGet("joy", out var expected);
        Assert.Equal(expected, span.Emoji);
    }

    [Fact]
    public void FindEmojiSpans_MidSentence_ReportsCorrectOffsets()
    {
        var text = "so funny :joy: really";
        var span = Assert.Single(Emojis(text));

        Assert.Equal(9, span.Start);
        Assert.Equal(":joy:", text[span.Start..span.End]);
    }

    [Fact]
    public void FindEmojiSpans_SeveralOnOneLine_ReturnsEachInOrder()
    {
        var spans = Emojis(":joy: and :rocket: and :fire:");

        Assert.Equal(3, spans.Count);
        Assert.True(spans[0].Start < spans[1].Start && spans[1].Start < spans[2].Start);
    }

    [Fact]
    public void FindEmojiSpans_AdjacentShortcodes_BothFound()
    {
        Assert.Equal(2, Emojis(":joy::rocket:").Count);
    }

    // The catalogue lookup is what makes detection unambiguous — a bare pattern would match ":30:"
    // in a timestamp. This is the guarantee the '^'/'~' constructs can't offer.
    [Theory]
    [InlineData("unknown :notarealshortcode: here")]
    [InlineData("10:30:45")]
    [InlineData("ratio 3:4:5")]
    [InlineData("see the following: three items")]
    [InlineData("")]
    [InlineData("no colons at all")]
    [InlineData(":joy")]
    [InlineData("joy:")]
    [InlineData("::")]
    [InlineData(": joy :")]
    public void FindEmojiSpans_NotARecognizedShortcode_ReturnsEmpty(string text)
    {
        Assert.Empty(Emojis(text));
    }

    [Fact]
    public void FindEmojiSpans_UppercaseShortcode_IsNotMatched()
    {
        Assert.Empty(Emojis(":JOY:"));
    }

    [Fact]
    public void FindEmojiSpans_InsideInlineCode_IsLiteral()
    {
        Assert.Empty(Emojis("`:joy:`"));
    }

    [Fact]
    public void FindEmojiSpans_AfterInlineCode_IsStillFound()
    {
        var span = Assert.Single(Emojis("`code` then :joy:"));

        Assert.Equal("joy", "`code` then :joy:"[(span.Start + 1)..(span.End - 1)]);
    }

    // Emoji is not suppressed by emphasis, unlike inline code — the same container reasoning as
    // underline: ":joy:" inside bold is still an emoji.
    [Theory]
    [InlineData("**:joy:**")]
    [InlineData("_:joy:_")]
    [InlineData("<u>:joy:</u>")]
    public void FindEmojiSpans_InsideEmphasisOrUnderline_IsStillFound(string text)
    {
        Assert.Single(Emojis(text));
    }

    [Fact]
    public void FindEmojiSpans_AfterBulletMarker_IsStillFound()
    {
        Assert.Single(Emojis("- :joy: item"));
    }

    [Fact]
    public void FindEmojiSpans_UnknownThenKnown_FindsTheKnownOne()
    {
        var text = ":notreal: :joy:";
        var span = Assert.Single(Emojis(text));

        Assert.Equal(":joy:", text[span.Start..span.End]);
    }
}
