using Lokad.Svg.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class PngImageDecoderTests
{
    private const string RedPixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==";

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
    public async Task Corrupt_compressed_pixels_fail_identically_in_all_six_api_forms()
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQAnGP4z8DwHwAFAAH/aW6S9AAAAABJRU5ErkJggg==' x='0' y='0' width='1' height='1'/></svg>");

        foreach (OutputForm form in Enum.GetValues<OutputForm>())
        {
            SvgException exception = await Assert.ThrowsAsync<SvgException>(() => ConvertPublicPng(svg, form));
            Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
        }
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

    private static SvgColor Decode(string base64)
    {
        byte[] png = System.Convert.FromBase64String(base64);
        return PngImageDecoder.DecodeSolidColor(png, SvgRenderLimits.Default, new RenderBudget(100_000), new ParserWorkCounter(10_000), CancellationToken.None).Color;
    }
}