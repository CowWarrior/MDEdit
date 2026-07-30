using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit.Editing;

/// <summary>
/// Live-preview element generator: renders a local image reference ("![alt](path)") as the actual
/// picture, replacing the whole span, except when the caret is inside it — revealing the raw text
/// so it can be edited. Per-span reveal with a visible replacement, the
/// <see cref="EmojiElementGenerator"/> template — but the first inline element that isn't
/// text-sized: the containing line grows to fit the picture, which works because AvalonEdit
/// measures inline objects with an infinite constraint and reports the object's height as its
/// baseline.
/// </summary>
/// <remarks>
/// The decline-vs-placeholder contract: this generator <em>declines</em> a span (returns no
/// element, so <see cref="LinkMarkerElementGenerator"/> renders it as marker-hidden alt text,
/// exactly as before this class existed) whenever the target isn't ours to render — a remote
/// scheme, a relative path with no document directory (unsaved document), or an unparseable
/// path; see <see cref="ImagePathResolver"/>. The drawn broken-image placeholder is reserved for
/// a genuinely resolved local path that then failed to load (missing file, decode error). The
/// placeholder is drawn shapes rather than a glyph for <see cref="TaskListMarkerElementGenerator"/>'s
/// reason — no dependency on the editor font carrying any particular character — and shows the
/// alt text beside the icon, since a bare icon would lose the only information the author wrote.
///
/// Decoded bitmaps are cached by resolved full path — deliberately <em>not</em> keyed on
/// <c>TextDocument.Version</c> like <see cref="TableRowElementGenerator"/>'s layout cache, which
/// would re-decode every image on every keystroke. Failures are cached too (as null) so a missing
/// file isn't re-probed on every redraw; the cache clears when <see cref="DocumentDirectory"/>
/// changes, which is also how failures recover. Known v1 limitation: an image edited on disk
/// won't refresh until a different document is opened.
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

            var resolved = ImagePathResolver.TryResolve(span.Url!, DocumentDirectory)!;
            var bitmap = GetBitmap(resolved);
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

            var alt = doc.GetText(span.TextStart, span.TextEnd - span.TextStart);
            return new InlineObjectElement(span.End - span.Start,
                BuildBrokenImagePlaceholder(CurrentContext.GlobalTextRunProperties.FontRenderingEmSize,
                                            CurrentContext.GlobalTextRunProperties.ForegroundBrush,
                                            CurrentContext.GlobalTextRunProperties.Typeface,
                                            alt));
        }

        // GetFirstInterestedOffset only ever returns offsets this method recognizes, so this is
        // unreachable in practice — return a harmless zero-length element rather than throw.
        return new InlineObjectElement(0, new TextBlock());
    }

    // Ours to render: an image span with a target that resolves to a local path, not currently
    // revealed under the caret. Everything else falls through to LinkMarkerElementGenerator,
    // which is registered after this generator so it only ever wins a declined span.
    private bool IsRenderable(LinkSpan span) =>
        span is { IsImage: true, Url: not null }
        && !IsCaretInside(span)
        && ImagePathResolver.TryResolve(span.Url, DocumentDirectory) is not null;

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
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);

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
            bitmap.Freeze();   // frozen = shareable across visual lines
            return bitmap;
        }
        catch
        {
            // Decode failures are diverse (IO, format, argument…); any failure means the broken
            // placeholder, never a crash mid-render.
            return null;
        }
    }

    // Drawn shapes rather than a glyph, styled entirely from the editor's run properties so it
    // follows theme and font size with no per-theme wiring in MainWindow.
    private static FrameworkElement BuildBrokenImagePlaceholder(double emSize, Brush foreground, Typeface typeface, string altText)
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

        // The photo frame.
        icon.Children.Add(new Border
        {
            BorderBrush     = foreground,
            BorderThickness = new Thickness(Math.Max(1.0, Math.Round(side / 12))),
            CornerRadius    = new CornerRadius(Math.Max(1.0, side / 8)),
            Background      = Brushes.Transparent,
        });

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
