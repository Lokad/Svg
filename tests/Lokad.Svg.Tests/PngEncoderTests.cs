using Lokad.Svg.Encoding;
using Lokad.Svg.Rendering;
using Lokad.Svg.Testing;
using Lokad.Svg.VisualDiff;
using System.Buffers.Binary;
using System.IO.Compression;

namespace Lokad.Svg.Tests;

public sealed class PngEncoderTests
{
    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Encodes_a_valid_rgba8_png_with_straight_alpha_pixels()
    {
        byte[] png = PngEncoder.Encode(1, 1, [10, 20, 30, 40], CancellationToken.None);

        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20)));
        Assert.Equal("IDAT", System.Text.Encoding.ASCII.GetString(png, 37, 4));
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Multiple_scanlines_keep_unfiltered_straight_rgba_and_clear_only_zero_alpha_rgb()
    {
        byte[] png = PngEncoder.Encode(3, 2,
            [10, 20, 30, 40, 99, 88, 77, 0, 255, 0, 128, 255,
             3, 4, 5, 1, 200, 50, 10, 0, 1, 2, 3, 254], CancellationToken.None);

        Assert.Equal(new byte[]
        {
            10, 20, 30, 40, 0, 0, 0, 0, 255, 0, 128, 255,
            3, 4, 5, 1, 0, 0, 0, 0, 1, 2, 3, 254
        }, PngRgbaDecoder.Decode(png, 6).Pixels);

        int dataLength = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33));
        using MemoryStream compressed = new(png, 41, dataLength);
        using ZLibStream scanlines = new(compressed, CompressionMode.Decompress);
        byte[] rows = new byte[26];
        scanlines.ReadExactly(rows);
        Assert.Equal(new byte[]
        {
            0, 10, 20, 30, 40, 0, 0, 0, 0, 255, 0, 128, 255,
            0, 3, 4, 5, 1, 0, 0, 0, 0, 1, 2, 3, 254
        }, rows);
        Assert.Equal(-1, scanlines.ReadByte());
    }

    [Theory]
    [InlineData(0, 1, 4, typeof(ArgumentOutOfRangeException), "width")]
    [InlineData(1, 0, 4, typeof(ArgumentOutOfRangeException), "height")]
    [InlineData(1, 1, 3, typeof(ArgumentException), "rgba")]
    [Trait("Layer", "Mechanism")]
    public void Rejects_invalid_pixel_buffers(int width, int height, int byteCount, Type errorType, string parameterName)
    {
        Exception exception = Assert.Throws(errorType, () => PngEncoder.Encode(width, height, new byte[byteCount], CancellationToken.None));

        Assert.Equal(parameterName, ((ArgumentException)exception).ParamName);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Cancellation_is_checked_before_scanline_encoding()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => PngEncoder.Encode(1, 1, [0, 0, 0, 0], cancellation.Token));
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Stream_sinks_emit_byte_identical_output_with_owned_memory()
    {
        byte[] pixels = CreateHighEntropyPixels();
        byte[] owned = PngEncoder.Encode(512, 512, pixels, CancellationToken.None);
        using MemoryStream synchronousDestination = new();
        int synchronousWritten = PngEncoder.EncodeTo(512, 512, pixels, synchronousDestination, new RenderBudget(new HostMemoryBudget(long.MaxValue)), owned.Length, CancellationToken.None);
        using MemoryStream asynchronousDestination = new();
        int asynchronousWritten = await PngEncoder.EncodeToAsync(512, 512, pixels, asynchronousDestination, new RenderBudget(new HostMemoryBudget(long.MaxValue)), owned.Length, CancellationToken.None);

        Assert.Equal(owned.Length, synchronousWritten);
        Assert.Equal(owned, synchronousDestination.ToArray());
        Assert.Equal(owned.Length, asynchronousWritten);
        Assert.Equal(owned, asynchronousDestination.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Contract")]
    public async Task Stream_buffer_growth_refusal_releases_scratch_and_allows_retry(bool asynchronous)
    {
        byte[] pixels = CreateHighEntropyPixels();
        byte[] expected = PngEncoder.Encode(512, 512, pixels, CancellationToken.None);
        using TestMemoryBudget root = new(32_768);
        RenderBudget budget = new(root);
        using MemoryStream refused = new();
        SvgException failure = asynchronous
            ? await Assert.ThrowsAsync<SvgException>(() => PngEncoder.EncodeToAsync(512, 512, pixels, refused, budget, int.MaxValue, CancellationToken.None))
            : Assert.Throws<SvgException>(() => PngEncoder.EncodeTo(512, 512, pixels, refused, budget, int.MaxValue, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, failure.ErrorCode);
        Assert.Equal(1, budget.Snapshot().DeniedReservationCount);
        Assert.True(budget.Snapshot().PeakBytes > 512 * 4 + 1);
        Assert.Equal(0, root.Reserved);
        Assert.True(refused.CanWrite);
        root.Limit = 8_000_000;
        using MemoryStream retry = new();
        int written = asynchronous
            ? await PngEncoder.EncodeToAsync(512, 512, pixels, retry, budget, int.MaxValue, CancellationToken.None)
            : PngEncoder.EncodeTo(512, 512, pixels, retry, budget, int.MaxValue, CancellationToken.None);
        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, retry.ToArray());
        Assert.Equal(0, root.Reserved);
        Assert.False(root.Disposed);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Encodes_directly_into_caller_destination()
    {
        byte[] owned = PngEncoder.Encode(1, 1, [1, 2, 3, 4], CancellationToken.None);
        byte[] destination = new byte[owned.Length];

        int written = PngEncoder.EncodeInto(1, 1, [1, 2, 3, 4], destination, CancellationToken.None);

        Assert.Equal(owned.Length, written);
        Assert.Equal(owned, destination);
        Assert.Throws<SvgException>(() => PngEncoder.EncodeInto(1, 1, [1, 2, 3, 4], new byte[1], CancellationToken.None));
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Streams_large_png_data_in_bounded_writes()
    {
        byte[] pixels = CreateHighEntropyPixels();
        using BoundedWriteStream destination = new(65_536);

        int written = PngEncoder.EncodeTo(512, 512, pixels, destination, new RenderBudget(new HostMemoryBudget(long.MaxValue)), int.MaxValue, CancellationToken.None);

        Assert.Equal(written, destination.Length);
        Assert.True(written > 65_536);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Streams_without_retaining_the_full_compressed_payload()
    {
        byte[] pixels = CreateHighEntropyPixels();
        using BoundedWriteStream destination = new(65_536);

        // Covers the 64 KiB aggregation cap plus one growth transient (measured near 100 KiB),
        // still far below the full compressed payload this test refuses to retain.
        int written = PngEncoder.EncodeTo(512, 512, pixels, destination, new RenderBudget(new HostMemoryBudget(110_000)), int.MaxValue, CancellationToken.None);

        Assert.Equal(written, destination.Length);
        Assert.True(written > 65_536);
        Assert.True(CountBoundedIdatChunks(destination.ToArray()) > 1);

        byte[] expected = destination.ToArray();
        using BoundedWriteStream exactDestination = new(65_536);
        int exactWritten = PngEncoder.EncodeTo(512, 512, pixels, exactDestination, new RenderBudget(new HostMemoryBudget(long.MaxValue)), expected.Length, CancellationToken.None);
        using BoundedWriteStream insufficientDestination = new(65_536);
        SvgException exception = Assert.Throws<SvgException>(() => PngEncoder.EncodeTo(512, 512, pixels, insufficientDestination, new RenderBudget(new HostMemoryBudget(long.MaxValue)), expected.Length - 1, CancellationToken.None));

        Assert.Equal(expected.Length, exactWritten);
        Assert.Equal(expected, exactDestination.ToArray());
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Synchronous_stream_encoding_propagates_destination_write_failures()
    {
        using FailingWriteStream destination = new();

        IOException exception = Assert.Throws<IOException>(() => PngEncoder.EncodeTo(1, 1, new byte[4], destination, new RenderBudget(new HostMemoryBudget(long.MaxValue)), int.MaxValue, CancellationToken.None));

        Assert.Equal("destination write failed", exception.Message);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Streams_large_png_data_in_bounded_asynchronous_writes()
    {
        byte[] pixels = CreateHighEntropyPixels();
        using BoundedAsyncWriteStream destination = new(65_536);

        // Covers the 64 KiB aggregation cap plus one growth transient, like the synchronous form.
        int written = await PngEncoder.EncodeToAsync(512, 512, pixels, destination, new RenderBudget(new HostMemoryBudget(110_000)), int.MaxValue, CancellationToken.None);

        Assert.Equal(written, destination.Length);
        Assert.True(written > 65_536);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Reserves_async_stream_chunk_scratch_before_writing()
    {
        using BoundedAsyncWriteStream destination = new(65_536);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await PngEncoder.EncodeToAsync(1, 1, new byte[4], destination, new RenderBudget(new HostMemoryBudget(132)), int.MaxValue, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Asynchronous_stream_encoding_propagates_destination_write_failures()
    {
        using FailingWriteStream destination = new();

        IOException exception = await Assert.ThrowsAsync<IOException>(async () => await PngEncoder.EncodeToAsync(1, 1, new byte[4], destination, new RenderBudget(new HostMemoryBudget(long.MaxValue)), int.MaxValue, CancellationToken.None));

        Assert.Equal("destination write failed", exception.Message);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Async_stream_encoding_observes_cancellation_after_output_begins()
    {
        using CancellationTokenSource cancellation = new();
        using CancellingAsyncWriteStream destination = new(cancellation);

        Task<int> task = PngEncoder.EncodeToAsync(1, 1, new byte[4], destination, new RenderBudget(new HostMemoryBudget(long.MaxValue)), int.MaxValue, cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        Assert.True(task.IsCanceled);
        Assert.Equal(8, destination.Length);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Async_stream_encoding_waits_for_destination_backpressure()
    {
        using StallingAsyncWriteStream destination = new();

        Task<int> task = PngEncoder.EncodeToAsync(1, 1, new byte[4], destination, new RenderBudget(new HostMemoryBudget(long.MaxValue)), int.MaxValue, CancellationToken.None);
        await destination.FirstWriteStarted;
        try
        {
            Assert.False(task.IsCompleted);
        }
        finally
        {
            destination.CompleteFirstWrite();
        }

        int written = await task;
        Assert.Equal(written, destination.Length);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Splits_large_compressed_data_into_bounded_IDAT_chunks()
    {
        byte[] pixels = CreateHighEntropyPixels();
        byte[] png = PngEncoder.Encode(512, 512, pixels, CancellationToken.None);
        int idatChunkCount = CountBoundedIdatChunks(png);

        byte[] exactCap = PngEncoder.Encode(512, 512, pixels, png.Length, CancellationToken.None);
        SvgException exception = Assert.Throws<SvgException>(() => PngEncoder.Encode(512, 512, pixels, png.Length - 1, CancellationToken.None));

        Assert.True(idatChunkCount > 1);
        Assert.Equal(png, exactCap);
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Theory]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(180)]
    [InlineData(181)]
    [Trait("Layer", "Contract")]
    public void Owned_and_caller_memory_limits_match_stream_output_near_idat_boundaries(int side)
    {
        byte[] pixels = new byte[side * side * 4];
        uint state = 0x12345678;
        for (int index = 0; index < pixels.Length; index++) { state = state * 1664525 + 1013904223; pixels[index] = (byte)(state >> 24); }
        using MemoryStream stream = new();
        int length = PngEncoder.EncodeTo(side, side, pixels, stream, new RenderBudget(new HostMemoryBudget(long.MaxValue)), int.MaxValue, CancellationToken.None);
        byte[] expected = stream.ToArray();
        byte[] destination = new byte[length];

        Assert.Equal(expected, PngEncoder.Encode(side, side, pixels, length, CancellationToken.None));
        Assert.Equal(length, PngEncoder.EncodeInto(side, side, pixels, destination, length, CancellationToken.None));
        Assert.Equal(expected, destination);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => PngEncoder.Encode(side, side, pixels, length - 1, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => PngEncoder.EncodeInto(side, side, pixels, destination, length - 1, CancellationToken.None)).ErrorCode);
        Assert.True(CountBoundedIdatChunks(expected) >= 1);
    }

    private static byte[] CreateHighEntropyPixels()
    {
        byte[] pixels = new byte[512 * 512 * 4];
        uint state = 0x12345678;
        for (int index = 0; index < pixels.Length; index++) { state = state * 1664525 + 1013904223; pixels[index] = (byte)(state >> 24); }
        return pixels;
    }

    private static int CountBoundedIdatChunks(byte[] png)
    {
        int idatChunkCount = 0;
        int offset = 8;
        while (offset < png.Length)
        {
            int dataLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset)));
            if (png.AsSpan(offset + 4, 4).SequenceEqual("IDAT"u8))
            {
                idatChunkCount++;
                Assert.InRange(dataLength, 1, 65_536);
            }
            offset += checked(dataLength + 12);
        }
        Assert.Equal(png.Length, offset);
        return idatChunkCount;
    }

    private sealed class BoundedWriteStream : Stream
    {
        private readonly MemoryStream _storage = new();
        private readonly int _maximumWriteLength;

        internal BoundedWriteStream(int maximumWriteLength) => _maximumWriteLength = maximumWriteLength;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _storage.Length;
        public override long Position { get => _storage.Position; set => throw new NotSupportedException(); }

        internal byte[] ToArray() => _storage.ToArray();

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > _maximumWriteLength) throw new InvalidOperationException("PNG stream writes must remain bounded.");
            _storage.Write(buffer);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _storage.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class FailingWriteStream : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("destination write failed");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) => ValueTask.FromException(new IOException("destination write failed"));
    }

    private sealed class BoundedAsyncWriteStream : Stream
    {
        private readonly MemoryStream _storage = new();
        private readonly int _maximumWriteLength;

        internal BoundedAsyncWriteStream(int maximumWriteLength) => _maximumWriteLength = maximumWriteLength;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _storage.Length;
        public override long Position { get => _storage.Position; set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The async encoder must not use synchronous stream writes.");

        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("The async encoder must not use synchronous stream writes.");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.Length > _maximumWriteLength) throw new InvalidOperationException("PNG stream writes must remain bounded.");
            _storage.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _storage.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class CancellingAsyncWriteStream : Stream
    {
        private readonly MemoryStream _storage = new();
        private readonly CancellationTokenSource _cancellation;
        private bool _hasCancelled;

        internal CancellingAsyncWriteStream(CancellationTokenSource cancellation) => _cancellation = cancellation;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _storage.Length;
        public override long Position { get => _storage.Position; set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The async encoder must not use synchronous stream writes.");

        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("The async encoder must not use synchronous stream writes.");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _storage.Write(buffer.Span);
            if (!_hasCancelled)
            {
                _hasCancelled = true;
                _cancellation.Cancel();
            }
            return ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _storage.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class StallingAsyncWriteStream : Stream
    {
        private readonly MemoryStream _storage = new();
        private readonly TaskCompletionSource _firstWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completeFirstWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _isFirstWrite = true;

        internal Task FirstWriteStarted => _firstWriteStarted.Task;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _storage.Length;
        public override long Position { get => _storage.Position; set => throw new NotSupportedException(); }

        internal void CompleteFirstWrite() => _completeFirstWrite.TrySetResult();

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The async encoder must not use synchronous stream writes.");

        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("The async encoder must not use synchronous stream writes.");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _storage.Write(buffer.Span);
            if (_isFirstWrite)
            {
                _isFirstWrite = false;
                _firstWriteStarted.TrySetResult();
                return new ValueTask(_completeFirstWrite.Task);
            }
            return ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _completeFirstWrite.TrySetResult();
                _storage.Dispose();
            }
            base.Dispose(disposing);
        }
    }

}
