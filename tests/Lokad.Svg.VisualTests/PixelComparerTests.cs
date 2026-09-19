using Lokad.Svg.VisualDiff;
using System.Buffers.Binary;
using System.IO.Compression;

namespace Lokad.Svg.VisualTests;

public sealed class PixelComparerTests
{
    private static readonly byte[] OnePixelRgbaPng = CreateOnePixelRgbaPng();

    [Fact]
    public void One_channel_difference_is_an_exact_failure()
    {
        RgbaImage reference = new(1, 1, [10, 20, 30, 40]);
        RgbaImage candidate = new(1, 1, [10, 21, 30, 40]);

        PixelComparison result = PixelComparer.Compare(reference, candidate);

        Assert.True(result.DimensionsMatch);
        Assert.Equal(1, result.ChangedPixelCount);
        Assert.Equal(1, result.MaximumChannelError);
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

    private static byte[] CreateOnePixelRgbaPng()
    {
        using MemoryStream result = new();
        result.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        WriteChunk(result, "IHDR"u8, [0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0]);
        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.NoCompression, true)) zlib.Write([0, 10, 20, 30, 40]);
        WriteChunk(result, "IDAT"u8, compressed.ToArray());
        WriteChunk(result, "IEND"u8, []);
        return result.ToArray();
    }

    private static void WriteChunk(Stream destination, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
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

    private static uint CrcStep(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        return crc;
    }
}
