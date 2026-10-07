using System.Globalization;
using System.Text.Json;
using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRoundStrokeConicTests
{
    [Theory]
    [InlineData("cap-even")]
    [InlineData("cap-fractional")]
    [InlineData("cap-unit")]
    [InlineData("cap-diagonal")]
    [InlineData("cap-vertical")]
    [InlineData("cap-offset")]
    [InlineData("cap-dot")]
    [InlineData("join-right")]
    [InlineData("join-left")]
    [InlineData("join-diagonal")]
    [InlineData("join-diagonal-reverse")]
    [InlineData("join-acute")]
    [InlineData("join-obtuse")]
    [InlineData("join-reversal")]
    [InlineData("join-near-quarter")]
    [InlineData("join-near-quarter-reverse")]
    public void Original_native_round_controls_endpoints_and_weights_match_every_float_bit(string id)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestData");
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "stroke-round-native.json")));
        JsonElement item = metadata.RootElement.GetProperty("cases").EnumerateArray().Single(value => value.GetProperty("id").GetString() == id);
        double[] p = item.GetProperty("p").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        bool cap = item.GetProperty("kind").GetString() == "cap";
        SvgDevicePoint[] input = cap ? [new(p[0], p[1]), new(p[2], p[3])] : [new(p[0], p[1]), new(p[2], p[3]), new(p[4], p[5])];
        SvgContourBuilder builder = new(1024, 1);
        builder.EnableQuadSpanRetention();
        SvgStrokeOutline.StrokeNativeOpenPolyline(input, p[^1], 4, cap ? SvgStrokeCap.Round : SvgStrokeCap.Butt, cap ? SvgStrokeJoin.Miter : SvgStrokeJoin.Round, builder, CancellationToken.None);
        SvgQuadSpan[] spans = builder.GetQuadSpans();
        SvgDevicePoint[] points = builder.GetContour(0).ToArray();
        string[][] rows = File.ReadAllLines(Path.Combine(directory, "stroke-round-native.csv")).Skip(1).Select(line => line.Split(',')).Where(row => row[0] == id).ToArray();
        Assert.Equal(2 * spans.Length, rows.Length);
        for (int index = 0; index < spans.Length; index++)
        {
            SvgQuadSpan span = spans[index];
            AssertPoint(rows[2 * index], span.Control, span.ConicWeight);
            AssertPoint(rows[2 * index + 1], points[span.EndIndex], span.ConicWeight);
        }

        static void AssertPoint(string[] row, SvgDevicePoint point, float weight)
        {
            Assert.Equal(uint.Parse(row[3], CultureInfo.InvariantCulture), BitConverter.SingleToUInt32Bits((float)point.X));
            Assert.Equal(uint.Parse(row[4], CultureInfo.InvariantCulture), BitConverter.SingleToUInt32Bits((float)point.Y));
            Assert.Equal(uint.Parse(row[5], CultureInfo.InvariantCulture), BitConverter.SingleToUInt32Bits(weight));
        }
    }

    [Theory]
    [InlineData("<path d='M2,16 H30' fill='none' stroke='red' stroke-width='2.5' stroke-linecap='round'/>")]
    [InlineData("<path d='M28,25 L16,7 L4,25' fill='none' stroke='red' stroke-width='5.25' stroke-linejoin='round' stroke-opacity='.61'/>")]
    [InlineData("<defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='16' cy='16' r='16'><stop stop-color='yellow'/><stop offset='1' stop-color='blue'/></radialGradient></defs><path d='M.125,16 h0 M21,16 h0' fill='none' stroke='url(#g)' stroke-width='13.25' stroke-linecap='round'/>")]
    public async Task Retained_round_strokes_have_six_output_parity(string body)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
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
    [InlineData("<g transform='translate(.375 .625)'><path d='M4,16 H28' fill='none' stroke='red' stroke-width='5.25' stroke-linecap='round'/></g>")]
    [InlineData("<defs><symbol id='s' viewBox='0 0 32 32'><path d='M4,16 H28' fill='none' stroke='red' stroke-width='5.25' stroke-linecap='round'/></symbol></defs><use href='#s' width='32' height='32'/>")]
    public void Round_controls_survive_group_and_symbol_placement(string body)
    {
        using SvgRoot root = SvgRootParser.Parse(TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>"), SvgRenderLimits.Default, CancellationToken.None);
        SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
        Assert.Equal(SvgPolygonMapping.NativeStrokeEdges, polygon.Mapping);
        Assert.Equal(4, polygon.QuadSpans.Length);
        Assert.All(polygon.QuadSpans, span => Assert.Equal(SvgRationalQuadratic.QuarterCircleWeight, span.ConicWeight));
    }

    [Theory]
    [InlineData("M8,8 h0", 1, 4)]
    [InlineData("M8,8 h0 M8,8 h0", 1, 4)]
    [InlineData("M8,8 h0 M8,8 h0 M24,8 h0", 2, 8)]
    public void Scene_dot_merging_preserves_retained_verbs_and_global_indices(string path, int contours, int verbs)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='16'><path d='" + path + "' fill='none' stroke='red' stroke-width='3.5' stroke-linecap='round'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
        Assert.Equal(contours, polygon.ContourEnds.Length);
        Assert.Equal(verbs, polygon.QuadSpans.Length);
        foreach (SvgQuadSpan span in polygon.QuadSpans)
        {
            int start = span.Contour == 0 ? 0 : polygon.ContourEnds[span.Contour - 1];
            Assert.InRange(span.StartIndex, start, polygon.ContourEnds[span.Contour] - 2);
            Assert.InRange(span.EndIndex, span.StartIndex + 1, polygon.ContourEnds[span.Contour] - 1);
        }
        // Scene fallback can merge identical contours while output replay keeps
        // the generated outline. Native single/duplicate dot pixels differ;
        // the four native-stroke-source-dot fixtures cover that distinction.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Multiple_round_contours_cache_native_counts_and_release_on_denial_or_cancellation(bool clipped)
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='16'><path d='M8,8 h0 M24,8 h0' fill='none' stroke='red' stroke-width='3.5' stroke-linecap='round'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
        SvgAnalyticContourPreparation preparation = new(polygon.Points, polygon.ContourEnds, polygon.Signs ?? [], false, polygon.Signs, polygon.Mapping, polygon.QuadSpans, [], new(6, 6, 26, 10), 32, 16);
        SvgAnalyticClipContext? clip = clipped ? new(new(0, 0, 32, 16)) : null;
        RenderBudget budget = new(1_000_000);
        long firstWork;
        using (SvgAnalyticCoverage first = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 0, 16, new(0, 16), clip, SvgAnalyticPaintClip.Binary, 1_000_000, budget, out firstWork, out bool eligible, CancellationToken.None))
        {
            Assert.True(eligible);
            Assert.Equal(18L, preparation.NativePointCount);
            first.PrepareRow(8, out long rowWork, CancellationToken.None);
            Assert.True(rowWork > 0);
        }
        long setup;
        using (SvgAnalyticCoverage cached = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 0, 16, new(0, 16), clip, SvgAnalyticPaintClip.Binary, 1_000_000, budget, out setup, out bool eligible, CancellationToken.None))
        { Assert.True(eligible); Assert.True(setup < firstWork); }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        try
        {
            using SvgAnalyticCoverage denied = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 0, 16, new(0, 16), clip, SvgAnalyticPaintClip.Binary, setup - 1, budget, out long _, out bool _, CancellationToken.None);
            Assert.Fail("Retained stroke setup must respect its work allowance.");
        }
        catch (SvgException exception) { Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode); }
        RenderBudget tight = new(1);
        try
        {
            using SvgAnalyticCoverage denied = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 0, 16, new(0, 16), clip, SvgAnalyticPaintClip.Binary, 1_000_000, tight, out long _, out bool _, CancellationToken.None);
            Assert.Fail("Retained stroke edge storage must be reserved before allocation.");
        }
        catch (SvgException exception) { Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode); }
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            using SvgAnalyticCoverage canceled = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 0, 16, new(0, 16), clip, SvgAnalyticPaintClip.Binary, 1_000_000, budget, out long _, out bool _, cancellation.Token);
            Assert.Fail("Retained stroke caches must observe cancellation.");
        }
        catch (OperationCanceledException exception) { Assert.Equal(cancellation.Token, exception.CancellationToken); }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("M4,16 L28,16 L4,16")]
    [InlineData("M4,16 L28,16 M12,16 L28,16")]
    public void Native_round_stroke_contacts_keep_signed_winding_analytic_coverage(string path)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><path d='" + path + "' fill='none' stroke='red' stroke-width='6.25' stroke-linejoin='round' stroke-linecap='round'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
        Assert.NotEmpty(polygon.QuadSpans);
        SvgAnalyticContourPreparation preparation = new(polygon.Points, polygon.ContourEnds, polygon.Signs ?? [], false, polygon.Signs, polygon.Mapping, polygon.QuadSpans, [], new(0, 12, 32, 20), 32, 32);
        RenderBudget budget = new(1_000_000);
        {
            using SvgAnalyticCoverage coverage = SvgAnalyticCoverage.Prepare(ref preparation, 0, 32, 0, 32, new(0, 32), null, SvgAnalyticPaintClip.Binary, 1_000_000, budget, out long _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            coverage.PrepareRow(16, out long _, CancellationToken.None);
            byte[] pixel = new byte[4];
            coverage.PaintPixel(16, pixel, SvgAnalyticPaint.Solid(new(255, 0, 0, 255)), SvgAnalyticClipCoverage.Full);
            Assert.Equal(255, pixel[3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
