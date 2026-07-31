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
}
