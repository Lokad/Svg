// Rectangle clip admission adapts Skia's SkRasterClip.cpp and analytic edge
// snapping at the revision in THIRD_PARTY_NOTICES.txt. Copyright 2010 Google Inc.
namespace Lokad.Svg.Rendering;

using Lokad.Svg.Parsing;

/// <summary>Identifies whether the paint consumer is wrapped by an antialiased clip.</summary>
internal enum SvgAnalyticPaintClip : byte
{
    Binary,
    Antialiased,
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

        static bool NearlyIntegral(float value)
        {
            value += .125f;
            return value - MathF.Floor(value) < .25f;
        }

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
}
