using ICSharpCode.AvalonEdit.Document;

namespace MDEdit.Editing;

/// <summary>
/// Computes the status bar's character count under the user's chosen line-break weight
/// (Requirements.md §9) — 0, 1, or 2 characters per line break, independent of what the line
/// break actually is in the underlying text.
/// </summary>
internal static class CharacterCounter
{
    /// <summary>
    /// <paramref name="doc"/>'s length as if every line break counted as
    /// <paramref name="lineBreakCharWeight"/> characters, regardless of whether it's actually an
    /// LF, a CRLF, or a lone CR in the raw text. Works by subtracting each line's real
    /// <see cref="DocumentLine.DelimiterLength"/> (0, 1, or 2) from <c>TextLength</c> and adding
    /// back <paramref name="lineBreakCharWeight"/> once per line break instead — so mixed line
    /// endings in one document are counted uniformly rather than each keeping its own actual
    /// width. <paramref name="lineBreakCharWeight"/> is clamped to [0, 2] since
    /// <c>settings.json</c> is hand-editable.
    /// </summary>
    public static int Count(TextDocument doc, int lineBreakCharWeight)
    {
        lineBreakCharWeight = Math.Clamp(lineBreakCharWeight, 0, 2);

        int actualDelimiterChars = 0;
        int lineBreakCount = 0;
        for (int n = 1; n <= doc.LineCount; n++)
        {
            var delimiterLength = doc.GetLineByNumber(n).DelimiterLength;
            if (delimiterLength == 0) continue;
            actualDelimiterChars += delimiterLength;
            lineBreakCount++;
        }

        return doc.TextLength - actualDelimiterChars + lineBreakCount * lineBreakCharWeight;
    }
}
