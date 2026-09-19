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
        WritePngPrefix(width, height, destination, cancellationToken);
        using PngIdatOutputStream compressed = new(destination, budget, maximumEncodedPngBytes, cancellationToken);
        CompressInto(width, height, rgba, budget, compressed, cancellationToken);
        int length = compressed.Complete();
        WriteChunk(destination, "IEND"u8, [], cancellationToken);
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
        PngCompressionBuffer compressed = new(budget, maximumCompressedBytes, cancellationToken);
        try
        {
            CompressInto(width, height, rgba, budget, compressed, cancellationToken);
            return compressed;
        }
        catch
        {
            compressed.Dispose();
            throw;
        }
    }

    private static void CompressInto(int width, int height, ReadOnlySpan<byte> rgba, RenderBudget budget, Stream destination, CancellationToken cancellationToken)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        int stride = checked(width * 4);
        if (rgba.Length != checked(stride * height)) throw new ArgumentException("Pixel buffer length does not match dimensions.", nameof(rgba));
        using RenderBudget.BudgetReservation rowReservation = budget.Reserve(checked((long)stride + 1), SvgMemoryCategory.Png);
        byte[] row = new byte[stride + 1];
        using ZLibStream zlib = new(destination, CompressionLevel.Optimal, true);
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

    private static void WritePngPrefix(int width, int height, Stream output, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        output.Write(PngSignature);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], checked((uint)height));
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR"u8, header, cancellationToken);
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

    private sealed class PngIdatOutputStream : Stream
    {
        private readonly Stream _output;
        private readonly RenderBudget _budget;
        private readonly int _maximumEncodedPngBytes;
        private readonly CancellationToken _cancellationToken;
        private RenderBudget.BudgetReservation? _reservation;
        private byte[]? _buffer;
        private int _bufferedLength;
        private int _compressedLength;
        private int _idatChunkCount;
        private bool _isComplete;

        internal PngIdatOutputStream(Stream output, RenderBudget budget, int maximumEncodedPngBytes, CancellationToken cancellationToken)
        {
            _output = output;
            _budget = budget;
            _maximumEncodedPngBytes = maximumEncodedPngBytes;
            _cancellationToken = cancellationToken;
        }

        internal int Complete()
        {
            if (_isComplete) throw new InvalidOperationException("PNG IDAT output was already completed.");
            _cancellationToken.ThrowIfCancellationRequested();
            if (_compressedLength == 0)
            {
                EnsureOutputLength(0, _idatChunkCount + 1);
                WriteChunk(_output, IdatChunkType, [], _cancellationToken);
                _idatChunkCount++;
            }
            else if (_bufferedLength > 0)
            {
                byte[] buffer = _buffer ?? throw new InvalidOperationException("PNG IDAT buffer allocation did not complete.");
                EnsureOutputLength(_compressedLength, _idatChunkCount + 1);
                WriteChunk(_output, IdatChunkType, buffer.AsSpan(0, _bufferedLength), _cancellationToken);
                _bufferedLength = 0;
                _idatChunkCount++;
            }
            _isComplete = true;
            return checked(FixedPngBytesWithoutIdat + _compressedLength + _idatChunkCount * PngChunkOverheadBytes);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _compressedLength;
        public override long Position { get => _compressedLength; set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_isComplete) throw new InvalidOperationException("PNG IDAT output was already completed.");
            _cancellationToken.ThrowIfCancellationRequested();
            while (!buffer.IsEmpty)
            {
                if (_bufferedLength == 0 && buffer.Length >= MaximumIdatPayloadBytes)
                {
                    int directCompressedLength = checked(_compressedLength + MaximumIdatPayloadBytes);
                    EnsureOutputLength(directCompressedLength, _idatChunkCount + 1);
                    WriteChunk(_output, IdatChunkType, buffer[..MaximumIdatPayloadBytes], _cancellationToken);
                    _compressedLength = directCompressedLength;
                    _idatChunkCount++;
                    buffer = buffer[MaximumIdatPayloadBytes..];
                    continue;
                }
                EnsureBufferCapacity(Math.Min(buffer.Length, MaximumIdatPayloadBytes));
                byte[] destination = _buffer ?? throw new InvalidOperationException("PNG IDAT buffer allocation did not complete.");
                int count = Math.Min(destination.Length - _bufferedLength, buffer.Length);
                int bufferedCompressedLength = checked(_compressedLength + count);
                EnsureOutputLength(bufferedCompressedLength, _idatChunkCount + 1);
                buffer[..count].CopyTo(destination.AsSpan(_bufferedLength));
                _bufferedLength += count;
                _compressedLength = bufferedCompressedLength;
                buffer = buffer[count..];
                if (_bufferedLength == destination.Length)
                {
                    WriteChunk(_output, IdatChunkType, destination, _cancellationToken);
                    _bufferedLength = 0;
                    _idatChunkCount++;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _reservation?.Dispose();
                _reservation = null;
                _buffer = null;
                _bufferedLength = 0;
            }
            base.Dispose(disposing);
        }

        private void EnsureBufferCapacity(int requiredLength)
        {
            if (_buffer is not null) return;
            int capacity = Math.Min(Math.Max(64, requiredLength), MaximumIdatPayloadBytes);
            RenderBudget.BudgetReservation replacementReservation = _budget.Reserve(capacity, SvgMemoryCategory.Png);
            byte[] replacement;
            try { replacement = new byte[capacity]; }
            catch { replacementReservation.Dispose(); throw; }
            _reservation = replacementReservation;
            _buffer = replacement;
        }

        private void EnsureOutputLength(int compressedLength, int idatChunkCount)
        {
            long outputLength = FixedPngBytesWithoutIdat + (long)compressedLength + (long)idatChunkCount * PngChunkOverheadBytes;
            if (outputLength > _maximumEncodedPngBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
        }
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
