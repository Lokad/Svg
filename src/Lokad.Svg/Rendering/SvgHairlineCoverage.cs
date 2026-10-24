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
    private readonly int _width, _height, _bitmapWidth, _bitmapHeight;
    private readonly SvgCpuBitmapOrigin _origin;
    private readonly SvgAnalyticClipBounds _interior;
    private readonly bool _regionClipping, _binaryViewport;
    private readonly SvgAnalyticPaint _paint;
    private readonly Span<SvgShaderSampler> _shaders;
    private SvgAnalyticClipBounds? _binaryBounds;
    private readonly (double Left, double Top, double Right, double Bottom)? _clip;
    private readonly SvgClipAntialiasing _antialiasing;
    private readonly ReadOnlySpan<byte> _viewportMask;
    private readonly ParserWorkCounter _work;
    private readonly CancellationToken _token;

    internal SvgHairlineCoverage(Span<byte> destination, int width, int height, SvgAnalyticPaint paint,
        (double Left, double Top, double Right, double Bottom)? clip, SvgClipAntialiasing antialiasing,
        ParserWorkCounter workCounter, CancellationToken cancellationToken)
        : this(destination, width, height, paint, clip, antialiasing, ReadOnlySpan<byte>.Empty, workCounter, cancellationToken) { }

    internal SvgHairlineCoverage(Span<byte> destination, int width, int height, SvgAnalyticPaint paint,
        (double Left, double Top, double Right, double Bottom)? clip, SvgClipAntialiasing antialiasing, ReadOnlySpan<byte> viewportMask,
        ParserWorkCounter workCounter, CancellationToken cancellationToken)
        : this(destination, width, height, paint, clip, antialiasing, viewportMask, false,
            new(new(0, 0, width, height), new(0, 0, width, height)), workCounter, cancellationToken)
    {
        // The compiled coverage-kernel vectors use a scalar bitmap clip and record pair
        // events before native clip-wrapper material dispatch. Path replay uses regions.
        _regionClipping = false;
    }

    /// <summary>Borrows a canvas while constructing local coverage in one bordered bitmap and painting only its owned interior.</summary>
    internal SvgHairlineCoverage(Span<byte> destination, int width, int height, SvgAnalyticPaint paint,
        (double Left, double Top, double Right, double Bottom)? clip, SvgClipAntialiasing antialiasing, ReadOnlySpan<byte> viewportMask, bool binaryViewport,
        SvgCpuRasterContext context, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        _origin = new(context.BitmapClip.Left, context.BitmapClip.Top);
        _binaryBounds = null;
        _regionClipping = true;
        _interior = context.Interior;
        _bitmapWidth = context.BitmapClip.Right - context.BitmapClip.Left;
        _bitmapHeight = context.BitmapClip.Bottom - context.BitmapClip.Top;
        if (!viewportMask.IsEmpty && viewportMask.Length != checked(width * height * 4)) throw new ArgumentException("Viewport mask must match the destination canvas.", nameof(viewportMask));
        _viewportMask = viewportMask; _binaryViewport = binaryViewport;
        _destination = destination; _width = width; _height = height; _paint = paint;
        _shaders = Span<SvgShaderSampler>.Empty;
        _clip = clip; _antialiasing = antialiasing; _work = workCounter; _token = cancellationToken;
    }

    /// <summary>Borrows one draw-local shader for the lifetime of this coverage walker.</summary>
    /// <remarks>The caller retains its ramp/texture owners and sampler storage until replay finishes.
    /// Geometry/pixel visits and optional sampling work use the same cancellable work counter.</remarks>
    internal SvgHairlineCoverage(Span<byte> destination, int width, int height, Span<SvgShaderSampler> shaders,
        (double Left, double Top, double Right, double Bottom)? clip, SvgClipAntialiasing antialiasing, ReadOnlySpan<byte> viewportMask, bool binaryViewport,
        SvgCpuRasterContext context, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        : this(destination, width, height, default(SvgAnalyticPaint), clip, antialiasing, viewportMask, binaryViewport, context, workCounter, cancellationToken)
    {
        if (shaders.Length != 1) throw new ArgumentException("Hairline replay requires one draw-local shader.", nameof(shaders));
        _shaders = shaders;
    }

    internal void DrawLine(SvgDevicePoint start, SvgDevicePoint end)
    {
        _work.Consume(_token);
        float x0 = (float)start.X, y0 = (float)start.Y, x1 = (float)end.X, y1 = (float)end.Y;
        if (!float.IsFinite(x0) || !float.IsFinite(y0) || !float.IsFinite(x1) || !float.IsFinite(y1)) return;
        if (!ClipLine(ref x0, ref y0, ref x1, ref y1, -32767, -32767, 32767, 32767)
            || !ClipLine(ref x0, ref y0, ref x1, ref y1, -1, -1, _bitmapWidth + 1, _bitmapHeight + 1)) return;
        int fixedX0 = (int)(x0 * 64), fixedY0 = (int)(y0 * 64), fixedX1 = (int)(x1 * 64), fixedY1 = (int)(y1 * 64);
        SvgAnalyticClipBounds bounds = new((Math.Min(fixedX0, fixedX1) >> 6) - 1, (Math.Min(fixedY0, fixedY1) >> 6) - 1,
            ((Math.Max(fixedX0, fixedX1) + 63) >> 6) + 1, ((Math.Max(fixedY0, fixedY1) + 63) >> 6) + 1);
        bool clipped = bounds.Left < 0 || bounds.Top < 0 || bounds.Right > _bitmapWidth || bounds.Bottom > _bitmapHeight;
        if (clipped) bounds = new(Math.Max(bounds.Left, 0), Math.Max(bounds.Top, 0), Math.Min(bounds.Right, _bitmapWidth), Math.Min(bounds.Bottom, _bitmapHeight));
        if (!_regionClipping) { clipped = true; bounds = new(0, 0, _bitmapWidth, _bitmapHeight); }
        if (_binaryViewport && !_viewportMask.IsEmpty)
        {
            SvgAnalyticClipBounds bitmap = new(_origin.X, _origin.Y, _origin.X + _bitmapWidth, _origin.Y + _bitmapHeight);
            SvgAnalyticClipBounds area = new(bounds.Left + _origin.X, bounds.Top + _origin.Y, bounds.Right + _origin.X, bounds.Bottom + _origin.Y);
            SvgBinaryClipRectangles regions = new(_viewportMask, _width, _height, bitmap, area, _work, _token);
            while (regions.MoveNext())
            {
                SvgAnalyticClipBounds region = regions.Current;
                SvgAnalyticClipBounds local = new(region.Left - _origin.X, region.Top - _origin.Y, region.Right - _origin.X, region.Bottom - _origin.Y);
                _binaryBounds = local;
                DrawFixed(fixedX0, fixedY0, fixedX1, fixedY1, true, local);
                _binaryBounds = null;
            }
            return;
        }
        DrawFixed(fixedX0, fixedY0, fixedX1, fixedY1, clipped, bounds);
    }

    private void DrawFixed(int x0, int y0, int x1, int y1, bool clipped, SvgAnalyticClipBounds bounds)
    {
        _work.Consume(_token);
        if (Math.Abs(x1 - x0) > 511 * 64 || Math.Abs(y1 - y0) > 511 * 64)
        {
            int middleX = (x0 >> 1) + (x1 >> 1), middleY = (y0 >> 1) + (y1 >> 1);
            DrawFixed(x0, y0, middleX, middleY, clipped, bounds);
            DrawFixed(middleX, middleY, x1, y1, clipped, bounds);
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
        int minimum = clipped ? horizontal ? bounds.Left : bounds.Top : 0;
        int maximum = clipped ? horizontal ? bounds.Right : bounds.Bottom : horizontal ? _bitmapWidth : _bitmapHeight;
        if (first >= maximum || stop <= minimum) return;
        if (first < minimum)
        {
            minor += slope * (minimum - first); first = minimum; firstCoverage = 64;
            if (stop - first == 1) { firstCoverage = ((x1 - 1) & 63) + 1; lastCoverage = 0; }
        }
        if (stop > maximum) { stop = maximum; lastCoverage = 0; }
        if (clipped)
        {
            int finalMinor = minor + (stop - first - 1) * slope;
            int minimumMinor = (Math.Min(minor, finalMinor) - 32768 >> 16) - 1;
            int maximumMinor = (Math.Max(minor, finalMinor) + 32768 + 65535 >> 16) + 1;
            int minorMinimum = horizontal ? bounds.Top : bounds.Left;
            int minorLimit = horizontal ? bounds.Bottom : bounds.Right;
            if (minimumMinor >= minorLimit || maximumMinor <= minorMinimum) return;
            if (minimumMinor >= minorMinimum && maximumMinor <= minorLimit) clipped = false;
        }
        // The native rectangular clip wrapper sends both adjacent pixels through its scalar
        // span blitter. Its rounding differs from the unclipped pair blitter even inside the clip.
        SvgAnalyticPaintKind kind = (_regionClipping && clipped) || axisAligned ? SvgAnalyticPaintKind.Direct : SvgAnalyticPaintKind.Pair;
        DrawColumn(first, minor, firstCoverage, horizontal, axisAligned, kind);
        minor += slope;
        int fullStop = stop - (lastCoverage > 0 ? 1 : 0);
        for (int major = first + 1; major < fullStop; major++)
        {
            DrawColumn(major, minor, 64, horizontal, axisAligned, kind);
            minor += slope;
        }
        if (lastCoverage > 0) DrawColumn(stop - 1, minor, lastCoverage, horizontal, axisAligned, kind);
    }

    private void DrawColumn(int major, int minor, int coverage, bool horizontal, bool axisAligned, SvgAnalyticPaintKind kind)
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
            Paint(horizontal ? major : lower - 1, horizontal ? lower - 1 : major, high, kind);
            Paint(horizontal ? major : lower, horizontal ? lower : major, low, kind);
        }
    }

    private void Paint(int x, int y, byte alpha, SvgAnalyticPaintKind kind)
    {
        _work.Consume(_token);
        if (alpha == 0 || (uint)x >= (uint)_bitmapWidth || (uint)y >= (uint)_bitmapHeight) return;
        if (_binaryBounds is {} bounds && (x < bounds.Left || x >= bounds.Right || y < bounds.Top || y >= bounds.Bottom)) return;
        x += _origin.X; y += _origin.Y;
        if (x < _interior.Left || x >= _interior.Right || y < _interior.Top || y >= _interior.Bottom) return;
        if (_clip is { } clip)
        {
            SvgAnalyticClipCoverage coverage = SvgAnalyticRectangleClip.GetCoverage(clip, _antialiasing, x, y);
            byte clipped = coverage.Apply(alpha);
            if (clipped != alpha) kind = SvgAnalyticPaintKind.Direct;
            alpha = clipped;
        }
        if (!_viewportMask.IsEmpty)
        {
            alpha = (byte)((alpha * _viewportMask[(y * _width + x) * 4 + 3] + 127) / 255);
            if (!_binaryViewport) kind = SvgAnalyticPaintKind.Direct;
        }
        if (alpha == 0) return;
        SvgAnalyticPaint paint = _shaders.IsEmpty ? _paint : _shaders[0].Sample(x, y, _work, _token);
        paint.Blend(_destination.Slice((y * _width + x) * 4, 4), alpha, kind);
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
