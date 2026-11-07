using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("linearGradient")]
    [InlineData("radialGradient")]
    [InlineData("pattern")]
    public async Task Glyph_shader_prepares_one_context_for_its_pixel_traversal(string paint)
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        string definition = paint == "pattern"
            ? "<pattern id='p' patternUnits='userSpaceOnUse' width='4.5' height='5.5'><rect width='3' height='4' fill='red'/></pattern>"
            : "<" + paint + " id='p' gradientUnits='userSpaceOnUse'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></" + paint + ">";
        // The synthetic outline is only ten font units high; render at its 1000-unit em.
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs>" + definition + "</defs><text x='3' y='55' font-family='Example' font-size='1000' fill='url(#p)'>A</text></svg>");
        int contexts = 0;
        using IDisposable scope = SvgShaderSamplingProbe.Arm(() => contexts++);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] output = SvgRasterTestHelper.RenderPixels(svg, options, out _, out _);
        Assert.Contains(output, value => value != 0);
        Assert.Equal(1, contexts);
    }
}
