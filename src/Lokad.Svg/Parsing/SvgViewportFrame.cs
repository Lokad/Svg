namespace Lokad.Svg.Parsing;

/// <summary>Carries one nested viewport origin offset with its absolute clip box and XML depth.</summary>
internal readonly record struct SvgViewportFrame(double OffsetX, double OffsetY, double? ViewportWidth, double? ViewportHeight, SvgClipBox Clip, int Depth)
{
    /// <summary>Shifts one fill box into viewport-parent space without clipping.</summary>
    internal SvgRectangle Shift(SvgRectangle rectangle)
        => rectangle with { X = rectangle.X + OffsetX, Y = rectangle.Y + OffsetY };

    /// <summary>Shifts one axis-aligned line into viewport-parent space without clipping.</summary>
    internal SvgAxisAlignedLine Shift(SvgAxisAlignedLine line)
        => line with { X1 = line.X1 + OffsetX, X2 = line.X2 + OffsetX, Y1 = line.Y1 + OffsetY, Y2 = line.Y2 + OffsetY };

    /// <summary>Shifts one fill box into viewport-parent space, then clips it to the viewport.</summary>
    internal SvgRectangle ClipShifted(SvgRectangle rectangle) => Clip.ClipRectangle(Shift(rectangle));

    /// <summary>Shifts one axis-aligned line into viewport-parent space, then clips its segment.</summary>
    internal SvgGeometry ClipShifted(SvgAxisAlignedLine line) => Clip.ClipLine(Shift(line));
}