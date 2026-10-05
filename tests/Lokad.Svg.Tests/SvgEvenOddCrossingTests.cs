using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgEvenOddCrossingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Zero_signed_area_crossings_keep_both_parity_lobes_and_release_storage(bool multiple)
    {
        SvgDevicePoint[] points = multiple
            ? [new(1, 1), new(7, 7), new(7, 1), new(1, 7), new(9, 1), new(15, 7), new(15, 1), new(9, 7)]
            : [new(1, 1), new(7, 7), new(7, 1), new(1, 7)];
        int[] ends = multiple ? [4, 8] : [4];
        int[] votes = multiple ? [1, 1] : [1];
        RenderBudget budget = new(1_000_000);
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(points, ends, votes, true, null, SvgPolygonMapping.FiniteEdges, [], [], 0, 16, 0, 16, 16, 16, new(0, 16), null, 1_000_000, budget, out long setup, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.True(setup > 0);
            walker.PrepareRow(2, out long _, CancellationToken.None);
            Assert.Equal((byte)255, walker.Row[6]);
            Assert.Equal((byte)0, walker.Row[4]);
            walker.PrepareRow(5, out long _, CancellationToken.None);
            Assert.Equal((byte)255, walker.Row[1]);
            if (multiple) Assert.Equal((byte)255, walker.Row[9]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.True(budget.Snapshot().PeakBytes > 0);
    }

    [Fact]
    public void Crossing_preparation_work_memory_denial_and_cancellation_release_ownership()
    {
        SvgDevicePoint[] points = [new(1, 1), new(7, 7), new(7, 1), new(1, 7), new(9, 1), new(15, 7), new(15, 1), new(9, 7)];
        RenderBudget success = new(1_000_000);
        long setup;
        long storage;
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(points, [4, 8], [1, 1], true, null, SvgPolygonMapping.FiniteEdges, [], [], 0, 16, 0, 16, 16, 16, new(0, 16), new(new(0, 0, 16, 16)), 1_000_000, success, out setup, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            storage = success.Snapshot().CurrentBytes;
        }
        RenderBudget workDenied = new(1_000_000);
        SvgException work = Assert.Throws<SvgException>(() =>
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(points, [4, 8], [1, 1], true, null, SvgPolygonMapping.FiniteEdges, [], [], 0, 16, 0, 16, 16, 16, new(0, 16), new(new(0, 0, 16, 16)), setup - 1, workDenied, out long _, out bool _, CancellationToken.None);
        });
        Assert.Equal(SvgErrorCode.LimitExceeded, work.ErrorCode);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        RenderBudget memoryDenied = new(storage - 1);
        SvgException memory = Assert.Throws<SvgException>(() =>
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(points, [4, 8], [1, 1], true, null, SvgPolygonMapping.FiniteEdges, [], [], 0, 16, 0, 16, 16, 16, new(0, 16), new(new(0, 0, 16, 16)), 1_000_000, memoryDenied, out long _, out bool _, CancellationToken.None);
        });
        Assert.Equal(SvgErrorCode.LimitExceeded, memory.ErrorCode);
        Assert.Equal(0, memoryDenied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new(1_000_000);
        OperationCanceledException exception = Assert.Throws<OperationCanceledException>(() =>
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(points, [4, 8], [1, 1], true, null, SvgPolygonMapping.FiniteEdges, [], [], 0, 16, 0, 16, 16, 16, new(0, 16), new(new(0, 0, 16, 16)), 1_000_000, cancelled, out long _, out bool _, cancellation.Token);
        });
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);
        Assert.Equal(0, cancelled.Snapshot().PeakBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Terminal_axis_retrace_retains_raw_geometry_and_still_rejects_nonadjacent_contacts(bool vertical)
    {
        SvgDevicePoint[] points = [new(16.25, 12.25), new(16.25, 60.25), new(44.25, 42.25), new(76.25, 42.25), new(76.25, 12.25), new(16.249998092651367, 12.25)];
        if (vertical) points = points.Select(p => new SvgDevicePoint(p.Y, p.X)).ToArray();
        SvgDevicePoint[] original = points.ToArray();
        Assert.Equal(SvgContourFlags.None, SvgContourStructure.ClassifyContour(points, 0, points.Length, CancellationToken.None));
        Assert.Equal(original, points);
        SvgDevicePoint[] contact = [new(0, 0), new(4, 4), new(0, 8), new(8, 8), new(4, 4), new(8, 0)];
        Assert.Equal(SvgContourFlags.SelfTouch, SvgContourStructure.ClassifyContour(contact, 0, contact.Length, CancellationToken.None));
    }

    [Fact]
    public async Task Parity_crossings_are_consistent_across_all_six_output_apis()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path fill-rule='evenodd' d='M1 1L7 7L7 1L1 7Z M9 1L15 7L15 1L9 7Z'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        var polygon = Assert.Single(root.Geometries).Polygon;
        Assert.Equal(8, polygon.Points.Length);
        Assert.Equal(new[] { 4, 8 }, polygon.ContourEnds);
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
