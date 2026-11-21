using MDEdit.Editing;

namespace MDEdit.Tests;

public class StartupFileTests
{
    private const string Exe = @"C:\Apps\MDEdit.exe";

    [Fact]
    public void NoArgumentAndNoActivationData_ReturnsNull()
        => Assert.Null(StartupFile.Resolve([Exe], null));

    [Fact]
    public void CommandLineArgument_IsReturned()
        => Assert.Equal(@"C:\docs\notes.md", StartupFile.Resolve([Exe, @"C:\docs\notes.md"], null));

    [Fact]
    public void CommandLineArgument_WinsOverActivationData()
        => Assert.Equal(@"C:\a.md", StartupFile.Resolve([Exe, @"C:\a.md"], @"C:\b.md"));

    // The double-click route under ClickOnce: no command-line argument at all, the file only in
    // the launcher's environment variable. This is the case that used to open a blank window.
    [Fact]
    public void ActivationDataPlainPath_IsReturned()
        => Assert.Equal(@"C:\docs\notes.md", StartupFile.Resolve([Exe], @"C:\docs\notes.md"));

    [Fact]
    public void ActivationDataFileUri_IsConvertedToLocalPath()
        => Assert.Equal(@"C:\my docs\notes.md", StartupFile.Resolve([Exe], "file:///C:/my%20docs/notes.md"));

    [Fact]
    public void ActivationDataUncFileUri_IsConvertedToUncPath()
        => Assert.Equal(@"\\server\share\notes.md", StartupFile.Resolve([Exe], "file://server/share/notes.md"));

    // A plain path must never go through Uri parsing, or '#' would be read as a fragment and the
    // rest of the file name silently dropped.
    [Fact]
    public void ActivationDataPlainPathWithHash_IsReturnedVerbatim()
        => Assert.Equal(@"C:\docs\C# notes.md", StartupFile.Resolve([Exe], @"C:\docs\C# notes.md"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankActivationData_ReturnsNull(string value)
        => Assert.Null(StartupFile.Resolve([Exe], value));

    [Fact]
    public void BlankCommandLineArgument_FallsThroughToActivationData()
        => Assert.Equal(@"C:\b.md", StartupFile.Resolve([Exe, ""], @"C:\b.md"));
}
