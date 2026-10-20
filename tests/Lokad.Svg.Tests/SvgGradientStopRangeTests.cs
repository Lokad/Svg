using System.Globalization;
using System.Text;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGradientStopRangeTests
{
    [Theory]
    [InlineData("linearGradient")]
    [InlineData("radialGradient")]
    public void Maximum_stop_count_renders_and_releases_owned_storage(string kind)
    {
        byte[] input = Input("<defs><" + kind + " id='g'>" + Stops(256) + "</" + kind + "></defs><rect width='64' height='48' fill='url(#g)'/>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            Assert.Equal(256, GetStops(root.Geometries[0]).Length);
            Assert.NotEmpty(SvgConverter.ConvertToPng(root, SvgRenderOptions.Default, budget, CancellationToken.None).ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("linearGradient")]
    [InlineData("radialGradient")]
    public void Stop_count_above_supported_bound_rejects_and_releases(string kind)
    {
        byte[] input = Input("<defs><" + kind + " id='g'>" + Stops(257) + "</" + kind + "></defs><rect width='64' height='48' fill='url(#g)'/>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Last_coincident_stop_controls_exact_overlap_color_in_large_ramp()
    {
        SvgColor red = new(255, 0, 0, 255), cyan = new(0, 255, 255, 255);
        SvgGradientStop[] stops = new SvgGradientStop[256];
        for (int i = 0; i < stops.Length; i++) stops[i] = new(i / 255d, red);
        for (int i = 126; i <= 129; i++) stops[i] = new(.5, i == 129 ? cyan : red);
        using SvgGradientRamp ramp = SvgGradientRamp.Create(stops, red, red, 0, 1, new(100_000), new(256), CancellationToken.None);
        SvgShaderPaint sample = ramp.SampleLinear(.5, 1f, 32);
        Assert.InRange(sample.Red, 0f, .01f);
        Assert.InRange(sample.Green, .99f, 1f);
        Assert.InRange(sample.Blue, .99f, 1f);
    }

    [Theory]
    [InlineData("linearGradient")]
    [InlineData("radialGradient")]
    public void Forward_aliases_borrow_the_same_retained_stop_array(string kind)
    {
        byte[] input = Input("<defs><" + kind + " id='alias' href='#g'/><" + kind + " id='g'>" + Stops(256) + "</" + kind + "></defs><rect width='32' height='48' fill='url(#g)'/><rect x='32' width='32' height='48' fill='url(#alias)'/>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            Assert.Same(GetStops(root.Geometries[0]), GetStops(root.Geometries[1]));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<stop offset='invalid'/>", SvgErrorCode.MalformedInput)]
    [InlineData("<stop offset='.5'/><stop offset='.5'/>", SvgErrorCode.UnsupportedFeature)]
    public void Invalid_stop_ranges_preserve_categorized_rejection_and_cleanup(string stops, SvgErrorCode expected)
    {
        byte[] input = Input("<defs><linearGradient id='g'>" + stops + "</linearGradient></defs><rect width='64' height='48' fill='url(#g)'/>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(expected, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Large_pending_stop_arrays_observe_sampled_denial_cancellation_and_retry()
    {
        byte[] input = Input("<defs><linearGradient id='alias' href='#g'>" + Stops(256) + "</linearGradient><linearGradient id='g'><stop/><stop offset='1'/></linearGradient></defs><path d='M0 0H64V48H0Z' fill='url(#alias)'/>");
        RenderBudget measured = new(1_000_000);
        ParserWorkCounter work = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None))
            Assert.Equal(256, GetStops(root.Geometries[0]).Length);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        foreach (long limit in new[] { 1, work.Used / 2, work.Used - 1 })
        {
            RenderBudget denied = new(1_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(limit), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        RenderBudget tight = new(measured.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, tight, null, new(1_000_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, new(1_000_000), cancellation.Token)).CancellationToken);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        Assert.NotEmpty(SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    private static SvgGradientStop[] GetStops(SvgGeometry geometry) => geometry.Kind switch
    {
        SvgGeometryKind.GradientFill => geometry.Gradient.Gradient.Stops,
        SvgGeometryKind.RadialGradient => geometry.Radial.Gradient.Stops,
        SvgGeometryKind.GradientPolygon => geometry.GradientPolygon.Gradient.Stops,
        SvgGeometryKind.RadialPolygon => geometry.RadialPolygon.Gradient.Stops,
        _ => throw new InvalidOperationException("Expected gradient paint."),
    };

    private static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='48'>" + content + "</svg>");
    private static string Stops(int count)
    {
        StringBuilder text = new();
        for (int i = 0; i < count; i++) text.Append("<stop offset='").Append((i / (double)(count - 1)).ToString("R", CultureInfo.InvariantCulture)).Append("' stop-color='").Append(i % 2 == 0 ? "red" : "blue").Append("'/>");
        return text.ToString();
    }
}
