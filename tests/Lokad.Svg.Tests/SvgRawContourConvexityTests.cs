using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRawContourConvexityTests
{
    private static readonly SvgDevicePoint[] SampledRing = [new(10, 10), new(20, 5), new(30, 5), new(40, 10), new(40, 40), new(10, 40)];
    private static readonly SvgCubicSpan[] Cubics = [new(0, 0, 2, new(11, 9), new(12, 10))];

    [Fact]
    public void Compiled_native_sign_turn_and_reversal_vectors_match()
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "raw-convexity-native.json")));
        Assert.Equal("f8b66b7597c4cc859d3ed190e9c6872241e6721c", data.RootElement.GetProperty("nativePin").GetString());
        JsonElement[] vectors = data.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        Assert.Equal(75, vectors.Length);
        Assert.Equal(30, vectors.Count(vector => vector.GetProperty("expectedConvex").GetBoolean()));
        foreach (JsonElement vector in vectors)
        {
            SvgDevicePoint[] points = vector.GetProperty("points").EnumerateArray().Select(point => new SvgDevicePoint(point[0].GetDouble(), point[1].GetDouble())).ToArray();
            bool expected = vector.GetProperty("expectedConvex").GetBoolean();
            Assert.True(expected == SvgRawContourConvexity.IsConvex(points, [], [], CancellationToken.None), vector.GetProperty("id").GetString());
        }
    }

    [Fact]
    public void Retained_controls_replace_samples_and_chain_across_the_implicit_close()
    {
        SvgDevicePoint[] points = [new(4, 4), new(16, 2), new(28, 4), new(28, 28), new(16, 30), new(4, 28)];
        SvgQuadSpan[] quads = [new(0, 0, 2, new(16, 0))];
        SvgCubicSpan[] cubics = [new(0, 3, 5, new(28, 32), new(4, 32)), new(0, 5, 6, new(4, 20), new(4, 12))];
        Assert.True(SvgRawContourConvexity.IsConvex(points, quads, cubics, CancellationToken.None));
        Assert.True(SvgRawContourConvexity.IsConvex(SampledRing, [], [], CancellationToken.None));
        Assert.False(SvgRawContourConvexity.IsConvex(SampledRing, [], Cubics, CancellationToken.None));
        using SvgRoot scene = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path d='M4 4C4 0 28 0 28 4V28C28 32 4 32 4 28Z' fill='#46596d'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        var polygon = Assert.Single(scene.Geometries).Polygon;
        SvgAnalyticContourPreparation preparation = new(polygon.Points, polygon.ContourEnds, [], false, null, polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, new(0, 0, 64, 64), 64, 64);
        RenderBudget budget = new(1_000_000);
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), new(new(0, 0, 64, 64)), 1_000_000, budget, out long _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.True(preparation.UseCurveSpans);
            Assert.Equal(true, preparation.Convex);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Classification_work_is_charged_once_and_cached_across_contexts()
    {
        SvgAnalyticContourPreparation preparation = new(SampledRing, [6], [], false, null, SvgPolygonMapping.FlattenedCurves, [], Cubics, new(0, 0, 64, 64), 64, 64);
        RenderBudget budget = new(1_000_000);
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), null, 1_000_000, budget, out long _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.Equal(false, preparation.Convex);
            Assert.True(preparation.UseCurveSpans);
        }
        long cachedWork;
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), null, 1_000_000, budget, out cachedWork, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
        }
        preparation.Convex = null;
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), null, 1_000_000, budget, out long repeatedWork, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.Equal(SampledRing.Length * 2L + Cubics.Length * 2L + 2, repeatedWork - cachedWork);
        }
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), new(new(0, 8, 64, 40)), 1_000_000, budget, out long _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.Equal(false, preparation.Convex);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Raw_classification_denial_precedes_storage_and_leaves_the_cache_unset()
    {
        SvgAnalyticContourPreparation preparation = new(SampledRing, [6], [], false, null, SvgPolygonMapping.FlattenedCurves, [], Cubics, new(0, 0, 64, 64), 64, 64);
        RenderBudget success = new(1_000_000);
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), null, 1_000_000, success, out long _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
        }
        preparation.Convex = null;
        RenderBudget denied = new(1_000_000);
        try
        {
            long classificationWork = SampledRing.Length * 2L + Cubics.Length * 2L + 2;
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), null, classificationWork - 1, denied, out long _, out bool _, CancellationToken.None);
            Assert.Fail("The raw-control traversal must observe its remaining work limit.");
        }
        catch (SvgException exception) { Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode); }
        Assert.Null(preparation.Convex);
        Assert.Equal(0, denied.Snapshot().PeakBytes);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Classification_preserves_the_cancelled_token()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException exception = Assert.Throws<OperationCanceledException>(() => SvgRawContourConvexity.IsConvex(SampledRing, [], Cubics, cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Theory]
    [InlineData("solid")]
    [InlineData("linear")]
    [InlineData("radial")]
    [InlineData("pattern")]
    public async Task Six_outputs_preserve_raw_control_dispatch_across_four_paints(string material)
    {
        string definitions = "<defs><linearGradient id='linear'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><radialGradient id='radial'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient><pattern id='pattern' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='4' height='4' fill='red'/></pattern></defs>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64' viewBox='0 0 32 32'>" + definitions + "<path d='M4 4C4 2 8 1 12 1C20 .9998 27 3 28 12C29 24 16 30 6 27C2 24 1 12 4 4Z' fill='" + (material == "solid" ? "#46596d" : "url(#" + material + ")") + "'/></svg>");
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        byte[] asyncDestination = new byte[expected.Length];
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncDestination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncDestination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }
}
