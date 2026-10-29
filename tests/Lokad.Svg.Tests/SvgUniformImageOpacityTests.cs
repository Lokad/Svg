using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgUniformImageOpacityTests
{
    public static IEnumerable<object[]> AlphaCases()
    {
        foreach (byte alpha in new byte[] { 0, 1, 63, 64, 127, 128, 254, 255 })
        foreach (string opacity in new[] { "0", ".5", "1" })
        foreach (string consumer in new[] { "direct", "group", "use", "symbol" }) yield return [alpha, opacity, consumer];
    }

    [Theory]
    [MemberData(nameof(AlphaCases))]
    public void Uniform_images_keep_bitmap_alpha_rounding_through_opacity_owners(byte alpha, string opacity, string consumer)
    {
        string image = Image(new(255, 0, 0, alpha));
        byte[] input = TextEncoding.UTF8.GetBytes(Document(image, opacity, consumer));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
        // Actual native integer placements use bitmap paint's byte scale,
        // including alpha127 at opacity .5 -> 63, independently of container.
        byte expected = opacity == "0" ? (byte)0 : opacity == "1" ? alpha : (byte)(alpha * 129 >> 8);
        for (int y = 0; y < 32; y++)
        for (int x = 0; x < 32; x++)
            Assert.Equal(x >= 4 && x < 28 && y >= 8 && y < 24 ? expected : (byte)0, surface.Pixels[(y * 32 + x) * 4 + 3]);
    }

    [Fact]
    public void Uniform_image_color_uses_bitmap_opacity_before_unpremultiplication()
    {
        byte[] input = TextEncoding.UTF8.GetBytes(Document(Image(new(85, 160, 220, 173)), ".68", "use"));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
        Assert.Equal(new byte[] { 85, 161, 220, 117 }, surface.Pixels.AsSpan((12 * 32 + 12) * 4, 4).ToArray());
    }

    private static string Image(SvgColor color)
    {
        byte[] pixels = [color.Red, color.Green, color.Blue, color.Alpha, color.Red, color.Green, color.Blue, color.Alpha,
            color.Red, color.Green, color.Blue, color.Alpha, color.Red, color.Green, color.Blue, color.Alpha];
        return "<image x='4' y='8' width='24' height='16' preserveAspectRatio='none' href='data:image/png;base64," + Convert.ToBase64String(PngEncoder.Encode(2, 2, pixels, CancellationToken.None)) + "'/>";
    }

    private static string Document(string image, string opacity, string consumer)
    {
        string body = consumer switch
        {
            "group" => "<g opacity='" + opacity + "'>" + image + "</g>",
            "use" => "<defs>" + image.Replace("/>", " id='s'/>", StringComparison.Ordinal) + "</defs><use href='#s' opacity='" + opacity + "'/>",
            "symbol" => "<defs><symbol id='s'>" + image + "</symbol></defs><use href='#s' opacity='" + opacity + "'/>",
            _ => image.Replace("/>", " opacity='" + opacity + "'/>", StringComparison.Ordinal)
        };
        return "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>";
    }
}
