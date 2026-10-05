using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPathPointDensityTests
{
    // This six-point native path is M C L L Z; the retained cubic replaces
    // four sampled chords. The closed path is intentionally nonconvex.
    private static readonly SvgDevicePoint[] Points = [new(10, 10), new(20, 5), new(30, 10), new(40, 15), new(50, 10), new(50, 40), new(10, 40)];
    private static readonly SvgCubicSpan[] Cubics = [new(0, 0, 4, new(15, 0), new(45, 20))];

    [Fact]
    public void Raw_verb_count_is_cached_across_clipped_and_uncut_contexts()
    {
        SvgAnalyticContourPreparation preparation = new(Points, [7], [], false, null, SvgPolygonMapping.FlattenedCurves, [], Cubics, new(0, 0, 64, 64), 64, 64);
        RenderBudget budget = new(1_000_000);
        long firstWork;
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), null, SvgAnalyticPaintClip.Binary, 1_000_000, budget, out firstWork, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.Equal(6L, preparation.NativePointCount);
        }
        // The second uncut context reuses the charged classification/count;
        // a construction clip still uses that same whole-path point count.
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), null, SvgAnalyticPaintClip.Binary, 1_000_000, budget, out long cachedWork, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.True(cachedWork < firstWork);
            Assert.Equal(6L, preparation.NativePointCount);
        }
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), new(new(0, 8, 64, 40)), SvgAnalyticPaintClip.Binary, 1_000_000, budget, out long _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.Equal(6L, preparation.NativePointCount);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Count_validation_is_denied_before_storage_and_does_not_cache_partial_work()
    {
        SvgAnalyticContourPreparation preparation = new(Points, [7], [], false, null, SvgPolygonMapping.FlattenedCurves, [], Cubics, new(0, 0, 64, 64), 64, 64);
        RenderBudget success = new(1_000_000);
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), null, SvgAnalyticPaintClip.Binary, 1_000_000, success, out long _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
        }
        preparation.NativePointCount = null;
        RenderBudget denied = new(1_000_000);
        try
        {
            // One contour and one span require three charged visits. All other
            // classifications are cached; denial must precede count/storage.
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), null, SvgAnalyticPaintClip.Binary, 2, denied, out long _, out bool _, CancellationToken.None);
            Assert.Fail("Count validation must observe the remaining work limit.");
        }
        catch (SvgException exception) { Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode); }
        Assert.Null(preparation.NativePointCount);
        Assert.Equal(0, denied.Snapshot().PeakBytes);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("mapping")]
    [InlineData("parity")]
    [InlineData("overlap")]
    [InlineData("nonfinite")]
    public void Declined_curve_hints_preserve_the_flattened_path_count(string reason)
    {
        SvgCubicSpan[] spans = reason switch
        {
            "overlap" => [Cubics[0], new(0, 3, 5, new(42, 12), new(48, 20))],
            "nonfinite" => [new(0, 0, 4, new(double.NaN, 0), new(45, 20))],
            _ => Cubics,
        };
        bool parity = reason == "parity";
        SvgAnalyticContourPreparation preparation = new(Points, [7], parity ? [1] : [], parity, null, reason == "mapping" ? SvgPolygonMapping.FiniteEdges : SvgPolygonMapping.FlattenedCurves, [], spans, new(0, 0, 64, 64), 64, 64);
        RenderBudget budget = new(1_000_000);
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), null, SvgAnalyticPaintClip.Binary, 1_000_000, budget, out long _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            Assert.Equal(7L, preparation.NativePointCount);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
