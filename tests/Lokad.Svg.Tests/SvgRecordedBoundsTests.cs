using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRecordedBoundsTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Default_mask_bounds_exclude_rectangle_stroke_and_keep_a_hard_boundary(bool forward, bool group, bool copy)
    {
        byte[] input = MaskedRectangle(forward, group, copy);
        RenderBudget budget = new(10_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None))
        {
            SvgDrawCommand mask = Assert.Single(scene.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup);
            SvgClipBox region = Assert.IsType<SvgClipBox>(mask.MaskRegion);
            Assert.Equal(12.8, region.X, 6);
            Assert.Equal(12.8, region.Y, 6);
            Assert.Equal(38.4, region.Width, 6);
            Assert.Equal(38.4, region.Height, 6);
            Assert.Equal(128, mask.MaskAlpha);
            Assert.Equal(SvgLayerRegionKind.Mask, mask.RegionKind);
            Assert.Equal(SvgPaintOpacity.Opaque, mask.SourceOpacity);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using SvgRoot parsed = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        using SvgConverter.RasterizedSurface image = SvgConverter.Rasterize(parsed, SvgRenderOptions.Default, new(10_000_000), CancellationToken.None);
        // These boundary and interior values come from the retained browser capture.
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, image.Pixels.AsSpan((16 * image.Width + 12) * 4, 4).ToArray());
        Assert.Equal(new byte[] { 255, 0, 0, 128 }, image.Pixels.AsSpan((16 * image.Width + 13) * 4, 4).ToArray());
        Assert.Equal(new byte[] { 0, 255, 0, 128 }, image.Pixels.AsSpan((30 * image.Width + 30) * 4, 4).ToArray());
    }

    [Theory]
    [InlineData(false, 2, false)]
    [InlineData(true, 2, false)]
    [InlineData(false, 6, false)]
    [InlineData(true, 6, false)]
    [InlineData(false, 2, true)]
    [InlineData(true, 2, true)]
    [InlineData(false, 6, true)]
    [InlineData(true, 6, true)]
    public void Square_dash_recording_keeps_object_and_visual_bounds_separate(bool radial, int width, bool copy)
    {
        RenderBudget budget = new(10_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(DashedLine(radial, width, copy), SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None))
        {
            SvgSceneStorage storage = Assert.IsType<SvgSceneStorage>(scene.SceneStorage);
            int x = copy ? 5 : 0, y = copy ? 3 : 0;
            SvgPaintBounds expectedObject = new(8 + x, 20 + y, 44 + x, 20 + y);
            SvgPaintBounds expectedVisual = width == 2 ? new(6 + x, 18 + y, 46 + x, 22 + y) : new(3 + x, 15 + y, 49 + x, 25 + y);
            SvgRecordedPaint first = scene.Geometries[0].RecordedPaint;
            Assert.All(scene.Geometries, geometry =>
            {
                Assert.Equal(first, geometry.RecordedPaint);
                SvgRecordedPaintValue paint = storage.RecordedPaintValues[geometry.RecordedPaint.Identity - 1];
                Assert.Equal(expectedObject, paint.ObjectBounds);
                Assert.Equal(expectedVisual, paint.VisualBounds);
                Assert.False(paint.Foldable);
            });
            SvgDrawCommand opacity = Assert.Single(scene.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup);
            Assert.Equal(0.4375f, opacity.SourceOpacity.Value);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recorded_bounds_release_on_parser_work_memory_and_cancellation_denial(bool radial)
    {
        byte[] input = DashedLine(radial, 6, true);
        RenderBudget measured = new(10_000_000);
        ParserWorkCounter work = new(1_000_000);
        using (SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        foreach (long maximum in new[] { measured.Snapshot().PeakBytes - 1, measured.Snapshot().PeakBytes })
        {
            RenderBudget budget = new(maximum);
            if (maximum < measured.Snapshot().PeakBytes)
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None)).ErrorCode);
            else using (SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(1_000_000), CancellationToken.None)) { }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
        RenderBudget denied = new(10_000_000);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(work.Used - 1), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(1_000_000), cancellation.Token)).CancellationToken);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    private static byte[] MaskedRectangle(bool forward, bool group, bool copy)
    {
        const string gradient = "<linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>";
        const string mask = "<defs><mask id='m'><rect width='64' height='64' fill='#808080'/></mask></defs>";
        string rectangle = "<rect id='r' x='16' y='16' width='32' height='32' fill='#00ff00' stroke='url(#g)' stroke-width='8'";
        string definitions = "<defs>" + gradient + (copy ? rectangle + "/>" : "") + "</defs>";
        string target = copy ? "<use href='#r'" : rectangle;
        string body = group ? "<g mask='url(#m)'>" + target + "/></g>" : target + " mask='url(#m)'/>";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'>" + definitions + (forward ? body + mask : mask + body) + "</svg>");
    }

    private static byte[] DashedLine(bool radial, int width, bool copy)
    {
        string gradient = radial
            ? "<radialGradient id='g' gradientUnits='userSpaceOnUse' cx='26' cy='20' r='24'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>"
            : "<linearGradient id='g' gradientUnits='userSpaceOnUse' x1='8' y1='20' x2='44' y2='20'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>";
        string line = "<line id='l' x1='8' y1='20' x2='44' y2='20' stroke='url(#g)' stroke-width='" + width + "' stroke-linecap='square' stroke-dasharray='12 8' opacity='0.4375'/>";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='48'><defs>" + gradient + (copy ? line : "") + "</defs>" + (copy ? "<use href='#l' x='5' y='3'/>" : line) + "</svg>");
    }
}
