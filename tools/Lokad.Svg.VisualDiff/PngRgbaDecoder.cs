using System.Buffers.Binary;
using System.IO.Compression;

namespace Lokad.Svg.VisualDiff;

/// <summary>Decodes the non-interlaced, 8-bit RGBA PNG subset used by the initial oracle.</summary>
public static class PngRgbaDecoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static RgbaImage Decode(ReadOnlyMemory<byte> png, int maximumPixels)
    {
        if (maximumPixels <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPixels));
        ReadOnlySpan<byte> input = png.Span;
        if (input.Length < Signature.Length || !input[..Signature.Length].SequenceEqual(Signature)) throw new InvalidDataException("PNG signature is invalid.");
        int position = Signature.Length;
        int width = 0;
        int height = 0;
        bool headerSeen = false;
        using MemoryStream compressed = new();
        while (position < input.Length)
        {
            if (input.Length - position < 12) throw new InvalidDataException("PNG chunk is truncated.");
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(input[position..]));
            if (length < 0 || input.Length - position - 12 < length) throw new InvalidDataException("PNG chunk length is invalid.");
            ReadOnlySpan<byte> type = input.Slice(position + 4, 4);
            ReadOnlySpan<byte> data = input.Slice(position + 8, length);
            uint declaredCrc = BinaryPrimitives.ReadUInt32BigEndian(input.Slice(position + 8 + length, 4));
            if (declaredCrc != Crc32(type, data)) throw new InvalidDataException("PNG chunk CRC is invalid.");
            if (type.SequenceEqual("IHDR"u8))
            {
                if (headerSeen || length != 13) throw new InvalidDataException("PNG header is invalid.");
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]));
                if (width <= 0 || height <= 0 || (long)width * height > maximumPixels || data[8] != 8 || data[9] != 6 || data[12] != 0) throw new NotSupportedException("Only bounded non-interlaced RGBA8 PNG images are supported.");
                headerSeen = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (!headerSeen) throw new InvalidDataException("PNG data precedes its header.");
                compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (length != 0 || position + length + 12 != input.Length) throw new InvalidDataException("PNG end chunk is invalid.");
                return Inflate(compressed, width, height);
            }
            position += length + 12;
        }

        throw new InvalidDataException("PNG end chunk is missing.");
    }

    private static RgbaImage Inflate(MemoryStream compressed, int width, int height)
    {
        int stride = checked(width * 4);
        byte[] filtered = new byte[checked((stride + 1) * height)];
        compressed.Position = 0;
        using (ZLibStream zlib = new(compressed, CompressionMode.Decompress, true))
        {
            zlib.ReadExactly(filtered);
            if (zlib.ReadByte() != -1) throw new InvalidDataException("PNG data exceeds expected scanline size.");
        }

        byte[] pixels = new byte[checked(stride * height)];
        for (int y = 0; y < height; y++)
        {
            int source = y * (stride + 1);
            int destination = y * stride;
            byte filter = filtered[source++];
            for (int x = 0; x < stride; x++)
            {
                byte left = x >= 4 ? pixels[destination + x - 4] : (byte)0;
                byte up = y > 0 ? pixels[destination + x - stride] : (byte)0;
                byte upperLeft = y > 0 && x >= 4 ? pixels[destination + x - stride - 4] : (byte)0;
                pixels[destination + x] = filter switch { 0 => filtered[source + x], 1 => (byte)(filtered[source + x] + left), 2 => (byte)(filtered[source + x] + up), 3 => (byte)(filtered[source + x] + ((left + up) >> 1)), 4 => (byte)(filtered[source + x] + Paeth(left, up, upperLeft)), _ => throw new InvalidDataException("PNG filter is invalid.") };
            }
        }
        return new RgbaImage(width, height, pixels);
    }

    private static byte Paeth(byte left, byte up, byte upperLeft) { int p = left + up - upperLeft; int a = Math.Abs(p - left); int b = Math.Abs(p - up); int c = Math.Abs(p - upperLeft); return a <= b && a <= c ? left : b <= c ? up : upperLeft; }
    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data) { uint crc = 0xffffffff; foreach (byte value in type) crc = Step(crc, value); foreach (byte value in data) crc = Step(crc, value); return ~crc; }
    private static uint Step(uint crc, byte value) { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u); return crc; }
}
