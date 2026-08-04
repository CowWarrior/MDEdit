using System.Globalization;
using MDEdit.Editing;

namespace MDEdit.Tests;

public class StatusFormatterTests
{
    // Passed explicitly so the assertions don't depend on the machine's locale — the production
    // calls deliberately use CurrentCulture so a French user sees "1,5 KB".
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1, "1 byte")]
    [InlineData(2, "2 bytes")]
    [InlineData(512, "512 bytes")]
    [InlineData(1023, "1,023 bytes")]
    public void FormatFileSize_BelowOneKilobyte_ReportsBytes(long bytes, string expected)
    {
        Assert.Equal(expected, StatusFormatter.FormatFileSize(bytes, Inv));
    }

    [Theory]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(10240, "10.0 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1572864, "1.5 MB")]
    [InlineData(1073741824, "1.0 GB")]
    [InlineData(1099511627776, "1.0 TB")]
    public void FormatFileSize_LargerSizes_UseScaledUnits(long bytes, string expected)
    {
        Assert.Equal(expected, StatusFormatter.FormatFileSize(bytes, Inv));
    }

    // Rounding to one decimal can land on exactly 1024.0, which would read as "1024.0 KB" beside a
    // file Explorer calls 1 MB.
    [Theory]
    [InlineData(1048575, "1.0 MB")]        // one byte under 1 MB
    [InlineData(1073741823, "1.0 GB")]     // one byte under 1 GB
    public void FormatFileSize_JustUnderAUnitBoundary_PromotesInsteadOfShowing1024(long bytes, string expected)
    {
        Assert.Equal(expected, StatusFormatter.FormatFileSize(bytes, Inv));
    }

    [Fact]
    public void FormatFileSize_BeyondLargestUnit_StaysInTerabytes()
    {
        Assert.EndsWith("TB", StatusFormatter.FormatFileSize(5L * 1024 * 1024 * 1024 * 1024, Inv));
    }

    [Fact]
    public void FormatFileSize_Negative_IsTreatedAsZero()
    {
        Assert.Equal("0 bytes", StatusFormatter.FormatFileSize(-1, Inv));
    }

    [Theory]
    [InlineData(0, "0 characters")]
    [InlineData(1, "1 character")]
    [InlineData(2, "2 characters")]
    [InlineData(1234, "1,234 characters")]
    [InlineData(1000000, "1,000,000 characters")]
    public void FormatCharacterCount_UsesGroupingAndSingularForm(int count, string expected)
    {
        Assert.Equal(expected, StatusFormatter.FormatCharacterCount(count, Inv));
    }

    [Theory]
    [InlineData(1, "1 selected")]
    [InlineData(56, "56 selected")]
    [InlineData(12345, "12,345 selected")]
    public void FormatSelectionCount_UsesGrouping(int count, string expected)
    {
        Assert.Equal(expected, StatusFormatter.FormatSelectionCount(count, Inv));
    }

    [Theory]
    [InlineData(1.0, "100%")]
    [InlineData(0.1, "10%")]
    [InlineData(0.75, "75%")]
    [InlineData(1.25, "125%")]
    [InlineData(5.0, "500%")]
    public void FormatZoom_RendersWholePercent(double level, string expected)
    {
        Assert.Equal(expected, StatusFormatter.FormatZoom(level, Inv));
    }

    // The ladder only ever produces whole percents, so a fraction can only come from a hand-edited
    // settings file — showing "72.5%" there would read as a bug rather than as fidelity.
    [Theory]
    [InlineData(0.7249, "72%")]
    [InlineData(0.7251, "73%")]
    public void FormatZoom_RoundsRatherThanShowingAFraction(double level, string expected)
    {
        Assert.Equal(expected, StatusFormatter.FormatZoom(level, Inv));
    }

    [Fact]
    public void FormatZoom_SurvivesNonFiniteInput()
    {
        Assert.Equal("100%", StatusFormatter.FormatZoom(double.NaN, Inv));
    }

    [Fact]
    public void Formatters_HonourTheSuppliedCulture()
    {
        var fr = new CultureInfo("fr-FR");   // comma decimal separator, narrow-space grouping

        Assert.Equal("1,5 KB", StatusFormatter.FormatFileSize(1536, fr));
        Assert.StartsWith("1", StatusFormatter.FormatCharacterCount(1234, fr));
    }
}
