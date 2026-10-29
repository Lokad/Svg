using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgMaskClipIsolationTests
{
    public static IEnumerable<object[]> MaskedPaints()
    {
        foreach (string shape in new[] { "single-opaque", "single-partial", "single-shader", "overlap", "solid-stroke", "shader-stroke" })
        foreach (string alpha in new[] { "1", ".75", ".5" })
        foreach (string maskType in new[] { "alpha", "luminance" })
        foreach (string opacity in new[] { "1", ".5" })
        foreach (string clip in new[] { "fractional", "integer", "contained" })
            yield return [shape, alpha, maskType, opacity, clip];
    }

    [Theory]
    [MemberData(nameof(MaskedPaints))]
    public void Mask_owners_preserve_native_clip_placement_and_definition_order(string shape, string alpha, string maskType, string opacity, string clip)
    {
        string paints = shape switch
        {
            "single-opaque" => "<rect x='4' y='4' width='20' height='20' fill='red'/>",
            "single-partial" => "<rect x='4' y='4' width='20' height='20' fill='#923475' fill-opacity='.625'/>",
            "single-shader" => "<rect x='4' y='4' width='20' height='20' fill='url(#p)'/>",
            "overlap" => "<rect x='4' y='4' width='20' height='20' fill='red'/><rect x='3' y='3' width='22' height='22' fill='blue' fill-opacity='.25'/>",
            "solid-stroke" => "<path d='M4 4H24V24H4Z' fill='red' stroke='blue' stroke-width='.25' stroke-opacity='.75'/>",
            _ => "<path d='M4 4H24V24H4Z' fill='red' stroke='url(#p)' stroke-width='.25' stroke-opacity='.75'/>"
        };
        string bounds = clip switch
        {
            "fractional" => "x='5.25' y='3.75' width='19.5' height='22'",
            "integer" => "x='5' y='4' width='20' height='22'",
            _ => "width='32' height='32'"
        };
        string definitions = "<defs><linearGradient id='p' gradientUnits='userSpaceOnUse' x2='32'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><clipPath id='c'><rect " + bounds + "/></clipPath><mask id='m' mask-type='" + maskType + "' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='32' height='32' fill='white' fill-opacity='" + alpha + "'/></mask></defs>";
        byte[] expected = Render(false, false);
        Assert.Equal(expected, Render(true, false));
        Assert.Equal(expected, Render(false, true));
        Assert.Equal(expected, Render(true, true));

        byte[] Render(bool forward, bool own)
        {
            string body = own ? "<g clip-path='url(#c)' mask='url(#m)' opacity='" + opacity + "'>" + paints + "</g>"
                : "<g clip-path='url(#c)'><g mask='url(#m)' opacity='" + opacity + "'>" + paints + "</g></g>";
            return SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + (forward ? body + definitions : definitions + body) + "</svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        }
    }

    [Theory]
    [InlineData(false, ".75", 5, 10, 146, 52, 117, 89)]
    [InlineData(true, "1", 16, 16, 123, 0, 131, 255)]
    [InlineData(true, ".75", 16, 16, 123, 0, 131, 191)]
    public void Mask_clip_edges_and_shader_grids_preserve_independently_captured_bytes(bool shader, string alpha, int x, int y, byte red, byte green, byte blue, byte opacity)
    {
        string paints = shader ? "<rect x='4' y='4' width='20' height='20' fill='url(#p)'/>"
            : "<rect x='4' y='4' width='20' height='20' fill='#923475' fill-opacity='.625'/>";
        string definitions = "<defs><linearGradient id='p' gradientUnits='userSpaceOnUse' x2='32'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><clipPath id='c'><rect x='5.25' y='3.75' width='19.5' height='22'/></clipPath><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='32' height='32' fill='white' fill-opacity='" + alpha + "'/></mask></defs>";
        foreach (bool forward in new[] { false, true })
        foreach (bool own in new[] { false, true })
        {
            string body = own ? "<g clip-path='url(#c)' mask='url(#m)'>" + paints + "</g>"
                : "<g clip-path='url(#c)'><g mask='url(#m)'>" + paints + "</g></g>";
            byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + (forward ? body + definitions : definitions + body) + "</svg>");
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(2_000_000));
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                Assert.Equal(new byte[] { red, green, blue, opacity }, surface.Pixels.AsSpan((y * 32 + x) * 4, 4).ToArray());
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }
}
