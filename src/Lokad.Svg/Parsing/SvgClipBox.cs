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

    /// <summary>Clips the painted stroke band while retaining its original edges.</summary>
    internal SvgGeometry ClipLine(SvgAxisAlignedLine line)
    {
        SvgRectangle band = line.ToStrokeBand();
        return Contains(band.X, band.Y, band.X + band.Width, band.Y + band.Height)
            ? SvgGeometry.FromAxisAlignedLine(line)
            : SvgGeometry.FromOrthogonalBand(ClipRectangle(band));
    }
}
