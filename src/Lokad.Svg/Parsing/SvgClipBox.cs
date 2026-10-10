namespace Lokad.Svg.Parsing;

[Flags]
internal enum SvgClipAntialiasing : byte
{
    None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8, All = Left | Top | Right | Bottom,
    BinaryLeft = 16, BinaryTop = 32, BinaryRight = 64, BinaryBottom = 128,
    Binary = BinaryLeft | BinaryTop | BinaryRight | BinaryBottom
}

/// <summary>Retains rectangle bounds and which boundaries belong to a geometric AA clip.</summary>
internal readonly record struct SvgClipBox(double X, double Y, double Width, double Height)
{
    /// <summary>Mask allocation regions retain their bounds but contribute no AA paint boundary.</summary>
    internal SvgClipAntialiasing Antialiasing { get; init; } = SvgClipAntialiasing.All;

    internal double Right => X + Width;

    internal double Bottom => Y + Height;

    /// <summary>Gets the finite passthrough box that clips nothing inside deferred scopes.</summary>
    internal static SvgClipBox Unbounded { get; } = new(-1e18, -1e18, 2e18, 2e18);

    /// <summary>Intersects two clip boxes, normalizing empty results to zero area.</summary>
    internal SvgClipBox Intersect(SvgClipBox other)
    {
        double left = Math.Max(X, other.X);
        double top = Math.Max(Y, other.Y);
        double right = Math.Min(Right, other.Right);
        double bottom = Math.Min(Bottom, other.Bottom);
        SvgClipAntialiasing edges = SvgClipAntialiasing.None;
        if (X >= other.X) edges |= Antialiasing & (SvgClipAntialiasing.Left | SvgClipAntialiasing.BinaryLeft);
        if (other.X >= X) edges |= other.Antialiasing & (SvgClipAntialiasing.Left | SvgClipAntialiasing.BinaryLeft);
        if (Y >= other.Y) edges |= Antialiasing & (SvgClipAntialiasing.Top | SvgClipAntialiasing.BinaryTop);
        if (other.Y >= Y) edges |= other.Antialiasing & (SvgClipAntialiasing.Top | SvgClipAntialiasing.BinaryTop);
        if (Right <= other.Right) edges |= Antialiasing & (SvgClipAntialiasing.Right | SvgClipAntialiasing.BinaryRight);
        if (other.Right <= Right) edges |= other.Antialiasing & (SvgClipAntialiasing.Right | SvgClipAntialiasing.BinaryRight);
        if (Bottom <= other.Bottom) edges |= Antialiasing & (SvgClipAntialiasing.Bottom | SvgClipAntialiasing.BinaryBottom);
        if (other.Bottom <= Bottom) edges |= other.Antialiasing & (SvgClipAntialiasing.Bottom | SvgClipAntialiasing.BinaryBottom);
        return new SvgClipBox(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top)) { Antialiasing = edges };
    }

    /// <summary>Retains the original fill box and its separate viewport paint boundary.</summary>
    internal SvgRectangle ClipRectangle(SvgRectangle rectangle)
        => rectangle with { Clip = rectangle.Clip is {} existing ? existing.Intersect(this) : this };

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
