using System.Windows;
using ICSharpCode.AvalonEdit;
using MDEdit.Editing;

namespace MDEdit.Tests;

/// <summary>
/// Regression tests for the selection AvalonEdit ends up with after a formatting command wraps
/// a selection reaching end-of-document. MainWindow.ApplyFormat used to set
/// <c>Editor.SelectionStart</c> then <c>Editor.SelectionLength</c> as two separate property
/// writes; TextEditor.SelectionStart's setter internally reuses whatever SelectionLength
/// currently reports (Select(value, SelectionLength)) rather than the value about to be
/// assigned. After MarkdownFormatter.Wrap's doc.Replace, AvalonEdit's selection anchors can
/// resolve to a length that no longer fits the document once combined with the new start —
/// only reachable when the replaced span extended to the end of the document — throwing
/// ArgumentOutOfRangeException. ApplyFormat now calls Editor.Select(start, length) atomically
/// instead. These run on a real (unshown) TextEditor/TextArea, not just TextDocument, since the
/// bug lives in AvalonEdit's own selection-anchor bookkeeping, not in MarkdownFormatter itself.
/// </summary>
public class ApplyFormatSelectionTests
{
    [Theory]
    [InlineData("**", "**")]
    [InlineData("_", "_")]
    [InlineData("~~", "~~")]
    [InlineData("`", "`")]
    public void Wrap_SelectionReachesEndOfDocument_ReselectsWithoutThrowing(string prefix, string suffix)
        => RunOnSta(() =>
        {
            var editor = new TextEditor { Text = "hi bold" }; // "bold" is the last 4 chars
            editor.Measure(new Size(800, 600));
            editor.Arrange(new Rect(0, 0, 800, 600));

            editor.Select(3, 4); // selects "bold", reaching EOF

            var sel = MarkdownFormatter.Wrap(editor.Document, new SelectionRange(editor.SelectionStart, editor.SelectionLength), prefix, suffix);
            Assert.NotNull(sel);
            editor.Select(sel!.Value.Start, sel.Value.Length); // mirrors MainWindow.ApplyFormat

            Assert.Equal("bold", editor.SelectedText);
            Assert.Equal($"hi {prefix}bold{suffix}", editor.Text);
        });

    private static void RunOnSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try { action(); } catch (Exception e) { ex = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (ex != null) throw ex;
    }
}
