using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Document;

namespace MDEdit.Editing;

/// <summary>Caret/selection position after a formatting operation (Length 0 = caret only).</summary>
internal readonly record struct SelectionRange(int Start, int Length);

/// <summary>
/// Markdown formatting operations on a <see cref="TextDocument"/>, kept free of any
/// TextEditor/UI dependency so they can be unit tested headlessly. Each method mutates
/// the document through discrete Replace/Insert/Remove calls (preserving AvalonEdit's
/// undo stack) and returns where the caret/selection should land afterwards — or null
/// to leave caret placement to the document's own anchor movement.
/// </summary>
internal static class MarkdownFormatter
{
    public static SelectionRange? Wrap(TextDocument doc, SelectionRange sel, string prefix, string suffix)
    {
        if (sel.Length > 0)
        {
            var inner = doc.GetText(sel.Start, sel.Length);
            doc.Replace(sel.Start, sel.Length, prefix + inner + suffix);
            return new SelectionRange(sel.Start + prefix.Length, inner.Length);
        }

        doc.Insert(sel.Start, prefix + suffix);
        return new SelectionRange(sel.Start + prefix.Length, 0);
    }

    public static SelectionRange? Heading(TextDocument doc, SelectionRange sel, int level)
    {
        var prefix = new string('#', level) + " ";
        var line   = doc.GetLineByOffset(sel.Start);
        var text   = doc.GetText(line);
        var body   = Regex.Replace(text, @"^#{1,6}\s*", "");
        doc.Replace(line.Offset, line.Length, prefix + body);
        return null;
    }

    /// <summary>
    /// Task list item: toggles the box on a line that already is one, adds a box to a plain bullet
    /// item, and otherwise inserts a fresh unchecked item — the three states Requirements.md §3 asks
    /// for ("inserts an unchecked item, and the user can toggle an existing item").
    /// </summary>
    /// <remarks>
    /// The bullet case matters: inserting "- [ ] " onto an existing "- foo" would produce
    /// "- [ ] - foo". Adding just the box after the existing marker turns it into a task instead,
    /// which is what pressing the button on a list item is asking for.
    /// </remarks>
    public static SelectionRange? TaskListItem(TextDocument doc, SelectionRange sel)
    {
        var line = doc.GetLineByOffset(sel.Start);

        if (MarkdownSyntax.TryGetTaskListMarker(doc, line, out int markerOffset, out int markerLength, out bool isChecked))
        {
            // The state character sits one before the closing ']'.
            int stateOffset = markerOffset + markerLength - 2;
            doc.Replace(stateOffset, 1, isChecked ? " " : "x");
            return null;
        }

        if (MarkdownSyntax.TryGetBulletListMarker(doc, line, out int bulletOffset))
        {
            int pos = bulletOffset - line.Offset + 1;
            while (pos < line.Length && doc.GetCharAt(line.Offset + pos) is ' ' or '\t') pos++;
            doc.Insert(line.Offset + pos, "[ ] ");
            return null;
        }

        doc.Insert(line.Offset, "- [ ] ");
        return null;
    }

    public static SelectionRange? ToggleLinePrefix(TextDocument doc, SelectionRange sel, string prefix)
    {
        var line = doc.GetLineByOffset(sel.Start);
        var text = doc.GetText(line);
        if (text.StartsWith(prefix, StringComparison.Ordinal))
            doc.Remove(line.Offset, prefix.Length);
        else
            doc.Insert(line.Offset, prefix);
        return null;
    }

    public static SelectionRange? CodeBlock(TextDocument doc, SelectionRange sel)
    {
        if (sel.Length > 0)
        {
            var inner = doc.GetText(sel.Start, sel.Length);
            doc.Replace(sel.Start, sel.Length, "```\n" + inner + "\n```");
            return null;
        }

        doc.Insert(sel.Start, "```\n\n```");
        return new SelectionRange(sel.Start + 4, 0);
    }

    public static SelectionRange? Link(TextDocument doc, SelectionRange sel)
    {
        if (sel.Length > 0)
        {
            var inner = doc.GetText(sel.Start, sel.Length);
            doc.Replace(sel.Start, sel.Length, $"[{inner}](url)");
            return new SelectionRange(sel.Start + 1 + inner.Length + 2, 3);
        }

        doc.Insert(sel.Start, "[link text](url)");
        return new SelectionRange(sel.Start + 1, 9);
    }
}
