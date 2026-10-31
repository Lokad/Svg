using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgRecordedSourceEnclosureTests
{
    [Theory]
    [InlineData("<rect x='8.25' y='10.125' width='11.25' height='95.5' rx='5.125'/>", 106.125f, 107.75f)]
    [InlineData("<ellipse cx='13.875' cy='57.875' rx='5.625' ry='47.75'/>", 106.125f, 107.75f)]
    [InlineData("<circle cx='13.875' cy='15.75' r='5.625'/>", 21.875f, 22.75f)]
    public void Drawing_enclosure_precedes_placement_while_filter_visuals_keep_fractional_extents(string shape, float visualBottom, float recordedBottom)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><g transform='translate(.5 .75)' stroke='black' stroke-width='1'>"
            + shape + "</g></svg>");
        using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgSceneStorage storage = Assert.IsType<SvgSceneStorage>(scene.SceneStorage);
        SvgRecordedPaintValue paint = storage.RecordedPaintValues[scene.Geometries[0].RecordedPaint.Identity - 1];
        SvgSourceObjectBounds source = Assert.IsType<SvgSourceObjectBounds>(paint.SourceObjectBounds);
        Assert.Equal(new SvgPaintBounds(7.75f, 9.625f, 20, visualBottom), source.VisualBounds);
        Assert.Equal(new SvgPaintBounds(7.5f, 9.75f, 20.5f, recordedBottom), paint.VisualBounds);
    }
}
