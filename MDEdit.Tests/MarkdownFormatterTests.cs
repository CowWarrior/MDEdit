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

    [Theory]
    [InlineData(4, "#### text")]
    [InlineData(5, "##### text")]
    [InlineData(6, "###### text")]
    public void Heading_LevelsFourToSix_AreInsertable(int level, string expected)
    {
        // These levels always rendered (TryGetHeadingLevel caps at 6, and each has its own
        // EditorPreferences entry); only the insertion commands stopped at 3, and the Format menu
        // now reaches them. Pins that the formatter needed no change to support that.
        var doc = new TextDocument("text");

        MarkdownFormatter.Heading(doc, new SelectionRange(0, 0), level);

        Assert.Equal(expected, doc.Text);
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

    // ── Table ───────────────────────────────────────────────────────────────
    // IsTableRowLine requires each row to start at column 0, so Table normalizes onto its own
    // lines depending on what's already on the cursor's line — these cases cover all four
    // combinations of leading/trailing text.

    private const string StarterTable = "| Header 1 | Header 2 |\n| --- | --- |\n| Cell 1 | Cell 2 |\n| Cell 3 | Cell 4 |\n| Cell 5 | Cell 6 |";

    [Fact]
    public void Table_CaretAloneOnEmptyLine_InsertsWithoutExtraBreaks()
    {
        var doc = new TextDocument("");

        var result = MarkdownFormatter.Table(doc, new SelectionRange(0, 0));

        Assert.Equal(StarterTable, doc.Text);
        Assert.Equal(new SelectionRange(2, 8), result);
        Assert.Equal("Header 1", doc.GetText(result!.Value.Start, result.Value.Length));
    }

    [Fact]
    public void Table_CaretAfterExistingTextOnSameLine_InsertsLeadingBreakOnly()
    {
        var doc = new TextDocument("Notes:"); // caret at the end — nothing follows

        var result = MarkdownFormatter.Table(doc, new SelectionRange(6, 0));

        Assert.Equal("Notes:\n" + StarterTable, doc.Text);
        Assert.Equal(new SelectionRange(9, 8), result);
        Assert.Equal("Header 1", doc.GetText(result!.Value.Start, result.Value.Length));
    }

    [Fact]
    public void Table_CaretBeforeExistingTextOnSameLine_InsertsTrailingBreakOnly()
    {
        var doc = new TextDocument("after"); // caret at the very start — nothing precedes

        var result = MarkdownFormatter.Table(doc, new SelectionRange(0, 0));

        Assert.Equal(StarterTable + "\nafter", doc.Text);
        Assert.Equal(new SelectionRange(2, 8), result);
    }

    [Fact]
    public void Table_CaretMidLineWithTextOnBothSides_InsertsBothBreaks()
    {
        var doc = new TextDocument("abcdef");

        var result = MarkdownFormatter.Table(doc, new SelectionRange(3, 0)); // between "abc" and "def"

        Assert.Equal("abc\n" + StarterTable + "\ndef", doc.Text);
        Assert.Equal(new SelectionRange(6, 8), result);
    }

    [Fact]
    public void Table_SelectionCoveringWholeLine_ReplacesWithoutExtraBreaks()
    {
        var doc = new TextDocument("XXXXX");

        var result = MarkdownFormatter.Table(doc, new SelectionRange(0, 5));

        Assert.Equal(StarterTable, doc.Text);
        Assert.Equal(new SelectionRange(2, 8), result);
    }

    [Fact]
    public void Table_PartialSelectionWithTextOnBothSides_ReplacesSelectionAndNormalizes()
    {
        var doc = new TextDocument("before SELECTED after");
        var sel = new SelectionRange(7, 8); // "SELECTED"

        var result = MarkdownFormatter.Table(doc, sel);

        Assert.Equal("before \n" + StarterTable + "\n after", doc.Text);
        Assert.DoesNotContain("SELECTED", doc.Text);
        Assert.Equal(new SelectionRange(10, 8), result);
    }

    [Fact]
    public void Table_InsertedText_IsRecognizedAsValidTableBlock()
    {
        var doc = new TextDocument("");
        MarkdownFormatter.Table(doc, new SelectionRange(0, 0));

        Assert.True(MarkdownSyntax.TryGetTableBlock(doc, 1, out int start, out int end));
        Assert.Equal(1, start);
        Assert.Equal(5, end);
    }

    // ── InsertEmoji ─────────────────────────────────────────────────────────

    [Fact]
    public void InsertEmoji_NoSelection_InsertsShortcodeAndPositionsCaretAfter()
    {
        var doc = new TextDocument("Hello ");
        var sel = new SelectionRange(6, 0);

        var result = MarkdownFormatter.InsertEmoji(doc, sel, "joy");

        Assert.Equal("Hello :joy:", doc.Text);
        Assert.Equal(new SelectionRange(11, 0), result);
    }

    [Fact]
    public void InsertEmoji_WithSelection_ReplacesSelectionOutright()
    {
        var doc = new TextDocument("before SELECTED after");
        var sel = new SelectionRange(7, 8); // "SELECTED"

        var result = MarkdownFormatter.InsertEmoji(doc, sel, "rocket");

        Assert.Equal("before :rocket: after", doc.Text);
        Assert.Equal(new SelectionRange(15, 0), result);
    }

    // ── Convert Emoji to Shortcodes ──────────────────────────────────────────

    [Fact]
    public void ConvertEmojiToShortcodes_NoSelection_ConvertsTheWholeDocument()
    {
        var doc = new TextDocument("a 👍 b\nc 👎 d");

        var result = MarkdownFormatter.ConvertEmojiToShortcodes(doc, new SelectionRange(0, 0));

        Assert.Equal("a :thumbsup: b\nc :thumbsdown: d", doc.Text);
        // Whole-document runs leave caret placement to the document's own anchor movement.
        Assert.Null(result);
    }

    [Fact]
    public void ConvertEmojiToShortcodes_WithSelection_ConvertsOnlyInsideIt()
    {
        var doc = new TextDocument("👍 kept 👍 changed");
        // Just the second emoji onward.
        var sel = new SelectionRange(7, doc.TextLength - 7);

        var result = MarkdownFormatter.ConvertEmojiToShortcodes(doc, sel);

        Assert.Equal("👍 kept :thumbsup: changed", doc.Text);
        // Nothing before the selection moved, so only its length grows.
        Assert.Equal(new SelectionRange(7, sel.Length + ":thumbsup:".Length - "👍".Length), result);
    }

    [Fact]
    public void ConvertEmojiToShortcodes_MultipleOnOneLine_AllConvert()
    {
        // Applied last-to-first, so each edit only shifts offsets already consumed. A forward pass
        // would misplace every replacement after the first, since a shortcode is longer than the
        // character it replaces.
        var doc = new TextDocument("👍👎👍");

        MarkdownFormatter.ConvertEmojiToShortcodes(doc, new SelectionRange(0, 0));

        Assert.Equal(":thumbsup::thumbsdown::thumbsup:", doc.Text);
    }

    [Fact]
    public void ConvertEmojiToShortcodes_IsOneUndoStep()
    {
        // A whole-document run can be hundreds of replacements; without RunUpdate grouping them,
        // undoing it would take hundreds of Ctrl+Z presses.
        var doc = new TextDocument("👍 a 👎 b 👍");
        var original = doc.Text;

        MarkdownFormatter.ConvertEmojiToShortcodes(doc, new SelectionRange(0, 0));
        Assert.NotEqual(original, doc.Text);

        doc.UndoStack.Undo();
        Assert.Equal(original, doc.Text);
    }

    [Fact]
    public void ConvertEmojiToShortcodes_SkipsInlineCodeAndFencedBlocks()
    {
        // Symmetry with rendering: MDEdit will not turn a shortcode inside code back into an emoji,
        // so converting one there would silently edit the contents of a code sample.
        var doc = new TextDocument("`👍` stays\n```\n👍 stays\n```\n👍 converts");

        MarkdownFormatter.ConvertEmojiToShortcodes(doc, new SelectionRange(0, 0));

        Assert.Equal("`👍` stays\n```\n👍 stays\n```\n:thumbsup: converts", doc.Text);
    }

    [Fact]
    public void ConvertEmojiToShortcodes_UncataloguedCharactersAreLeftAlone()
    {
        var doc = new TextDocument("‱ ordinary text");

        var result = MarkdownFormatter.ConvertEmojiToShortcodes(doc, new SelectionRange(0, 0));

        Assert.Equal("‱ ordinary text", doc.Text);
        Assert.Null(result);
    }

    [Fact]
    public void ConvertEmojiToShortcodes_ExistingShortcodesAreNotDoubled()
    {
        // The command is idempotent: a second run has nothing left to find, so a user who runs it
        // twice does not end up with ":​:thumbsup::".
        var doc = new TextDocument("👍 and :rocket:");

        MarkdownFormatter.ConvertEmojiToShortcodes(doc, new SelectionRange(0, 0));
        var once = doc.Text;
        MarkdownFormatter.ConvertEmojiToShortcodes(doc, new SelectionRange(0, 0));

        Assert.Equal(":thumbsup: and :rocket:", once);
        Assert.Equal(once, doc.Text);
    }
}
