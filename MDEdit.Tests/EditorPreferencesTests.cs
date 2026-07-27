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
    public void Defaults_MatchPreviouslyHardcodedValues()
    {
        var prefs = new EditorPreferences();

        Assert.Equal("Arial", prefs.WysiwygFontFamily);
        Assert.Equal("Cascadia Code, Consolas, Courier New", prefs.CodeFontFamily);

        Assert.Equal("#0057AE", prefs.HeadingColorLight);
        Assert.Equal("#58A6FF", prefs.HeadingColorDark);

        Assert.Equal("#6A737D", prefs.BlockquoteColorLight);
        Assert.Equal("#8B949E", prefs.BlockquoteColorDark);

        Assert.Equal("#BBBBBB", prefs.HorizontalRuleColorLight);
        Assert.Equal("#484F58", prefs.HorizontalRuleColorDark);

        Assert.Equal("#F5E7A3", prefs.HighlightBackgroundLight);
        Assert.Equal("#6A5E2E", prefs.HighlightBackgroundDark);

        Assert.Equal("#888888", prefs.StrikethroughColorLight);
        Assert.Equal("#8B949E", prefs.StrikethroughColorDark);

        Assert.Equal("#C7254E", prefs.CodeForegroundLight);
        Assert.Equal("#FF7B72", prefs.CodeForegroundDark);
        Assert.Equal("#FEF2F2", prefs.CodeBackgroundLight);
        Assert.Equal("#30363D", prefs.CodeBackgroundDark);

        Assert.Equal("#0065BD", prefs.LinkColorLight);
        Assert.Equal("#58A6FF", prefs.LinkColorDark);

        Assert.Equal("#005CC5", prefs.ListMarkerColorLight);
        Assert.Equal("#79C0FF", prefs.ListMarkerColorDark);

        Assert.Equal("#888888", prefs.CommentColorLight);
        Assert.Equal("#8B949E", prefs.CommentColorDark);
    }
}
