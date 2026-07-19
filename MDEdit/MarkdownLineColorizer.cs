using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit;

internal sealed class MarkdownLineColorizer : DocumentColorizingTransformer
{
    private static readonly SolidColorBrush LightHeadingBrush    = Freeze(Color.FromRgb(0x00, 0x57, 0xAE));
    private static readonly SolidColorBrush LightBlockquoteBrush = Freeze(Color.FromRgb(0x6A, 0x73, 0x7D));
    private static readonly SolidColorBrush LightHRuleBrush      = Freeze(Color.FromRgb(0xBB, 0xBB, 0xBB));

    private static readonly SolidColorBrush DarkHeadingBrush    = Freeze(Color.FromRgb(0x58, 0xA6, 0xFF));
    private static readonly SolidColorBrush DarkBlockquoteBrush = Freeze(Color.FromRgb(0x8B, 0x94, 0x9E));
    private static readonly SolidColorBrush DarkHRuleBrush      = Freeze(Color.FromRgb(0x48, 0x4F, 0x58));

    // Set by MainWindow.ApplyTheme; a TextView.Redraw() afterwards re-runs ColorizeLine.
    public bool IsDark { get; set; }

    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);
        if (text.Length == 0) return;

        if (TryGetHeadingLevel(text, out int level))
            ColorLine(line, IsDark ? DarkHeadingBrush : LightHeadingBrush, level <= 3 ? FontWeights.Bold : FontWeights.SemiBold);
        else if (text[0] == '>')
            ColorLine(line, IsDark ? DarkBlockquoteBrush : LightBlockquoteBrush, FontWeights.Normal, italic: true);
        else if (IsHorizontalRule(text))
            ColorLine(line, IsDark ? DarkHRuleBrush : LightHRuleBrush, FontWeights.Normal);
    }

    private void ColorLine(DocumentLine line, SolidColorBrush brush,
        FontWeight weight, bool italic = false)
    {
        ChangeLinePart(line.Offset, line.EndOffset, el =>
        {
            el.TextRunProperties.SetForegroundBrush(brush);
            var old = el.TextRunProperties.Typeface;
            el.TextRunProperties.SetTypeface(new Typeface(
                old.FontFamily,
                italic ? FontStyles.Italic : old.Style,
                weight,
                old.Stretch));
        });
    }

    private static bool TryGetHeadingLevel(string text, out int level)
    {
        level = 0;
        if (text[0] != '#') return false;
        int count = 0;
        while (count < text.Length && text[count] == '#') count++;
        if (count > 6 || count >= text.Length || text[count] != ' ') return false;
        level = count;
        return true;
    }

    private static bool IsHorizontalRule(string text)
    {
        if (text.Length < 3) return false;
        char c = text[0];
        if (c != '-' && c != '*' && c != '_') return false;
        foreach (char ch in text)
            if (ch != c && ch != ' ') return false;
        return true;
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }
}
