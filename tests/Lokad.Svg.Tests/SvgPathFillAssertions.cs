using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

/// <summary>Checks integer fill semantics independently of the retained scene representation.</summary>
internal static class SvgPathFillAssertions
{
    internal static void MatchesIntegerBands(SvgRoot root, ReadOnlySpan<SvgRectangle> bands)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(268_435_456L));
        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
        byte[] expected = new byte[surface.Pixels.Length];
        foreach (SvgRectangle band in bands)
        {
            Assert.True(band.HasFill);
            Assert.Null(band.Stroke);
            Assert.Equal(Math.Truncate(band.X), band.X);
            Assert.Equal(Math.Truncate(band.Y), band.Y);
            Assert.Equal(Math.Truncate(band.Width), band.Width);
            Assert.Equal(Math.Truncate(band.Height), band.Height);
            int left = (int)Math.Clamp(band.X, 0, surface.Width), top = (int)Math.Clamp(band.Y, 0, surface.Height);
            int right = (int)Math.Clamp(band.X + band.Width, 0, surface.Width), bottom = (int)Math.Clamp(band.Y + band.Height, 0, surface.Height);
            for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++)
                {
                    Span<byte> pixel = expected.AsSpan((y * surface.Width + x) * 4, 4);
                    Assert.Equal(0, pixel[3]); // Expected bands describe disjoint painted regions.
                    pixel[0] = (byte)((band.Color.Red * band.Color.Alpha + 127) / 255);
                    pixel[1] = (byte)((band.Color.Green * band.Color.Alpha + 127) / 255);
                    pixel[2] = (byte)((band.Color.Blue * band.Color.Alpha + 127) / 255);
                    pixel[3] = band.Color.Alpha;
                }
        }
        Assert.Equal(expected, surface.Pixels.ToArray());
    }

    internal static void Equivalent(SvgRoot first, SvgRoot second)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(268_435_456L));
        using SvgConverter.RasterizedSurface expected = SvgConverter.Rasterize(first, SvgRenderOptions.Default, budget, CancellationToken.None);
        using SvgConverter.RasterizedSurface actual = SvgConverter.Rasterize(second, SvgRenderOptions.Default, budget, CancellationToken.None);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Pixels.ToArray(), actual.Pixels.ToArray());
    }
}
