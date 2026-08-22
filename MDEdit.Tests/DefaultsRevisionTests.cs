using System.Text.Json;
using MDEdit.Editing;
using MDEdit.Services;

namespace MDEdit.Tests;

// The defaults-revision mechanism (Requirements.md §6): a shipped default that changes must reach an
// existing settings.json, without overwriting anything the user set themselves.
//
// The whole feature turns on one distinction ElementStyle cannot express — "never set" versus "set
// to a value that happens to equal the default" — so ModeStyles.TouchedElements carries it instead.
// These tests pin both halves: that an untouched element updates, and that a touched one survives.
// A regression either way is silent (stale defaults, or discarded customization), which is exactly
// why it is worth pinning rather than reading off the code.
public class DefaultsRevisionTests
{
    private const string OldLinkBlue = "#0065BD";

    // A file this build has already written: current on both stamps, so nothing is re-applied.
    private static string CurrentFileJson(Action<AppSettings>? customize = null)
    {
        var settings = new AppSettings();
        settings.EditorPreferences.Version = EditorPreferences.CurrentVersion;
        settings.EditorPreferences.DefaultsRevision = EditorPreferences.CurrentDefaultsRevision;
        customize?.Invoke(settings);
        return JsonSerializer.Serialize(settings);
    }

    [Fact]
    public void DefaultsRevision_IsUnstampedUntilSaved()
    {
        // Same contract as Version, and for the same reason: System.Text.Json leaves a property
        // initializer in place when the property is absent from the JSON, so initializing this to
        // CurrentDefaultsRevision would make a file written before the mechanism claim to be
        // current — and it would then never receive the defaults it is missing.
        Assert.Equal(0, new EditorPreferences().DefaultsRevision);
    }

    [Fact]
    public void DefaultsRevision_DrivesTheReapplyIndependentlyOfTheSchemaVersion()
    {
        // Two counters, deliberately: the schema Version gates *inference*, DefaultsRevision gates
        // the *re-apply*. Folding them into one would mean every schema bump silently re-applied
        // defaults, and every retuned colour had to claim to be a schema change. Pinned by driving
        // the two independently — a current-schema file with a stale defaults revision must still
        // be updated, which is the combination a plain "Version < Current" check would miss.
        Assert.Equal(1, EditorPreferences.CurrentDefaultsRevision);

        string staleDefaults = CurrentFileJson(s =>
        {
            s.EditorPreferences.DefaultsRevision = 0;
            s.EditorPreferences.Wysiwyg.Style(StyledElements.Link).ForegroundLight = "#OLDVALUE";
        });
        Assert.Equal(
            OldLinkBlue,
            SettingsService.Deserialize(staleDefaults)
                .EditorPreferences.Wysiwyg.Elements[StyledElements.Link].ForegroundLight);

        // The mirror image: defaults current, schema stale. Inference runs, the re-apply does not,
        // so a value differing from the default is read as the user's and kept.
        string staleSchema = CurrentFileJson(s =>
        {
            s.EditorPreferences.Version = 2;
            s.EditorPreferences.Wysiwyg.Style(StyledElements.Link).ForegroundLight = "#OLDVALUE";
        });
        Assert.Equal(
            "#OLDVALUE",
            SettingsService.Deserialize(staleSchema)
                .EditorPreferences.Wysiwyg.Elements[StyledElements.Link].ForegroundLight);
    }

    // ── Re-applying a changed default ────────────────────────────────────────

    [Fact]
    public void StaleDefault_ReachesAnElementTheUserNeverSet()
    {
        // A file carrying a superseded value for an element it never marked as touched: exactly the
        // shape that used to be unreachable, because the file's own value won forever.
        string json = CurrentFileJson(s =>
        {
            s.EditorPreferences.DefaultsRevision = 0; // predates this build's defaults
            s.EditorPreferences.Wysiwyg.Style(StyledElements.Link).ForegroundLight = "#OLDVALUE";
        });

        var loaded = SettingsService.Deserialize(json);

        Assert.Equal(OldLinkBlue, loaded.EditorPreferences.Wysiwyg.Elements[StyledElements.Link].ForegroundLight);
        Assert.Equal(EditorPreferences.CurrentDefaultsRevision, loaded.EditorPreferences.DefaultsRevision);
    }

    [Fact]
    public void StaleDefault_LeavesAnElementTheUserSetAlone()
    {
        const string chosen = "#FF00FF";
        string json = CurrentFileJson(s =>
        {
            s.EditorPreferences.DefaultsRevision = 0;
            s.EditorPreferences.Wysiwyg.Style(StyledElements.Link).ForegroundLight = chosen;
            s.EditorPreferences.Wysiwyg.TouchedElements.Add(StyledElements.Link);
        });

        var loaded = SettingsService.Deserialize(json);

        Assert.Equal(chosen, loaded.EditorPreferences.Wysiwyg.Elements[StyledElements.Link].ForegroundLight);
        // The mark itself has to survive the round trip, or the value is protected exactly once.
        Assert.Contains(StyledElements.Link, loaded.EditorPreferences.Wysiwyg.TouchedElements);
    }

    [Fact]
    public void ReapplyingDefaults_UpdatesTheBaseFontOnlyWhileNormalIsUntouched()
    {
        // Normal's family and size live on the mode rather than in its ElementStyle, so they are the
        // one part of the model where the touch mark has to reach outside the Elements dictionary.
        var untouched = ModeStyles.WysiwygDefaults();
        untouched.BaseFontFamily = "Comic Sans MS";
        untouched.ReapplyUntouchedDefaults(ModeStyles.WysiwygDefaults());
        Assert.Equal("Arial", untouched.BaseFontFamily);

        var touched = ModeStyles.WysiwygDefaults();
        touched.BaseFontFamily = "Comic Sans MS";
        touched.TouchedElements.Add(StyledElements.Normal);
        touched.ReapplyUntouchedDefaults(ModeStyles.WysiwygDefaults());
        Assert.Equal("Comic Sans MS", touched.BaseFontFamily);
    }

    [Fact]
    public void AlreadyCurrentFile_IsLeftCompletelyAlone()
    {
        // The common case by far: nothing to do, and in particular no re-apply that would quietly
        // undo a customization whose touch mark went missing for any reason.
        const string chosen = "#123456";
        string json = CurrentFileJson(s =>
            s.EditorPreferences.Wysiwyg.Style(StyledElements.Link).ForegroundLight = chosen);

        var loaded = SettingsService.Deserialize(json);

        Assert.Equal(chosen, loaded.EditorPreferences.Wysiwyg.Elements[StyledElements.Link].ForegroundLight);
    }

    // ── Upgrading a file written before touch tracking ───────────────────────

    [Fact]
    public void PreTouchTrackingFile_KeepsEveryCustomizationItCarries()
    {
        // THE case this mechanism must never get wrong. Such a file deserializes with an empty
        // touched set, which reads as "the user has never set anything" — so re-applying defaults
        // without inferring first would wipe every customization in it. Version is what tells the
        // two apart, and this is the use the stamp was kept for when Migrate was deleted.
        const string chosen = "#FF00FF";
        string json = CurrentFileJson(s =>
        {
            s.EditorPreferences.Version = 2;          // per-element, pre-touch-tracking
            s.EditorPreferences.DefaultsRevision = 0;
            s.EditorPreferences.Wysiwyg.Style(StyledElements.Link).ForegroundLight = chosen;
            s.EditorPreferences.Source.BaseFontFamily = "Consolas";
        });

        var loaded = SettingsService.Deserialize(json);

        Assert.Equal(chosen, loaded.EditorPreferences.Wysiwyg.Elements[StyledElements.Link].ForegroundLight);
        Assert.Equal("Consolas", loaded.EditorPreferences.Source.BaseFontFamily);
        // Inferred, so it stays protected against every future revision rather than just this one.
        Assert.Contains(StyledElements.Link, loaded.EditorPreferences.Wysiwyg.TouchedElements);
        Assert.Contains(StyledElements.Normal, loaded.EditorPreferences.Source.TouchedElements);
    }

    [Fact]
    public void PreTouchTrackingFile_LeavesUntouchedElementsUnmarked()
    {
        // The other half: inference must not mark everything, or such a file would be frozen at its
        // current values forever and gain nothing from the mechanism.
        string json = CurrentFileJson(s =>
        {
            s.EditorPreferences.Version = 2;
            s.EditorPreferences.DefaultsRevision = 0;
            s.EditorPreferences.Wysiwyg.Style(StyledElements.Link).ForegroundLight = "#FF00FF";
        });

        var loaded = SettingsService.Deserialize(json);
        var touched = loaded.EditorPreferences.Wysiwyg.TouchedElements;

        Assert.Contains(StyledElements.Link, touched);
        Assert.DoesNotContain(StyledElements.Heading1, touched);
        Assert.DoesNotContain(StyledElements.CodeBlock, touched);
    }

    [Fact]
    public void PrePerElementFile_LandsOnCurrentDefaultsWithNothingMarked()
    {
        // The oldest shape still in the wild: flat properties System.Text.Json ignores entirely, so
        // there is nothing to infer from and everything should read as untouched.
        const string json = """
            {
              "EditorPreferences": {
                "WysiwygFontFamily": "Georgia",
                "HeadingColorLight": "#123456"
              }
            }
            """;

        var loaded = SettingsService.Deserialize(json);

        Assert.Empty(loaded.EditorPreferences.Wysiwyg.TouchedElements);
        Assert.Equal("Arial", loaded.EditorPreferences.Wysiwyg.BaseFontFamily);
        Assert.Equal(EditorPreferences.CurrentDefaultsRevision, loaded.EditorPreferences.DefaultsRevision);
    }

    [Fact]
    public void InferringTouches_ComparesAgainstTheModesOwnDefaults()
    {
        // WYSIWYG differs from Source by named deltas (heading scales, code families, the hyperlink
        // underline, strikethrough's grey). Comparing either mode against the wrong table would mark
        // every one of those as user-set and freeze them permanently.
        var wysiwyg = ModeStyles.WysiwygDefaults();
        wysiwyg.InferTouchedElements(ModeStyles.WysiwygDefaults());
        Assert.Empty(wysiwyg.TouchedElements);

        var source = ModeStyles.SourceDefaults();
        source.InferTouchedElements(ModeStyles.SourceDefaults());
        Assert.Empty(source.TouchedElements);
    }

    // ── Null tolerance ───────────────────────────────────────────────────────

    [Fact]
    public void HandEditedNulls_FallBackInsteadOfThrowing()
    {
        // settings.json is hand-editable and the non-nullable declarations do not stop
        // System.Text.Json producing null. An NRE here would escape Load's catch filter and fail
        // startup rather than falling back — the same hazard the EditorPreferences guard covers.
        var loaded = SettingsService.Deserialize("""
            { "EditorPreferences": { "Wysiwyg": null, "Source": { "TouchedElements": null, "Elements": null } } }
            """);

        Assert.Equal("Arial", loaded.EditorPreferences.Wysiwyg.BaseFontFamily);
        Assert.NotNull(loaded.EditorPreferences.Source.TouchedElements);
        Assert.NotNull(loaded.EditorPreferences.Source.Elements);
    }
}
