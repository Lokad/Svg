using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgUseStrokeOpacityTests
{
    [Theory]
    [InlineData("", .375f, 96)]
    [InlineData("stroke-opacity='.625'", .625f, 159)]
    [InlineData("stroke-opacity='1'", 1f, 255)]
    public void Source_stroke_opacity_wins_independently_of_fill_opacity(string authored, float alpha, int colorAlpha)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='24'><defs><rect id='p' x='4' y='4' width='12' height='10' fill='#297fb3' stroke='#bc9334' stroke-width='2' " + authored + "/></defs><use href='#p' fill-opacity='.75' stroke-opacity='.375'/></svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgGeometry geometry = Assert.Single(scene.Geometries);
            SvgSceneStorage storage = scene.SceneStorage ?? throw new InvalidOperationException();
            SvgRecordedPaintValue fill = storage.RecordedPaintValues[geometry.RecordedPaint.Identity - 1];
            SvgRecordedPaintValue stroke = storage.RecordedPaintValues[geometry.RecordedStrokePaint.Identity - 1];
            Assert.False(fill.IsStroke);
            Assert.True(stroke.IsStroke);
            Assert.Equal(.75f, fill.Alpha);
            Assert.Equal(alpha, stroke.Alpha);
            Assert.Equal(new SvgColor(188, 147, 52, checked((byte)colorAlpha)), Assert.IsType<SvgStroke>(geometry.Rectangle.Stroke).Color);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("direct-rectangle-transparent")]
    [InlineData("nested-use-authored-transparent")]
    [InlineData("shader-stroke-path-transparent")]
    [InlineData("shader-fill-path-solid-stroke-transparent")]
    [InlineData("new-use-curved-stroke-transparent")]
    public void Instance_opacity_and_unpainted_centerlines_release_all_owners_on_denial_and_cancellation(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "use-stroke-opacity", identity + ".svg"));
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None))
            {}
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) DenyMemory(limit);
        DenyMemory(peak - 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget canceled = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, canceled, null, cancellation.Token)).CancellationToken);
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, canceled, null, CancellationToken.None))
        {
            Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(scene, SvgRenderOptions.Default, canceled, cancellation.Token)).CancellationToken);
            using SvgConverter.RasterizedSurface retried = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, canceled, CancellationToken.None);
        }
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(limit));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None);
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }
}
