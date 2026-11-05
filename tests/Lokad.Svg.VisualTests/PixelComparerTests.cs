using Lokad.Svg.VisualDiff;
using Lokad.Svg.Encoding;
using System.Buffers.Binary;
using System.IO.Compression;

namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Mechanism")]
public sealed class PixelComparerTests
{
    private static readonly byte[] OnePixelRgbaPng =
        CreatePng(("IHDR", Header(1, 1, 6)), ("IDAT", Compress([0, 10, 20, 30, 40])), ("IEND", []));

    [Fact]
    public void One_channel_difference_is_an_exact_failure()
    {
        RgbaImage reference = new(1, 1, [10, 20, 30, 40]);
        RgbaImage candidate = new(1, 1, [10, 21, 30, 40]);

        PixelComparison result = PixelComparer.Compare(reference, candidate);

        Assert.True(result.DimensionsMatch);
        Assert.Equal(1, result.ChangedPixelCount);
        Assert.Equal(1, result.MaximumChannelError);
        Assert.Equal(0, result.MaximumAlphaError);
        Assert.Equal(0, result.MinimumMismatchX);
        Assert.Equal(0, result.MaximumMismatchY);
    }

    [Fact]
    public void Invisible_rgb_is_canonicalized_but_alpha_is_not()
    {
        RgbaImage reference = new(1, 1, [1, 2, 3, 0]);
        RgbaImage candidate = new(1, 1, [200, 100, 50, 0]);

        PixelComparison result = PixelComparer.Compare(reference, candidate);

        Assert.Equal(0, result.ChangedPixelCount);
    }

    [Fact]
    public void Alpha_differences_are_reported_separately_with_mismatch_bounds()
    {
        RgbaImage reference = new(2, 1, [0, 0, 0, 0, 0, 0, 0, 12]);
        RgbaImage candidate = new(2, 1, [0, 0, 0, 0, 0, 0, 0, 20]);

        PixelComparison result = PixelComparer.Compare(reference, candidate);

        Assert.Equal(1, result.AlphaChangedPixelCount);
        Assert.Equal(8, result.MaximumAlphaError);
        Assert.Equal(1, result.MinimumMismatchX);
        Assert.Equal(1, result.MaximumMismatchX);
        Assert.Equal(0, result.MinimumMismatchY);
    }

    [Fact]
    public void Dimension_difference_fails_without_intersection_comparison()
    {
        PixelComparison result = PixelComparer.Compare(new RgbaImage(1, 1, [0, 0, 0, 0]), new RgbaImage(2, 1, [0, 0, 0, 0, 0, 0, 0, 0]));

        Assert.False(result.DimensionsMatch);
    }

    [Fact]
    public void Independent_decoder_reads_a_known_rgba_png()
    {
        RgbaImage image = PngRgbaDecoder.Decode(OnePixelRgbaPng, 1);

        Assert.Equal(1, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal(new byte[] { 10, 20, 30, 40 }, image.Pixels);
    }

    [Fact]
    public void Independent_decoder_rejects_a_corrupted_chunk()
    {
        byte[] corrupted = (byte[])OnePixelRgbaPng.Clone();
        corrupted[20] ^= 1;

        Assert.Throws<InvalidDataException>(() => PngRgbaDecoder.Decode(corrupted, 1));
    }

    [Fact]
    public void Independent_decoder_enforces_its_pixel_limit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PngRgbaDecoder.Decode(OnePixelRgbaPng, 0));
    }

    [Fact]
    public void Independent_decoder_reads_rgb_with_optional_palette_and_ancillary_chunks()
    {
        byte[] data = Compress([0, 10, 20, 30]);
        byte[] png = CreatePng(("IHDR", Header(1, 1, 2)), ("PLTE", [10, 20, 30]),
            ("tEXt", "key\0value"u8.ToArray()), ("IDAT", data[..2]), ("IDAT", data[2..]), ("IEND", []));

        Assert.Equal(new byte[] { 10, 20, 30, 255 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    [Fact]
    public void Independent_decoder_rejects_transparency_instead_of_comparing_it_as_opaque()
    {
        byte[] png = CreatePng(("IHDR", Header(1, 1, 2)), ("tRNS", [0, 10, 0, 20, 0, 30]),
            ("IDAT", Compress([0, 10, 20, 30])), ("IEND", []));

        Assert.Throws<NotSupportedException>(() => PngRgbaDecoder.Decode(png, 1));
    }

    [Theory]
    [InlineData(10, 1)]
    [InlineData(11, 1)]
    [InlineData(12, 2)]
    public void Independent_decoder_rejects_invalid_header_methods(int offset, byte value)
    {
        byte[] header = Header(1, 1, 6);
        header[offset] = value;
        byte[] png = CreatePng(("IHDR", header), ("IDAT", Compress([0, 10, 20, 30, 40])), ("IEND", []));

        Assert.Throws<InvalidDataException>(() => PngRgbaDecoder.Decode(png, 1));
    }

    [Fact]
    public void Independent_decoder_rejects_unknown_critical_chunks()
    {
        byte[] png = CreatePng(("IHDR", Header(1, 1, 6)), ("ABCD", []),
            ("IDAT", Compress([0, 10, 20, 30, 40])), ("IEND", []));

        Assert.Throws<NotSupportedException>(() => PngRgbaDecoder.Decode(png, 1));
    }

    [Theory]
    [InlineData(0x7fffffffu)]
    [InlineData(0x80000000u)]
    [InlineData(0xffffffffu)]
    public void Independent_decoder_rejects_oversized_chunk_lengths_without_overflow(uint length)
    {
        byte[] png = (byte[])OnePixelRgbaPng.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(8), length);

        Assert.Throws<InvalidDataException>(() => PngRgbaDecoder.Decode(png, 1));
    }

    [Theory]
    [InlineData(0u, 1u)]
    [InlineData(1u, 0u)]
    [InlineData(0xffffffffu, 1u)]
    [InlineData(1u, 0x80000000u)]
    public void Independent_decoder_rejects_invalid_dimensions_without_overflow(uint width, uint height)
    {
        byte[] png = CreatePng(("IHDR", Header(width, height, 6)), ("IEND", []));

        Assert.Throws<InvalidDataException>(() => PngRgbaDecoder.Decode(png, int.MaxValue));
    }

    [Theory]
    [InlineData(2u, 1u, 1)]
    [InlineData(536870912u, 1u, int.MaxValue)]
    [InlineData(1u, 536870911u, int.MaxValue)]
    public void Independent_decoder_bounds_pixel_and_scanline_storage(uint width, uint height, int maximumPixels)
    {
        byte[] png = CreatePng(("IHDR", Header(width, height, 6)), ("IEND", []));

        Assert.Throws<NotSupportedException>(() => PngRgbaDecoder.Decode(png, maximumPixels));
    }

    [Fact]
    public void Independent_decoder_rejects_missing_or_out_of_order_chunks()
    {
        byte[] header = Header(1, 1, 6);
        byte[] data = Compress([0, 10, 20, 30, 40]);
        byte[][] invalid = [
            CreatePng(("tEXt", []), ("IHDR", header), ("IDAT", data), ("IEND", [])),
            CreatePng(("IDAT", data), ("IHDR", header), ("IEND", [])),
            CreatePng(("IHDR", header), ("IHDR", header), ("IDAT", data), ("IEND", [])),
            CreatePng(("IHDR", header), ("IEND", [])),
            CreatePng(("IHDR", header), ("IDAT", data)),
            CreatePng(("IHDR", header), ("IDAT", data[..2]), ("tEXt", []), ("IDAT", data[2..]), ("IEND", [])),
            CreatePng(("IHDR", header), ("IDAT", data), ("PLTE", [10, 20, 30]), ("IEND", [])),
            CreatePng(("IHDR", header), ("IDAT", data), ("IEND", []), ("tEXt", []))
        ];

        Assert.All(invalid, png => Assert.Throws<InvalidDataException>(() => PngRgbaDecoder.Decode(png, 1)));
    }

    [Fact]
    public void Independent_decoder_reads_production_encoder_output()
    {
        byte[] source = [10, 20, 30, 40, 50, 60, 70, 80];
        byte[] png = PngEncoder.Encode(2, 1, source, CancellationToken.None);

        RgbaImage decoded = PngRgbaDecoder.Decode(png, 2);

        Assert.Equal(source, decoded.Pixels);
    }

    [Fact]
    public void Production_encoder_canonicalizes_invisible_rgb()
    {
        byte[] png = PngEncoder.Encode(1, 1, [10, 20, 30, 0], CancellationToken.None);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, PngRgbaDecoder.Decode(png, 1).Pixels);
    }

    private static byte[] Header(uint width, uint height, byte colorType)
    {
        byte[] header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = colorType;
        return header;
    }

    private static byte[] Compress(ReadOnlySpan<byte> scanline)
    {
        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.NoCompression, true)) zlib.Write(scanline);
        return compressed.ToArray();
    }

    private static byte[] CreatePng(params (string Type, byte[] Data)[] chunks)
    {
        using MemoryStream result = new();
        result.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        foreach ((string type, byte[] data) in chunks) WriteChunk(result, System.Text.Encoding.ASCII.GetBytes(type), data);
        return result.ToArray();

        static void WriteChunk(Stream destination, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
            destination.Write(length);
            destination.Write(type);
            destination.Write(data);
            uint crc = 0xffffffff;
            foreach (byte value in type) crc = CrcStep(crc, value);
            foreach (byte value in data) crc = CrcStep(crc, value);
            BinaryPrimitives.WriteUInt32BigEndian(length, ~crc);
            destination.Write(length);
        }

        static uint CrcStep(uint crc, byte value)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
            return crc;
        }
    }
}
