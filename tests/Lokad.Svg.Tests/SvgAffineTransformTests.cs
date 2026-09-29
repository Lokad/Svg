namespace Lokad.Svg.Tests;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

public sealed class SvgAffineTransformTests
{
    [Fact]
    public void Identity_maps_integer_vertices_exactly()
    {
        Assert.Equal(new SvgDevicePoint(0, 0), SvgAffineTransform.Identity.Apply(new SvgPoint(0, 0)));
        Assert.Equal(new SvgDevicePoint(3, -4), SvgAffineTransform.Identity.Apply(new SvgPoint(3, -4)));
        Assert.Equal(new SvgDevicePoint(int.MaxValue, int.MinValue), SvgAffineTransform.Identity.Apply(new SvgPoint(int.MaxValue, int.MinValue)));
    }

    [Fact]
    public void Translation_is_exact()
    {
        SvgAffineTransform transform = SvgAffineTransform.FromTranslation(3, -4);

        Assert.Equal(new SvgDevicePoint(4, -2), transform.Apply(new SvgPoint(1, 2)));
        Assert.Equal(new SvgDevicePoint(3, -4), transform.Apply(new SvgPoint(0, 0)));
    }

    [Fact]
    public void Scale_and_reflection_are_exact()
    {
        Assert.Equal(new SvgDevicePoint(10, -21), SvgAffineTransform.FromScale(2, -3).Apply(new SvgPoint(5, 7)));
        Assert.Equal(new SvgDevicePoint(0, 45), SvgAffineTransform.FromScale(0, 5).Apply(new SvgPoint(9, 9)));
    }

    [Fact]
    public void Explicit_matrix_is_exact()
    {
        SvgAffineTransform transform = SvgAffineTransform.FromMatrix(1, 2, 3, 4, 5, 6);

        Assert.Equal(new SvgDevicePoint(36, 52), transform.Apply(new SvgPoint(7, 8)));
    }

    [Fact]
    public void Fractional_coordinates_map_exactly()
    {
        Assert.Equal(new SvgDevicePoint(1.75, 2), SvgAffineTransform.FromTranslation(0.25, -0.5).Apply(1.5, 2.5));
    }

    [Fact]
    public void Compose_applies_the_inner_transform_first()
    {
        SvgAffineTransform translation = SvgAffineTransform.FromTranslation(3, 4);
        SvgAffineTransform scale = SvgAffineTransform.FromScale(2, 5);

        Assert.Equal(new SvgDevicePoint(5, 9), translation.Compose(scale).Apply(new SvgPoint(1, 1)));
        Assert.Equal(new SvgDevicePoint(8, 25), scale.Compose(translation).Apply(new SvgPoint(1, 1)));
    }

    [Fact]
    public void Compose_is_associative_on_small_integers()
    {
        SvgAffineTransform first = SvgAffineTransform.FromMatrix(1, 2, 0, 1, 3, 4);
        SvgAffineTransform second = SvgAffineTransform.FromScale(2, 3);
        SvgAffineTransform third = SvgAffineTransform.FromTranslation(5, 6);
        SvgPoint point = new(7, 8);

        Assert.Equal(first.Compose(second.Compose(third)).Apply(point), first.Compose(second).Compose(third).Apply(point));
    }

    [Fact]
    public void Rotation_fixes_the_origin_for_any_angle()
    {
        Assert.Equal(new SvgDevicePoint(0, 0), SvgAffineTransform.FromRotationDegrees(45).Apply(new SvgPoint(0, 0)));
        Assert.Equal(new SvgDevicePoint(0, 0), SvgAffineTransform.FromRotationDegrees(90).Apply(new SvgPoint(0, 0)));
        Assert.Equal(new SvgDevicePoint(0, 0), SvgAffineTransform.FromRotationDegrees(180).Apply(new SvgPoint(0, 0)));
        Assert.Equal(new SvgDevicePoint(0, 0), SvgAffineTransform.FromSkewXDegrees(45).Apply(new SvgPoint(0, 0)));
        Assert.Equal(new SvgDevicePoint(0, 0), SvgAffineTransform.FromSkewYDegrees(45).Apply(new SvgPoint(0, 0)));
    }

    [Fact]
    public void Quarter_turn_maps_axes_with_exact_sine()
    {
        SvgDevicePoint turned = SvgAffineTransform.FromRotationDegrees(90).Apply(new SvgPoint(2, 0));

        Assert.Equal(2.0, turned.Y);
        Assert.Equal(2.0 * System.Math.Cos(System.Math.PI / 2), turned.X);
        Assert.Equal(-2.0, SvgAffineTransform.FromRotationDegrees(-90).Apply(new SvgPoint(2, 0)).Y);
    }

    [Fact]
    public void Skew_shears_one_axis_only()
    {
        Assert.Equal(SvgAffineTransform.Identity, SvgAffineTransform.FromSkewXDegrees(0));
        Assert.Equal(SvgAffineTransform.Identity, SvgAffineTransform.FromSkewYDegrees(0));

        SvgDevicePoint shearedX = SvgAffineTransform.FromSkewXDegrees(45).Apply(new SvgPoint(3, 7));
        Assert.Equal(7.0, shearedX.Y);
        Assert.Equal(3.0 + System.Math.Tan(System.Math.PI / 4) * 7.0, shearedX.X);

        SvgDevicePoint shearedY = SvgAffineTransform.FromSkewYDegrees(45).Apply(new SvgPoint(3, 7));
        Assert.Equal(3.0, shearedY.X);
        Assert.Equal(7.0 + System.Math.Tan(System.Math.PI / 4) * 3.0, shearedY.Y);
    }

    [Fact]
    public void Rotation_about_center_matches_translate_sandwich()
    {
        SvgAffineTransform centered = SvgAffineTransform.FromRotationDegrees(30, 4, 5);
        SvgAffineTransform sandwich = SvgAffineTransform.FromTranslation(4, 5).Compose(SvgAffineTransform.FromRotationDegrees(30)).Compose(SvgAffineTransform.FromTranslation(-4, -5));

        Assert.Equal(sandwich, centered);
    }

    [Fact]
    public void Determinant_is_exact_for_small_integers()
    {
        Assert.Equal(1.0, SvgAffineTransform.Identity.Determinant);
        Assert.Equal(1.0, SvgAffineTransform.FromTranslation(3, 4).Determinant);
        Assert.Equal(10.0, SvgAffineTransform.FromScale(2, 5).Determinant);
        Assert.Equal(0.0, SvgAffineTransform.FromScale(0, 5).Determinant);
    }

    [Fact]
    public void Singular_scales_map_without_throwing()
    {
        Assert.Equal(new SvgDevicePoint(0, 45), SvgAffineTransform.FromScale(0, 5).Apply(new SvgPoint(9, 9)));
    }

    public static System.Collections.Generic.IEnumerable<object[]> NonFiniteValues()
    {
        yield return new object[] { double.NaN };
        yield return new object[] { double.PositiveInfinity };
        yield return new object[] { double.NegativeInfinity };
    }

    [Theory]
    [MemberData(nameof(NonFiniteValues))]
    public void Factories_reject_non_finite_inputs(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgAffineTransform.FromTranslation(value, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgAffineTransform.FromScale(value, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgAffineTransform.FromRotationDegrees(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgAffineTransform.FromRotationDegrees(value, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgAffineTransform.FromSkewXDegrees(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgAffineTransform.FromSkewYDegrees(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgAffineTransform.FromMatrix(value, 0, 0, 1, 0, 0));
    }

    [Fact]
    public void Compose_rejects_overflowed_coefficients()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgAffineTransform.FromScale(1e308, 1).Compose(SvgAffineTransform.FromScale(10, 1)));
    }

    [Fact]
    public void Apply_rejects_non_finite_results()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgAffineTransform.FromScale(1e308, 1).Apply(new SvgPoint(10, 0)));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Axis_aligned_reports_axis_flags()
    {
        Assert.True(SvgAffineTransform.Identity.IsAxisAligned);
        Assert.True(SvgAffineTransform.FromScale(2, -3).IsAxisAligned);
        Assert.False(SvgAffineTransform.FromRotationDegrees(30).IsAxisAligned);
        Assert.False(SvgAffineTransform.FromSkewXDegrees(10).IsAxisAligned);
    }

    [Fact]
    public void Uniform_detection_covers_rotations_and_rejects_stretch()
    {
        Assert.True(SvgAffineTransform.Identity.IsUniformScale());
        Assert.True(SvgAffineTransform.FromScale(2, 2).IsUniformScale());
        Assert.True(SvgAffineTransform.FromScale(2, -2).IsUniformScale());
        Assert.True(SvgAffineTransform.FromRotationDegrees(30).IsUniformScale());
        Assert.False(SvgAffineTransform.FromScale(2, 3).IsUniformScale());
        Assert.True(SvgAffineTransform.FromRotationDegrees(30).TryGetUniformScale(out double factor));
        Assert.Equal(1, factor, 9);
    }

    [Fact]
    public void Compose_checked_rejects_out_of_range_results()
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgAffineTransform.FromScale(1e308, 1).ComposeChecked(SvgAffineTransform.FromScale(1, 1)));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Inversion_covers_identity_translation_scale_and_rotation()
    {
        Assert.True(SvgAffineTransform.Identity.TryInvert(out SvgAffineTransform identityInverse));
        Assert.Equal(SvgAffineTransform.Identity, identityInverse);
        SvgAffineTransform translation = SvgAffineTransform.FromTranslation(3, -4);
        Assert.True(translation.TryInvert(out SvgAffineTransform translationInverse));
        SvgDevicePoint translated = translation.Apply(new SvgPoint(5, 7));
        Assert.Equal(new SvgDevicePoint(5, 7), translationInverse.Apply(translated.X, translated.Y));
        SvgAffineTransform scale = SvgAffineTransform.FromScale(2, -3);
        Assert.True(scale.TryInvert(out SvgAffineTransform scaleInverse));
        SvgDevicePoint scaled = scale.Apply(new SvgPoint(5, 7));
        Assert.Equal(new SvgDevicePoint(5, 7), scaleInverse.Apply(scaled.X, scaled.Y));
        SvgAffineTransform rotation = SvgAffineTransform.FromRotationDegrees(30);
        Assert.True(rotation.TryInvert(out SvgAffineTransform rotationInverse));
        SvgDevicePoint rotated = rotation.Apply(new SvgPoint(5, 7));
        SvgDevicePoint roundTripped = rotationInverse.Apply(rotated.X, rotated.Y);
        Assert.Equal(5, roundTripped.X, 9);
        Assert.Equal(7, roundTripped.Y, 9);
    }

    [Fact]
    public void Inversion_rejects_singular_and_overflowing_maps()
    {
        Assert.False(SvgAffineTransform.FromScale(0, 5).TryInvert(out _));
        Assert.False(SvgAffineTransform.FromScale(1e308, 1e308).TryInvert(out _));
    }
}
