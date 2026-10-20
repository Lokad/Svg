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
    /// <returns>True on success, with the appended count in <paramref name="written"/>; false only when
    /// output is too short, with a zero count. Discard partial output on failure. Policy and numeric failures throw.</returns>
    internal static bool TrySample(SvgNormalizedArc arc, SvgFlattenPolicy policy, Span<SvgDevicePoint> output, CancellationToken cancellationToken, out int written)
    {
        written = 0;
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
            if (!AppendSample(output, ref appended, arc.End.X, arc.End.Y, policy, cancellationToken)) return false;
        }
        else
        {
            double endAngle = RequireFinite(arc.StartAngleRadians + arc.SweepRadians);
            if (!SubdivideArc(arc, arc.StartAngleRadians, endAngle, 0, policy, output, ref appended, cancellationToken)) return false;
            output[appended - 1] = arc.End;
        }

        cancellationToken.ThrowIfCancellationRequested();
        written = appended;
        return true;
    }

    private static bool SubdivideArc(SvgNormalizedArc arc, double fromAngle, double toAngle, int depth, SvgFlattenPolicy policy, Span<SvgDevicePoint> output, ref int appended, CancellationToken cancellationToken)
    {
        double middleAngle = RequireFinite((fromAngle + toAngle) / 2);
        SvgDevicePoint from = ArcPoint(arc, fromAngle);
        SvgDevicePoint middle = ArcPoint(arc, middleAngle);
        SvgDevicePoint to = ArcPoint(arc, toAngle);
        if (ChordDeviation(middle, from, to) <= policy.Flatness)
        {
            return AppendSample(output, ref appended, to.X, to.Y, policy, cancellationToken);
        }

        if (depth >= policy.MaximumDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "Elliptical arc sampling exceeds the supported subdivision depth.");
        return SubdivideArc(arc, fromAngle, middleAngle, depth + 1, policy, output, ref appended, cancellationToken)
            && SubdivideArc(arc, middleAngle, toAngle, depth + 1, policy, output, ref appended, cancellationToken);
    }

    /// <summary>Appends one full ellipse loop, starting exactly at the angle-zero origin.</summary>
    /// <remarks>
    /// <para>Traversal runs from angle zero toward increasing angles, matching the browser circle direction measured on the 512px single-dash control. Each quarter subdivides independently with the endpoint convention, so the loop carries no
    /// duplicate and its implicit closing edge is a tested chord. A flatness above the loop extent
    /// collapses each quarter to its right end, and rotation leads the four cardinal samples with the origin.</para>
    /// </remarks>
    /// <returns>True on success, with the appended count in <paramref name="written"/>; false only when
    /// output is too short, with a zero count. Discard partial output on failure. Policy and numeric failures throw.</returns>
    internal static bool TrySampleFull(SvgDevicePoint center, double radiusX, double radiusY, double rotationRadians, SvgFlattenPolicy policy, Span<SvgDevicePoint> output, CancellationToken cancellationToken, out int written)
    {
        written = 0;
        ValidatePoint(center, nameof(center));
        if (!double.IsFinite(radiusX) || !double.IsFinite(radiusY) || radiusX <= 0 || radiusY <= 0) throw new ArgumentOutOfRangeException(nameof(radiusX));
        if (!double.IsFinite(rotationRadians)) throw new ArgumentOutOfRangeException(nameof(rotationRadians));
        policy.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        SvgDevicePoint origin = EllipsePointAt(center.X, center.Y, radiusX, radiusY, rotationRadians, 0);
        SvgNormalizedArc loop = SvgNormalizedArc.CreateArc(origin, center, radiusX, radiusY, rotationRadians, 0, 2 * Math.PI, origin);
        int appended = 0;
        double quarter = Math.PI / 2;
        if (!SubdivideArc(loop, 0, quarter, 0, policy, output, ref appended, cancellationToken)
            || !SubdivideArc(loop, quarter, 2 * quarter, 0, policy, output, ref appended, cancellationToken)
            || !SubdivideArc(loop, 2 * quarter, 3 * quarter, 0, policy, output, ref appended, cancellationToken)
            || !SubdivideArc(loop, 3 * quarter, 4 * quarter, 0, policy, output, ref appended, cancellationToken)) return false;
        output[appended - 1] = origin;
        for (int index = appended - 1; index > 0; index--)
        {
            output[index] = output[index - 1];
        }

        output[0] = origin;
        cancellationToken.ThrowIfCancellationRequested();
        written = appended;
        return true;
    }

    /// <summary>Converts one arc section to cubic Bezier controls with matching endpoints.</summary>
    /// <remarks>The kappa construction scales each endpoint tangent by four-thirds tan(section / 4); sections beyond a quarter must be split by the caller. Maximum radial deviation stays 2.7e-4 of the radius, far below the coverage quantum, so the cubic walks the arc. A reversed section carries a negative arm that flips both tangents consistently.</remarks>
    internal static (SvgDevicePoint FirstControl, SvgDevicePoint SecondControl) CubicControls(SvgNormalizedArc arc, double fromAngle, double toAngle)
    {
        if (arc.Kind != SvgArcKind.Arc) throw new ArgumentOutOfRangeException(nameof(arc));
        if (!double.IsFinite(fromAngle) || !double.IsFinite(toAngle)) throw new ArgumentOutOfRangeException(nameof(fromAngle));
        double section = RequireFinite(toAngle - fromAngle);
        if (Math.Abs(section) > Math.PI / 2 + 1e-9) throw new ArgumentOutOfRangeException(nameof(toAngle));
        double arm = RequireFinite(4d / 3d * Math.Tan(section / 4));
        SvgDevicePoint start = EllipsePointAt(arc.Center.X, arc.Center.Y, arc.RadiusX, arc.RadiusY, arc.RotationRadians, fromAngle);
        SvgDevicePoint end = EllipsePointAt(arc.Center.X, arc.Center.Y, arc.RadiusX, arc.RadiusY, arc.RotationRadians, toAngle);
        (double startTX, double startTY) = ArcTangentAt(arc, fromAngle);
        (double endTX, double endTY) = ArcTangentAt(arc, toAngle);
        SvgDevicePoint first = new(RequireFinite(start.X + arm * startTX), RequireFinite(start.Y + arm * startTY));
        SvgDevicePoint second = new(RequireFinite(end.X - arm * endTX), RequireFinite(end.Y - arm * endTY));
        return (first, second);
    }

    private static (double X, double Y) ArcTangentAt(SvgNormalizedArc arc, double angle)
    {
        double sine = Math.Sin(angle);
        double cosine = Math.Cos(angle);
        double tangentX = RequireFinite(-arc.RadiusX * sine);
        double tangentY = RequireFinite(arc.RadiusY * cosine);
        double rotationCosine = Math.Cos(arc.RotationRadians);
        double rotationSine = Math.Sin(arc.RotationRadians);
        return (RequireFinite(rotationCosine * tangentX - rotationSine * tangentY), RequireFinite(rotationSine * tangentX + rotationCosine * tangentY));
    }

    private static SvgDevicePoint ArcPoint(SvgNormalizedArc arc, double angle)
    {
        return EllipsePointAt(arc.Center.X, arc.Center.Y, arc.RadiusX, arc.RadiusY, arc.RotationRadians, angle);
    }

    private static SvgDevicePoint EllipsePointAt(double centerX, double centerY, double radiusX, double radiusY, double rotationRadians, double angle)
    {
        double cosine = Math.Cos(angle);
        double sine = Math.Sin(angle);
        double unitX = RequireFinite(radiusX * cosine);
        double unitY = RequireFinite(radiusY * sine);
        double rotationCosine = Math.Cos(rotationRadians);
        double rotationSine = Math.Sin(rotationRadians);
        double rotatedX = RequireFinite(rotationCosine * unitX - rotationSine * unitY);
        double rotatedY = RequireFinite(rotationSine * unitX + rotationCosine * unitY);
        return new SvgDevicePoint(RequireFinite(centerX + rotatedX), RequireFinite(centerY + rotatedY));
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

    private static bool AppendSample(Span<SvgDevicePoint> output, ref int appended, double x, double y, SvgFlattenPolicy policy, CancellationToken cancellationToken)
    {
        if (appended >= policy.MaximumSegments) throw new SvgException(SvgErrorCode.LimitExceeded, "Elliptical arc sampling exceeds the supported expansion range.");
        if (appended >= output.Length) return false;
        if ((appended & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
        output[appended] = new SvgDevicePoint(x, y);
        appended++;
        return true;
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
