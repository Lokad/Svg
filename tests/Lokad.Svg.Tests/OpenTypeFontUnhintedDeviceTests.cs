using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    public void Unhinted_fixed_scaling_truncates_size_and_rounds_signed_half_units_away_from_zero()
    {
        OpenTypeUnhintedDeviceScale scale = OpenTypeUnhintedDeviceScale.Create(19.994983673095703, 1000);
        Assert.Equal(83821, scale.Factor);
        OpenTypeUnhintedDeviceScale half = new(32768);
        Assert.Equal(1, half.Apply(1));
        Assert.Equal(-1, half.Apply(-1));
        Assert.Equal(2, half.Apply(3));
        Assert.Equal(-2, half.Apply(-3));
        Assert.Equal(0, OpenTypeUnhintedDeviceScale.Create(.001, 1000).Apply(1000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(32768)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Unhinted_scaling_rejects_sizes_outside_the_native_coordinate_bound(double size)
        => Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => OpenTypeUnhintedDeviceScale.Create(size, 1000)).ErrorCode);

    [Fact]
    public void Unhinted_point_assembly_releases_owners_on_success_denial_cancellation()
    {
        byte[] bytes = CreateTriangleFont();
        OpenTypeTableRange range = OpenTypeFont.GetGlyphDataRange(bytes, 2, CancellationToken.None);
        WriteInt16(bytes, range.Offset, -1);
        WriteUInt16(bytes, range.Offset + 10, 515);
        WriteUInt16(bytes, range.Offset + 12, 1);
        WriteInt16(bytes, range.Offset + 14, 3);
        WriteInt16(bytes, range.Offset + 16, -4);
        byte[] unchanged = bytes.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        RenderBudget budget = new(long.MaxValue);
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetUnhintedDeviceContours(bytes, 2, metrics, 20.48, budget, CancellationToken.None))
            Assert.NotEmpty(contours.Segments);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, bytes);
        RenderBudget denied = new(budget.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => OpenTypeFont.GetUnhintedDeviceContours(bytes, 2, metrics, 20.48, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => OpenTypeFont.GetUnhintedDeviceContours(bytes, 2, metrics, 20.48, budget, cancellation.Token));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
