using MDEdit.Editing;

namespace MDEdit.Tests;

public class DefaultAppsGateTests
{
    // Verbatim from an installed machine's HKCU\Software\Classes\MDEdit.Document\shell\open\command.
    private const string ClickOnceCommand =
        "rundll32.exe dfshim.dll, ShOpenVerbExtension {60e9e955-74c1-4102-b6c6-3c6e1afe106a} %1";

    [Fact]
    public void ClickOnceCommand_IsUpdateProof()
        => Assert.True(DefaultAppsGate.IsUpdateProofCommand(ClickOnceCommand));

    // What the removed FileAssociationService wrote: a version-specific exe path that goes stale
    // on the next update. MDEdit must never advertise itself on top of one.
    [Fact]
    public void VersionSpecificExePath_IsNotUpdateProof()
        => Assert.False(DefaultAppsGate.IsUpdateProofCommand(
            "\"C:\\Users\\u\\AppData\\Local\\Apps\\2.0\\AB\\CD\\mded..tion_0000_0001.0000_abcd\\MDEdit.exe\" \"%1\""));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rundll32.exe dfshim.dll, ShOpenVerbApplication https://example.com/x.application")]
    public void MissingOrOtherCommand_IsNotUpdateProof(string? command)
        => Assert.False(DefaultAppsGate.IsUpdateProofCommand(command));

    [Fact]
    public void ShouldPrompt_OnlyWhenRegisteredAndNeverAsked()
    {
        Assert.True(DefaultAppsGate.ShouldPrompt(alreadyAsked: false, registered: true));
        Assert.False(DefaultAppsGate.ShouldPrompt(alreadyAsked: true, registered: true));
        // Not registered (dev build, or no ClickOnce association): don't spend the one question.
        Assert.False(DefaultAppsGate.ShouldPrompt(alreadyAsked: false, registered: false));
    }

    [Fact]
    public void SettingsUri_Registered_LinksToMDEditsOwnPage()
        => Assert.Equal("ms-settings:defaultapps?registeredAppUser=MDEdit", DefaultAppsGate.SettingsUri(registered: true));

    [Fact]
    public void SettingsUri_NotRegistered_OpensGeneralPage()
        => Assert.Equal("ms-settings:defaultapps", DefaultAppsGate.SettingsUri(registered: false));
}
