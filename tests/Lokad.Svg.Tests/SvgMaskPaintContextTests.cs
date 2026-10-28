using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Xunit;

namespace Lokad.Svg.Tests;

public sealed class SvgMaskPaintContextTests
{
    [Theory]
    [InlineData("translated-alpha", 128, 64, 32)]
    [InlineData("translated-luminance", 127, 63, 31)]
    public void Mask_mode_retains_its_independent_native_picture_dispatch(string identity, int edge, int bottom, int corner)
    {
        byte[] pixels = Rasterize(identity);
        Assert.Equal(edge, pixels[(2 * 32 + 8) * 4 + 3]);
        Assert.Equal(bottom, pixels[(4 * 32 + 3) * 4 + 3]);
        Assert.Equal(corner, pixels[(4 * 32 + 8) * 4 + 3]);
    }

    [Fact]
    public void Unmasked_path_retains_its_independent_native_filled_rectangle_coverage()
    {
        byte[] pixels = Rasterize("unmasked-translated");
        Assert.Equal(127, pixels[(2 * 32 + 8) * 4 + 3]);
        Assert.Equal(63, pixels[(4 * 32 + 3) * 4 + 3]);
        Assert.Equal(31, pixels[(4 * 32 + 8) * 4 + 3]);
    }

    private static byte[] Rasterize(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-instance-composition", identity + ".svg"));
        RenderBudget sceneBudget = new(2_000_000), rasterBudget = new(2_000_000);
        byte[] pixels;
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, rasterBudget, CancellationToken.None))
            pixels = surface.Pixels.ToArray();
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
        return pixels;
    }
}
