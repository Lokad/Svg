using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgOneChordQuadTests
{
    [Theory]
    [InlineData(false, false, 4, 138)]
    [InlineData(false, true, 4, 138)]
    [InlineData(true, false, 16, 124)]
    [InlineData(true, true, 16, 124)]
    public void Authored_quadratics_keep_browser_coverage_with_one_flattened_chord(bool pair, bool evenOdd, int x, byte alpha)
    {
        string path = pair
            ? "M16.25 8.25 Q17.14 8.42 17.85 8.68 V24.25 H20.25 V30.25 H16.25 Z M40.25 8.25 Q41.14 8.42 41.85 8.68 V24.25 H44.25 V30.25 H40.25 Z"
            : "M4.125 8.125 Q5.015 8.295 5.725 8.555 V24.125 H8.125 V30.125 H4.125 Z";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='72' height='48'><path fill='#406080' fill-rule='" + (evenOdd ? "evenodd" : "nonzero") + "' d='" + path + "'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
        Assert.Equal(pair ? 2 : 1, polygon.QuadSpans.Length);
        foreach (SvgQuadSpan span in polygon.QuadSpans) Assert.Equal(1, span.EndIndex - span.StartIndex);
        int[] votes = Enumerable.Repeat(1, polygon.ContourEnds.Length).ToArray();
        SvgAnalyticContourPreparation preparation = new(polygon.Points, polygon.ContourEnds, votes, evenOdd, null, SvgPolygonMapping.NativeFillEdges, polygon.QuadSpans, [], new(x, 8, pair ? 45 : 9, 31), 72, 48);
        RenderBudget budget = new(1_000_000);
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 72, 8, 31, new(0, 72), null, SvgAnalyticPaintClip.Binary, 1_000_000, budget, out long _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            walker.PrepareRow(8, out long _, CancellationToken.None);
            byte[] pixel = new byte[4];
            walker.PaintPixel(x, pixel, SvgAnalyticPaint.Solid(polygon.Color), SvgAnalyticClipCoverage.Full);
            // Stable pinned-browser controls cover the single and paired shoulders.
            Assert.Equal(alpha, pixel[3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Short_quad_setup_denials_and_cancellation_release_owners()
    {
        SvgDevicePoint[] points = [new(4.125, 8.125), new(5.725, 8.555), new(5.725, 24.125), new(8.125, 24.125), new(8.125, 30.125), new(4.125, 30.125)];
        SvgAnalyticContourPreparation preparation = new(points, [6], [], false, null, SvgPolygonMapping.NativeFillEdges, [new(0, 0, 1, new(5.015, 8.295))], [], new(4, 8, 9, 31), 72, 48);
        RenderBudget budget = new(1_000_000);
        long bytes, work;
        {
            using SvgAnalyticCoverage warm = SvgAnalyticCoverage.Prepare(ref preparation, 0, 72, 8, 31, new(0, 72), null, SvgAnalyticPaintClip.Antialiased, 1_000_000, budget, out long _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
        }
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 72, 8, 31, new(0, 72), null, SvgAnalyticPaintClip.Antialiased, 1_000_000, budget, out work, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            bytes = budget.Snapshot().CurrentBytes;
        }
        Assert.True(bytes > 0);
        RenderBudget denied = new(bytes - 1);
        try
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 72, 8, 31, new(0, 72), null, SvgAnalyticPaintClip.Antialiased, work, denied, out long _, out bool _, CancellationToken.None);
            Assert.Fail("Short-quad storage exceeded the budget.");
        }
        catch (SvgException error) { Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode); }
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        try
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 72, 8, 31, new(0, 72), null, SvgAnalyticPaintClip.Antialiased, work - 1, budget, out long _, out bool _, CancellationToken.None);
            Assert.Fail("Short-quad setup exceeded remaining work.");
        }
        catch (SvgException error) { Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode); }
        {
            using CancellationTokenSource cancellation = new();
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 72, 8, 31, new(0, 72), null, SvgAnalyticPaintClip.Antialiased, work, budget, out long _, out bool eligible, cancellation.Token);
            Assert.True(eligible);
            walker.PrepareRow(8, out long _, cancellation.Token);
            cancellation.Cancel();
            try { walker.PrepareRow(9, out long _, cancellation.Token); Assert.Fail("Canceled short-quad row succeeded."); }
            catch (OperationCanceledException error) { Assert.Equal(cancellation.Token, error.CancellationToken); }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("one-chord-quad-shoulders72.svg")]
    [InlineData("one-chord-quad-shoulders144.svg")]
    public async Task Six_outputs_agree_for_short_quadratics_and_both_fill_rules(string source)
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
}
