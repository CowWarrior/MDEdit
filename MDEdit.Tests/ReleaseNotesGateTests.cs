using MDEdit.Editing;

namespace MDEdit.Tests;

public class ReleaseNotesGateTests
{
    [Fact]
    public void GetReleaseVersion_DropsRevision()
        => Assert.Equal("1.0.3", ReleaseNotesGate.GetReleaseVersion(new Version(1, 0, 3, 59)));

    [Fact]
    public void GetReleaseVersion_NullVersion_ReturnsNull()
        => Assert.Null(ReleaseNotesGate.GetReleaseVersion(null));

    [Theory]
    [InlineData(1, 0, 3, 59)]
    [InlineData(1, 0, 3, 1)] // same release, different Revision — Revision must not factor in
    public void GetReleaseVersion_IgnoresRevisionEntirely(int major, int minor, int build, int revision)
        => Assert.Equal("1.0.3", ReleaseNotesGate.GetReleaseVersion(new Version(major, minor, build, revision)));

    [Fact]
    public void ShouldShow_FreshInstall_EmptyLastShown_ReturnsTrue()
        => Assert.True(ReleaseNotesGate.ShouldShow("", "1.0.3"));

    [Fact]
    public void ShouldShow_NullLastShown_ReturnsTrue()
        => Assert.True(ReleaseNotesGate.ShouldShow(null, "1.0.3"));

    [Fact]
    public void ShouldShow_SameRelease_ReturnsFalse()
        => Assert.False(ReleaseNotesGate.ShouldShow("1.0.3", "1.0.3"));

    [Fact]
    public void ShouldShow_DifferentRelease_ReturnsTrue()
        => Assert.True(ReleaseNotesGate.ShouldShow("1.0.2", "1.0.3"));

    [Fact]
    public void ShouldShow_ReleaseWentBackward_StillReturnsTrue()
        // Equality, not ordinal comparison — a hypothetical downgrade/repair shows the page
        // again rather than requiring special-case handling.
        => Assert.True(ReleaseNotesGate.ShouldShow("1.0.3", "1.0.2"));
}
