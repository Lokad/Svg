namespace Lokad.Svg.Parsing;

/// <summary>Represents the calibrated integer transform subset and its geometry application.</summary>
internal readonly record struct AxisTransform(int ScaleX, int ScaleY, int OffsetX, int OffsetY)
{
    internal static AxisTransform Identity { get; } = new(1, 1, 0, 0);

    internal AxisTransform Compose(AxisTransform child) => new(Multiply(ScaleX, child.ScaleX), Multiply(ScaleY, child.ScaleY), Offset(ScaleX, child.OffsetX, OffsetX), Offset(ScaleY, child.OffsetY, OffsetY));

    /// <summary>Applies this positive axis-aligned transform to the current rectangle geometry fast path.</summary>
    internal SvgRectangle Apply(SvgRectangle rectangle) => rectangle with
    {
        X = ScaleX * rectangle.X + OffsetX,
        Y = ScaleY * rectangle.Y + OffsetY,
        Width = ScaleX * rectangle.Width,
        Height = ScaleY * rectangle.Height
    };

    /// <summary>Applies this transform to an integer orthogonal band while preserving its checked coordinate range.</summary>
    internal SvgRectangle ApplyOrthogonalRectangle(int x, int y, long width, long height, SvgColor color, bool hasFill)
        => new(ApplyCoordinate(ScaleX, x, OffsetX), ApplyCoordinate(ScaleY, y, OffsetY), ApplyExtent(ScaleX, width), ApplyExtent(ScaleY, height), color, hasFill, null);

    private static int Multiply(int left, int right)
    {
        try { return checked(left * right); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported integer transform exceeds the coordinate range."); }
    }

    private static int Offset(int scale, int value, int offset)
    {
        try { return checked(scale * value + offset); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported integer transform exceeds the coordinate range."); }
    }

    private static int ApplyCoordinate(int scale, int value, int offset) => Offset(scale, value, offset);

    private static double ApplyExtent(int scale, long value)
    {
        try { return checked((long)scale * value); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported integer transform exceeds the coordinate range."); }
    }
}
