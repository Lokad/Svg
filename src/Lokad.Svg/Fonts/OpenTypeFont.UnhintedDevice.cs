// Adapts the pinned Fontations/Skrifa unhinted TrueType point loader.
// Copyright (c) 2019 Fontations Developers. See THIRD_PARTY_NOTICES.txt.
namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

/// <summary>Scales font-unit operands to the native unhinted TrueType 26.6 grid. This immutable value owns no storage; callers charge each visited point.</summary>
internal readonly record struct OpenTypeUnhintedDeviceScale(int Factor)
{
    internal static OpenTypeUnhintedDeviceScale Create(double deviceSize, ushort unitsPerEm)
    {
        if (!double.IsFinite(deviceSize) || deviceSize <= 0 || deviceSize > 32767)
            throw new SvgException(SvgErrorCode.LimitExceeded, "The unhinted font size exceeds the native coordinate range.");
        // The native unhinted scaler truncates ppem to 26.6 before fixed division.
        int size = checked((int)((float)deviceSize * 64f));
        long factor = (((long)size << 16) + unitsPerEm / 2) / unitsPerEm;
        if (factor > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "The unhinted font scale exceeds its fixed-point bound.");
        return new((int)factor);
    }

    internal int Apply(int value)
    {
        long product = (long)value * Factor;
        long scaled = (Math.Abs(product) + 32768) >> 16;
        if (scaled > int.MaxValue + (product < 0 ? 1L : 0L)) throw new SvgException(SvgErrorCode.LimitExceeded, "An unhinted glyph coordinate exceeds its signed bound.");
        return checked((int)(product < 0 ? -scaled : scaled));
    }
}

internal static partial class OpenTypeFont
{
    /// <summary>Returns owned unhinted device contours, scaling children before component placement and implied-point construction.</summary>
    /// <remarks>Borrows the unchanged face. Existing recursion, point/contour bounds, work and cancellation checks apply; every temporary and returned array keeps its exclusive reservation until disposal. Metric inheritance determines the final horizontal origin. No programs, native services or host fonts are acquired.</remarks>
    internal static OpenTypeGlyphContourStorage GetUnhintedDeviceContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, double deviceSize, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        OpenTypeUnhintedDeviceScale scale = OpenTypeUnhintedDeviceScale.Create(deviceSize, metrics.UnitsPerEm);
        using OpenTypeGlyphPointOutline outline = GetGlyphPointOutline(bytes, glyphIndex, metrics, null, scale, budget, work, token);
        int origin = outline.Phantoms is {} phantoms ? phantoms.LeftX : 0;
        if (origin != 0)
            for (int index = 0; index < outline.Points.Length; index++)
            {
                work.Consume(token);
                OpenTypeGlyphPoint point = outline.Points[index];
                long x = (long)point.X - origin;
                if (x < int.MinValue || x > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "An unhinted glyph origin exceeds its signed bound.");
                outline.Points[index] = new((int)x, point.Y, point.IsOnCurve);
            }
        using OpenTypeSegmentAccumulator segments = new(budget, GlyphDecoderBudgetPhase.ExpandedSegmentCapacity, GlyphDecoderBudgetPhase.ExpandedSegmentExact, GlyphDecoderBudgetPhase.ExpandedContourBoundaryCapacity, GlyphDecoderBudgetPhase.ExpandedContourBoundaryExact, work, token);
        BuildQuadraticContours(outline.Points, outline.Ends, segments, true, TrueTypeImpliedPointRounding.TowardZero, work, token);
        return segments.Detach();
    }
}
