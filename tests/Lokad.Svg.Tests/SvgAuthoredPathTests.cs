using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAuthoredPathTests
{
    private const string Path = "<path d='M1.137 2.319 C3.913 1.137 6.619 2.137 7.813 5.319 L2.137 7.619 Z'/>";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Crossing_replay_keeps_original_curves_after_scene_normalization(bool isEvenOdd)
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse("<path fill-rule='" + (isEvenOdd ? "evenodd" : "nonzero") + "' d='M0,0 C2,2 6,6 8,8 L8,0 C6,2 2,6 0,8 Z'/>", budget))
        {
            SvgGeometry geometry = Assert.Single(root.Geometries);
            Assert.Equal(2, geometry.Polygon.ContourEnds.Length);
            SvgAuthoredPath authored = Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath);
            using SvgMappedAuthoredPath mapped = authored.Prepare(SvgAffineTransform.Identity, isEvenOdd, budget, new ParserWorkCounter(100_000), CancellationToken.None);
            Assert.Single(mapped.ContourEnds);
            Assert.Equal(2, mapped.CubicSpans.Length);
            Assert.Equal(isEvenOdd ? new[] { 1 } : null, mapped.Signs);
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(mapped.Points, mapped.ContourEnds, mapped.Signs ?? [], isEvenOdd, mapped.Signs, SvgPolygonMapping.NativeFillEdges, mapped.QuadSpans, mapped.CubicSpans, 0, 8, 0, 8, 16, 16, budget, out _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            walker.PrepareRow(3, out _, CancellationToken.None);
            byte[] pixel = new byte[4];
            walker.PaintPixel(3, pixel, SvgAnalyticPaint.Solid(new(255, 0, 0, 255)), SvgAnalyticClipCoverage.Full);
            // Three fresh pinned-browser captures record this native crossing.
            Assert.Equal(130, pixel[3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Even_odd_replay_keeps_all_authored_contours_including_crossings(bool isEvenOdd)
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse("<path fill-rule='" + (isEvenOdd ? "evenodd" : "nonzero") + "' d='M0,0 C2,2 6,6 8,8 L8,0 C6,2 2,6 0,8 Z M10,0 C12,2 16,6 18,8 L18,0 C16,2 12,6 10,8 Z'/>", budget))
        {
            SvgAuthoredPath authored = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            using SvgMappedAuthoredPath mapped = authored.Prepare(SvgAffineTransform.Identity, isEvenOdd, budget, new ParserWorkCounter(100_000), CancellationToken.None);
            Assert.Equal(2, mapped.ContourEnds.Length);
            Assert.Equal(isEvenOdd ? new[] { 1, 1 } : null, mapped.Signs);
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(mapped.Points, mapped.ContourEnds, mapped.Signs ?? [], isEvenOdd, mapped.Signs, SvgPolygonMapping.NativeFillEdges, 0, 18, 0, 8, 32, 16, budget, out _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            walker.PrepareRow(2, out _, CancellationToken.None);
            foreach (int x in new[] { 1, 11 })
            {
                byte[] pixel = new byte[4];
                walker.PaintPixel(x, pixel, SvgAnalyticPaint.Solid(new(255, 0, 0, 255)), SvgAnalyticClipCoverage.Full);
                Assert.Equal(255, pixel[3]);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Cancelled_even_odd_replay_releases_storage_and_preserves_the_token()
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse("<path fill-rule='evenodd' d='M1,1 Q15,1 15,15 L1,15 Z M5,5 Q11,5 11,11 L5,11 Z'/>", budget))
        {
            SvgAuthoredPath authored = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            long retained = budget.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            OperationCanceledException cancelled = Assert.ThrowsAny<OperationCanceledException>(() => authored.Prepare(SvgAffineTransform.Identity, true, budget, new ParserWorkCounter(100_000), cancellation.Token));
            Assert.Equal(cancellation.Token, cancelled.CancellationToken);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgMappedAuthoredPath retry = authored.Prepare(SvgAffineTransform.Identity, true, budget, new ParserWorkCounter(100_000), CancellationToken.None);
            Assert.Equal(new[] { 1, 1 }, retry.Signs);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Denied_crossing_replay_keeps_its_authored_owner_and_releases_scratch()
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse("<path fill-rule='evenodd' d='M0,0 C2,2 6,6 8,8 L8,0 C6,2 2,6 0,8 Z'/>", budget))
        {
            SvgAuthoredPath authored = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            long retained = budget.Snapshot().CurrentBytes;
            using (budget.Reserve(SvgRenderLimits.Default.MaximumAccountedBytes - retained - 100, SvgMemoryCategory.Surface))
            {
                long padded = budget.Snapshot().CurrentBytes;
                SvgException denied = Assert.Throws<SvgException>(() => authored.Prepare(SvgAffineTransform.Identity, true, budget, new ParserWorkCounter(100_000), CancellationToken.None));
                Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
                Assert.Equal(padded, budget.Snapshot().CurrentBytes);
            }
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgMappedAuthoredPath retry = authored.Prepare(SvgAffineTransform.Identity, true, budget, new ParserWorkCounter(100_000), CancellationToken.None);
            Assert.Single(retry.ContourEnds);
            Assert.Equal(2, retry.CubicSpans.Length);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Denied_endpoint_arc_replay_releases_scratch_and_can_retry(bool denyWork)
    {
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot root = Parse("<path d='M12,4 A8,8 0 1 1 20,12 Z'/>", budget))
        {
            SvgAuthoredPath authored = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            long retained = budget.Snapshot().CurrentBytes;
            if (denyWork)
            {
                SvgException denied = Assert.Throws<SvgException>(() => authored.Prepare(SvgAffineTransform.Identity, false, budget, new ParserWorkCounter(authored.Commands.Length + 2), CancellationToken.None));
                Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
            }
            else
            {
                using (budget.Reserve(SvgRenderLimits.Default.MaximumAccountedBytes - retained - 100, SvgMemoryCategory.Surface))
                {
                    long padded = budget.Snapshot().CurrentBytes;
                    SvgException denied = Assert.Throws<SvgException>(() => authored.Prepare(SvgAffineTransform.Identity, false, budget, new ParserWorkCounter(100_000), CancellationToken.None));
                    Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
                    Assert.Equal(padded, budget.Snapshot().CurrentBytes);
                }
            }
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgMappedAuthoredPath retry = authored.Prepare(SvgAffineTransform.Identity, false, budget, new ParserWorkCounter(100_000), CancellationToken.None);
            Assert.Equal(3, retry.QuadSpans.Length);
            Assert.Empty(retry.CubicSpans);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static SvgRoot Parse(string body, RenderBudget budget)
        => SvgRootParser.Parse(TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' viewBox='0 0 32 32'>" + body + "</svg>"), SvgRenderLimits.Default, budget, null, new ParserWorkCounter(SvgRenderLimits.Default.MaximumWorkUnits), CancellationToken.None);
}
