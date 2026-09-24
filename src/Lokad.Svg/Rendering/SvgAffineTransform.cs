namespace Lokad.Svg.Rendering;

using Lokad.Svg.Parsing;

/// <summary>Maps contour vertices to device space with a general two-dimensional affine transform.</summary>
/// <remarks>
/// <para>Coefficients follow the SVG matrix(a b c d e f) convention: x prime equals a times x plus c times y plus e,
/// and y prime equals b times x plus d times y plus f. Composition applies the inner transform first:
/// outer.Compose(inner) maps every point exactly as outer.Apply(inner.Apply(point)).</para>
/// <para>Translation, scale (including negative reflection), rotation about an optional center and horizontal or
/// vertical skew compose in that listed order. Factories reject non-finite inputs; Apply fails with
/// <see cref="SvgErrorCode.LimitExceeded"/> when a mapped coordinate is not finite. Singular transforms are admitted
/// and collapse dimensions; downstream zero-area rules own that geometry.</para>
/// </remarks>
internal readonly record struct SvgAffineTransform(double A, double B, double C, double D, double E, double F)
{
    internal static SvgAffineTransform Identity => new(1, 0, 0, 1, 0, 0);

    internal static SvgAffineTransform FromTranslation(double offsetX, double offsetY)
    {
        ValidateFinite(offsetX, nameof(offsetX));
        ValidateFinite(offsetY, nameof(offsetY));
        return new SvgAffineTransform(1, 0, 0, 1, offsetX, offsetY);
    }

    internal static SvgAffineTransform FromScale(double scaleX, double scaleY)
    {
        ValidateFinite(scaleX, nameof(scaleX));
        ValidateFinite(scaleY, nameof(scaleY));
        return new SvgAffineTransform(scaleX, 0, 0, scaleY, 0, 0);
    }

    internal static SvgAffineTransform FromRotationDegrees(double angleDegrees)
    {
        return FromRotationDegrees(angleDegrees, 0, 0);
    }

    internal static SvgAffineTransform FromRotationDegrees(double angleDegrees, double centerX, double centerY)
    {
        ValidateFinite(angleDegrees, nameof(angleDegrees));
        ValidateFinite(centerX, nameof(centerX));
        ValidateFinite(centerY, nameof(centerY));
        double radians = angleDegrees * (Math.PI / 180d);
        double cosine = Math.Cos(radians);
        double sine = Math.Sin(radians);
        return CreateChecked(cosine, sine, -sine, cosine, centerX - cosine * centerX + sine * centerY, centerY - sine * centerX - cosine * centerY);
    }

    internal static SvgAffineTransform FromSkewXDegrees(double angleDegrees)
    {
        ValidateFinite(angleDegrees, nameof(angleDegrees));
        double slope = Math.Tan(angleDegrees * (Math.PI / 180d));
        ValidateFinite(slope, nameof(angleDegrees));
        return new SvgAffineTransform(1, 0, slope, 1, 0, 0);
    }

    internal static SvgAffineTransform FromSkewYDegrees(double angleDegrees)
    {
        ValidateFinite(angleDegrees, nameof(angleDegrees));
        double slope = Math.Tan(angleDegrees * (Math.PI / 180d));
        ValidateFinite(slope, nameof(angleDegrees));
        return new SvgAffineTransform(1, slope, 0, 1, 0, 0);
    }

    internal static SvgAffineTransform FromMatrix(double a, double b, double c, double d, double e, double f)
    {
        return CreateChecked(a, b, c, d, e, f);
    }

    internal SvgAffineTransform Compose(SvgAffineTransform inner)
    {
        return CreateChecked(
            A * inner.A + C * inner.B,
            B * inner.A + D * inner.B,
            A * inner.C + C * inner.D,
            B * inner.C + D * inner.D,
            A * inner.E + C * inner.F + E,
            B * inner.E + D * inner.F + F);
    }

    internal double Determinant => A * D - B * C;

    internal SvgDevicePoint Apply(SvgPoint point)
    {
        return Apply(point.X, point.Y);
    }

    internal SvgDevicePoint Apply(double x, double y)
    {
        double mappedX = A * x + C * y + E;
        double mappedY = B * x + D * y + F;
        if (!double.IsFinite(mappedX) || !double.IsFinite(mappedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
        return new SvgDevicePoint(mappedX, mappedY);
    }

    private static SvgAffineTransform CreateChecked(double a, double b, double c, double d, double e, double f)
    {
        ValidateFinite(a, nameof(a));
        ValidateFinite(b, nameof(b));
        ValidateFinite(c, nameof(c));
        ValidateFinite(d, nameof(d));
        ValidateFinite(e, nameof(e));
        ValidateFinite(f, nameof(f));
        return new SvgAffineTransform(a, b, c, d, e, f);
    }

    private static void ValidateFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(parameterName);
    }
}
