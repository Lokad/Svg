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
    private const int AsyncScratchStorageBytes = 64;
    private const int AsyncScratchLength = 17;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly byte[] IhdrChunkType = [73, 72, 68, 82];
    private static readonly byte[] IdatChunkType = [73, 68, 65, 84];
    private static readonly byte[] IendChunkType = [73, 69, 78, 68];

    internal static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba, CancellationToken cancellationToken)
        => Encode(width, height, rgba, int.MaxValue, cancellationToken);

    internal static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba, int maximumEncodedPngBytes, CancellationToken cancellationToken)
        => Encode(width, height, rgba, new RenderBudget(long.MaxValue), maximumEncodedPngBytes, cancellationToken);

    internal static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba, RenderBudget budget, int maximumEncodedPngBytes, CancellationToken cancellationToken)
        => Encode(width, height, rgba, budget, maximumEncodedPngBytes, 0, long.MaxValue, cancellationToken);

    internal static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba, RenderBudget budget, int maximumEncodedPngBytes, long initialWorkUnits, long maximumWorkUnits, CancellationToken cancellationToken)
    {
        if (maximumEncodedPngBytes < FixedPngBytesWithoutIdat + PngChunkOverheadBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
        PngWorkCounter workCounter = new(initialWorkUnits, maximumWorkUnits);
        using RenderBudget.BudgetReservation minimumOutput = budget.Reserve(FixedPngBytesWithoutIdat + PngChunkOverheadBytes, SvgMemoryCategory.Output);
        using PngCompressionBuffer compressed = Compress(width, height, rgba, budget, GetMaximumCompressedLength(maximumEncodedPngBytes), workCounter, cancellationToken);
        int length = GetOutputLength(checked((int)compressed.Length));
        if (length > maximumEncodedPngBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
        using RenderBudget.BudgetReservation remainingOutput = budget.Reserve(length - (FixedPngBytesWithoutIdat + PngChunkOverheadBytes), SvgMemoryCategory.Output);
        byte[] output = new byte[length];
        WritePng(width, height, compressed.Written, output, workCounter, cancellationToken);
        return output;
    }

    internal static int EncodeInto(int width, int height, ReadOnlySpan<byte> rgba, Memory<byte> destination, CancellationToken cancellationToken)
        => EncodeInto(width, height, rgba, destination, new RenderBudget(long.MaxValue), int.MaxValue, cancellationToken);

    internal static int EncodeInto(int width, int height, ReadOnlySpan<byte> rgba, Memory<byte> destination, int maximumEncodedPngBytes, CancellationToken cancellationToken)
        => EncodeInto(width, height, rgba, destination, new RenderBudget(long.MaxValue), maximumEncodedPngBytes, cancellationToken);

    internal static int EncodeInto(int width, int height, ReadOnlySpan<byte> rgba, Memory<byte> destination, RenderBudget budget, int maximumEncodedPngBytes, CancellationToken cancellationToken)
        => EncodeInto(width, height, rgba, destination, budget, maximumEncodedPngBytes, 0, long.MaxValue, cancellationToken);

    internal static int EncodeInto(int width, int height, ReadOnlySpan<byte> rgba, Memory<byte> destination, RenderBudget budget, int maximumEncodedPngBytes, long initialWorkUnits, long maximumWorkUnits, CancellationToken cancellationToken)
    {
        if (maximumEncodedPngBytes < FixedPngBytesWithoutIdat + PngChunkOverheadBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
        PngWorkCounter workCounter = new(initialWorkUnits, maximumWorkUnits);
        using PngCompressionBuffer compressed = Compress(width, height, rgba, budget, GetMaximumCompressedLength(maximumEncodedPngBytes), workCounter, cancellationToken);
        int length = GetOutputLength(checked((int)compressed.Length));
        if (length > maximumEncodedPngBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
        if (destination.Length < length) throw new SvgException(SvgErrorCode.InsufficientDestination, "PNG destination is too small.");
        WritePng(width, height, compressed.Written, destination.Span[..length], workCounter, cancellationToken);
        return length;
    }

    internal static int EncodeTo(int width, int height, ReadOnlySpan<byte> rgba, Stream destination, RenderBudget budget, int maximumEncodedPngBytes, CancellationToken cancellationToken)
        => EncodeTo(width, height, rgba, destination, budget, maximumEncodedPngBytes, 0, long.MaxValue, cancellationToken);

    internal static int EncodeTo(int width, int height, ReadOnlySpan<byte> rgba, Stream destination, RenderBudget budget, int maximumEncodedPngBytes, long initialWorkUnits, long maximumWorkUnits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("PNG destination must be writable.", nameof(destination));
        if (maximumEncodedPngBytes < FixedPngBytesWithoutIdat + PngChunkOverheadBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
        PngWorkCounter workCounter = new(initialWorkUnits, maximumWorkUnits);
        WritePngPrefix(width, height, destination, workCounter, cancellationToken);
        using PngIdatOutputStream compressed = new(destination, budget, maximumEncodedPngBytes, workCounter, cancellationToken);
        CompressInto(width, height, rgba, budget, compressed, workCounter, cancellationToken);
        int length = compressed.Complete();
        WriteChunk(destination, "IEND"u8, [], workCounter, cancellationToken);
        return length;
    }

    internal static async Task<int> EncodeToAsync(int width, int height, ReadOnlyMemory<byte> rgba, Stream destination, RenderBudget budget, int maximumEncodedPngBytes, CancellationToken cancellationToken)
        => await EncodeToAsync(width, height, rgba, destination, budget, maximumEncodedPngBytes, 0, long.MaxValue, cancellationToken).ConfigureAwait(false);

    internal static async Task<int> EncodeToAsync(int width, int height, ReadOnlyMemory<byte> rgba, Stream destination, RenderBudget budget, int maximumEncodedPngBytes, long initialWorkUnits, long maximumWorkUnits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("PNG destination must be writable.", nameof(destination));
        if (maximumEncodedPngBytes < FixedPngBytesWithoutIdat + PngChunkOverheadBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
        PngWorkCounter workCounter = new(initialWorkUnits, maximumWorkUnits);
        using RenderBudget.BudgetReservation scratchReservation = budget.Reserve(AsyncScratchStorageBytes, SvgMemoryCategory.Png);
        byte[] scratch = new byte[AsyncScratchLength];
        await WritePngPrefixAsync(width, height, destination, scratch, workCounter, cancellationToken).ConfigureAwait(false);
        using PngAsyncIdatOutputStream compressed = new(destination, budget, maximumEncodedPngBytes, scratch, workCounter, cancellationToken);
        await CompressIntoAsync(width, height, rgba, budget, compressed, workCounter, cancellationToken).ConfigureAwait(false);
        int length = await compressed.CompleteAsync().ConfigureAwait(false);
        await WriteChunkAsync(destination, IendChunkType, ReadOnlyMemory<byte>.Empty, scratch, workCounter, cancellationToken).ConfigureAwait(false);
        return length;
    }

    private static PngCompressionBuffer Compress(int width, int height, ReadOnlySpan<byte> rgba, RenderBudget budget, int maximumCompressedBytes, PngWorkCounter workCounter, CancellationToken cancellationToken)
    {
        PngCompressionBuffer compressed = new(budget, maximumCompressedBytes, workCounter, cancellationToken);
        try
        {
            CompressInto(width, height, rgba, budget, compressed, workCounter, cancellationToken);
            return compressed;
        }
        catch
        {
            compressed.Dispose();
            throw;
        }
    }

    private static void CompressInto(int width, int height, ReadOnlySpan<byte> rgba, RenderBudget budget, Stream destination, PngWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        int stride = checked(width * 4);
        if (rgba.Length != checked(stride * height)) throw new ArgumentException("Pixel buffer length does not match dimensions.", nameof(rgba));
        workCounter.ConsumePixels(width, height, cancellationToken);
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

    private static async Task CompressIntoAsync(int width, int height, ReadOnlyMemory<byte> rgba, RenderBudget budget, Stream destination, PngWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        int stride = checked(width * 4);
        if (rgba.Length != checked(stride * height)) throw new ArgumentException("Pixel buffer length does not match dimensions.", nameof(rgba));
        workCounter.ConsumePixels(width, height, cancellationToken);
        using RenderBudget.BudgetReservation rowReservation = budget.Reserve(checked((long)stride + 1), SvgMemoryCategory.Png);
        byte[] row = new byte[stride + 1];
        await using ZLibStream zlib = new(destination, CompressionLevel.Optimal, true);
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            row[0] = 0;
            rgba.Span.Slice(y * stride, stride).CopyTo(row.AsSpan(1));
            for (int x = 1; x < row.Length; x += 4)
            {
                if (row[x + 3] == 0) { row[x] = 0; row[x + 1] = 0; row[x + 2] = 0; }
            }
            await zlib.WriteAsync(row, cancellationToken).ConfigureAwait(false);
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

    private static void WritePng(int width, int height, ReadOnlySpan<byte> compressed, Span<byte> output, PngWorkCounter workCounter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PngSignature.CopyTo(output);
        int offset = 8;
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], checked((uint)height));
        header[8] = 8;
        header[9] = 6;
        offset += WriteChunk(output[offset..], "IHDR"u8, header, workCounter, cancellationToken);
        offset += WriteIdatChunks(output[offset..], compressed, workCounter, cancellationToken);
        offset += WriteChunk(output[offset..], "IEND"u8, [], workCounter, cancellationToken);
    }

    private static void WritePngPrefix(int width, int height, Stream output, PngWorkCounter workCounter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        output.Write(PngSignature);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], checked((uint)height));
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR"u8, header, workCounter, cancellationToken);
    }

    private static async Task WritePngPrefixAsync(int width, int height, Stream output, byte[] scratch, PngWorkCounter workCounter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await output.WriteAsync(PngSignature, cancellationToken).ConfigureAwait(false);
        BinaryPrimitives.WriteUInt32BigEndian(scratch.AsSpan(4), checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(scratch.AsSpan(8), checked((uint)height));
        scratch[12] = 8;
        scratch[13] = 6;
        await WriteChunkAsync(output, IhdrChunkType, scratch.AsMemory(4, 13), scratch, workCounter, cancellationToken).ConfigureAwait(false);
    }

    private static int WriteIdatChunks(Span<byte> output, ReadOnlySpan<byte> compressed, PngWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (compressed.IsEmpty) return WriteChunk(output, "IDAT"u8, [], workCounter, cancellationToken);
        int outputOffset = 0;
        for (int compressedOffset = 0; compressedOffset < compressed.Length;)
        {
            int length = Math.Min(MaximumIdatPayloadBytes, compressed.Length - compressedOffset);
            outputOffset += WriteChunk(output[outputOffset..], "IDAT"u8, compressed.Slice(compressedOffset, length), workCounter, cancellationToken);
            compressedOffset += length;
        }
        return outputOffset;
    }

    private static int WriteChunk(Span<byte> output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data, PngWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (type.SequenceEqual(IdatChunkType)) workCounter.ConsumeSerializedBytes(data.Length, cancellationToken);
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

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data, PngWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (type.SequenceEqual(IdatChunkType)) workCounter.ConsumeSerializedBytes(data.Length, cancellationToken);
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

    private static async Task WriteChunkAsync(Stream output, ReadOnlyMemory<byte> type, ReadOnlyMemory<byte> data, byte[] scratch, PngWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (type.Span.SequenceEqual(IdatChunkType)) workCounter.ConsumeSerializedBytes(data.Length, cancellationToken);
        uint crc = 0xffffffff;
        foreach (byte value in type.Span) crc = CrcStep(crc, value);
        for (int index = 0; index < data.Length; index++)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            crc = CrcStep(crc, data.Span[index]);
        }
        BinaryPrimitives.WriteUInt32BigEndian(scratch, checked((uint)data.Length));
        await output.WriteAsync(scratch.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(type, cancellationToken).ConfigureAwait(false);
        for (int offset = 0; offset < data.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(65_536, data.Length - offset);
            await output.WriteAsync(data.Slice(offset, count), cancellationToken).ConfigureAwait(false);
            offset += count;
        }
        BinaryPrimitives.WriteUInt32BigEndian(scratch, ~crc);
        await output.WriteAsync(scratch.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
    }

    private sealed class PngIdatOutputStream : Stream
    {
        private readonly Stream _output;
        private readonly RenderBudget _budget;
        private readonly int _maximumEncodedPngBytes;
        private readonly PngWorkCounter _workCounter;
        private readonly CancellationToken _cancellationToken;
        private RenderBudget.BudgetReservation? _reservation;
        private byte[]? _buffer;
        private int _bufferedLength;
        private int _compressedLength;
        private int _idatChunkCount;
        private bool _isComplete;

        internal PngIdatOutputStream(Stream output, RenderBudget budget, int maximumEncodedPngBytes, PngWorkCounter workCounter, CancellationToken cancellationToken)
        {
            _output = output;
            _budget = budget;
            _maximumEncodedPngBytes = maximumEncodedPngBytes;
            _workCounter = workCounter;
            _cancellationToken = cancellationToken;
        }

        internal int Complete()
        {
            if (_isComplete) throw new InvalidOperationException("PNG IDAT output was already completed.");
            _cancellationToken.ThrowIfCancellationRequested();
            if (_compressedLength == 0)
            {
                EnsureOutputLength(0, _idatChunkCount + 1);
                WriteChunk(_output, IdatChunkType, [], _workCounter, _cancellationToken);
                _idatChunkCount++;
            }
            else if (_bufferedLength > 0)
            {
                byte[] buffer = _buffer ?? throw new InvalidOperationException("PNG IDAT buffer allocation did not complete.");
                EnsureOutputLength(_compressedLength, _idatChunkCount + 1);
                WriteChunk(_output, IdatChunkType, buffer.AsSpan(0, _bufferedLength), _workCounter, _cancellationToken);
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
            _workCounter.ConsumeCompressedBytes(buffer.Length, _cancellationToken);
            while (!buffer.IsEmpty)
            {
                if (_bufferedLength == 0 && buffer.Length >= MaximumIdatPayloadBytes)
                {
                    int directCompressedLength = checked(_compressedLength + MaximumIdatPayloadBytes);
                    EnsureOutputLength(directCompressedLength, _idatChunkCount + 1);
                    WriteChunk(_output, IdatChunkType, buffer[..MaximumIdatPayloadBytes], _workCounter, _cancellationToken);
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
                if (_bufferedLength == destination.Length && !TryGrowBuffer())
                {
                    WriteChunk(_output, IdatChunkType, destination, _workCounter, _cancellationToken);
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

        /// <summary>Grows the aggregation buffer toward the 64 KiB chunk cap instead of flushing a tiny chunk.</summary>
        private bool TryGrowBuffer()
        {
            byte[] current = _buffer ?? throw new InvalidOperationException("PNG IDAT buffer allocation did not complete.");
            if (current.Length >= MaximumIdatPayloadBytes) return false;
            int capacity = Math.Min(checked(current.Length * 2), MaximumIdatPayloadBytes);
            RenderBudget.BudgetReservation replacementReservation = _budget.Reserve(capacity, SvgMemoryCategory.Png);
            byte[] replacement;
            try { replacement = new byte[capacity]; }
            catch { replacementReservation.Dispose(); throw; }
            try
            {
                _workCounter.ConsumeBufferedCopyBytes(_bufferedLength, _cancellationToken);
                current.AsSpan(0, _bufferedLength).CopyTo(replacement);
            }
            catch { replacementReservation.Dispose(); throw; }
            _reservation?.Dispose();
            _reservation = replacementReservation;
            _buffer = replacement;
            return true;
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

    private sealed class PngAsyncIdatOutputStream : Stream
    {
        private readonly Stream _output;
        private readonly RenderBudget _budget;
        private readonly int _maximumEncodedPngBytes;
        private readonly byte[] _scratch;
        private readonly PngWorkCounter _workCounter;
        private readonly CancellationToken _cancellationToken;
        private RenderBudget.BudgetReservation? _reservation;
        private byte[]? _buffer;
        private int _bufferedLength;
        private int _compressedLength;
        private int _idatChunkCount;
        private bool _isComplete;

        internal PngAsyncIdatOutputStream(Stream output, RenderBudget budget, int maximumEncodedPngBytes, byte[] scratch, PngWorkCounter workCounter, CancellationToken cancellationToken)
        {
            _output = output;
            _budget = budget;
            _maximumEncodedPngBytes = maximumEncodedPngBytes;
            _scratch = scratch;
            _workCounter = workCounter;
            _cancellationToken = cancellationToken;
        }

        internal async Task<int> CompleteAsync()
        {
            if (_isComplete) throw new InvalidOperationException("PNG IDAT output was already completed.");
            _cancellationToken.ThrowIfCancellationRequested();
            if (_compressedLength == 0)
            {
                EnsureOutputLength(0, _idatChunkCount + 1);
                await WriteChunkAsync(_output, IdatChunkType, ReadOnlyMemory<byte>.Empty, _scratch, _workCounter, _cancellationToken).ConfigureAwait(false);
                _idatChunkCount++;
            }
            else if (_bufferedLength > 0)
            {
                byte[] buffer = _buffer ?? throw new InvalidOperationException("PNG IDAT buffer allocation did not complete.");
                EnsureOutputLength(_compressedLength, _idatChunkCount + 1);
                await WriteChunkAsync(_output, IdatChunkType, buffer.AsMemory(0, _bufferedLength), _scratch, _workCounter, _cancellationToken).ConfigureAwait(false);
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

        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The asynchronous PNG IDAT sink requires asynchronous writes.");

        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("The asynchronous PNG IDAT sink requires asynchronous writes.");

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_isComplete) throw new InvalidOperationException("PNG IDAT output was already completed.");
            cancellationToken.ThrowIfCancellationRequested();
            _cancellationToken.ThrowIfCancellationRequested();
            _workCounter.ConsumeCompressedBytes(buffer.Length, _cancellationToken);
            while (!buffer.IsEmpty)
            {
                if (_bufferedLength == 0 && buffer.Length >= MaximumIdatPayloadBytes)
                {
                    int directCompressedLength = checked(_compressedLength + MaximumIdatPayloadBytes);
                    EnsureOutputLength(directCompressedLength, _idatChunkCount + 1);
                    await WriteChunkAsync(_output, IdatChunkType, buffer[..MaximumIdatPayloadBytes], _scratch, _workCounter, _cancellationToken).ConfigureAwait(false);
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
                buffer[..count].Span.CopyTo(destination.AsSpan(_bufferedLength));
                _bufferedLength += count;
                _compressedLength = bufferedCompressedLength;
                buffer = buffer[count..];
                if (_bufferedLength == destination.Length && !TryGrowBuffer())
                {
                    await WriteChunkAsync(_output, IdatChunkType, destination, _scratch, _workCounter, _cancellationToken).ConfigureAwait(false);
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

        /// <summary>Grows the aggregation buffer toward the 64 KiB chunk cap instead of flushing a tiny chunk.</summary>
        private bool TryGrowBuffer()
        {
            byte[] current = _buffer ?? throw new InvalidOperationException("PNG IDAT buffer allocation did not complete.");
            if (current.Length >= MaximumIdatPayloadBytes) return false;
            int capacity = Math.Min(checked(current.Length * 2), MaximumIdatPayloadBytes);
            RenderBudget.BudgetReservation replacementReservation = _budget.Reserve(capacity, SvgMemoryCategory.Png);
            byte[] replacement;
            try { replacement = new byte[capacity]; }
            catch { replacementReservation.Dispose(); throw; }
            try
            {
                _workCounter.ConsumeBufferedCopyBytes(_bufferedLength, _cancellationToken);
                current.AsSpan(0, _bufferedLength).CopyTo(replacement);
            }
            catch { replacementReservation.Dispose(); throw; }
            _reservation?.Dispose();
            _reservation = replacementReservation;
            _buffer = replacement;
            return true;
        }

        private void EnsureBufferCapacity(int requiredLength)
        {
            if (_buffer is not null) return;
            int capacity = Math.Min(Math.Max(64, requiredLength), MaximumIdatPayloadBytes);
            RenderBudget.BudgetReservation reservation = _budget.Reserve(capacity, SvgMemoryCategory.Png);
            byte[] buffer;
            try { buffer = new byte[capacity]; }
            catch { reservation.Dispose(); throw; }
            _reservation = reservation;
            _buffer = buffer;
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
        private readonly PngWorkCounter _workCounter;
        private readonly CancellationToken _cancellationToken;
        private RenderBudget.BudgetReservation? _reservation;
        private byte[]? _buffer;
        private int _length;

        internal PngCompressionBuffer(RenderBudget budget, int maximumLength, PngWorkCounter workCounter, CancellationToken cancellationToken)
        {
            _budget = budget;
            _maximumLength = maximumLength;
            _workCounter = workCounter;
            _cancellationToken = cancellationToken;
        }

        internal ReadOnlySpan<byte> Written => _buffer is null ? [] : _buffer.AsSpan(0, _length);

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
            _workCounter.ConsumeCompressedBytes(buffer.Length, _cancellationToken);
            int requiredLength = checked(_length + buffer.Length);
            if (requiredLength > _maximumLength) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
            EnsureCapacity(requiredLength);
            byte[] destination = _buffer ?? throw new InvalidOperationException("PNG compression buffer allocation did not complete.");
            CopyWithCheckpoints(buffer, destination.AsSpan(_length));
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
            try
            {
                if (_buffer is not null)
                {
                    _workCounter.ConsumeBufferedCopyBytes(_length, _cancellationToken);
                    CopyWithCheckpoints(_buffer.AsSpan(0, _length), replacement);
                }
            }
            catch
            {
                replacementReservation.Dispose();
                throw;
            }
            _reservation?.Dispose();
            _reservation = replacementReservation;
            _buffer = replacement;
        }

        private void CopyWithCheckpoints(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            for (int offset = 0; offset < source.Length; offset += 4096)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(4096, source.Length - offset);
                source.Slice(offset, count).CopyTo(destination.Slice(offset, count));
            }
        }
    }

    private sealed class PngWorkCounter
    {
        private readonly long _maximumWorkUnits;
        private long _usedWorkUnits;

        internal PngWorkCounter(long initialWorkUnits, long maximumWorkUnits)
        {
            if (initialWorkUnits < 0 || maximumWorkUnits < initialWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG encoding exceeds the configured work limit.");
            _usedWorkUnits = initialWorkUnits;
            _maximumWorkUnits = maximumWorkUnits;
        }

        internal void ConsumePixels(int width, int height, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long pixels = checked((long)width * height);
            if (pixels > _maximumWorkUnits - _usedWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG preprocessing exceeds the configured work limit.");
            _usedWorkUnits += pixels;
        }

        internal void ConsumeCompressedBytes(int byteCount, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (byteCount > _maximumWorkUnits - _usedWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG compression exceeds the configured work limit.");
            _usedWorkUnits += byteCount;
        }

        /// <summary>Charges geometric-buffer growth copies of already-written compressed bytes.</summary>
        internal void ConsumeBufferedCopyBytes(int byteCount, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (byteCount > _maximumWorkUnits - _usedWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG compression-buffer growth exceeds the configured work limit.");
            _usedWorkUnits += byteCount;
        }

        /// <summary>Charges CRC computation and payload copying while compressed IDAT payload is serialized.</summary>
        internal void ConsumeSerializedBytes(int byteCount, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (byteCount > _maximumWorkUnits - _usedWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG serialization exceeds the configured work limit.");
            _usedWorkUnits += byteCount;
        }
    }

    private static uint CrcStep(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        return crc;
    }
}
