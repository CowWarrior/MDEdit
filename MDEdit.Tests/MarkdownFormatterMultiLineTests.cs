using ICSharpCode.AvalonEdit.Document;
using MDEdit.Editing;

namespace MDEdit.Tests;

/// <summary>
/// The line-based commands used to affect only the line containing the start of the selection
/// (Requirements.md §3 "Known issues"). These cover the multi-line behaviour, and pin the
/// single-line behaviour that must not change.
/// </summary>
public class MarkdownFormatterMultiLineTests
{
    // Selects from the start of the document to the end of the given 1-based line.
    private static SelectionRange ThroughLine(TextDocument doc, int lineNumber)
    {
        var line = doc.GetLineByNumber(lineNumber);
        return new SelectionRange(0, line.EndOffset);
    }

    // ── Bullet / numbered / blockquote prefixes ───────────────────────────

    [Fact]
    public void ToggleLinePrefix_MultiLineSelection_PrefixesEveryLine()
    {
        var doc = new TextDocument("one\ntwo\nthree");

        MarkdownFormatter.ToggleLinePrefix(doc, ThroughLine(doc, 3), "- ");

        Assert.Equal("- one\n- two\n- three", doc.Text);
    }

    [Fact]
    public void ToggleLinePrefix_AllLinesAlreadyPrefixed_RemovesFromEvery()
    {
        var doc = new TextDocument("- one\n- two\n- three");

        MarkdownFormatter.ToggleLinePrefix(doc, ThroughLine(doc, 3), "- ");

        Assert.Equal("one\ntwo\nthree", doc.Text);
    }

    // Normalize rather than flip: a mixed block becomes uniformly prefixed, so a second press
    // clears it. Flipping each line independently would just invert the mixture.
    [Fact]
    public void ToggleLinePrefix_MixedSelection_AddsToTheMissingLinesOnly()
    {
        var doc = new TextDocument("- one\ntwo\n- three");

        MarkdownFormatter.ToggleLinePrefix(doc, ThroughLine(doc, 3), "- ");

        Assert.Equal("- one\n- two\n- three", doc.Text);
    }

    [Fact]
    public void ToggleLinePrefix_PressedTwiceOnMixedSelection_EndsUpCleared()
    {
        var doc = new TextDocument("- one\ntwo\n- three");

        MarkdownFormatter.ToggleLinePrefix(doc, ThroughLine(doc, 3), "- ");
        MarkdownFormatter.ToggleLinePrefix(doc, ThroughLine(doc, 3), "- ");

        Assert.Equal("one\ntwo\nthree", doc.Text);
    }

    [Fact]
    public void ToggleLinePrefix_BlankLinesInsideSelection_AreSkipped()
    {
        var doc = new TextDocument("one\n\ntwo");

        MarkdownFormatter.ToggleLinePrefix(doc, ThroughLine(doc, 3), "- ");

        Assert.Equal("- one\n\n- two", doc.Text);
    }

    [Fact]
    public void ToggleLinePrefix_Blockquote_UsesTheSamePathAsBullets()
    {
        var doc = new TextDocument("one\ntwo");

        MarkdownFormatter.ToggleLinePrefix(doc, ThroughLine(doc, 2), "> ");

        Assert.Equal("> one\n> two", doc.Text);
    }

    // ── Numbered lists ────────────────────────────────────────────────────
    // Numbered lists can't go through ToggleLinePrefix: a fixed prefix would write "1." on every
    // line. This is the regression that shipped in 1.0.3's first cut.

    [Fact]
    public void NumberedList_MultiLineSelection_NumbersSequentially()
    {
        var doc = new TextDocument("one\ntwo\nthree");

        MarkdownFormatter.NumberedList(doc, ThroughLine(doc, 3));

        Assert.Equal("1. one\n2. two\n3. three", doc.Text);
    }

    [Fact]
    public void NumberedList_TenOrMoreLines_KeepsCountingPastSingleDigits()
    {
        var doc = new TextDocument(string.Join("\n", Enumerable.Range(1, 11).Select(i => $"item{i}")));

        MarkdownFormatter.NumberedList(doc, ThroughLine(doc, 11));

        Assert.Contains("10. item10", doc.Text);
        Assert.Contains("11. item11", doc.Text);
    }

    [Fact]
    public void NumberedList_AllLinesAlreadyNumbered_RemovesEveryMarker()
    {
        var doc = new TextDocument("1. one\n2. two\n3. three");

        MarkdownFormatter.NumberedList(doc, ThroughLine(doc, 3));

        Assert.Equal("one\ntwo\nthree", doc.Text);
    }

    [Fact]
    public void NumberedList_MixedSelection_RenumbersInPlaceWithoutDoublingMarkers()
    {
        var doc = new TextDocument("1. one\ntwo\n1. three");

        MarkdownFormatter.NumberedList(doc, ThroughLine(doc, 3));

        Assert.Equal("1. one\n2. two\n3. three", doc.Text);
    }

    [Fact]
    public void NumberedList_PressedTwice_EndsUpCleared()
    {
        var doc = new TextDocument("one\ntwo\nthree");

        MarkdownFormatter.NumberedList(doc, ThroughLine(doc, 3));
        MarkdownFormatter.NumberedList(doc, ThroughLine(doc, 3));

        Assert.Equal("one\ntwo\nthree", doc.Text);
    }

    [Fact]
    public void NumberedList_BlankLinesInsideSelection_AreSkippedAndNumberingContinues()
    {
        var doc = new TextDocument("one\n\ntwo");

        MarkdownFormatter.NumberedList(doc, ThroughLine(doc, 3));

        Assert.Equal("1. one\n\n2. two", doc.Text);
    }

    [Fact]
    public void NumberedList_SingleLine_InsertsOne()
    {
        var doc = new TextDocument("one");

        MarkdownFormatter.NumberedList(doc, new SelectionRange(0, 0));

        Assert.Equal("1. one", doc.Text);
    }

    [Fact]
    public void NumberedList_SingleLineAlreadyNumbered_RemovesTheMarker()
    {
        var doc = new TextDocument("1. one");

        MarkdownFormatter.NumberedList(doc, new SelectionRange(0, 0));

        Assert.Equal("one", doc.Text);
    }

    [Fact]
    public void NumberedList_CaretOnBlankLine_StillInsertsMarker()
    {
        var doc = new TextDocument("");

        MarkdownFormatter.NumberedList(doc, new SelectionRange(0, 0));

        Assert.Equal("1. ", doc.Text);
    }

    [Fact]
    public void NumberedList_MultiLineEdit_IsASingleUndoStep()
    {
        var doc = new TextDocument("one\ntwo\nthree");

        MarkdownFormatter.NumberedList(doc, ThroughLine(doc, 3));
        doc.UndoStack.Undo();

        Assert.Equal("one\ntwo\nthree", doc.Text);
    }

    // ── Selection boundary ────────────────────────────────────────────────

    // Dragging from the start of line 1 to the start of line 3 highlights two lines, not three.
    [Fact]
    public void ToggleLinePrefix_SelectionEndingAtStartOfALine_ExcludesThatLine()
    {
        var doc = new TextDocument("one\ntwo\nthree");
        var thirdLineStart = doc.GetLineByNumber(3).Offset;

        MarkdownFormatter.ToggleLinePrefix(doc, new SelectionRange(0, thirdLineStart), "- ");

        Assert.Equal("- one\n- two\nthree", doc.Text);
    }

    [Fact]
    public void ToggleLinePrefix_SelectionWithinOneLine_AffectsOnlyThatLine()
    {
        var doc = new TextDocument("one\ntwo\nthree");
        var second = doc.GetLineByNumber(2);

        MarkdownFormatter.ToggleLinePrefix(doc, new SelectionRange(second.Offset, 1), "- ");

        Assert.Equal("one\n- two\nthree", doc.Text);
    }

    // ── Single-line behaviour must not regress ────────────────────────────

    [Fact]
    public void ToggleLinePrefix_CaretOnBlankLine_StillInsertsPrefix()
    {
        // Adding a marker to an empty line is how a list gets started — blank-line skipping
        // applies only inside a multi-line selection.
        var doc = new TextDocument("");

        MarkdownFormatter.ToggleLinePrefix(doc, new SelectionRange(0, 0), "- ");

        Assert.Equal("- ", doc.Text);
    }

    [Fact]
    public void ToggleLinePrefix_SingleLine_ReturnsNullSoCaretIsLeftAlone()
    {
        var doc = new TextDocument("one");

        Assert.Null(MarkdownFormatter.ToggleLinePrefix(doc, new SelectionRange(0, 0), "- "));
    }

    // ── Returned selection ────────────────────────────────────────────────

    [Fact]
    public void ToggleLinePrefix_MultiLine_ReturnsSelectionCoveringEveryAffectedLine()
    {
        var doc = new TextDocument("one\ntwo");

        var result = MarkdownFormatter.ToggleLinePrefix(doc, ThroughLine(doc, 2), "- ");

        Assert.NotNull(result);
        Assert.Equal(0, result!.Value.Start);
        Assert.Equal(doc.TextLength, result.Value.Length);   // "- one\n- two"
    }

    // ── Headings ──────────────────────────────────────────────────────────

    [Fact]
    public void Heading_MultiLineSelection_AppliesToEveryLine()
    {
        var doc = new TextDocument("one\ntwo\nthree");

        MarkdownFormatter.Heading(doc, ThroughLine(doc, 3), 2);

        Assert.Equal("## one\n## two\n## three", doc.Text);
    }

    [Fact]
    public void Heading_MultiLineSelection_ReplacesAnyExistingHeadingMarkers()
    {
        var doc = new TextDocument("# one\n### two\nthree");

        MarkdownFormatter.Heading(doc, ThroughLine(doc, 3), 1);

        Assert.Equal("# one\n# two\n# three", doc.Text);
    }

    [Fact]
    public void Heading_BlankLinesInsideSelection_AreSkipped()
    {
        var doc = new TextDocument("one\n\ntwo");

        MarkdownFormatter.Heading(doc, ThroughLine(doc, 3), 1);

        Assert.Equal("# one\n\n# two", doc.Text);
    }

    // ── Task lists ────────────────────────────────────────────────────────

    [Fact]
    public void TaskListItem_MultiLinePlainText_MakesEveryLineATask()
    {
        var doc = new TextDocument("one\ntwo\nthree");

        MarkdownFormatter.TaskListItem(doc, ThroughLine(doc, 3));

        Assert.Equal("- [ ] one\n- [ ] two\n- [ ] three", doc.Text);
    }

    [Fact]
    public void TaskListItem_MultiLineBullets_AddsBoxesWithoutDoublingTheMarker()
    {
        var doc = new TextDocument("- one\n- two");

        MarkdownFormatter.TaskListItem(doc, ThroughLine(doc, 2));

        Assert.Equal("- [ ] one\n- [ ] two", doc.Text);
    }

    [Fact]
    public void TaskListItem_AllUnchecked_ChecksEvery()
    {
        var doc = new TextDocument("- [ ] one\n- [ ] two");

        MarkdownFormatter.TaskListItem(doc, ThroughLine(doc, 2));

        Assert.Equal("- [x] one\n- [x] two", doc.Text);
    }

    [Fact]
    public void TaskListItem_AllChecked_UnchecksEvery()
    {
        var doc = new TextDocument("- [x] one\n- [x] two");

        MarkdownFormatter.TaskListItem(doc, ThroughLine(doc, 2));

        Assert.Equal("- [ ] one\n- [ ] two", doc.Text);
    }

    // Mixed checked/unchecked normalizes to checked rather than flipping each line, so the block
    // doesn't stay mixed forever.
    [Fact]
    public void TaskListItem_MixedStates_ChecksEvery()
    {
        var doc = new TextDocument("- [x] one\n- [ ] two");

        MarkdownFormatter.TaskListItem(doc, ThroughLine(doc, 2));

        Assert.Equal("- [x] one\n- [x] two", doc.Text);
    }

    [Fact]
    public void TaskListItem_SomeLinesNotTasksYet_ConvertsThoseAndLeavesExistingStatesAlone()
    {
        var doc = new TextDocument("- [x] one\nplain");

        MarkdownFormatter.TaskListItem(doc, ThroughLine(doc, 2));

        Assert.Equal("- [x] one\n- [ ] plain", doc.Text);
    }

    [Fact]
    public void TaskListItem_ThreePresses_CycleThroughTaskThenCheckedThenUnchecked()
    {
        var doc = new TextDocument("one\ntwo");

        MarkdownFormatter.TaskListItem(doc, ThroughLine(doc, 2));
        Assert.Equal("- [ ] one\n- [ ] two", doc.Text);

        MarkdownFormatter.TaskListItem(doc, ThroughLine(doc, 2));
        Assert.Equal("- [x] one\n- [x] two", doc.Text);

        MarkdownFormatter.TaskListItem(doc, ThroughLine(doc, 2));
        Assert.Equal("- [ ] one\n- [ ] two", doc.Text);
    }

    [Fact]
    public void TaskListItem_BlankLinesInsideSelection_AreSkipped()
    {
        var doc = new TextDocument("one\n\ntwo");

        MarkdownFormatter.TaskListItem(doc, ThroughLine(doc, 3));

        Assert.Equal("- [ ] one\n\n- [ ] two", doc.Text);
    }

    [Fact]
    public void TaskListItem_IndentedLines_KeepTheirIndent()
    {
        var doc = new TextDocument("  - one\n  - two");

        MarkdownFormatter.TaskListItem(doc, ThroughLine(doc, 2));

        Assert.Equal("  - [ ] one\n  - [ ] two", doc.Text);
    }

    // ── Undo grouping ─────────────────────────────────────────────────────

    [Fact]
    public void MultiLineEdit_IsASingleUndoStep()
    {
        var doc = new TextDocument("one\ntwo\nthree");
        var original = doc.Text;

        MarkdownFormatter.ToggleLinePrefix(doc, ThroughLine(doc, 3), "- ");
        Assert.NotEqual(original, doc.Text);

        doc.UndoStack.Undo();

        Assert.Equal(original, doc.Text);
    }
}
