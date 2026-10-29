using Lokad.Svg.VisualDiff;
using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Contract")]
public sealed class SvgBackgroundColorTests
{
    [Theory]
    [InlineData(10, 20, 30, 0, 0, 0, 0)]
    [InlineData(64, 128, 192, 1, 0, 255, 255)]
    [InlineData(12, 24, 36, 64, 12, 24, 36)]
    [InlineData(10, 20, 30, 128, 10, 20, 30)]
    [InlineData(10, 20, 30, 255, 10, 20, 30)]
    public void Empty_canvas_background_observes_the_premultiplied_surface_and_straight_capture_contract(
        byte red, byte green, byte blue, byte alpha, byte expectedRed, byte expectedGreen, byte expectedBlue)
    {
        RgbaImage image = Render("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>", new(red, green, blue, alpha));
        // Alpha one deliberately quantizes RGB to zero/255 when crossing an
        // RGBA8 premultiplied surface. Transparent PNG RGB is canonical zero.
        Assert.Equal(new[] { expectedRed, expectedGreen, expectedBlue, alpha }, image.Pixels);
    }

    [Fact]
    public void Colored_zero_alpha_background_does_not_change_partially_transparent_source_pixels()
    {
        const string input = "<svg xmlns='http://www.w3.org/2000/svg' width='2' height='1'><rect width='1' height='1' fill='red' fill-opacity='.5'/></svg>";
        RgbaImage transparent = Render(input, SvgColor.Transparent);
        RgbaImage colored = Render(input, new(10, 20, 30, 0));
        Assert.Equal(transparent.Pixels, colored.Pixels);
    }

    [Fact]
    public void Partial_background_composes_like_the_same_rgba_underlay()
    {
        const string source = "<rect width='1' height='1' fill='red' fill-opacity='.5'/>";
        const string start = "<svg xmlns='http://www.w3.org/2000/svg' width='2' height='1'>";
        RgbaImage background = Render(start + source + "</svg>", new(10, 20, 30, 128));
        RgbaImage underlay = Render(start + "<rect width='2' height='1' fill='#0a141e' fill-opacity='.5019607843137255'/>" + source + "</svg>", SvgColor.Transparent);
        Assert.Equal(underlay.Pixels, background.Pixels);
    }

    private static RgbaImage Render(string svg, SvgColor background)
    {
        SvgRenderOptions options = new(null, 1, background, SvgRenderLimits.Default);
        byte[] png = SvgConverter.ConvertToPng(TextEncoding.UTF8.GetBytes(svg), options, CancellationToken.None).ToArray();
        return PngRgbaDecoder.Decode(png, 2);
    }
}
