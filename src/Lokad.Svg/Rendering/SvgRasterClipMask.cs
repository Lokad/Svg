using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Borrows canvas coverage from RGBA storage, native clip bitmaps or separately intersected rectangles.</summary>
/// <remarks>Owns no storage and retains no cache. The caller keeps the tape or bitmap owner alive through
/// synchronous replay, supplies its canvas stride, and checks cancellation during traversal.
/// Rectangle coverage uses the same rounding as materialized viewport clips. An empty
/// mask with no storage, owner or rectangle clips nothing; successive clips still intersect separately.</remarks>
internal readonly ref struct SvgRasterClipMask
{
    private readonly SvgCpuClipCoverage? _coverage;

    internal SvgRasterClipMask(ReadOnlySpan<byte> pixels)
    {
        Pixels = pixels;
        Rectangle = null;
        ParentRectangle = null;
        _coverage = null;
    }

    /// <summary>Borrows compact clip samples; the owning clip scope retains its reservations.</summary>
    internal SvgRasterClipMask(SvgCpuClipCoverage coverage)
    {
        Pixels = []; Rectangle = null; ParentRectangle = null;
        _coverage = coverage;
    }

    internal SvgRasterClipMask(SvgClipBox rectangle)
        : this(rectangle, null) { }

    /// <summary>Keeps the parent's AA coverage independent of the child, including coincident edges.</summary>
    internal SvgRasterClipMask(SvgClipBox rectangle, SvgClipBox? parentRectangle)
    {
        Pixels = [];
        Rectangle = rectangle;
        ParentRectangle = parentRectangle;
        _coverage = null;
    }

    internal static SvgRasterClipMask Empty => new(ReadOnlySpan<byte>.Empty);
    internal ReadOnlySpan<byte> Pixels { get; }
    internal SvgClipBox? Rectangle { get; }
    internal SvgClipBox? ParentRectangle { get; }
    internal bool IsEmpty => Pixels.IsEmpty && Rectangle is null && _coverage is null;

    internal byte GetAlpha(int width, int x, int y)
    {
        byte alpha = Rectangle is {} rectangle
            ? SvgAnalyticRectangleClip.GetCoverage((rectangle.X, rectangle.Y, rectangle.Right, rectangle.Bottom),
                rectangle.Antialiasing, x, y).Apply(255)
            : _coverage is {} coverage ? coverage.GetCanvasAlpha(x, y)
            : Pixels.IsEmpty ? (byte)255 : Pixels[(y * width + x) * 4 + 3];
        if (ParentRectangle is {} parent)
        {
            byte inherited = SvgAnalyticRectangleClip.GetCoverage((parent.X, parent.Y, parent.Right, parent.Bottom),
                parent.Antialiasing, x, y).Apply(255);
            alpha = (byte)((alpha * inherited + 127) / 255);
        }
        return alpha;
    }

    /// <summary>Tests whether a complete bitmap intersection has opaque rectangular coverage.</summary>
    /// <remarks>The caller supplies valid in-canvas bounds and stride and keeps coverage alive.
    /// Analytic rectangles need four corner reads; tapes scan every pixel with cancellation
    /// between rows. The query owns no storage or cache and permits independent readers.</remarks>
    internal bool IsOpaqueRectangle(SvgAnalyticClipBounds bounds, int width, CancellationToken token)
    {
        if (Rectangle is not null)
        {
            if (bounds.Left >= bounds.Right || bounds.Top >= bounds.Bottom) return true;
            token.ThrowIfCancellationRequested();
            return GetAlpha(width, bounds.Left, bounds.Top) == 255
                && GetAlpha(width, bounds.Right - 1, bounds.Top) == 255
                && GetAlpha(width, bounds.Left, bounds.Bottom - 1) == 255
                && GetAlpha(width, bounds.Right - 1, bounds.Bottom - 1) == 255;
        }
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = bounds.Left; x < bounds.Right; x++)
                if (GetAlpha(width, x, y) != 255) return false;
        }
        return true;
    }

    internal byte GetAlphaAtOffset(int width, int offset)
        => Rectangle is null && _coverage is null ? Pixels.IsEmpty ? (byte)255 : Pixels[offset + 3]
            : GetAlpha(width, offset / 4 % width, offset / 4 / width);
}
