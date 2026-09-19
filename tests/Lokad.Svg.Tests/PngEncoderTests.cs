using Lokad.Svg.Encoding;
using Lokad.Svg.Rendering;
using System.Buffers.Binary;

namespace Lokad.Svg.Tests;

public sealed class PngEncoderTests
{
    [Fact]
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
    public void Cancellation_is_checked_before_scanline_encoding()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => PngEncoder.Encode(1, 1, [0, 0, 0, 0], cancellation.Token));
    }

    [Fact]
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
    public void Streams_large_png_data_in_bounded_writes()
    {
        byte[] pixels = CreateHighEntropyPixels();
        using BoundedWriteStream destination = new(65_536);

        int written = PngEncoder.EncodeTo(512, 512, pixels, destination, new RenderBudget(long.MaxValue), int.MaxValue, CancellationToken.None);

        Assert.Equal(written, destination.Length);
        Assert.True(written > 65_536);
    }

    [Fact]
    public async Task Streams_large_png_data_in_bounded_asynchronous_writes()
    {
        byte[] pixels = CreateHighEntropyPixels();
        using BoundedAsyncWriteStream destination = new(65_536);

        int written = await PngEncoder.EncodeToAsync(512, 512, pixels, destination, new RenderBudget(long.MaxValue), int.MaxValue, CancellationToken.None);

        Assert.Equal(written, destination.Length);
        Assert.True(written > 65_536);
    }

    [Fact]
    public async Task Async_stream_encoding_observes_cancellation_after_output_begins()
    {
        using CancellationTokenSource cancellation = new();
        using CancellingAsyncWriteStream destination = new(cancellation);

        Task<int> task = PngEncoder.EncodeToAsync(1, 1, new byte[4], destination, new RenderBudget(long.MaxValue), int.MaxValue, cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        Assert.True(task.IsCanceled);
        Assert.Equal(8, destination.Length);
    }

    [Fact]
    public async Task Async_stream_encoding_waits_for_destination_backpressure()
    {
        using StallingAsyncWriteStream destination = new();

        Task<int> task = PngEncoder.EncodeToAsync(1, 1, new byte[4], destination, new RenderBudget(long.MaxValue), int.MaxValue, CancellationToken.None);
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
    public void Splits_large_compressed_data_into_bounded_IDAT_chunks()
    {
        byte[] pixels = CreateHighEntropyPixels();
        byte[] png = PngEncoder.Encode(512, 512, pixels, CancellationToken.None);
        int idatChunkCount = 0;
        for (int offset = 8; offset < png.Length;)
        {
            int dataLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset)));
            if (png.AsSpan(offset + 4, 4).SequenceEqual("IDAT"u8))
            {
                idatChunkCount++;
                Assert.InRange(dataLength, 1, 65_536);
            }
            offset += checked(dataLength + 12);
        }

        byte[] exactCap = PngEncoder.Encode(512, 512, pixels, png.Length, CancellationToken.None);
        SvgException exception = Assert.Throws<SvgException>(() => PngEncoder.Encode(512, 512, pixels, png.Length - 1, CancellationToken.None));

        Assert.True(idatChunkCount > 1);
        Assert.Equal(png, exactCap);
        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    private static byte[] CreateHighEntropyPixels()
    {
        byte[] pixels = new byte[512 * 512 * 4];
        uint state = 0x12345678;
        for (int index = 0; index < pixels.Length; index++) { state = state * 1664525 + 1013904223; pixels[index] = (byte)(state >> 24); }
        return pixels;
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
