using System.Windows;
using System.Windows.Media;
using MDEdit.Services;

namespace MDEdit.Editing;

/// <summary>
/// Turns an <see cref="ElementStyle"/>'s persisted, WPF-free values (hex strings, named weights, a
/// named decoration, a size multiplier) into the WPF types the two rendering paths need.
/// </summary>
/// <remarks>
/// Shared deliberately: per-element styling reaches the editor through two completely different
/// mechanisms — <see cref="MarkdownHighlighting"/> writing onto the syntax-highlighting definition,
/// and <c>MarkdownLineColorizer</c> writing onto visual line elements — and the two must never
/// disagree about what a stored value means. This is the same "one shared gate" rule the detection
/// code follows.
/// <para>
/// Every method maps an unrecognized value to null, i.e. inherit, rather than throwing:
/// <c>settings.json</c> is hand-editable, and a typo in a weight name should cost that one
/// override, not the ability to open a document.
/// </para>
/// </remarks>
internal static class StyleResolver
{
    public static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static string? Foreground(ElementStyle style, bool dark)
        => dark ? style.ForegroundDark : style.ForegroundLight;

    public static string? Background(ElementStyle style, bool dark)
        => dark ? style.BackgroundDark : style.BackgroundLight;

    public static FontWeight? Weight(string? value) => value switch
    {
        ElementStyle.WeightNormal => FontWeights.Normal,
        ElementStyle.WeightSemiBold => FontWeights.SemiBold,
        ElementStyle.WeightBold => FontWeights.Bold,
        _ => null,
    };

    public static FontStyle? Style(bool? italic) => italic switch
    {
        true => FontStyles.Italic,
        false => FontStyles.Normal,
        _ => null,
    };

    // Null decoration inherits; any recognized value answers both questions, which is what keeps
    // underline and strikethrough mutually exclusive by construction rather than by validation.
    public static bool? Underline(string? decoration)
        => decoration is null ? null : decoration == ElementStyle.DecorationUnderline;

    public static bool? Strikethrough(string? decoration)
        => decoration is null ? null : decoration == ElementStyle.DecorationStrikethrough;

    /// <summary>
    /// The element's rendered size, or null to inherit the mode's base. A multiplier rather than an
    /// absolute size is what lets one base-size change rescale every element proportionally.
    /// </summary>
    public static double? EmSize(ElementStyle style, ModeStyles mode)
        => style.FontScale is double scale && scale > 0 ? mode.BaseFontSize * scale : null;

    // Frozen for the same reason the brushes are: this is cached and shared across every visual line
    // using the element. WPF's own TextDecorations.Underline/Strikethrough are already frozen
    // statics, so an unfrozen empty one here would be the odd member out — and the only one carrying
    // thread affinity.
    private static readonly TextDecorationCollection NoDecorations = CreateFrozenEmptyDecorations();

    public static TextDecorationCollection? Decorations(string? decoration) => decoration switch
    {
        ElementStyle.DecorationUnderline => TextDecorations.Underline,
        ElementStyle.DecorationStrikethrough => TextDecorations.Strikethrough,
        // Explicitly "none" clears whatever was inherited; null leaves it alone.
        ElementStyle.DecorationNone => NoDecorations,
        _ => null,
    };

    private static TextDecorationCollection CreateFrozenEmptyDecorations()
    {
        var decorations = new TextDecorationCollection();
        decorations.Freeze();
        return decorations;
    }

    /// <summary>
    /// Resolves one element's style into ready-to-apply WPF values, with frozen brushes.
    /// </summary>
    /// <remarks>
    /// Every field is null when the element inherits it, so a caller applies only what was actually
    /// set and leaves the rest of the run's properties untouched. An element absent from the mode's
    /// dictionary resolves to all-null, i.e. inherits everything.
    /// <para>
    /// Brushes are frozen: they are cached and shared across every visual line that uses the
    /// element, and a frozen brush is both cheaper and safe to reuse.
    /// </para>
    /// </remarks>
    public static ResolvedStyle Resolve(string elementKey, ModeStyles mode, bool dark)
    {
        if (!mode.Elements.TryGetValue(elementKey, out var style)) return default;

        return new ResolvedStyle(
            FreezeBrush(Foreground(style, dark)),
            FreezeBrush(Background(style, dark)),
            style.FontFamily is string family ? new FontFamily(family) : null,
            EmSize(style, mode),
            Weight(style.FontWeight),
            Style(style.Italic),
            Decorations(style.Decoration));
    }

    private static SolidColorBrush? FreezeBrush(string? hex)
    {
        if (hex is null) return null;

        var brush = new SolidColorBrush(ParseColor(hex));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// One element's style as ready-to-apply WPF values. A null member means "inherit" — the caller
/// leaves that property of the text run alone.
/// </summary>
internal readonly record struct ResolvedStyle(
    SolidColorBrush? Foreground,
    SolidColorBrush? Background,
    FontFamily? Family,
    double? EmSize,
    FontWeight? Weight,
    FontStyle? Style,
    TextDecorationCollection? Decorations);
