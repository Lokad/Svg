using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgStripStepTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(65536 >> 2, 2, 0)]
    [InlineData(65536 >> 1, 1, 0)]
    [InlineData((65536 >> 1) + (65536 >> 2), 2, 0)]
    [InlineData(65536, 0, 0)]
    [InlineData(-1, 2, 0)]
    [InlineData(-(65536 >> 1), 1, 0)]
    [InlineData(-65536, 0, 0)]
    public void Quantum_and_negative_gaps_keep_legacy_shift_routing(int gap, int shift, int advance)
    {
        SvgAnalyticCoverage.SelectStripStep(gap, out int actualShift, out int actualAdvance);

        Assert.Equal(shift, actualShift);
        Assert.Equal(advance, actualAdvance);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData((65536 >> 2) - 1, (65536 >> 2) - 1)]
    [InlineData((65536 >> 2) + 1, (65536 >> 2) + 1)]
    [InlineData((65536 >> 1) + 1, (65536 >> 1) + 1)]
    [InlineData(65536 - 1, 65536 - 1)]
    public void Fractional_gaps_advance_proportionally(int gap, int advance)
    {
        SvgAnalyticCoverage.SelectStripStep(gap, out int actualShift, out int actualAdvance);

        Assert.Equal(0, actualShift);
        Assert.Equal(advance, actualAdvance);
    }
}
