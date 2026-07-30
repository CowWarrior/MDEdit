using MDEdit.Editing;

namespace MDEdit.Tests;

public class ImagePathResolverTests
{
    // Null always means "decline — fall through to the link generator's marker hiding", never
    // "broken": remote schemes are phase-2 territory, and a relative path with no document
    // directory (an unsaved document) is unresolvable rather than missing.

    [Theory]
    [InlineData("http://example.com/img.png")]
    [InlineData("https://example.com/img.png")]
    [InlineData("HTTPS://example.com/img.png")]
    [InlineData("file:///C:/images/img.png")]
    [InlineData("ftp://example.com/img.png")]
    public void TryResolve_AnyScheme_ReturnsNull(string url)
    {
        Assert.Null(ImagePathResolver.TryResolve(url, @"C:\docs"));
    }

    [Fact]
    public void TryResolve_AbsolutePath_ReturnsIt()
    {
        // Absolute paths need no base directory, so a null one is fine.
        Assert.Equal(@"C:\images\a.png", ImagePathResolver.TryResolve(@"C:\images\a.png", null));
    }

    [Fact]
    public void TryResolve_RelativePath_CombinesWithDocumentDirectory()
    {
        Assert.Equal(@"C:\docs\img.png", ImagePathResolver.TryResolve("img.png", @"C:\docs"));
    }

    [Fact]
    public void TryResolve_RelativeWithParentSegments_Normalizes()
    {
        Assert.Equal(@"C:\docs\shared\img.png",
            ImagePathResolver.TryResolve(@"..\shared\img.png", @"C:\docs\notes"));
    }

    [Fact]
    public void TryResolve_ForwardSlashes_Resolve()
    {
        Assert.Equal(@"C:\docs\sub\img.png", ImagePathResolver.TryResolve("sub/img.png", @"C:\docs"));
    }

    [Fact]
    public void TryResolve_RelativePathWithoutDocumentDirectory_ReturnsNull()
    {
        Assert.Null(ImagePathResolver.TryResolve("img.png", null));
    }

    // A trailing quoted title is part of the parenthesized target, not the path — without
    // stripping it we'd probe a file literally named 'img.png "The title"'.
    [Theory]
    [InlineData("img.png \"The title\"")]
    [InlineData("img.png 'The title'")]
    public void TryResolve_QuotedTitle_IsStripped(string url)
    {
        Assert.Equal(@"C:\docs\img.png", ImagePathResolver.TryResolve(url, @"C:\docs"));
    }

    // CommonMark's bracketed-destination form allows spaces in the path.
    [Fact]
    public void TryResolve_AngleBracketWrapped_IsUnwrapped()
    {
        Assert.Equal(@"C:\docs\img with space.png",
            ImagePathResolver.TryResolve("<img with space.png>", @"C:\docs"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolve_EmptyOrWhitespace_ReturnsNull(string url)
    {
        Assert.Null(ImagePathResolver.TryResolve(url, @"C:\docs"));
    }

    [Fact]
    public void TryResolve_InvalidPathCharacters_ReturnsNull()
    {
        // GetFullPath throws on an embedded NUL on every platform (unlike '|', which .NET no
        // longer rejects) — the catch turns that into a decline.
        Assert.Null(ImagePathResolver.TryResolve("img\0.png", @"C:\docs"));
    }
}
