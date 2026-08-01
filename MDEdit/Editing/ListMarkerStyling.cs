using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit.Editing;

/// <summary>
/// Builds the <see cref="TextBlock"/> that stands in for a list marker in WYSIWYG — the bullet
/// glyph and the numbered marker alike.
/// </summary>
/// <remarks>
/// Shared by <see cref="BulletListMarkerElementGenerator"/> and
/// <see cref="NumberedListMarkerElementGenerator"/> because bullets and numbers are one styleable
/// element (<see cref="StyledElements.ListMarker"/>): two copies of this would be two chances for a
/// numbered list to stop matching the bulleted list above it.
/// </remarks>
internal static class ListMarkerStyling
{
    /// <summary>
    /// Starts from the editor's own text run properties and applies only what the element actually
    /// overrides, so an unset style renders exactly as it did before per-element styling existed.
    /// </summary>
    public static TextBlock CreateMarkerBlock(string text, ITextRunConstructionContext context, ResolvedStyle style)
    {
        var props = context.GlobalTextRunProperties;
        var typeface = props.Typeface;

        return new TextBlock
        {
            Text        = text,
            FontFamily  = style.Family ?? typeface.FontFamily,
            FontStyle   = style.Style ?? typeface.Style,
            FontWeight  = style.Weight ?? typeface.Weight,
            FontStretch = typeface.Stretch,
            FontSize    = style.EmSize ?? props.FontRenderingEmSize,
            Foreground  = style.Foreground ?? props.ForegroundBrush,
            TextDecorations = style.Decorations,
            // Matches BlockquoteMarkerElementGenerator's indent so list items sit at the same pixel
            // depth as blockquote content.
            Margin      = new Thickness(BlockquoteMarkerElementGenerator.IndentPerLevel, 0, 0, 0),
        };
    }
}
