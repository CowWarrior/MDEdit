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
        var (firstLine, lastLine) = SelectedLineNumbers(doc, sel);

        using (doc.RunUpdate())
        {
            for (int n = lastLine; n >= firstLine; n--)
            {
                var line = doc.GetLineByNumber(n);
                if (firstLine != lastLine && IsBlank(doc, line)) continue;

                var body = Regex.Replace(doc.GetText(line), @"^#{1,6}\s*", "");
                doc.Replace(line.Offset, line.Length, prefix + body);
            }
        }

        return CoveringSelection(doc, firstLine, lastLine);
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
        var (firstLine, lastLine) = SelectedLineNumbers(doc, sel);

        if (firstLine == lastLine)
        {
            ToggleTaskOnLine(doc, doc.GetLineByNumber(firstLine));
            return null;
        }

        // Across several lines the command normalizes rather than flipping each line on its own:
        // first make every line a task, then check them all, then uncheck them all. Flipping each
        // line independently would scramble a block that is already a mix of checked and unchecked.
        bool allTasks = true, allChecked = true, anyLine = false;
        for (int n = firstLine; n <= lastLine; n++)
        {
            var line = doc.GetLineByNumber(n);
            if (IsBlank(doc, line)) continue;
            anyLine = true;

            if (MarkdownSyntax.TryGetTaskListMarker(doc, line, out _, out _, out bool isChecked))
            {
                if (!isChecked) allChecked = false;
            }
            else
            {
                allTasks = false;
                allChecked = false;
            }
        }
        if (!anyLine) return CoveringSelection(doc, firstLine, lastLine);

        using (doc.RunUpdate())
        {
            // Bottom-up so an edit never shifts a line this loop has yet to visit.
            for (int n = lastLine; n >= firstLine; n--)
            {
                var line = doc.GetLineByNumber(n);
                if (IsBlank(doc, line)) continue;

                if (!allTasks) MakeTaskListItem(doc, line);
                else SetTaskChecked(doc, line, !allChecked);
            }
        }

        return CoveringSelection(doc, firstLine, lastLine);
    }

    /// <summary>
    /// Numbered list. Unlike bullets and blockquotes this cannot go through
    /// <see cref="ToggleLinePrefix"/>, which applies one fixed string to every line — the marker has
    /// to count, so a multi-line selection becomes "1." "2." "3." rather than "1." three times.
    /// </summary>
    /// <remarks>
    /// Numbering runs continuously across blank lines inside the selection (1, blank, 2) rather than
    /// restarting, on the grounds that the user selected one block and meant one list. A line that is
    /// already numbered is renumbered in place rather than given a second marker.
    /// </remarks>
    public static SelectionRange? NumberedList(TextDocument doc, SelectionRange sel)
    {
        var (firstLine, lastLine) = SelectedLineNumbers(doc, sel);
        var single = firstLine == lastLine;

        // Lines to act on, in document order — blank lines are skipped only in a multi-line
        // selection, matching the other line-based commands.
        var targets = new List<int>();
        for (int n = firstLine; n <= lastLine; n++)
        {
            if (!single && IsBlank(doc, doc.GetLineByNumber(n))) continue;
            targets.Add(n);
        }
        if (targets.Count == 0) return CoveringSelection(doc, firstLine, lastLine);

        // Same normalize-don't-flip rule as the other commands: strip only when every target is
        // already numbered, otherwise number the block through.
        var remove = targets.All(n => MarkdownSyntax.TryGetNumberedListMarker(doc, doc.GetLineByNumber(n), out _, out _));

        using (doc.RunUpdate())
        {
            // Bottom-up so an edit never shifts a line still to be visited; the number comes from
            // the target's index, so counting order is unaffected by the direction of iteration.
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                var line = doc.GetLineByNumber(targets[i]);
                var hasMarker = MarkdownSyntax.TryGetNumberedListMarker(doc, line, out int markerOffset, out int markerLength);

                if (remove)
                {
                    if (!hasMarker) continue;
                    // Take the space after the marker with it, so removal is the exact inverse.
                    var length = markerLength;
                    if (markerOffset + length < line.EndOffset &&
                        doc.GetCharAt(markerOffset + length) is ' ' or '\t') length++;
                    doc.Remove(markerOffset, length);
                }
                else if (hasMarker)
                {
                    // markerLength covers the digits and the '.', not the following space.
                    doc.Replace(markerOffset, markerLength, $"{i + 1}.");
                }
                else
                {
                    doc.Insert(line.Offset, $"{i + 1}. ");
                }
            }
        }

        return CoveringSelection(doc, firstLine, lastLine);
    }

    public static SelectionRange? ToggleLinePrefix(TextDocument doc, SelectionRange sel, string prefix)
    {
        var (firstLine, lastLine) = SelectedLineNumbers(doc, sel);

        if (firstLine == lastLine)
        {
            // Single line keeps the original behaviour exactly, including on a blank line — adding
            // the prefix to an empty line is how a list gets started in the first place.
            var only = doc.GetLineByNumber(firstLine);
            ApplyLinePrefix(doc, only, prefix, remove: HasPrefix(doc, only, prefix));
            return null;
        }

        // Remove only when every affected line already has the prefix; otherwise add it to the ones
        // that don't. A mixed block therefore becomes uniformly prefixed, and pressing again clears
        // it — the same normalize-don't-flip rule as TaskListItem.
        bool remove = true, anyLine = false;
        for (int n = firstLine; n <= lastLine && remove; n++)
        {
            var line = doc.GetLineByNumber(n);
            if (IsBlank(doc, line)) continue;
            anyLine = true;
            if (!HasPrefix(doc, line, prefix)) remove = false;
        }

        using (doc.RunUpdate())
        {
            for (int n = lastLine; n >= firstLine; n--)
            {
                var line = doc.GetLineByNumber(n);
                if (IsBlank(doc, line)) continue;
                ApplyLinePrefix(doc, line, prefix, remove && anyLine);
            }
        }

        return CoveringSelection(doc, firstLine, lastLine);
    }

    // ── Multi-line helpers ────────────────────────────────────────────────
    // Line *numbers* rather than DocumentLine references or offsets: none of the line-based commands
    // add or remove line breaks, so numbers stay valid across every edit below, while offsets do not.

    /// <summary>
    /// The range of lines a selection touches. A selection ending exactly at the start of a line
    /// does not include that line — dragging from the start of line 1 to the start of line 3 selects
    /// two lines, which is both what the user sees highlighted and what other editors do.
    /// </summary>
    private static (int First, int Last) SelectedLineNumbers(TextDocument doc, SelectionRange sel)
    {
        var first = doc.GetLineByOffset(sel.Start);
        if (sel.Length <= 0) return (first.LineNumber, first.LineNumber);

        var last = doc.GetLineByOffset(sel.Start + sel.Length);
        if (last.LineNumber > first.LineNumber && sel.Start + sel.Length == last.Offset)
            last = doc.GetLineByNumber(last.LineNumber - 1);

        return (first.LineNumber, last.LineNumber);
    }

    /// <summary>
    /// Leaves the whole affected block selected, so the command can be pressed again to toggle back
    /// and the user can see what changed. Returns null for a single line, preserving the original
    /// behaviour of leaving caret placement to the document's own anchors.
    /// </summary>
    private static SelectionRange? CoveringSelection(TextDocument doc, int firstLine, int lastLine)
    {
        if (lastLine <= firstLine) return null;
        var first = doc.GetLineByNumber(firstLine);
        var last  = doc.GetLineByNumber(lastLine);
        return new SelectionRange(first.Offset, last.EndOffset - first.Offset);
    }

    // Blank lines inside a multi-line selection are skipped: a bullet or heading marker on an empty
    // line in the middle of a block is noise. The single-line paths deliberately don't skip them.
    private static bool IsBlank(TextDocument doc, DocumentLine line)
        => string.IsNullOrWhiteSpace(doc.GetText(line));

    private static bool HasPrefix(TextDocument doc, DocumentLine line, string prefix)
        => doc.GetText(line).StartsWith(prefix, StringComparison.Ordinal);

    private static void ApplyLinePrefix(TextDocument doc, DocumentLine line, string prefix, bool remove)
    {
        if (remove)
        {
            if (HasPrefix(doc, line, prefix)) doc.Remove(line.Offset, prefix.Length);
        }
        else if (!HasPrefix(doc, line, prefix))
        {
            doc.Insert(line.Offset, prefix);
        }
    }

    private static void ToggleTaskOnLine(TextDocument doc, DocumentLine line)
    {
        if (MarkdownSyntax.TryGetTaskListMarker(doc, line, out int markerOffset, out int markerLength, out bool isChecked))
        {
            // The state character sits one before the closing ']'.
            doc.Replace(markerOffset + markerLength - 2, 1, isChecked ? " " : "x");
            return;
        }
        MakeTaskListItem(doc, line);
    }

    /// <remarks>
    /// The bullet case matters: inserting "- [ ] " onto an existing "- foo" would produce
    /// "- [ ] - foo". Adding just the box after the existing marker turns it into a task instead.
    /// </remarks>
    private static void MakeTaskListItem(TextDocument doc, DocumentLine line)
    {
        if (MarkdownSyntax.TryGetTaskListMarker(doc, line, out _, out _, out _)) return;

        if (MarkdownSyntax.TryGetBulletListMarker(doc, line, out int bulletOffset))
        {
            int pos = bulletOffset - line.Offset + 1;
            while (pos < line.Length && doc.GetCharAt(line.Offset + pos) is ' ' or '\t') pos++;
            doc.Insert(line.Offset + pos, "[ ] ");
            return;
        }

        doc.Insert(line.Offset, "- [ ] ");
    }

    private static void SetTaskChecked(TextDocument doc, DocumentLine line, bool isChecked)
    {
        if (!MarkdownSyntax.TryGetTaskListMarker(doc, line, out int markerOffset, out int markerLength, out bool current))
            return;
        if (current == isChecked) return;
        doc.Replace(markerOffset + markerLength - 2, 1, isChecked ? "x" : " ");
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
