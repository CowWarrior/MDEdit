using MDEdit.Editing;

namespace MDEdit.Tests;

public class ImagePathResolverTests
{
    // Null always means "decline — fall through to the link generator's marker hiding", never
    // "broken": remote schemes are phase-2 territory, and a relative path with no document
    // directory (an unsaved document) is unresolvable rather than missing.

    // The local resolver declines every scheme, http(s) included — those belong to
    // TryResolveRemote. This pins that the two entry points are disjoint, so a URL can never
    // reach File.OpenRead no matter what the remote opt-in is set to.
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

    [Theory]
    [InlineData("http://example.com/img.png", "http")]
    [InlineData("https://example.com/img.png", "https")]
    [InlineData("https://example.com:8443/img.png", "https")]
    [InlineData("https://example.com/a/b/img.png?v=2", "https")]
    public void TryResolveRemote_HttpAndHttps_Accepted(string url, string expectedScheme)
    {
        Assert.True(ImagePathResolver.TryResolveRemote(url, out var uri));
        Assert.Equal(expectedScheme, uri.Scheme);
    }

    [Fact]
    public void TryResolveRemote_UppercaseScheme_NormalizesToLowercase()
    {
        Assert.True(ImagePathResolver.TryResolveRemote("HTTPS://Example.COM/img.png", out var uri));
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("example.com", uri.Host);
    }

    [Theory]
    [InlineData("file:///C:/images/img.png")]
    [InlineData("ftp://example.com/img.png")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:a@b.c")]
    [InlineData("about:blank")]
    public void TryResolveRemote_NonHttpSchemes_Rejected(string url)
    {
        Assert.False(ImagePathResolver.TryResolveRemote(url, out var uri));
        Assert.Null(uri);
    }

    // The important one: Uri.TryCreate SUCCEEDS on several of these with scheme "file", so this
    // pins that the http/https whitelist — not TryCreate's success — is what decides. Without
    // it, a local path would be handed to the network stack.
    [Theory]
    [InlineData(@"C:\images\a.png")]
    [InlineData("img.png")]
    [InlineData("sub/img.png")]
    [InlineData(@"..\shared\img.png")]
    [InlineData(@"\\server\share\img.png")]
    public void TryResolveRemote_LocalPaths_Rejected(string url)
    {
        Assert.False(ImagePathResolver.TryResolveRemote(url, out var uri));
        Assert.Null(uri);
    }

    // Pins that normalization is genuinely shared with TryResolve rather than reimplemented.
    [Theory]
    [InlineData("https://example.com/img.png \"The title\"")]
    [InlineData("https://example.com/img.png 'The title'")]
    public void TryResolveRemote_QuotedTitle_IsStripped(string url)
    {
        Assert.True(ImagePathResolver.TryResolveRemote(url, out var uri));
        Assert.Equal("https://example.com/img.png", uri.AbsoluteUri);
    }

    [Fact]
    public void TryResolveRemote_AngleBracketWrapped_IsUnwrapped()
    {
        Assert.True(ImagePathResolver.TryResolveRemote("<https://example.com/img.png>", out var uri));
        Assert.Equal("https://example.com/img.png", uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolveRemote_EmptyOrWhitespace_ReturnsFalse(string url)
    {
        Assert.False(ImagePathResolver.TryResolveRemote(url, out var uri));
        Assert.Null(uri);
    }
}
