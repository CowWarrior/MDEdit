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

    // ── Reverse lookup (Convert Emoji to Shortcodes) ─────────────────────────

    [Fact]
    public void TryGetShortcode_PrefersTheNameListedFirstInTheFile()
    {
        // The alias collision the reverse map exists to resolve: 👍 is both "thumbsup" and "+1",
        // 👎 both "thumbsdown" and "-1". Emoji.txt is hand-ordered with the preferred name first,
        // so first-listed-wins needs no second list to express the preference — but nothing else
        // would catch it silently flipping to the alias.
        Assert.True(EmojiCatalog.TryGetShortcode("👍", out var up));
        Assert.Equal("thumbsup", up);

        Assert.True(EmojiCatalog.TryGetShortcode("👎", out var down));
        Assert.Equal("thumbsdown", down);
    }

    [Fact]
    public void TryGetShortcode_RoundTripsWithTheForwardLookup()
    {
        // Every shortcode the reverse map yields must map back to the same character, or the
        // command would rewrite a document into something that renders differently.
        foreach (var (_, emoji) in EmojiCatalog.All)
        {
            Assert.True(EmojiCatalog.TryGetShortcode(emoji, out var shortcode));
            Assert.True(EmojiCatalog.TryGet(shortcode, out var roundTripped));
            Assert.Equal(emoji, roundTripped);
        }
    }

    [Fact]
    public void TryGetShortcode_UnknownCharacterIsNotConverted()
    {
        Assert.False(EmojiCatalog.TryGetShortcode("x", out _));
    }

    [Fact]
    public void CouldStartEmoji_IsAProperFilterRatherThanAlwaysTrue()
    {
        // The O(1) rejection the scan leans on. A version returning true for everything would still
        // pass every other test here while making the scan build a substring at every character.
        Assert.False(EmojiCatalog.CouldStartEmoji('a'));
        Assert.False(EmojiCatalog.CouldStartEmoji(' '));
        // Indexed off the string, not a char literal: 👍 is a surrogate pair and does not fit in one.
        Assert.True(EmojiCatalog.CouldStartEmoji("👍"[0]));
    }

    [Fact]
    public void FindEmojiCharacterSpans_FindsLiteralEmojiWithItsShortcode()
    {
        var doc = new TextDocument("hi 👍 there");

        var span = Assert.Single(MarkdownSyntax.FindEmojiCharacterSpans(doc, doc.GetLineByNumber(1)));

        Assert.Equal("thumbsup", span.Shortcode);
        Assert.Equal("👍", doc.GetText(span.Start, span.End - span.Start));
    }

    [Fact]
    public void FindEmojiCharacterSpans_SkipsInlineCode()
    {
        // Mirrors FindEmojiSpans skipping code the other way round. MDEdit will not render a
        // shortcode inside backticks back into an emoji, so it must not convert one to a shortcode
        // there either.
        var doc = new TextDocument("`👍` 👎");

        var span = Assert.Single(MarkdownSyntax.FindEmojiCharacterSpans(doc, doc.GetLineByNumber(1)));

        Assert.Equal("thumbsdown", span.Shortcode);
    }

    [Fact]
    public void FindEmojiCharacterSpans_UncataloguedCharacterIsLeftAlone()
    {
        var doc = new TextDocument("plain text ‱ here");

        Assert.Empty(MarkdownSyntax.FindEmojiCharacterSpans(doc, doc.GetLineByNumber(1)));
    }

    [Fact]
    public void FindEmojiSpans_EscapedColon_IsNotAnEmoji()
    {
        Assert.Empty(Emojis(@"\:joy:"));
    }

    [Fact]
    public void FindEmojiSpans_BackslashInsideCodeSpan_DoesNotDisturbTheCodeSkip()
    {
        // The escape check runs AFTER the code-span skip, since a backslash inside backticks is
        // literal. Reversing the two would let an escape consume a character inside the code span
        // and desynchronise the scan position from the span's real end.
        var text = @"`\:joy:` :joy:";
        var span = Assert.Single(Emojis(text));

        Assert.Equal(":joy:", text[span.Start..span.End]);
        Assert.Equal(9, span.Start);
    }
}
