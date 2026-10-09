namespace Lokad.Svg.Rendering;

using Lokad.Svg.Parsing;

/// <summary>Maps contour vertices to device space with a general two-dimensional affine transform.</summary>
/// <remarks>
/// <para>Coefficients follow the SVG matrix(a b c d e f) convention: x prime equals a times x plus c times y plus e,
/// and y prime equals b times x plus d times y plus f. Composition applies the inner transform first:
/// outer.Compose(inner) maps every point exactly as outer.Apply(inner.Apply(point)).</para>
/// <para>Translation, scale (including negative reflection), rotation about an optional center and horizontal or
/// vertical skew about the origin. Factories reject non-finite inputs; Apply fails with
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

    /// <summary>Builds an axis-aligned transform without validation for already-checked integer geometry.</summary>
    internal static SvgAffineTransform FromScaleOffset(double scaleX, double scaleY, double offsetX, double offsetY)
    {
        return new SvgAffineTransform(scaleX, 0, 0, scaleY, offsetX, offsetY);
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

    /// <summary>Inverts this transform when its determinant is nonzero, reporting singular or overflowing maps as missing.</summary>
    internal bool TryInvert(out SvgAffineTransform inverse)
    {
        double determinant = Determinant;
        if (determinant == 0 || !double.IsFinite(determinant)) { inverse = Identity; return false; }
        double a = D / determinant;
        double b = -B / determinant;
        double c = -C / determinant;
        double d = A / determinant;
        double e = (C * F - D * E) / determinant;
        double f = (B * E - A * F) / determinant;
        if (!double.IsFinite(a) || !double.IsFinite(b) || !double.IsFinite(c) || !double.IsFinite(d) || !double.IsFinite(e) || !double.IsFinite(f)) { inverse = Identity; return false; }
        inverse = new SvgAffineTransform(a, b, c, d, e, f);
        return true;
    }

    /// <summary>Reports whether this transform maps axes to axes, keeping rectangles axis-aligned.</summary>
    internal bool IsAxisAligned => B == 0 && C == 0;

    /// <summary>Reports whether this transform scales uniformly, covering axis-aligned and uniform general transforms.</summary>
    internal bool IsUniformScale()
    {
        if (IsAxisAligned) return Math.Abs(A) == Math.Abs(D);
        return TryGetUniformScale(out _);
    }

    /// <summary>Composes two transforms with the integer-range discipline of axis-aligned composition.</summary>
    internal SvgAffineTransform ComposeChecked(SvgAffineTransform inner)
    {
        SvgAffineTransform composed = Compose(inner);
        if (composed.A < int.MinValue || composed.A > int.MaxValue || composed.B < int.MinValue || composed.B > int.MaxValue || composed.C < int.MinValue || composed.C > int.MaxValue || composed.D < int.MinValue || composed.D > int.MaxValue || composed.E < int.MinValue || composed.E > int.MaxValue || composed.F < int.MinValue || composed.F > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported transform exceeds the coordinate range.");
        return composed;
    }

    /// <summary>Reports the uniform scale factor when both columns are perpendicular and share a nonzero length.</summary>
    internal bool TryGetUniformScale(out double scale)
    {
        double first = Math.Sqrt(A * A + B * B);
        double second = Math.Sqrt(C * C + D * D);
        scale = first;
        if (!double.IsFinite(first) || !double.IsFinite(second)) return false;
        double magnitude = Math.Max(first, second);
        if (magnitude == 0) return false;
        if (Math.Abs(first - second) > 1e-9 * magnitude) return false;
        return Math.Abs(A * C + B * D) <= 1e-9 * magnitude * magnitude;
    }

    /// <summary>Applies this axis-aligned transform to the current rectangle geometry fast path, normalizing reflected extents.</summary>
    internal SvgRectangle Apply(SvgRectangle rectangle)
    {
        if (!IsAxisAligned) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned rectangle transforms are implemented.");
        double x = A * rectangle.X + E;
        double y = D * rectangle.Y + F;
        double width = A * rectangle.Width;
        double height = D * rectangle.Height;
        if (width < 0) { x += width; width = -width; }
        if (height < 0) { y += height; height = -height; }
        return rectangle with { X = x, Y = y, Width = width, Height = height, Clip = rectangle.Clip is {} clip ? Apply(clip) : null };
    }

    /// <summary>Applies this axis-aligned transform to a retained axis-aligned line, keeping the stroke width non-negative.</summary>
    internal SvgAxisAlignedLine Apply(SvgAxisAlignedLine line)
    {
        if (!IsAxisAligned) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned line transforms are implemented.");
        double strokeScale = line.X1 != line.X2 ? D : A;
        double scaledWidth = Math.Abs(strokeScale) * line.StrokeWidth;
        if (!double.IsFinite(scaledWidth) || scaledWidth != Math.Truncate(scaledWidth) || scaledWidth > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer effective line widths are implemented.");
        int strokeWidth = checked((int)scaledWidth);
        return line with
        {
            X1 = A * line.X1 + E,
            Y1 = D * line.Y1 + F,
            X2 = A * line.X2 + E,
            Y2 = D * line.Y2 + F,
            StrokeWidth = strokeWidth
        };
    }

    /// <summary>Applies this axis-aligned transform to a nested clip box, normalizing reflected extents.</summary>
    internal SvgClipBox Apply(SvgClipBox box)
    {
        if (!IsAxisAligned) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned clip transforms are implemented.");
        double x = A * box.X + E;
        double y = D * box.Y + F;
        double width = A * box.Width;
        double height = D * box.Height;
        SvgClipAntialiasing edges = box.Antialiasing;
        if (A < 0) edges = (edges & ~(SvgClipAntialiasing.Left | SvgClipAntialiasing.Right)) | ((edges & SvgClipAntialiasing.Left) != 0 ? SvgClipAntialiasing.Right : 0) | ((edges & SvgClipAntialiasing.Right) != 0 ? SvgClipAntialiasing.Left : 0);
        if (D < 0) edges = (edges & ~(SvgClipAntialiasing.Top | SvgClipAntialiasing.Bottom)) | ((edges & SvgClipAntialiasing.Top) != 0 ? SvgClipAntialiasing.Bottom : 0) | ((edges & SvgClipAntialiasing.Bottom) != 0 ? SvgClipAntialiasing.Top : 0);
        if (width < 0) { x += width; width = -width; }
        if (height < 0) { y += height; height = -height; }
        return new(x, y, width, height) { Antialiasing = edges };
    }

    /// <summary>Applies this axis-aligned transform to an integer orthogonal band while preserving its checked coordinate range.</summary>
    internal SvgRectangle ApplyOrthogonalRectangle(int x, int y, long width, long height, SvgColor color, bool hasFill)
    {
        if (!IsAxisAligned) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned orthogonal transforms are implemented.");
        double mappedX = ApplyAxisCoordinate(A, x, E);
        double mappedY = ApplyAxisCoordinate(D, y, F);
        double extentX = ApplyAxisExtent(A, width);
        double extentY = ApplyAxisExtent(D, height);
        if (extentX < 0) { mappedX = ShiftAxisCoordinate(mappedX, extentX); extentX = -extentX; }
        if (extentY < 0) { mappedY = ShiftAxisCoordinate(mappedY, extentY); extentY = -extentY; }
        return new(mappedX, mappedY, extentX, extentY, color, hasFill, null);
    }

    /// <summary>Maps one user-space gradient vector endpoint through this transform.</summary>
    internal (double X, double Y) ApplyToUserPoint(double x, double y)
    {
        double mappedX = A * x + C * y + E;
        double mappedY = B * x + D * y + F;
        if (!double.IsFinite(mappedX) || !double.IsFinite(mappedY)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported integer transform exceeds the gradient coordinate range.");
        return (mappedX, mappedY);
    }

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

    private static double ApplyAxisCoordinate(double scale, int value, double offset)
    {
        double mapped = scale * value + offset;
        if (!double.IsFinite(mapped) || mapped < int.MinValue || mapped > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported transform exceeds the coordinate range.");
        return mapped;
    }

    private static double ShiftAxisCoordinate(double coordinate, double extent)
    {
        double shifted = coordinate + extent;
        if (!double.IsFinite(shifted) || shifted < int.MinValue || shifted > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported transform exceeds the coordinate range.");
        return shifted;
    }

    private static double ApplyAxisExtent(double scale, long value)
    {
        double extent = scale * value;
        if (!double.IsFinite(extent) || extent < long.MinValue || extent > long.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported transform exceeds the coordinate range.");
        return extent;
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
