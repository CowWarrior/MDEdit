using System.IO;
using System.Linq;
using System.Windows.Media;

namespace MDEdit.Editing;

/// <summary>
/// The installed font families offered in Preferences (Requirements.md §6), and which of them are
/// fixed-width.
/// </summary>
/// <remarks>
/// The fixed-width split exists for the font drop-downs: source mode is monospaced by design and
/// WYSIWYG's code elements want a monospaced family too, so those families are listed first rather
/// than leaving the user to find "Consolas" somewhere in a few hundred alphabetical entries.
/// <para>
/// WPF exposes no "is this font monospaced" flag, and the OS font metadata that would answer it
/// (the OS/2 table's <c>isFixedPitch</c>) isn't surfaced either — so this measures instead, which is
/// the same thing a renderer would do and needs no new dependency: a family is fixed-width when a
/// deliberately mixed set of characters all advance by the same width.
/// </para>
/// </remarks>
internal static class FontCatalog
{
    // Narrow, wide, and punctuation. In any proportional family these differ substantially; in a
    // fixed-pitch one they are identical by definition. Four characters rather than two so a family
    // that happens to match on one pair isn't misfiled.
    private static readonly char[] ProbeCharacters = ['i', 'W', 'M', '.'];

    // Advance widths are ems (typically 0..1), so differences in a proportional font are on the
    // order of 0.1–0.5. This only needs to absorb rounding in the font's own metrics.
    private const double WidthTolerance = 0.001;

    private static IReadOnlyList<string>? _installed;
    private static IReadOnlyList<string>? _monospaced;

    /// <summary>
    /// Every installed family name, sorted case-insensitively. Enumerating and classifying the
    /// system fonts costs real time, and both Preferences tabs want the same answer, so it is
    /// computed once per process.
    /// </summary>
    public static IReadOnlyList<string> Installed => _installed ??= Fonts.SystemFontFamilies
        .Select(f => f.Source)
        .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>The fixed-width subset of <see cref="Installed"/>, in the same order.</summary>
    public static IReadOnlyList<string> Monospaced => _monospaced ??= Installed
        .Where(IsMonospaced)
        .ToList();

    /// <summary>
    /// Whether <paramref name="familyName"/> resolves to a fixed-width font. Accepts a
    /// comma-separated fallback stack (WPF's <see cref="FontFamily"/> resolves it to the first
    /// available family), which matters because the default code font is exactly that.
    /// </summary>
    /// <remarks>
    /// Returns false rather than throwing for an unknown, empty, or unreadable family: this feeds a
    /// list in a settings dialog, and one damaged font on the machine must not be able to stop
    /// Preferences from opening.
    /// </remarks>
    public static bool IsMonospaced(string? familyName)
    {
        if (string.IsNullOrWhiteSpace(familyName)) return false;

        try
        {
            return IsMonospaced(new FontFamily(familyName));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or FileFormatException)
        {
            return false;
        }
    }

    private static bool IsMonospaced(FontFamily family)
    {
        var typeface = family.GetTypefaces().FirstOrDefault();
        if (typeface is null || !typeface.TryGetGlyphTypeface(out var glyphs)) return false;

        double? reference = null;
        foreach (char c in ProbeCharacters)
        {
            if (!glyphs.CharacterToGlyphMap.TryGetValue(c, out ushort glyphIndex)) return false;
            if (!glyphs.AdvanceWidths.TryGetValue(glyphIndex, out double width)) return false;

            // A family whose probe glyphs are all zero-width would otherwise pass trivially.
            if (width <= 0) return false;

            reference ??= width;
            if (Math.Abs(width - reference.Value) > WidthTolerance) return false;
        }

        return reference is not null;
    }
}
