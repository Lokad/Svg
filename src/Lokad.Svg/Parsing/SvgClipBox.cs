namespace Lokad.Svg.Parsing;

/// <summary>Represents one nested viewport clip box with its XML depth while streaming.</summary>
internal readonly record struct SvgClipBox(double X, double Y, double Width, double Height, int Depth)
{
    internal double Right => X + Width;

    internal double Bottom => Y + Height;

    /// <summary>Intersects two clip boxes, normalizing empty results to zero area.</summary>
    internal SvgClipBox Intersect(SvgClipBox other)
    {
        double left = Math.Max(X, other.X);
        double top = Math.Max(Y, other.Y);
        double right = Math.Min(Right, other.Right);
        double bottom = Math.Min(Bottom, other.Bottom);
        return new SvgClipBox(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), Depth);
    }

    /// <summary>Clips one fill box to this viewport, preserving its paint.</summary>
    internal SvgRectangle ClipRectangle(SvgRectangle rectangle)
    {
        double left = Math.Max(rectangle.X, X);
        double top = Math.Max(rectangle.Y, Y);
        double right = Math.Min(rectangle.X + rectangle.Width, Right);
        double bottom = Math.Min(rectangle.Y + rectangle.Height, Bottom);
        return rectangle with { X = left, Y = top, Width = Math.Max(0, right - left), Height = Math.Max(0, bottom - top) };
    }

    /// <summary>Tests whether this viewport fully contains the given outer bounds.</summary>
    internal bool Contains(double left, double top, double right, double bottom)
        => left >= X && top >= Y && right <= Right && bottom <= Bottom;

    /// <summary>Clips one axis-aligned line to this viewport, shortening its segment.</summary>
    internal SvgGeometry ClipLine(SvgAxisAlignedLine line)
    {
        if (line.X1 != line.X2)
        {
            if (line.Y1 < Y || line.Y1 > Bottom) return EmptyBand(line.Color);
            double start = Math.Max(Math.Min(line.X1, line.X2), X);
            double end = Math.Min(Math.Max(line.X1, line.X2), Right);
            if (end <= start) return EmptyBand(line.Color);
            return SvgGeometry.FromAxisAlignedLine(line with { X1 = start, X2 = end, Y1 = line.Y1, Y2 = line.Y1 });
        }
        else
        {
            if (line.X1 < X || line.X1 > Right) return EmptyBand(line.Color);
            double start = Math.Max(Math.Min(line.Y1, line.Y2), Y);
            double end = Math.Min(Math.Max(line.Y1, line.Y2), Bottom);
            if (end <= start) return EmptyBand(line.Color);
            return SvgGeometry.FromAxisAlignedLine(line with { X1 = line.X1, X2 = line.X1, Y1 = start, Y2 = end });
        }
    }

    private static SvgGeometry EmptyBand(SvgColor color)
        => SvgGeometry.FromOrthogonalBand(new SvgRectangle(0, 0, 0, 0, color, false, null));
}