using ICSharpCode.AvalonEdit.Document;
using MDEdit.Editing;

namespace MDEdit.Tests;

public class CharacterCounterTests
{
    [Theory]
    [InlineData("a\r\nb", 2, 4)]  // CRLF: weight 2 matches the raw text exactly (today's behavior)
    [InlineData("a\r\nb", 1, 3)]  // treats the break as 1 character instead of its actual 2
    [InlineData("a\r\nb", 0, 2)]  // the break contributes nothing
    public void Count_CrlfDocument_AppliesWeightUniformly(string text, int weight, int expected)
        => Assert.Equal(expected, CharacterCounter.Count(new TextDocument(text), weight));

    [Theory]
    [InlineData("a\nb", 2, 4)]    // LF-only: weight 2 counts the single '\n' as if it were 2 chars
    [InlineData("a\nb", 1, 3)]    // matches TextLength, since the raw break is already 1 char
    [InlineData("a\nb", 0, 2)]
    public void Count_LfOnlyDocument_AppliesWeightUniformly(string text, int weight, int expected)
        => Assert.Equal(expected, CharacterCounter.Count(new TextDocument(text), weight));

    [Fact]
    public void Count_MixedLineEndings_CountsEachBreakByWeightNotActualWidth()
    {
        // Two breaks (one CRLF, one LF) — both must count identically under one weight,
        // regardless of their different actual raw widths (2 vs 1).
        var doc = new TextDocument("a\r\nb\nc");
        Assert.Equal(3 + 2 * 2, CharacterCounter.Count(doc, 2)); // "abc" + 2 breaks * 2
        Assert.Equal(3 + 2 * 1, CharacterCounter.Count(doc, 1));
        Assert.Equal(3 + 2 * 0, CharacterCounter.Count(doc, 0));
    }

    [Fact]
    public void Count_NoLineBreaks_WeightHasNoEffect()
    {
        var doc = new TextDocument("hello");
        Assert.Equal(5, CharacterCounter.Count(doc, 0));
        Assert.Equal(5, CharacterCounter.Count(doc, 1));
        Assert.Equal(5, CharacterCounter.Count(doc, 2));
    }

    [Fact]
    public void Count_EmptyDocument_IsZero()
        => Assert.Equal(0, CharacterCounter.Count(new TextDocument(""), 2));

    [Theory]
    [InlineData(-5)]
    [InlineData(99)]
    public void Count_OutOfRangeWeight_IsClamped(int weight)
    {
        // settings.json is hand-editable; an out-of-range value must not corrupt the count —
        // it clamps to the nearest valid weight (0 or 2) rather than producing nonsense.
        var doc = new TextDocument("a\r\nb");
        var clamped = weight < 0 ? 0 : 2;
        Assert.Equal(CharacterCounter.Count(doc, clamped), CharacterCounter.Count(doc, weight));
    }
}
