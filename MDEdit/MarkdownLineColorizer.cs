using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using MDEdit.Editing;

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

    // Set by MainWindow's live-preview toggle. Only affects heading font size (Typora-style
    // scaling) — colors/weight apply regardless, matching the pre-live-preview behavior.
    public bool LivePreviewEnabled { get; set; }

    protected override void ColorizeLine(DocumentLine line)
    {
        var doc = CurrentContext.Document;
        if (line.Length == 0) return;

        if (MarkdownSyntax.TryGetHeadingLevel(doc, line, out int level, out _))
        {
            var scale = LivePreviewEnabled ? HeadingScale(level) : 1.0;
            ColorLine(line, IsDark ? DarkHeadingBrush : LightHeadingBrush,
                level <= 3 ? FontWeights.Bold : FontWeights.SemiBold, emSizeScale: scale);
            return;
        }

        if (MarkdownSyntax.TryGetBlockquoteMarkerLength(doc, line, out _, out _))
        {
            ColorLine(line, IsDark ? DarkBlockquoteBrush : LightBlockquoteBrush, FontWeights.Normal, italic: true);
            return;
        }

        var text = doc.GetText(line);
        if (IsHorizontalRule(text))
            ColorLine(line, IsDark ? DarkHRuleBrush : LightHRuleBrush, FontWeights.Normal);
    }

    // Typora-ish size ratios relative to the editor's base font size; only applied in live preview.
    private static double HeadingScale(int level) => level switch
    {
        1 => 1.6,
        2 => 1.4,
        3 => 1.25,
        4 => 1.15,
        5 => 1.05,
        _ => 1.0,
    };

    private void ColorLine(DocumentLine line, SolidColorBrush brush,
        FontWeight weight, bool italic = false, double emSizeScale = 1.0)
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
            if (emSizeScale != 1.0)
                el.TextRunProperties.SetFontRenderingEmSize(el.TextRunProperties.FontRenderingEmSize * emSizeScale);
        });
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
