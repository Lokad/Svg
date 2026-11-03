using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgDashCullingTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(0, 10, true)]
    [InlineData(10, 0, true)]
    [InlineData(0, 0, true)]
    [InlineData(10, 10, false)]
    public void Only_axis_lines_and_rectangles_retain_native_culling_sources(float x, float y, bool expected)
    {
        Assert.Equal(expected, SvgDashCulling.IsEligible([Move(0, 0), Line(x, y), End()], SvgAffineTransform.Identity, CancellationToken.None));
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(false, false, 7, 367, -2, 258, 3, 0, 7, 259)]
    [InlineData(false, false, 7, 367, 252, 390, 3, 0, 250, 367)]
    [InlineData(false, true, 7, 367, 252, 390, 3, 0, 367, 250)]
    [InlineData(true, false, -30, 30, -5, 12, 4, 1.25, -7.25, 14)]
    [InlineData(true, true, -30, 30, -5, 12, 4, 1.25, 15.25, -6)]
    [InlineData(false, false, 7, 367, 7, 367, 3, 0, 7, 367)]
    [InlineData(false, false, 7, 367, -2, 258.5, 3, 0, 7, 259)]
    public void Axis_culling_preserves_period_and_prior_edge_phase(bool horizontal, bool reverse,
        float minimum, float maximum, float low, float high, float period, float prior, float expectedFirst, float expectedLast)
    {
        SvgDevicePoint first = Point(reverse ? maximum : minimum), last = Point(reverse ? minimum : maximum);
        SvgDashCull bounds = horizontal ? new(low, -10, high, 10) : new(-10, low, 10, high);
        Assert.True(SvgDashCulling.ClipLine(ref first, ref last, bounds, period, prior));
        Assert.Equal(Point(expectedFirst), first);
        Assert.Equal(Point(expectedLast), last);

        SvgDevicePoint Point(float value) => horizontal ? new(value, 0) : new(0, value);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(0, 0, 9, 9)]
    [InlineData(0, -9, 0, -8)]
    [InlineData(15, 0, 20, 0)]
    public void Diagonal_or_disjoint_culling_keeps_the_original_source(float x0, float y0, float x1, float y1)
    {
        SvgDevicePoint first = new(x0, y0), last = new(x1, y1);
        Assert.False(SvgDashCulling.ClipLine(ref first, ref last, new(0, 0, 10, 10), 3, 0));
        Assert.Equal(new(x0, y0), first);
        Assert.Equal(new(x1, y1), last);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void A_culled_zero_line_keeps_the_native_cap_carrier()
    {
        SvgDevicePoint first = new(0, 0), last = first;
        Assert.True(SvgDashCulling.ClipLine(ref first, ref last, new(0, 0, 10, 10), 3, 0));
        Assert.Equal(new(0, 0), first);
        Assert.Equal(new(1.001f / (1 << 12), 0), last);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Bitmap_translation_is_narrowed_before_inverting_the_native_construction_map()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='600' height='384'><line x1='17' y1='7' x2='17' y2='367' stroke='blue' stroke-width='.5' stroke-dasharray='1 2'/></svg>");
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgAuthoredPath path = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            SvgAuthoredDashSource source = Assert.IsType<SvgAuthoredDashSource>(path.DashSource);
            Assert.True(source.TryFilter(new(1, 0, 0, .1, 0, 251.1), new(new(0, 254, 256, 384), new(0, 254)),
                budget, CancellationToken.None, out OwnedParserArray<SvgAuthoredPathCommand> filtered));
            using (filtered)
                // Native float translation is 251.1f - 254, then inverted. The
                // resulting 27.999939 source bound precedes the dash at 28.
                Assert.Equal(new SvgDevicePoint(17, 25), filtered.Items[0].First);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(0)]
    [InlineData(0.75)]
    [InlineData(-0.75)]
    public void Path_length_scaling_is_resolved_before_the_source_is_culled(double offset)
    {
        SvgAuthoredPathCommand[] source = [Move(17, 7), Line(17, 367), End()];
        SvgAuthoredPathCommand[] clipped = [Move(17, 7), Line(17, 259), End()];
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (OwnedParserArray<SvgAuthoredPathCommand> actual = SvgNativeDashPath.FilterClipped(source, [2, 4], offset * 2, 720,
            SvgAffineTransform.Identity, 1, new(-2, -2, 258, 258), budget, CancellationToken.None))
        using (OwnedParserArray<SvgAuthoredPathCommand> expected = SvgNativeDashPath.Filter(clipped, [1, 2], offset, null,
            SvgAffineTransform.Identity, 1, budget, CancellationToken.None))
            Assert.Equal(expected.Items, actual.Items);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Rectangle_edges_retain_prior_phase_and_the_original_closed_join()
    {
        SvgAuthoredPathCommand[] source = [Move(2, 2), Line(72, 2), Line(72, 72), Line(2, 72),
            new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default)];
        RenderBudget budget = new(new HostMemoryBudget(100_000));
        using (OwnedParserArray<SvgAuthoredPathCommand> actual = SvgDashCulling.Cull(source, SvgAffineTransform.Identity,
            new(10, 10, 28, 28), 8, .75f, [5, 3], 0, 280, budget, CancellationToken.None, out bool changed))
        {
            Assert.True(changed);
            Assert.Equal(16, actual.Items.Length);
            Assert.Equal(new(10, 2), actual.Items[0].First);
            Assert.Equal(new(32, 2), actual.Items[1].First);
            Assert.Equal(new(72, 4), actual.Items[3].First);
            Assert.Equal(new(72, 32), actual.Items[4].First);
            Assert.Equal(new(36, 72), actual.Items[6].First);
            Assert.Equal(new(10, 72), actual.Items[7].First);
            Assert.Equal(new(2, 34), actual.Items[9].First);
            Assert.Equal(new(2, 10), actual.Items[10].First);
            Assert.Equal(new(2, 2 + 70f / (1 << 12)), actual.Items[12].First);
            Assert.Equal(new(2, 2), actual.Items[13].First);
            Assert.Equal(new(2 + 70f / (1 << 12), 2), actual.Items[14].First);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Empty_moves_do_not_change_the_first_rectangle_or_invent_a_closed_final_contour(bool leading, bool trailing)
    {
        SvgAuthoredPathCommand[] rectangle = [Move(2, 2), Line(72, 2), Line(72, 72), Line(2, 72),
            new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default)];
        SvgAuthoredPathCommand[] source = (leading ? new[] { Move(100, 100) } : []).Concat(rectangle)
            .Concat(trailing ? [Move(100, 100), End()] : Array.Empty<SvgAuthoredPathCommand>()).ToArray();
        RenderBudget budget = new(new HostMemoryBudget(100_000));
        using (OwnedParserArray<SvgAuthoredPathCommand> actual = SvgDashCulling.Cull(source, SvgAffineTransform.Identity,
            new(10, 10, 28, 28), 8, .75f, [5, 3], 0, 280, budget, CancellationToken.None, out bool changed))
        {
            Assert.True(changed);
            Assert.Equal(trailing ? 12 : 16, actual.Items.Length);
            Assert.Equal(new(10, 2), actual.Items[0].First);
            Assert.DoesNotContain(actual.Items, command => command.First == new SvgDevicePoint(100, 100));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static SvgAuthoredPathCommand Move(float x, float y) => new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(x, y), default, default);
    private static SvgAuthoredPathCommand Line(float x, float y) => new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(x, y), default, default);
    private static SvgAuthoredPathCommand End() => new(SvgAuthoredPathVerb.End, SvgAuthoredArcFlags.None, default, default, default);
}
