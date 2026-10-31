using System.Globalization;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgFilterSourceHintTests
{
    [Fact]
    public void Parameter_capture_encloses_source_visuals_before_gaussian_expansion()
        => AssertNativeParameterCapture(3.84278f, new(38.714375f, 47.374504f, 206.11948f, 209.79495f),
            new(-1000, -1000, 1000, 1000), new(26, 35, 219, 222), new(27, 35, 218, 222));

    [Fact]
    public void Referenced_gradient_capture_preserves_native_left_and_right_enclosure()
        => AssertNativeParameterCapture(5.6950996f, new(218.27927f, 81.11185f, 562.594f, 213.11453f),
            new(183.8478f, 67.911575f, 597.02545f, 226.3148f), new(200, 67, 581, 227), new(201, 67, 580, 227));

    [Fact]
    public void Translucent_gradient_capture_preserves_native_top_enclosure()
        => AssertNativeParameterCapture(10.108017f, new(696.90625f, 397.375f, 1280.2188f, 744),
            new(638.575f, 362.7125f, 1338.55f, 778.6625f), new(665, 366, 1312, 775), new(666, 367, 1311, 775));

    private static void AssertNativeParameterCapture(float sigma, SvgPaintBounds visual, SvgPaintBounds region,
        SvgPaintBounds expected, SvgPaintBounds withoutSourceEnclosure)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='300' height='300'><defs><filter id='f' filterUnits='userSpaceOnUse' x='-1000' y='-1000' width='2000' height='2000'><feGaussianBlur stdDeviation='"
            + sigma.ToString("R", CultureInfo.InvariantCulture) + "'/></filter></defs><rect width='4' height='4' filter='url(#f)'/></svg>");
        RenderBudget parser = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
            SvgFilterResource resource = arena.ResourceAt(arena.BindingAt(scene.Commands[0].MaskContentIndex).ResourceIndex);
            SvgFilterRegionContext context = new(SvgAffineTransform.Identity, new(300, 300), region);
            RenderBudget scratch = new(new HostMemoryBudget(1_000_000));
            // Independent saveLayer observations are retained in the SVGWeb stage records.
            Assert.Equal(expected,
                SvgFilterPlayback.ParameterCaptureBounds(resource, new(1, 1), context, visual, scratch, CancellationToken.None));
            Assert.Equal(withoutSourceEnclosure, SvgFilterPlayback.RoundParameterBounds(
                SvgFilterPlayback.SourceLayerBounds(resource, new(1, 1), context, visual, scratch, CancellationToken.None)));
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(-.25f, -.75f, 8.25f, 9.75f, -1, -1, 9, 10)]
    [InlineData(2.25f, 3.75f, 7.25f, 8.75f, 2, 3, 8, 9)]
    [InlineData(-20, -20, 20, 20, -10, -10, 10, 10)]
    [InlineData(1, 2, 7, 8, 1, 2, 7, 8)]
    [InlineData(-.0001f, .0001f, 7.0001f, 7.9999f, -1, 0, 8, 8)]
    public void Requested_image_intersects_before_enclosing_without_layer_inset(float left, float top,
        float right, float bottom, float expectedLeft, float expectedTop, float expectedRight, float expectedBottom)
    {
        SvgPaintBounds hint = new(left, top, right, bottom);
        SvgPaintBounds request = new(-10, -10, 10, 10);
        SvgPaintBounds expected = new(expectedLeft, expectedTop, expectedRight, expectedBottom);
        Assert.Equal(expected, SvgFilterPlayback.RequestedSourceImageBounds(hint, request));
        Assert.Equal(expected, SvgFilterPlayback.RequestedSourceImageBounds(request, hint));
    }

    public static IEnumerable<object[]> InvalidImageCoordinates()
    {
        foreach (float coordinate in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 2147483648f, -2147483904f })
            for (int index = 0; index < 4; index++) yield return [coordinate, index];
    }

    [Theory]
    [MemberData(nameof(InvalidImageCoordinates))]
    public void Requested_image_admits_each_coordinate_before_integer_conversion(float coordinate, int index)
    {
        float[] edges = [0, 0, 1, 1];
        edges[index] = coordinate;
        SvgPaintBounds hint = new(edges[0], edges[1], edges[2], edges[3]);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgFilterPlayback.RequestedSourceImageBounds(hint, hint)).ErrorCode);
    }

    [Theory]
    [InlineData(-1, 5, 11, 26, 23)]
    [InlineData(-.5, 5, 11, 26, 23)]
    [InlineData(-.01, 5, 11, 26, 23)]
    [InlineData(-.001, 5, 11, 26, 23)]
    [InlineData(-.0001, 5, 11, 26, 23)]
    [InlineData(0, 6, 11, 26, 22)]
    [InlineData(.0001, 6, 10, 27, 22)]
    [InlineData(.001, 6, 10, 27, 22)]
    [InlineData(.01, 6, 10, 27, 22)]
    [InlineData(.5, 6, 10, 27, 22)]
    [InlineData(1, 6, 10, 27, 22)]
    public void Source_capture_encloses_authored_offset_fringes_before_layer_scaling(double offset,
        float left, float top, float right, float bottom)
    {
        string dx = offset.ToString("R", CultureInfo.InvariantCulture), dy = (-offset).ToString("R", CultureInfo.InvariantCulture);
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>"
            + "<defs><filter id='f' filterUnits='userSpaceOnUse' x='-100' y='-100' width='200' height='200'>"
            + "<feOffset in='SourceAlpha' dx='" + dx + "' dy='" + dy + "'/></filter></defs><rect width='4' height='4' filter='url(#f)'/></svg>");
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
            SvgFilterResource resource = arena.ResourceAt(arena.BindingAt(scene.Commands[0].MaskContentIndex).ResourceIndex);
            SvgFilterRegionContext context = new(SvgAffineTransform.Identity, new(32, 32), new(-100, -100, 100, 100));
            RenderBudget scratch = new RenderBudget(new HostMemoryBudget(1_000_000));
            SvgPaintBounds hint = SvgFilterPlayback.SourceLayerBounds(resource, new(1, 1), context, new(6, 11, 26, 22),
                scratch, CancellationToken.None);
            // Native source capture encloses both source and shifted output in
            // parameter coordinates. Layer-space inset rounding belongs later.
            Assert.Equal(new SvgPaintBounds(left, top, right, bottom), SvgFilterPlayback.RoundParameterBounds(hint));
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }
}
