using Lokad.Svg.Rendering;
using Lokad.Svg.Encoding;

namespace Lokad.Svg.Tests;

public sealed class SvgShaderSamplingContextLifetimeTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("linearGradient")]
    [InlineData("radialGradient")]
    public void Rectangle_shader_prepares_one_context_for_its_pixel_traversal(string gradient)
    {
        string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><" + gradient
            + " id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></" + gradient
            + "></defs><rect x='5.25' y='7.5' width='40' height='32' fill='url(#p)'/></svg>";
        int contexts = 0;
        using IDisposable scope = SvgShaderSamplingProbe.Arm(() => contexts++);
        byte[] pixels = SvgRasterTestHelper.RenderPixels(svg, out int width, out int height);
        Assert.Equal(64, width);
        Assert.Equal(64, height);
        Assert.Contains(pixels, value => value != 0);
        Assert.Equal(1, contexts);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("pattern")]
    [InlineData("image")]
    public void Texture_rectangle_prepares_one_context_for_its_pixel_traversal(string kind)
    {
        string content;
        if (kind == "pattern")
            content = "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='4.5' height='5.5'><rect width='3' height='4' fill='red'/></pattern></defs><rect x='5.25' y='7.5' width='40' height='32' fill='url(#p)'/>";
        else
        {
            byte[] pixels = [255, 0, 0, 255, 0, 0, 255, 255, 0, 255, 0, 255, 255, 255, 0, 255];
            string png = Convert.ToBase64String(PngEncoder.Encode(2, 2, pixels, CancellationToken.None));
            content = "<image x='5.25' y='7.5' width='40' height='32' href='data:image/png;base64," + png + "'/>";
        }
        int contexts = 0;
        using IDisposable scope = SvgShaderSamplingProbe.Arm(() => contexts++);
        byte[] output = SvgRasterTestHelper.RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'>" + content + "</svg>", out _, out _);
        Assert.Contains(output, value => value != 0);
        Assert.Equal(1, contexts);
    }
}
