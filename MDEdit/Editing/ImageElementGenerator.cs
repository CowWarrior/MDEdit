using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit.Editing;

/// <summary>
/// Live-preview element generator: renders an image reference ("![alt](path)") as the actual
/// picture, replacing the whole span, except when the caret is inside it — revealing the raw text
/// so it can be edited. Per-span reveal with a visible replacement, the
/// <see cref="EmojiElementGenerator"/> template — but the first inline element that isn't
/// text-sized: the containing line grows to fit the picture, which works because AvalonEdit
/// measures inline objects with an infinite constraint and reports the object's height as its
/// baseline.
/// </summary>
/// <remarks>
/// Local files render unconditionally. Remote http(s) images render only while the
/// <see cref="LoadRemoteImages"/> opt-in (View > Load Remote Images) is on — off by default,
/// because fetching one just from having a document open discloses to its host that the document
/// was opened. <see cref="RemoteEnabled"/> is the single gate for that, consulted from
/// <c>IsRenderable</c>, which is itself the single shared gate both
/// <c>GetFirstInterestedOffset</c> and <c>ConstructElement</c> run through — deliberately not a
/// second flag on <see cref="RemoteImageLoader"/>, since two gates that must agree is a standing
/// failure mode in this codebase. Because the local resolver is tried first and short-circuits,
/// and the caret check comes before both, a local image never parses as a URL and clicking into
/// an image's source never starts a fetch.
///
/// The decline-vs-placeholder contract: this generator <em>declines</em> a span (returns no
/// element, so <see cref="LinkMarkerElementGenerator"/> renders it as marker-hidden alt text,
/// exactly as before this class existed) whenever the target isn't ours to render — a relative
/// path with no document directory (unsaved document), an unparseable path, a non-http(s)
/// scheme, or any remote URL while the opt-in is off; see <see cref="ImagePathResolver"/>. The
/// drawn placeholders are reserved for a target that <em>did</em> resolve: the broken kind for
/// one that then failed to load (missing file, HTTP error, decode error), the loading kind while
/// a remote fetch is in flight. Both are drawn shapes rather than glyphs for
/// <see cref="TaskListMarkerElementGenerator"/>'s reason — no dependency on the editor font
/// carrying any particular character — and both show the alt text beside the icon, since a bare
/// icon would lose the only information the author wrote.
///
/// Local bitmaps are cached by resolved full path — deliberately <em>not</em> keyed on
/// <c>TextDocument.Version</c> like <see cref="TableRowElementGenerator"/>'s layout cache, which
/// would re-decode every image on every keystroke. Failures are cached too (as null) so a missing
/// file isn't re-probed on every redraw; the cache clears when <see cref="DocumentDirectory"/>
/// changes, which is also how failures recover. Known v1 limitation: an image edited on disk
/// won't refresh until a different document is opened. Remote images have their own cache with
/// the same lifetime, inside <see cref="RemoteImageLoader"/>.
///
/// A fresh <see cref="Image"/>/placeholder element is built per <c>ConstructElement</c> call —
/// the TextView takes each inline object as a visual child, so instances can't be shared across
/// visual lines; the frozen <see cref="BitmapImage"/> is the shareable, cached thing.
///
/// The colorizer needs no image handling: revealed raw "![alt](path)" text already gets the
/// per-span mono-font swap because <c>ApplyRevealedSourceFont</c> consumes the same
/// <c>FindLinkSpans</c>, and a rendered span has no visible text left to style.
///
/// The span's characters keep their document offsets, so selection, undo, and the saved file are
/// unaffected — only the rendering changes.
/// </remarks>
internal sealed class ImageElementGenerator : VisualLineElementGenerator
{
    /// <summary>The sizing clamp: natural size up to this height, scaled down uniformly above it.</summary>
    internal const double MaxImageHeight = 300.0;

    /// <summary>
    /// Decode-memory cap for oversized sources, 2x the display clamp for high-DPI/zoom headroom.
    /// Applied only when the source is taller — unconditional DecodePixelHeight would upscale
    /// smaller images' decoded bitmaps and break natural-size rendering.
    /// </summary>
    internal const int DecodeHeightCap = (int)(MaxImageHeight * 2);

    public bool Enabled { get; set; }
    public int CaretOffset { get; set; } = -1;

    private readonly RemoteImageLoader _remote = new();

    private bool _loadRemoteImages;

    /// <summary>
    /// The View > Load Remote Images opt-in (AppSettings.LoadRemoteImages), off by default. Half
    /// of <see cref="RemoteEnabled"/>. Changing it in either direction invalidates the remote
    /// loader: turning it off cancels every in-flight fetch and drops every cached bitmap, so the
    /// setting takes effect immediately rather than at the next document open.
    /// </summary>
    public bool LoadRemoteImages
    {
        get => _loadRemoteImages;
        set
        {
            if (_loadRemoteImages == value) return;
            _loadRemoteImages = value;
            _remote.Invalidate();
        }
    }

    /// <summary>
    /// Pushed in by MainWindow and passed straight through to the loader (a pass-through rather
    /// than a second stored field, so there is no duplicated state to keep in sync). A remote
    /// bitmap arrives long after ConstructElement returned, and AvalonEdit captures an inline
    /// object's DesiredSize at construction time — so a late arrival cannot resize its own line
    /// and has to ask for a redraw instead.
    /// </summary>
    public Action? RequestRedraw
    {
        get => _remote.RequestRedraw;
        set => _remote.RequestRedraw = value;
    }

    // The single remote gate. Evaluated before the URL is even parsed, so with the opt-in off
    // nothing about a remote target is inspected and no request can be issued.
    private bool RemoteEnabled => Enabled && LoadRemoteImages;

    private string? _documentDirectory;

    /// <summary>
    /// The open document's directory, against which relative paths resolve; null for an unsaved
    /// document. Fed by MainWindow from FileService.CurrentPath — the first generator to need
    /// anything from there.
    /// </summary>
    public string? DocumentDirectory
    {
        get => _documentDirectory;
        set
        {
            if (string.Equals(_documentDirectory, value, StringComparison.OrdinalIgnoreCase)) return;
            _documentDirectory = value;
            _bitmaps.Clear();
            // A different document means a different URL set, and a completion still in transit
            // must not repaint the new one.
            _remote.Invalidate();
        }
    }

    // Resolved full path -> frozen bitmap, or null for a cached load failure.
    private readonly Dictionary<string, BitmapImage?> _bitmaps = new(StringComparer.OrdinalIgnoreCase);

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!Enabled) return -1;

        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(startOffset);

        foreach (var span in MarkdownSyntax.FindLinkSpans(doc, line))
        {
            if (!IsRenderable(span)) continue;
            if (span.Start >= startOffset) return span.Start;
        }

        return -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var doc  = CurrentContext.Document;
        var line = doc.GetLineByOffset(offset);

        foreach (var span in MarkdownSyntax.FindLinkSpans(doc, line))
        {
            if (!IsRenderable(span)) continue;
            if (offset != span.Start) continue;

            var alt = doc.GetText(span.TextStart, span.TextEnd - span.TextStart);
            var inFlight = false;
            BitmapImage? bitmap;

            if (ImagePathResolver.TryResolve(span.Url!, DocumentDirectory) is { } localPath)
            {
                bitmap = GetBitmap(localPath);
            }
            else
            {
                // True by IsRenderable's construction — it accepted this span, and the local
                // branch above didn't claim it.
                ImagePathResolver.TryResolveRemote(span.Url!, out var uri);
                bitmap = _remote.GetOrStartFetch(uri!, out inFlight);
            }

            if (bitmap is not null)
            {
                // MaxHeight rather than Height: under AvalonEdit's infinite measure constraint,
                // Max* is what clamps — the image keeps its natural size below the cap.
                return new InlineObjectElement(span.End - span.Start, new Image
                {
                    Source    = bitmap,
                    MaxHeight = MaxImageHeight,
                    Stretch   = Stretch.Uniform,
                });
            }

            return new InlineObjectElement(span.End - span.Start,
                BuildImagePlaceholder(CurrentContext.GlobalTextRunProperties.FontRenderingEmSize,
                                      CurrentContext.GlobalTextRunProperties.ForegroundBrush,
                                      CurrentContext.GlobalTextRunProperties.Typeface,
                                      alt,
                                      inFlight ? PlaceholderKind.Loading : PlaceholderKind.Broken));
        }

        // GetFirstInterestedOffset only ever returns offsets this method recognizes, so this is
        // unreachable in practice — return a harmless zero-length element rather than throw.
        return new InlineObjectElement(0, new TextBlock());
    }

    // Ours to render: an image span, not currently revealed under the caret, whose target either
    // resolves to a local path or is a remote http(s) URL while the opt-in is on. Everything else
    // falls through to LinkMarkerElementGenerator, which is registered after this generator so it
    // only ever wins a declined span.
    //
    // Two orderings here are load-bearing: the local resolver runs first and short-circuits, so
    // the phase-1 local contract is untouched and a local path never parses as a Uri; and
    // RemoteEnabled is evaluated before TryResolveRemote, so with the opt-in off nothing about a
    // remote URL is inspected at all.
    private bool IsRenderable(LinkSpan span) =>
        span is { IsImage: true, Url: not null }
        && !IsCaretInside(span)
        && (ImagePathResolver.TryResolve(span.Url, DocumentDirectory) is not null
            || (RemoteEnabled && ImagePathResolver.TryResolveRemote(span.Url, out _)));

    private bool IsCaretInside(LinkSpan span) => CaretOffset >= span.Start && CaretOffset <= span.End;

    private BitmapImage? GetBitmap(string path)
    {
        if (_bitmaps.TryGetValue(path, out var cached)) return cached;
        var bitmap = LoadBitmap(path);
        _bitmaps[path] = bitmap;
        return bitmap;
    }

    private static BitmapImage? LoadBitmap(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return DecodeFrozen(File.OpenRead(path));
        }
        catch
        {
            // Opening can fail on its own (locked, permission denied) before decoding starts.
            return null;
        }
    }

    /// <summary>
    /// Decodes a stream into a frozen bitmap, or null on any failure. Shared by the local and
    /// remote paths so the two can never drift on decode settings. Takes ownership of the
    /// stream.
    /// </summary>
    /// <remarks>
    /// Freezing is what makes this safe to call off the UI thread (the remote path does): a
    /// BitmapImage built on any thread and frozen can be handed to the UI thread, which is the
    /// load-bearing fact behind <see cref="RemoteImageLoader"/>'s whole design. An unfrozen one
    /// throws at render time, far from the cause — hence the explicit CanFreeze guard rather
    /// than assuming it.
    /// </remarks>
    internal static BitmapImage? DecodeFrozen(Stream stream)
    {
        try
        {
            using (stream)
            {
                // Header-only read to learn the source height before deciding whether to cap the
                // decode (see DecodeHeightCap).
                int sourceHeight = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.None).Frames[0].PixelHeight;
                stream.Position = 0;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = stream;
                // Decode fully now so the stream can close with no lingering file lock.
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                if (sourceHeight > DecodeHeightCap) bitmap.DecodePixelHeight = DecodeHeightCap;
                bitmap.EndInit();
                if (!bitmap.CanFreeze) return null;
                bitmap.Freeze();   // frozen = shareable across visual lines, and across threads
                return bitmap;
            }
        }
        catch
        {
            // Decode failures are diverse (IO, format, argument…); any failure means the broken
            // placeholder, never a crash mid-render.
            return null;
        }
    }

    private enum PlaceholderKind { Broken, Loading }

    // Drawn shapes rather than a glyph, styled entirely from the editor's run properties so it
    // follows theme and font size with no per-theme wiring in MainWindow.
    //
    // The outer border, the icon frame, the sizing and the alt-text label are identical across
    // both kinds, so a loading box resolving to a broken one never changes the line's height.
    // The two are told apart by SHAPE, not colour: colour comes from the theme here and can't be
    // assumed to contrast usefully in both.
    //
    // Rejected for the loading kind: an animated spinner (a hosted object is rebuilt on every
    // redraw and torn down on scroll, so the animation would restart constantly, and it would put
    // a running clock per visible image into the render loop); a literal "Loading…" string (it
    // competes with the alt text for the same line and would need localizing); and reserving
    // MaxImageHeight of blank space (most images are shorter than the cap, so it shows a huge
    // empty box for a small icon and makes an image-heavy document unreadable while it loads).
    private static FrameworkElement BuildImagePlaceholder(
        double emSize, Brush foreground, Typeface typeface, string altText, PlaceholderKind kind)
    {
        double side = Math.Round(emSize * 1.1);
        double strokeThickness = Math.Max(1.0, side / 10);

        var icon = new Grid
        {
            Width  = side,
            Height = side,
            Margin = new Thickness(0, 0, Math.Round(emSize * 0.3), 0),
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
        };

        // The photo frame — common to both kinds.
        icon.Children.Add(new Border
        {
            BorderBrush     = foreground,
            BorderThickness = new Thickness(Math.Max(1.0, Math.Round(side / 12))),
            CornerRadius    = new CornerRadius(Math.Max(1.0, side / 8)),
            Background      = Brushes.Transparent,
        });

        if (kind == PlaceholderKind.Broken)
        {
            // The mountains.
            icon.Children.Add(new Polyline
            {
                Points =
                [
                    new Point(side * 0.15, side * 0.78),
                    new Point(side * 0.38, side * 0.45),
                    new Point(side * 0.55, side * 0.62),
                    new Point(side * 0.78, side * 0.35),
                ],
                Stroke             = foreground,
                StrokeThickness    = strokeThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap   = PenLineCap.Round,
                StrokeLineJoin     = PenLineJoin.Round,
            });

            // The diagonal slash — the "broken" affordance.
            icon.Children.Add(new Line
            {
                X1 = side * 0.10, Y1 = side * 0.90,
                X2 = side * 0.90, Y2 = side * 0.10,
                Stroke             = foreground,
                StrokeThickness    = strokeThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap   = PenLineCap.Round,
            });
        }
        else
        {
            // Three dots — the universal "working" mark. No mountains and no slash, so the two
            // kinds are distinguishable at a glance without relying on colour.
            double dot = Math.Max(1.0, Math.Round(side / 6));
            double gap = Math.Max(1.0, side / 10);
            var dots = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            };
            for (int i = 0; i < 3; i++)
            {
                dots.Children.Add(new Ellipse
                {
                    Width  = dot,
                    Height = dot,
                    Fill   = foreground,
                    Margin = i == 1 ? new Thickness(gap, 0, gap, 0) : default,
                });
            }
            icon.Children.Add(dots);

            // Reads as "not there yet" rather than "wrong", and stays theme-correct.
            icon.Opacity = 0.6;
        }

        var label = new TextBlock
        {
            Text        = altText,
            FontFamily  = typeface.FontFamily,
            FontStyle   = typeface.Style,
            FontWeight  = typeface.Weight,
            FontStretch = typeface.Stretch,
            FontSize    = emSize,
            Foreground  = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(icon);
        panel.Children.Add(label);

        return new Border
        {
            BorderBrush     = foreground,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(2),
            Padding         = new Thickness(Math.Round(emSize * 0.3), Math.Round(emSize * 0.15), Math.Round(emSize * 0.3), Math.Round(emSize * 0.15)),
            Background      = Brushes.Transparent,
            Child           = panel,
        };
    }
}
