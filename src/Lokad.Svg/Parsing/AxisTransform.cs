namespace Lokad.Svg.Parsing;

/// <summary>Represents the axis-aligned transform subset with finite scales and offsets, plus its geometry application.</summary>
internal readonly record struct AxisTransform(double ScaleX, double ScaleY, double OffsetX, double OffsetY)
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
        double strokeScale = line.X1 != line.X2 ? ScaleY : ScaleX;
        double scaledWidth = Math.Abs(strokeScale) * line.StrokeWidth;
        if (!double.IsFinite(scaledWidth) || scaledWidth != Math.Truncate(scaledWidth) || scaledWidth > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer effective line widths are implemented.");
        int strokeWidth = checked((int)scaledWidth);
        return line with
        {
            X1 = ScaleX * line.X1 + OffsetX,
            Y1 = ScaleY * line.Y1 + OffsetY,
            X2 = ScaleX * line.X2 + OffsetX,
            Y2 = ScaleY * line.Y2 + OffsetY,
            StrokeWidth = strokeWidth
        };
    }

    /// <summary>Maps one integer contour vertex through this transform, keeping integer vertices exact under integer offsets.</summary>
    internal (double X, double Y) ApplyToVertex(int x, int y)
    {
        return (Offset(ScaleX, x, OffsetX), Offset(ScaleY, y, OffsetY));
    }

    /// <summary>Maps one user-space gradient vector endpoint through this transform.</summary>
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
        double mappedX = ApplyCoordinate(ScaleX, x, OffsetX);
        double mappedY = ApplyCoordinate(ScaleY, y, OffsetY);
        double extentX = ApplyExtent(ScaleX, width);
        double extentY = ApplyExtent(ScaleY, height);
        if (extentX < 0) { mappedX = ShiftCoordinate(mappedX, extentX); extentX = -extentX; }
        if (extentY < 0) { mappedY = ShiftCoordinate(mappedY, extentY); extentY = -extentY; }
        return new(mappedX, mappedY, extentX, extentY, color, hasFill, null);
    }

    private static double Multiply(double left, double right)
    {
        double product = left * right;
        if (!double.IsFinite(product) || product < int.MinValue || product > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported transform exceeds the coordinate range.");
        return product;
    }

    private static double Offset(double scale, double value, double offset)
    {
        double mapped = scale * value + offset;
        if (!double.IsFinite(mapped) || mapped < int.MinValue || mapped > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported transform exceeds the coordinate range.");
        return mapped;
    }

    private static double ApplyCoordinate(double scale, int value, double offset) => Offset(scale, value, offset);

    private static double ShiftCoordinate(double coordinate, double extent)
    {
        double shifted = coordinate + extent;
        if (!double.IsFinite(shifted) || shifted < int.MinValue || shifted > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported transform exceeds the coordinate range.");
        return shifted;
    }

    private static double ApplyExtent(double scale, long value)
    {
        double extent = scale * value;
        if (!double.IsFinite(extent) || extent < long.MinValue || extent > long.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported transform exceeds the coordinate range.");
        return extent;
    }
}
