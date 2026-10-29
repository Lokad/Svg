using System.Globalization;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanisms")]
public sealed class SvgFilterSourceHintTests
{
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
        RenderBudget parser = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
            SvgFilterResource resource = arena.ResourceAt(arena.BindingAt(scene.Commands[0].MaskContentIndex).ResourceIndex);
            SvgFilterRegionContext context = new(SvgAffineTransform.Identity, new(32, 32), new(-100, -100, 100, 100));
            RenderBudget scratch = new(1_000_000);
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
