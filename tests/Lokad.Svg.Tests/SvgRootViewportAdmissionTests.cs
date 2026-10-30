using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgRootViewportAdmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nonuniform_mapping_requires_source_commands_only_for_painted_curves(bool hasFill)
    {
        RenderBudget budget = new(new HostMemoryBudget(100_000));
        using (SvgPolygonFill fill = SvgPolygonFill.Create(
            [new(0, 0), new(1, 3), new(3, 3), new(4, 0)], [4], null,
            [], [new(0, 0, 3, new(0, 4), new(4, 4))],
            new(255, 0, 0, 255), hasFill, false, SvgPolygonMapping.FlattenedCurves, null, null, budget))
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            SvgRoot root = new(new(8, false), new(8, false),
                new(0, 0, 4, 8, SvgViewBoxAlignment.None, SvgViewBoxAlignment.None, SvgViewBoxFit.None),
                [geometry], [new(SvgDrawCommandKind.Geometry, 0, 255, null, -1, 255)], false, null);
            if (hasFill)
            {
                SvgException error = Assert.Throws<SvgException>(() =>
                    SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None));
                Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
                Assert.Contains("retained source commands", error.Message);
            }
            else
            {
                using SvgConverter.RasterizedSurface surface =
                    SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
                Assert.All(surface.Pixels, value => Assert.Equal(0, value));
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
