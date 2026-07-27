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
