using System.IO;
using MDEdit.Editing;

namespace MDEdit.Tests;

// Covers the loader's two pure guards. Deliberately no network I/O, no WPF elements and no
// dispatcher: the state machine around them is WPF-bound and belongs to the manual checklist,
// per the standing rule that element generators aren't tested directly.
public class RemoteImageLoaderTests
{
    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/gif")]
    [InlineData("image/webp")]
    [InlineData("image/svg+xml")]   // passes here, then fails to decode — WPF has no SVG decoder
    [InlineData("IMAGE/PNG")]
    public void IsAcceptableImageContentType_ImageTypes_Accepted(string mediaType)
    {
        Assert.True(RemoteImageLoader.IsAcceptableImageContentType(mediaType));
    }

    // The common "server didn't know" answer for a legitimately served image.
    [Fact]
    public void IsAcceptableImageContentType_OctetStream_Accepted()
    {
        Assert.True(RemoteImageLoader.IsAcceptableImageContentType("application/octet-stream"));
    }

    // text/html is the case that matters: a 200-with-an-error-page or a captive portal must land
    // on the broken placeholder rather than being handed to the decoder.
    [Theory]
    [InlineData("text/html")]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    [InlineData("application/pdf")]
    public void IsAcceptableImageContentType_NonImageTypes_Rejected(string mediaType)
    {
        Assert.False(RemoteImageLoader.IsAcceptableImageContentType(mediaType));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAcceptableImageContentType_MissingOrEmpty_Rejected(string? mediaType)
    {
        Assert.False(RemoteImageLoader.IsAcceptableImageContentType(mediaType));
    }

    [Fact]
    public async Task ReadCappedAsync_UnderCap_ReturnsBytes()
    {
        var payload = Payload(100);

        var result = await RemoteImageLoader.ReadCappedAsync(new MemoryStream(payload), 1000, default);

        Assert.Equal(payload, result);
    }

    // The cap is inclusive.
    [Fact]
    public async Task ReadCappedAsync_ExactlyAtCap_ReturnsBytes()
    {
        var payload = Payload(1000);

        var result = await RemoteImageLoader.ReadCappedAsync(new MemoryStream(payload), 1000, default);

        Assert.Equal(payload, result);
    }

    [Fact]
    public async Task ReadCappedAsync_OneByteOverCap_ReturnsNull()
    {
        var result = await RemoteImageLoader.ReadCappedAsync(new MemoryStream(Payload(1001)), 1000, default);

        Assert.Null(result);
    }

    // Through a stream that never returns more than a few bytes per read, so the cap cannot be
    // tripped by a single large read — the shape a real chunked HTTP body arrives in.
    [Fact]
    public async Task ReadCappedAsync_ChunkedSource_StillTripsCap()
    {
        var result = await RemoteImageLoader.ReadCappedAsync(new ChunkedStream(Payload(1001)), 1000, default);

        Assert.Null(result);
    }

    // A zero-byte body reads fine and then fails at decode; only the read layer is asserted here.
    [Fact]
    public async Task ReadCappedAsync_EmptySource_ReturnsEmptyArray()
    {
        var result = await RemoteImageLoader.ReadCappedAsync(new MemoryStream([]), 1000, default);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    private static byte[] Payload(int length)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = (byte)(i % 256);
        return bytes;
    }

    // Hands back at most 7 bytes per Read so the cap check cannot pass by accident.
    private sealed class ChunkedStream(byte[] data) : Stream
    {
        private const int ChunkSize = 7;
        private int _position;

        public override int Read(byte[] buffer, int offset, int count)
        {
            int remaining = data.Length - _position;
            if (remaining <= 0) return 0;
            int toCopy = Math.Min(Math.Min(count, ChunkSize), remaining);
            Array.Copy(data, _position, buffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
