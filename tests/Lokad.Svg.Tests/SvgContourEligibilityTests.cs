using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgContourEligibilityTests
{
    [Fact]
    public void Disjoint_twin_triangles_take_analytic_rows_without_fan()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(0, 8), new(8, 8), new(16, 8), new(8, 16)];
        int[] ends = [3, 6];
        int[] roles = [1, 1];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, roles, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(new SvgRootLength(16, false), new SvgRootLength(16, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1, 255)], false, null);
            SvgRenderLimits limits = new(8_192, 16, 4_096, 4_096, 16, 16);
            int fanEvaluations = 0;
            using IDisposable fanScope = SvgPolygonCoverageProbe.Arm(() => fanEvaluations++);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None);
            Assert.Equal(0, fanEvaluations);
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Nested_counter_takes_analytic_rows_without_fan()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        SvgDevicePoint[] points = [new(0, 0), new(16, 0), new(16, 16), new(0, 16), new(12, 4), new(12, 12), new(4, 12), new(4, 4)];
        int[] ends = [4, 8];
        int[] roles = [1, -1];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, roles, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(new SvgRootLength(16, false), new SvgRootLength(16, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1, 255)], false, null);
            SvgRenderLimits limits = new(8_192, 16, 4_096, 4_096, 16, 16);
            int fanEvaluations = 0;
            using IDisposable fanScope = SvgPolygonCoverageProbe.Arm(() => fanEvaluations++);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None);
            Assert.Equal(0, fanEvaluations);
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Evenodd_twin_takes_analytic_rows_without_fan()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(0, 8), new(8, 8), new(16, 8), new(8, 16)];
        int[] ends = [3, 6];
        int[] signs = [1, 1];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, signs, new SvgColor(255, 0, 0, 255), true, true, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(new SvgRootLength(16, false), new SvgRootLength(16, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1, 255)], false, null);
            SvgRenderLimits limits = new(8_192, 16, 4_096, 4_096, 16, 16);
            int fanEvaluations = 0;
            using IDisposable fanScope = SvgPolygonCoverageProbe.Arm(() => fanEvaluations++);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None);
            Assert.Equal(0, fanEvaluations);
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Edge_sharing_multi_keeps_fan_path()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(0, 8), new(8, 0), new(8, 8), new(0, 8)];
        int[] ends = [3, 6];
        int[] roles = [1, 1];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, roles, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(new SvgRootLength(16, false), new SvgRootLength(16, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1, 255)], false, null);
            SvgRenderLimits limits = new(8_192, 16, 4_096, 4_096, 16, 16);
            int fanEvaluations = 0;
            using IDisposable fanScope = SvgPolygonCoverageProbe.Arm(() => fanEvaluations++);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None);
            Assert.True(fanEvaluations > 0);
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Overlapping_multi_keeps_fan_path()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(0, 8), new(4, 0), new(12, 0), new(4, 8)];
        int[] ends = [3, 6];
        int[] roles = [1, 1];
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, roles, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.FiniteEdges, null, null, budget);
        try
        {
            SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);
            using SvgRoot root = new(new SvgRootLength(16, false), new SvgRootLength(16, false), null, [geometry], [new SvgDrawCommand(SvgDrawCommandKind.Geometry, 0, 255, null, -1, 255)], false, null);
            SvgRenderLimits limits = new(8_192, 16, 4_096, 4_096, 16, 16);
            int fanEvaluations = 0;
            using IDisposable fanScope = SvgPolygonCoverageProbe.Arm(() => fanEvaluations++);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new SvgRenderOptions(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None);
            Assert.True(fanEvaluations > 0);
        }
        finally { fill.Dispose(); }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Multi_contour_preparation_charges_set_scan_and_reports_eligibility()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        SvgDevicePoint[] twin = [new(0, 0), new(8, 0), new(0, 8), new(8, 8), new(16, 8), new(8, 16)];
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(twin, [3, 6], [], false, [1, 1], SvgPolygonMapping.QuarterEdges, 0, 16, 0, 16, 16, 16, budget, out bool twinEligible, CancellationToken.None))
        {
            Assert.True(twinEligible);
        }
        SvgDevicePoint[] touching = [new(0, 0), new(8, 0), new(0, 8), new(8, 0), new(8, 8), new(0, 8)];
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(touching, [3, 6], [], false, [1, 1], SvgPolygonMapping.QuarterEdges, 0, 16, 0, 16, 16, 16, budget, out bool touchingEligible, CancellationToken.None))
        {
            Assert.False(touchingEligible);
        }
        SvgDevicePoint[] degenerate = [new(0, 0), new(0, 0), new(0, 0)];
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(degenerate, [3], [], false, [0], SvgPolygonMapping.QuarterEdges, 0, 16, 0, 16, 16, 16, budget, out bool degenerateEligible, CancellationToken.None))
        {
            Assert.False(degenerateEligible);
        }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Shared_vertex_multi_keeps_fan_path()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        SvgDevicePoint[] lobes = [new(2, 2), new(4, 3), new(8, 4), new(8, 8), new(2, 8), new(4, 4), new(4, 2), new(4, 0), new(2, 2), new(0, 4), new(0, 0), new(2, 2)];
        int[] ends = [5, 9, 12];
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(lobes, ends, [], false, null, SvgPolygonMapping.QuarterEdges, 0, 16, 0, 16, 16, 16, budget, out bool eligible, CancellationToken.None))
        {
            Assert.False(eligible);
        }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Self_touching_curve_multi_takes_analytic_path()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        SvgDevicePoint[] loops = [new(0, 0), new(8, 0), new(0, 8), new(16, 0), new(24, 8), new(24, 0), new(20, 4)];
        int[] ends = [3, 7];
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(loops, ends, [], false, null, SvgPolygonMapping.FlattenedCurves, 0, 32, 0, 16, 32, 16, budget, out bool eligible, CancellationToken.None))
        {
            Assert.True(eligible);
        }
        AssertBudgetReleased(budget);
    }

    [Fact]
    public void Transversal_crossing_straight_multi_takes_analytic_path()
    {
        // Straight-edge loops meeting only in transversal crossings union through
        // signed-winding intervals on any mapping; touches and coincident spans keep the fan path.
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        SvgDevicePoint[] bands = [new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(2, 2), new(6, 2), new(6, 6), new(2, 6)];
        int[] ends = [4, 8];
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(bands, ends, [], false, [1, 1], SvgPolygonMapping.FiniteEdges, 0, 8, 0, 8, 8, 8, budget, out bool eligible, CancellationToken.None))
        {
            Assert.True(eligible);
        }
        AssertBudgetReleased(budget);
    }

    private static void AssertBudgetReleased(RenderBudget budget)
    {
        RenderBudgetSnapshot snapshot = budget.Snapshot();
        Assert.Equal(0, snapshot.CurrentBytes);
        Assert.Equal(0, snapshot.BytesByCategory[(int)SvgMemoryCategory.Surface]);
    }
}
