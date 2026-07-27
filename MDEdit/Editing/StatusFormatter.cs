using System.Globalization;

namespace MDEdit.Editing;

/// <summary>
/// Formats the figures shown in the status bar. Pure string formatting, kept out of MainWindow so
/// the rounding and unit-promotion edge cases can be unit-tested without a window.
/// </summary>
internal static class StatusFormatter
{
    private const long Scale = 1024;
    private static readonly string[] LargeUnits = ["KB", "MB", "GB", "TB"];

    /// <summary>
    /// "512 bytes", "1.5 KB", "2.4 MB". Uses 1024-based units labelled KB/MB/GB, matching what
    /// Windows Explorer reports, so the two figures agree for a saved file.
    /// </summary>
    public static string FormatFileSize(long bytes, IFormatProvider? provider = null)
    {
        provider ??= CultureInfo.CurrentCulture;
        if (bytes < 0) bytes = 0;
        if (bytes < Scale)
            return bytes == 1 ? "1 byte" : $"{bytes.ToString("N0", provider)} bytes";

        double value = bytes;
        int unit = -1;
        do
        {
            value /= Scale;
            unit++;
        }
        while (value >= Scale && unit < LargeUnits.Length - 1);

        // Rounding to one decimal can land exactly on 1024.0 (e.g. 1,048,575 bytes -> "1024.0 KB"),
        // which reads as nonsense next to a 1 MB file. Promote to the next unit when that happens.
        if (Math.Round(value, 1) >= Scale && unit < LargeUnits.Length - 1)
        {
            value /= Scale;
            unit++;
        }

        return $"{value.ToString("0.0", provider)} {LargeUnits[unit]}";
    }

    /// <summary>"1,234 characters".</summary>
    public static string FormatCharacterCount(int count, IFormatProvider? provider = null)
    {
        provider ??= CultureInfo.CurrentCulture;
        return count == 1 ? "1 character" : $"{count.ToString("N0", provider)} characters";
    }

    /// <summary>"56 selected" — shown only while a selection exists.</summary>
    public static string FormatSelectionCount(int count, IFormatProvider? provider = null)
    {
        provider ??= CultureInfo.CurrentCulture;
        return $"{count.ToString("N0", provider)} selected";
    }
}
