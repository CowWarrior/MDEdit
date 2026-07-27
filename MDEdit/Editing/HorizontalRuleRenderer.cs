using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using MDEdit;

namespace MDEdit.Editing;

/// <summary>
/// Draws the actual horizontal rule line for every visible line
/// <see cref="HorizontalRuleElementGenerator"/> is currently hiding. A rule has no content to
/// size a rendered element from — unlike a bullet's "•" or a table's measured columns — so it
/// has to span whatever width is actually on screen, which only an <see cref="IBackgroundRenderer"/>
/// drawing directly in the <c>TextView</c>'s own pixel space can answer (an inline element is
/// measured with an infinite width constraint, per the note on
/// <see cref="BlockquoteAccentBarRenderer"/> in Architecture). Registered on
/// <c>TextView.BackgroundRenderers</c> (not <c>ElementGenerators</c>) for that reason — the same
/// split as the blockquote accent bar and the table grid.
/// </summary>
/// <remarks>
/// Deliberately pinned to the <em>viewport</em>, not the document's horizontal scroll position —
/// unlike the blockquote bar and table grid, which track <c>HorizontalOffset</c> because their
/// geometry is anchored to real document content (an indent level, a column boundary), a rule has
/// no content-relative anchor worth preserving: it represents "the whole row", so it should always
/// fill whatever is currently visible rather than appearing to scroll off to the left when the
/// user scrolls right to read a long line elsewhere in the document.
/// </remarks>
internal sealed class HorizontalRuleRenderer : IBackgroundRenderer
{
    private const double LeftInset  = 4.0;
    private const double RightInset = 4.0;
    private const double Thickness  = 1.0;

    private readonly HorizontalRuleElementGenerator _generator;
    // HRuleBrushLight/Dark are now settable (AppSettings.EditorPreferences drives them, see
    // MainWindow.ApplyEditorPreferences), so this reads them through a reference to the colorizer
    // instance rather than a static field — the same reason this already holds one to
    // HorizontalRuleElementGenerator.
    private readonly MarkdownLineColorizer _colorizer;

    public HorizontalRuleRenderer(HorizontalRuleElementGenerator generator, MarkdownLineColorizer colorizer)
    {
        _generator = generator;
        _colorizer = colorizer;
    }

    public bool Enabled { get; set; }
    public bool IsDark { get; set; }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!Enabled) return;

        var doc = textView.Document;
        if (doc == null) return;

        var visualLines = textView.VisualLines;
        if (visualLines.Count == 0) return;

        // Colors, not brush choice, are shared with MarkdownLineColorizer's source-mode hrule
        // styling — the drawn rule and the raw "---" revealed under the caret must read as the
        // same construct.
        var brush = IsDark ? _colorizer.HRuleBrushDark : _colorizer.HRuleBrushLight;
        double right = textView.ActualWidth - RightInset;
        if (right <= LeftInset) return;

        foreach (var vl in visualLines)
        {
            var line = vl.FirstDocumentLine;
            if (!_generator.IsRendered(doc, line)) continue;

            double top    = vl.VisualTop - textView.VerticalOffset;
            double y      = Math.Round(top + vl.Height / 2);
            drawingContext.DrawRectangle(brush, null,
                new Rect(LeftInset, y - Thickness / 2, right - LeftInset, Thickness));
        }
    }
}
