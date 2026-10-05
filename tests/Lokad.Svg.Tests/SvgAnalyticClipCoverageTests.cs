using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAnalyticClipCoverageTests
{
    [Fact]
    public void Every_path_and_clip_byte_pair_matches_the_compiled_native_product()
    {
        // Generated with SkMulDiv255Round from include/private/SkMath.h at
        // f8b66b7597c4cc859d3ed190e9c6872241e6721c. Order: path byte, clip byte.
        byte[] native = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/clip-alpha-native.bin"));
        Assert.Equal(65536, native.Length);
        for (int path = 0; path < 256; path++)
        for (int clip = 0; clip < 256; clip++)
            Assert.Equal(native[path * 256 + clip], SvgAnalyticClipCoverage.FromMask((byte)clip).Apply((byte)path));
    }

    [Theory]
    [InlineData(.15, 0, 285.85, 200, 0, 100, 217)]
    [InlineData(.15, 0, 285.85, 200, 285, 100, 216)]
    [InlineData(.3, 0, 571.7, 400, 0, 200, 179)]
    [InlineData(.3, 0, 571.7, 400, 571, 200, 178)]
    [InlineData(.6, 0, 1143.4, 800, 0, 400, 103)]
    [InlineData(.6, 0, 1143.4, 800, 1143, 400, 102)]
    [InlineData(0, .375, 40, 27.625, 1, 0, 128)]
    [InlineData(0, .375, 40, 27.625, 1, 27, 191)]
    [InlineData(.15, .375, 35.85, 27.625, 0, 0, 109)]
    [InlineData(.15, .375, 35.85, 27.625, 35, 0, 108)]
    [InlineData(.988, .251, 35.75, 7.75, 0, 0, 0)]
    [InlineData(.988, .251, 35.75, 7.75, 0, 1, 4)]
    public void Rectangle_mask_preserves_native_boundary_bytes(double left, double top, double right, double bottom, int x, int y, byte expected)
    {
        // Pinned native convex rectangle walking emits anti-rect fringes on
        // full Y rows and additive, snapped runs on partial Y rows.
        SvgAnalyticClipCoverage mask = SvgAnalyticRectangleClip.GetCoverage((left, top, right, bottom), SvgClipAntialiasing.All, x, y);
        Assert.Equal(expected, mask.Apply(255));
    }

    [Fact]
    public void Initially_binary_nearly_integral_rectangle_uses_integer_coverage()
    {
        SvgAnalyticClipCoverage mask = SvgAnalyticRectangleClip.GetCoverage((.06, .06, 35.94, 27.94), SvgClipAntialiasing.All, 0, 0);
        Assert.Equal(255, mask.Apply(255));
        Assert.Equal(50, mask.Apply(50));
    }

    [Fact]
    public void Allocation_only_bounds_preserve_geometric_fallback()
    {
        SvgAnalyticClipCoverage area = SvgAnalyticRectangleClip.GetCoverage((.15, .375, 35.85, 27.625), SvgClipAntialiasing.None, 0, 0);
        Assert.Equal(135, area.Apply(255));
    }
}
