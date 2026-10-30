// SVG baseline metric conventions follow Blink's font_metrics.cc.
// Copyright 2005, 2008, 2010 Apple Inc.; 2006 Alexey Proskuryakov. See THIRD_PARTY_NOTICES.txt.
using Lokad.Svg.Fonts;

namespace Lokad.Svg.Parsing;

/// <summary>Keeps fractional layout offsets separate from integer text-path paint offsets at one font scale.</summary>
/// <remarks>Values own no storage. Construction borrows the prepared face, observes cancellation during
/// x-height lookup and retains no font reference. The immutable result is safe to share between readers.</remarks>
internal readonly record struct SvgTextBaselineMetrics(double LayoutShift, double PathPaintShift)
{
    internal static SvgTextBaselineMetrics Create(SvgGlyphFontStyle fontStyle, SvgTextBaseline baseline,
        double scale, SvgGlyphMetricTarget target, CancellationToken token)
    {
        if (baseline == SvgTextBaseline.Alphabetic) return default;
        SvgPreparedFont font = fontStyle.Font;
        float size = (float)(fontStyle.FontSize * scale);
        float ascent = target == SvgGlyphMetricTarget.Windows
            ? size * font.Metrics.LineMetrics.WindowsAscender / font.UnitsPerEm
            : font.Metrics.LineMetrics.Ascender * (size / font.UnitsPerEm);
        float roundedAscent = MathF.Floor(ascent + .5f);
        float baselineAscent;
        int xHeight = 0;
        if (baseline == SvgTextBaseline.Middle)
        {
            xHeight = font.GetXHeight(token);
            baselineAscent = roundedAscent - (xHeight > 0 ? size * xHeight / font.UnitsPerEm / 2 : size / 4);
        }
        else baselineAscent = roundedAscent * .2f;
        // Layout retains the existing LayoutUnit rounding. A path's recorded
        // font paints with integer baseline metrics before its remaining map.
        double layoutShift = (roundedAscent - MathF.Round(baselineAscent * 64, MidpointRounding.AwayFromZero) / 64) / scale;
        float paintSize = SvgGlyphDeviceMetrics.GetEffectiveFontSize(fontStyle.FontSize * scale);
        float paintAscent = target == SvgGlyphMetricTarget.Windows
            ? paintSize * font.Metrics.LineMetrics.WindowsAscender / font.UnitsPerEm
            : font.Metrics.LineMetrics.Ascender * (paintSize / font.UnitsPerEm);
        paintAscent = MathF.Floor(paintAscent + .5f);
        float paintShift = baseline == SvgTextBaseline.Middle
            ? MathF.Truncate(xHeight > 0 ? paintSize * xHeight / font.UnitsPerEm / 2 : paintSize / 4)
            : paintAscent - MathF.Truncate(paintAscent * 2 / 10);
        double pathPaintShift = paintShift / scale;
        if (!double.IsFinite(layoutShift) || !double.IsFinite(pathPaintShift))
            throw new SvgException(SvgErrorCode.LimitExceeded, "The text baseline exceeds the device range.");
        return new(layoutShift, pathPaintShift);
    }
}
