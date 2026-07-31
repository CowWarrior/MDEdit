using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.RegularExpressions;

namespace MDEdit.Editing;

/// <summary>
/// Resolves a Markdown image target ("![alt](url)") to either an absolute local file path
/// (<see cref="TryResolve"/>) or a remote http(s) URI (<see cref="TryResolveRemote"/>), or
/// decides it isn't ours to render at all.
/// </summary>
/// <remarks>
/// The contract every caller relies on: a declined result always means "fall through to
/// LinkMarkerElementGenerator's marker-hidden alt text", never "broken". For the local entry
/// point that covers any URI scheme, a relative path with no document directory (an unsaved
/// document's image isn't missing, it's unresolvable — it may become right the moment the user
/// saves), and anything unparseable as a path. Only a resolved target that then fails to load
/// earns the drawn broken-image placeholder.
///
/// The two entry points are deliberately kept **disjoint** rather than merged into one union
/// type. <see cref="TryResolve"/> still declines every scheme, so a URL can never reach
/// File.OpenRead; <see cref="TryResolveRemote"/> accepts only http and https, so a local path
/// can never reach the network. A discriminated result would instead force every caller to
/// *handle* a remote case — backwards for a feature whose safe default (remote loading off)
/// should be reached by writing no code at all.
///
/// Remote loading is off by default and gated on AppSettings.LoadRemoteImages (View > Load
/// Remote Images): fetching a remote URL just from having the document open discloses to its
/// host that the document was opened, the tracking-pixel concern email clients guard against.
/// This class only answers "what kind of target is this" — it never fetches anything, and it is
/// never consulted for a remote URL while that setting is off; see
/// <see cref="ImageElementGenerator"/>'s RemoteEnabled gate.
/// </remarks>
internal static class ImagePathResolver
{
    // A trailing quoted title ('img.png "The title"') is part of the parenthesized target but
    // not of the path — without stripping it we'd probe a file literally named that.
    private static readonly Regex TrailingTitlePattern = new("""\s+("[^"]*"|'[^']*')$""");

    // Any URI scheme declines from the LOCAL entry point, including file:// (local files are
    // reachable as plain paths). Deliberately not Uri.TryCreate, which claims "C:\x.png" with
    // scheme "c".
    private static readonly Regex SchemePattern = new("^[A-Za-z][A-Za-z0-9+.-]*://");

    /// <summary>
    /// Shared normalization for both entry points: trim, unwrap CommonMark's bracketed
    /// destination form, and drop a trailing quoted title. Returns "" when nothing usable is
    /// left, which both entry points decline on.
    /// </summary>
    private static string Normalize(string url)
    {
        url = url.Trim();

        // CommonMark's bracketed-destination form: <path with spaces.png>.
        if (url.Length >= 2 && url[0] == '<' && url[^1] == '>')
            url = url[1..^1];

        return TrailingTitlePattern.Replace(url, string.Empty);
    }

    /// <summary>
    /// Resolves a target to an absolute local file path, or null to decline. Declines every URI
    /// scheme — including http(s), which belongs to <see cref="TryResolveRemote"/>.
    /// </summary>
    public static string? TryResolve(string url, string? documentDirectory)
    {
        var normalized = Normalize(url);
        if (normalized.Length == 0) return null;
        if (SchemePattern.IsMatch(normalized)) return null;

        try
        {
            if (Path.IsPathFullyQualified(normalized))
                return Path.GetFullPath(normalized);
            return documentDirectory is null ? null : Path.GetFullPath(normalized, documentDirectory);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves a target to a remote http(s) URI, or declines. The scheme whitelist — not
    /// Uri.TryCreate's success — is the decision: TryCreate happily turns "C:\images\a.png" and
    /// "\\server\share\a.png" into absolute URIs with scheme "file", so accepting whatever it
    /// parses would hand local paths to the network stack. This is phase 1's "deliberately not
    /// Uri.TryCreate" note applied from the other side.
    /// </summary>
    public static bool TryResolveRemote(string url, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;

        var normalized = Normalize(url);
        if (normalized.Length == 0) return false;
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var parsed)) return false;

        // Uri lowercases the scheme during parsing, so "HTTPS://" arrives here as "https".
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            return false;

        uri = parsed;
        return true;
    }
}
