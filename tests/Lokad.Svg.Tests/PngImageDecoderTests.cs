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

    private static SvgColor Decode(string base64)
    {
        byte[] png = Convert.FromBase64String(base64);
        return PngImageDecoder.DecodeSolidColor(png, SvgRenderLimits.Default, new RenderBudget(100_000), new ParserWorkCounter(10_000), CancellationToken.None);
    }
}