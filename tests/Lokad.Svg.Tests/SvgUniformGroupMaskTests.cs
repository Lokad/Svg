using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgUniformGroupMaskTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deferred_mask_scopes_follow_expanded_children_without_absorbing_siblings(bool forward)
    {
        RenderBudget budget = new(10_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(Input(forward), SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None))
        {
            Assert.Collection(scene.Commands,
                command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, 255, null, -1, 64), command),
                command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1, 255), command),
                command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255), command),
                command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, 1, 255, null, -1, 255), command),
                command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, 255, null, -1, 192), command),
                command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.Geometry, 2, 255, null, -1, 255), command),
                command => Assert.Equal(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255), command));
            Assert.True(Assert.IsType<SvgAuthoredPath>(scene.Geometries[0].AuthoredPath).IsKnownConvex);
            Assert.Equal(3, scene.Geometries.Length);
            Assert.All(scene.Geometries, geometry => Assert.Equal(255, geometry.Kind == SvgGeometryKind.PolygonFill ? geometry.Polygon.Color.Alpha : geometry.Rectangle.Color.Alpha));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Partial_mask_resolution_releases_scene_storage_on_memory_work_and_token_denial(bool forward)
    {
        RenderBudget measured = new(10_000_000);
        ParserWorkCounter work = new(1_000_000);
        using (SvgRootParser.Parse(Input(forward), SvgRenderLimits.Default, measured, null, work, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget tight = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(Input(forward), SvgRenderLimits.Default, tight, null, new(1_000_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        RenderBudget denied = new(10_000_000);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(Input(forward), SvgRenderLimits.Default, denied, null, new(work.Used - 1), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException canceled = Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(Input(forward), SvgRenderLimits.Default, denied, null, new(1_000_000), cancellation.Token));
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Isolated_mask_surfaces_release_storage_on_memory_work_and_token_denial(bool forward)
    {
        RenderBudget sceneBudget = new(10_000_000), measured = new(10_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(Input(forward), SvgRenderLimits.Default, sceneBudget, null, new(1_000_000), CancellationToken.None))
        {
            long work;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None)) work = surface.WorkUnits;
            Assert.Equal(0, measured.Snapshot().CurrentBytes);
            RenderBudget tight = new(measured.Snapshot().PeakBytes - 1);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(scene, SvgRenderOptions.Default, tight, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, tight.Snapshot().CurrentBytes);
            RenderBudget denied = new(10_000_000);
            SvgRenderLimits standard = SvgRenderLimits.Default;
            SvgRenderLimits limits = new(standard.MaximumAccountedBytes, standard.MaximumInputBytes, standard.MaximumDimension,
                standard.MaximumPixels, standard.MaximumEncodedPngBytes, standard.MaximumNodes, standard.MaximumPathCommands,
                checked((int)work - 1), standard.MaximumXmlDepth, standard.MaximumAttributesPerElement);
            SvgRenderOptions limited = new(null, 1, SvgColor.Transparent, limits);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(scene, limited, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            OperationCanceledException canceled = Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(scene, SvgRenderOptions.Default, denied, cancellation.Token));
            Assert.Equal(cancellation.Token, canceled.CancellationToken);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
    }

    private static byte[] Input(bool forward)
    {
        const string masks = "<defs><mask id='a'><rect width='43' height='37' fill='#404040'/></mask><mask id='b'><rect width='43' height='37' fill='#c0c0c0'/></mask></defs>";
        const string content = "<g mask='url(#a)'><use href='#r'/></g><rect x='1' y='1' width='2' height='2' fill='#b13d72'/><g mask='url(#b)'><rect x='13' y='11' width='23' height='19' fill='#327fc1'/></g>";
        const string definition = "<defs><rect id='r' x='6.375' y='4.25' width='29.125' height='23.5' rx='6.25' ry='4.75' fill='#2b79c1'/></defs>";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='43' height='37'>" + (forward ? content + masks : masks + content) + definition + "</svg>");
    }
}
