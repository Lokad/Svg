// Adapts Blink SVG fragment paint placement and alphabetic metric rounding.
// Copyright 2017 The Chromium Authors. See THIRD_PARTY_NOTICES.txt.
namespace Lokad.Svg.Fonts;

using Lokad.Svg.Rendering;

/// <summary>Selects the existing platform scaler's horizontal metric convention.</summary>
internal enum SvgGlyphMetricTarget { Windows, Linux }

/// <summary>Maps bounded font advances and SVG fragments through the output grid without rounding authored origins.</summary>
internal readonly record struct SvgGlyphDeviceMetrics(double Scale, SvgGlyphMetricTarget Target, bool IsTextPath)
{
    internal double GetAdvance(ushort advance, ushort unitsPerEm, double fontSize)
        => GetAdvance(advance, 0, unitsPerEm, fontSize);

    internal double GetAdvance(ushort advance, int pairAdjustment, ushort unitsPerEm, double fontSize)
    {
        float size = (float)(fontSize * Scale);
        if (!double.IsFinite(fontSize) || fontSize <= 0 || !float.IsFinite(size) || size < 0 || unitsPerEm == 0 || !double.IsFinite(Scale) || Scale <= 0)
            throw new SvgException(SvgErrorCode.LimitExceeded, "The text metrics exceed the device range.");
        double deviceAdvance;
        long adjustedAdvance = (long)advance + pairAdjustment;
        if (Target == SvgGlyphMetricTarget.Linux)
        {
            // FreeType retains its size and font-unit scale as 26.6 / 16.16,
            // then rounds the linear advance with FT_MulDiv(..., 64).
            double size26 = Math.Truncate(size * 64f);
            double scale16 = Math.Floor(size26 * 65536 / unitsPerEm + .5);
            double scaledAdvance = adjustedAdvance * scale16 / 64;
            deviceAdvance = Math.CopySign(Math.Floor(Math.Abs(scaledAdvance) + .5), scaledAdvance) / 65536;
        }
        else if (Target == SvgGlyphMetricTarget.Windows)
        {
            // DirectWrite design metrics use scalar multiplication before division.
            float width = size * adjustedAdvance / unitsPerEm;
            deviceAdvance = Math.Truncate(width * 65536f) / 65536;
        }
        else throw new ArgumentOutOfRangeException(nameof(Target));
        if (!double.IsFinite(deviceAdvance) || deviceAdvance * 65536 > int.MaxValue || deviceAdvance * 65536 < int.MinValue)
            throw new SvgException(SvgErrorCode.LimitExceeded, "The text advance exceeds the device range.");
        return (IsTextPath ? Math.Ceiling(deviceAdvance * 64) / 64 : deviceAdvance) / Scale;
    }

    internal double RoundOffset(double offset) => MathF.Floor((float)(offset * Scale) * 64) / (Scale * 64);

    internal double RoundWidth(double width) => MathF.Ceiling((float)(width * Scale) * 64) / (Scale * 64);

    /// <summary>Applies native fragment paint truncation before the retained tangent map and device strike placement.</summary>
    /// <remarks>Operates on immutable borrowed metrics without allocating or owning storage. The caller charges one glyph step and checks cancellation. Semantic path measurement remains unchanged.</remarks>
    internal SvgAffineTransform PlaceTextPathPaint(SvgAffineTransform map, double halfAdvance, double baselineShift, double fontSize, OpenTypeFontMetrics font)
    {
        float size = (float)(fontSize * Scale);
        if (!double.IsFinite(fontSize) || fontSize <= 0 || !double.IsFinite(Scale) || Scale <= 0 || !float.IsFinite(size) || font.UnitsPerEm == 0)
            throw new SvgException(SvgErrorCode.LimitExceeded, "The text-path paint metrics exceed the device range.");
        if (Target is not (SvgGlyphMetricTarget.Windows or SvgGlyphMetricTarget.Linux)) throw new ArgumentOutOfRangeException(nameof(Target));
        float ascent = Target == SvgGlyphMetricTarget.Windows
            ? size * font.LineMetrics.WindowsAscender / font.UnitsPerEm
            : font.LineMetrics.Ascender * (size / font.UnitsPerEm);
        float descent = Target == SvgGlyphMetricTarget.Windows
            ? size * font.LineMetrics.WindowsDescender / font.UnitsPerEm
            : -font.LineMetrics.Descender * (size / font.UnitsPerEm);
        float roundedAscent = MathF.Floor(ascent + .5f);
        if (Target == SvgGlyphMetricTarget.Linux && MathF.Floor(descent + .5f) < descent && roundedAscent >= 1) roundedAscent--;
        SvgDevicePoint center = map.Apply(halfAdvance, -baselineShift);
        // Blink multiplies its scalar path point by the scalar font/viewport
        // scale before building the fragment rectangle. Rounding only after a
        // double product can select the adjacent 1/64-pixel paint rectangle.
        float centerX = (float)center.X * (float)Scale, centerY = (float)center.Y * (float)Scale;
        // Blink paints the unrotated SVG fragment from a LayoutUnit rectangle,
        // truncated toward zero at 1/64. Its character queries retain floats.
        // Rotate this small paint displacement about the advance midpoint before
        // the output viewport translation and the native quarter-pixel strike.
        float top = centerY - roundedAscent;
        float dx = MathF.Truncate(centerX * 64) / 64 - centerX;
        float dy = MathF.Truncate(top * 64) / 64 + roundedAscent - centerY;
        if (!float.IsFinite(roundedAscent) || !float.IsFinite(dx) || !float.IsFinite(dy))
            throw new SvgException(SvgErrorCode.LimitExceeded, "The text-path paint position exceeds the device range.");
        return map with { E = map.E + (map.A * dx + map.C * dy) / Scale, F = map.F + (map.B * dx + map.D * dy) / Scale };
    }
}
