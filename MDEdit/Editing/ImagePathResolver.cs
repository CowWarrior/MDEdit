using System.IO;
using System.Text.RegularExpressions;

namespace MDEdit.Editing;

/// <summary>
/// Resolves a Markdown image target ("![alt](url)") to an absolute local file path, or decides
/// it isn't ours to render. The contract callers rely on: null always means "decline — fall
/// through to LinkMarkerElementGenerator's marker-hidden alt text", never "broken". That covers
/// a remote scheme (phase 1 renders local images only — fetching a remote URL just from having
/// the document open discloses that it was opened, so auto-loading needs an explicit product
/// call first), a relative path with no document directory (an unsaved document's image isn't
/// missing, it's unresolvable — it may become right the moment the user saves), and anything
/// unparseable as a path. Only a non-null result that then fails to load earns the drawn
/// broken-image placeholder.
/// </summary>
internal static class ImagePathResolver
{
    // A trailing quoted title ('img.png "The title"') is part of the parenthesized target but
    // not of the path — without stripping it we'd probe a file literally named that.
    private static readonly Regex TrailingTitlePattern = new("""\s+("[^"]*"|'[^']*')$""");

    // Any URI scheme declines, including file:// (local files are reachable as plain paths).
    // Deliberately not Uri.TryCreate, which claims "C:\x.png" with scheme "c".
    private static readonly Regex SchemePattern = new("^[A-Za-z][A-Za-z0-9+.-]*://");

    public static string? TryResolve(string url, string? documentDirectory)
    {
        url = url.Trim();

        // CommonMark's bracketed-destination form: <path with spaces.png>.
        if (url.Length >= 2 && url[0] == '<' && url[^1] == '>')
            url = url[1..^1];

        url = TrailingTitlePattern.Replace(url, string.Empty);
        if (url.Length == 0) return null;
        if (SchemePattern.IsMatch(url)) return null;

        try
        {
            if (Path.IsPathFullyQualified(url))
                return Path.GetFullPath(url);
            return documentDirectory is null ? null : Path.GetFullPath(url, documentDirectory);
        }
        catch
        {
            return null;
        }
    }
}
