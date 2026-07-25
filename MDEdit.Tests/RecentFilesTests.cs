using MDEdit.Editing;

namespace MDEdit.Tests;

public class RecentFilesTests
{
    // Absolute paths throughout: RecentFiles normalizes via Path.GetFullPath, so a relative
    // path would resolve against the test host's working directory and make assertions brittle.
    private const string A = @"C:\docs\a.md";
    private const string B = @"C:\docs\b.md";
    private const string C = @"C:\docs\c.md";

    [Fact]
    public void Add_PutsPathFirst()
    {
        var result = RecentFiles.Add([A, B], C);

        Assert.Equal([C, A, B], result);
    }

    [Fact]
    public void Add_OnEmptyList_ReturnsSingleEntry()
    {
        Assert.Equal([A], RecentFiles.Add([], A));
    }

    [Fact]
    public void Add_NullList_IsTreatedAsEmpty()
    {
        Assert.Equal([A], RecentFiles.Add(null, A));
    }

    [Fact]
    public void Add_ExistingPath_PromotesRatherThanDuplicates()
    {
        var result = RecentFiles.Add([A, B, C], C);

        Assert.Equal([C, A, B], result);
    }

    [Theory]
    [InlineData(@"C:\DOCS\A.MD")]      // different case
    [InlineData(@"C:\docs\.\a.md")]    // redundant "."
    [InlineData(@"C:\docs\sub\..\a.md")] // redundant ".."
    public void Add_EquivalentSpelling_IsTheSameEntry(string equivalent)
    {
        var result = RecentFiles.Add([A, B], equivalent);

        Assert.Equal(2, result.Count);
        Assert.Equal(A, result[0], ignoreCase: true);
        Assert.Equal(B, result[1]);
    }

    [Fact]
    public void Add_BeyondMaxEntries_DropsTheOldest()
    {
        var full = Enumerable.Range(0, RecentFiles.MaxEntries)
            .Select(i => $@"C:\docs\file{i}.md")
            .ToList();

        var result = RecentFiles.Add(full, A);

        Assert.Equal(RecentFiles.MaxEntries, result.Count);
        Assert.Equal(A, result[0]);
        Assert.DoesNotContain($@"C:\docs\file{RecentFiles.MaxEntries - 1}.md", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Add_BlankPath_LeavesListUnchanged(string? blank)
    {
        Assert.Equal([A, B], RecentFiles.Add([A, B], blank));
    }

    [Fact]
    public void Remove_DropsMatchingEntry()
    {
        Assert.Equal([A, C], RecentFiles.Remove([A, B, C], B));
    }

    [Fact]
    public void Remove_IsCaseInsensitive()
    {
        Assert.Equal([B], RecentFiles.Remove([A, B], @"C:\DOCS\A.MD"));
    }

    [Fact]
    public void Remove_AbsentPath_LeavesListUnchanged()
    {
        Assert.Equal([A, B], RecentFiles.Remove([A, B], C));
    }

    [Fact]
    public void Sanitize_DropsBlanksAndDuplicates()
    {
        var result = RecentFiles.Sanitize([A, "", B, @"C:\DOCS\A.MD", "   ", B]);

        Assert.Equal([A, B], result);
    }

    [Fact]
    public void Sanitize_Null_ReturnsEmpty()
    {
        Assert.Empty(RecentFiles.Sanitize(null));
    }

    [Fact]
    public void Sanitize_AppliesTheCap()
    {
        var overLong = Enumerable.Range(0, RecentFiles.MaxEntries + 5)
            .Select(i => $@"C:\docs\file{i}.md");

        Assert.Equal(RecentFiles.MaxEntries, RecentFiles.Sanitize(overLong).Count);
    }

    [Fact]
    public void Sanitize_KeepsOrder()
    {
        Assert.Equal([C, A, B], RecentFiles.Sanitize([C, A, B]));
    }

    [Fact]
    public void Add_DoesNotMutateTheInputList()
    {
        var original = new List<string> { A, B };

        RecentFiles.Add(original, C);

        Assert.Equal([A, B], original);
    }
}
