namespace Lokad.Svg.Parsing;

/// <summary>Represents the calibrated integer transform subset and its geometry application.</summary>
internal readonly record struct AxisTransform(int ScaleX, int ScaleY, int OffsetX, int OffsetY)
{
    internal static AxisTransform Identity { get; } = new(1, 1, 0, 0);

    internal AxisTransform Compose(AxisTransform child) => new(Multiply(ScaleX, child.ScaleX), Multiply(ScaleY, child.ScaleY), Offset(ScaleX, child.OffsetX, OffsetX), Offset(ScaleY, child.OffsetY, OffsetY));

    /// <summary>Applies this axis-aligned transform to the current rectangle geometry fast path, normalizing reflected extents.</summary>
    internal SvgRectangle Apply(SvgRectangle rectangle)
    {
        double x = ScaleX * rectangle.X + OffsetX;
        double y = ScaleY * rectangle.Y + OffsetY;
        double width = ScaleX * rectangle.Width;
        double height = ScaleY * rectangle.Height;
        if (width < 0) { x += width; width = -width; }
        if (height < 0) { y += height; height = -height; }
        return rectangle with { X = x, Y = y, Width = width, Height = height };
    }

    /// <summary>Applies this axis-aligned transform to a retained axis-aligned line, keeping the stroke width non-negative.</summary>
    internal SvgAxisAlignedLine Apply(SvgAxisAlignedLine line)
    {
        int strokeScale = line.X1 != line.X2 ? ScaleY : ScaleX;
        int strokeWidth;
        try { strokeWidth = checked(Math.Abs(strokeScale) * line.StrokeWidth); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported integer transform exceeds the line stroke-width range."); }
        return line with
        {
            X1 = ScaleX * line.X1 + OffsetX,
            Y1 = ScaleY * line.Y1 + OffsetY,
            X2 = ScaleX * line.X2 + OffsetX,
            Y2 = ScaleY * line.Y2 + OffsetY,
            StrokeWidth = strokeWidth
        };
    }

    /// <summary>Maps one user-space gradient vector endpoint through this admitted integer transform.</summary>
    internal (double X, double Y) ApplyToUserPoint(double x, double y)
    {
        double mappedX = ScaleX * x + OffsetX;
        double mappedY = ScaleY * y + OffsetY;
        if (!double.IsFinite(mappedX) || !double.IsFinite(mappedY)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported integer transform exceeds the gradient coordinate range.");
        return (mappedX, mappedY);
    }
    /// <summary>Applies this axis-aligned transform to a nested clip box, normalizing reflected extents.</summary>
    internal SvgClipBox Apply(SvgClipBox box)
    {
        double x = ScaleX * box.X + OffsetX;
        double y = ScaleY * box.Y + OffsetY;
        double width = ScaleX * box.Width;
        double height = ScaleY * box.Height;
        if (width < 0) { x += width; width = -width; }
        if (height < 0) { y += height; height = -height; }
        return new(x, y, width, height);
    }

    /// <summary>Applies this transform to an integer orthogonal band while preserving its checked coordinate range.</summary>
    internal SvgRectangle ApplyOrthogonalRectangle(int x, int y, long width, long height, SvgColor color, bool hasFill)
    {
        int mappedX = ApplyCoordinate(ScaleX, x, OffsetX);
        int mappedY = ApplyCoordinate(ScaleY, y, OffsetY);
        double extentX = ApplyExtent(ScaleX, width);
        double extentY = ApplyExtent(ScaleY, height);
        if (extentX < 0) { mappedX = ShiftCoordinate(mappedX, extentX); extentX = -extentX; }
        if (extentY < 0) { mappedY = ShiftCoordinate(mappedY, extentY); extentY = -extentY; }
        return new(mappedX, mappedY, extentX, extentY, color, hasFill, null);
    }

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

    private static int ShiftCoordinate(int coordinate, double extent)
    {
        try { return checked(coordinate + checked((int)extent)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported integer transform exceeds the coordinate range."); }
    }

    private static double ApplyExtent(int scale, long value)
    {
        try { return checked((long)scale * value); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported integer transform exceeds the coordinate range."); }
    }
}
