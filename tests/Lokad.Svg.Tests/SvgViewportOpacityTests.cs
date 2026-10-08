using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgViewportOpacityTests
{
    [Fact]
    public void Inherited_viewport_clip_restores_the_group_once_and_leaves_source_ink_uncut()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "symbol-viewport-opacity", "right-bottom-transparent.svg"));
        RenderBudget budget = new(10_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgDrawCommand group = Assert.Single(scene.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup);
            Assert.Equal(SvgLayerRegionKind.Viewport, group.RegionKind);
            SvgClipBox clip = Assert.IsType<SvgClipBox>(group.MaskRegion);
            Assert.Equal(new SvgClipBox(3.375, 4.625, 32, 24), clip);
            Assert.Equal(.625f, group.SourceOpacity.Value);
            Assert.Equal(255, group.MaskAlpha);
            Assert.Equal(4, scene.Geometries.Length);
            Assert.All(scene.Geometries, geometry => Assert.Null(geometry.Polygon.Clip));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
