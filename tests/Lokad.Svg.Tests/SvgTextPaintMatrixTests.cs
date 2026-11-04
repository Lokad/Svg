using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgTextPaintMatrixTests
{
    [Fact]
    public void Physical_fragment_stages_retain_the_native_pattern_half_byte_boundary()
    {
        // Independent source/capture controls: Research/text-paint-bounds/fragment-stages.json.
        SvgAffineTransform parent = new(1, 0.25, 0.10000000149011612, 1, 1, 2);
        SvgTextShaderDrawing drawing = new(parent, new(0.97138491179582087, -0.23751074320001905, 0.23751074320001905, 0.97138491179582087, -12.496974333067485, 10.006951651313926), 1.0179636478424072f);
        SvgShaderMatrix canvas = drawing.AppendTo(SvgShaderMatrix.FromAffine(parent));
        SvgShaderMatrix local = SvgShaderMatrix.FromAffine(drawing.RebaseLocal(parent, SvgAffineTransform.Identity));
        SvgShaderCoordinates coordinates = SvgShaderCoordinates.CreateImage(canvas,
            SvgShaderMatrix.Concat(local, new(8f / 9f, 0, 0, 0, 8f / 9f, 0)));
        Assert.True(coordinates.TryMap(26, 25, default, out (float X, float Y) point));
        Assert.Equal(1104523580u, BitConverter.SingleToUInt32Bits(point.X));
        Assert.Equal(1045995008u, BitConverter.SingleToUInt32Bits(point.X + .5f - MathF.Floor(point.X + .5f)));
        Assert.Equal(parent.ComposeChecked(drawing.Fragment), drawing.Map);
    }

    [Theory]
    [InlineData(1, 0, 0, 1, 0, 0)]
    [InlineData(1, 0, 0, 1, 7, -3)]
    [InlineData(0, 1, -1, 0, 7, -3)]
    [InlineData(2, 0, 0, .5, 7, -3)]
    [InlineData(1, .5, 0, 1, 7, -3)]
    public void Glyph_local_paint_retains_reference_and_local_transform_order(double a, double b, double c, double d, double e, double f)
    {
        SvgAffineTransform glyph = new(a, b, c, d, e, f);
        SvgAffineTransform reference = new(2, 0, 0, 4, 10, 20);
        SvgAffineTransform pattern = new(1, 0, 0, 2, 3, 5);
        SvgAffineTransform local = new SvgTextShaderDrawing(glyph, SvgAffineTransform.Identity, 1).RebaseLocal(reference, pattern);
        Assert.Equal(reference.ComposeChecked(pattern), glyph.ComposeChecked(local));
    }

    [Fact]
    public void Glyph_local_paint_requires_an_invertible_drawing_map()
    {
        SvgException error = Assert.Throws<SvgException>(() => new SvgTextShaderDrawing(new(1, 0, 0, 0, 0, 0), SvgAffineTransform.Identity, 1).RebaseLocal(SvgAffineTransform.Identity, SvgAffineTransform.Identity));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
    }

    [Theory]
    [InlineData(.9355431263565993, -.3532124838208867, 3.994923152863189, 35.83285607913443, 12)]
    [InlineData(.9618854877665057, -.27345257070320494, 18.14732544371656, 30.48047414815534, 13)]
    public void Curved_glyph_stages_keep_float_texture_ceil_boundaries(double cosine, double sine, double x, double y, int pixels)
    {
        // The first two glyph maps from the retained grid-pattern control.
        // Flattening these stages to their mathematical identity loses the second boundary.
        SvgAffineTransform glyph = new(cosine, sine, -sine, cosine, x, y);
        SvgAffineTransform local = new SvgTextShaderDrawing(glyph, SvgAffineTransform.Identity, 1).RebaseLocal(SvgAffineTransform.Identity, SvgAffineTransform.Identity);
        SvgShaderMatrix draw = SvgShaderMatrix.Concat(new(1.5f, 0, 0, 0, 1.5f, 0), Narrow(glyph));
        SvgShaderMatrix total = SvgShaderMatrix.Concat(draw, Narrow(local));
        (float scaleX, float scaleY) = total.GetPictureScale();
        Assert.Equal(pixels, MathF.Ceiling(8 * scaleX));
        Assert.Equal(pixels, MathF.Ceiling(8 * scaleY));

        static SvgShaderMatrix Narrow(SvgAffineTransform matrix)
            => new((float)matrix.A, (float)matrix.C, (float)matrix.E, (float)matrix.B, (float)matrix.D, (float)matrix.F);
    }

    [Theory]
    [InlineData(-1, 12)]
    [InlineData(0, 12)]
    [InlineData(1, 13)]
    public void Picture_scale_keeps_both_sides_of_an_integral_texture_boundary(int adjacent, int pixels)
    {
        float scale = adjacent < 0 ? MathF.BitDecrement(1.5f) : adjacent > 0 ? MathF.BitIncrement(1.5f) : 1.5f;
        (float x, float y) = new SvgShaderMatrix(scale, 0, 0, 0, scale, 0).GetPictureScale();
        Assert.Equal(pixels, MathF.Ceiling(8 * x));
        Assert.Equal(pixels, MathF.Ceiling(8 * y));
    }
}
