using System.Text.Json;
using MDEdit.Editing;
using MDEdit.Services;

namespace MDEdit.Tests;

// Covers the one-time fold of a pre-per-element settings.json (the flat, mode-independent font and
// colour properties) into the two ModeStyles sets. An upgrading user's customizations surviving is
// the whole point: silently reverting someone's palette to the defaults on upgrade would look
// exactly like the app forgetting their settings, and nothing else would catch it.
public class EditorPreferencesMigrationTests
{
    // A v1 file: no Version, no Wysiwyg/Source, every value customized away from its default so a
    // migration that quietly wrote defaults instead would fail rather than coincidentally pass.
    private const string LegacyJson = """
    {
      "EditorPreferences": {
        "WysiwygFontFamily": "Georgia",
        "CodeFontFamily": "Fira Code",
        "HeadingColorLight": "#111111",
        "HeadingColorDark": "#AAAAAA",
        "BlockquoteColorLight": "#222222",
        "BlockquoteColorDark": "#BBBBBB",
        "HorizontalRuleColorLight": "#333333",
        "HorizontalRuleColorDark": "#CCCCCC",
        "HighlightBackgroundLight": "#444444",
        "HighlightBackgroundDark": "#DDDDDD",
        "StrikethroughColorLight": "#555555",
        "StrikethroughColorDark": "#EEEEEE",
        "CodeForegroundLight": "#666666",
        "CodeForegroundDark": "#FFFFFF",
        "CodeBackgroundLight": "#777777",
        "CodeBackgroundDark": "#101010",
        "LinkColorLight": "#888888",
        "LinkColorDark": "#202020",
        "ListMarkerColorLight": "#999999",
        "ListMarkerColorDark": "#303030",
        "CommentColorLight": "#0A0A0A",
        "CommentColorDark": "#404040"
      }
    }
    """;

    private static EditorPreferences MigratedLegacy()
        => SettingsService.Deserialize(LegacyJson).EditorPreferences;

    [Fact]
    public void LegacyFile_IsStampedWithTheCurrentVersion()
    {
        Assert.Equal(EditorPreferences.CurrentVersion, MigratedLegacy().Version);
    }

    [Fact]
    public void LegacyColors_ReachBothModes()
    {
        // Colours were mode-independent before this version, so both sets must carry them —
        // migrating only one would silently reset the user's palette in the other editor mode.
        var prefs = MigratedLegacy();

        foreach (var mode in new[] { prefs.Wysiwyg, prefs.Source })
        {
            var e = mode.Elements;

            for (int level = 1; level <= 6; level++)
            {
                Assert.Equal("#111111", e[StyledElements.Heading(level)].ForegroundLight);
                Assert.Equal("#AAAAAA", e[StyledElements.Heading(level)].ForegroundDark);
            }

            Assert.Equal("#222222", e[StyledElements.Blockquote].ForegroundLight);
            Assert.Equal("#BBBBBB", e[StyledElements.Blockquote].ForegroundDark);

            Assert.Equal("#333333", e[StyledElements.HorizontalRule].ForegroundLight);
            Assert.Equal("#CCCCCC", e[StyledElements.HorizontalRule].ForegroundDark);

            Assert.Equal("#444444", e[StyledElements.Highlight].BackgroundLight);
            Assert.Equal("#DDDDDD", e[StyledElements.Highlight].BackgroundDark);

            Assert.Equal("#555555", e[StyledElements.Strikethrough].ForegroundLight);
            Assert.Equal("#EEEEEE", e[StyledElements.Strikethrough].ForegroundDark);

            Assert.Equal("#888888", e[StyledElements.Link].ForegroundLight);
            Assert.Equal("#202020", e[StyledElements.Link].ForegroundDark);

            Assert.Equal("#999999", e[StyledElements.ListMarker].ForegroundLight);
            Assert.Equal("#303030", e[StyledElements.ListMarker].ForegroundDark);

            Assert.Equal("#0A0A0A", e[StyledElements.Comment].ForegroundLight);
            Assert.Equal("#404040", e[StyledElements.Comment].ForegroundDark);

            // One legacy palette drove both code elements; they become independently editable but
            // must start out identical.
            foreach (var key in new[] { StyledElements.InlineCode, StyledElements.CodeBlock })
            {
                Assert.Equal("#666666", e[key].ForegroundLight);
                Assert.Equal("#FFFFFF", e[key].ForegroundDark);
                Assert.Equal("#777777", e[key].BackgroundLight);
                Assert.Equal("#101010", e[key].BackgroundDark);
            }
        }
    }

    [Fact]
    public void LegacyFonts_LandInTheirThreeRoles()
    {
        // WysiwygFontFamily was the WYSIWYG base. CodeFontFamily did double duty: the source-mode
        // base font, and the family pinned on code spans so they stayed fixed-width once WYSIWYG
        // flipped the base. All three roles have to survive.
        var prefs = MigratedLegacy();

        Assert.Equal("Georgia", prefs.Wysiwyg.BaseFontFamily);
        Assert.Equal("Fira Code", prefs.Source.BaseFontFamily);
        Assert.Equal("Fira Code", prefs.Wysiwyg.Elements[StyledElements.InlineCode].FontFamily);
        Assert.Equal("Fira Code", prefs.Wysiwyg.Elements[StyledElements.CodeBlock].FontFamily);
    }

    [Fact]
    public void Migration_LeavesSourceModeAllMono()
    {
        // The source set's code elements must inherit the mono base rather than pin a family —
        // pinning one here would be harmless today and wrong the moment the base font changes.
        foreach (var (key, style) in MigratedLegacy().Source.Elements)
            Assert.True(style.FontFamily is null, $"'{key}' pins a font family in source mode.");
    }

    [Fact]
    public void Migration_LeavesHeadingScalingAlone()
    {
        // Scaling never existed as a setting, so it must come from the new defaults untouched:
        // WYSIWYG keeps the old HeadingScale table, source stays unscaled.
        var prefs = MigratedLegacy();

        Assert.Equal(1.6, prefs.Wysiwyg.Elements[StyledElements.Heading1].FontScale);
        Assert.Null(prefs.Source.Elements[StyledElements.Heading1].FontScale);
    }

    [Fact]
    public void LegacyFileWithDefaultValues_MigratesToExactlyTheDefaultTables()
    {
        // The upgrade path most users take: never opened Preferences, so the old file carries the
        // old defaults verbatim. The result has to be indistinguishable from a fresh install, or the
        // first launch after updating would render differently for no reason. This is what proves
        // the values were carried across to their new home without drifting.
        var untouched = """
        {
          "EditorPreferences": {
            "WysiwygFontFamily": "Arial",
            "CodeFontFamily": "Cascadia Code, Consolas, Courier New",
            "HeadingColorLight": "#0057AE",   "HeadingColorDark": "#58A6FF",
            "BlockquoteColorLight": "#6A737D", "BlockquoteColorDark": "#8B949E",
            "HorizontalRuleColorLight": "#BBBBBB", "HorizontalRuleColorDark": "#484F58",
            "HighlightBackgroundLight": "#F5E7A3", "HighlightBackgroundDark": "#6A5E2E",
            "StrikethroughColorLight": "#888888", "StrikethroughColorDark": "#8B949E",
            "CodeForegroundLight": "#C7254E", "CodeForegroundDark": "#FF7B72",
            "CodeBackgroundLight": "#FEF2F2", "CodeBackgroundDark": "#30363D",
            "LinkColorLight": "#0065BD", "LinkColorDark": "#58A6FF",
            "ListMarkerColorLight": "#005CC5", "ListMarkerColorDark": "#79C0FF",
            "CommentColorLight": "#888888", "CommentColorDark": "#8B949E"
          }
        }
        """;

        var migrated = SettingsService.Deserialize(untouched).EditorPreferences;
        var fresh = new EditorPreferences();

        Assert.Equal(JsonSerializer.Serialize(fresh.Wysiwyg), JsonSerializer.Serialize(migrated.Wysiwyg));
        Assert.Equal(JsonSerializer.Serialize(fresh.Source), JsonSerializer.Serialize(migrated.Source));
    }

    [Fact]
    public void UntouchedLegacyValues_AdoptDefaultsChangedSinceThatVersion()
    {
        // The reason migration compares against the v1 defaults instead of copying blindly. The
        // code palette changed after per-element styling shipped; a user who never customized it
        // must land on the new palette, not stay pinned to the old one with nothing in the UI to
        // explain why they look different from a fresh install.
        var json = """
        {
          "EditorPreferences": {
            "CodeForegroundLight": "#C7254E", "CodeForegroundDark": "#FF7B72",
            "CodeBackgroundLight": "#FEF2F2", "CodeBackgroundDark": "#30363D"
          }
        }
        """;

        var code = SettingsService.Deserialize(json).EditorPreferences.Source.Elements[StyledElements.InlineCode];

        Assert.Equal("#00FF33", code.ForegroundLight);
        Assert.Equal("#000000", code.BackgroundLight);
    }

    [Fact]
    public void CustomizedLegacyValues_StillWinOverChangedDefaults()
    {
        // The other side of the same rule: a value the user actually changed is theirs and survives,
        // even for an element whose default moved underneath it.
        var json = """
        { "EditorPreferences": { "CodeForegroundLight": "#ABCDEF" } }
        """;

        var code = SettingsService.Deserialize(json).EditorPreferences.Source.Elements[StyledElements.InlineCode];

        Assert.Equal("#ABCDEF", code.ForegroundLight);
        // Untouched half of the same element still moves to the new default.
        Assert.Equal("#000000", code.BackgroundLight);
    }

    [Fact]
    public void Migration_ClearsTheLegacyPropertiesSoTheyLeaveTheFile()
    {
        // Nulled once folded, and JsonIgnore drops them on the next save. Leaving them populated
        // would be worse than untidy: anything that later re-ran the fold would overwrite genuine
        // per-element choices with these stale values.
        var prefs = MigratedLegacy();

        Assert.Null(prefs.WysiwygFontFamily);
        Assert.Null(prefs.CodeFontFamily);
        Assert.Null(prefs.HeadingColorLight);
        Assert.Null(prefs.CommentColorDark);
        Assert.DoesNotContain("HeadingColorLight", JsonSerializer.Serialize(prefs));
    }

    [Fact]
    public void PartialLegacyFile_LeavesTheUnmentionedHalfAlone()
    {
        // A hand-edited file can carry one half of a colour pair. Writing the missing half through
        // as null would turn a colour the user never touched into an inherit.
        var json = """
        { "EditorPreferences": { "LinkColorLight": "#123456" } }
        """;

        var prefs = SettingsService.Deserialize(json).EditorPreferences;

        Assert.Equal("#123456", prefs.Wysiwyg.Elements[StyledElements.Link].ForegroundLight);
        Assert.Equal("#58A6FF", prefs.Wysiwyg.Elements[StyledElements.Link].ForegroundDark);
    }

    [Fact]
    public void CurrentVersionFile_IsLeftCompletelyAlone()
    {
        // The legacy properties keep their defaults forever in a migrated file. Re-running the fold
        // would overwrite real per-element choices with those stale defaults — so the version guard
        // is what protects every customization made after upgrading.
        var prefs = new EditorPreferences { Version = EditorPreferences.CurrentVersion }; // as Save stamps it
        prefs.Wysiwyg.Elements[StyledElements.Link].ForegroundLight = "#ABCDEF";
        prefs.Source.Elements[StyledElements.Heading1].FontWeight = ElementStyle.WeightNormal;
        var json = JsonSerializer.Serialize(new AppSettings { EditorPreferences = prefs });

        var reloaded = SettingsService.Deserialize(json).EditorPreferences;

        Assert.Equal("#ABCDEF", reloaded.Wysiwyg.Elements[StyledElements.Link].ForegroundLight);
        Assert.Equal(ElementStyle.WeightNormal, reloaded.Source.Elements[StyledElements.Heading1].FontWeight);
    }

    [Fact]
    public void Migration_IsIdempotent()
    {
        var once = MigratedLegacy();
        var twice = MigratedLegacy();
        twice.Migrate();

        Assert.Equal(JsonSerializer.Serialize(once), JsonSerializer.Serialize(twice));
    }

    [Fact]
    public void MissingElementEntry_IsRecreatedRatherThanDroppingTheColor()
    {
        // settings.json is hand-editable, so a v1 file can arrive with an element entry deleted.
        // The migrated colour has to land somewhere rather than being silently discarded.
        var json = """
        {
          "EditorPreferences": {
            "LinkColorLight": "#123456",
            "LinkColorDark": "#654321",
            "Wysiwyg": { "BaseFontFamily": "Arial", "BaseFontSize": 14, "Elements": {} },
            "Source": { "BaseFontFamily": "Consolas", "BaseFontSize": 14, "Elements": {} }
          }
        }
        """;

        var prefs = SettingsService.Deserialize(json).EditorPreferences;

        Assert.Equal("#123456", prefs.Wysiwyg.Elements[StyledElements.Link].ForegroundLight);
        Assert.Equal("#654321", prefs.Source.Elements[StyledElements.Link].ForegroundDark);
    }

    [Fact]
    public void NullEditorPreferences_FallsBackToDefaultsInsteadOfThrowing()
    {
        // An explicit null deserializes despite the non-nullable declaration, and would NRE out of
        // Load's catch filter — failing startup rather than falling back the way a corrupt file does.
        var prefs = SettingsService.Deserialize("""{ "EditorPreferences": null }""").EditorPreferences;

        Assert.Equal("Arial", prefs.Wysiwyg.BaseFontFamily);
    }
}
