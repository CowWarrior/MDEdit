using MDEdit.Editing;

namespace MDEdit.Tests;

// Covers the fixed-width detection behind Preferences' font drop-downs (Requirements.md §6). WPF
// exposes no "is this monospaced" flag, so FontCatalog measures glyph advance widths — this pins
// that the measurement actually separates the two families of font, and that a machine with an
// unreadable or missing font degrades to "not monospaced" instead of taking the dialog down.
//
// Runs on an STA thread via WpfTestApplication: FontFamily and GlyphTypeface are WPF types, and
// xUnit's own test threads are not guaranteed STA.
public class FontCatalogTests
{
    // Present on every supported Windows install, so these are safe to assert on by name.
    [Theory]
    [InlineData("Consolas")]
    [InlineData("Courier New")]
    [InlineData("Lucida Console")]
    public void KnownFixedWidthFamilies_AreDetected(string family)
    {
        WpfTestApplication.RunOnSta(() => Assert.True(FontCatalog.IsMonospaced(family), $"{family} should be monospaced."));
    }

    [Theory]
    [InlineData("Arial")]
    [InlineData("Times New Roman")]
    [InlineData("Segoe UI")]
    [InlineData("Georgia")]
    public void KnownProportionalFamilies_AreRejected(string family)
    {
        WpfTestApplication.RunOnSta(() => Assert.False(FontCatalog.IsMonospaced(family), $"{family} should not be monospaced."));
    }

    [Fact]
    public void DefaultCodeFontStack_IsDetected()
    {
        // The shipped default code font is a fallback stack, not a single family. If the stack
        // didn't classify, MDEdit's own default would be missing from the monospaced group in the
        // very drop-down that exists to make monospaced fonts easy to find.
        WpfTestApplication.RunOnSta(() =>
            Assert.True(FontCatalog.IsMonospaced("Cascadia Code, Consolas, Courier New")));
    }

    [Fact]
    public void UnresolvableStack_FallsBackToTheFirstAvailableFamily()
    {
        // WPF resolves a stack to the first family actually installed, so a leading bogus name is
        // skipped rather than poisoning the answer.
        WpfTestApplication.RunOnSta(() =>
        {
            Assert.True(FontCatalog.IsMonospaced("No Such Font 12345, Consolas"));
            Assert.False(FontCatalog.IsMonospaced("No Such Font 12345, Arial"));
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("No Such Font 12345")]
    public void UnusableNames_ReturnFalseRatherThanThrow(string? family)
    {
        // This feeds a list in a settings dialog; one bad name must never stop Preferences opening.
        WpfTestApplication.RunOnSta(() => Assert.False(FontCatalog.IsMonospaced(family)));
    }

    [Fact]
    public void Installed_IsNonEmptyAndSorted()
    {
        WpfTestApplication.RunOnSta(() =>
        {
            var installed = FontCatalog.Installed;

            Assert.NotEmpty(installed);
            Assert.Equal(installed.OrderBy(s => s, StringComparer.OrdinalIgnoreCase), installed);
        });
    }

    [Fact]
    public void Monospaced_IsANonEmptyProperSubsetOfInstalled()
    {
        WpfTestApplication.RunOnSta(() =>
        {
            var installed = FontCatalog.Installed.ToHashSet(StringComparer.Ordinal);
            var monospaced = FontCatalog.Monospaced;

            Assert.NotEmpty(monospaced);
            Assert.All(monospaced, family => Assert.Contains(family, installed));

            // Proper subset: a detector that classified everything (or nothing) as monospaced would
            // still satisfy every assertion above, and would make the grouping pointless.
            Assert.True(monospaced.Count < installed.Count,
                "Every installed family was classified as monospaced — the detector isn't discriminating.");
        });
    }
}
