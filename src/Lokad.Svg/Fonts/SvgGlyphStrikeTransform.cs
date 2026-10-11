// Adapts pinned Skia SkScalerContext.cpp and src/utils/SkMatrix22.cpp.
// Copyright 2014 Google Inc. See THIRD_PARTY_NOTICES.txt.
namespace Lokad.Svg.Fonts;

using Lokad.Svg.Rendering;

/// <summary>Retains the native relaxed glyph transform and its vertical unhinted scaler size.</summary>
/// <remarks>This immutable value owns no storage. Its bounded arithmetic is independent of the borrowed font and safe for concurrent callers. Placement uses the original transform separately; only outline construction uses these coefficients.</remarks>
internal readonly record struct SvgGlyphStrikeTransform(float DeviceSize, SvgAffineTransform RemainingMap, SvgAffineTransform RelaxedMap)
{
    internal static SvgGlyphStrikeTransform Create(double deviceFontSize, SvgAffineTransform map)
    {
        float size = (float)deviceFontSize;
        if (!float.IsFinite(size) || size <= 0) throw new SvgException(SvgErrorCode.LimitExceeded, "The mapped font size exceeds its device range.");
        float a = Relax(map.A), b = Relax(map.B), c = Relax(map.C), d = Relax(map.D);
        SvgAffineTransform relaxed = new(a, b, c, d, 0, 0);
        float x = a * size, y = b * size, cx = c * size, dy = d * size;
        float cosine, sine;
        if (y == 0) { cosine = MathF.CopySign(1, x); sine = 0; }
        else if (x == 0) { cosine = 0; sine = -MathF.CopySign(1, y); }
        else if (MathF.Abs(y) > MathF.Abs(x))
        {
            float ratio = x / y;
            float length = MathF.CopySign(MathF.Sqrt(1 + ratio * ratio), y);
            sine = -1 / length;
            cosine = -sine * ratio;
        }
        else
        {
            float ratio = y / x;
            float length = MathF.CopySign(MathF.Sqrt(1 + ratio * ratio), x);
            cosine = 1 / length;
            sine = -cosine * ratio;
        }
        // SkMatrix concatenation uses double intermediates for each two-product sum.
        float horizontal = (float)((double)cosine * x - (double)sine * y);
        float vertical = MathF.Abs((float)((double)sine * cx + (double)cosine * dy));
        if (!float.IsFinite(horizontal) || !float.IsFinite(vertical)) throw new SvgException(SvgErrorCode.LimitExceeded, "The mapped font scaler exceeds its device range.");
        if (MathF.Abs(horizontal) <= 1f / 4096 || vertical <= 1f / 4096)
            return new(1, new(0, 0, 0, 0, 0, 0), relaxed);
        float inverse = 1 / vertical;
        SvgAffineTransform remaining = new(x * inverse, y * inverse, cx * inverse, dy * inverse, 0, 0);
        return new(vertical, remaining, relaxed);

        static float Relax(double value)
        {
            float scaled = (float)value * 1024;
            if (!float.IsFinite(scaled)) throw new SvgException(SvgErrorCode.LimitExceeded, "The mapped font transform exceeds its device range.");
            return MathF.Floor(scaled + .5f) / 1024;
        }
    }
}
