using System.Globalization;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgNativeStrokeOutlineTests
{
    [Theory]
    [InlineData("stroke-linejoin='bevel'", "")]
    [InlineData("style='stroke-linejoin:bevel'", "")]
    [InlineData("", "stroke-linejoin='bevel'")]
    [InlineData("stroke-linejoin='bevel' stroke-dasharray='3 1'", "")]
    public void Bevel_polylines_match_open_paths_with_direct_inline_inherited_and_dashed_styles(string attributes, string inherited)
    {
        string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><g " + inherited + ">";
        string suffix = " fill='none' stroke='#e04020' stroke-width='3.5' " + attributes + "/></g></svg>";
        byte[] polyline = System.Text.Encoding.UTF8.GetBytes(prefix + "<polyline points='14,12 8,4 2,12'" + suffix);
        byte[] path = System.Text.Encoding.UTF8.GetBytes(prefix + "<path d='M14,12 L8,4 L2,12'" + suffix);
        Assert.Equal(SvgConverter.ConvertToPng(path, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(polyline, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("miter-join-acute-64x64.svg")]
    [InlineData("bevel-join-acute-64x64.svg")]
    [InlineData("float-stroke-viewbox.svg")]
    public async Task Six_outputs_preserve_native_open_stroke_winding(string source)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", source));
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }

    [Theory]
    [InlineData("acute-miter", 8, 40, 32, 8, 56, 40, 8, 10)]
    [InlineData("right-miter", 2, 12, 8, 12, 8, 4, 4, 4)]
    [InlineData("diagonal-miter", 2, 12, 8, 4, 14, 12, 3.5, 4)]
    [InlineData("acute-bevel-limit", 8, 40, 32, 8, 56, 40, 8, 1)]
    [InlineData("obtuse-miter", 1, 12, 8, 4, 15, 12, 5, 4)]
    [InlineData("short-miter", 6, 8, 8, 4, 10, 8, 8, 10)]
    public void Original_compiled_native_miter_and_normalization_points_match(string id, double ax, double ay, double bx, double by, double cx, double cy, double width, double limit)
    {
        string[][] rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "TestData", "stroke-miter-native.csv"))
            .Skip(1).Select(line => line.Split(',')).Where(fields => fields[0] == id).ToArray();
        Assert.InRange(rows.Length, 8, 9);
        SvgDevicePoint[] expected = rows.Where(fields => fields[1] == "outer")
            .Concat(rows.Where(fields => fields[1] == "inner").Reverse()).Select(Point).ToArray();
        SvgContourBuilder builder = new(32, 1);
        SvgStrokeOutline.StrokeNativeOpenPolyline([new(ax, ay), new(bx, by), new(cx, cy)], width, limit, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None);
        Assert.Equal(1, builder.ContourCount);
        Assert.Equal(expected, builder.GetContour(0).ToArray());

        static SvgDevicePoint Point(string[] fields)
            => new(BitConverter.Int32BitsToSingle(unchecked((int)uint.Parse(fields[5], CultureInfo.InvariantCulture))), BitConverter.Int32BitsToSingle(unchecked((int)uint.Parse(fields[6], CultureInfo.InvariantCulture))));
    }

    [Fact]
    public void Native_winding_overlap_is_nonconvex_and_preserves_the_stroke_hole()
    {
        SvgContourBuilder builder = new(32, 1);
        SvgStrokeOutline.StrokeNativeOpenPolyline([new(8, 40), new(32, 8), new(56, 40)], 8, 10, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None);
        SvgDevicePoint[] points = builder.GetContour(0).ToArray();
        SvgAnalyticContourPreparation preparation = new(points, [points.Length], [], false, null, SvgPolygonMapping.NativeStrokeEdges, [], [], new(4, 1, 60, 43), 64, 64);
        RenderBudget budget = new(1_000_000);
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), null, SvgAnalyticPaintClip.Binary, 1_000_000, budget, out long _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.Equal(SvgContourFlags.ProperCrossing, preparation.Structure);
            Assert.Equal(false, preparation.Convex);
            byte[] pixel = new byte[4];
            walker.PrepareRow(20, 1_000_000, out long _, CancellationToken.None);
            walker.PaintPixel(20, pixel, SvgAnalyticPaint.Solid(new(0, 0, 0, 255)), SvgAnalyticClipCoverage.Full);
            Assert.Equal(255, pixel[3]);
            Array.Clear(pixel);
            walker.PaintPixel(32, pixel, SvgAnalyticPaint.Solid(new(0, 0, 0, 255)), SvgAnalyticClipCoverage.Full);
            Assert.Equal(0, pixel[3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(nameof(SvgPolygonMapping.FiniteEdges))]
    [InlineData(nameof(SvgPolygonMapping.QuarterEdges))]
    public void Ordinary_straight_polygon_crossing_guards_remain(string mappingName)
    {
        SvgPolygonMapping mapping = Enum.Parse<SvgPolygonMapping>(mappingName);
        SvgDevicePoint[] points = [new(8, 4), new(14, 12), new(2, 12), new(12, 4), new(4, 12)];
        SvgAnalyticContourPreparation preparation = new(points, [5], [], false, null, mapping, [], [], new(0, 0, 16, 16), 16, 16);
        RenderBudget budget = new(1_000_000);
        using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 16, 0, 16, new(0, 16), null, SvgAnalyticPaintClip.Binary, 1_000_000, budget, out long _, out bool eligible, CancellationToken.None);
        Assert.False(eligible);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Float_collapsed_vertices_keep_a_band_and_cancellation_is_observed()
    {
        SvgDevicePoint[] points = [new(32, 4), new(32.00000001, 4), new(40, 4)];
        SvgContourBuilder builder = new(16, 1);
        SvgStrokeOutline.StrokeNativeOpenPolyline(points, 2, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None);
        Assert.Equal(new SvgDevicePoint[] { new(32, 3), new(40, 3), new(40, 5), new(32, 5) }, builder.GetContour(0).ToArray());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => SvgStrokeOutline.StrokeNativeOpenPolyline(points, 2, 4, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, new(16, 1), cancellation.Token));
    }

    [Fact]
    public void Round_caps_do_not_invent_zero_length_edges_or_self_touches()
    {
        SvgContourBuilder builder = new(64, 1);
        SvgStrokeOutline.StrokeNativeOpenPolyline([new(2, 14), new(8, 2), new(14, 14)], 4, 4, SvgStrokeCap.Round, SvgStrokeJoin.Round, builder, CancellationToken.None);
        SvgDevicePoint[] points = builder.GetContour(0).ToArray();
        Assert.Equal(SvgContourFlags.ProperCrossing, SvgContourStructure.ClassifyContour(points, 0, points.Length, CancellationToken.None));
        for (int index = 0; index < points.Length; index++) Assert.NotEqual(points[index], points[(index + 1) % points.Length]);
    }

    [Fact]
    public void Cached_native_stroke_setup_releases_storage_on_work_memory_and_cancellation_denial()
    {
        SvgContourBuilder builder = new(32, 1);
        SvgStrokeOutline.StrokeNativeOpenPolyline([new(8, 40), new(32, 8), new(56, 40)], 8, 10, SvgStrokeCap.Butt, SvgStrokeJoin.Miter, builder, CancellationToken.None);
        SvgDevicePoint[] points = builder.GetContour(0).ToArray();
        SvgAnalyticContourPreparation preparation = new(points, [points.Length], [], false, null, SvgPolygonMapping.NativeStrokeEdges, [], [], new(4, 1, 60, 43), 64, 64);
        RenderBudget budget = new(1_000_000);
        long setup;
        long storage;
        {
            using SvgAnalyticCoverage warm = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), null, SvgAnalyticPaintClip.Binary, 1_000_000, budget, out long _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
        }
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), null, SvgAnalyticPaintClip.Binary, 1_000_000, budget, out setup, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            storage = budget.Snapshot().CurrentBytes;
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        try
        {
            using SvgAnalyticCoverage denied = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), null, SvgAnalyticPaintClip.Binary, setup - 1, budget, out long _, out bool _, CancellationToken.None);
            Assert.Fail("Native stroke construction must fit the remaining setup work.");
        }
        catch (SvgException exception) { Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode); }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget tight = new(storage - 1);
        try
        {
            using SvgAnalyticCoverage denied = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), null, SvgAnalyticPaintClip.Binary, 1_000_000, tight, out long _, out bool _, CancellationToken.None);
            Assert.Fail("Native stroke setup must release earlier storage when a later reservation fails.");
        }
        catch (SvgException exception) { Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode); }
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            using SvgAnalyticCoverage canceled = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), null, SvgAnalyticPaintClip.Binary, 1_000_000, budget, out long _, out bool _, cancellation.Token);
            Assert.Fail("Cached native stroke setup must still observe the caller's cancellation.");
        }
        catch (OperationCanceledException exception) { Assert.Equal(cancellation.Token, exception.CancellationToken); }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
