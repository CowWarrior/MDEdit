using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit.Editing;

/// <summary>
/// Live-preview element generator: renders a recognized emoji shortcode (<c>:joy:</c>) as the emoji
/// character itself, except for whichever shortcode the caret is currently inside — revealing the
/// raw text so it can be edited.
/// </summary>
/// <remarks>
/// This is the first generator to combine <em>per-span</em> reveal (like
/// <see cref="EmphasisMarkerElementGenerator"/>) with a <em>visible</em> replacement (like
/// <see cref="BulletListMarkerElementGenerator"/>) — the other visible-replacement generators are
/// all per-line. The whole shortcode is replaced rather than markers hidden around content: unlike
/// emphasis, there is no inner text to keep.
///
/// The emoji renders monochrome, not in colour — WPF's text stack has no COLR/CPAL colour-font
/// support. That is a platform limitation, verified in this app, and not something to keep trying
/// to fix here; colour would need an image-based rendering route entirely.
///
/// The shortcode characters keep their document offsets, so selection, undo, and the saved file are
/// unaffected — only the rendering changes.
/// </remarks>
internal sealed class EmojiElementGenerator : VisualLineElementGenerator
{
    public bool Enabled { get; set; }
    public int CaretOffset { get; set; } = -1;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!Enabled) return -1;

        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(startOffset);

        foreach (var span in MarkdownSyntax.FindEmojiSpans(doc, line))
        {
            if (IsCaretInside(span)) continue;
            if (span.Start >= startOffset) return span.Start;
        }

        return -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(offset);

        foreach (var span in MarkdownSyntax.FindEmojiSpans(doc, line))
        {
            if (IsCaretInside(span)) continue;
            if (offset != span.Start) continue;

            // Styled from the editor's own run properties (typeface/size/foreground) so it follows
            // the current theme and font with no per-theme wiring in MainWindow — same approach as
            // BulletListMarkerElementGenerator.
            var props    = CurrentContext.GlobalTextRunProperties;
            var typeface = props.Typeface;
            var glyph = new TextBlock
            {
                Text        = span.Emoji,
                FontFamily  = typeface.FontFamily,
                FontStyle   = typeface.Style,
                FontWeight  = typeface.Weight,
                FontStretch = typeface.Stretch,
                FontSize    = props.FontRenderingEmSize,
                Foreground  = props.ForegroundBrush,
            };
            return new InlineObjectElement(span.End - span.Start, glyph);
        }

        // GetFirstInterestedOffset only ever returns offsets this method recognizes, so this is
        // unreachable in practice — return a harmless zero-length element rather than throw.
        return new InlineObjectElement(0, new TextBlock());
    }

    private bool IsCaretInside(EmojiSpan span) => CaretOffset >= span.Start && CaretOffset <= span.End;
}
