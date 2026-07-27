using System.IO;
using System.Text.Json;

namespace MDEdit.Services;

internal sealed class AppSettings
{
    public bool WordWrap { get; set; }
    public bool ShowLineNumbers { get; set; } = true;
    // "Light", "Dark", or "System" — parsed leniently by ThemeService.Parse.
    public string Theme { get; set; } = "System";
    // Live-preview ("WYSIWYG") editor mode toggle — see the View > Editor Mode menu.
    public bool LivePreview { get; set; }
    // Most-recently-used file paths, newest first — see the File > Recent Files menu.
    // Kept in shape by Editing/RecentFiles, which also sanitizes it on load.
    public List<string> RecentFiles { get; set; } = [];
    // The release ("Major.Minor.Build") ReleaseNotes.md was last auto-shown for — see
    // MainWindow.MaybeShowReleaseNotesOnFirstRun and Editing/ReleaseNotesGate. Empty means never shown.
    public string LastReleaseNotesVersionShown { get; set; } = "";
    // How many characters the status bar's character count charges per line break — 0, 1, or 2
    // (Requirements.md §9). Defaults to 2 so an upgrading user's displayed count doesn't change:
    // that was the only behavior before this setting existed (Editor.Document.TextLength counts
    // a CRLF literally). See Editing/CharacterCounter.
    public int LineBreakCharWeight { get; set; } = 2;
    // WYSIWYG/code fonts and formatted-span colors (Requirements.md §6, View > Preferences).
    // See MainWindow.ApplyEditorPreferences.
    public EditorPreferences EditorPreferences { get; set; } = new();
}

/// <summary>
/// User-customizable fonts and per-construct colors (Requirements.md §6). Every default below is
/// copied exactly from what was previously hardcoded in <c>MarkdownLineColorizer</c>,
/// <c>Resources/Markdown.xshd</c> (plus <c>MainWindow.DarkHighlightColors</c> for the dark half),
/// <c>BlockquoteAccentBarRenderer</c>, and <c>HorizontalRuleRenderer</c> — so a fresh or upgrading
/// install renders identically to before this setting existed, until the user opens Preferences.
/// Colors are hex strings (e.g. "#0057AE"), parsed via <c>ColorConverter.ConvertFromString</c>.
/// Only constructs that already had an assigned color are customizable — Bold/Italic/BoldItalic
/// (weight/style only) and Underline (a decoration, not a color) have nothing to customize, and
/// table grid lines/header shading are structural chrome, not a formatted span.
/// </summary>
internal sealed class EditorPreferences
{
    public string WysiwygFontFamily { get; set; } = "Arial";
    public string CodeFontFamily { get; set; } = "Cascadia Code, Consolas, Courier New";

    public string HeadingColorLight { get; set; } = "#0057AE";
    public string HeadingColorDark { get; set; } = "#58A6FF";

    // Also drives BlockquoteAccentBarRenderer's bar — one color for both, as today.
    public string BlockquoteColorLight { get; set; } = "#6A737D";
    public string BlockquoteColorDark { get; set; } = "#8B949E";

    // Also drives the table's structural pipe/delimiter-row dimming — shared, as today.
    public string HorizontalRuleColorLight { get; set; } = "#BBBBBB";
    public string HorizontalRuleColorDark { get; set; } = "#484F58";

    // Background only — highlighted text keeps the editor's standard foreground, as today.
    public string HighlightBackgroundLight { get; set; } = "#F5E7A3";
    public string HighlightBackgroundDark { get; set; } = "#6A5E2E";

    public string StrikethroughColorLight { get; set; } = "#888888";
    public string StrikethroughColorDark { get; set; } = "#8B949E";

    // Shared by inline code and fenced code blocks, as today.
    public string CodeForegroundLight { get; set; } = "#C7254E";
    public string CodeForegroundDark { get; set; } = "#FF7B72";
    public string CodeBackgroundLight { get; set; } = "#FEF2F2";
    public string CodeBackgroundDark { get; set; } = "#30363D";

    public string LinkColorLight { get; set; } = "#0065BD";
    public string LinkColorDark { get; set; } = "#58A6FF";

    public string ListMarkerColorLight { get; set; } = "#005CC5";
    public string ListMarkerColorDark { get; set; } = "#79C0FF";

    // HTML comments.
    public string CommentColorLight { get; set; } = "#888888";
    public string CommentColorDark { get; set; } = "#8B949E";
}

internal static class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MDEdit", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Missing, corrupt, or unreadable — fall back to defaults rather than fail startup.
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings));
    }
}
