using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAuthoredPathTests
{
    private const string Path = "<path d='M1.137 2.319 C3.913 1.137 6.619 2.137 7.813 5.319 L2.137 7.619 Z'/>";

    [Theory]
    [InlineData("<defs><path id='p' d='M1.137 2.319 C3.913 1.137 6.619 2.137 7.813 5.319 L2.137 7.619 Z'/></defs><use href='#p' fill='red'/><use href='#p' x='1' fill='blue'/>")]
    [InlineData("<defs><symbol id='p' viewBox='0 0 16 16'>" + Path + "</symbol></defs><use href='#p' width='16' height='16' fill='red'/><use href='#p' width='16' height='16' x='1' fill='blue'/>")]
    [InlineData("<defs><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><symbol id='p' viewBox='0 0 16 16'><path fill='url(#g)' d='M1.137 2.319 C3.913 1.137 6.619 2.137 7.813 5.319 L2.137 7.619 Z'/></symbol></defs><use href='#p' width='16' height='16'/><use href='#p' width='16' height='16' x='1'/>")]
    public void Repeated_instances_keep_independent_authored_storage_and_release_it(string body)
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse(body, budget))
        {
            SvgGeometry[] visible = root.Geometries.Where(g => g.Kind != SvgGeometryKind.Invalid).ToArray();
            Assert.Equal(2, visible.Length);
            SvgAuthoredPath first = Assert.IsType<SvgAuthoredPath>(visible[0].AuthoredPath);
            SvgAuthoredPath second = Assert.IsType<SvgAuthoredPath>(visible[1].AuthoredPath);
            Assert.NotSame(first, second);
            Assert.Equal(first.Commands.ToArray(), second.Commands.ToArray());
            Assert.NotEqual(first.SceneMap.E, second.SceneMap.E);
            using var raster = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Object_bounding_box_paint_keeps_scene_coordinates_during_authored_replay()
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse("<defs><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><g transform='scale(2)'><path fill='url(#g)' d='M1.137 2.319 C3.913 1.137 6.619 2.137 7.813 5.319 L2.137 7.619 Z'/></g>", budget))
        {
            SvgGeometry geometry = Assert.Single(root.Geometries);
            SvgAuthoredPath authored = Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath);
            SvgRectangle sceneBox = geometry.GradientPolygon.Box;
            SvgDevicePoint[] scenePoints = geometry.GradientPolygon.Points.ToArray();
            using SvgMappedAuthoredPath mapped = authored.Prepare(SvgAffineTransform.FromScaleOffset(.5, .5, 0, 0), false, budget, new ParserWorkCounter(100_000), CancellationToken.None);
            Assert.Equal(sceneBox, geometry.GradientPolygon.Box);
            Assert.Equal(scenePoints, geometry.GradientPolygon.Points);
            Assert.Equal(2, authored.SceneMap.A);
            Assert.Equal((double)(float)1.137, mapped.Points[0].X);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Denied_replay_releases_partial_arrays_and_keeps_the_source(long workLimit)
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse(Path, budget))
        {
            SvgAuthoredPath authored = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            long retained = budget.Snapshot().CurrentBytes;
            SvgException denied = Assert.Throws<SvgException>(() => authored.Prepare(SvgAffineTransform.Identity, false, budget, new ParserWorkCounter(workLimit), CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgMappedAuthoredPath retry = authored.Prepare(SvgAffineTransform.Identity, false, budget, new ParserWorkCounter(100_000), CancellationToken.None);
            Assert.NotEmpty(retry.Points);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Denied_authored_copy_releases_its_replacement_and_preserves_the_source()
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse(Path, budget))
        {
            SvgGeometry source = Assert.Single(root.Geometries);
            long retained = budget.Snapshot().CurrentBytes;
            SvgPolygonFill polygon = source.Polygon;
            SvgGeometry replacement = SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(polygon.Points, polygon.ContourEnds, polygon.Signs, polygon.Color, polygon.HasFill, polygon.IsEvenOdd, polygon.Mapping, polygon.Clip, null, budget));
            SvgException denied = Assert.Throws<SvgException>(() => source.CopyAuthoredPathTo(replacement, SvgAffineTransform.Identity, budget, new ParserWorkCounter(0), CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            Assert.NotEmpty(Assert.IsType<SvgAuthoredPath>(source.AuthoredPath).Commands.ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static SvgRoot Parse(string body, RenderBudget budget)
        => SvgRootParser.Parse(TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' viewBox='0 0 32 32'>" + body + "</svg>"), SvgRenderLimits.Default, budget, null, new ParserWorkCounter(SvgRenderLimits.Default.MaximumWorkUnits), CancellationToken.None);
}
