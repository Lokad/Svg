using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGradientViewportTests
{
    private const string Stops = "<stop stop-color='#e04020'/><stop offset='1' stop-color='#2040e0'/>";

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("linearGradient", false)]
    [InlineData("linearGradient", true)]
    [InlineData("radialGradient", false)]
    [InlineData("radialGradient", true)]
    public void Percentages_follow_the_gradient_definition_and_each_alias_viewport(string kind, bool nestedAlias)
    {
        byte[] input = Document(kind, nestedAlias);
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(2, root.Geometries.Length);
        if (kind == "linearGradient")
        {
            SvgLinearPaintMap first = Assert.IsType<SvgLinearPaintMap>(root.Geometries[0].Gradient.Gradient.SourcePaint);
            SvgLinearPaintMap second = Assert.IsType<SvgLinearPaintMap>(root.Geometries[1].Gradient.Gradient.SourcePaint);
            Assert.Equal((16, 0, 64, 0), (first.Data.X1, first.Data.Y1, first.Data.X2, first.Data.Y2));
            Assert.Equal(nestedAlias ? (4, 0, 16, 0) : (16, 0, 64, 0),
                (second.Data.X1, second.Data.Y1, second.Data.X2, second.Data.Y2));
        }
        else
        {
            SvgRadialGradientSource first = Assert.IsType<SvgRadialGradientSource>(root.Geometries[0].Radial.Gradient.SourceDefinition);
            SvgRadialGradientSource second = Assert.IsType<SvgRadialGradientSource>(root.Geometries[1].Radial.Gradient.SourceDefinition);
            Assert.Equal((32, 16, 25.298223495483398), (first.CenterX, first.CenterY, first.Radius));
            Assert.Equal(nestedAlias ? (8, 8, 8) : (32, 16, 25.298223495483398),
                (second.CenterX, second.CenterY, second.Radius));
            Assert.Equal(nestedAlias, !ReferenceEquals(first, second));
        }
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("linearGradient", false)]
    [InlineData("radialGradient", false)]
    [InlineData("linearGradient", true)]
    [InlineData("radialGradient", true)]
    public void Unit_overrides_reinterpret_inherited_lengths_without_reusing_normalized_coordinates(string kind, bool reverse)
    {
        string coordinates = kind == "linearGradient" ? "x1='.5' y1='0' x2='100%' y2='0'" : "cx='50%' cy='50%' r='50%'";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='32'><defs>"
            + "<" + kind + " id='g' href='#base' gradientUnits='" + (reverse ? "objectBoundingBox" : "userSpaceOnUse") + "'/>"
            + "<" + kind + " id='base' gradientUnits='" + (reverse ? "userSpaceOnUse" : "objectBoundingBox") + "' "
            + coordinates + ">" + Stops + "</" + kind + ">"
            + "</defs><rect width='32' height='32' fill='url(#g)'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        if (kind == "linearGradient")
        {
            Assert.Equal(.5, geometry.Gradient.Gradient.X1);
            Assert.Equal(reverse ? 1 : 64, geometry.Gradient.Gradient.X2);
        }
        else
        {
            SvgRadialGradientSource source = Assert.IsType<SvgRadialGradientSource>(geometry.Radial.Gradient.SourceDefinition);
            Assert.Equal(reverse ? (.5, .5, .5) : (32, 16, 25.298223495483398), (source.CenterX, source.CenterY, source.Radius));
        }
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Percentage_alias_storage_is_released_on_refusal_and_can_be_prepared_again()
    {
        byte[] input = Document("radialGradient", true);
        RenderBudget measured = new(new HostMemoryBudget(268_435_456));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, CancellationToken.None))
            Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget denied = new(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded,
            Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, CancellationToken.None))
            Assert.Equal(2, root.Geometries.Length);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
    }

    private static byte[] Document(string kind, bool nestedAlias)
    {
        string coordinates = kind == "linearGradient" ? "x1='25%' y1='0'" : "cx='50%' cy='50%' r='50%'";
        string alias = "<" + kind + " id='localPaint' href='#base'/>";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='32'><defs>"
            + "<" + kind + " id='base' gradientUnits='userSpaceOnUse' " + coordinates + ">" + Stops + "</" + kind + ">"
            + "<" + kind + " id='rootPaint' href='#base'/>" + (nestedAlias ? "" : alias)
            + "</defs><rect width='32' height='32' fill='url(#rootPaint)'/>"
            + "<svg x='32' width='32' height='32' viewBox='0 0 16 16'>"
            + (nestedAlias ? "<defs>" + alias + "</defs>" : "")
            + "<rect width='16' height='16' fill='url(#localPaint)'/></svg></svg>");
    }

}
