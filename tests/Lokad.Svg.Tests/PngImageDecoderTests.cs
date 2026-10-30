using Lokad.Svg.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class PngImageDecoderTests
{
    [Trait("Layer", "Contract")]
    [Theory]
    [InlineData(64)]
    [InlineData(256)]
    public void Fragmented_idat_capacity_and_growth_are_accounted_before_allocation(int side)
    {
        int rowBytes = side * 4;
        byte[] pixels = new byte[(rowBytes + 1) * side];
        byte[] compressed;
        using (MemoryStream bytes = new())
        {
            using (var zlib = new System.IO.Compression.ZLibStream(bytes, System.IO.Compression.CompressionLevel.NoCompression, true)) zlib.Write(pixels);
            compressed = bytes.ToArray();
        }
        byte[] single = BuildPng([new("IHDR", TestIhdr(side, side, 6)), new("IDAT", compressed), new("IEND", [])]);
        byte[] split = BuildPng([new("IHDR", TestIhdr(side, side, 6)), new("IDAT", compressed[..^1]), new("IDAT", compressed[^1..]), new("IEND", [])]);
        byte[] unchanged = split.ToArray();
        long limit = 2L * compressed.Length + 2 * rowBytes + 64;
        RenderBudget tight = new RenderBudget(new HostMemoryBudget(limit));
        Assert.Equal(SvgColor.Transparent, Decode(single, tight, CancellationToken.None));
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        SvgException denied = Assert.Throws<SvgException>(() => Decode(split, tight, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        Assert.Equal(1, tight.Snapshot().DeniedReservationCount);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);

        RenderBudget roomy = new RenderBudget(new HostMemoryBudget(8L * compressed.Length));
        Assert.Equal(SvgColor.Transparent, Decode(split, roomy, CancellationToken.None));
        // The retained doubled compressed buffer coexists with unfiltered pixels.
        Assert.True(roomy.Snapshot().PeakBytes >= 2L * (compressed.Length - 1) + pixels.Length + 2 * rowBytes);
        Assert.Equal(0, roomy.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, split);
    }

    public static IEnumerable<object[]> InvalidChunkStructures()
    {
        List<TestChunk> rgb = ParseChunks(BuiltRgb());
        foreach (string name in new[] { "1aAa", "a1Aa", "aa1a", "aaA1", "aaaa" })
            yield return [name, BuildPng([rgb[0], new(name, []), rgb[1], rgb[2]])];
        yield return ["ancillary-before-header", BuildPng([new("aaAa", []), .. rgb])];
        yield return ["separated-image-data", BuildPng([rgb[0], rgb[1], new("aaAa", []), new("IDAT", []), rgb[2]])];
        byte[] compressed = rgb[1].Data;
        yield return ["separated-image-payload", BuildPng([rgb[0], new("IDAT", compressed[..3]), new("aaAa", []), new("IDAT", compressed[3..]), rgb[2]])];
        yield return ["late-transparency", BuildPng([rgb[0], rgb[1], new("tRNS", [0, 255, 0, 0, 0, 0]), rgb[2]])];
        yield return ["palette-after-transparency", BuildPng([rgb[0], new("tRNS", [0, 255, 0, 0, 0, 0]), new("PLTE", [255, 0, 0]), rgb[1], rgb[2]])];
        List<TestChunk> indexed = ParseChunks(BuiltPalette());
        yield return ["indexed-transparency-before-palette", BuildPng([indexed[0], new("tRNS", [128]), indexed[1], indexed[2], indexed[3]])];
        yield return ["transparency-exceeds-palette", BuildPng([indexed[0], indexed[1], new("tRNS", [128, 128, 128]), indexed[2], indexed[3]])];
        foreach (byte colorType in new byte[] { 0, 4 })
            yield return ["grayscale-palette-" + colorType, BuildPng([new("IHDR", TestIhdr(1, 1, colorType)), new("PLTE", [255, 0, 0]), rgb[1], rgb[2]])];
        yield return ["missing-image-data", BuildPng([rgb[0], rgb[2]])];
    }

    [Trait("Layer", "Mechanism")]
    [Theory]
    [MemberData(nameof(InvalidChunkStructures))]
    public void Invalid_chunk_structures_release_decoder_storage(string scenario, byte[] png)
    {
        Assert.NotEmpty(scenario);
        byte[] unchanged = png.ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        SvgException error = Assert.Throws<SvgException>(() => Decode(png, budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.MalformedInput, error.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, png);
    }

    [Trait("Layer", "Contract")]
    [Theory]
    [MemberData(nameof(InvalidChunkStructures))]
    public async Task Invalid_chunk_structures_fail_before_public_output(string scenario, byte[] png)
    {
        Assert.NotEmpty(scenario);
        byte[] svg = ImageSvg(Convert.ToBase64String(png));
        foreach (OutputForm form in Enum.GetValues<OutputForm>())
            Assert.Equal(SvgErrorCode.MalformedInput, (await Assert.ThrowsAsync<SvgException>(() => ConvertPublicPng(svg, form))).ErrorCode);
    }

    [Trait("Layer", "Contract")]
    [Fact]
    public async Task Adjacent_image_chunks_and_valid_ancillary_placement_preserve_public_pixels()
    {
        List<TestChunk> original = ParseChunks(BuiltRgb());
        byte[] compressed = original[1].Data;
        byte[] split = BuildPng([original[0], new("aaAa", []), new("IDAT", []), new("IDAT", compressed[..3]),
            new("IDAT", []), new("IDAT", compressed[3..]), new("IDAT", []), new("aaAA", []), original[2]]);
        Assert.Equal(new SvgColor(255, 0, 0, 255), DecodeBuilt(split));
        byte[] expected = (await ConvertPublicPng(ImageSvg(Convert.ToBase64String(BuiltRgb())), OutputForm.Owned)).ToArray();
        foreach (OutputForm form in Enum.GetValues<OutputForm>())
            Assert.Equal(expected, (await ConvertPublicPng(ImageSvg(Convert.ToBase64String(split)), form)).ToArray());
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Palette_before_transparency_retains_truecolor_and_indexed_alpha()
    {
        List<TestChunk> rgb = ParseChunks(BuiltRgb());
        byte[] truecolor = BuildPng([rgb[0], new("PLTE", [255, 0, 0]), new("tRNS", [0, 255, 0, 0, 0, 0]), rgb[1], rgb[2]]);
        Assert.Equal(new SvgColor(255, 0, 0, 0), DecodeBuilt(truecolor));
        List<TestChunk> indexed = ParseChunks(BuiltPalette());
        indexed.Insert(2, new("tRNS", [128]));
        Assert.Equal(new SvgColor(255, 0, 0, 128), DecodeBuilt(BuildPng(indexed)));
    }

    [Trait("Layer", "Mechanism")]
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void Full_significance_metadata_preserves_eight_bit_samples(int colorType)
    {
        byte[] row = colorType switch
        {
            0 => [0, 120], 2 => [0, 255, 0, 0], 3 => [0, 0],
            4 => [0, 120, 128], 6 => [0, 255, 0, 0, 128],
            _ => throw new ArgumentOutOfRangeException(nameof(colorType))
        };
        byte[] significance = colorType switch
        {
            0 => [8], 2 or 3 => [8, 8, 8], 4 => [8, 8], 6 => [8, 8, 8, 8],
            _ => throw new ArgumentOutOfRangeException(nameof(colorType))
        };
        List<TestChunk> chunks = [new("IHDR", TestIhdr(1, 1, (byte)colorType)), new("sBIT", significance)];
        if (colorType == 3) chunks.Add(new("PLTE", [255, 0, 0]));
        chunks.Add(new("IDAT", ZlibCompress(row))); chunks.Add(new("IEND", []));
        SvgColor expected = colorType switch
        {
            0 => new(120, 120, 120, 255), 2 or 3 => new(255, 0, 0, 255),
            4 => new(120, 120, 120, 128), 6 => new(255, 0, 0, 128),
            _ => throw new ArgumentOutOfRangeException(nameof(colorType))
        };
        Assert.Equal(expected, DecodeBuilt(BuildPng(chunks)));
    }

    [Trait("Layer", "Mechanism")]
    [Theory]
    [InlineData("", SvgErrorCode.MalformedInput)]
    [InlineData("080808", SvgErrorCode.MalformedInput)]
    [InlineData("00080808", SvgErrorCode.MalformedInput)]
    [InlineData("09080808", SvgErrorCode.MalformedInput)]
    [InlineData("07080808", SvgErrorCode.UnsupportedFeature)]
    public void Significant_bit_payloads_preserve_explicit_malformed_and_format_boundaries(string payload, SvgErrorCode expected)
    {
        List<TestChunk> chunks = ParseChunks(Convert.FromBase64String(RedPixel));
        chunks.Insert(1, new("sBIT", Convert.FromHexString(payload)));
        Assert.Equal(expected, DecodeBuiltError(BuildPng(chunks)));
    }

    [Trait("Layer", "Mechanism")]
    [Theory]
    [InlineData("before-header")]
    [InlineData("duplicate")]
    [InlineData("after-palette")]
    [InlineData("after-image-data")]
    public void Significant_bit_chunks_require_unique_pre_palette_pre_image_placement(string placement)
    {
        List<TestChunk> chunks = ParseChunks(BuiltPalette());
        TestChunk significance = new("sBIT", [8, 8, 8]);
        switch (placement)
        {
            case "before-header": chunks.Insert(0, significance); break;
            case "duplicate": chunks.Insert(1, significance); chunks.Insert(2, significance); break;
            case "after-palette": chunks.Insert(2, significance); break;
            case "after-image-data": chunks.Insert(3, significance); break;
            default: throw new ArgumentOutOfRangeException(nameof(placement));
        }
        Assert.Equal(SvgErrorCode.MalformedInput, DecodeBuiltError(BuildPng(chunks)));
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Orientation_metadata_remains_an_explicit_format_boundary()
    {
        List<TestChunk> chunks = ParseChunks(Convert.FromBase64String(RedPixel));
        chunks.Insert(1, new("eXIf", [0, 0, 0, 0]));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, DecodeBuiltError(BuildPng(chunks)));
    }

    [Trait("Layer", "Contract")]
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void Varied_png_formats_retain_independent_premultiplied_pixels(int colorType)
    {
        byte[] row = colorType switch
        {
            0 => [0, 120, 240],
            2 => [0, 255, 0, 0, 0, 0, 255],
            3 => [0, 0, 1],
            4 => [0, 120, 128, 240, 64],
            6 => [0, 255, 0, 0, 128, 0, 0, 255, 64],
            _ => throw new ArgumentOutOfRangeException(nameof(colorType)),
        };
        byte[] expected = colorType switch
        {
            0 => [120, 120, 120, 255, 240, 240, 240, 255],
            2 => [255, 0, 0, 255, 0, 0, 255, 255],
            3 => [128, 0, 0, 128, 0, 0, 0, 0],
            4 => [60, 60, 60, 128, 60, 60, 60, 64],
            6 => [128, 0, 0, 128, 0, 0, 64, 64],
            _ => throw new ArgumentOutOfRangeException(nameof(colorType)),
        };
        List<TestChunk> chunks = [new("IHDR", TestIhdr(2, 1, (byte)colorType))];
        if (colorType == 3) { chunks.Add(new("PLTE", [255, 0, 0, 0, 255, 0])); chunks.Add(new("tRNS", [128, 0])); }
        chunks.Add(new("IDAT", ZlibCompress(row))); chunks.Add(new("IEND", []));
        byte[] png = BuildPng(chunks), unchanged = png.ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        DecodedPngImage image = PngImageDecoder.Decode(png, SvgRenderLimits.Default, budget, CancellationToken.None);
        using (DecodedPngRaster raster = Assert.IsType<DecodedPngRaster>(image.Raster))
        {
            Assert.Equal(2, image.Width); Assert.Equal(1, image.Height);
            Assert.Equal(expected, raster.Pixels);
            Assert.True(budget.Snapshot().CurrentBytes >= expected.Length);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, png);
    }

    [Trait("Layer", "Contract")]
    [Fact]
    public void Uniform_png_does_not_retain_a_pixel_surface()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        DecodedPngImage image = PngImageDecoder.Decode(Convert.FromBase64String(RedPixel), SvgRenderLimits.Default, budget, CancellationToken.None);
        Assert.Null(image.Raster);
        Assert.Equal(new SvgColor(255, 0, 0, 255), image.Color);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Trait("Layer", "Contract")]
    [Fact]
    public void A_later_bad_row_releases_the_already_decoded_raster()
    {
        byte[] png = BuildPng([
            new("IHDR", TestIhdr(2, 2, 6)),
            new("IDAT", ZlibCompress([0, 255, 0, 0, 128, 0, 0, 255, 64, 5, 0, 0, 0, 0, 0, 0, 0, 0])),
            new("IEND", []),
        ]);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() =>
            PngImageDecoder.Decode(png, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.True(budget.Snapshot().PeakBytes >= 4 * 2 * 2);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Trait("Layer", "Contract")]
    [Fact]
    public void Repeated_decoding_releases_pixels_and_preserves_the_result()
    {
        byte[] png = BuildPng([
            new("IHDR", TestIhdr(2, 2, 6)),
            new("IDAT", ZlibCompress([0, 255, 0, 0, 128, 0, 0, 255, 64, 0, 0, 255, 0, 255, 0, 0, 0, 0])),
            new("IEND", []),
        ]);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        DecodedPngImage first = PngImageDecoder.Decode(png, SvgRenderLimits.Default, budget, CancellationToken.None);
        byte[] expected;
        using (DecodedPngRaster raster = Assert.IsType<DecodedPngRaster>(first.Raster)) expected = raster.Pixels.ToArray();
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        DecodedPngImage retry = PngImageDecoder.Decode(png, SvgRenderLimits.Default, budget, CancellationToken.None);
        using (DecodedPngRaster raster = Assert.IsType<DecodedPngRaster>(retry.Raster)) Assert.Equal(expected, raster.Pixels);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Trait("Layer", "Mechanism")]
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

    [Trait("Layer", "Mechanism")]
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
    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Decodes_a_solid_rgba_pixel()
    {
        SvgColor color = Decode(RedPixel);

        Assert.Equal(new SvgColor(255, 0, 0, 255), color);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Decodes_a_transparent_gray_pixel_through_transparency()
    {
        SvgColor color = Decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAAAnRSTlMAgJsrThgAAAAKSURBVHicY2gAAACCAIF3zXK2AAAAAElFTkSuQmCC");

        Assert.Equal(new SvgColor(128, 128, 128, 0), color);
    }

    [Trait("Layer", "Mechanism")]
    [Theory]
    [InlineData("iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAIAAAB7QOjdAAAADUlEQVR4nGP4zwAE/wEHAAH/4iOeWQAAAABJRU5ErkJggg==")]
    [InlineData("iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAMAAADD/I+4AAAABlBMVEX/AAAAAP9sof2OAAAAC0lEQVR4nGNgYAQAAAQAAr96P0oAAAAASUVORK5CYII=")]
    public void Decodes_two_color_rgb_and_palette_rows(string base64)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        DecodedPngImage image = PngImageDecoder.Decode(Convert.FromBase64String(base64), SvgRenderLimits.Default, budget, CancellationToken.None);
        try
        {
            Assert.Equal(2, image.Width);
            Assert.Equal(1, image.Height);
            Assert.NotNull(image.Raster);
            Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 }, image.Raster.Pixels);
        }
        finally { image.Raster?.Dispose(); }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Rejects_interlaced_data()
    {
        SvgException exception = Assert.Throws<SvgException>(() => Decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAAHncGNIAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC"));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Decodes_a_solid_palette_pixel()
    {
        SvgColor color = Decode("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAMAAABFaP0WAAAAA1BMVEX/AAAZ4gk3AAAAC0lEQVR4nGNgAAEAAAYAAf6MZ8gAAAAASUVORK5CYII=");

        Assert.Equal(new SvgColor(255, 0, 0, 255), color);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Decodes_a_palette_pixel_through_palette_transparency()
    {
        SvgColor color = DecodeBuilt(BuildPng([
            new("IHDR", TestIhdr(2, 2, 3)), new("PLTE", [255, 0, 0, 0, 255, 0]),
            new("tRNS", [255, 128]), new("IDAT", ZlibCompress([0, 1, 1, 0, 1, 1])), new("IEND", [])]));

        Assert.Equal(new SvgColor(0, 255, 0, 128), color);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Decodes_a_transparent_palette_pixel()
    {
        SvgColor color = DecodeBuilt(BuildPng([
            new("IHDR", TestIhdr(2, 2, 3)), new("PLTE", [255, 0, 0, 0, 255, 0]),
            new("tRNS", [255, 0]), new("IDAT", ZlibCompress([0, 1, 1, 0, 1, 1])), new("IEND", [])]));

        Assert.Equal(new SvgColor(0, 255, 0, 0), color);
    }

    [Trait("Layer", "Mechanism")]
    [Theory]
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


    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Rejects_color_managed_data()
    {
        SvgException exception = Assert.Throws<SvgException>(() => Decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAABGdBTUEAALGPC/xhBQAAAAxJREFUeJxj+M/AAAADAQEAyf6S7wAAAABJRU5ErkJggg=="));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Rejects_a_broken_integrity_check()
    {
        SvgException exception = Assert.Throws<SvgException>(() => Decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkKfgg=="));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Rejects_truncated_data()
    {
        SvgException exception = Assert.Throws<SvgException>(() => Decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQQ=="));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Categorizes_corrupt_compressed_pixels_as_malformed()
    {
        SvgException exception = Assert.Throws<SvgException>(() => Decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQAnGP4z8DwHwAFAAH/aW6S9AAAAABJRU5ErkJggg=="));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Rejects_unknown_critical_chunks()
    {
        SvgException exception = Assert.Throws<SvgException>(() => Decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAEFCQ2GQE/TiAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg=="));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Ignores_unknown_ancillary_chunks()
    {
        SvgColor color = Decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAGFiQ0RDaMl7AAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");

        Assert.Equal(new SvgColor(255, 0, 0, 255), color);
    }

    [Trait("Layer", "Contract")]
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

    [Trait("Layer", "Mechanism")]
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

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Built_rgb_decodes_red()
    {
        Assert.Equal(new SvgColor(255, 0, 0, 255), DecodeBuilt(BuiltRgb()));
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Built_palette_decodes_red()
    {
        Assert.Equal(new SvgColor(255, 0, 0, 255), DecodeBuilt(BuiltPalette()));
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Rejects_idat_before_plte()
    {
        System.Collections.Generic.List<TestChunk> chunks = ParseChunks(BuiltPalette());
        byte[] reordered = BuildPng([chunks[0], chunks[2], chunks[1], chunks[3]]);

        Assert.Equal(SvgErrorCode.MalformedInput, DecodeBuiltError(reordered));
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Rejects_missing_iend()
    {
        Assert.Equal(SvgErrorCode.MalformedInput, DecodeBuiltError(DropChunk(BuiltRgb(), "IEND")));
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Rejects_trailing_bytes_after_iend()
    {
        byte[] png = BuiltRgb();
        byte[] trailed = new byte[png.Length + 1];
        System.Buffer.BlockCopy(png, 0, trailed, 0, png.Length);

        Assert.Equal(SvgErrorCode.MalformedInput, DecodeBuiltError(trailed));
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Rejects_second_ihdr()
    {
        System.Collections.Generic.List<TestChunk> chunks = ParseChunks(BuiltRgb());
        byte[] doubled = BuildPng([chunks[0], chunks[0], chunks[1], chunks[2]]);

        Assert.Equal(SvgErrorCode.MalformedInput, DecodeBuiltError(doubled));
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Rejects_truncated_chunk_header()
    {
        byte[] png = BuiltRgb();
        byte[] cut = new byte[png.Length - 6];
        System.Buffer.BlockCopy(png, 0, cut, 0, cut.Length);

        Assert.Equal(SvgErrorCode.MalformedInput, DecodeBuiltError(cut));
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Rejects_overlong_chunk_length()
    {
        byte[] png = BuiltRgb();
        System.Collections.Generic.List<TestChunk> chunks = ParseChunks(png);
        byte[] patched = (byte[])png.Clone();
        WriteInt32BigEndian(patched, chunks[1].Offset, 1_000_000);

        Assert.Equal(SvgErrorCode.MalformedInput, DecodeBuiltError(patched));
    }

    [Trait("Layer", "Mechanism")]
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

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Rejects_late_transparency()
    {
        byte[] png = BuildPng(
        [
            new TestChunk("IHDR", TestIhdr(1, 1, 0)),
            new TestChunk("IDAT", ZlibCompress([0, 128])),
            new TestChunk("tRNS", [0, 128]),
            new TestChunk("IEND", []),
        ]);

        Assert.Equal(SvgErrorCode.MalformedInput, DecodeBuiltError(png));
    }

    [Trait("Layer", "Mechanism")]
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
        Array.Fill(memoryDestination, (byte)0xA5);
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
            Assert.All(memoryDestination, value => Assert.Equal((byte)0xA5, value));
            Assert.Equal(0, streamDestination.Length);
            throw;
        }
    }

    private enum OutputForm { Owned, OwnedAsync, Memory, MemoryAsync, Stream, StreamAsync }

    [Trait("Layer", "Contract")]
    [Fact]
    public void Denied_idat_storage_rejects_decode_without_retention()
    {
        byte[] png = System.Convert.FromBase64String(RedPixel);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(5));

        SvgException exception = Assert.Throws<SvgException>(() => Decode(png, budget, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Trait("Layer", "Contract")]
    [Fact]
    public void Denied_pixel_storage_rejects_decode_without_retention()
    {
        byte[] png = System.Convert.FromBase64String(RedPixel);
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(100_000));
        _ = Decode(png, measured, CancellationToken.None);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));

        SvgException exception = Assert.Throws<SvgException>(() => Decode(png, budget, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Trait("Layer", "Contract")]
    [Fact]
    public void Cancelled_decode_throws_operation_cancelled()
    {
        byte[] png = System.Convert.FromBase64String(RedPixel);

        Assert.Throws<OperationCanceledException>(() => Decode(png, new RenderBudget(new HostMemoryBudget(100_000)), new CancellationToken(true)));
    }

    [Trait("Layer", "Mechanism")]
    [Fact]
    public void Narrow_and_wide_uniform_rows_decode_the_same_color()
    {
        byte[] small = System.Convert.FromBase64String(RedPixel);
        byte[] large = System.Convert.FromBase64String(Wide16384);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Decode(small, new RenderBudget(new HostMemoryBudget(100_000)), CancellationToken.None));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Decode(large, new RenderBudget(new HostMemoryBudget(1_000_000)), CancellationToken.None));
    }

    [Trait("Layer", "Contract")]
    [Fact]
    public void Malformed_transparency_stays_malformed_with_tight_budget()
    {
        byte[] png = System.Convert.FromBase64String(RgbaWithTransparency);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1));

        SvgException exception = Assert.Throws<SvgException>(() => Decode(png, budget, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static SvgColor Decode(byte[] png, RenderBudget budget, CancellationToken cancellationToken)
    {
        DecodedPngImage image = PngImageDecoder.Decode(png, SvgRenderLimits.Default, budget, cancellationToken);
        try
        {
            Assert.Null(image.Raster);
            return image.Color;
        }
        finally { image.Raster?.Dispose(); }
    }
    private static byte[] ImageSvg(string base64)
    {
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'><image href='data:image/png;base64," + base64 + "' x='0' y='0' width='1' height='1'/></svg>");
    }

    private static SvgColor DecodeBuilt(byte[] png)
    {
        return Decode(png, new RenderBudget(new HostMemoryBudget(100_000)), CancellationToken.None);
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

    private sealed record TestChunk(string Type, byte[] Data, int Offset, uint StoredCrc)
    {
        internal TestChunk(string type, byte[] data) : this(type, data, 0, 0) { }
    }

    private static SvgColor Decode(string base64)
    {
        byte[] png = System.Convert.FromBase64String(base64);
        return Decode(png, new RenderBudget(new HostMemoryBudget(100_000)), CancellationToken.None);
    }
}
