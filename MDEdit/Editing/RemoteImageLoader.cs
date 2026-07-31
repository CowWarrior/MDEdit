using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MDEdit.Editing;

/// <summary>
/// Fetches remote http(s) images for <see cref="ImageElementGenerator"/> and caches the decoded
/// result, asking for a redraw when one arrives. The app's only networking code.
/// </summary>
/// <remarks>
/// <b>The privacy guarantee this class is shaped around:</b> with View > Load Remote Images off,
/// MDEdit issues no network request of any kind for a document's content. That is a structural
/// property, not a runtime check — <c>HttpClient</c> appears in this file and nowhere else, this
/// class is instantiated exactly once (a field of <see cref="ImageElementGenerator"/>), and
/// <see cref="GetOrStartFetch"/> has exactly one call site, reachable only through that
/// generator's <c>IsRenderable</c>, whose remote clause evaluates its <c>RemoteEnabled</c> gate
/// <em>before</em> even parsing the URL. Turning the setting off calls <see cref="Invalidate"/>,
/// which cancels in-flight requests and drops every cached bitmap, so it takes effect at once
/// rather than at the next document open. Fetching is also viewport-bounded for free: AvalonEdit
/// only runs element generators for lines it has laid out, so a document with 200 remote images
/// makes one request per image the user actually scrolls to, not 200 on open.
///
/// <b>This is the app's first async, HttpClient and CancellationToken code</b>, and the second
/// use of Dispatcher.BeginInvoke (MainWindow's OS-theme-change marshal is the other). Four rules
/// hold it together:
/// <list type="bullet">
/// <item><b>Every await takes ConfigureAwait(false).</b> The fetch is started from the UI thread,
/// so without it each continuation would resume there and the image decode — the expensive part —
/// would run on the UI thread, defeating the whole design. It is also exactly why completion
/// needs the explicit dispatcher post below.</item>
/// <item><b>All cache mutation happens on the UI thread, so there is no lock.</b> Reads happen
/// only in ConstructElement, which AvalonEdit calls on the UI thread; the in-flight mark is
/// written synchronously before a fetch starts, which is what makes dedupe airtight lock-free;
/// completions post one BeginInvoke that does the generation check, the write and the redraw
/// request together.</item>
/// <item><b>A BitmapImage built on any thread and frozen is safe to hand to the UI thread.</b>
/// That standard WPF pattern is the load-bearing fact here — see
/// <see cref="ImageElementGenerator.DecodeFrozen"/>, shared with the local-image path so the two
/// can never drift on decode settings.</item>
/// <item><b>Completions are coalesced into one redraw per burst</b> via a queued flag posted at
/// DispatcherPriority.Background: every Normal-priority cache write in a burst drains before any
/// Background item runs, so N images finishing together cost exactly one redraw. Deterministic,
/// with no timer interval to tune, and Background priority also guarantees the redraw cannot
/// re-enter an in-progress layout pass.</item>
/// </list>
///
/// The redraw is full-view rather than per-line because this class is keyed by URL and knows
/// nothing about document offsets — by the time a fetch completes the document may have been
/// edited and the span moved or deleted, and one URL may appear on several lines. A full redraw
/// only re-runs generators for lines currently in the viewport, so its cost is bounded by
/// viewport size, and coalescing bounds how many happen.
///
/// The byte cap is enforced by streaming (<see cref="ReadCappedAsync"/>) rather than
/// HttpClient's MaxResponseContentBufferSize: reading response headers first lets a bad status,
/// a non-image content type or an oversized Content-Length be rejected before a single byte of
/// body is read, and it makes the cap a pure, unit-testable static. A Content-Length is never
/// used to pre-size the buffer — a lying header would be a memory-DoS vector.
///
/// Because <c>_dispatcher</c> captures Dispatcher.CurrentDispatcher, <b>this class must be
/// constructed on the UI thread</b>. It is: a field of a field of MainWindow. The two testable
/// helpers are static for the same reason — the tests never construct an instance, and never
/// touch the network.
/// </remarks>
internal sealed class RemoteImageLoader
{
    /// <summary>
    /// Ceiling on a downloaded image's encoded size. Comfortably above any realistic photo or
    /// screenshot a Markdown document embeds, while bounding what one hostile or mistaken URL
    /// can cost in memory and time. The decoded size is bounded separately by
    /// <see cref="ImageElementGenerator.DecodeHeightCap"/>.
    /// </summary>
    internal const int MaxImageBytes = 8 * 1024 * 1024;

    /// <summary>
    /// Long enough for a slow CDN at realistic image sizes, short enough that a dead host
    /// settles into the broken-image placeholder rather than leaving a loading box up
    /// indefinitely. Surfaces as TaskCanceledException, which is treated as any other failure.
    /// </summary>
    // Declared before Http: static fields initialize in declaration order, and CreateClient
    // reads this one.
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            // No cookie jar accumulates in-process, so no cookie is ever echoed back to a host —
            // directly in service of the tracking-pixel concern this feature is gated on.
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            // Standard DNS-staleness mitigation for a long-lived static client.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            // Credentials are deliberately left unset: never offer Windows integrated auth to a
            // third-party host. Do not "helpfully" add UseDefaultCredentials here.
        };

        var client = new HttpClient(handler) { Timeout = RequestTimeout };

        // An honest identifying User-Agent — some hosts reject or degrade a UA-less client.
        // Deliberately not a spoofed browser UA.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
            "MDEdit", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        return client;
    }

    private enum RemoteImageState { InFlight, Loaded, Failed }

    private readonly record struct RemoteImageEntry(RemoteImageState State, BitmapImage? Bitmap);

    // Keyed by uri.AbsoluteUri, Ordinal — URL paths and queries are case-sensitive, and Uri has
    // already normalized scheme and host case. An absent key means "not yet attempted", which
    // with the three states gives the four the state machine needs. One record rather than a
    // dictionary plus a parallel in-flight set, so the two can never disagree.
    private readonly Dictionary<string, RemoteImageEntry> _entries = new(StringComparer.Ordinal);

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private CancellationTokenSource _cts = new();
    private int _generation;
    private bool _redrawQueued;

    /// <summary>
    /// Pushed in by MainWindow. Invoked (coalesced, on the UI thread) whenever a fetch completes
    /// and the view needs to re-run ConstructElement against the now-populated cache.
    /// </summary>
    public Action? RequestRedraw { get; set; }

    /// <summary>
    /// UI thread only. Returns the frozen bitmap once loaded; otherwise null, with
    /// <paramref name="inFlight"/> true while a fetch is running (caller shows the loading
    /// placeholder) and false for a cached failure (broken placeholder). Starts a fetch on a
    /// first sighting.
    /// </summary>
    public BitmapImage? GetOrStartFetch(Uri uri, out bool inFlight)
    {
        var key = uri.AbsoluteUri;
        if (_entries.TryGetValue(key, out var entry))
        {
            inFlight = entry.State == RemoteImageState.InFlight;
            return entry.Bitmap;
        }

        // Marked in-flight synchronously, before the fetch starts — that ordering is what makes
        // dedupe airtight without a lock. A second ConstructElement for the same URL (another
        // line in this same render pass, or a later one) sees InFlight and starts nothing.
        _entries[key] = new RemoteImageEntry(RemoteImageState.InFlight, null);
        inFlight = true;
        _ = FetchAsync(uri, _generation, _cts.Token);
        return null;
    }

    /// <summary>
    /// UI thread only. Cancels every in-flight fetch, drops every cached entry, and makes any
    /// completion still in transit a no-op. Called when the remote opt-in changes in either
    /// direction and when the document changes.
    /// </summary>
    public void Invalidate()
    {
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        // Bumped so a completion already on its way back discards instead of repopulating.
        _generation++;
        _entries.Clear();
        // _redrawQueued is deliberately left alone: a queued redraw after an invalidate is both
        // harmless and wanted — it's what repaints the now-cleared images.
    }

    /// <summary>
    /// Whether a response's media type is worth handing to the image decoder.
    /// application/octet-stream is accepted because it is the common "server didn't know"
    /// answer for a legitimately served image; a missing type is rejected, since decoding
    /// unknown bytes from an unknown host buys nothing. text/html is the case that matters — a
    /// 200-with-an-error-page or a captive portal must land on the broken placeholder rather
    /// than in the decoder. image/svg+xml passes here and then fails to decode (WPF has no SVG
    /// decoder), which is the honest outcome and deliberately not special-cased.
    /// </summary>
    internal static bool IsAcceptableImageContentType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType)) return false;
        return mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads <paramref name="source"/> fully, or returns null the moment it exceeds
    /// <paramref name="maxBytes"/>. The cap is inclusive: exactly maxBytes is accepted. The
    /// running total is checked per read rather than trusting any declared length, so a chunked
    /// or mis-declared body cannot slip past it.
    /// </summary>
    internal static async Task<byte[]?> ReadCappedAsync(Stream source, int maxBytes, CancellationToken token)
    {
        var buffer = new byte[8192];
        using var accumulated = new MemoryStream();
        int total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxBytes) return null;
            accumulated.Write(buffer, 0, read);
        }
        return accumulated.ToArray();
    }

    private async Task FetchAsync(Uri uri, int generation, CancellationToken token)
    {
        BitmapImage? bitmap = null;
        try
        {
            using var response = await Http
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);

            // Headers first, so a bad status, a non-image type, or an oversized declared length
            // costs no body bytes at all.
            if (response.IsSuccessStatusCode
                && IsAcceptableImageContentType(response.Content.Headers.ContentType?.MediaType)
                && response.Content.Headers.ContentLength is not > MaxImageBytes)
            {
                using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                var bytes = await ReadCappedAsync(stream, MaxImageBytes, token).ConfigureAwait(false);
                // Decoded here, off the UI thread, and frozen before it crosses back.
                if (bytes is not null) bitmap = ImageElementGenerator.DecodeFrozen(new MemoryStream(bytes));
            }
        }
        catch (Exception)
        {
            // Failure modes here are diverse — HttpRequestException, TaskCanceledException (the
            // timeout), OperationCanceledException (Invalidate), IOException, decode errors —
            // and every one means the same thing: the broken placeholder, never a crash. This is
            // phase 1's catch-all extended to the network.
            bitmap = null;
        }

        // Posted outside the catch so a failure still records Failed — otherwise the entry would
        // sit InFlight forever and show a loading box that never resolves.
        try
        {
            if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
            // Discarded deliberately: DispatcherOperation is awaitable, so inside this async
            // method an un-discarded call would raise CS4014. There is nothing to wait for — the
            // post is the end of this fetch's work.
            _ = _dispatcher.BeginInvoke(DispatcherPriority.Normal,
                new Action(() => OnCompleted(uri.AbsoluteUri, generation, bitmap)));
        }
        catch (Exception)
        {
            // The dispatcher can begin shutting down between the check and the post — a fetch
            // outliving window close must not throw.
        }
    }

    // UI thread.
    private void OnCompleted(string key, int generation, BitmapImage? bitmap)
    {
        // A completion from before an Invalidate must not repopulate the cache or repaint.
        if (generation != _generation) return;

        _entries[key] = bitmap is null
            ? new RemoteImageEntry(RemoteImageState.Failed, null)
            : new RemoteImageEntry(RemoteImageState.Loaded, bitmap);

        RequestCoalescedRedraw();
    }

    // UI thread. See the coalescing note in the class remarks.
    private void RequestCoalescedRedraw()
    {
        if (_redrawQueued) return;
        _redrawQueued = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _redrawQueued = false;
            RequestRedraw?.Invoke();
        }));
    }
}
