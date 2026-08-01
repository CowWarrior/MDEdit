using System.Windows;
using System.Windows.Media;
using MDEdit.Editing;
using MDEdit.Services;

namespace MDEdit.Tests;

// Covers StyleResolver — the single place a persisted ElementStyle becomes WPF values. Per-element
// styling (Requirements.md §6) reaches the editor through two unrelated mechanisms, the syntax
// highlighting definition and MarkdownLineColorizer, and both resolve through here so they can
// never disagree about what a stored value means. The colorizer itself is WPF-bound and untestable
// by the usual rule; this is the part of it that isn't.
//
// Runs on STA via WpfTestApplication: brushes and font families are WPF types.
public class StyleResolverTests
{
    private static ModeStyles ModeWith(ElementStyle style, double baseSize = 14.0) => new()
    {
        BaseFontFamily = "Arial",
        BaseFontSize = baseSize,
        Elements = { [StyledElements.Link] = style },
    };

    private static ResolvedStyle Resolve(ElementStyle style, bool dark = false, double baseSize = 14.0)
    {
        ResolvedStyle resolved = default;
        WpfTestApplication.RunOnSta(() => resolved = StyleResolver.Resolve(StyledElements.Link, ModeWith(style, baseSize), dark));
        return resolved;
    }

    [Fact]
    public void MissingElement_InheritsEverything()
    {
        // An absent key is the normal way to say "this element overrides nothing", so it has to
        // resolve to all-null rather than to some default that would overwrite the run's own values.
        ResolvedStyle resolved = default;
        WpfTestApplication.RunOnSta(() =>
            resolved = StyleResolver.Resolve(StyledElements.Link, new ModeStyles(), dark: false));

        Assert.Equal(default, resolved);
    }

    [Fact]
    public void EmptyStyle_ResolvesToAllNull()
    {
        Assert.Equal(default, Resolve(new ElementStyle()));
    }

    [Fact]
    public void ThemeSelectsTheMatchingColorHalf()
    {
        var style = new ElementStyle
        {
            ForegroundLight = "#112233",
            ForegroundDark = "#445566",
            BackgroundLight = "#778899",
            BackgroundDark = "#AABBCC",
        };

        Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), Resolve(style).Foreground!.Color);
        Assert.Equal(Color.FromRgb(0x77, 0x88, 0x99), Resolve(style).Background!.Color);
        Assert.Equal(Color.FromRgb(0x44, 0x55, 0x66), Resolve(style, dark: true).Foreground!.Color);
        Assert.Equal(Color.FromRgb(0xAA, 0xBB, 0xCC), Resolve(style, dark: true).Background!.Color);
    }

    [Fact]
    public void Brushes_AreFrozen()
    {
        // They are cached and shared across every visual line using the element, so an unfrozen
        // brush would be both wasteful and cross-thread-fragile.
        var resolved = Resolve(new ElementStyle { ForegroundLight = "#112233" });

        Assert.True(resolved.Foreground!.IsFrozen);
    }

    [Fact]
    public void FontScale_MultipliesTheModesBaseSize()
    {
        // A multiplier rather than an absolute size is what lets one base-size change rescale every
        // element proportionally.
        Assert.Equal(22.4, Resolve(new ElementStyle { FontScale = 1.6 }).EmSize!.Value, precision: 10);
        Assert.Equal(32.0, Resolve(new ElementStyle { FontScale = 1.6 }, baseSize: 20).EmSize!.Value, precision: 10);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void NonPositiveOrAbsentScale_Inherits(double? scale)
    {
        // A zero or negative size would throw deep inside WPF's text layout, far from the settings
        // file that caused it.
        Assert.Null(Resolve(new ElementStyle { FontScale = scale }).EmSize);
    }

    [Theory]
    [InlineData("Normal")]
    [InlineData("SemiBold")]
    [InlineData("Bold")]
    public void NamedWeights_Resolve(string name)
    {
        Assert.NotNull(Resolve(new ElementStyle { FontWeight = name }).Weight);
    }

    [Fact]
    public void SemiBold_IsDistinctFromBold()
    {
        // The reason weight is a named value rather than a bool: headings 4-6 are SemiBold, and a
        // Bold checkbox would have had to round them to one or the other.
        Assert.Equal(FontWeights.SemiBold, Resolve(new ElementStyle { FontWeight = "SemiBold" }).Weight);
        Assert.Equal(FontWeights.Bold, Resolve(new ElementStyle { FontWeight = "Bold" }).Weight);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Heavyish")]
    [InlineData("bold")] // case matters — these are stored constants, not free text
    public void UnrecognizedWeight_InheritsInsteadOfThrowing(string? name)
    {
        Assert.Null(Resolve(new ElementStyle { FontWeight = name }).Weight);
    }

    [Fact]
    public void ItalicFalse_IsDistinctFromInherit()
    {
        // "Not italic" has to be expressible: blockquote and comment default to italic, so turning
        // one off must produce an explicit Normal rather than a null that leaves it italic.
        Assert.Equal(FontStyles.Italic, Resolve(new ElementStyle { Italic = true }).Style);
        Assert.Equal(FontStyles.Normal, Resolve(new ElementStyle { Italic = false }).Style);
        Assert.Null(Resolve(new ElementStyle { Italic = null }).Style);
    }

    [Fact]
    public void Decorations_MapToTheirWpfCollections()
    {
        Assert.Equal(TextDecorations.Underline, Resolve(new ElementStyle { Decoration = "Underline" }).Decorations);
        Assert.Equal(TextDecorations.Strikethrough, Resolve(new ElementStyle { Decoration = "Strikethrough" }).Decorations);
    }

    [Fact]
    public void DecorationNone_ClearsRatherThanInherits()
    {
        // Explicit "None" has to produce an empty collection that overwrites an inherited
        // decoration; null must leave it alone. Collapsing the two would make an underline
        // impossible to switch off.
        var none = Resolve(new ElementStyle { Decoration = "None" }).Decorations;
        Assert.NotNull(none);
        // Frozen, so it can be enumerated from any thread — including this one, which is not the
        // STA thread it was created on. Unfrozen, this very assertion threw.
        Assert.True(none.IsFrozen);
        Assert.Empty(none);

        Assert.Null(Resolve(new ElementStyle { Decoration = null }).Decorations);
    }

    [Fact]
    public void FontFamily_ResolvesIncludingAFallbackStack()
    {
        var resolved = Resolve(new ElementStyle { FontFamily = "Cascadia Code, Consolas, Courier New" });

        Assert.Equal("Cascadia Code, Consolas, Courier New", resolved.Family!.Source);
    }

    [Theory]
    [InlineData(1, "heading1")]
    [InlineData(6, "heading6")]
    [InlineData(7, "heading6")]
    [InlineData(99, "heading6")]
    public void HeadingKeys_ClampAboveLevelSix(int level, string expected)
    {
        // MarkdownSyntax.TryGetHeadingLevel caps at 6; anything beyond must land on a real key
        // rather than one no default table defines.
        Assert.Equal(expected, StyledElements.Heading(level));
    }
}
