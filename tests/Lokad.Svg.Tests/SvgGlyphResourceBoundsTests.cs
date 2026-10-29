using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGlyphResourceBoundsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Glyph_resources_use_quadratic_extrema_before_object_mapping(bool reflected)
    {
        SvgGlyphQuadraticContour[] contours = [new([new(new(0, 0), new(8, 16), new(16, 0))])];
        SvgAffineTransform map = reflected ? SvgAffineTransform.FromMatrix(-1, 0, 0, 1, 20, 0) : SvgAffineTransform.FromMatrix(1, 0, .5, 1, 3, 2);
        SvgRectangle bounds = SvgGlyphOutline.Bounds(contours, map, CancellationToken.None);
        Assert.Equal(reflected ? 4 : 3, bounds.X); Assert.Equal(reflected ? 0 : 2, bounds.Y);
        Assert.Equal(16, bounds.Width); Assert.Equal(8, bounds.Height);
        Assert.Equal(new SvgGlyphQuadraticSegment(new(0, 0), new(8, 16), new(16, 0)), Assert.Single(Assert.Single(contours).Segments));
    }

    [Fact]
    public void Glyph_bounds_keep_empty_outcomes_and_bounded_cancelable_visits()
    {
        Assert.Equal(default, SvgGlyphOutline.Bounds([], SvgAffineTransform.Identity, CancellationToken.None));
        SvgGlyphQuadraticContour[] contours = [new([new(new(0, 0), new(8, 16), new(16, 0))])];
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgGlyphOutline.Bounds(contours, SvgAffineTransform.Identity, cancellation.Token)).CancellationToken);
    }
}
