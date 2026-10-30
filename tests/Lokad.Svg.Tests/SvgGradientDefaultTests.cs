using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGradientDefaultTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Explicit_default_radial_units_match_omitted_units_without_a_template(int count)
    {
        string stops = count == 0 ? "" : count == 1 ? "<stop stop-color='red'/>" : "<stop stop-color='red'/><stop offset='1' stop-color='blue'/>";
        foreach (bool forward in new[] { false, true })
        foreach (string transform in new[] { "", " gradientTransform='translate(.1 .1)'" })
            Assert.Equal(Render(Svg("", transform, forward)), Render(Svg(" gradientUnits='objectBoundingBox'", transform, forward)));

        byte[] Svg(string units, string transform, bool forward)
        {
            string definitions = "<defs><radialGradient id='p'" + units + transform + ">" + stops + "</radialGradient></defs>";
            const string draw = "<rect width='32' height='32' fill='url(#p)'/>";
            return Input(forward ? draw + definitions : definitions + draw);
        }
    }

    public static IEnumerable<object[]> ImplicitPercentages()
    {
        foreach (string attributes in new[] { "", "x1='2'", "y1='2' y2='20'", "x1='0' y1='0' y2='0'" })
            yield return ["<linearGradient id='p' gradientUnits='userSpaceOnUse' " + attributes + "><stop stop-color='red'/></linearGradient>",
                true, attributes.Contains("x1='2'", StringComparison.Ordinal) ? 2 : 0,
                attributes.Contains("y1='2'", StringComparison.Ordinal) ? 2 : 0, 32,
                attributes.Contains("y2='20'", StringComparison.Ordinal) ? 20 : 0];
        foreach (string attributes in new[] { "", "cx='16'", "cy='16' r='8'", "cx='16' r='8'", "cx='16' cy='16'" })
            yield return ["<radialGradient id='p' gradientUnits='userSpaceOnUse' " + attributes + "><stop stop-color='red'/></radialGradient>",
                false, 16, 16, attributes.Contains("r='8'", StringComparison.Ordinal) ? 8 : 16, 0];
        yield return ["<radialGradient id='base' gradientUnits='userSpaceOnUse' cx='16' cy='16' r='8'><stop stop-color='red'/></radialGradient><linearGradient id='p' href='#base'/>", true, 0, 0, 32, 0];
        yield return ["<linearGradient id='base' gradientUnits='userSpaceOnUse' x2='32'><stop stop-color='red'/></linearGradient><radialGradient id='p' href='#base'/>", false, 16, 16, 16, 0];
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [MemberData(nameof(ImplicitPercentages))]
    public void Initial_user_space_percentages_resolve_before_constant_paint_lowering(string gradients, bool linear, double x, double y, double extent, double endY)
    {
        byte[] svg = Input("<defs>" + gradients + "</defs><rect width='32' height='32' fill='url(#p)'/>");
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(svg, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgGeometry geometry = Assert.Single(root.Geometries);
            if (linear)
            {
                SvgLinearPaintMap source = Assert.IsType<SvgLinearPaintMap>(geometry.Gradient.Gradient.SourcePaint);
                Assert.Equal((x, y, extent, endY), (source.Data.X1, source.Data.Y1, source.Data.X2, source.Data.Y2));
            }
            else
            {
                SvgRadialGradientSource source = Assert.IsType<SvgRadialGradientSource>(geometry.Radial.Gradient.SourceDefinition);
                Assert.Equal((x, y, extent), (source.CenterX, source.CenterY, source.Radius));
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("linearGradient", "x2='32'", "x1='2'", "x1='2' x2='32'")]
    [InlineData("radialGradient", "cx='16' cy='16' r='8'", "", "cx='16' cy='16' r='8'")]
    public void Numeric_vectors_and_inherited_numeric_coordinates_remain_supported(string kind, string vector, string overrides, string resolved)
    {
        foreach (bool forward in new[] { false, true })
        {
            string source = "<" + kind + " id='base' gradientUnits='userSpaceOnUse' " + vector + "><stop stop-color='red'/><stop offset='1' stop-color='blue'/></" + kind + ">";
            string alias = "<" + kind + " id='p' href='#base' " + overrides + "/>";
            byte[] input = Input("<defs>" + (forward ? alias + source : source + alias) + "</defs><rect width='32' height='32' fill='url(#p)'/>");
            byte[] expected = Input("<defs><" + kind + " id='p' gradientUnits='userSpaceOnUse' " + resolved + "><stop stop-color='red'/><stop offset='1' stop-color='blue'/></" + kind + "></defs><rect width='32' height='32' fill='url(#p)'/>");
            Assert.Equal(Render(expected), Render(input));
        }
    }

    private static byte[] Input(string body) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
    private static byte[] Render(byte[] svg) => SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
