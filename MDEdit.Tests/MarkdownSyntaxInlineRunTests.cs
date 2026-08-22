using MDEdit.Editing;

namespace MDEdit.Tests;

// MarkdownSyntax.FormatInline — the string-based resolution of a table cell into styled runs
// (Requirements.md §5). Both halves of table rendering consume it: TableRowElementGenerator measures
// column widths from these runs and builds its Inlines from them, so a bug here misaligns every
// column rather than merely mis-styling text.
public class MarkdownSyntaxInlineRunTests
{
    private static IReadOnlyList<InlineRun> Runs(string text) => MarkdownSyntax.FormatInline(text);

    private static string Rendered(string text)
        => string.Concat(Runs(text).Select(r => r.Text));

    [Fact]
    public void FormatInline_PlainText_IsOneUnstyledRun()
    {
        var run = Assert.Single(Runs("just text"));

        Assert.Equal("just text", run.Text);
        Assert.Equal(InlineStyles.None, run.Styles);
    }

    [Fact]
    public void FormatInline_EmptyString_YieldsNothing()
    {
        Assert.Empty(Runs(""));
    }

    // Table-driven rather than a [Theory]: InlineStyles is internal (reachable here only through
    // InternalsVisibleTo), and an internal type cannot appear in a public test method's signature.
    [Fact]
    public void FormatInline_SingleConstruct_DropsMarkersAndCarriesStyle()
    {
        (string Text, string Expected, InlineStyles Styles)[] cases =
        [
            ("**bold**",     "bold",   InlineStyles.Bold),
            ("*italic*",     "italic", InlineStyles.Italic),
            ("_italic_",     "italic", InlineStyles.Italic),
            ("***both***",   "both",   InlineStyles.Bold | InlineStyles.Italic),
            ("~~gone~~",     "gone",   InlineStyles.Strikethrough),
            ("==note==",     "note",   InlineStyles.Highlight),
            ("`code`",       "code",   InlineStyles.Code),
            ("<u>under</u>", "under",  InlineStyles.Underline),
            ("^up^",         "up",     InlineStyles.Superscript),
            ("~down~",       "down",   InlineStyles.Subscript),
        ];

        foreach (var (text, expected, styles) in cases)
        {
            var run = Assert.Single(Runs(text));
            Assert.Equal(expected, run.Text);
            Assert.Equal(styles, run.Styles);
        }
    }

    [Fact]
    public void FormatInline_MarkersAreRemovedFromTheRenderedText()
    {
        // The property the column measurement depends on: what is measured is what is displayed.
        Assert.Equal("bold and italic", Rendered("**bold** and *italic*"));
    }

    [Fact]
    public void FormatInline_NestedEmphasis_CombinesStylesOverTheOverlap()
    {
        // Nesting composes because the scanner reports nested runs flat and each ORs its style over
        // its own range — nothing reconstructs a tree.
        var run = Assert.Single(Runs("**_x_**"));

        Assert.Equal("x", run.Text);
        Assert.Equal(InlineStyles.Bold | InlineStyles.Italic, run.Styles);
    }

    [Fact]
    public void FormatInline_PartiallyStyledText_SplitsAtStyleBoundaries()
    {
        var runs = Runs("a **b** c");

        Assert.Collection(runs,
            r => { Assert.Equal("a ", r.Text); Assert.Equal(InlineStyles.None, r.Styles); },
            r => { Assert.Equal("b", r.Text); Assert.Equal(InlineStyles.Bold, r.Styles); },
            r => { Assert.Equal(" c", r.Text); Assert.Equal(InlineStyles.None, r.Styles); });
    }

    [Fact]
    public void FormatInline_Link_KeepsLabelAndHidesTarget()
    {
        var run = Assert.Single(Runs("[label](https://example.com)"));

        Assert.Equal("label", run.Text);
        Assert.Equal(InlineStyles.Link, run.Styles);
    }

    [Fact]
    public void FormatInline_Image_RendersAltTextUnstyled()
    {
        // A deliberate boundary: a decoded bitmap inside a fixed-width measured cell is a separate
        // problem, and alt text is a description rather than a link, so it carries no link styling.
        var run = Assert.Single(Runs("![alt text](pic.png)"));

        Assert.Equal("alt text", run.Text);
        Assert.Equal(InlineStyles.None, run.Styles);
    }

    [Fact]
    public void FormatInline_Emoji_IsSubstitutedForItsShortcode()
    {
        Assert.Equal("hi 😂", Rendered("hi :joy:"));
    }

    [Fact]
    public void FormatInline_EmojiInsideBold_KeepsTheSurroundingStyle()
    {
        var run = Assert.Single(Runs("**:joy:**"));

        Assert.Equal("😂", run.Text);
        Assert.Equal(InlineStyles.Bold, run.Styles);
    }

    [Fact]
    public void FormatInline_UncataloguedShortcode_StaysLiteralText()
    {
        Assert.Equal(":notarealemoji:", Rendered(":notarealemoji:"));
    }

    [Fact]
    public void FormatInline_CodeSpanContent_IsNotParsedForEmphasis()
    {
        // CommonMark gives code spans precedence, so the asterisks inside are literal characters —
        // and must still be measured, since they are still displayed.
        var run = Assert.Single(Runs("`**not bold**`"));

        Assert.Equal("**not bold**", run.Text);
        Assert.Equal(InlineStyles.Code, run.Styles);
    }

    [Fact]
    public void FormatInline_EscapedDelimiter_RendersLiterallyIncludingItsBackslash()
    {
        // Pins the second deliberate boundary: escapes keep their backslash visible here because
        // they do everywhere else in the editor. Hiding it is a whole-editor change, not a table
        // one — if that ever lands, this test should change with it rather than be deleted.
        Assert.Equal(@"\*not emphasis\*", Rendered(@"\*not emphasis\*"));
    }

    [Fact]
    public void FormatInline_IntrawordUnderscore_StaysPlain()
    {
        var run = Assert.Single(Runs("snake_case_name"));

        Assert.Equal("snake_case_name", run.Text);
        Assert.Equal(InlineStyles.None, run.Styles);
    }
}
