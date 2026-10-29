using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgImageSourceViewTests
{
    [Theory]
    [InlineData("xMinYMid slice", 0, 3)]
    [InlineData("xMidYMid slice", 1.5f, 4.5f)]
    [InlineData("xMaxYMid slice", 3, 6)]
    public void Slice_keeps_the_requested_pixel_window_independent_of_destination_coverage(string aspect, float left, float right)
    {
        (SvgRectangle box, SvgShaderMatrix local, SvgImageSourceRect source) = SvgImageSyntax.MapRasterPlacement(
            aspect, new("", 0, 0, null, null), 6, 4, 12, 16);
        Assert.Equal(new(left, 0, right, 4), source);
        Assert.Equal((12d, 16d), (box.Width, box.Height));
        Assert.Equal(new(4, 0, -left * 4, 0, 4, 0), local);
    }

    [Theory]
    [InlineData("xMidYMin slice", 0, 3)]
    [InlineData("xMidYMid slice", 1.5f, 4.5f)]
    [InlineData("xMidYMax slice", 3, 6)]
    public void Slice_retains_vertical_alignment_endpoints(string aspect, float top, float bottom)
    {
        var placement = SvgImageSyntax.MapRasterPlacement(aspect, new("", 0, 0, null, null), 4, 6, 16, 12);
        Assert.Equal(new(0, top, 4, bottom), placement.SourceRect);
        Assert.Equal(new(4, 0, 0, 0, 4, -top * 4), placement.Local);
    }

    [Theory]
    [InlineData(.25f, 1, 0, 1)]
    [InlineData(1, 2, 1, 2)]
    [InlineData(1, 2.0000002f, 1, 3)]
    [InlineData(1.25f, 1.5f, 1, 2)]
    public void Sampling_encloses_fractional_windows_before_fixed_coordinate_rounding(float left, float right, int first, int end)
    {
        SvgImageSourceRect source = new(left, 0, right, 1);
        Assert.False(source.IsEmpty);
        Assert.Equal(new(first, 0, end, 1), source.PixelBounds);
    }

    [Fact]
    public void Destination_clipping_does_not_recrop_or_move_source_pixels()
    {
        SvgImageSourceRect source = new(1.5f, 0, 4.5f, 4);
        SvgShaderMatrix local = new(4, 0, -6, 0, 4, 0), reference = new(-1, .2f, 24, 0, 1, 0);
        using SvgImageGeometry image = new(new SvgColor(85, 160, 220, 173), 6, 4,
            new(0, 0, 12, 16, default, true, null), source, reference, local, 128);
        using SvgImageGeometry clipped = image.ClipTo(new(2, 3, 10, 13));
        Assert.Equal(source, clipped.SourceRect);
        Assert.Equal(local, clipped.LocalMap);
        Assert.Equal(reference, clipped.ReferenceMap);
        Assert.Equal(image.UniformSample, clipped.UniformSample);
        Assert.NotEqual(image.Box, clipped.Box);
    }

    [Theory]
    [InlineData(-.5f, 40)]
    [InlineData(.5f, 40)]
    [InlineData(1, 60)]
    [InlineData(1.5f, 80)]
    [InlineData(2.5f, 80)]
    public void Affine_sampling_clamps_to_the_view_while_retaining_parent_stride(float x, byte red)
    {
        byte[] pixels = Enumerable.Repeat((byte)255, 6 * 4 * 4).ToArray();
        for (int y = 1; y < 3; y++)
        {
            pixels[(y * 6 + 2) * 4] = 40;
            pixels[(y * 6 + 3) * 4] = 80;
        }
        byte[] unchanged = pixels.ToArray();
        Assert.Equal(new(red, 255, 255, 255), SvgConstantShaderPaint.SampleAffine(
            pixels, 6, 4, x, .5f, false, new(0, 0), new(2, 1, 4, 3)));
        Assert.Equal(unchanged, pixels);
    }

    [Theory]
    [InlineData(8, 8, .5f, 4, 4)]
    [InlineData(8, 8, .56249994f, 4, 4)]
    [InlineData(8, 8, .5625f, 8, 8)]
    [InlineData(8, 8, .0625f, 1, 1)]
    [InlineData(8, 8, .062499992f, 0, 0)]
    [InlineData(7, 5, .5f, 4, 3)]
    [InlineData(7, 5, .25f, 2, 2)]
    [InlineData(9, 1, .5f, 5, 1)]
    [InlineData(1, 9, .5f, 1, 5)]
    public void Recorded_decode_levels_use_rounded_requests_and_ceil_sizes(int width, int height, float scale, int expectedWidth, int expectedHeight)
        => Assert.Equal((expectedWidth, expectedHeight), SvgImageMipMap.SelectDecodeSize(new(scale, 0, 0, 0, scale, 0), width, height));

    [Theory]
    [InlineData(1, 0, 0, 0)]
    [InlineData(2, 1, 0, .5f)]
    [InlineData(4, 2, 1, .5f)]
    [InlineData(8, 3, 2, .5f)]
    public void Picture_levels_retain_fractional_mip_weights(float inverseScale, int nearest, int upper, float lowerWeight)
    {
        SvgShaderMatrix inverse = new(inverseScale, 0, 0, 0, inverseScale, 0);
        Assert.Equal(nearest, SvgImageMipMap.SelectLevel(inverse, 16, 16));
        Assert.Equal((upper, lowerWeight), SvgImageMipMap.SelectLinearLevels(inverse, 16, 16));
    }

    [Theory]
    [InlineData(1, 2, 1, 3, 4, 201)]
    [InlineData(1, 3, 2, 4, 6, 202)]
    [InlineData(2, 1, 1, 3, 4, 201)]
    [InlineData(3, 1, 2, 4, 6, 202)]
    [InlineData(2, 2, 2, 5, 7, 202)]
    [InlineData(2, 3, 3, 7, 10, 203)]
    [InlineData(3, 2, 3, 7, 10, 203)]
    [InlineData(3, 3, 5, 10, 15, 205)]
    public void Reduction_keeps_box_triangle_weights_and_joint_truncation(int width, int height, byte red, byte green, byte blue, byte alpha)
    {
        int stride = width + 2;
        byte[] pixels = Enumerable.Repeat((byte)255, stride * (height + 2) * 4).ToArray();
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int value = y * width + x + 1, at = ((y + 1) * stride + x + 1) * 4;
            pixels[at] = (byte)value; pixels[at + 1] = (byte)(value * 2);
            pixels[at + 2] = (byte)(value * 3); pixels[at + 3] = (byte)(200 + value);
        }
        byte[] unchanged = pixels.ToArray(), output = new byte[4];
        SvgAnalyticClipBounds bounds = new(1, 1, width + 1, height + 1);
        Assert.Equal(width * height, SvgImageMipMap.SampleCount(bounds));
        SvgImageMipMap.Reduce(pixels, stride, bounds, output, CancellationToken.None);
        Assert.Equal(new byte[] { red, green, blue, alpha }, output);
        Assert.Equal(unchanged, pixels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Image_reduction_observes_cancellation_before_writing_borrowed_storage(bool resize)
    {
        byte[] pixels = new byte[16], output = [0xA5, 0xA5, 0xA5, 0xA5];
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        Assert.Equal(canceled.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            if (resize) SvgImageMipMap.Resize(pixels, 2, new(0, 0, 2, 2), output, 1, 1, canceled.Token);
            else SvgImageMipMap.Reduce(pixels, 2, new(0, 0, 2, 2), output, canceled.Token);
        }).CancellationToken);
        Assert.Equal(new byte[] { 0xA5, 0xA5, 0xA5, 0xA5 }, output);
    }
}
