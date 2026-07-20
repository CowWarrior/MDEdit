using ICSharpCode.AvalonEdit.Document;
using MDEdit.Editing;

namespace MDEdit.Tests;

public class MarkdownFormatterTests
{
    // ── Wrap (bold/italic/strikethrough/inline code) ──────────────────────

    [Fact]
    public void Wrap_WithSelection_WrapsAndSelectsInnerText()
    {
        var doc = new TextDocument("Hello world");
        var sel = new SelectionRange(6, 5); // "world"

        var result = MarkdownFormatter.Wrap(doc, sel, "**", "**");

        Assert.Equal("Hello **world**", doc.Text);
        Assert.Equal(new SelectionRange(8, 5), result);
    }

    [Fact]
    public void Wrap_NoSelection_InsertsPlaceholderAndPositionsCaretBetweenMarkers()
    {
        var doc = new TextDocument("Hello ");
        var sel = new SelectionRange(6, 0);

        var result = MarkdownFormatter.Wrap(doc, sel, "**", "**");

        Assert.Equal("Hello ****", doc.Text);
        Assert.Equal(new SelectionRange(8, 0), result); // caret sits between the two "**"
    }

    // ── Heading ─────────────────────────────────────────────────────────────

    [Fact]
    public void Heading_OnPlainLine_PrependsMarker()
    {
        var doc = new TextDocument("Title");

        MarkdownFormatter.Heading(doc, new SelectionRange(0, 0), 2);

        Assert.Equal("## Title", doc.Text);
    }

    [Fact]
    public void Heading_ReplacesExistingHeadingLevel()
    {
        var doc = new TextDocument("### Title");

        MarkdownFormatter.Heading(doc, new SelectionRange(0, 0), 1);

        Assert.Equal("# Title", doc.Text);
    }

    [Fact]
    public void Heading_CaretMidLine_StillAffectsWholeLine()
    {
        var doc = new TextDocument("## Title");
        var sel = new SelectionRange(5, 0); // caret inside "Title"

        MarkdownFormatter.Heading(doc, sel, 3);

        Assert.Equal("### Title", doc.Text);
    }

    // ── ToggleLinePrefix (lists/blockquote) ──────────────────────────────────

    [Fact]
    public void ToggleLinePrefix_AddsPrefixWhenAbsent()
    {
        var doc = new TextDocument("item");

        MarkdownFormatter.ToggleLinePrefix(doc, new SelectionRange(0, 0), "- ");

        Assert.Equal("- item", doc.Text);
    }

    [Fact]
    public void ToggleLinePrefix_RemovesPrefixWhenPresent()
    {
        var doc = new TextDocument("- item");

        MarkdownFormatter.ToggleLinePrefix(doc, new SelectionRange(0, 0), "- ");

        Assert.Equal("item", doc.Text);
    }

    // ── CodeBlock ───────────────────────────────────────────────────────────

    [Fact]
    public void CodeBlock_WithSelection_FencesSelectedText()
    {
        var doc = new TextDocument("var x = 1;");
        var sel = new SelectionRange(0, 10);

        MarkdownFormatter.CodeBlock(doc, sel);

        Assert.Equal("```\nvar x = 1;\n```", doc.Text);
    }

    [Fact]
    public void CodeBlock_NoSelection_InsertsEmptyFenceAndPositionsCaretInside()
    {
        var doc = new TextDocument("");

        var result = MarkdownFormatter.CodeBlock(doc, new SelectionRange(0, 0));

        Assert.Equal("```\n\n```", doc.Text);
        Assert.Equal(new SelectionRange(4, 0), result);
    }

    // ── Link ────────────────────────────────────────────────────────────────

    [Fact]
    public void Link_WithSelection_WrapsAsLinkTextAndSelectsUrlPlaceholder()
    {
        var doc = new TextDocument("see docs");
        var sel = new SelectionRange(4, 4); // "docs"

        var result = MarkdownFormatter.Link(doc, sel);

        Assert.Equal("see [docs](url)", doc.Text);
        Assert.Equal(new SelectionRange(11, 3), result); // "url" selected
    }

    [Fact]
    public void Link_NoSelection_InsertsPlaceholderLinkAndSelectsLinkText()
    {
        var doc = new TextDocument("");

        var result = MarkdownFormatter.Link(doc, new SelectionRange(0, 0));

        Assert.Equal("[link text](url)", doc.Text);
        Assert.Equal(new SelectionRange(1, 9), result); // "link text" selected
    }
}
