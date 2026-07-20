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
