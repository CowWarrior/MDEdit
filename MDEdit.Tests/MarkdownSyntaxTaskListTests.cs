using ICSharpCode.AvalonEdit.Document;
using MDEdit.Editing;

namespace MDEdit.Tests;

public class MarkdownSyntaxTaskListTests
{
    private static (bool Found, int Offset, int Length, bool Checked) Task(string text)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);
        var found = MarkdownSyntax.TryGetTaskListMarker(doc, line, out int offset, out int length, out bool isChecked);
        return (found, offset, length, isChecked);
    }

    [Theory]
    [InlineData("- [ ] todo", false)]
    [InlineData("- [x] done", true)]
    [InlineData("- [X] done", true)]
    [InlineData("* [ ] todo", false)]
    [InlineData("+ [x] done", true)]
    public void TryGetTaskListMarker_RecognizesBothStatesAndAllBullets(string text, bool expectedChecked)
    {
        var result = Task(text);

        Assert.True(result.Found);
        Assert.Equal(0, result.Offset);
        Assert.Equal(5, result.Length);          // "- [ ]" — bullet through ']', not the space
        Assert.Equal(expectedChecked, result.Checked);
    }

    [Fact]
    public void TryGetTaskListMarker_Indented_ReportsOffsetOfTheBullet()
    {
        var result = Task("    - [ ] nested");

        Assert.True(result.Found);
        Assert.Equal(4, result.Offset);
        Assert.Equal(5, result.Length);
    }

    [Fact]
    public void TryGetTaskListMarker_NoTrailingText_IsStillATask()
    {
        // A just-inserted item whose trailing space has been trimmed must still toggle.
        Assert.True(Task("- [ ]").Found);
    }

    [Theory]
    [InlineData("- plain bullet")]
    [InlineData("plain text")]
    [InlineData("")]
    [InlineData("- [] missing state")]
    [InlineData("- [ai] two chars")]
    [InlineData("- [y] wrong letter")]
    [InlineData("[ ] no bullet")]
    [InlineData("- [ ]nospace")]
    [InlineData("1. [ ] numbered, not a bullet")]
    [InlineData("--- [ ] horizontal rule")]
    public void TryGetTaskListMarker_NotATask_ReturnsFalse(string text)
    {
        Assert.False(Task(text).Found);
    }

    // A task line IS a bullet line — that's correct, and it's why the bullet generator has to check
    // for a task and stand aside rather than the detection excluding it.
    [Fact]
    public void TryGetBulletListMarker_AlsoMatchesTaskLines()
    {
        var doc = new TextDocument("- [ ] todo");

        Assert.True(MarkdownSyntax.TryGetBulletListMarker(doc, doc.GetLineByNumber(1), out _));
    }

    [Fact]
    public void FindEmphasisSpans_InsideTaskItem_IsStillFound()
    {
        var doc  = new TextDocument("- [ ] review the **bold** item");
        var span = Assert.Single(MarkdownSyntax.FindEmphasisSpans(doc, doc.GetLineByNumber(1)));

        Assert.Equal("**bold**", "- [ ] review the **bold** item"[span.Start..span.End]);
    }
}

public class MarkdownFormatterTaskListTests
{
    private static string Apply(string text, int caretOffset = 0)
    {
        var doc = new TextDocument(text);
        MarkdownFormatter.TaskListItem(doc, new SelectionRange(caretOffset, 0));
        return doc.Text;
    }

    [Fact]
    public void TaskListItem_OnPlainLine_InsertsUncheckedItem()
    {
        Assert.Equal("- [ ] write it", Apply("write it"));
    }

    [Fact]
    public void TaskListItem_OnEmptyLine_InsertsUncheckedItem()
    {
        Assert.Equal("- [ ] ", Apply(""));
    }

    // Inserting the whole prefix onto an existing bullet would give "- [ ] - foo".
    [Fact]
    public void TaskListItem_OnBulletItem_AddsBoxRatherThanASecondBullet()
    {
        Assert.Equal("- [ ] foo", Apply("- foo"));
    }

    [Fact]
    public void TaskListItem_OnIndentedBulletItem_AddsBoxInPlace()
    {
        Assert.Equal("    - [ ] foo", Apply("    - foo"));
    }

    [Fact]
    public void TaskListItem_OnUncheckedItem_ChecksIt()
    {
        Assert.Equal("- [x] todo", Apply("- [ ] todo"));
    }

    [Fact]
    public void TaskListItem_OnCheckedItem_UnchecksIt()
    {
        Assert.Equal("- [ ] done", Apply("- [x] done"));
    }

    [Fact]
    public void TaskListItem_TogglesRoundTrip()
    {
        Assert.Equal("- [ ] todo", Apply(Apply("- [ ] todo")));
    }

    [Fact]
    public void TaskListItem_OnIndentedItem_TogglesTheRightCharacter()
    {
        Assert.Equal("    - [x] nested", Apply("    - [ ] nested"));
    }

    [Fact]
    public void TaskListItem_OnSecondLine_AffectsOnlyThatLine()
    {
        var text = "- [ ] first\n- [ ] second";
        var doc  = new TextDocument(text);
        MarkdownFormatter.TaskListItem(doc, new SelectionRange(text.IndexOf("second", StringComparison.Ordinal), 0));

        Assert.Equal("- [ ] first\n- [x] second", doc.Text);
    }
}
