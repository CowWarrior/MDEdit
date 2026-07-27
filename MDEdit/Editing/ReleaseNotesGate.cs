namespace MDEdit.Editing;

/// <summary>
/// Pure decision logic for whether <c>ReleaseNotes.md</c> should auto-open — pulled out of
/// <c>MainWindow</c> so it's unit-testable, the same reasoning as <see cref="RecentFiles"/>.
/// </summary>
internal static class ReleaseNotesGate
{
    /// <summary>
    /// The release identifier from an assembly version — <c>Major.Minor.Build</c> only,
    /// dropping <c>Revision</c> (the git commit count). Matches ReleaseNotes.md's changelog
    /// headers ("### Version 1.0.3") exactly. Deliberately NOT the full 4-part version or
    /// Revision alone: per the versioning scheme, two different publishes can share the same
    /// Revision value, so either of those would show the page far more or less often than once
    /// per actual release.
    /// </summary>
    public static string? GetReleaseVersion(Version? assemblyVersion)
        => assemblyVersion is null ? null : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";

    /// <summary>
    /// Whether the release notes should be shown for <paramref name="currentRelease"/>, given
    /// the release last recorded as shown. Equality, not "greater than": a fresh install has
    /// an empty <paramref name="lastShown"/>, and any release change — including a
    /// hypothetical repair or downgrade — should show it again. ClickOnce only moves forward
    /// in practice, so the simpler rule costs nothing.
    /// </summary>
    public static bool ShouldShow(string? lastShown, string currentRelease)
        => lastShown != currentRelease;
}
