using System.Buffers.Binary;
using System.IO.Compression;

namespace Lokad.Svg.VisualDiff;

/// <summary>Independently decodes bounded, non-interlaced RGB8/RGBA8 PNGs without tRNS transparency.</summary>
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
        int bytesPerPixel = 0;
        bool headerSeen = false;
        bool paletteSeen = false;
        bool dataSeen = false;
        bool dataEnded = false;
        using MemoryStream compressed = new();
        while (position < input.Length)
        {
            if (input.Length - position < 12) throw new InvalidDataException("PNG chunk is truncated.");
            uint declaredLength = BinaryPrimitives.ReadUInt32BigEndian(input[position..]);
            if (declaredLength > (uint)(input.Length - position - 12)) throw new InvalidDataException("PNG chunk length is invalid.");
            int length = (int)declaredLength;
            ReadOnlySpan<byte> type = input.Slice(position + 4, 4);
            foreach (byte letter in type)
                if (letter is not (>= (byte)'A' and <= (byte)'Z') and not (>= (byte)'a' and <= (byte)'z')) throw new InvalidDataException("PNG chunk type is invalid.");
            if ((type[2] & 32) != 0) throw new InvalidDataException("PNG chunk type has an invalid reserved bit.");
            ReadOnlySpan<byte> data = input.Slice(position + 8, length);
            uint declaredCrc = BinaryPrimitives.ReadUInt32BigEndian(input.Slice(position + 8 + length, 4));
            if (declaredCrc != Crc32(type, data)) throw new InvalidDataException("PNG chunk CRC is invalid.");
            if (!headerSeen && !type.SequenceEqual("IHDR"u8)) throw new InvalidDataException("PNG header must be the first chunk.");
            if (dataSeen && !type.SequenceEqual("IDAT"u8)) dataEnded = true;
            if (type.SequenceEqual("IHDR"u8))
            {
                if (headerSeen || length != 13) throw new InvalidDataException("PNG header is invalid.");
                uint declaredWidth = BinaryPrimitives.ReadUInt32BigEndian(data);
                uint declaredHeight = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                if (declaredWidth is 0 or > int.MaxValue || declaredHeight is 0 or > int.MaxValue) throw new InvalidDataException("PNG dimensions are invalid.");
                width = (int)declaredWidth;
                height = (int)declaredHeight;
                if (data[10] != 0 || data[11] != 0 || data[12] > 1) throw new InvalidDataException("PNG header methods are invalid.");
                if ((long)width * height > maximumPixels || data[8] != 8 || data[12] != 0) throw new NotSupportedException("Only bounded non-interlaced RGB8/RGBA8 PNG images are supported.");
                bytesPerPixel = data[9] switch { 2 => 3, 6 => 4, _ => throw new NotSupportedException("Only bounded non-interlaced RGB8/RGBA8 PNG images are supported.") };
                if ((long)width * height * 4 > Array.MaxLength || ((long)width * bytesPerPixel + 1) * height > Array.MaxLength) throw new NotSupportedException("PNG decoded storage exceeds the supported range.");
                headerSeen = true;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (paletteSeen || dataSeen || length == 0 || length > 768 || length % 3 != 0) throw new InvalidDataException("PNG palette is invalid.");
                paletteSeen = true; // Optional suggested palettes do not change truecolor samples.
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (dataEnded) throw new InvalidDataException("PNG data chunks must be consecutive.");
                dataSeen = true;
                compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (!dataSeen || length != 0 || position + length + 12 != input.Length) throw new InvalidDataException("PNG end chunk is invalid.");
                return Inflate(compressed, width, height, bytesPerPixel);
            }
            else if (type.SequenceEqual("tRNS"u8)) throw new NotSupportedException("PNG tRNS transparency is not supported; use RGBA8.");
            else if ((type[0] & 32) == 0) throw new NotSupportedException("PNG critical chunk is not supported.");
            position += length + 12;
        }

        throw new InvalidDataException("PNG end chunk is missing.");
    }

    private static RgbaImage Inflate(MemoryStream compressed, int width, int height, int bytesPerPixel)
    {
        int stride = checked(width * bytesPerPixel);
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
                byte left = x >= bytesPerPixel ? pixels[destination + x - bytesPerPixel] : (byte)0;
                byte up = y > 0 ? pixels[destination + x - stride] : (byte)0;
                byte upperLeft = y > 0 && x >= bytesPerPixel ? pixels[destination + x - stride - bytesPerPixel] : (byte)0;
                pixels[destination + x] = filter switch { 0 => filtered[source + x], 1 => (byte)(filtered[source + x] + left), 2 => (byte)(filtered[source + x] + up), 3 => (byte)(filtered[source + x] + ((left + up) >> 1)), 4 => (byte)(filtered[source + x] + Paeth(left, up, upperLeft)), _ => throw new InvalidDataException("PNG filter is invalid.") };
            }
        }
        if (bytesPerPixel == 4) return new RgbaImage(width, height, pixels);
        byte[] rgba = new byte[checked(width * height * 4)];
        for (int source = 0, destination = 0; source < pixels.Length; source += 3, destination += 4)
        {
            rgba[destination] = pixels[source]; rgba[destination + 1] = pixels[source + 1]; rgba[destination + 2] = pixels[source + 2]; rgba[destination + 3] = 255;
        }
        return new RgbaImage(width, height, rgba);
    }

    private static byte Paeth(byte left, byte up, byte upperLeft) { int p = left + up - upperLeft; int a = Math.Abs(p - left); int b = Math.Abs(p - up); int c = Math.Abs(p - upperLeft); return a <= b && a <= c ? left : b <= c ? up : upperLeft; }
    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data) { uint crc = 0xffffffff; foreach (byte value in type) crc = Step(crc, value); foreach (byte value in data) crc = Step(crc, value); return ~crc; }
    private static uint Step(uint crc, byte value) { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u); return crc; }
}
