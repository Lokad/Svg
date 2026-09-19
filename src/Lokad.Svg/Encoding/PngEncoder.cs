using System.Buffers.Binary;
using System.IO.Compression;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Encoding;

/// <summary>Encodes tightly packed straight-alpha RGBA8 pixels as a non-interlaced PNG.</summary>
internal static class PngEncoder
{
    private const int FixedPngBytesWithoutIdat = 45;
    private const int PngChunkOverheadBytes = 12;
    private const int MaximumIdatPayloadBytes = 65_536;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly byte[] IhdrChunkType = [73, 72, 68, 82];
    private static readonly byte[] IdatChunkType = [73, 68, 65, 84];
    private static readonly byte[] IendChunkType = [73, 69, 78, 68];

    internal static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba, CancellationToken cancellationToken)
        => Encode(width, height, rgba, int.MaxValue, cancellationToken);

    internal static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba, int maximumEncodedPngBytes, CancellationToken cancellationToken)
        => Encode(width, height, rgba, new RenderBudget(long.MaxValue), maximumEncodedPngBytes, cancellationToken);

    internal static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba, RenderBudget budget, int maximumEncodedPngBytes, CancellationToken cancellationToken)
    {
        if (maximumEncodedPngBytes < FixedPngBytesWithoutIdat + PngChunkOverheadBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
        using RenderBudget.BudgetReservation minimumOutput = budget.Reserve(FixedPngBytesWithoutIdat + PngChunkOverheadBytes, SvgMemoryCategory.Output);
        using PngCompressionBuffer compressed = Compress(width, height, rgba, budget, GetMaximumCompressedLength(maximumEncodedPngBytes), cancellationToken);
        int length = GetOutputLength(checked((int)compressed.Length));
        if (length > maximumEncodedPngBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
        using RenderBudget.BudgetReservation remainingOutput = budget.Reserve(length - (FixedPngBytesWithoutIdat + PngChunkOverheadBytes), SvgMemoryCategory.Output);
        byte[] output = new byte[length];
        WritePng(width, height, compressed.Written, output, cancellationToken);
        return output;
    }

    internal static int EncodeInto(int width, int height, ReadOnlySpan<byte> rgba, Memory<byte> destination, CancellationToken cancellationToken)
        => EncodeInto(width, height, rgba, destination, new RenderBudget(long.MaxValue), int.MaxValue, cancellationToken);

    internal static int EncodeInto(int width, int height, ReadOnlySpan<byte> rgba, Memory<byte> destination, int maximumEncodedPngBytes, CancellationToken cancellationToken)
        => EncodeInto(width, height, rgba, destination, new RenderBudget(long.MaxValue), maximumEncodedPngBytes, cancellationToken);

    internal static int EncodeInto(int width, int height, ReadOnlySpan<byte> rgba, Memory<byte> destination, RenderBudget budget, int maximumEncodedPngBytes, CancellationToken cancellationToken)
    {
        if (maximumEncodedPngBytes < FixedPngBytesWithoutIdat + PngChunkOverheadBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
        using PngCompressionBuffer compressed = Compress(width, height, rgba, budget, GetMaximumCompressedLength(maximumEncodedPngBytes), cancellationToken);
        int length = GetOutputLength(checked((int)compressed.Length));
        if (length > maximumEncodedPngBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
        if (destination.Length < length) throw new SvgException(SvgErrorCode.InsufficientDestination, "PNG destination is too small.");
        WritePng(width, height, compressed.Written, destination.Span[..length], cancellationToken);
        return length;
    }

    internal static int EncodeTo(int width, int height, ReadOnlySpan<byte> rgba, Stream destination, RenderBudget budget, int maximumEncodedPngBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("PNG destination must be writable.", nameof(destination));
        if (maximumEncodedPngBytes < FixedPngBytesWithoutIdat + PngChunkOverheadBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
        using PngCompressionBuffer compressed = Compress(width, height, rgba, budget, GetMaximumCompressedLength(maximumEncodedPngBytes), cancellationToken);
        int length = GetOutputLength(checked((int)compressed.Length));
        if (length > maximumEncodedPngBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
        WritePng(width, height, compressed.Written, destination, cancellationToken);
        return length;
    }

    internal static async Task<int> EncodeToAsync(int width, int height, ReadOnlyMemory<byte> rgba, Stream destination, RenderBudget budget, int maximumEncodedPngBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("PNG destination must be writable.", nameof(destination));
        if (maximumEncodedPngBytes < FixedPngBytesWithoutIdat + PngChunkOverheadBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
        using PngCompressionBuffer compressed = Compress(width, height, rgba.Span, budget, GetMaximumCompressedLength(maximumEncodedPngBytes), cancellationToken);
        int length = GetOutputLength(checked((int)compressed.Length));
        if (length > maximumEncodedPngBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
        await WritePngAsync(width, height, compressed.WrittenMemory, destination, cancellationToken).ConfigureAwait(false);
        return length;
    }

    private static PngCompressionBuffer Compress(int width, int height, ReadOnlySpan<byte> rgba, RenderBudget budget, int maximumCompressedBytes, CancellationToken cancellationToken)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        int stride = checked(width * 4);
        if (rgba.Length != checked(stride * height)) throw new ArgumentException("Pixel buffer length does not match dimensions.", nameof(rgba));
        using RenderBudget.BudgetReservation rowReservation = budget.Reserve(checked((long)stride + 1), SvgMemoryCategory.Png);
        byte[] row = new byte[stride + 1];
        PngCompressionBuffer compressed = new(budget, maximumCompressedBytes, cancellationToken);
        try
        {
            using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, true))
            {
                for (int y = 0; y < height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    row[0] = 0;
                    rgba.Slice(y * stride, stride).CopyTo(row.AsSpan(1));
                    for (int x = 1; x < row.Length; x += 4)
                    {
                        if (row[x + 3] == 0) { row[x] = 0; row[x + 1] = 0; row[x + 2] = 0; }
                    }
                    zlib.Write(row);
                }
            }
            return compressed;
        }
        catch
        {
            compressed.Dispose();
            throw;
        }
    }

    private static int GetOutputLength(int compressedLength)
    {
        int idatChunkCount = Math.Max(1, checked((int)(((long)compressedLength + MaximumIdatPayloadBytes - 1) / MaximumIdatPayloadBytes)));
        return checked(FixedPngBytesWithoutIdat + compressedLength + checked(idatChunkCount * PngChunkOverheadBytes));
    }

    private static int GetMaximumCompressedLength(int maximumEncodedPngBytes)
    {
        int maximumCompressedLength = 0;
        for (int idatChunkCount = 1; ; idatChunkCount++)
        {
            long lowerBound = (long)(idatChunkCount - 1) * MaximumIdatPayloadBytes;
            long upperBound = Math.Min((long)idatChunkCount * MaximumIdatPayloadBytes, (long)maximumEncodedPngBytes - FixedPngBytesWithoutIdat - (long)idatChunkCount * PngChunkOverheadBytes);
            if (upperBound <= lowerBound) return maximumCompressedLength;
            maximumCompressedLength = checked((int)upperBound);
        }
    }

    private static void WritePng(int width, int height, ReadOnlySpan<byte> compressed, Span<byte> output, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PngSignature.CopyTo(output);
        int offset = 8;
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], checked((uint)height));
        header[8] = 8;
        header[9] = 6;
        offset += WriteChunk(output[offset..], "IHDR"u8, header, cancellationToken);
        offset += WriteIdatChunks(output[offset..], compressed, cancellationToken);
        offset += WriteChunk(output[offset..], "IEND"u8, [], cancellationToken);
    }

    private static void WritePng(int width, int height, ReadOnlySpan<byte> compressed, Stream output, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        output.Write(PngSignature);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], checked((uint)height));
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR"u8, header, cancellationToken);
        WriteIdatChunks(output, compressed, cancellationToken);
        WriteChunk(output, "IEND"u8, [], cancellationToken);
    }

    private static async Task WritePngAsync(int width, int height, ReadOnlyMemory<byte> compressed, Stream output, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await output.WriteAsync(PngSignature, cancellationToken).ConfigureAwait(false);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), checked((uint)height));
        header[8] = 8;
        header[9] = 6;
        await WriteChunkAsync(output, IhdrChunkType, header, cancellationToken).ConfigureAwait(false);
        await WriteIdatChunksAsync(output, compressed, cancellationToken).ConfigureAwait(false);
        await WriteChunkAsync(output, IendChunkType, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
    }

    private static int WriteIdatChunks(Span<byte> output, ReadOnlySpan<byte> compressed, CancellationToken cancellationToken)
    {
        if (compressed.IsEmpty) return WriteChunk(output, "IDAT"u8, [], cancellationToken);
        int outputOffset = 0;
        for (int compressedOffset = 0; compressedOffset < compressed.Length;)
        {
            int length = Math.Min(MaximumIdatPayloadBytes, compressed.Length - compressedOffset);
            outputOffset += WriteChunk(output[outputOffset..], "IDAT"u8, compressed.Slice(compressedOffset, length), cancellationToken);
            compressedOffset += length;
        }
        return outputOffset;
    }

    private static void WriteIdatChunks(Stream output, ReadOnlySpan<byte> compressed, CancellationToken cancellationToken)
    {
        if (compressed.IsEmpty)
        {
            WriteChunk(output, "IDAT"u8, [], cancellationToken);
            return;
        }
        for (int offset = 0; offset < compressed.Length;)
        {
            int length = Math.Min(MaximumIdatPayloadBytes, compressed.Length - offset);
            WriteChunk(output, "IDAT"u8, compressed.Slice(offset, length), cancellationToken);
            offset += length;
        }
    }

    private static async Task WriteIdatChunksAsync(Stream output, ReadOnlyMemory<byte> compressed, CancellationToken cancellationToken)
    {
        if (compressed.IsEmpty)
        {
            await WriteChunkAsync(output, IdatChunkType, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
            return;
        }
        for (int offset = 0; offset < compressed.Length;)
        {
            int length = Math.Min(MaximumIdatPayloadBytes, compressed.Length - offset);
            await WriteChunkAsync(output, IdatChunkType, compressed.Slice(offset, length), cancellationToken).ConfigureAwait(false);
            offset += length;
        }
    }

    private static int WriteChunk(Span<byte> output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data, CancellationToken cancellationToken)
    {
        int length = checked(12 + data.Length);
        uint crc = 0xffffffff;
        foreach (byte value in type) crc = CrcStep(crc, value);
        for (int index = 0; index < data.Length; index++)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            crc = CrcStep(crc, data[index]);
        }
        BinaryPrimitives.WriteUInt32BigEndian(output, checked((uint)data.Length));
        type.CopyTo(output[4..]); data.CopyTo(output[8..]);
        BinaryPrimitives.WriteUInt32BigEndian(output.Slice(8 + data.Length), ~crc);
        return length;
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data, CancellationToken cancellationToken)
    {
        uint crc = 0xffffffff;
        foreach (byte value in type) crc = CrcStep(crc, value);
        for (int index = 0; index < data.Length; index++)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            crc = CrcStep(crc, data[index]);
        }
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        output.Write(length);
        output.Write(type);
        for (int offset = 0; offset < data.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(65_536, data.Length - offset);
            output.Write(data.Slice(offset, count));
            offset += count;
        }
        BinaryPrimitives.WriteUInt32BigEndian(length, ~crc);
        output.Write(length);
    }

    private static async Task WriteChunkAsync(Stream output, ReadOnlyMemory<byte> type, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        uint crc = 0xffffffff;
        foreach (byte value in type.Span) crc = CrcStep(crc, value);
        for (int index = 0; index < data.Length; index++)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            crc = CrcStep(crc, data.Span[index]);
        }
        byte[] length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        await output.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(type, cancellationToken).ConfigureAwait(false);
        for (int offset = 0; offset < data.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(65_536, data.Length - offset);
            await output.WriteAsync(data.Slice(offset, count), cancellationToken).ConfigureAwait(false);
            offset += count;
        }
        BinaryPrimitives.WriteUInt32BigEndian(length, ~crc);
        await output.WriteAsync(length, cancellationToken).ConfigureAwait(false);
    }

    private sealed class PngCompressionBuffer : Stream
    {
        private readonly RenderBudget _budget;
        private readonly int _maximumLength;
        private readonly CancellationToken _cancellationToken;
        private RenderBudget.BudgetReservation? _reservation;
        private byte[]? _buffer;
        private int _length;

        internal PngCompressionBuffer(RenderBudget budget, int maximumLength, CancellationToken cancellationToken)
        {
            _budget = budget;
            _maximumLength = maximumLength;
            _cancellationToken = cancellationToken;
        }

        internal ReadOnlySpan<byte> Written => _buffer is null ? [] : _buffer.AsSpan(0, _length);

        internal ReadOnlyMemory<byte> WrittenMemory => _buffer is null ? ReadOnlyMemory<byte>.Empty : _buffer.AsMemory(0, _length);

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position { get => _length; set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            int requiredLength = checked(_length + buffer.Length);
            if (requiredLength > _maximumLength) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
            EnsureCapacity(requiredLength);
            byte[] destination = _buffer ?? throw new InvalidOperationException("PNG compression buffer allocation did not complete.");
            buffer.CopyTo(destination.AsSpan(_length));
            _length = requiredLength;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _reservation?.Dispose();
                _reservation = null;
                _buffer = null;
                _length = 0;
            }
            base.Dispose(disposing);
        }

        private void EnsureCapacity(int requiredLength)
        {
            if (_buffer is not null && requiredLength <= _buffer.Length) return;
            int capacity = _buffer is null ? Math.Min(Math.Max(64, requiredLength), _maximumLength) : Math.Min(Math.Max(checked(_buffer.Length * 2), requiredLength), _maximumLength);
            RenderBudget.BudgetReservation replacementReservation = _budget.Reserve(capacity, SvgMemoryCategory.Png);
            byte[] replacement;
            try { replacement = new byte[capacity]; }
            catch { replacementReservation.Dispose(); throw; }
            if (_buffer is not null) _buffer.AsSpan(0, _length).CopyTo(replacement);
            _reservation?.Dispose();
            _reservation = replacementReservation;
            _buffer = replacement;
        }
    }

    private static uint CrcStep(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        return crc;
    }
}
