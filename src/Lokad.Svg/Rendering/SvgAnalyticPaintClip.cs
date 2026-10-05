// Rectangle clip admission/coverage adapts Skia's SkRasterClip.cpp, SkAAClip.cpp,
// analytic edge snapping and SkMath.h at the revision in THIRD_PARTY_NOTICES.txt.
// Copyright 2006, 2016 The Android Open Source Project; 2010, 2011 Google Inc.
namespace Lokad.Svg.Rendering;

using Lokad.Svg.Parsing;

/// <summary>Identifies whether the paint consumer is wrapped by an antialiased clip.</summary>
internal enum SvgAnalyticPaintClip : byte
{
    Binary,
    Antialiased,
}

/// <summary>Applies either geometric area or a native byte clip mask to one paint operation.</summary>
/// <remarks>This immutable value borrows no storage and allocates nothing. Byte masks round the product over 255; geometric fallback retains its existing area arithmetic. Coverage must be applied to each ordered paint separately.</remarks>
internal readonly struct SvgAnalyticClipCoverage
{
    private readonly double _value;
    private readonly bool _byteMask;
    private SvgAnalyticClipCoverage(double value, bool byteMask) { _value = value; _byteMask = byteMask; }
    internal static SvgAnalyticClipCoverage Full => FromMask(255);
    internal static SvgAnalyticClipCoverage FromArea(double area) => new(area, false);
    internal static SvgAnalyticClipCoverage FromMask(byte alpha) => new(alpha, true);
    internal bool IsEmpty => _value <= 0;
    internal byte Apply(byte alpha) => _value == (_byteMask ? 255 : 1) ? alpha
        : _byteMask ? (byte)((alpha * (byte)_value + 127) / 255)
        : (byte)Math.Round(alpha * _value, MidpointRounding.AwayFromZero);
}

/// <summary>Classifies the admitted rectangle clip within one integer bitmap context.</summary>
internal static class SvgAnalyticRectangleClip
{
    /// <remarks>The caller supplies an immutable mapped rectangle and charges four boundary visits before calling. This constant-size classification allocates nothing, retains no state and is thread safe. It covers the native initially-BW rectangle shortcut and a resulting opaque rectangle after analytic X/Y snapping; it does not construct an AA mask or reproduce successive clip-alpha intersections. Cancellation belongs to the caller's context traversal.</remarks>
    internal static SvgAnalyticPaintClip Classify(double left, double top, double right, double bottom, SvgAnalyticClipBounds bitmap, SvgClipAntialiasing antialiasing)
    {
        if (antialiasing == SvgClipAntialiasing.None) return SvgAnalyticPaintClip.Binary;
        float l = (float)left, t = (float)top, r = (float)right, b = (float)bottom;
        if (NearlyIntegral(l) && NearlyIntegral(t) && NearlyIntegral(r) && NearlyIntegral(b)) return SvgAnalyticPaintClip.Binary;
        return (antialiasing & SvgClipAntialiasing.Left) != 0 && HasPartialBoundary(l, bitmap.Left, bitmap.Right, false)
            || (antialiasing & SvgClipAntialiasing.Right) != 0 && HasPartialBoundary(r, bitmap.Left, bitmap.Right, false)
            || (antialiasing & SvgClipAntialiasing.Top) != 0 && HasPartialBoundary(t, bitmap.Top, bitmap.Bottom, true)
            || (antialiasing & SvgClipAntialiasing.Bottom) != 0 && HasPartialBoundary(b, bitmap.Top, bitmap.Bottom, true)
            ? SvgAnalyticPaintClip.Antialiased : SvgAnalyticPaintClip.Binary;

        static bool HasPartialBoundary(float value, int minimum, int maximum, bool vertical)
        {
            // Outlying boundaries, including overflowed floats, cannot leave
            // an antialiased fringe inside this finite bitmap.
            if (!(value > minimum && value < maximum)) return false;
            double snapped = Math.Truncate(value * 256f) / 256;
            if (vertical) snapped = Math.Floor(snapped * 4 + .5) / 4;
            return snapped > minimum && snapped < maximum && snapped != Math.Floor(snapped);
        }
    }

    /// <summary>Gets the admitted rectangle's clip coverage at one destination pixel.</summary>
    /// <remarks>Native AA rectangles truncate X to 1/256 and snap Y to quarters before constructing their byte mask. Initially binary, nearly integral rectangles round to integer boundaries. Partial Y rows pass through the native run-alpha snap; full-height fringes retain their bytes. Boundary provenance prevents allocation bounds from becoming AA edges. This constant-size calculation owns no storage; the caller's per-pixel raster work covers it. It models one rectangle, not successive AA mask intersections or cache history.</remarks>
    internal static SvgAnalyticClipCoverage GetCoverage((double Left, double Top, double Right, double Bottom) bounds, SvgClipAntialiasing antialiasing, int x, int y)
    {
        if (x >= bounds.Left && x + 1 <= bounds.Right && y >= bounds.Top && y + 1 <= bounds.Bottom) return SvgAnalyticClipCoverage.Full;
        if (antialiasing == SvgClipAntialiasing.None)
        {
            (double h, double v) = Area(bounds, x, y);
            return SvgAnalyticClipCoverage.FromArea(h * v);
        }
        bool binary = NearlyIntegral((float)bounds.Left) && NearlyIntegral((float)bounds.Top) && NearlyIntegral((float)bounds.Right) && NearlyIntegral((float)bounds.Bottom);
        double left = Snap(bounds.Left, SvgClipAntialiasing.Left, false, antialiasing, binary), right = Snap(bounds.Right, SvgClipAntialiasing.Right, false, antialiasing, binary);
        double top = Snap(bounds.Top, SvgClipAntialiasing.Top, true, antialiasing, binary), bottom = Snap(bounds.Bottom, SvgClipAntialiasing.Bottom, true, antialiasing, binary);
        (double horizontal, double vertical) = Area((left, top, right, bottom), x, y);
        byte alpha = (byte)Math.Round(horizontal * vertical * 255, MidpointRounding.AwayFromZero);
        if (!binary && vertical != 1) alpha = alpha > 247 ? (byte)255 : alpha < 8 ? (byte)0 : alpha;
        return SvgAnalyticClipCoverage.FromMask(alpha);

        static double Snap(double value, SvgClipAntialiasing boundary, bool vertical, SvgClipAntialiasing antialiasing, bool binary)
        {
            if ((antialiasing & boundary) == 0) return value;
            if (binary) return MathF.Floor((float)value + .5f);
            double snapped = Math.Truncate((float)value * 256f) / 256;
            return vertical ? Math.Floor(snapped * 4 + .5) / 4 : snapped;
        }
        static (double Horizontal, double Vertical) Area((double Left, double Top, double Right, double Bottom) rectangle, int x, int y)
            => (Math.Clamp(Math.Min(rectangle.Right, x + 1) - Math.Max(rectangle.Left, x), 0, 1), Math.Clamp(Math.Min(rectangle.Bottom, y + 1) - Math.Max(rectangle.Top, y), 0, 1));
    }

    private static bool NearlyIntegral(float value)
    {
        value += .125f;
        return value - MathF.Floor(value) < .25f;
    }
}
