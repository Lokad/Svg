using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class LatinLightHintingTests
{
    [Fact]
    public void Fits_opposite_windings_to_the_same_alignment_zones()
    {
        OpenTypeGlyphPoint[] forward = [new(0, 0, true), new(0, 700, true), new(300, 700, true), new(300, 0, true)];
        OpenTypeGlyphPoint[] backward = forward.Reverse().ToArray();
        LatinAlignmentZone[] zones = [new(0, 0, 700, 0, false, false), new(700, 700, 700, 0, true, false)];
        var budget = new RenderBudget(long.MaxValue);
        LatinLightHinting.Hint(forward, [3], zones, 1000, 16, budget, new(long.MaxValue), CancellationToken.None);
        LatinLightHinting.Hint(backward, [3], zones, 1000, 16, budget, new(long.MaxValue), CancellationToken.None);

        Assert.Equal(forward, backward.Reverse());
        Assert.Equal(new int[] { 0, 704, 704, 0 }, forward.Select(point => point.Y));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Scales_without_edges_when_the_face_has_no_Latin_alignment_samples()
    {
        OpenTypeGlyphPoint[] points = [new(-100, 0, true), new(0, 700, true), new(100, 0, true)];
        var budget = new RenderBudget(long.MaxValue);

        LatinLightHinting.Hint(points, [2], [], 1000, 16, budget, new(long.MaxValue), CancellationToken.None);

        Assert.Equal(new OpenTypeGlyphPoint[] { new(-102, 0, true), new(0, 717, true), new(102, 0, true) }, points);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Keeps_zero_scaled_subpixel_outlines_bounded()
    {
        OpenTypeGlyphPoint[] points = [new(0, 0, true), new(0, 700, true), new(300, 700, true), new(300, 0, true)];
        LatinAlignmentZone[] zones = [new(700, 700, 700, 0, true, true)];
        var budget = new RenderBudget(long.MaxValue);

        LatinLightHinting.Hint(points, [3], zones, 1000, 0.001, budget, new(long.MaxValue), CancellationToken.None);

        Assert.All(points, point => Assert.Equal(new OpenTypeGlyphPoint(0, 0, true), point));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(32768)]
    public void Rejects_out_of_range_device_sizes_before_allocation(double size)
    {
        var budget = new RenderBudget(long.MaxValue);
        SvgException exception = Assert.Throws<SvgException>(() => LatinLightHinting.Hint([], [], [], 1000, size, budget, new(long.MaxValue), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().PeakBytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(287)]
    [InlineData(288)]
    [InlineData(500)]
    public void Releases_point_and_topology_scratch_after_memory_denial(long maximumBytes)
    {
        OpenTypeGlyphPoint[] points = [new(0, 0, true), new(0, 700, true), new(300, 700, true), new(300, 0, true)];
        LatinAlignmentZone[] zones = [new(0, 0, 700, 0, false, false), new(700, 700, 700, 0, true, false)];
        var budget = new RenderBudget(maximumBytes);

        SvgException exception = Assert.Throws<SvgException>(() => LatinLightHinting.Hint(points, [3], zones, 1000, 16, budget, new(long.MaxValue), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(1, budget.Snapshot().DeniedReservationCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(30)]
    public void Releases_scratch_when_work_expires_then_allows_retry(long maximumWork)
    {
        OpenTypeGlyphPoint[] points = [new(0, 0, true), new(0, 700, true), new(300, 700, true), new(300, 0, true)];
        LatinAlignmentZone[] zones = [new(0, 0, 700, 0, false, false), new(700, 700, 700, 0, true, false)];
        var budget = new RenderBudget(long.MaxValue);

        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => LatinLightHinting.Hint(points, [3], zones, 1000, 16, budget, new(maximumWork), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        LatinLightHinting.Hint(points, [3], zones, 1000, 16, budget, new(long.MaxValue), CancellationToken.None);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(704, points[1].Y);
    }

    [Fact]
    public void Observes_cancellation_before_allocating_outline_scratch()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var budget = new RenderBudget(long.MaxValue);

        Assert.ThrowsAny<OperationCanceledException>(() => LatinLightHinting.Hint([new(0, 0, true)], [0], [new(0, 0, 0, 0, false, false)], 1000, 16, budget, new(long.MaxValue), cancellation.Token));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
