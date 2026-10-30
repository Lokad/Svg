using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Borrows canvas coverage from either an RGBA tape or one analytic rectangle.</summary>
/// <remarks>Owns no storage and retains no cache. The caller keeps the tape alive through
/// synchronous replay, supplies its canvas stride, and checks cancellation during traversal.
/// Rectangle coverage uses the same rounding as materialized viewport clips. An empty
/// tape without a rectangle clips nothing; successive clips still intersect separately.</remarks>
internal readonly ref struct SvgRasterClipMask
{
    internal SvgRasterClipMask(ReadOnlySpan<byte> pixels)
    {
        Pixels = pixels;
        Rectangle = null;
    }

    internal SvgRasterClipMask(SvgClipBox rectangle)
    {
        Pixels = [];
        Rectangle = rectangle;
    }

    internal static SvgRasterClipMask Empty => new(ReadOnlySpan<byte>.Empty);
    internal ReadOnlySpan<byte> Pixels { get; }
    internal SvgClipBox? Rectangle { get; }
    internal bool IsEmpty => Pixels.IsEmpty && Rectangle is null;

    internal byte GetAlpha(int width, int x, int y)
        => Rectangle is {} rectangle
            ? SvgAnalyticRectangleClip.GetCoverage((rectangle.X, rectangle.Y, rectangle.Right, rectangle.Bottom),
                rectangle.Antialiasing, x, y).Apply(255)
            : Pixels.IsEmpty ? (byte)255 : Pixels[(y * width + x) * 4 + 3];

    internal byte GetAlphaAtOffset(int width, int offset)
        => Rectangle is null ? Pixels.IsEmpty ? (byte)255 : Pixels[offset + 3]
            : GetAlpha(width, offset / 4 % width, offset / 4 / width);
}
