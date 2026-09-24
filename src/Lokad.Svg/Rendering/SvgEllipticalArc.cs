namespace Lokad.Svg.Rendering;

/// <summary>Classifies one normalized elliptical arc segment.</summary>
internal enum SvgArcKind
{
    /// <summary>The segment degenerates to the straight line from the arc start to <see cref="SvgNormalizedArc.End"/>.</summary>
    Line,

    /// <summary>The segment is a genuine elliptical arc around <see cref="SvgNormalizedArc.Center"/>.</summary>
    Arc,
}

/// <summary>Converts endpoint-parameterized elliptical arcs to center parameterization in device space.</summary>
/// <remarks>
/// <para>Implements the endpoint-to-center conversion with radii correction: absolute radii, the straight-line
/// fallback for zero radii, omission for identical endpoints, upscale of too-small radii, and the
/// large-arc/sweep center selection. Callers supply device-space points so rotation and correction apply
/// after the <see cref="SvgAffineTransform"/> mapping; downstream flattening samples the center form.</para>
/// <para>Non-finite inputs are caller contract violations reported as <see cref="ArgumentOutOfRangeException"/>.
/// Any non-finite intermediate, including overflowing corrections on extreme coordinates, fails with
/// <see cref="SvgErrorCode.LimitExceeded"/>. A slightly negative center radicand from rounding is clamped
/// to zero; genuinely negative values are unreachable because radii are corrected first.</para>
/// <para>This is pure geometry. Path-grammar parsing, flag validation and pixel fidelity are separate
/// concerns owned by later slices.</para>
/// </remarks>
internal static class SvgEllipticalArc
{
    /// <summary>Normalizes one endpoint arc to center form, or to a line for degenerate inputs.</summary>
    internal static SvgNormalizedArc Normalize(SvgDevicePoint start, double radiusX, double radiusY, double rotationDegrees, bool largeArc, bool sweep, SvgDevicePoint end)
    {
        ValidatePoint(start, nameof(start));
        ValidatePoint(end, nameof(end));
        if (!double.IsFinite(radiusX) || !double.IsFinite(radiusY)) throw new ArgumentOutOfRangeException(nameof(radiusX));
        if (!double.IsFinite(rotationDegrees)) throw new ArgumentOutOfRangeException(nameof(rotationDegrees));
        if (start == end) return SvgNormalizedArc.CreateLine(end);
        double major = Math.Abs(radiusX);
        double minor = Math.Abs(radiusY);
        if (major == 0 || minor == 0) return SvgNormalizedArc.CreateLine(end);
        double rotation = RequireFinite(rotationDegrees * (Math.PI / 180d));
        double cosine = Math.Cos(rotation);
        double sine = Math.Sin(rotation);
        double halfX = RequireFinite((start.X - end.X) / 2);
        double halfY = RequireFinite((start.Y - end.Y) / 2);
        double primeX = RequireFinite(cosine * halfX + sine * halfY);
        double primeY = RequireFinite(-sine * halfX + cosine * halfY);
        double lambda = RequireFinite(RequireFinite(primeX * primeX) / RequireFinite(major * major) + RequireFinite(primeY * primeY) / RequireFinite(minor * minor));
        if (lambda > 1)
        {
            double scale = Math.Sqrt(lambda);
            major = RequireFinite(scale * major);
            minor = RequireFinite(scale * minor);
        }

        double numerator = RequireFinite(RequireFinite(major * major) * RequireFinite(minor * minor) - RequireFinite(major * major) * RequireFinite(primeY * primeY) - RequireFinite(minor * minor) * RequireFinite(primeX * primeX));
        double denominator = RequireFinite(RequireFinite(major * major) * RequireFinite(primeY * primeY) + RequireFinite(minor * minor) * RequireFinite(primeX * primeX));
        if (denominator == 0) return SvgNormalizedArc.CreateLine(end);
        double factor = Math.Sqrt(Math.Max(numerator / denominator, 0));
        if (largeArc == sweep) factor = -factor;
        double centerPrimeX = RequireFinite(factor * RequireFinite(RequireFinite(major * primeY) / minor));
        double centerPrimeY = RequireFinite(factor * RequireFinite(-RequireFinite(minor * primeX) / major));
        double middleX = RequireFinite((start.X + end.X) / 2);
        double middleY = RequireFinite((start.Y + end.Y) / 2);
        double centerX = RequireFinite(middleX + cosine * centerPrimeX - sine * centerPrimeY);
        double centerY = RequireFinite(middleY + sine * centerPrimeX + cosine * centerPrimeY);
        double startAngle = RequireFinite(Math.Atan2(RequireFinite((primeY - centerPrimeY) / minor), RequireFinite((primeX - centerPrimeX) / major)));
        double cross = RequireFinite(RequireFinite((primeX - centerPrimeX) / major) * RequireFinite((-primeY - centerPrimeY) / minor) - RequireFinite((primeY - centerPrimeY) / minor) * RequireFinite((-primeX - centerPrimeX) / major));
        double dot = RequireFinite(RequireFinite((primeX - centerPrimeX) / major) * RequireFinite((-primeX - centerPrimeX) / major) + RequireFinite((primeY - centerPrimeY) / minor) * RequireFinite((-primeY - centerPrimeY) / minor));
        double sweepAngle = Math.Atan2(cross, dot);
        if (!sweep && sweepAngle > 0) sweepAngle -= 2 * Math.PI;
        else if (sweep && sweepAngle < 0) sweepAngle += 2 * Math.PI;
        return SvgNormalizedArc.CreateArc(new SvgDevicePoint(centerX, centerY), major, minor, rotation, startAngle, sweepAngle, end);
    }

    private static void ValidatePoint(SvgDevicePoint point, string parameterName)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new ArgumentOutOfRangeException(parameterName);
    }

    private static double RequireFinite(double value)
    {
        if (!double.IsFinite(value)) throw new SvgException(SvgErrorCode.LimitExceeded, "Elliptical arc normalization exceeds the admitted numeric range.");
        return value;
    }
}

/// <summary>One endpoint arc in center parameterization, or its straight-line degeneration.</summary>
internal readonly record struct SvgNormalizedArc(SvgArcKind Kind, SvgDevicePoint Center, double RadiusX, double RadiusY, double RotationRadians, double StartAngleRadians, double SweepRadians, SvgDevicePoint End)
{
    /// <summary>Creates the straight-line degeneration ending at <paramref name="end"/>.</summary>
    internal static SvgNormalizedArc CreateLine(SvgDevicePoint end)
    {
        return new SvgNormalizedArc(SvgArcKind.Line, new SvgDevicePoint(0, 0), 0, 0, 0, 0, 0, end);
    }

    /// <summary>Creates the genuine arc form with corrected radii and radian angles.</summary>
    internal static SvgNormalizedArc CreateArc(SvgDevicePoint center, double radiusX, double radiusY, double rotationRadians, double startAngleRadians, double sweepRadians, SvgDevicePoint end)
    {
        return new SvgNormalizedArc(SvgArcKind.Arc, center, radiusX, radiusY, rotationRadians, startAngleRadians, sweepRadians, end);
    }
}