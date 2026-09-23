namespace Lokad.Svg.Rendering;

using Lokad.Svg.Parsing;

/// <summary>Represents one contour vertex mapped to double device space.</summary>
internal readonly record struct SvgDevicePoint(double X, double Y);

/// <summary>Maps integer user-space contour vertices to device space with an axis-aligned scale and offset.</summary>
/// <remarks>
/// <para>This covers viewBox/output scaling, translation and reflection of contour geometry. Rotation and skew
/// have no admitted producer yet; they must not be smuggled in through these scales.</para>
/// <para>Non-finite results fail as <see cref="SvgErrorCode.LimitExceeded"/>, matching the render backend edge
/// mapping. Integer inputs convert exactly; the offset addition may round.</para>
/// </remarks>
internal readonly record struct SvgDeviceTransform(double ScaleX, double OffsetX, double ScaleY, double OffsetY)
{
    /// <summary>Maps one integer contour vertex to device space.</summary>
    internal SvgDevicePoint Apply(SvgPoint point)
    {
        double x = ScaleX * point.X + OffsetX;
        double y = ScaleY * point.Y + OffsetY;
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
        return new SvgDevicePoint(x, y);
    }
}
