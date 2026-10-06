// Adapts SkScan_Antihair.cpp and SkLineClipper.cpp at the revision in THIRD_PARTY_NOTICES.txt.
// Copyright 2011 The Android Open Source Project; 2011 Google Inc.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Paints ordered native line hairline coverage directly into a borrowed premultiplied surface.</summary>
/// <remarks>Owns no heap storage. The caller retains the surface and paint. Clipping bounds fixed-point coordinates;
/// subdivision has at most eight stack levels within that range. Every subdivision and pixel visit consumes work
/// before painting and checks cancellation. Instances belong to one conversion and share no mutable state.</remarks>
internal ref struct SvgHairlineCoverage
{
    private readonly Span<byte> _destination;
    private readonly int _width, _height;
    private readonly SvgAnalyticPaint _paint;
    private readonly (double Left, double Top, double Right, double Bottom)? _clip;
    private readonly SvgClipAntialiasing _antialiasing;
    private readonly ParserWorkCounter _work;
    private readonly CancellationToken _token;

    internal SvgHairlineCoverage(Span<byte> destination, int width, int height, SvgAnalyticPaint paint,
        (double Left, double Top, double Right, double Bottom)? clip, SvgClipAntialiasing antialiasing,
        ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        _destination = destination; _width = width; _height = height; _paint = paint;
        _clip = clip; _antialiasing = antialiasing; _work = workCounter; _token = cancellationToken;
    }

    internal void DrawLine(SvgDevicePoint start, SvgDevicePoint end)
    {
        _work.Consume(_token);
        float x0 = (float)start.X, y0 = (float)start.Y, x1 = (float)end.X, y1 = (float)end.Y;
        if (!float.IsFinite(x0) || !float.IsFinite(y0) || !float.IsFinite(x1) || !float.IsFinite(y1)) return;
        if (!ClipLine(ref x0, ref y0, ref x1, ref y1, -32767, -32767, 32767, 32767)
            || !ClipLine(ref x0, ref y0, ref x1, ref y1, -1, -1, _width + 1, _height + 1)) return;
        DrawFixed((int)(x0 * 64), (int)(y0 * 64), (int)(x1 * 64), (int)(y1 * 64));
    }

    private void DrawFixed(int x0, int y0, int x1, int y1)
    {
        _work.Consume(_token);
        if (Math.Abs(x1 - x0) > 511 * 64 || Math.Abs(y1 - y0) > 511 * 64)
        {
            int middleX = (x0 >> 1) + (x1 >> 1), middleY = (y0 >> 1) + (y1 >> 1);
            DrawFixed(x0, y0, middleX, middleY);
            DrawFixed(middleX, middleY, x1, y1);
            return;
        }
        bool horizontal = Math.Abs(x1 - x0) > Math.Abs(y1 - y0);
        if (!horizontal) { (x0, y0) = (y0, x0); (x1, y1) = (y1, x1); }
        if (x0 > x1) { (x0, x1) = (x1, x0); (y0, y1) = (y1, y0); }
        if (x0 == x1 && y0 == y1) return;
        int first = x0 >> 6, stop = (x1 + 63) >> 6;
        bool axisAligned = y0 == y1;
        int slope = axisAligned ? 0 : ((y1 - y0) << 16) / (x1 - x0);
        int minor = y0 << 10;
        if (!axisAligned) minor += (slope * (32 - (x0 & 63)) + 32) >> 6;
        int firstCoverage = stop - first == 1 ? x1 - x0 : 64 - (x0 & 63);
        int lastCoverage = stop - first == 1 ? 0 : x1 & 63;
        int maximum = horizontal ? _width : _height;
        if (first >= maximum || stop <= 0) return;
        if (first < 0)
        {
            minor += slope * -first; first = 0; firstCoverage = 64;
            if (stop - first == 1) { firstCoverage = ((x1 - 1) & 63) + 1; lastCoverage = 0; }
        }
        if (stop > maximum) { stop = maximum; lastCoverage = 0; }
        DrawColumn(first, minor, firstCoverage, horizontal, axisAligned);
        minor += slope;
        int fullStop = stop - (lastCoverage > 0 ? 1 : 0);
        for (int major = first + 1; major < fullStop; major++)
        {
            DrawColumn(major, minor, 64, horizontal, axisAligned);
            minor += slope;
        }
        if (lastCoverage > 0) DrawColumn(stop - 1, minor, lastCoverage, horizontal, axisAligned);
    }

    private void DrawColumn(int major, int minor, int coverage, bool horizontal, bool axisAligned)
    {
        int shifted = minor + 32768, lower = shifted >> 16;
        int alpha = (shifted >> 8) & 255;
        byte low = (byte)((alpha * coverage) >> 6), high = (byte)(((255 - alpha) * coverage) >> 6);
        if (axisAligned)
        {
            Paint(horizontal ? major : lower, horizontal ? lower : major, low, SvgAnalyticPaintKind.Direct);
            Paint(horizontal ? major : lower - 1, horizontal ? lower - 1 : major, high, SvgAnalyticPaintKind.Direct);
        }
        else
        {
            Paint(horizontal ? major : lower - 1, horizontal ? lower - 1 : major, high, SvgAnalyticPaintKind.Pair);
            Paint(horizontal ? major : lower, horizontal ? lower : major, low, SvgAnalyticPaintKind.Pair);
        }
    }

    private void Paint(int x, int y, byte alpha, SvgAnalyticPaintKind kind)
    {
        _work.Consume(_token);
        if (alpha == 0 || (uint)x >= (uint)_width || (uint)y >= (uint)_height) return;
        if (_clip is { } clip)
        {
            SvgAnalyticClipCoverage coverage = SvgAnalyticRectangleClip.GetCoverage(clip, _antialiasing, x, y);
            byte clipped = coverage.Apply(alpha);
            if (clipped != alpha) kind = SvgAnalyticPaintKind.Direct;
            alpha = clipped;
        }
        _paint.Blend(_destination.Slice((y * _width + x) * 4, 4), alpha, kind);
    }

    private static bool ClipLine(ref float x0, ref float y0, ref float x1, ref float y1, float left, float top, float right, float bottom)
    {
        float minX = MathF.Min(x0, x1), maxX = MathF.Max(x0, x1), minY = MathF.Min(y0, y1), maxY = MathF.Max(y0, y1);
        if (left <= minX && top <= minY && right >= maxX && bottom >= maxY) return true;
        if (Outside(maxX, left, maxX - minX) || Outside(right, minX, maxX - minX)
            || Outside(maxY, top, maxY - minY) || Outside(bottom, minY, maxY - minY)) return false;
        float aX = x0, aY = y0, bX = x1, bY = y1;
        if (aY < top) { aX = IntersectHorizontal(x0, y0, x1, y1, top); aY = top; }
        if (bY < top) { bX = IntersectHorizontal(x0, y0, x1, y1, top); bY = top; }
        if (aY > bottom) { aX = IntersectHorizontal(x0, y0, x1, y1, bottom); aY = bottom; }
        if (bY > bottom) { bX = IntersectHorizontal(x0, y0, x1, y1, bottom); bY = bottom; }
        if (MathF.Max(aX, bX) <= left || MathF.Min(aX, bX) >= right)
            if (aX != bX || aX < left || aX > right) return false;
        if (aX < left) { aY = IntersectVertical(aX, aY, bX, bY, left); aX = left; }
        if (bX < left) { bY = IntersectVertical(aX, aY, bX, bY, left); bX = left; }
        if (aX > right) { aY = IntersectVertical(aX, aY, bX, bY, right); aX = right; }
        if (bX > right) { bY = IntersectVertical(aX, aY, bX, bY, right); bX = right; }
        x0 = aX; y0 = aY; x1 = bX; y1 = bY;
        return true;

        static bool Outside(float a, float b, float dimension) => a <= b && (a < b || dimension > 0);
        static float IntersectHorizontal(float x0, float y0, float x1, float y1, float y)
            => MathF.Abs(y1 - y0) <= 1f / 4096 ? (x0 + x1) * .5f
                : (float)Math.Clamp(x0 + ((double)y - y0) * ((double)x1 - x0) / ((double)y1 - y0), MathF.Min(x0, x1), MathF.Max(x0, x1));
        static float IntersectVertical(float x0, float y0, float x1, float y1, float x)
            => MathF.Abs(x1 - x0) <= 1f / 4096 ? (y0 + y1) * .5f
                : (float)(y0 + ((double)x - x0) * ((double)y1 - y0) / ((double)x1 - x0));
    }
}
