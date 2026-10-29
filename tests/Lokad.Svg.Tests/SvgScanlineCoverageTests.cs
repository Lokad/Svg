using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgScanlineCoverageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Denied_scratch_storage_does_not_allocate_contour_arrays(bool voted)
    {
        const int count = 100_000;
        SvgDevicePoint[] points = new SvgDevicePoint[count * 3];
        int[] ends = new int[count];
        int[] roles = new int[count];
        for (int index = 0; index < count; index++)
        {
            points[index * 3] = new(0, 0); points[index * 3 + 1] = new(1, 0); points[index * 3 + 2] = new(0, 1);
            ends[index] = index * 3 + 3; roles[index] = 1;
        }
        _ = Measure(points.AsSpan(0, 3), ends.AsSpan(0, 1), voted ? [1] : null);
        _ = Measure(points, ends, voted ? roles : null);
        long small = Measure(points.AsSpan(0, 3), ends.AsSpan(0, 1), voted ? [1] : null);
        long large = Measure(points, ends, voted ? roles : null);
        Assert.InRange(large, 0, small + 4096);

        static long Measure(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> ends, int[]? roles)
        {
            long allocated = long.MaxValue;
            for (int sample = 0; sample < 3; sample++)
            {
                RenderBudget budget = new RenderBudget(new HostMemoryBudget(1));
                SvgException? failure = null;
                long before = GC.GetAllocatedBytesForCurrentThread();
                try
                {
                    using SvgScanlineCoverage scan = SvgScanlineCoverage.Prepare(points, ends, [], false, roles, 0, 1, budget, CancellationToken.None);
                }
                catch (SvgException exception) { failure = exception; }
                allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
                // Assertions are outside the measured interval, and every sample validates denial.
                Assert.Equal(SvgErrorCode.LimitExceeded, failure?.ErrorCode);
                Assert.Equal(0, budget.Snapshot().PeakBytes);
                Assert.Equal(0, budget.Snapshot().CurrentBytes);
                Assert.Equal(1, budget.Snapshot().DeniedReservationCount);
            }
            return allocated;
        }
    }

    [Fact]
    public void Skipped_pixels_match_zero_fan_total_on_simple_triangle()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(2, 2), new SvgDevicePoint(14, 2), new SvgDevicePoint(2, 14)];
        int[] ends = [3];
        int[] roles = [1];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using SvgScanlineCoverage scan = SvgScanlineCoverage.Prepare(points, ends, roles, false, roles, -2, 18, budget, CancellationToken.None);

        int culled = 0;
        for (int y = -2; y < 18; y++)
        {
            scan.PrepareRow(y, CancellationToken.None);
            for (int x = -2; x < 18; x++)
            {
                double fan = SvgPolygonCoverage.GetWindingCoverage(points, ends, roles, x, y, CancellationToken.None);
                if (!scan.MightCover(x)) { Assert.True(Math.Abs(fan) < 0.000000001, "Culled pixel carries fan coverage."); culled++; }
            }
        }

        Assert.True(culled > 0, "Sparse triangle must cull background pixels.");
    }

    [Fact]
    public void Skipped_pixels_match_zero_fan_total_on_evenodd_hole()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(16, 0), new SvgDevicePoint(16, 16), new SvgDevicePoint(0, 16), new SvgDevicePoint(5, 5), new SvgDevicePoint(11, 5), new SvgDevicePoint(11, 11), new SvgDevicePoint(5, 11)];
        int[] ends = [4, 8];
        int[] signs = [1, -1];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using SvgScanlineCoverage scan = SvgScanlineCoverage.Prepare(points, ends, signs, true, signs, -2, 18, budget, CancellationToken.None);

        int evaluatedHole = 0;
        for (int y = -2; y < 18; y++)
        {
            scan.PrepareRow(y, CancellationToken.None);
            for (int x = -2; x < 18; x++)
            {
                double fan = SvgPolygonCoverage.GetEvenOddCoverage(points, ends, signs, x, y, CancellationToken.None);
                if (!scan.MightCover(x)) Assert.True(Math.Abs(fan) < 0.000000001, "Culled pixel carries fan coverage.");
                else if (x >= 5 && x < 11 && y >= 5 && y < 11) evaluatedHole++;
            }
        }

        Assert.True(evaluatedHole > 0, "Hole interior evaluates through the fan path.");
    }

    [Fact]
    public void Touching_pair_keeps_shared_vertex_pixels_evaluated()
    {
        SvgDevicePoint shared = new(8, 8);
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), shared, new SvgDevicePoint(16, 16), new SvgDevicePoint(8, 16), shared];
        int[] ends = [3, 6];
        int[] roles = [1, 1];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using SvgScanlineCoverage scan = SvgScanlineCoverage.Prepare(points, ends, roles, false, roles, -2, 18, budget, CancellationToken.None);

        for (int y = -2; y < 18; y++)
        {
            scan.PrepareRow(y, CancellationToken.None);
            for (int x = -2; x < 18; x++)
            {
                double fan = SvgPolygonCoverage.GetWindingCoverage(points, ends, roles, x, y, CancellationToken.None);
                if (!scan.MightCover(x)) Assert.True(Math.Abs(fan) < 0.000000001, "Culled pixel carries fan coverage.");
            }
        }

        scan.PrepareRow(8, CancellationToken.None);
        Assert.True(scan.MightCover(8), "Shared-vertex pixel must evaluate.");
        Assert.True(scan.MightCover(7), "Vertex-adjacent pixel must evaluate.");
    }

    [Fact]
    public void Plain_winding_without_roles_matches_fan_sign()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(2, 2), new SvgDevicePoint(2, 14), new SvgDevicePoint(14, 14), new SvgDevicePoint(14, 2)];
        int[] ends = [4];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using SvgScanlineCoverage scan = SvgScanlineCoverage.Prepare(points, ends, [], false, null, -2, 18, budget, CancellationToken.None);

        for (int y = -2; y < 18; y++)
        {
            scan.PrepareRow(y, CancellationToken.None);
            for (int x = -2; x < 18; x++)
            {
                double fan = SvgPolygonCoverage.GetWindingCoverage(points, ends, x, y, CancellationToken.None);
                if (!scan.MightCover(x)) Assert.True(Math.Abs(fan) < 0.000000001, "Culled pixel carries fan coverage.");
            }
        }
    }

    [Fact]
    public void Degenerate_contours_contribute_no_edges()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(2, 2), new SvgDevicePoint(14, 2), new SvgDevicePoint(2, 14), new SvgDevicePoint(30, 30), new SvgDevicePoint(40, 40)];
        int[] ends = [3, 5];
        int[] roles = [1, 1];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using SvgScanlineCoverage scan = SvgScanlineCoverage.Prepare(points, ends, roles, false, roles, -2, 44, budget, CancellationToken.None);

        scan.PrepareRow(32, CancellationToken.None);
        Assert.False(scan.MightCover(32), "Degenerate-only row must cull.");
    }

    [Fact]
    public void Non_finite_vertices_disable_culling()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(2, 2), new SvgDevicePoint(14, 2), new SvgDevicePoint(double.NaN, 14)];
        int[] ends = [3];
        int[] roles = [1];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using SvgScanlineCoverage scan = SvgScanlineCoverage.Prepare(points, ends, roles, false, roles, -2, 18, budget, CancellationToken.None);

        scan.PrepareRow(4, CancellationToken.None);
        Assert.True(scan.MightCover(-2), "Non-finite payload must evaluate everywhere.");
        Assert.True(scan.MightCover(17), "Non-finite payload must evaluate everywhere.");
    }

    [Fact]
    public void Empty_payload_culls_everything()
    {
        SvgDevicePoint[] points = [];
        int[] ends = [];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using SvgScanlineCoverage scan = SvgScanlineCoverage.Prepare(points, ends, [], false, null, 0, 8, budget, CancellationToken.None);

        scan.PrepareRow(4, CancellationToken.None);
        Assert.False(scan.MightCover(4), "Empty payload must cull.");
    }

    [Fact]
    public void Mismatched_role_counts_throw_like_coverage()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8)];
        int[] ends = [3];
        int[] roles = [1, 1];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));

        Assert.Throws<ArgumentOutOfRangeException>(() => SvgScanlineCoverage.Prepare(points, ends, roles, false, roles, 0, 8, budget, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgPolygonCoverage.GetWindingCoverage(points, ends, roles, 0, 0, CancellationToken.None));
    }

    [Fact]
    public void Self_intersecting_outline_keeps_even_winding_lens_evaluated()
    {
        // Acute-apex miter stroke outline whose ring self-intersects: the lens between the two upward crossings carries winding two, which parity culling mistook for outside.
        SvgDevicePoint[] points = [new SvgDevicePoint(2.8778955729143845, 13.478852130680574), new SvgDevicePoint(8.877895572914385, 2.4788521306805733), new SvgDevicePoint(7.004106793532296, 1.9094642539574815), new SvgDevicePoint(6.004106793532296, 12.90946425395748), new SvgDevicePoint(7.995893206467704, 13.09053574604252), new SvgDevicePoint(8.995893206467704, 2.0905357460425185), new SvgDevicePoint(7.122104427085616, 1.5211478693194267), new SvgDevicePoint(1.1221044270856155, 12.521147869319426)];
        int[] ends = [8];
        int[] roles = [1];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using SvgScanlineCoverage plain = SvgScanlineCoverage.Prepare(points, ends, [], false, null, 0, 16, budget, CancellationToken.None);
        using SvgScanlineCoverage voted = SvgScanlineCoverage.Prepare(points, ends, roles, false, roles, 0, 16, budget, CancellationToken.None);

        plain.PrepareRow(3, CancellationToken.None);
        voted.PrepareRow(3, CancellationToken.None);
        Assert.True(plain.MightCover(7), "Even-winding lens pixel must evaluate through the plain fan path.");
        Assert.True(voted.MightCover(7), "Even-winding lens pixel must evaluate through the voted fan path.");

        for (int y = 0; y < 16; y++)
        {
            plain.PrepareRow(y, CancellationToken.None);
            voted.PrepareRow(y, CancellationToken.None);
            for (int x = 0; x < 16; x++)
            {
                double plainFan = SvgPolygonCoverage.GetWindingCoverage(points, ends, x, y, CancellationToken.None);
                if (!plain.MightCover(x)) Assert.True(Math.Abs(plainFan) < 0.000000001, "Culled pixel carries plain fan coverage.");
                double votedFan = SvgPolygonCoverage.GetWindingCoverage(points, ends, roles, x, y, CancellationToken.None);
                if (!voted.MightCover(x)) Assert.True(Math.Abs(votedFan) < 0.000000001, "Culled pixel carries voted fan coverage.");
            }
        }
    }
}
