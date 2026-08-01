using MDEdit.Editing;
using MDEdit.Services;

namespace MDEdit.Tests;

// Pins EditorPreferences' defaults to exactly what was hardcoded before this setting existed —
// MarkdownLineColorizer's brushes, Resources/Markdown.xshd, MainWindow.DarkHighlightColors,
// BlockquoteAccentBarRenderer, and HorizontalRuleRenderer — so a fresh or upgrading install keeps
// rendering identically. A silently drifted default here would be a visual regression nothing
// else would catch.
public class EditorPreferencesTests
{
    [Fact]
    public void LegacyProperties_AreNullOnAFreshObject()
    {
        // They are migration input only — a settings.json written before per-element styling. A
        // fresh object must have nothing to fold, or every new install would run the migration and
        // overwrite its own defaults. What they used to hold is now pinned by
        // SourceDefaults_MatchPreviouslyHardcodedValues, in the one place it now lives.
        var prefs = new EditorPreferences();

        Assert.Null(prefs.WysiwygFontFamily);
        Assert.Null(prefs.CodeFontFamily);
        Assert.Null(prefs.HeadingColorLight);
        Assert.Null(prefs.CodeBackgroundDark);
        Assert.Null(prefs.CommentColorDark);
    }

    [Fact]
    public void Version_IsUnstampedUntilSaved()
    {
        // Version records the schema a settings.json was WRITTEN with, and SettingsService.Save
        // stamps it. It deliberately has no initializer: System.Text.Json leaves an initializer in
        // place for a property absent from the JSON, so defaulting this to CurrentVersion would
        // make every pre-per-element file claim to be migrated already and skip the fold entirely.
        Assert.Equal(2, EditorPreferences.CurrentVersion);
        Assert.Equal(0, new EditorPreferences().Version);
    }

    // ── Base fonts ───────────────────────────────────────────────────────────

    [Fact]
    public void SourceDefaults_UseTheMonoStackAtTheEditorsOwnSize()
    {
        var source = new EditorPreferences().Source;

        Assert.Equal("Cascadia Code, Consolas, Courier New", source.BaseFontFamily);
        Assert.Equal(14.0, source.BaseFontSize);
    }

    [Fact]
    public void WysiwygDefaults_UseTheProseFontAtTheEditorsOwnSize()
    {
        var wysiwyg = new EditorPreferences().Wysiwyg;

        Assert.Equal("Arial", wysiwyg.BaseFontFamily);
        Assert.Equal(14.0, wysiwyg.BaseFontSize);
    }

    [Fact]
    public void BothModes_DefineEveryCatalogedElement()
    {
        var prefs = new EditorPreferences();

        foreach (var element in StyledElements.All)
        {
            Assert.True(prefs.Source.Elements.ContainsKey(element.Key),
                $"Source is missing a default for '{element.Key}'.");
            Assert.True(prefs.Wysiwyg.Elements.ContainsKey(element.Key),
                $"Wysiwyg is missing a default for '{element.Key}'.");
        }
    }

    [Fact]
    public void Normal_IsTheFirstElement()
    {
        // Ordering is the point of it: body text is what every other element inherits from, so
        // Preferences puts it at the top where the default sample is visible at a glance.
        Assert.Equal(StyledElements.Normal, StyledElements.All[0].Key);
        Assert.Equal("Normal", StyledElements.All[0].Label);
    }

    [Fact]
    public void Normal_IsNotAnXshdElement()
    {
        // Body text is the editor control's own font properties, not a highlighting colour — so
        // MarkdownHighlighting must not go looking for a <Color> to fill in for it.
        Assert.Null(StyledElements.All.Single(e => e.Key == StyledElements.Normal).XshdColorName);
    }

    [Fact]
    public void NormalDefaults_OverrideNothing()
    {
        // Its family and size are the mode's base rather than fields of the style, and nothing else
        // is set — which is what keeps an unmodified install rendering body text exactly as it did
        // before this element existed.
        var prefs = new EditorPreferences();

        AssertStyle(prefs.Source.Elements[StyledElements.Normal]);
        AssertStyle(prefs.Wysiwyg.Elements[StyledElements.Normal]);
    }

    [Fact]
    public void BothModes_DefineNothingOutsideTheCatalog()
    {
        // A key here that StyledElements doesn't list would be unreachable from Preferences and
        // invisible to every consumer — dead settings nobody could ever change back.
        var known = StyledElements.All.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var prefs = new EditorPreferences();

        Assert.DoesNotContain(prefs.Source.Elements.Keys, k => !known.Contains(k));
        Assert.DoesNotContain(prefs.Wysiwyg.Elements.Keys, k => !known.Contains(k));
    }

    // ── Source-mode element defaults ─────────────────────────────────────────
    // Every field not named in an AssertStyle call is asserted null, i.e. "inherits" — so this also
    // pins that nothing pins a font family or size in source mode, which is what keeps it all-mono.

    [Fact]
    public void SourceDefaults_MatchPreviouslyHardcodedValues()
    {
        var e = new EditorPreferences().Source.Elements;

        AssertStyle(e[StyledElements.Heading1], weight: "Bold", fgLight: "#0057AE", fgDark: "#58A6FF");
        AssertStyle(e[StyledElements.Heading2], weight: "Bold", fgLight: "#0057AE", fgDark: "#58A6FF");
        AssertStyle(e[StyledElements.Heading3], weight: "Bold", fgLight: "#0057AE", fgDark: "#58A6FF");
        AssertStyle(e[StyledElements.Heading4], weight: "SemiBold", fgLight: "#0057AE", fgDark: "#58A6FF");
        AssertStyle(e[StyledElements.Heading5], weight: "SemiBold", fgLight: "#0057AE", fgDark: "#58A6FF");
        AssertStyle(e[StyledElements.Heading6], weight: "SemiBold", fgLight: "#0057AE", fgDark: "#58A6FF");

        // Weight is pinned Normal on both, not left to inherit: the old ColorLine calls forced it,
        // so "> **bold**" rendered at normal weight throughout. Leaving these null would let the
        // emphasis survive — better, arguably, but a visible change to existing documents.
        AssertStyle(e[StyledElements.Blockquote], weight: "Normal", italic: true, fgLight: "#6A737D", fgDark: "#8B949E");
        AssertStyle(e[StyledElements.HorizontalRule], weight: "Normal", fgLight: "#BBBBBB", fgDark: "#484F58");

        AssertStyle(e[StyledElements.Bold], weight: "Bold");
        AssertStyle(e[StyledElements.Italic], italic: true);
        AssertStyle(e[StyledElements.BoldItalic], weight: "Bold", italic: true);

        // Grey, not struck — Decoration stays null, matching how strikethrough has always rendered.
        AssertStyle(e[StyledElements.Strikethrough], fgLight: "#888888", fgDark: "#8B949E");

        AssertStyle(e[StyledElements.Highlight], bgLight: "#F5E7A3", bgDark: "#6A5E2E");
        AssertStyle(e[StyledElements.Underline], decoration: "Underline");

        AssertStyle(e[StyledElements.InlineCode],
            fgLight: "#C7254E", fgDark: "#FF7B72", bgLight: "#FEF2F2", bgDark: "#30363D");
        AssertStyle(e[StyledElements.CodeBlock],
            fgLight: "#C7254E", fgDark: "#FF7B72", bgLight: "#FEF2F2", bgDark: "#30363D");

        AssertStyle(e[StyledElements.Link], fgLight: "#0065BD", fgDark: "#58A6FF");
        AssertStyle(e[StyledElements.ListMarker], fgLight: "#005CC5", fgDark: "#79C0FF");
        AssertStyle(e[StyledElements.Comment], italic: true, fgLight: "#888888", fgDark: "#8B949E");
    }

    [Fact]
    public void SourceDefaults_PinNoFamilyOrScaleAnywhere()
    {
        // Source mode is all-mono and unscaled by design: everything inherits the base, so a family
        // or scale appearing here would be the regression that quietly ends that.
        foreach (var (key, style) in new EditorPreferences().Source.Elements)
        {
            Assert.True(style.FontFamily is null, $"'{key}' pins a font family in source mode.");
            Assert.True(style.FontScale is null, $"'{key}' pins a font scale in source mode.");
        }
    }

    // ── WYSIWYG deltas ───────────────────────────────────────────────────────

    [Fact]
    public void WysiwygDefaults_ScaleHeadingsLikeTheOldHeadingScaleTable()
    {
        var e = new EditorPreferences().Wysiwyg.Elements;

        Assert.Equal(1.6, e[StyledElements.Heading1].FontScale);
        Assert.Equal(1.4, e[StyledElements.Heading2].FontScale);
        Assert.Equal(1.25, e[StyledElements.Heading3].FontScale);
        Assert.Equal(1.15, e[StyledElements.Heading4].FontScale);
        Assert.Equal(1.05, e[StyledElements.Heading5].FontScale);
        // Level 6 was HeadingScale's default arm at 1.0, expressed here as "inherit".
        Assert.Null(e[StyledElements.Heading6].FontScale);
    }

    [Fact]
    public void WysiwygDefaults_KeepCodeFixedWidthAgainstTheProseBase()
    {
        var e = new EditorPreferences().Wysiwyg.Elements;

        Assert.Equal("Cascadia Code, Consolas, Courier New", e[StyledElements.InlineCode].FontFamily);
        Assert.Equal("Cascadia Code, Consolas, Courier New", e[StyledElements.CodeBlock].FontFamily);
    }

    [Fact]
    public void WysiwygDefaults_DifferFromSourceOnlyByHeadingScaleAndCodeFamily()
    {
        // The whole point of the two-mode model is that the modes start identical apart from a
        // named, reviewable set of deltas. Any other divergence between the two default tables is a
        // mistake, and this is the test that says so rather than leaving it to a reader.
        var prefs = new EditorPreferences();
        var scaled = new[]
        {
            StyledElements.Heading1, StyledElements.Heading2, StyledElements.Heading3,
            StyledElements.Heading4, StyledElements.Heading5,
        };
        var refamilied = new[] { StyledElements.InlineCode, StyledElements.CodeBlock };

        foreach (var element in StyledElements.All)
        {
            var source = prefs.Source.Elements[element.Key];
            var wysiwyg = prefs.Wysiwyg.Elements[element.Key];

            Assert.Equal(source.FontWeight, wysiwyg.FontWeight);
            Assert.Equal(source.Italic, wysiwyg.Italic);
            Assert.Equal(source.Decoration, wysiwyg.Decoration);
            Assert.Equal(source.ForegroundLight, wysiwyg.ForegroundLight);
            Assert.Equal(source.ForegroundDark, wysiwyg.ForegroundDark);
            Assert.Equal(source.BackgroundLight, wysiwyg.BackgroundLight);
            Assert.Equal(source.BackgroundDark, wysiwyg.BackgroundDark);

            if (!scaled.Contains(element.Key))
                Assert.Equal(source.FontScale, wysiwyg.FontScale);
            if (!refamilied.Contains(element.Key))
                Assert.Equal(source.FontFamily, wysiwyg.FontFamily);
        }
    }

    [Fact]
    public void ModeDefaults_AreIndependentInstances()
    {
        // The two tables are built from one shared factory; if it ever returned shared ElementStyle
        // instances, editing a color in one tab would silently change the other tab too.
        var prefs = new EditorPreferences();

        prefs.Wysiwyg.Elements[StyledElements.Link].ForegroundLight = "#123456";

        Assert.Equal("#0065BD", prefs.Source.Elements[StyledElements.Link].ForegroundLight);
    }

    private static void AssertStyle(
        ElementStyle style,
        string? family = null,
        double? scale = null,
        string? weight = null,
        bool? italic = null,
        string? decoration = null,
        string? fgLight = null,
        string? fgDark = null,
        string? bgLight = null,
        string? bgDark = null)
    {
        Assert.Equal(family, style.FontFamily);
        Assert.Equal(scale, style.FontScale);
        Assert.Equal(weight, style.FontWeight);
        Assert.Equal(italic, style.Italic);
        Assert.Equal(decoration, style.Decoration);
        Assert.Equal(fgLight, style.ForegroundLight);
        Assert.Equal(fgDark, style.ForegroundDark);
        Assert.Equal(bgLight, style.BackgroundLight);
        Assert.Equal(bgDark, style.BackgroundDark);
    }
}
