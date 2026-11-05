// Rectangle coverage adapts SkScan_Antihair.cpp at the revision in THIRD_PARTY_NOTICES.txt.
// Copyright 2011 The Android Open Source Project.
namespace Lokad.Svg.Rendering;

/// <summary>Retains the native rectangle's rounded 24.8 edges after clipping to the finite output bitmap.</summary>
/// <remarks>This immutable value owns no storage. Preparation visits four edges; GetAlpha performs constant work. Cancellation belongs to the caller's draw/row traversal. The one-pixel specializations retain the native minus-one rule.</remarks>
internal readonly record struct SvgRectangleCoverage(long Left, long Top, long Right, long Bottom)
{
    internal static SvgRectangleCoverage Create(double left, double top, double right, double bottom, int width, int height)
    {
        return new(Snap(left, width), Snap(top, height), Snap(right, width), Snap(bottom, height));

        static long Snap(double value, int limit)
        {
            float coordinate = (float)Math.Clamp(value, 0, limit);
            long fixedPoint = (long)(coordinate * 65536f);
            return (fixedPoint + 128) >> 8;
        }
    }

    internal byte GetAlpha(int x, int y)
    {
        long horizontal = Math.Min(Right, ((long)x + 1) * 256) - Math.Max(Left, (long)x * 256);
        long vertical = Math.Min(Bottom, ((long)y + 1) * 256) - Math.Max(Top, (long)y * 256);
        if (horizontal <= 0 || vertical <= 0) return 0;
        if ((Top >> 8) == ((Bottom - 1) >> 8)) return (byte)(((vertical - 1) * horizontal) >> 8);
        if (vertical == 256)
        {
            if ((Left >> 8) == ((Right - 1) >> 8)) return (byte)(horizontal - 1);
            return (byte)Math.Min(horizontal, 255);
        }
        return (byte)((vertical * horizontal) >> 8);
    }

    /// <summary>Returns the end of the native rectangle-blitter call containing a painted pixel.</summary>
    /// <remarks>The clip is the bitmap construction window, independently of retained output.
    /// Rounded edges and integer clipping precede dispatch. A single-row rectangle and fractional
    /// top/bottom rows use 100-pixel calls even when their coverage rounds to 255; interior rows
    /// use one rectangle call. Edge columns are separate vertical calls. This immutable lookup
    /// allocates nothing; the caller owns pixel traversal and cancellation.</remarks>
    internal int GetSpanEnd(int x, int y, SvgAnalyticClipBounds clip)
    {
        long left = Math.Max(Left, (long)clip.Left * 256), top = Math.Max(Top, (long)clip.Top * 256);
        long right = Math.Min(Right, (long)clip.Right * 256), bottom = Math.Min(Bottom, (long)clip.Bottom * 256);
        long first = (left + 255) >> 8, last = right >> 8;
        if (x < first || x >= last) return x + 1;
        bool scanline = (top >> 8) == ((bottom - 1) >> 8)
            || (top & 255) != 0 && y == (top >> 8)
            || (bottom & 255) != 0 && y == (bottom >> 8);
        return (int)(scanline ? Math.Min(last, first + ((x - first) / 100 + 1) * 100) : last);
    }
}

/// <summary>Retains one mapped rectangle and a separate byte paint clip within a finite pixel range.</summary>
/// <remarks>Preparation retains mapped edges and the admitted pixel range before painting. The value allocates nothing and owns no storage; callers check cancellation per row. Alpha applies the clip to each operation, rather than cropping the original fill.</remarks>
internal readonly record struct SvgRectangleRaster(int Left, int Top, int Right, int Bottom, SvgRectangleCoverage Coverage, Lokad.Svg.Parsing.SvgClipBox? Clip)
{
    internal byte GetAlpha(int x, int y)
    {
        byte alpha = Coverage.GetAlpha(x, y);
        return Clip is {} clip ? SvgAnalyticRectangleClip.GetCoverage((clip.X, clip.Y, clip.Right, clip.Bottom), clip.Antialiasing, x, y).Apply(alpha) : alpha;
    }
}
