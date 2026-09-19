using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class PixelComparerTests
{
    [Fact]
    public void One_channel_difference_is_an_exact_failure()
    {
        RgbaImage reference = new(1, 1, [10, 20, 30, 40]);
        RgbaImage candidate = new(1, 1, [10, 21, 30, 40]);

        PixelComparison result = PixelComparer.Compare(reference, candidate);

        Assert.True(result.DimensionsMatch);
        Assert.Equal(1, result.ChangedPixelCount);
        Assert.Equal(1, result.MaximumChannelError);
    }

    [Fact]
    public void Invisible_rgb_is_canonicalized_but_alpha_is_not()
    {
        RgbaImage reference = new(1, 1, [1, 2, 3, 0]);
        RgbaImage candidate = new(1, 1, [200, 100, 50, 0]);

        PixelComparison result = PixelComparer.Compare(reference, candidate);

        Assert.Equal(0, result.ChangedPixelCount);
    }

    [Fact]
    public void Dimension_difference_fails_without_intersection_comparison()
    {
        PixelComparison result = PixelComparer.Compare(new RgbaImage(1, 1, [0, 0, 0, 0]), new RgbaImage(2, 1, [0, 0, 0, 0, 0, 0, 0, 0]));

        Assert.False(result.DimensionsMatch);
    }
}
