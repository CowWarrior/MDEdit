using System.Windows;
using MDEdit.Editing;
using MDEdit.Services;

namespace MDEdit.Tests;

// Covers MarkdownHighlighting.Build — the half of per-element styling (Requirements.md §6) that
// reaches the editor through the syntax-highlighting definition rather than through
// MarkdownLineColorizer. Everything here compiles the real embedded grammar through AvalonEdit's
// real loader, so these assertions are about what the editor would actually render, not about an
// intermediate model.
public class MarkdownHighlightingTests
{
    private static ICSharpCode.AvalonEdit.Highlighting.HighlightingColor Color(
        ModeStyles mode, bool dark, string xshdName)
    {
        var color = MarkdownHighlighting.Build(mode, dark).GetNamedColor(xshdName);
        Assert.NotNull(color);
        return color;
    }

    [Fact]
    public void AllFourCombinationsBuild()
    {
        // Four definitions exist at a time — light/dark × source/WYSIWYG. A grammar or style change
        // that only breaks one of them would otherwise surface as "the editor looks wrong after I
        // toggle modes", far from the cause.
        var prefs = new EditorPreferences();

        foreach (var mode in new[] { prefs.Source, prefs.Wysiwyg })
            foreach (bool dark in new[] { false, true })
                Assert.NotNull(MarkdownHighlighting.Build(mode, dark).MainRuleSet);
    }

    // ── The inherit contract ─────────────────────────────────────────────────

    [Fact]
    public void ClearedProperty_InheritsRatherThanFallingBackToTheGrammar()
    {
        // The single most important behaviour here. Build clears every styleable property before
        // applying the element's style, so an override the user cleared genuinely inherits. Were
        // the grammar's own values left as a fallback, clearing Bold's weight would silently
        // resurface whatever Markdown.xshd baked in — the UI would say "inherit" and the editor
        // would disagree.
        var mode = ModeStyles.WysiwygDefaults();
        mode.Elements[StyledElements.Bold].FontWeight = null;

        Assert.Null(Color(mode, dark: false, "Bold").FontWeight);
    }

    [Fact]
    public void MissingElementEntry_LeavesTheColorCompletelyUnstyled()
    {
        // An element absent from the dictionary means "inherit everything", which has to hold even
        // though the grammar still declares the colour by name for its rules to reference.
        var mode = ModeStyles.WysiwygDefaults();
        mode.Elements.Remove(StyledElements.Link);

        var color = Color(mode, dark: false, "Link");

        Assert.Null(color.Foreground);
        Assert.Null(color.Background);
        Assert.Null(color.FontFamily);
        Assert.Null(color.FontSize);
        Assert.Null(color.FontWeight);
        Assert.Null(color.FontStyle);
        Assert.Null(color.Underline);
        Assert.Null(color.Strikethrough);
    }

    // ── Defaults reach the compiled definition ───────────────────────────────

    [Fact]
    public void Defaults_CarryWeightStyleAndDecoration()
    {
        var mode = ModeStyles.SourceDefaults();

        Assert.Equal(FontWeights.Bold, Color(mode, dark: false, "Bold").FontWeight);
        Assert.Equal(FontStyles.Italic, Color(mode, dark: false, "Italic").FontStyle);

        var boldItalic = Color(mode, dark: false, "BoldItalic");
        Assert.Equal(FontWeights.Bold, boldItalic.FontWeight);
        Assert.Equal(FontStyles.Italic, boldItalic.FontStyle);

        Assert.True(Color(mode, dark: false, "Underline").Underline);
    }

    [Fact]
    public void StrikethroughDefault_IsGreyTextAndNotActuallyStruck()
    {
        // How strikethrough has always rendered here. Turning on a real strike is newly available,
        // but must not become the default — that would change every existing document's appearance.
        var color = Color(ModeStyles.SourceDefaults(), dark: false, "Strike");

        Assert.Equal("#FF888888", color.Foreground!.ToString());
        Assert.NotEqual(true, color.Strikethrough);
    }

    [Fact]
    public void HighlightDefault_SetsOnlyABackground()
    {
        // Highlighted text keeps the editor's standard foreground; pinning one would fight the
        // user's own text colour.
        var color = Color(ModeStyles.SourceDefaults(), dark: false, "Highlight");

        Assert.Null(color.Foreground);
        Assert.NotNull(color.Background);
    }

    [Fact]
    public void ThemeSelectsTheMatchingColorHalf()
    {
        var mode = ModeStyles.SourceDefaults();

        Assert.Equal("#FF0065BD", Color(mode, dark: false, "Link").Foreground!.ToString());
        Assert.Equal("#FF58A6FF", Color(mode, dark: true, "Link").Foreground!.ToString());
    }

    // ── The mode split ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("InlineCode")]
    [InlineData("CodeBlock")]
    public void WysiwygPinsCodeToAFixedWidthFamily(string xshdName)
    {
        // This is what keeps code fixed-width once WYSIWYG swaps the base font to a proportional
        // one. The loss would only ever be visible by eye, and only in WYSIWYG.
        var family = Color(ModeStyles.WysiwygDefaults(), dark: false, xshdName).FontFamily;

        Assert.NotNull(family);
        Assert.Equal("Cascadia Code, Consolas, Courier New", family.Source);
    }

    [Theory]
    [InlineData("InlineCode")]
    [InlineData("CodeBlock")]
    public void SourcePinsNoFamilyBecauseTheBaseIsAlreadyMono(string xshdName)
    {
        Assert.Null(Color(ModeStyles.SourceDefaults(), dark: false, xshdName).FontFamily);
    }

    [Fact]
    public void NeitherModeScalesAnXshdElementByDefault()
    {
        // Only headings scale, and headings are colorizer-driven. A size appearing on an inline
        // element would mean a default had drifted.
        foreach (var mode in new[] { ModeStyles.SourceDefaults(), ModeStyles.WysiwygDefaults() })
            foreach (var element in StyledElements.All.Where(e => e.XshdColorName is not null))
                Assert.Null(Color(mode, dark: false, element.XshdColorName!).FontSize);
    }

    // ── Applied overrides ────────────────────────────────────────────────────

    [Fact]
    public void FontScale_ResolvesToAWholePointSize()
    {
        // XshdColor.FontSize is int?, so these land on whole points. 14 × 1.5 = 21 exactly; the
        // fractional case is the one worth pinning, since it silently rounds.
        var mode = ModeStyles.WysiwygDefaults();
        mode.BaseFontSize = 14;
        mode.Elements[StyledElements.InlineCode].FontScale = 1.5;
        Assert.Equal(21, Color(mode, dark: false, "InlineCode").FontSize);

        mode.Elements[StyledElements.InlineCode].FontScale = 1.15; // 16.1
        Assert.Equal(16, Color(mode, dark: false, "InlineCode").FontSize);
    }

    [Fact]
    public void Decoration_IsMutuallyExclusiveByConstruction()
    {
        // AvalonEdit's ApplyColorToElement calls SetTextDecorations twice, the second replacing the
        // first, so underline and strikethrough can never combine. One stored value answering both
        // questions is what makes that unrepresentable rather than merely discouraged.
        var mode = ModeStyles.SourceDefaults();

        mode.Elements[StyledElements.Link].Decoration = ElementStyle.DecorationStrikethrough;
        var struck = Color(mode, dark: false, "Link");
        Assert.True(struck.Strikethrough);
        Assert.False(struck.Underline);

        mode.Elements[StyledElements.Link].Decoration = ElementStyle.DecorationNone;
        var plain = Color(mode, dark: false, "Link");
        Assert.False(plain.Strikethrough);
        Assert.False(plain.Underline);
    }

    [Fact]
    public void ItalicFalse_ClearsItalicRatherThanInheriting()
    {
        // "Not italic" and "inherit" are different answers, and Comment defaults to italic — so
        // turning it off has to produce an explicit Normal, not a null that leaves it italic.
        var mode = ModeStyles.SourceDefaults();
        mode.Elements[StyledElements.Comment].Italic = false;

        Assert.Equal(FontStyles.Normal, Color(mode, dark: false, "Comment").FontStyle);
    }

    [Fact]
    public void UnrecognizedWeight_InheritsInsteadOfThrowing()
    {
        // settings.json is hand-editable; a typo should cost that one override, not the ability to
        // open a document.
        var mode = ModeStyles.SourceDefaults();
        mode.Elements[StyledElements.Bold].FontWeight = "Heavyish";

        Assert.Null(Color(mode, dark: false, "Bold").FontWeight);
    }
}
