using System.Buffers.Binary;
using System.IO.Compression;

namespace Lokad.Svg.Encoding;

/// <summary>Encodes tightly packed straight-alpha RGBA8 pixels as a non-interlaced PNG.</summary>
internal static class PngEncoder
{
    internal static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba, CancellationToken cancellationToken)
    {
        byte[] compressed = Compress(width, height, rgba, cancellationToken);
        byte[] output = new byte[GetOutputLength(compressed.Length)];
        WritePng(width, height, compressed, output);
        return output;
    }

    internal static int EncodeInto(int width, int height, ReadOnlySpan<byte> rgba, Memory<byte> destination, CancellationToken cancellationToken)
    {
        byte[] compressed = Compress(width, height, rgba, cancellationToken);
        int length = GetOutputLength(compressed.Length);
        if (destination.Length < length) throw new SvgException(SvgErrorCode.InsufficientDestination, "PNG destination is too small.");
        WritePng(width, height, compressed, destination.Span[..length]);
        return length;
    }

    private static byte[] Compress(int width, int height, ReadOnlySpan<byte> rgba, CancellationToken cancellationToken)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        int stride = checked(width * 4);
        if (rgba.Length != checked(stride * height)) throw new ArgumentException("Pixel buffer length does not match dimensions.", nameof(rgba));
        byte[] filtered = new byte[checked((stride + 1) * height)];
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rgba.Slice(y * stride, stride).CopyTo(filtered.AsSpan(y * (stride + 1) + 1));
        }
        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, true)) zlib.Write(filtered);
        return compressed.ToArray();
    }

    private static int GetOutputLength(int compressedLength) => checked(8 + 25 + 12 + compressedLength + 12);

    private static void WritePng(int width, int height, ReadOnlySpan<byte> compressed, Span<byte> output)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        signature.CopyTo(output);
        int offset = 8;
        offset += WriteChunk(output[offset..], "IHDR"u8, CreateHeader(width, height));
        offset += WriteChunk(output[offset..], "IDAT"u8, compressed);
        offset += WriteChunk(output[offset..], "IEND"u8, []);
    }

    private static byte[] CreateHeader(int width, int height)
    {
        byte[] header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), checked((uint)height));
        header[8] = 8;
        header[9] = 6;
        return header;
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        output.Write(length); output.Write(type); output.Write(data);
        uint crc = 0xffffffff;
        foreach (byte value in type) crc = CrcStep(crc, value);
        foreach (byte value in data) crc = CrcStep(crc, value);
        BinaryPrimitives.WriteUInt32BigEndian(length, ~crc);
        output.Write(length);
    }

    private static int WriteChunk(Span<byte> output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        int length = checked(12 + data.Length);
        BinaryPrimitives.WriteUInt32BigEndian(output, checked((uint)data.Length));
        type.CopyTo(output[4..]); data.CopyTo(output[8..]);
        uint crc = 0xffffffff;
        foreach (byte value in type) crc = CrcStep(crc, value);
        foreach (byte value in data) crc = CrcStep(crc, value);
        BinaryPrimitives.WriteUInt32BigEndian(output.Slice(8 + data.Length), ~crc);
        return length;
    }

    private static uint CrcStep(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        return crc;
    }
}
