using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgViewportOpacityTests
{
    [Theory]
    [InlineData("translate(12 7)")]
    [InlineData("matrix(1 .25 -.3 .8 8 2)")]
    public void Nested_viewport_own_map_matches_an_enclosing_map_with_physical_clip_and_sibling(string transform)
    {
        const string viewport = "<svg x='8' y='6' width='40' height='32' viewBox='0 0 20 16' opacity='.625'><rect width='28' height='24' fill='#2168ad'/><circle cx='14' cy='10' r='5' fill='#ab7128'/></svg>";
        const string start = "<svg xmlns='http://www.w3.org/2000/svg' width='96' height='72'><g transform='translate(4 3)'>";
        const string end = "<rect x='72' y='4' width='4' height='4' fill='blue'/></g></svg>";
        byte[] expected = System.Text.Encoding.UTF8.GetBytes(start + "<g transform='" + transform + "'>" + viewport + "</g>" + end);
        byte[] input = System.Text.Encoding.UTF8.GetBytes(start + viewport.Replace("<svg ", "<svg transform='" + transform + "' ", StringComparison.Ordinal) + end);
        RenderBudget sceneBudget = new(1_000_000), renderBudget = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, CancellationToken.None))
        {
            byte[] actual = SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, renderBudget, CancellationToken.None).ToArray();
            Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(), actual);
            Assert.Equal(0, renderBudget.Snapshot().CurrentBytes);
            RenderBudget denied = new(renderBudget.Snapshot().PeakBytes / 2);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            Assert.Equal(actual, SvgConverter.ConvertToPng(scene, SvgRenderOptions.Default, renderBudget, CancellationToken.None).ToArray());
        }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
    }

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
