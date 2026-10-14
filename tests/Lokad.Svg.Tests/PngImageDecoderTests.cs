using Lokad.Svg.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class PngImageDecoderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(255)]
    public void Decodes_gray_and_alpha_as_independent_png_channels(int alpha)
    {
        byte[] png = BuildPng(
        [
            new TestChunk("IHDR", TestIhdr(2, 1, 4)),
            new TestChunk("IDAT", ZlibCompress([0, 120, (byte)alpha, 120, (byte)alpha])),
            new TestChunk("IEND", []),
        ]);
        Assert.Equal(new SvgColor(120, 120, 120, (byte)alpha), DecodeBuilt(png));
    }

    [Fact]
    public void Grayscale_alpha_rejects_redundant_transparency_metadata()
    {
        byte[] png = BuildPng(
        [
            new TestChunk("IHDR", TestIhdr(1, 1, 4)),
            new TestChunk("tRNS", [0, 120]),
            new TestChunk("IDAT", ZlibCompress([0, 120, 128])),
            new TestChunk("IEND", []),
        ]);
        Assert.Equal(SvgErrorCode.MalformedInput, DecodeBuiltError(png));
    }

    private const string RedPixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==";
    private const string Wide16384 = "iVBORw0KGgoAAAANSUhEUgAAQAAAAAABCAYAAAAmn7ZYAAABVklEQVR4nO3SMQ0AAACDMPyb3oz0wABpq8kDBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYIABBhhggAEGGGCAAQYYYICBAzvUh3LqxPMyAAAAAElFTkSuQmCC";
    private const string RgbaWithTransparency = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAnRSTlMAAHaTzTgAAAANSURBVHicY/jPwPAfAAUAAf+JmT0dAAAAAElFTkSuQmCC";
    [Fact]
    public void Decodes_a_solid_rgba_pixel()
    {
        SvgColor color = Decode(RedPixel);

        Assert.Equal(new SvgColor(255, 0, 0, 255), color);
    }

    [Fact]
    public void Decodes_a_transparent_gray_pixel_through_transparency()
    {
        SvgColor color = Decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAAAnRSTlMAgJsrThgAAAAKSURBVHicY2gAAACCAIF3zXK2AAAAAElFTkSuQmCC");

        Assert.Equal(new SvgColor(128, 128, 128, 0), color);
    }

    [Fact]
    public void Rejects_a_two_color_pixel_row()
    {
        SvgException exception = Assert.Throws<SvgException>(() => Decode("iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAIAAAB7QOjdAAAADUlEQVR4nGP4zwAE/wEHAAH/4iOeWQAAAABJRU5ErkJggg=="));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_interlaced_data()
    {
        SvgException exception = Assert.Throws<SvgException>(() => Decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAAHncGNIAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC"));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_palette_data()
    {
        SvgException exception = Assert.Throws<SvgException>(() => Decode("iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAMAAADD/I+4AAAABlBMVEX/AAAAAP9sof2OAAAAC0lEQVR4nGNgYAQAAAQAAr96P0oAAAAASUVORK5CYII="));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Decodes_a_solid_palette_pixel()
    {
        SvgColor color = Decode("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAMAAABFaP0WAAAAA1BMVEX/AAAZ4gk3AAAAC0lEQVR4nGNgAAEAAAYAAf6MZ8gAAAAASUVORK5CYII=");

        Assert.Equal(new SvgColor(255, 0, 0, 255), color);
    }

    [Fact]
    public void Decodes_a_palette_pixel_through_palette_transparency()
    {
        SvgColor color = Decode("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAMAAABFaP0WAAAABlBMVEX/AAAA/wDSh+9xAAAADklEQVR4nGNgZGRgZAQAABIABVUZwSwAAAACdFJOU/+ACA+zagAAAABJRU5ErkJggg==");

        Assert.Equal(new SvgColor(0, 255, 0, 128), color);
    }

    [Fact]
    public void Decodes_a_transparent_palette_pixel()
    {
        SvgColor color = Decode("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAMAAABFaP0WAAAABlBMVEX/AAAA/wDSh+9xAAAADklEQVR4nGNgZGRgZAQAABIABVUZwSwAAAACdFJOU/8A5bcwSgAAAABJRU5ErkJggg==");

        Assert.Equal(new SvgColor(0, 255, 0, 0), color);
    }

    [Theory]
    [InlineData("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAMAAABFaP0WAAAABlBMVEX/AAAA/wDSh+9xAAAADklEQVR4nGNgYGRgZAAAAAwAAzi4CA8AAAAASUVORK5CYII=", SvgErrorCode.UnsupportedFeature)]
    [InlineData("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAMAAABFaP0WAAAABlBMVEX/AAAA/wDSh+9xAAAADklEQVR4nGNgZWVgZQUAAEIAFbGAWJQAAAAASUVORK5CYII=", SvgErrorCode.MalformedInput)]
    [InlineData("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAMAAABFaP0WAAAAC0lEQVR4nGNgAAEAAAYAAf6MZ8gAAAAASUVORK5CYII=", SvgErrorCode.MalformedInput)]
    [InlineData("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAMAAABFaP0WAAAAA1BMVEX/AAAZ4gk3AAAAA1BMVEX/AAAZ4gk3AAAAC0lEQVR4nGNgAAEAAAYAAf6MZ8gAAAAASUVORK5CYII=", SvgErrorCode.MalformedInput)]
    [InlineData("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAMAAABFaP0WAAAAC0lEQVR4nGNgAAEAAAYAAf6MZ8gAAAADUExURf8AABniCTcAAAAASUVORK5CYII=", SvgErrorCode.MalformedInput)]
    [InlineData("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAMAAAEyb82AAAAAA1BMVEX/AAAZ4gk3AAAAC0lEQVR4nGNgAAEAAAYAAf6MZ8gAAAAASUVORK5CYII=", SvgErrorCode.UnsupportedFeature)]
    public void Rejects_invalid_palette_data(string base64, SvgErrorCode expectedErrorCode)
    {
        SvgException exception = Assert.Throws<SvgException>(() => Decode(base64));

        Assert.Equal(expectedErrorCode, exception.ErrorCode);
    }


    [Fact]
    public void Rejects_color_managed_data()
    {
        SvgException exception = Assert.Throws<SvgException>(() => Decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAABGdBTUEAALGPC/xhBQAAAAxJREFUeJxj+M/AAAADAQEAyf6S7wAAAABJRU5ErkJggg=="));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_a_broken_integrity_check()
    {
        SvgException exception = Assert.Throws<SvgException>(() => Decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkKfgg=="));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_truncated_data()
    {
        SvgException exception = Assert.Throws<SvgException>(() => Decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQQ=="));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Categorizes_corrupt_compressed_pixels_as_malformed()
    {
        SvgException exception = Assert.Throws<SvgException>(() => Decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQAnGP4z8DwHwAFAAH/aW6S9AAAAABJRU5ErkJggg=="));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_unknown_critical_chunks()
    {
        SvgException exception = Assert.Throws<SvgException>(() => Decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAEFCQ2GQE/TiAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg=="));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Ignores_unknown_ancillary_chunks()
    {
        SvgColor color = Decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAGFiQ0RDaMl7AAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");

        Assert.Equal(new SvgColor(255, 0, 0, 255), color);
    }

    [Fact]
    public async Task Corrupt_images_fail_identically_in_all_six_api_forms()
    {
        string[] corruptImages =
        [
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQAnGP4z8DwHwAFAAH/aW6S9AAAAABJRU5ErkJggg==",
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAEFCQ2GQE/TiAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==",
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQQ==",
            System.Convert.ToBase64String(DropChunk(BuiltRgb(), "IEND")),
        ];

        foreach (string base64 in corruptImages)
        {
            byte[] svg = ImageSvg(base64);
            foreach (OutputForm form in Enum.GetValues<OutputForm>())
            {
                SvgException exception = await Assert.ThrowsAsync<SvgException>(() => ConvertPublicPng(svg, form));
                Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
            }
        }
    }

    [Fact]
    public void Recomputed_crc_matches_stored_integrity()
    {
        byte[] png = System.Convert.FromBase64String(RedPixel);
        System.Collections.Generic.List<TestChunk> chunks = ParseChunks(png);

        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks.ConvertAll(chunk => chunk.Type));
        foreach (TestChunk chunk in chunks)
        {
            Assert.Equal(chunk.StoredCrc, TestCrc32(System.Text.Encoding.ASCII.GetBytes(chunk.Type), chunk.Data));
        }
    }

    [Fact]
    public void Built_rgb_decodes_red()
    {
        Assert.Equal(new SvgColor(255, 0, 0, 255), DecodeBuilt(BuiltRgb()));
    }

    [Fact]
    public void Built_palette_decodes_red()
    {
        Assert.Equal(new SvgColor(255, 0, 0, 255), DecodeBuilt(BuiltPalette()));
    }

    [Fact]
    public void Rejects_idat_before_plte()
    {
        System.Collections.Generic.List<TestChunk> chunks = ParseChunks(BuiltPalette());
        byte[] reordered = BuildPng([chunks[0], chunks[2], chunks[1], chunks[3]]);

        Assert.Equal(SvgErrorCode.MalformedInput, DecodeBuiltError(reordered));
    }

    [Fact]
    public void Rejects_missing_iend()
    {
        Assert.Equal(SvgErrorCode.MalformedInput, DecodeBuiltError(DropChunk(BuiltRgb(), "IEND")));
    }

    [Fact]
    public void Rejects_trailing_bytes_after_iend()
    {
        byte[] png = BuiltRgb();
        byte[] trailed = new byte[png.Length + 1];
        System.Buffer.BlockCopy(png, 0, trailed, 0, png.Length);

        Assert.Equal(SvgErrorCode.MalformedInput, DecodeBuiltError(trailed));
    }

    [Fact]
    public void Rejects_second_ihdr()
    {
        System.Collections.Generic.List<TestChunk> chunks = ParseChunks(BuiltRgb());
        byte[] doubled = BuildPng([chunks[0], chunks[0], chunks[1], chunks[2]]);

        Assert.Equal(SvgErrorCode.MalformedInput, DecodeBuiltError(doubled));
    }

    [Fact]
    public void Rejects_truncated_chunk_header()
    {
        byte[] png = BuiltRgb();
        byte[] cut = new byte[png.Length - 6];
        System.Buffer.BlockCopy(png, 0, cut, 0, cut.Length);

        Assert.Equal(SvgErrorCode.MalformedInput, DecodeBuiltError(cut));
    }

    [Fact]
    public void Rejects_overlong_chunk_length()
    {
        byte[] png = BuiltRgb();
        System.Collections.Generic.List<TestChunk> chunks = ParseChunks(png);
        byte[] patched = (byte[])png.Clone();
        WriteInt32BigEndian(patched, chunks[1].Offset, 1_000_000);

        Assert.Equal(SvgErrorCode.MalformedInput, DecodeBuiltError(patched));
    }

    [Fact]
    public void Rejects_empty_pixel_stream()
    {
        byte[] png = BuildPng(
        [
            new TestChunk("IHDR", TestIhdr(1, 1, 2)),
            new TestChunk("IDAT", ZlibCompress([])),
            new TestChunk("IEND", []),
        ]);

        Assert.Equal(SvgErrorCode.MalformedInput, DecodeBuiltError(png));
    }

    [Fact]
    public void Late_transparency_still_applies()
    {
        byte[] png = BuildPng(
        [
            new TestChunk("IHDR", TestIhdr(1, 1, 0)),
            new TestChunk("IDAT", ZlibCompress([0, 128])),
            new TestChunk("tRNS", [0, 128]),
            new TestChunk("IEND", []),
        ]);

        Assert.Equal(new SvgColor(128, 128, 128, 0), DecodeBuilt(png));
    }

    [Fact]
    public void Rejects_ancillary_after_end()
    {
        System.Collections.Generic.List<TestChunk> chunks = ParseChunks(BuiltRgb());
        chunks.Add(new TestChunk("aaAa", [1, 2, 3]));

        Assert.Equal(SvgErrorCode.MalformedInput, DecodeBuiltError(BuildPng(chunks)));
    }

    private static async Task<ReadOnlyMemory<byte>> ConvertPublicPng(byte[] svg, OutputForm form)
    {
        byte[] memoryDestination = new byte[1_024];
        using MemoryStream streamDestination = new();
        try
        {
            switch (form)
            {
                case OutputForm.Owned: return Lokad.Svg.SvgConverter.ConvertToPng(svg, Lokad.Svg.SvgRenderOptions.Default, CancellationToken.None);
                case OutputForm.OwnedAsync: return await Lokad.Svg.SvgConverter.ConvertToPngAsync(svg, Lokad.Svg.SvgRenderOptions.Default, CancellationToken.None);
                case OutputForm.Memory:
                    int written = Lokad.Svg.SvgConverter.ConvertToPng(svg, memoryDestination, Lokad.Svg.SvgRenderOptions.Default, CancellationToken.None);
                    return memoryDestination.AsMemory(0, written);
                case OutputForm.MemoryAsync:
                    int writtenAsync = await Lokad.Svg.SvgConverter.ConvertToPngAsync(svg, memoryDestination, Lokad.Svg.SvgRenderOptions.Default, CancellationToken.None);
                    return memoryDestination.AsMemory(0, writtenAsync);
                case OutputForm.Stream:
                    Lokad.Svg.SvgConverter.ConvertToPng(svg, streamDestination, Lokad.Svg.SvgRenderOptions.Default, CancellationToken.None);
                    return streamDestination.ToArray();
                case OutputForm.StreamAsync:
                    await Lokad.Svg.SvgConverter.ConvertToPngAsync(svg, streamDestination, Lokad.Svg.SvgRenderOptions.Default, CancellationToken.None);
                    return streamDestination.ToArray();
                default: throw new ArgumentOutOfRangeException(nameof(form));
            }
        }
        catch (SvgException)
        {
            Assert.All(memoryDestination, value => Assert.Equal(0, value));
            Assert.Equal(0, streamDestination.Length);
            throw;
        }
    }

    private enum OutputForm { Owned, OwnedAsync, Memory, MemoryAsync, Stream, StreamAsync }

    [Fact]
    public void Denied_idat_storage_rejects_decode_without_retention()
    {
        byte[] png = System.Convert.FromBase64String(RedPixel);
        RenderBudget budget = new(5);

        SvgException exception = Assert.Throws<SvgException>(() => Decode(png, budget, new ParserWorkCounter(10_000), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Denied_pixel_storage_rejects_decode_without_retention()
    {
        byte[] png = System.Convert.FromBase64String(RedPixel);
        RenderBudget budget = new(20);

        SvgException exception = Assert.Throws<SvgException>(() => Decode(png, budget, new ParserWorkCounter(10_000), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Exhausted_work_rejects_decode()
    {
        byte[] png = System.Convert.FromBase64String(RedPixel);

        SvgException exception = Assert.Throws<SvgException>(() => Decode(png, new RenderBudget(100_000), new ParserWorkCounter(0), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Cancelled_decode_throws_operation_cancelled()
    {
        byte[] png = System.Convert.FromBase64String(RedPixel);

        Assert.Throws<OperationCanceledException>(() => Decode(png, new RenderBudget(100_000), new ParserWorkCounter(10_000), new CancellationToken(true)));
    }

    [Fact]
    public void Wider_rows_consume_more_decoder_work()
    {
        byte[] small = System.Convert.FromBase64String(RedPixel);
        byte[] large = System.Convert.FromBase64String(Wide16384);
        ParserWorkCounter smallWork = new(1_000_000);
        ParserWorkCounter largeWork = new(1_000_000);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Decode(small, new RenderBudget(100_000), smallWork, CancellationToken.None));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Decode(large, new RenderBudget(1_000_000), largeWork, CancellationToken.None));

        Assert.True(largeWork.Used > smallWork.Used);
    }

    [Fact]
    public void Malformed_transparency_stays_malformed_with_tight_budget()
    {
        byte[] png = System.Convert.FromBase64String(RgbaWithTransparency);
        RenderBudget budget = new(1);

        SvgException exception = Assert.Throws<SvgException>(() => Decode(png, budget, new ParserWorkCounter(10_000), CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static SvgColor Decode(byte[] png, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        return PngImageDecoder.DecodeSolidColor(png, SvgRenderLimits.Default, budget, workCounter, cancellationToken).Color;
    }
    private static byte[] ImageSvg(string base64)
    {
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'><image href='data:image/png;base64," + base64 + "' x='0' y='0' width='1' height='1'/></svg>");
    }

    private static SvgColor DecodeBuilt(byte[] png)
    {
        return Decode(png, new RenderBudget(100_000), new ParserWorkCounter(10_000), CancellationToken.None);
    }

    private static SvgErrorCode DecodeBuiltError(byte[] png)
    {
        return Assert.Throws<SvgException>(() => DecodeBuilt(png)).ErrorCode;
    }

    private static byte[] BuiltRgb()
    {
        return BuildPng(
        [
            new TestChunk("IHDR", TestIhdr(1, 1, 2)),
            new TestChunk("IDAT", ZlibCompress([0, 255, 0, 0])),
            new TestChunk("IEND", []),
        ]);
    }

    private static byte[] BuiltPalette()
    {
        return BuildPng(
        [
            new TestChunk("IHDR", TestIhdr(2, 1, 3)),
            new TestChunk("PLTE", [255, 0, 0, 0, 255, 0]),
            new TestChunk("IDAT", ZlibCompress([0, 0, 0])),
            new TestChunk("IEND", []),
        ]);
    }

    private static byte[] DropChunk(byte[] png, string type)
    {
        System.Collections.Generic.List<TestChunk> chunks = ParseChunks(png);
        chunks.RemoveAll(chunk => chunk.Type == type);
        return BuildPng(chunks);
    }

    private static byte[] BuildPng(System.Collections.Generic.List<TestChunk> chunks)
    {
        using MemoryStream stream = new();
        stream.Write([137, 80, 78, 71, 13, 10, 26, 10], 0, 8);
        foreach (TestChunk chunk in chunks)
        {
            byte[] type = System.Text.Encoding.ASCII.GetBytes(chunk.Type);
            byte[] header = new byte[8];
            WriteInt32BigEndian(header, 0, chunk.Data.Length);
            System.Buffer.BlockCopy(type, 0, header, 4, 4);
            stream.Write(header, 0, header.Length);
            stream.Write(chunk.Data, 0, chunk.Data.Length);
            byte[] crc = new byte[4];
            WriteUInt32BigEndian(crc, 0, TestCrc32(type, chunk.Data));
            stream.Write(crc, 0, crc.Length);
        }

        return stream.ToArray();
    }

    private static System.Collections.Generic.List<TestChunk> ParseChunks(byte[] png)
    {
        System.Collections.Generic.List<TestChunk> chunks = new();
        int position = 8;
        while (position + 12 <= png.Length)
        {
            int length = (png[position] << 24) | (png[position + 1] << 16) | (png[position + 2] << 8) | png[position + 3];
            if (length < 0 || position + 12 + length > png.Length) throw new InvalidOperationException("Test PNG skeleton is malformed.");
            string type = System.Text.Encoding.ASCII.GetString(png, position + 4, 4);
            byte[] data = new byte[length];
            System.Buffer.BlockCopy(png, position + 8, data, 0, length);
            uint storedCrc = ((uint)png[position + 8 + length] << 24) | ((uint)png[position + 8 + length + 1] << 16) | ((uint)png[position + 8 + length + 2] << 8) | png[position + 8 + length + 3];
            chunks.Add(new TestChunk(type, data, position, storedCrc));
            position += 12 + length;
        }

        return chunks;
    }

    private static byte[] TestIhdr(int width, int height, byte colorType)
    {
        byte[] ihdr = new byte[13];
        WriteInt32BigEndian(ihdr, 0, width);
        WriteInt32BigEndian(ihdr, 4, height);
        ihdr[8] = 8;
        ihdr[9] = colorType;
        return ihdr;
    }

    private static byte[] ZlibCompress(byte[] raw)
    {
        using MemoryStream output = new();
        using (System.IO.Compression.ZLibStream zlib = new(output, System.IO.Compression.CompressionLevel.Optimal))
        {
            zlib.Write(raw, 0, raw.Length);
        }

        return output.ToArray();
    }

    private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static readonly uint[] TestCrcTable = BuildTestCrcTable();

    private static uint[] BuildTestCrcTable()
    {
        uint[] table = new uint[256];
        for (uint index = 0; index < 256; index++)
        {
            uint entry = index;
            for (int bit = 0; bit < 8; bit++) entry = (entry & 1) == 1 ? 0xEDB88320u ^ (entry >> 1) : entry >> 1;
            table[index] = entry;
        }

        return table;
    }

    private static uint TestCrc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte value in type) crc = TestCrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        foreach (byte value in data) crc = TestCrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private sealed record TestChunk(string Type, byte[] Data, int Offset = 0, uint StoredCrc = 0);    private static SvgColor Decode(string base64)
    {
        byte[] png = System.Convert.FromBase64String(base64);
        return PngImageDecoder.DecodeSolidColor(png, SvgRenderLimits.Default, new RenderBudget(100_000), new ParserWorkCounter(10_000), CancellationToken.None).Color;
    }
}
