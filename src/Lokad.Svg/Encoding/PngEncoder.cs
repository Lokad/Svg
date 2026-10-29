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
        => Encode(width, height, rgba, budget, null, maximumEncodedPngBytes, cancellationToken);

    /// <summary>Encodes with temporary scratch under budget and optional retained output under a pre-established sibling scope.</summary>
    /// <remarks>The caller owns outputScope through result consumption and disposes it on failure. This method finishes all scratch users before return.</remarks>
    internal static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba, RenderBudget budget, IMemoryScope? outputScope,
        int maximumEncodedPngBytes, CancellationToken cancellationToken)
    {
        if (maximumEncodedPngBytes < FixedPngBytesWithoutIdat + PngChunkOverheadBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");

        using RenderBudget.BudgetReservation? minimumOutput = ReserveOutput(budget, outputScope, FixedPngBytesWithoutIdat + PngChunkOverheadBytes);
        using PngSegmentedBuffer compressed = Compress(width, height, rgba, budget, GetMaximumCompressedLength(maximumEncodedPngBytes), cancellationToken);
        int length = GetOutputLength(checked((int)compressed.Length));
        if (length > maximumEncodedPngBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
        using RenderBudget.BudgetReservation? remainingOutput = ReserveOutput(budget, outputScope, length - (FixedPngBytesWithoutIdat + PngChunkOverheadBytes));
        byte[] output = new byte[length];
        WritePng(width, height, compressed, output, cancellationToken);
        return output;

        static RenderBudget.BudgetReservation? ReserveOutput(RenderBudget scratch, IMemoryScope? owner, long bytes)
        {
            if (owner is null) return scratch.Reserve(bytes, SvgMemoryCategory.Output);
            if (!owner.TryReserve(bytes, out _)) throw new SvgException(SvgErrorCode.LimitExceeded, "The host refused PNG result storage.");
            // The pre-established result scope owns this charge until result disposal.
            return null;
        }
    }

    internal static int EncodeInto(int width, int height, ReadOnlySpan<byte> rgba, Memory<byte> destination, CancellationToken cancellationToken)
        => EncodeInto(width, height, rgba, destination, new RenderBudget(long.MaxValue), int.MaxValue, cancellationToken);

    internal static int EncodeInto(int width, int height, ReadOnlySpan<byte> rgba, Memory<byte> destination, int maximumEncodedPngBytes, CancellationToken cancellationToken)
        => EncodeInto(width, height, rgba, destination, new RenderBudget(long.MaxValue), maximumEncodedPngBytes, cancellationToken);

    internal static int EncodeInto(int width, int height, ReadOnlySpan<byte> rgba, Memory<byte> destination, RenderBudget budget, int maximumEncodedPngBytes, CancellationToken cancellationToken)
    {
        if (maximumEncodedPngBytes < FixedPngBytesWithoutIdat + PngChunkOverheadBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");

        using PngSegmentedBuffer compressed = Compress(width, height, rgba, budget, GetMaximumCompressedLength(maximumEncodedPngBytes), cancellationToken);
        int length = GetOutputLength(checked((int)compressed.Length));
        if (length > maximumEncodedPngBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
        if (destination.Length < length) throw new SvgException(SvgErrorCode.InsufficientDestination, "PNG destination is too small.");
        WritePng(width, height, compressed, destination.Span[..length], cancellationToken);
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

        using RenderBudget.BudgetReservation scratchReservation = budget.Reserve(AsyncScratchStorageBytes, SvgMemoryCategory.Png);
        byte[] scratch = new byte[AsyncScratchLength];
        await WritePngPrefixAsync(width, height, destination, scratch, cancellationToken).ConfigureAwait(false);
        using PngAsyncIdatOutputStream compressed = new(destination, budget, maximumEncodedPngBytes, scratch, cancellationToken);
        await CompressIntoAsync(width, height, rgba, budget, compressed, cancellationToken).ConfigureAwait(false);
        int length = await compressed.CompleteAsync().ConfigureAwait(false);
        await WriteChunkAsync(destination, IendChunkType, ReadOnlyMemory<byte>.Empty, scratch, cancellationToken).ConfigureAwait(false);
        return length;
    }

    private static PngSegmentedBuffer Compress(int width, int height, ReadOnlySpan<byte> rgba, RenderBudget budget, int maximumCompressedBytes, CancellationToken cancellationToken)
    {
        PngSegmentedBuffer compressed = new(budget, maximumCompressedBytes, cancellationToken);
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

    private static void CompressInto(int width, int height, ReadOnlySpan<byte> rgba, RenderBudget budget, PngCompressionOutput destination, CancellationToken cancellationToken)
    {
        int stride = ValidatePixelBuffer(width, height, rgba);
        cancellationToken.ThrowIfCancellationRequested();
        using RenderBudget.BudgetReservation rowReservation = budget.Reserve(checked((long)stride + 1), SvgMemoryCategory.Png);
        byte[] row = new byte[stride + 1];
        using ZLibStream zlib = new(destination, CompressionLevel.Optimal, true);
        try
        {
            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                row[0] = 0;
                rgba.Slice(y * stride, stride).CopyTo(row.AsSpan(1));
                ClearTransparentPixelRgb(row, cancellationToken);
                zlib.Write(row);
            }
        }
        catch
        {
            destination.Abort();
            throw;
        }
    }

    private static async Task CompressIntoAsync(int width, int height, ReadOnlyMemory<byte> rgba, RenderBudget budget, PngCompressionOutput destination, CancellationToken cancellationToken)
    {
        int stride = ValidatePixelBuffer(width, height, rgba.Span);
        cancellationToken.ThrowIfCancellationRequested();
        using RenderBudget.BudgetReservation rowReservation = budget.Reserve(checked((long)stride + 1), SvgMemoryCategory.Png);
        byte[] row = new byte[stride + 1];
        await using ZLibStream zlib = new(destination, CompressionLevel.Optimal, true);
        try
        {
            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                row[0] = 0;
                rgba.Span.Slice(y * stride, stride).CopyTo(row.AsSpan(1));
                ClearTransparentPixelRgb(row, cancellationToken);
                await zlib.WriteAsync(row, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            destination.Abort();
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
        // Pay for complete IDAT chunks first; a final partial chunk needs its own overhead.
        int available = maximumEncodedPngBytes - FixedPngBytesWithoutIdat;
        int chunkBytes = MaximumIdatPayloadBytes + PngChunkOverheadBytes;
        return available / chunkBytes * MaximumIdatPayloadBytes
            + Math.Max(0, available % chunkBytes - PngChunkOverheadBytes);
    }

    private static void WritePng(int width, int height, PngSegmentedBuffer compressed, Span<byte> output, CancellationToken cancellationToken)
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
        offset += WriteSegmentedIdatChunks(output[offset..], compressed, cancellationToken);
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

    private static async Task WritePngPrefixAsync(int width, int height, Stream output, byte[] scratch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await output.WriteAsync(PngSignature, cancellationToken).ConfigureAwait(false);
        BinaryPrimitives.WriteUInt32BigEndian(scratch.AsSpan(4), checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(scratch.AsSpan(8), checked((uint)height));
        scratch[12] = 8;
        scratch[13] = 6;
        await WriteChunkAsync(output, IhdrChunkType, scratch.AsMemory(4, 13), scratch, cancellationToken).ConfigureAwait(false);
    }

    private static int WriteSegmentedIdatChunks(Span<byte> output, PngSegmentedBuffer compressed, CancellationToken cancellationToken)
    {
        if (compressed.Length == 0) return WriteChunk(output, "IDAT"u8, [], cancellationToken);
        int outputOffset = 0;
        int remaining = checked((int)compressed.Length);
        int segmentIndex = 0;
        int segmentOffset = 0;
        while (remaining > 0)
        {
            int chunkLength = Math.Min(MaximumIdatPayloadBytes, remaining);
            outputOffset += WriteSegmentedChunk(output[outputOffset..], compressed, ref segmentIndex, ref segmentOffset, chunkLength, cancellationToken);
            remaining -= chunkLength;
        }
        return outputOffset;
    }

    private static int WriteSegmentedChunk(Span<byte> output, PngSegmentedBuffer compressed, ref int segmentIndex, ref int segmentOffset, int chunkLength, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BinaryPrimitives.WriteUInt32BigEndian(output, checked((uint)chunkLength));
        IdatChunkType.CopyTo(output[4..]);
        uint crc = 0xffffffff;
        foreach (byte value in IdatChunkType) crc = CrcStep(crc, value);
        int outputPosition = 8;
        int pending = chunkLength;
        while (pending > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PngSegmentedBuffer.Segment segment = compressed.Segments[segmentIndex];
            int fragment = Math.Min(Math.Min(4096, segment.Used - segmentOffset), pending);
            segment.Buffer.AsSpan(segmentOffset, fragment).CopyTo(output.Slice(outputPosition, fragment));
            for (int index = 0; index < fragment; index++) crc = CrcStep(crc, output[outputPosition + index]);
            outputPosition += fragment;
            segmentOffset += fragment;
            pending -= fragment;
            if (segmentOffset == segment.Used) { segmentIndex++; segmentOffset = 0; }
        }
        BinaryPrimitives.WriteUInt32BigEndian(output.Slice(outputPosition), ~crc);
        return checked(12 + chunkLength);
    }

    private static int WriteChunk(Span<byte> output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int length = checked(12 + data.Length);
        uint crc = ComputeChunkCrc(type, data, cancellationToken);
        BinaryPrimitives.WriteUInt32BigEndian(output, checked((uint)data.Length));
        type.CopyTo(output[4..]); data.CopyTo(output[8..]);
        BinaryPrimitives.WriteUInt32BigEndian(output.Slice(8 + data.Length), ~crc);
        return length;
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        uint crc = ComputeChunkCrc(type, data, cancellationToken);
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

    private static async Task WriteChunkAsync(Stream output, ReadOnlyMemory<byte> type, ReadOnlyMemory<byte> data, byte[] scratch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        uint crc = ComputeChunkCrc(type.Span, data.Span, cancellationToken);
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

    /// <summary>Owns compressed staging for one synchronous or asynchronous encoder.</summary>
    /// <remarks>Implementations discard writes after Abort so compressor disposal cannot retry admission or output after an earlier failure. They retain reservations until disposal, leave the borrowed destination open and serialize all writes and disposal within one operation.</remarks>
    private abstract class PngCompressionOutput : Stream
    {
        internal bool IsAborted { get; private set; }
        internal void Abort() => IsAborted = true;
    }

    private sealed class PngIdatOutputStream : PngCompressionOutput
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
            if (IsAborted) return;
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
                if (_bufferedLength == destination.Length && !TryGrowBuffer())
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
                _cancellationToken.ThrowIfCancellationRequested();
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

    private sealed class PngAsyncIdatOutputStream : PngCompressionOutput
    {
        private readonly Stream _output;
        private readonly RenderBudget _budget;
        private readonly int _maximumEncodedPngBytes;
        private readonly byte[] _scratch;
        private readonly CancellationToken _cancellationToken;
        private RenderBudget.BudgetReservation? _reservation;
        private byte[]? _buffer;
        private int _bufferedLength;
        private int _compressedLength;
        private int _idatChunkCount;
        private bool _isComplete;

        internal PngAsyncIdatOutputStream(Stream output, RenderBudget budget, int maximumEncodedPngBytes, byte[] scratch, CancellationToken cancellationToken)
        {
            _output = output;
            _budget = budget;
            _maximumEncodedPngBytes = maximumEncodedPngBytes;
            _scratch = scratch;
            _cancellationToken = cancellationToken;
        }

        internal async Task<int> CompleteAsync()
        {
            if (_isComplete) throw new InvalidOperationException("PNG IDAT output was already completed.");
            _cancellationToken.ThrowIfCancellationRequested();
            if (_compressedLength == 0)
            {
                EnsureOutputLength(0, _idatChunkCount + 1);
                await WriteChunkAsync(_output, IdatChunkType, ReadOnlyMemory<byte>.Empty, _scratch, _cancellationToken).ConfigureAwait(false);
                _idatChunkCount++;
            }
            else if (_bufferedLength > 0)
            {
                byte[] buffer = _buffer ?? throw new InvalidOperationException("PNG IDAT buffer allocation did not complete.");
                EnsureOutputLength(_compressedLength, _idatChunkCount + 1);
                await WriteChunkAsync(_output, IdatChunkType, buffer.AsMemory(0, _bufferedLength), _scratch, _cancellationToken).ConfigureAwait(false);
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
            if (IsAborted) return;
            if (_isComplete) throw new InvalidOperationException("PNG IDAT output was already completed.");
            cancellationToken.ThrowIfCancellationRequested();
            _cancellationToken.ThrowIfCancellationRequested();
            while (!buffer.IsEmpty)
            {
                if (_bufferedLength == 0 && buffer.Length >= MaximumIdatPayloadBytes)
                {
                    int directCompressedLength = checked(_compressedLength + MaximumIdatPayloadBytes);
                    EnsureOutputLength(directCompressedLength, _idatChunkCount + 1);
                    await WriteChunkAsync(_output, IdatChunkType, buffer[..MaximumIdatPayloadBytes], _scratch, _cancellationToken).ConfigureAwait(false);
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
                    await WriteChunkAsync(_output, IdatChunkType, destination, _scratch, _cancellationToken).ConfigureAwait(false);
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
                _cancellationToken.ThrowIfCancellationRequested();
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

    /// <summary>Accumulates compressed bytes in budgeted segments without geometric copies.</summary>
    private sealed class PngSegmentedBuffer : PngCompressionOutput
    {
        private readonly RenderBudget _budget;
        private readonly int _maximumLength;
        private readonly CancellationToken _cancellationToken;
        private readonly List<Segment> _segments = new();
        private int _length;

        /// <summary>Retains one budgeted segment; every segment except the last is full.</summary>
        internal sealed class Segment(byte[] buffer, RenderBudget.BudgetReservation? reservation)
        {
            internal byte[] Buffer = buffer;
            internal int Used;
            internal RenderBudget.BudgetReservation? Reservation = reservation;
        }

        internal PngSegmentedBuffer(RenderBudget budget, int maximumLength, CancellationToken cancellationToken)
        {
            _budget = budget;
            _maximumLength = maximumLength;
            _cancellationToken = cancellationToken;
        }

        internal IReadOnlyList<Segment> Segments => _segments;

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
            if (IsAborted) return;
            _cancellationToken.ThrowIfCancellationRequested();
            int requiredLength = checked(_length + buffer.Length);
            if (requiredLength > _maximumLength) throw new SvgException(SvgErrorCode.LimitExceeded, "PNG output exceeds the configured encoded-size limit.");
            while (!buffer.IsEmpty)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                Segment tail = EnsureTail(buffer.Length);
                int count = Math.Min(tail.Buffer.Length - tail.Used, buffer.Length);
                CopyWithCheckpoints(buffer[..count], tail.Buffer.AsSpan(tail.Used));
                tail.Used += count;
                _length = checked(_length + count);
                buffer = buffer[count..];
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Segment segment in _segments) segment.Reservation?.Dispose();
                _segments.Clear();
                _length = 0;
            }
            base.Dispose(disposing);
        }

        private Segment EnsureTail(int remaining)
        {
            if (_segments.Count != 0)
            {
                Segment tail = _segments[^1];
                if (tail.Used < tail.Buffer.Length) return tail;
            }
            // Later segments track the current write with the same 64-byte floor; chunk boundaries
            // always come from the cumulative slicer below. This class has a single caller feeding
            // DeflateStream chunks, so no adversarial write-fragmentation guard is needed.
            int capacity = Math.Min(Math.Max(64, remaining), MaximumIdatPayloadBytes);
            RenderBudget.BudgetReservation reservation = _budget.Reserve(capacity, SvgMemoryCategory.Png);
            byte[] segment;
            try { segment = new byte[capacity]; }
            catch { reservation.Dispose(); throw; }
            Segment created = new(segment, reservation);
            _segments.Add(created);
            return created;
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
    /// <summary>Computes one PNG chunk CRC with deterministic cancellation checkpoints.</summary>
    private static uint ComputeChunkCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data, CancellationToken cancellationToken)
    {
        uint crc = 0xffffffff;
        foreach (byte value in type) crc = CrcStep(crc, value);
        for (int index = 0; index < data.Length; index++)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            crc = CrcStep(crc, data[index]);
        }
        return crc;
    }

    /// <summary>Validates pixel dimensions against their buffer and returns the row stride.</summary>
    private static int ValidatePixelBuffer(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        int stride = checked(width * 4);
        if (rgba.Length != checked(stride * height)) throw new ArgumentException("Pixel buffer length does not match dimensions.", nameof(rgba));
        return stride;
    }

    /// <summary>Clears RGB channels of fully transparent pixels in one filter row.</summary>
    private static void ClearTransparentPixelRgb(Span<byte> row, CancellationToken cancellationToken)
    {
        for (int x = 1; x < row.Length; x += 4)
        {
            if (((x - 1) & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (row[x + 3] == 0) { row[x] = 0; row[x + 1] = 0; row[x + 2] = 0; }
        }
    }

    private static uint CrcStep(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        return crc;
    }
}
