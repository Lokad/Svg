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
        if (start == end) return SvgNormalizedArc.CreateLine(start, end);
        double major = Math.Abs(radiusX);
        double minor = Math.Abs(radiusY);
        if (major == 0 || minor == 0) return SvgNormalizedArc.CreateLine(start, end);
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
        if (denominator == 0) return SvgNormalizedArc.CreateLine(start, end);
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
        return SvgNormalizedArc.CreateArc(start, new SvgDevicePoint(centerX, centerY), major, minor, rotation, startAngle, sweepAngle, end);
    }

    /// <summary>Appends the sampled arc polyline after its start, ending exactly at <see cref="SvgNormalizedArc.End"/>.</summary>
    /// <remarks>
    /// <para>Adaptive angular subdivision halves each angle interval while the mid-angle point deviates
    /// from the chord beyond the policy flatness. Interior samples lie on the ellipse; the final point
    /// is exactly <see cref="SvgNormalizedArc.End"/>, which the true curve reaches by construction.</para>
    /// </remarks>
    /// <returns>The number of appended points, which is at least one.</returns>
    internal static int Sample(SvgNormalizedArc arc, SvgFlattenPolicy policy, Span<SvgDevicePoint> output, CancellationToken cancellationToken)
    {
        if (arc.Kind != SvgArcKind.Line && arc.Kind != SvgArcKind.Arc) throw new ArgumentOutOfRangeException(nameof(arc));
        ValidatePoint(arc.Start, nameof(arc));
        ValidatePoint(arc.Center, nameof(arc));
        ValidatePoint(arc.End, nameof(arc));
        if (!double.IsFinite(arc.RadiusX) || !double.IsFinite(arc.RadiusY)) throw new ArgumentOutOfRangeException(nameof(arc));
        if (!double.IsFinite(arc.RotationRadians) || !double.IsFinite(arc.StartAngleRadians) || !double.IsFinite(arc.SweepRadians)) throw new ArgumentOutOfRangeException(nameof(arc));
        if (arc.Kind == SvgArcKind.Arc && (arc.RadiusX <= 0 || arc.RadiusY <= 0)) throw new ArgumentOutOfRangeException(nameof(arc));
        policy.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        int appended = 0;
        if (arc.Kind == SvgArcKind.Line)
        {
            AppendSample(output, ref appended, arc.End.X, arc.End.Y, policy, cancellationToken);
        }
        else
        {
            double endAngle = RequireFinite(arc.StartAngleRadians + arc.SweepRadians);
            SubdivideArc(arc, arc.StartAngleRadians, endAngle, 0, policy, output, ref appended, cancellationToken);
            output[appended - 1] = arc.End;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return appended;
    }

    private static void SubdivideArc(SvgNormalizedArc arc, double fromAngle, double toAngle, int depth, SvgFlattenPolicy policy, Span<SvgDevicePoint> output, ref int appended, CancellationToken cancellationToken)
    {
        double middleAngle = RequireFinite((fromAngle + toAngle) / 2);
        SvgDevicePoint from = ArcPoint(arc, fromAngle);
        SvgDevicePoint middle = ArcPoint(arc, middleAngle);
        SvgDevicePoint to = ArcPoint(arc, toAngle);
        if (ChordDeviation(middle, from, to) <= policy.Flatness)
        {
            AppendSample(output, ref appended, to.X, to.Y, policy, cancellationToken);
            return;
        }

        if (depth >= policy.MaximumDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "Elliptical arc sampling exceeds the supported subdivision depth.");
        SubdivideArc(arc, fromAngle, middleAngle, depth + 1, policy, output, ref appended, cancellationToken);
        SubdivideArc(arc, middleAngle, toAngle, depth + 1, policy, output, ref appended, cancellationToken);
    }

    private static SvgDevicePoint ArcPoint(SvgNormalizedArc arc, double angle)
    {
        double cosine = Math.Cos(angle);
        double sine = Math.Sin(angle);
        double unitX = RequireFinite(arc.RadiusX * cosine);
        double unitY = RequireFinite(arc.RadiusY * sine);
        double rotationCosine = Math.Cos(arc.RotationRadians);
        double rotationSine = Math.Sin(arc.RotationRadians);
        double rotatedX = RequireFinite(rotationCosine * unitX - rotationSine * unitY);
        double rotatedY = RequireFinite(rotationSine * unitX + rotationCosine * unitY);
        return new SvgDevicePoint(RequireFinite(arc.Center.X + rotatedX), RequireFinite(arc.Center.Y + rotatedY));
    }

    private static double ChordDeviation(SvgDevicePoint middle, SvgDevicePoint from, SvgDevicePoint to)
    {
        double chordX = RequireFinite(to.X - from.X);
        double chordY = RequireFinite(to.Y - from.Y);
        double armX = RequireFinite(middle.X - from.X);
        double armY = RequireFinite(middle.Y - from.Y);
        double chordSquared = RequireFinite(chordX * chordX + chordY * chordY);
        if (chordSquared == 0) return Math.Sqrt(RequireFinite(armX * armX + armY * armY));
        return RequireFinite(Math.Abs(chordX * armY - chordY * armX)) / Math.Sqrt(chordSquared);
    }

    private static void AppendSample(Span<SvgDevicePoint> output, ref int appended, double x, double y, SvgFlattenPolicy policy, CancellationToken cancellationToken)
    {
        if (appended >= policy.MaximumSegments || appended >= output.Length) throw new SvgException(SvgErrorCode.LimitExceeded, "Elliptical arc sampling exceeds the supported expansion range.");
        if ((appended & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
        output[appended] = new SvgDevicePoint(x, y);
        appended++;
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
internal readonly record struct SvgNormalizedArc(SvgArcKind Kind, SvgDevicePoint Start, SvgDevicePoint Center, double RadiusX, double RadiusY, double RotationRadians, double StartAngleRadians, double SweepRadians, SvgDevicePoint End)
{
    /// <summary>Creates the straight-line degeneration from <paramref name="start"/> to <paramref name="end"/>.</summary>
    internal static SvgNormalizedArc CreateLine(SvgDevicePoint start, SvgDevicePoint end)
    {
        return new SvgNormalizedArc(SvgArcKind.Line, start, new SvgDevicePoint(0, 0), 0, 0, 0, 0, 0, end);
    }

    /// <summary>Creates the genuine arc form with corrected radii and radian angles.</summary>
    internal static SvgNormalizedArc CreateArc(SvgDevicePoint start, SvgDevicePoint center, double radiusX, double radiusY, double rotationRadians, double startAngleRadians, double sweepRadians, SvgDevicePoint end)
    {
        return new SvgNormalizedArc(SvgArcKind.Arc, start, center, radiusX, radiusY, rotationRadians, startAngleRadians, sweepRadians, end);
    }
}