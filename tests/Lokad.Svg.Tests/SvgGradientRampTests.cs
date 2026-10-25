using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGradientRampTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void Inline_coefficients_preserve_previous_float_lanes_without_owned_storage(int count, bool radial)
    {
        SvgColor start = new(23, 169, 205, 175), end = new(195, 213, 55, 200);
        SvgGradientStop[] stops = count switch
        {
            0 => [],
            1 => [new(.1, start) { Alpha = .27f }],
            _ => [new(.1, start) { Alpha = .27f }, new(.8, end) { Alpha = .81f }]
        };
        RenderBudget budget = new(1);
        using SvgGradientRamp ramp = SvgGradientRamp.Create(stops, start, end, .1, .8, budget, new(1), CancellationToken.None);
        foreach (double position in new[] { -1d, -0d, 0d, .1, .5, .8, 1d, 2d })
        foreach (float opacity in new[] { 0f, .37f, 1f })
        foreach (int matrix in new[] { 0, 63 })
        {
            SvgShaderPaint expected = default;
            Exception? previous = Record.Exception(() => expected = radial ? SvgGradientReference.RadialRamp(stops, start, end, .1, .8, position, opacity, matrix) : SvgGradientReference.Ramp(stops, start, end, .1, .8, position, opacity, matrix));
            if (previous is null) EqualBits(expected, radial ? ramp.SampleRadial(position, opacity, matrix) : ramp.SampleLinear(position, opacity, matrix));
            else
            {
                Exception current = Record.Exception(() => { if (radial) ramp.SampleRadial(position, opacity, matrix); else ramp.SampleLinear(position, opacity, matrix); })
                    ?? throw new InvalidOperationException("The prepared ramp must preserve the previous rejection.");
                Assert.Equal(previous.GetType(), current.GetType());
                Assert.Equal(previous.Message, current.Message);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Arbitrary_coefficients_preserve_repeated_and_float_collapsed_stop_boundaries(bool radial)
    {
        SvgColor first = new(23, 169, 205, 255), second = new(195, 213, 55, 255), third = new(149, 17, 233, 255);
        foreach (SvgGradientStop[] stops in new SvgGradientStop[][]
        {
            [new(.125, first) { Alpha = .27f }, new(.125, second) { Alpha = .81f }, new(.375, third) { Alpha = .43f }, new(.875, first) { Alpha = .95f }, new(.875, second) { Alpha = .81f }],
            [new(.5, first), new(double.BitIncrement(.5), second), new(double.BitIncrement(double.BitIncrement(.5)), third)]
        })
        {
            RenderBudget budget = new(10_000);
            using (SvgGradientRamp ramp = SvgGradientRamp.Create(stops, first, stops[^1].Color, stops[0].Offset, stops[^1].Offset, budget, new(256), CancellationToken.None))
            {
                foreach (double position in new[] { -.25, 0d, .125, double.BitDecrement(.375), .375, double.BitIncrement(.375), .5, .875, 1d, 1.25 })
                foreach (float opacity in new[] { .37f, 1f })
                    EqualBits(radial ? SvgGradientReference.RadialRamp(stops, first, stops[^1].Color, stops[0].Offset, stops[^1].Offset, position, opacity, 32) : SvgGradientReference.Ramp(stops, first, stops[^1].Color, stops[0].Offset, stops[^1].Offset, position, opacity, 32),
                        radial ? ramp.SampleRadial(position, opacity, 32) : ramp.SampleLinear(position, opacity, 32));
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Fact]
    public void Clipped_out_gradient_rectangles_do_not_reserve_unused_coefficients()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop stop-color='red'/><stop offset='.5' stop-color='green'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect x='100' y='100' width='4' height='4' fill='url(#g)'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        RenderBudget budget = new(8 * 8 * 4);
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.All(surface.Pixels, value => Assert.Equal(0, value));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Preparation_observes_memory_work_and_cancellation_and_releases_partial_storage()
    {
        SvgGradientStop[] stops = [new(0, new(0, 0, 0, 255)), new(.5, new(255, 0, 0, 255)), new(1, new(255, 255, 255, 255))];
        RenderBudget budget = new(10_000), tight = new(1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgGradientRamp.Create(stops, stops[0].Color, stops[^1].Color, 0, 1, tight, new(3), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgGradientRamp.Create(stops, stops[0].Color, stops[^1].Color, 0, 1, budget, new(2), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgGradientRamp.Create(stops, stops[0].Color, stops[^1].Color, 0, 1, budget, new(3), cancellation.Token)).CancellationToken);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using (SvgGradientRamp ramp = SvgGradientRamp.Create(stops, stops[0].Color, stops[^1].Color, 0, 1, budget, new(3), CancellationToken.None))
        {
            SvgShaderPaint expected = ramp.SampleLinear(.25, 1, 32);
            stops[1] = new(.5, new(0, 0, 255, 255));
            EqualBits(expected, ramp.SampleLinear(.25, 1, 32));
            Assert.True(budget.Snapshot().CurrentBytes > 0);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Uniform_large_ramp_needs_no_segment_storage_and_keeps_nonfinite_positions_constant()
    {
        SvgColor color = new(192, 64, 128, 255);
        SvgGradientStop[] stops = Enumerable.Range(0, 256).Select(index => new SvgGradientStop(index / 255d, color) { Alpha = .65f }).ToArray();
        RenderBudget budget = new(1);
        ParserWorkCounter work = new(256);
        using (SvgGradientRamp ramp = SvgGradientRamp.Create(stops, color, color, 0, 1, budget, work, CancellationToken.None))
        {
            SvgShaderPaint expected = ramp.SampleLinear(.5, .7f, 32);
            foreach (double position in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                EqualBits(expected, ramp.SampleLinear(position, .7f, 32));
                EqualBits(expected, ramp.SampleRadial(position, .7f, 32));
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            Assert.Equal(256, work.Used);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static void EqualBits(SvgShaderPaint expected, SvgShaderPaint actual)
    {
        Assert.Equal(BitConverter.SingleToUInt32Bits(expected.Red), BitConverter.SingleToUInt32Bits(actual.Red));
        Assert.Equal(BitConverter.SingleToUInt32Bits(expected.Green), BitConverter.SingleToUInt32Bits(actual.Green));
        Assert.Equal(BitConverter.SingleToUInt32Bits(expected.Blue), BitConverter.SingleToUInt32Bits(actual.Blue));
        Assert.Equal(BitConverter.SingleToUInt32Bits(expected.Alpha), BitConverter.SingleToUInt32Bits(actual.Alpha));
    }
}
