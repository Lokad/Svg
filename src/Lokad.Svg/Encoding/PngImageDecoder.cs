using System.Buffers.Binary;
using System.IO.Compression;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Encoding;

/// <summary>Decodes solid-color 8-bit PNG data URIs into a single color without retaining pixels.</summary>
/// <summary>Retains one decoded solid-color image with its intrinsic dimensions.</summary>
internal readonly record struct DecodedSolidImage(SvgColor Color, int Width, int Height);

internal static class PngImageDecoder
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    private static uint[]? _crcTable;

    internal static DecodedSolidImage DecodeSolidColor(ReadOnlyMemory<byte> png, SvgRenderLimits limits, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(workCounter);
        if (!png.Span.StartsWith(PngSignature)) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded image data is not a PNG file.");
        int width = 0;
        int height = 0;
        int channels = 0;
        bool isPaletteType = false;
        byte[]? transparent = null;
        byte[]? palette = null;
        bool seenImageData = false;
        using var compressed = new MemoryStream();
        using var storage = new WorkingStorage(budget);
        long compressedLength = 0;
        int position = PngSignature.Length;
        bool seenHeader = false;
        bool seenEnd = false;
        while (position < png.Length)
        {
            workCounter.Consume(cancellationToken);
            if (position > png.Length - 12) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data ends inside a chunk header.");
            int length = BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(position, 4));
            if (length < 0 || length > png.Length - position - 12) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data has an invalid chunk length.");
            ReadOnlySpan<byte> type = png.Span.Slice(position + 4, 4);
            ReadOnlySpan<byte> data = png.Span.Slice(position + 8, length);
            uint expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.Span.Slice(position + 8 + length, 4));
            workCounter.ConsumeBytes(length, cancellationToken);
            if (ComputeCrc(type, data) != expectedCrc) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data fails its chunk integrity check.");
            if (type.SequenceEqual("IHDR"u8))
            {
                if (seenHeader || position != PngSignature.Length) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data misplaces its header chunk.");
                if (length != 13) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data has a malformed header chunk.");
                width = BinaryPrimitives.ReadInt32BigEndian(data.Slice(0, 4));
                height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4));
                int bitDepth = data[8];
                int colorType = data[9];
                if (width <= 0 || height <= 0 || width > limits.MaximumDimension || height > limits.MaximumDimension) throw new SvgException(SvgErrorCode.LimitExceeded, "Embedded PNG dimensions exceed the configured dimension limit.");
                long pixelCount = (long)width * height;
                if (pixelCount > limits.MaximumPixels) throw new SvgException(SvgErrorCode.LimitExceeded, "Embedded PNG pixel count exceeds the configured pixel limit.");
                isPaletteType = colorType == 3;
                channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 6 => 4, _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only grayscale, palette, RGB, and RGBA embedded PNG data are implemented.") };
                if (bitDepth != 8) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only 8-bit embedded PNG data are implemented.");
                if (data[10] != 0) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data uses an unsupported compression method.");
                if (data[11] != 0) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data uses an unsupported filter method.");
                if (data[12] != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Interlaced embedded PNG data are not implemented.");
                seenHeader = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (!seenHeader || seenEnd) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data misplaces its image data.");
                if (length > 0)
                {
                    compressedLength += length;
                    if (compressedLength > limits.MaximumAccountedBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "Embedded PNG data exceeds the configured accounting limit.");
                    storage.ReserveCompressed(compressedLength);
                }
                compressed.Write(data);
                seenImageData = true;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (!seenHeader || seenEnd || seenImageData) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data misplaces its palette chunk.");
                if (palette is not null) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data repeats its palette chunk.");
                if (length == 0 || length % 3 != 0 || length > 768) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data has a malformed palette chunk.");
                storage.ReservePalette(length);
                try { palette = data.ToArray(); }
                catch { storage.ReleasePalette(); throw; }
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (!seenHeader || seenEnd) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data misplaces its end chunk.");
                if (length != 0) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data has a malformed end chunk.");
                seenEnd = true;
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                if (!seenHeader || seenEnd) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data misplaces its transparency chunk.");
                if (transparent is not null) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data repeats its transparency chunk.");
                ValidateTransparencyLength(data, channels, isPaletteType);
                if (length > 0)
                {
                    storage.ReserveTransparency(length);
                    try { transparent = data.ToArray(); }
                    catch { storage.ReleaseTransparency(); throw; }
                }
                else
                {
                    transparent = [];
                }
            }
            else if (IsColorManaged(type))
            {
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Color-managed embedded PNG data are not implemented.");
            }
            else if (type.SequenceEqual("sBIT"u8) || type.SequenceEqual("eXIf"u8))
            {
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Significant-bit and orientation-carrying embedded PNG data are not implemented.");
            }
            else if (IsCritical(type))
            {
                throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data carries an unrecognized critical chunk.");
            }
            position += length + 12;
        }
        if (!seenHeader || !seenEnd) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data is missing its header or end chunk.");
        if (position != png.Length) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data has trailing bytes.");
        long rowBytes = (long)width * channels;
        long pixelBytes = checked((rowBytes + 1) * height);
        if (pixelBytes > limits.MaximumAccountedBytes || rowBytes > limits.MaximumAccountedBytes / 2 || pixelBytes > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "Embedded PNG pixel storage exceeds the configured accounting limit.");
        using RenderBudget.BudgetReservation reservation = budget.Reserve(checked(pixelBytes + 2 * rowBytes), SvgMemoryCategory.Parser);
        byte[] pixels = new byte[(int)pixelBytes];
        compressed.Position = 0;
        try
        {
            using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
            {
                int filled = 0;
                while (filled < pixels.Length)
                {
                    workCounter.Consume(cancellationToken);
                    int read = zlib.Read(pixels, filled, pixels.Length - filled);
                    if (read == 0) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data ends inside its pixel stream.");
                    filled += read;
                }
                if (zlib.ReadByte() != -1) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data carries trailing pixel bytes.");
            }
        }
        catch (InvalidDataException)
        {
            throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data carries corrupt compressed pixel data.");
        }
        return new DecodedSolidImage(ScanSolidColor(pixels, width, height, channels, transparent, palette, isPaletteType, (int)rowBytes, workCounter, cancellationToken), width, height);
    }

    private static SvgColor ScanSolidColor(byte[] pixels, int width, int height, int channels, byte[]? transparent, byte[]? palette, bool isPaletteType, int rowBytes, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (isPaletteType && palette is null) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data is missing its palette chunk.");
        if (isPaletteType && transparent is not null && transparent.Length > 256) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data has a malformed transparency chunk.");
        byte[] previous = new byte[rowBytes];
        byte[] current = new byte[rowBytes];
        int red = -1;
        int green = -1;
        int blue = -1;
        int alpha = -1;
        int position = 0;
        for (int y = 0; y < height; y++)
        {
            workCounter.Consume(cancellationToken);
            workCounter.ConsumeBytes(rowBytes, cancellationToken);
            int filter = pixels[position];
            if (filter > 4) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data uses an unsupported row filter.");
            Buffer.BlockCopy(pixels, position + 1, current, 0, current.Length);
            UnfilterRow(filter, current, previous, channels, cancellationToken);
            for (int x = 0; x < width; x++)
            {
                if ((x & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                int offset = x * channels;
                int pixelRed;
                int pixelGreen;
                int pixelBlue;
                int pixelAlpha;
                if (isPaletteType)
                {
                    int index = current[offset];
                    if (palette is null || index * 3 + 2 >= palette.Length) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data references a missing palette entry.");
                    pixelRed = palette[index * 3];
                    pixelGreen = palette[index * 3 + 1];
                    pixelBlue = palette[index * 3 + 2];
                    pixelAlpha = transparent is not null && index < transparent.Length ? transparent[index] : 255;
                }
                else
                {
                    pixelRed = current[offset];
                    pixelGreen = channels == 1 ? current[offset] : current[offset + 1];
                    pixelBlue = channels == 1 ? current[offset] : current[offset + 2];
                    pixelAlpha = channels == 4 ? current[offset + 3] : 255;
                    if (transparent is not null) pixelAlpha = ApplyTransparency(pixelRed, pixelGreen, pixelBlue, pixelAlpha, channels, transparent);
                }
                if (red < 0)
                {
                    red = pixelRed;
                    green = pixelGreen;
                    blue = pixelBlue;
                    alpha = pixelAlpha;
                }
                else if (pixelRed != red || pixelGreen != green || pixelBlue != blue || pixelAlpha != alpha)
                {
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only solid-color embedded PNG data are implemented.");
                }
            }
            (previous, current) = (current, previous);
            position += current.Length + 1;
        }
        return new SvgColor((byte)red, (byte)green, (byte)blue, (byte)alpha);
    }

    private static int ApplyTransparency(int red, int green, int blue, int alpha, int channels, byte[] transparent)
    {
        if (channels == 1)
        {
            if (transparent.Length != 2) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data has a malformed transparency chunk.");
            int transparentGray = (transparent[0] << 8) | transparent[1];
            return red == transparentGray ? 0 : alpha;
        }
        if (channels == 3)
        {
            if (transparent.Length != 6) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data has a malformed transparency chunk.");
            int transparentRed = (transparent[0] << 8) | transparent[1];
            int transparentGreen = (transparent[2] << 8) | transparent[3];
            int transparentBlue = (transparent[4] << 8) | transparent[5];
            return red == transparentRed && green == transparentGreen && blue == transparentBlue ? 0 : alpha;
        }
        throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data carries transparency for an alpha channel.");
    }

    private static void UnfilterRow(int filter, byte[] current, byte[] previous, int channels, CancellationToken cancellationToken)
    {
        switch (filter)
        {
            case 0:
                return;
            case 1:
                for (int index = channels; index < current.Length; index++)
                {
                    if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                    current[index] += current[index - channels];
                }
                return;
            case 2:
                for (int index = 0; index < current.Length; index++)
                {
                    if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                    current[index] += previous[index];
                }
                return;
            case 3:
                for (int index = 0; index < current.Length; index++)
                {
                    if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                    int left = index >= channels ? current[index - channels] : 0;
                    current[index] += (byte)((left + previous[index]) / 2);
                }
                return;
            default:
                for (int index = 0; index < current.Length; index++)
                {
                    if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                    int left = index >= channels ? current[index - channels] : 0;
                    int above = previous[index];
                    int upperLeft = index >= channels ? previous[index - channels] : 0;
                    current[index] += (byte)PaethPredictor(left, above, upperLeft);
                }
                return;
        }
    }

    private static int PaethPredictor(int left, int above, int upperLeft)
    {
        int estimate = left + above - upperLeft;
        int leftError = Math.Abs(estimate - left);
        int aboveError = Math.Abs(estimate - above);
        int upperLeftError = Math.Abs(estimate - upperLeft);
        if (leftError <= aboveError && leftError <= upperLeftError) return left;
        return aboveError <= upperLeftError ? above : upperLeft;
    }

    /// <summary>Owns the budgeted IDAT, palette and transparency copies retained while one PNG decodes.</summary>
    private sealed class WorkingStorage : IDisposable
    {
        private readonly RenderBudget _budget;
        private RenderBudget.BudgetReservation? _palette;
        private RenderBudget.BudgetReservation? _transparency;
        private RenderBudget.BudgetReservation? _compressed;

        internal WorkingStorage(RenderBudget budget)
        {
            ArgumentNullException.ThrowIfNull(budget);
            _budget = budget;
        }

        internal void ReservePalette(int length) => _palette = _budget.Reserve(length, SvgMemoryCategory.Parser);

        internal void ReleasePalette()
        {
            _palette?.Dispose();
            _palette = null;
        }

        internal void ReserveTransparency(int length) => _transparency = _budget.Reserve(length, SvgMemoryCategory.Parser);

        internal void ReleaseTransparency()
        {
            _transparency?.Dispose();
            _transparency = null;
        }

        internal void ReserveCompressed(long total)
        {
            RenderBudget.BudgetReservation next = _budget.Reserve(total, SvgMemoryCategory.Parser);
            _compressed?.Dispose();
            _compressed = next;
        }

        public void Dispose()
        {
            _compressed?.Dispose();
            _compressed = null;
            _transparency?.Dispose();
            _transparency = null;
            _palette?.Dispose();
            _palette = null;
        }
    }

    private static void ValidateTransparencyLength(ReadOnlySpan<byte> data, int channels, bool isPaletteType)
    {
        if (isPaletteType)
        {
            if (data.Length > 256) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data has a malformed transparency chunk.");
        }
        else if (channels == 1)
        {
            if (data.Length != 2) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data has a malformed transparency chunk.");
        }
        else if (channels == 3)
        {
            if (data.Length != 6) throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data has a malformed transparency chunk.");
        }
        else
        {
            throw new SvgException(SvgErrorCode.MalformedInput, "Embedded PNG data carries transparency for an alpha channel.");
        }
    }

    private static bool IsCritical(ReadOnlySpan<byte> type) => (type[0] & 0x20) == 0;

    private static bool IsColorManaged(ReadOnlySpan<byte> type)
        => type.SequenceEqual("gAMA"u8) || type.SequenceEqual("cHRM"u8) || type.SequenceEqual("sRGB"u8) || type.SequenceEqual("iCCP"u8);

    private static uint ComputeCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        _crcTable ??= BuildCrcTable();
        uint crc = 0xFFFFFFFF;
        foreach (byte value in type) crc = _crcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        foreach (byte value in data) crc = _crcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint value = 0; value < 256; value++)
        {
            uint entry = value;
            for (int bit = 0; bit < 8; bit++) entry = (entry & 1) == 1 ? 0xEDB88320 ^ (entry >> 1) : entry >> 1;
            table[value] = entry;
        }
        return table;
    }
}