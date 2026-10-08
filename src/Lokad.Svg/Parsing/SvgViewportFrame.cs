namespace Lokad.Svg.Parsing;

/// <summary>Carries one nested viewport map, user-space percentage bases, absolute clip box and XML depth.</summary>
internal readonly record struct SvgViewportFrame(double OffsetX, double OffsetY, double ScaleX, double ScaleY, double? ViewportWidth, double? ViewportHeight, SvgClipBox Clip, int Depth)
{
    internal bool IsScaled => ScaleX != 1 || ScaleY != 1;

    internal bool IsTrivial => OffsetX == 0 && OffsetY == 0 && !IsScaled;

    /// <summary>Shifts one fill box into viewport-parent space without clipping.</summary>
    internal SvgRectangle Shift(SvgRectangle rectangle)
        => rectangle with { X = rectangle.X * ScaleX + OffsetX, Y = rectangle.Y * ScaleY + OffsetY, Width = rectangle.Width * ScaleX, Height = rectangle.Height * ScaleY, Clip = rectangle.Clip is {} clip ? new SvgClipBox(clip.X * ScaleX + OffsetX, clip.Y * ScaleY + OffsetY, clip.Width * ScaleX, clip.Height * ScaleY) { Antialiasing = clip.Antialiasing } : null };

    /// <summary>Shifts one axis-aligned line into viewport-parent space without clipping.</summary>
    internal SvgAxisAlignedLine Shift(SvgAxisAlignedLine line)
        => line with { X1 = line.X1 * ScaleX + OffsetX, X2 = line.X2 * ScaleX + OffsetX, Y1 = line.Y1 * ScaleY + OffsetY, Y2 = line.Y2 * ScaleY + OffsetY };

    /// <summary>Shifts one fill box into viewport-parent space, then clips it to the viewport.</summary>
    internal SvgRectangle ClipShifted(SvgRectangle rectangle) => Clip.ClipRectangle(Shift(rectangle));

    /// <summary>Shifts one axis-aligned line into viewport-parent space, then clips its segment.</summary>
    internal SvgGeometry ClipShifted(SvgAxisAlignedLine line) => Clip.ClipLine(Shift(line));
}
