using System.Linq;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using MDEdit.Services;

namespace MDEdit.Editing;

/// <summary>
/// Compiles <c>Resources/Markdown.xshd</c> into a highlighting definition styled for one editor mode
/// and one theme (Requirements.md §6).
/// </summary>
/// <remarks>
/// Four definitions exist at a time — light/dark × source/WYSIWYG — because per-element styling is
/// per-mode, not just per-theme. Recolouring the parsed XSHD model before compiling it (rather than
/// mutating a loaded <see cref="IHighlightingDefinition"/>, whose colours may be frozen) is the
/// approach that was already in use for the two theme variants; this widens it from colours to
/// every styleable property.
/// <para>
/// <b>Every styleable property is cleared before the element's style is applied.</b> That is what
/// makes "inherit" mean inherit: were the grammar's own values left in place as a fallback, an
/// element whose weight the user cleared would silently fall back to whatever the file happened to
/// bake in, which is not what the UI promised. It is also why <c>Markdown.xshd</c> declares bare
/// <c>&lt;Color name="…"/&gt;</c> elements with no visual attributes — the defaults of record live
/// in <see cref="EditorPreferences"/>, in exactly one place.
/// </para>
/// <para>
/// Only elements carrying an <see cref="StyledElements.Element.XshdColorName"/> are handled here;
/// headings, blockquote and horizontal rule are line-level constructs XSHD cannot express, and are
/// styled by <c>MarkdownLineColorizer</c> against the same <see cref="ModeStyles"/>.
/// </para>
/// </remarks>
internal static class MarkdownHighlighting
{
    public static IHighlightingDefinition Build(ModeStyles mode, bool dark)
    {
        using var stream = typeof(MarkdownHighlighting).Assembly
            .GetManifestResourceStream("MDEdit.Resources.Markdown.xshd")!;
        using var reader = new XmlTextReader(stream);
        var xshd = HighlightingLoader.LoadXshd(reader);

        var colors = xshd.Elements.OfType<XshdColor>()
            .Where(c => c.Name is not null)
            .ToDictionary(c => c.Name!, StringComparer.Ordinal);

        foreach (var element in StyledElements.All)
        {
            if (element.XshdColorName is null) continue;
            if (!colors.TryGetValue(element.XshdColorName, out var color)) continue;

            mode.Elements.TryGetValue(element.Key, out var style);
            Apply(color, style, mode, dark);
        }

        return HighlightingLoader.Load(xshd, HighlightingManager.Instance);
    }

    private static void Apply(XshdColor color, ElementStyle? style, ModeStyles mode, bool dark)
    {
        // Clear before applying — see the class remarks. A missing element entry therefore renders
        // as plain inherited text rather than as whatever the grammar last happened to say.
        color.Foreground = null;
        color.Background = null;
        color.FontFamily = null;
        color.FontSize = null;
        color.FontWeight = null;
        color.FontStyle = null;
        color.Underline = null;
        color.Strikethrough = null;

        if (style is null) return;

        if (StyleResolver.Foreground(style, dark) is string fg)
            color.Foreground = new SimpleHighlightingBrush(StyleResolver.ParseColor(fg));
        if (StyleResolver.Background(style, dark) is string bg)
            color.Background = new SimpleHighlightingBrush(StyleResolver.ParseColor(bg));
        if (style.FontFamily is string family)
            color.FontFamily = new FontFamily(family);

        // XshdColor.FontSize is int?, so sizes here land on whole points — fractional multipliers
        // are representable for the colorizer-driven elements but not for these. Preferences shows
        // the resolved size beside the multiplier so the rounding is visible rather than puzzling.
        if (StyleResolver.EmSize(style, mode) is double em && em >= 1)
            color.FontSize = (int)Math.Round(em);

        color.FontWeight = StyleResolver.Weight(style.FontWeight);
        color.FontStyle = StyleResolver.Style(style.Italic);
        color.Underline = StyleResolver.Underline(style.Decoration);
        color.Strikethrough = StyleResolver.Strikethrough(style.Decoration);
    }
}
