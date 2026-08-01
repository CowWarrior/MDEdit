using MDEdit.Services;

namespace MDEdit.Tests;

public class AppSettingsTests
{
    // A settings.json written by a version predating a property simply lacks it, so every
    // default here is also what an upgrading user gets. EditorPreferencesTests pins the nested
    // presentation defaults; this pins the top-level ones.

    // Its own named test on purpose: this is the remote-image opt-in invariant. Remote fetching
    // must never become the default — not for a fresh install, and not for a settings.json
    // written before the property existed. A failure here is a privacy regression, not cosmetic
    // default drift.
    [Fact]
    public void LoadRemoteImages_DefaultsToOff()
    {
        Assert.False(new AppSettings().LoadRemoteImages);
    }

    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var settings = new AppSettings();

        Assert.False(settings.WordWrap);
        Assert.True(settings.ShowLineNumbers);
        Assert.Equal("System", settings.Theme);
        Assert.False(settings.LivePreview);
        Assert.NotNull(settings.RecentFiles);
        Assert.Empty(settings.RecentFiles);
        Assert.Equal("", settings.LastReleaseNotesVersionShown);
        Assert.Equal(2, settings.LineBreakCharWeight);
        Assert.NotNull(settings.EditorPreferences);
    }

    // The whole recovery story for a settings.json written before per-element styling, now that the
    // one-time fold has been removed: its flat properties are unrecognized, System.Text.Json ignores
    // them, and the file loads onto current defaults. If that ever stopped holding — an added
    // [JsonExtensionData] sink, or UnmappedMemberHandling.Disallow — an upgrading user would get a
    // JsonException instead, which Load catches into a *total* reset of every setting, not just the
    // presentation ones. Cheap to pin, silent and broad if it breaks.
    [Fact]
    public void PrePerElementFile_LoadsOntoCurrentDefaults()
    {
        // Shape of a real pre-per-element file: top-level settings the current build still reads,
        // alongside EditorPreferences carrying only the flat properties that no longer exist.
        // PascalCase deliberately — SettingsService.Save serializes with no JsonSerializerOptions,
        // so that is what a real file looks like, and deserialization is case-sensitive by default.
        const string json = """
            {
              "WordWrap": true,
              "Theme": "Dark",
              "EditorPreferences": {
                "WysiwygFontFamily": "Georgia",
                "CodeFontFamily": "Consolas",
                "HeadingColorLight": "#123456",
                "CodeBackgroundDark": "#654321"
              }
            }
            """;

        var settings = SettingsService.Deserialize(json);

        // Recognized settings still load.
        Assert.True(settings.WordWrap);
        Assert.Equal("Dark", settings.Theme);

        // The dropped ones neither throw nor leak through: presentation is at current defaults, and
        // the file is still stamped 0 so a future migration can tell it was never converted.
        Assert.Equal(0, settings.EditorPreferences.Version);
        Assert.Equal("Arial", settings.EditorPreferences.Wysiwyg.BaseFontFamily);
        Assert.Equal(
            "Cascadia Code, Consolas, Courier New",
            settings.EditorPreferences.Source.BaseFontFamily);
    }

    // A hand-edited file may null the object outright; the non-nullable declaration does not stop
    // System.Text.Json producing null, and everything downstream would NRE. Load's catch filter
    // doesn't cover NRE, so this would fail startup rather than fall back.
    [Fact]
    public void ExplicitNullEditorPreferences_FallsBackToDefaults()
    {
        var settings = SettingsService.Deserialize("""{ "EditorPreferences": null }""");

        Assert.NotNull(settings.EditorPreferences);
        Assert.Equal("Arial", settings.EditorPreferences.Wysiwyg.BaseFontFamily);
    }
}
