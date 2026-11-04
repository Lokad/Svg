using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAnalyticStorageWindowTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(-2, -1)]
    [InlineData(1, -1)]
    [InlineData(9, 9)]
    public void Signed_bitmap_origins_preserve_exact_integer_translation_of_representable_edges(int x, int y)
    {
        SvgDevicePoint[] points = [new(.25, .5), new(6.25, .5), new(6.25, 6.5), new(.25, 6.5)];
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        byte[] expected = Render(points, new(0, 0), budget);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Contains(expected, value => value != 0);
        Assert.Equal(expected, Render(points, new(x, y), budget));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static byte[] Render(SvgDevicePoint[] vertices, SvgCpuBitmapOrigin origin, RenderBudget renderBudget)
        {
            SvgAnalyticContourPreparation preparation = new(vertices, [4], [], false, null, SvgPolygonMapping.NativeFillEdges,
                [], [], new(0, 0, 7, 7), 8, 8);
            using SvgAnalyticCoverage coverage = SvgAnalyticCoverage.Prepare(ref preparation,
                0, 8, 0, 8, new(0, 8), origin, null, SvgAnalyticPaintClip.Binary, renderBudget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            byte[] pixels = new byte[8 * 8 * 4];
            for (int row = 0; row < 8; row++)
            {
                coverage.PrepareRow(row, CancellationToken.None);
                for (int column = 0; column < 8; column++)
                    coverage.PaintPixel(column, pixels.AsSpan((row * 8 + column) * 4, 4),
                        SvgAnalyticPaint.Solid(new(255, 255, 255, 255)), SvgAnalyticClipCoverage.Full);
            }
            return pixels;
        }
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(0, 11, 7, 182)]
    [InlineData(1, 11, 7, 182)]
    [InlineData(1, 2, 7, 182)]
    [InlineData(0, 11, 19, 75)]
    [InlineData(1, 11, 19, 75)]
    [InlineData(1, 2, 19, 75)]
    public void Owned_row_storage_does_not_cut_off_the_bitmap_border(int firstStoredColumn, int lastStoredColumn, int row, byte nativeAlpha)
    {
        // Independent native vectors: Research/affine-filter-capture/edge-capture.json.
        SvgDevicePoint[] points = CreatePoints();
        SvgAnalyticContourPreparation preparation = new(points, [4], [], false, null, SvgPolygonMapping.NativeFillEdges,
            [], [], new(-15, 7, 10, 20), 256, 256);
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgAnalyticCoverage coverage = SvgAnalyticCoverage.Prepare(ref preparation,
            firstStoredColumn, lastStoredColumn, 6, 21, new(0, 256), new SvgAnalyticClipContext(new(0, 0, 256, 256)),
            SvgAnalyticPaintClip.Binary, budget, out bool eligible, CancellationToken.None))
        {
            Assert.True(eligible);
            coverage.PrepareRow(row, CancellationToken.None);
            byte[] pixel = new byte[4];
            coverage.PaintPixel(1 - firstStoredColumn, pixel, SvgAnalyticPaint.Solid(new(255, 255, 255, 255)), SvgAnalyticClipCoverage.Full);
            Assert.Equal(nativeAlpha, pixel[3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Narrow_row_reserves_complete_span_scratch_releases_refusal_and_can_retry()
    {
        RenderBudget accepted = new(new HostMemoryBudget(1_000_000));
        byte expected = Run(accepted);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        RenderBudget denied = new(new HostMemoryBudget(accepted.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Run(denied)).ErrorCode);
        Assert.Equal(1, denied.Snapshot().DeniedReservationCount);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        Assert.Equal(expected, Run(accepted));
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);

        static byte Run(RenderBudget budget)
        {
            SvgDevicePoint[] points = CreatePoints();
            SvgAnalyticContourPreparation preparation = new(points, [4], [], false, null, SvgPolygonMapping.NativeFillEdges,
                [], [], new(-15, 7, 10, 20), 256, 256);
            using SvgAnalyticCoverage coverage = SvgAnalyticCoverage.Prepare(ref preparation,
                1, 2, 6, 21, new(0, 256), new SvgAnalyticClipContext(new(0, 0, 256, 256)),
                SvgAnalyticPaintClip.Binary, budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.Equal(1, coverage.Row.Length);
            coverage.PrepareRow(7, CancellationToken.None);
            byte[] pixel = new byte[4];
            coverage.PaintPixel(0, pixel, SvgAnalyticPaint.Solid(new(255, 255, 255, 255)), SvgAnalyticClipCoverage.Full);
            return pixel[3];
        }
    }

    private static SvgDevicePoint[] CreatePoints() =>
    [
        new(-14.496488571166992, 7.065484046936035),
        new(9.499044418334961, 7.528508186340332),
        new(9.231149673461914, 19.525516510009766),
        new(-14.764383316040039, 19.06249237060547)
    ];
}
