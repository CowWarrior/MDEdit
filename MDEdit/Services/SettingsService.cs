using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MDEdit.Editing;

namespace MDEdit.Services;

internal sealed class AppSettings
{
    public bool WordWrap { get; set; }
    public bool ShowLineNumbers { get; set; } = true;
    // "Light", "Dark", or "System" — parsed leniently by ThemeService.Parse.
    public string Theme { get; set; } = "System";
    // Live-preview ("WYSIWYG") editor mode toggle — see the View > Editor Mode menu.
    public bool LivePreview { get; set; }
    // Whether WYSIWYG mode fetches and renders http(s):// images (Requirements.md §5), from
    // View > Load Remote Images. Defaults to FALSE and must stay that way: fetching a remote
    // image just from having a document open discloses to its host that the document was opened —
    // the tracking-pixel concern email clients guard against. With this off MDEdit issues no
    // network request for a document's images at all; see ImageElementGenerator.RemoteEnabled
    // and RemoteImageLoader's class comment for how that is structurally guaranteed.
    // Deliberately NOT in EditorPreferences: that object is replaced wholesale by
    // PreferencesWindow's Cancel and Reset, and it is purely presentational — a privacy setting
    // must not be revertible by a dialog that never showed it.
    public bool LoadRemoteImages { get; set; }
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
    /// <summary>
    /// Schema version of the <c>settings.json</c> this came from — 0 for a file written before the
    /// stamp existed, <see cref="CurrentVersion"/> for anything this build has saved.
    /// </summary>
    /// <remarks>
    /// <b>Nothing reads this today, and it is kept deliberately.</b> The one-time fold of a
    /// pre-per-element file was removed once the affected installs stopped being worth ~500 lines
    /// (an unrecognized property is ignored by System.Text.Json, so such a file loads and lands on
    /// current defaults). What the stamp still buys is the ability to tell, later, what schema a
    /// file was written with — which any future migration or defaults-revision mechanism needs and
    /// cannot reconstruct after the fact. Dropping it would leave every file written from now on
    /// unidentifiable.
    /// <para>
    /// <b>Deliberately has no initializer, and is stamped by <see cref="SettingsService.Save"/>
    /// rather than by the constructor.</b> System.Text.Json leaves a property initializer in place
    /// when the property is absent from the JSON, so initializing this to
    /// <see cref="CurrentVersion"/> would make an unstamped file claim to be current — exactly the
    /// distinction the field exists to preserve. Stamping on write is also what the value honestly
    /// means: the schema the file was written with, not the schema some in-memory object believes in.
    /// </para>
    /// </remarks>
    public int Version { get; set; }

    public const int CurrentVersion = 2;

    /// <summary>
    /// Per-element styling for WYSIWYG mode and for source mode, kept fully independent
    /// (Requirements.md §6): the two modes render the same document for different purposes, so a
    /// heading may be large and proportional in one and plain mono in the other.
    /// </summary>
    /// <remarks>
    /// The defaults reproduce today's rendering exactly — see
    /// <see cref="ModeStyles.WysiwygDefaults"/> for how each of the old special cases (heading
    /// scaling being WYSIWYG-only, code staying mono when the base font flips) becomes an ordinary
    /// default rather than a branch in rendering code.
    /// </remarks>
    public ModeStyles Wysiwyg { get; set; } = ModeStyles.WysiwygDefaults();
    public ModeStyles Source { get; set; } = ModeStyles.SourceDefaults();
}

/// <summary>
/// One editor mode's complete text styling (Requirements.md §6): a base font every element inherits
/// from, plus per-element overrides keyed by <see cref="StyledElements"/>'s stable keys.
/// </summary>
/// <remarks>
/// WYSIWYG and source mode each own one of these, fully independent. That independence is what lets
/// every special case in the old rendering code become an ordinary default here — see
/// <see cref="WysiwygDefaults"/>.
/// </remarks>
internal sealed class ModeStyles
{
    /// <summary>The family every element inherits unless it sets <see cref="ElementStyle.FontFamily"/>.</summary>
    public string BaseFontFamily { get; set; } = "";

    /// <summary>The size, in WPF device-independent units, that <see cref="ElementStyle.FontScale"/> multiplies.</summary>
    public double BaseFontSize { get; set; } = DefaultBaseFontSize;

    /// <summary>
    /// Per-element overrides. An absent key inherits everything — equivalent to an
    /// <see cref="ElementStyle"/> with every property null, so nothing needs to enumerate
    /// <see cref="StyledElements.All"/> to stay valid.
    /// </summary>
    public Dictionary<string, ElementStyle> Elements { get; set; } = new();

    /// <summary>
    /// The style for one element, created on demand. For <b>editing</b> only — rendering reads
    /// <see cref="Elements"/> directly, since creating entries during a redraw would mutate the
    /// settings from the render loop.
    /// </summary>
    public ElementStyle Style(string key)
    {
        if (!Elements.TryGetValue(key, out var style))
            Elements[key] = style = new ElementStyle();
        return style;
    }

    // The editor's pre-Requirements-§6 hardcoded values, kept as the defaults so an unmodified or
    // upgrading install renders identically. MainWindow.xaml's editor carries the same two.
    internal const string CodeFontStack = "Cascadia Code, Consolas, Courier New";
    internal const string ProseFont = "Arial";
    internal const double DefaultBaseFontSize = 14.0;

    /// <summary>
    /// Source mode: all-mono, no heading scaling — code and prose share one fixed-width base, which
    /// is why nothing here overrides <see cref="ElementStyle.FontFamily"/> or
    /// <see cref="ElementStyle.FontScale"/>.
    /// </summary>
    public static ModeStyles SourceDefaults() => new()
    {
        BaseFontFamily = CodeFontStack,
        BaseFontSize = DefaultBaseFontSize,
        Elements = SharedElementDefaults(),
    };

    /// <summary>
    /// WYSIWYG mode: identical to <see cref="SourceDefaults"/> apart from the base family and the
    /// two deltas applied below. Written as "source defaults plus deltas" rather than as a second
    /// full table on purpose — the differences between the modes are the interesting part, and this
    /// way they can't drift apart silently when a color changes.
    /// </summary>
    public static ModeStyles WysiwygDefaults()
    {
        var elements = SharedElementDefaults();

        // Typora-ish heading scaling, previously MarkdownLineColorizer.HeadingScale gated on
        // LivePreviewEnabled. Expressing it as a WYSIWYG-only default is what retires that gate:
        // source mode simply leaves FontScale null, which resolves to 1.0.
        elements[StyledElements.Heading1].FontScale = 1.6;
        elements[StyledElements.Heading2].FontScale = 1.4;
        elements[StyledElements.Heading3].FontScale = 1.25;
        elements[StyledElements.Heading4].FontScale = 1.15;
        elements[StyledElements.Heading5].FontScale = 1.05;
        // Heading 6 stays at 1.0 — left null rather than set explicitly, matching HeadingScale's
        // own default arm.

        // Code keeps the fixed-width family when the base flips to a proportional one. This was
        // Markdown.xshd pinning fontFamily on InlineCode/CodeBlock in *both* modes; as a per-mode
        // default it needs no pinning in source mode, where the base is already mono.
        elements[StyledElements.InlineCode].FontFamily = CodeFontStack;
        elements[StyledElements.CodeBlock].FontFamily = CodeFontStack;

        // Hyperlinks underline in WYSIWYG only: there the markers are hidden and the link text
        // reads as a document's link, where underlining is the near-universal convention. In source
        // mode the surrounding [text](url) is visible and already says "link", so underlining it as
        // well would just be noise on syntax the user is looking straight at.
        elements[StyledElements.Link].Decoration = ElementStyle.DecorationUnderline;

        // Both modes strike (see SharedElementDefaults); WYSIWYG additionally drops the grey, so
        // struck text reads at ordinary document colour rather than doubly deleted. Source keeps the
        // grey, where dimming is a useful second cue while reading raw syntax.
        var strikethrough = elements[StyledElements.Strikethrough];
        strikethrough.ForegroundLight = null;
        strikethrough.ForegroundDark = null;

        return new ModeStyles
        {
            BaseFontFamily = ProseFont,
            BaseFontSize = DefaultBaseFontSize,
            Elements = elements,
        };
    }

    // Every color, weight, style and decoration that applied in both editor modes before this
    // setting existed — copied from MarkdownLineColorizer, Resources/Markdown.xshd and the old
    // MainWindow.DarkHighlightColors. EditorPreferencesTests pins each one.
    private static Dictionary<string, ElementStyle> SharedElementDefaults() => new()
    {
        // Body text: everything inherits. Its family and size are the mode's base rather than
        // fields here, so this entry carries only weight/italic/colour — all unset, which leaves
        // the editor at normal weight in the theme's own foreground, exactly as before §6.
        [StyledElements.Normal] = new(),

        // All six heading levels share one color; only the weight differs, and it splits at 3 —
        // exactly MarkdownLineColorizer's old "level <= 3 ? Bold : SemiBold". SemiBold is why
        // weight is a named value rather than a bold checkbox.
        [StyledElements.Heading1] = Heading(ElementStyle.WeightBold),
        [StyledElements.Heading2] = Heading(ElementStyle.WeightBold),
        [StyledElements.Heading3] = Heading(ElementStyle.WeightBold),
        [StyledElements.Heading4] = Heading(ElementStyle.WeightSemiBold),
        [StyledElements.Heading5] = Heading(ElementStyle.WeightSemiBold),
        [StyledElements.Heading6] = Heading(ElementStyle.WeightSemiBold),

        // Weight is pinned Normal, not left to inherit, because the old ColorLine call forced it:
        // a "> **bold**" line rendered entirely at normal weight, the line-wide styling flattening
        // the emphasis inside it. Set explicitly so an unmodified install still does — but now
        // reachable, so anyone who wants bold to survive inside a quote can set it to inherit.
        [StyledElements.Blockquote] = new()
        {
            FontWeight = ElementStyle.WeightNormal,
            Italic = true,
            ForegroundLight = "#6A737D",
            ForegroundDark = "#8B949E",
        },

        // Also drives the table's delimiter row and pipe dimming — one color for several structural
        // marks, as before this setting existed. Weight pinned Normal for the same reason as
        // blockquote above.
        [StyledElements.HorizontalRule] = new()
        {
            FontWeight = ElementStyle.WeightNormal,
            ForegroundLight = "#BBBBBB",
            ForegroundDark = "#484F58",
        },

        [StyledElements.Bold] = new() { FontWeight = ElementStyle.WeightBold },
        [StyledElements.Italic] = new() { Italic = true },
        [StyledElements.BoldItalic] = new() { FontWeight = ElementStyle.WeightBold, Italic = true },

        // Actually struck, in both modes. Before per-element styling this was grey text and nothing
        // more — AvalonEdit could always draw the strike, nothing ever asked it to. Source mode
        // keeps the grey alongside it (the WYSIWYG delta drops it; see WysiwygDefaults).
        [StyledElements.Strikethrough] = new()
        {
            Decoration = ElementStyle.DecorationStrikethrough,
            ForegroundLight = "#888888",
            ForegroundDark = "#8B949E",
        },

        // Background only — highlighted text keeps the editor's standard foreground.
        [StyledElements.Highlight] = new()
        {
            BackgroundLight = "#F5E7A3",
            BackgroundDark = "#6A5E2E",
        },

        [StyledElements.Underline] = new() { Decoration = ElementStyle.DecorationUnderline },

        [StyledElements.InlineCode] = CodeColors(),
        [StyledElements.CodeBlock] = CodeColors(),

        [StyledElements.Link] = new()
        {
            ForegroundLight = "#0065BD",
            ForegroundDark = "#58A6FF",
        },

        [StyledElements.ListMarker] = new()
        {
            ForegroundLight = "#005CC5",
            ForegroundDark = "#79C0FF",
        },

        [StyledElements.Comment] = new()
        {
            Italic = true,
            ForegroundLight = "#888888",
            ForegroundDark = "#8B949E",
        },
    };

    private static ElementStyle Heading(string weight) => new()
    {
        FontWeight = weight,
        ForegroundLight = "#0057AE",
        ForegroundDark = "#58A6FF",
    };

    // Inline code and fenced code blocks share one palette, as they always have. A fresh instance
    // per element rather than a shared one: they are independently editable in Preferences.
    //
    // A deliberate terminal look, chosen rather than inherited: green on black in light theme,
    // amber on the dark surface in dark theme. These are NOT the pre-§6 values (#C7254E on #FEF2F2
    // / #FF7B72), so they are the one place the "unmodified install renders identically" bar is
    // knowingly given up — an upgrading install lands on these too.
    private static ElementStyle CodeColors() => new()
    {
        ForegroundLight = "#00FF33",
        ForegroundDark = "#FFB000",
        BackgroundLight = "#000000",
        BackgroundDark = "#30363D",
    };
}

/// <summary>
/// One element's style overrides within a single editor mode. <b>Every property is nullable and
/// null means inherit</b> — that distinction is load-bearing: a value equal to the base today must
/// not silently become pinned, or changing the base font would stop propagating to it.
/// </summary>
/// <remarks>
/// The nullable shape maps 1:1 onto AvalonEdit's own semantics, verified against
/// <c>HighlightingColorizer.ApplyColorToElement</c> (tag v6.3.1, the release this project's
/// 6.3.1.120 package ships): it rebuilds the typeface from the element's *current* one for whichever
/// of family/style/weight are null, so an unset field costs nothing and inherits correctly with no
/// code here.
/// </remarks>
internal sealed class ElementStyle
{
    public const string WeightNormal = "Normal";
    public const string WeightSemiBold = "SemiBold";
    public const string WeightBold = "Bold";

    public const string DecorationNone = "None";
    public const string DecorationUnderline = "Underline";
    public const string DecorationStrikethrough = "Strikethrough";

    /// <summary>Null inherits the mode's <see cref="ModeStyles.BaseFontFamily"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FontFamily { get; set; }

    /// <summary>
    /// Multiplier on the mode's base size; null means 1.0. A multiplier rather than an absolute
    /// point size so it survives a change to the base — the same shape heading scaling already had.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? FontScale { get; set; }

    /// <summary>Null inherits; otherwise one of the <c>Weight*</c> constants.</summary>
    /// <remarks>
    /// Named rather than a bool specifically to keep <see cref="WeightSemiBold"/> expressible —
    /// headings 4–6 are SemiBold today, and a Bold checkbox would default them to Normal and render
    /// them lighter than they are now.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FontWeight { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Italic { get; set; }

    /// <summary>Null inherits; otherwise one of the <c>Decoration*</c> constants.</summary>
    /// <remarks>
    /// One field rather than separate Underline and Strikethrough flags because AvalonEdit cannot
    /// render both: <c>ApplyColorToElement</c>'s second <c>SetTextDecorations</c> call *replaces*
    /// the first rather than merging, so setting both would silently yield strikethrough alone.
    /// Making them mutually exclusive here means the UI can never express something that won't
    /// render.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Decoration { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ForegroundLight { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ForegroundDark { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BackgroundLight { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BackgroundDark { get; set; }

    /// <summary>
    /// Overwrites every property from <paramref name="other"/>, in place.
    /// </summary>
    /// <remarks>
    /// In place rather than returning a copy because callers hand out references to these — the
    /// Preferences editor holds one for the selected element — so replacing the instance would
    /// leave the editor writing into an object nothing reads any more.
    /// </remarks>
    public void CopyFrom(ElementStyle other)
    {
        FontFamily = other.FontFamily;
        FontScale = other.FontScale;
        FontWeight = other.FontWeight;
        Italic = other.Italic;
        Decoration = other.Decoration;
        ForegroundLight = other.ForegroundLight;
        ForegroundDark = other.ForegroundDark;
        BackgroundLight = other.BackgroundLight;
        BackgroundDark = other.BackgroundDark;
    }
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
                return Deserialize(File.ReadAllText(SettingsPath));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Missing, corrupt, or unreadable — fall back to defaults rather than fail startup.
        }
        return new AppSettings();
    }

    /// <summary>
    /// Parses settings JSON into a usable <see cref="AppSettings"/>. Split out from
    /// <see cref="Load"/>, which owns the fixed file path, so this is reachable from tests without
    /// touching <c>%AppData%</c>.
    /// </summary>
    /// <remarks>
    /// A file written before per-element styling needs no conversion: its flat properties are simply
    /// unrecognized, which System.Text.Json ignores, so it loads and lands on current defaults. That
    /// is the whole recovery story now — see <see cref="EditorPreferences.Version"/>.
    /// </remarks>
    internal static AppSettings Deserialize(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

        // An explicit "editorPreferences": null in a hand-edited file deserializes as null despite
        // the non-nullable declaration, and everything downstream would hit an NRE — which Load's
        // catch filter doesn't cover, so it would fail startup rather than fall back to defaults.
        settings.EditorPreferences ??= new EditorPreferences();

        return settings;
    }

    public static void Save(AppSettings settings)
    {
        // Stamp the schema the file is being written with, so the next Load can tell a
        // per-element file from a pre-per-element one. See EditorPreferences.Version for why this
        // can't be a property initializer instead.
        settings.EditorPreferences.Version = EditorPreferences.CurrentVersion;

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings));
    }
}
