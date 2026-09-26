namespace Lokad.Svg.Rendering;

/// <summary>Flattens device-space quadratic and cubic curve segments into bounded polylines.</summary>
/// <remarks>
/// <para>Adaptive midpoint subdivision halves a segment while its control deviation from the chord exceeds
/// the policy flatness. Callers map control points through <see cref="SvgAffineTransform"/> first; this
/// kernel works in device space so the flatness bound reads directly in device pixels.</para>
/// <para>Expansion is bounded twice: the policy segment cap and the caller buffer capacity. Exhausting the
/// subdivision depth or either expansion bound fails with <see cref="SvgErrorCode.LimitExceeded"/>, as does
/// any non-finite intermediate. Non-finite control points and invalid policies are caller contract
/// violations reported as <see cref="ArgumentOutOfRangeException"/>.</para>
/// <para>This is pure geometry. Coverage quantization and pixel fidelity of flattened curves are separate
/// calibration concerns. Callers charge one curve-expansion work unit per appended segment; this kernel
/// holds no budget itself and allocates nothing.</para>
/// </remarks>
internal static class SvgCurveFlatten
{
    /// <summary>Backstop subdivision depth, far above the levels finite device geometry can require.</summary>
    internal const int MaximumSupportedDepth = 1024;

    /// <summary>Appends the flattened quadratic polyline after <paramref name="start"/>, ending exactly at <paramref name="end"/>.</summary>
    /// <returns>The number of appended points, which is at least one.</returns>
    internal static int FlattenQuadratic(SvgDevicePoint start, SvgDevicePoint control, SvgDevicePoint end, SvgFlattenPolicy policy, Span<SvgDevicePoint> output, CancellationToken cancellationToken)
    {
        ValidatePoint(start, nameof(start));
        ValidatePoint(control, nameof(control));
        ValidatePoint(end, nameof(end));
        policy.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        int appended = 0;
        SubdivideQuadratic(start.X, start.Y, control.X, control.Y, end.X, end.Y, 0, policy, output, ref appended, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return appended;
    }

    /// <summary>Appends the flattened cubic polyline after <paramref name="start"/>, ending exactly at <paramref name="end"/>.</summary>
    /// <returns>The number of appended points, which is at least one.</returns>
    internal static int FlattenCubic(SvgDevicePoint start, SvgDevicePoint firstControl, SvgDevicePoint secondControl, SvgDevicePoint end, SvgFlattenPolicy policy, Span<SvgDevicePoint> output, CancellationToken cancellationToken)
    {
        ValidatePoint(start, nameof(start));
        ValidatePoint(firstControl, nameof(firstControl));
        ValidatePoint(secondControl, nameof(secondControl));
        ValidatePoint(end, nameof(end));
        policy.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        int appended = 0;
        SubdivideCubic(start.X, start.Y, firstControl.X, firstControl.Y, secondControl.X, secondControl.Y, end.X, end.Y, 0, policy, output, ref appended, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return appended;
    }

    private static void SubdivideQuadratic(double startX, double startY, double controlX, double controlY, double endX, double endY, int depth, SvgFlattenPolicy policy, Span<SvgDevicePoint> output, ref int appended, CancellationToken cancellationToken)
    {
        if (QuadraticDeviation(startX, startY, controlX, controlY, endX, endY) <= policy.Flatness)
        {
            AppendPoint(output, ref appended, endX, endY, policy, cancellationToken);
            return;
        }

        if (depth >= policy.MaximumDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "Curve flattening exceeds the supported subdivision depth.");
        double leftX = RequireFinite((startX + controlX) / 2);
        double leftY = RequireFinite((startY + controlY) / 2);
        double rightX = RequireFinite((controlX + endX) / 2);
        double rightY = RequireFinite((controlY + endY) / 2);
        double middleX = RequireFinite((leftX + rightX) / 2);
        double middleY = RequireFinite((leftY + rightY) / 2);
        SubdivideQuadratic(startX, startY, leftX, leftY, middleX, middleY, depth + 1, policy, output, ref appended, cancellationToken);
        SubdivideQuadratic(middleX, middleY, rightX, rightY, endX, endY, depth + 1, policy, output, ref appended, cancellationToken);
    }

    private static void SubdivideCubic(double startX, double startY, double firstControlX, double firstControlY, double secondControlX, double secondControlY, double endX, double endY, int depth, SvgFlattenPolicy policy, Span<SvgDevicePoint> output, ref int appended, CancellationToken cancellationToken)
    {
        if (CubicDeviation(startX, startY, firstControlX, firstControlY, secondControlX, secondControlY, endX, endY) <= policy.Flatness)
        {
            AppendPoint(output, ref appended, endX, endY, policy, cancellationToken);
            return;
        }

        if (depth >= policy.MaximumDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "Curve flattening exceeds the supported subdivision depth.");
        double leftX = RequireFinite((startX + firstControlX) / 2);
        double leftY = RequireFinite((startY + firstControlY) / 2);
        double middleX = RequireFinite((firstControlX + secondControlX) / 2);
        double middleY = RequireFinite((firstControlY + secondControlY) / 2);
        double rightX = RequireFinite((secondControlX + endX) / 2);
        double rightY = RequireFinite((secondControlY + endY) / 2);
        double leftMidX = RequireFinite((leftX + middleX) / 2);
        double leftMidY = RequireFinite((leftY + middleY) / 2);
        double rightMidX = RequireFinite((middleX + rightX) / 2);
        double rightMidY = RequireFinite((middleY + rightY) / 2);
        double splitX = RequireFinite((leftMidX + rightMidX) / 2);
        double splitY = RequireFinite((leftMidY + rightMidY) / 2);
        SubdivideCubic(startX, startY, leftX, leftY, leftMidX, leftMidY, splitX, splitY, depth + 1, policy, output, ref appended, cancellationToken);
        SubdivideCubic(splitX, splitY, rightMidX, rightMidY, rightX, rightY, endX, endY, depth + 1, policy, output, ref appended, cancellationToken);
    }

    private static double QuadraticDeviation(double startX, double startY, double controlX, double controlY, double endX, double endY)
    {
        double chordX = RequireFinite(endX - startX);
        double chordY = RequireFinite(endY - startY);
        double armX = RequireFinite(controlX - startX);
        double armY = RequireFinite(controlY - startY);
        double chordSquared = RequireFinite(chordX * chordX + chordY * chordY);
        if (chordSquared == 0) return Math.Sqrt(RequireFinite(armX * armX + armY * armY));
        double chordLength = Math.Sqrt(chordSquared);
        double across = RequireFinite(Math.Abs(chordX * armY - chordY * armX)) / chordLength;
        return Math.Max(across, SegmentOvershoot(chordX, chordY, armX, armY, chordSquared, chordLength));
    }

    private static double CubicDeviation(double startX, double startY, double firstControlX, double firstControlY, double secondControlX, double secondControlY, double endX, double endY)
    {
        double chordX = RequireFinite(endX - startX);
        double chordY = RequireFinite(endY - startY);
        double firstX = RequireFinite(firstControlX - startX);
        double firstY = RequireFinite(firstControlY - startY);
        double secondX = RequireFinite(secondControlX - startX);
        double secondY = RequireFinite(secondControlY - startY);
        double chordSquared = RequireFinite(chordX * chordX + chordY * chordY);
        if (chordSquared == 0)
        {
            double firstSquared = RequireFinite(firstX * firstX + firstY * firstY);
            double secondSquared = RequireFinite(secondX * secondX + secondY * secondY);
            return Math.Sqrt(RequireFinite(Math.Max(firstSquared, secondSquared)));
        }

        double chordLength = Math.Sqrt(chordSquared);
        double first = RequireFinite(Math.Abs(chordX * firstY - chordY * firstX)) / chordLength;
        double second = RequireFinite(Math.Abs(chordX * secondY - chordY * secondX)) / chordLength;
        double overshoot = Math.Max(SegmentOvershoot(chordX, chordY, firstX, firstY, chordSquared, chordLength), SegmentOvershoot(chordX, chordY, secondX, secondY, chordSquared, chordLength));
        return Math.Max(Math.Max(first, second), overshoot);
    }

    private static double SegmentOvershoot(double chordX, double chordY, double armX, double armY, double chordSquared, double chordLength)
    {
        double along = RequireFinite(RequireFinite(chordX * armX) + RequireFinite(chordY * armY));
        double beyond = along < 0 ? RequireFinite(-along) : (along > chordSquared ? RequireFinite(along - chordSquared) : 0);
        return RequireFinite(beyond) / chordLength;
    }

    private static void AppendPoint(Span<SvgDevicePoint> output, ref int appended, double x, double y, SvgFlattenPolicy policy, CancellationToken cancellationToken)
    {
        if (appended >= policy.MaximumSegments || appended >= output.Length) throw new SvgException(SvgErrorCode.LimitExceeded, "Curve flattening exceeds the supported expansion range.");
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
        if (!double.IsFinite(value)) throw new SvgException(SvgErrorCode.LimitExceeded, "Curve flattening exceeds the admitted numeric range.");
        return value;
    }
}

/// <summary>Bounds one curve-flattening expansion in device space.</summary>
/// <remarks>
/// <para><see cref="Flatness"/> caps the admitted control deviation of every emitted chord in device
/// pixels. <see cref="MaximumDepth"/> is a recursion backstop finite inputs converge far below;
/// <see cref="MaximumSegments"/> caps appended points alongside the caller buffer capacity.</para>
/// </remarks>
internal readonly record struct SvgFlattenPolicy
{
    /// <summary>Initializes a flattening bound with explicit error and work limits.</summary>
    internal SvgFlattenPolicy(double flatness, int maximumDepth, int maximumSegments)
    {
        Flatness = flatness;
        MaximumDepth = maximumDepth;
        MaximumSegments = maximumSegments;
        Validate();
    }

    /// <summary>Gets the maximum admitted control deviation of one emitted chord in device pixels.</summary>
    internal double Flatness { get; }

    /// <summary>Gets the backstop subdivision depth.</summary>
    internal int MaximumDepth { get; }

    /// <summary>Gets the maximum appended points alongside the caller buffer capacity.</summary>
    internal int MaximumSegments { get; }

    /// <summary>Validates the bound, including default-constructed instances which carry zero flatness.</summary>
    internal void Validate()
    {
        if (!double.IsFinite(Flatness) || Flatness <= 0) throw new ArgumentOutOfRangeException(nameof(Flatness));
        if (MaximumDepth < 1 || MaximumDepth > SvgCurveFlatten.MaximumSupportedDepth) throw new ArgumentOutOfRangeException(nameof(MaximumDepth));
        if (MaximumSegments < 1) throw new ArgumentOutOfRangeException(nameof(MaximumSegments));
    }
}