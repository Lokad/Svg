// Adapts the pinned Fontations/Skrifa unhinted TrueType point loader.
// Copyright (c) 2019 Fontations Developers. See THIRD_PARTY_NOTICES.txt.
namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

/// <summary>Scales font-unit operands to the native unhinted TrueType 26.6 grid. This immutable value owns no storage; callers check cancellation per visited point.</summary>
internal readonly record struct OpenTypeUnhintedDeviceScale(int Factor)
{
    internal bool WindowsArithmetic { get; private init; }
    private float HorizontalFactor { get; init; }

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

    internal static OpenTypeUnhintedDeviceScale CreateWindows(double deviceSize, double horizontalSize, ushort unitsPerEm)
    {
        if (!double.IsFinite(deviceSize) || deviceSize <= 0 || deviceSize > 32767 || !double.IsFinite(horizontalSize) || horizontalSize <= 0 || horizontalSize > 32767 || unitsPerEm == 0)
            throw new SvgException(SvgErrorCode.LimitExceeded, "The unfitted Windows font size exceeds the native coordinate range.");
        double factor = Math.Floor(deviceSize * 64 * 65536 / unitsPerEm + .5);
        if (factor > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "The unfitted Windows font scale exceeds its fixed-point bound.");
        return new((int)factor) { WindowsArithmetic = true, HorizontalFactor = (float)horizontalSize / unitsPerEm * 64 };
    }

    internal int ApplyHorizontal(int value)
    {
        if (!WindowsArithmetic) return Apply(value);
        // Ideal Windows outlines round signed halves away from zero;
        // the fitted interpreter's initial X coordinates have a separate rule.
        double rounded = Math.Round((double)(value * HorizontalFactor), MidpointRounding.AwayFromZero);
        if (!double.IsFinite(rounded) || rounded < int.MinValue || rounded > int.MaxValue)
            throw new SvgException(SvgErrorCode.LimitExceeded, "An unfitted horizontal glyph coordinate exceeds its signed bound.");
        return (int)rounded;
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
    internal static OpenTypeGlyphContourStorage GetUnhintedDeviceContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, double deviceSize, RenderBudget budget, CancellationToken token)
        => GetUnhintedDeviceContours(bytes, glyphIndex, metrics, OpenTypeUnhintedDeviceScale.Create(deviceSize, metrics.UnitsPerEm), OpenTypeCompositeOffsetMath.FontationsLength, TrueTypeImpliedPointRounding.TowardZero, null, budget, token);

    private static OpenTypeGlyphContourStorage GetUnhintedDeviceContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, OpenTypeUnhintedDeviceScale scale, OpenTypeCompositeOffsetMath offsetMath, TrueTypeImpliedPointRounding midpointRounding, OpenTypeWindowsGlyphTransform? postTransform, RenderBudget budget, CancellationToken token)
    {
        using OpenTypeGlyphPointOutline outline = GetGlyphPointOutline(bytes, glyphIndex, metrics, null, scale, offsetMath, budget, token);
        int origin = outline.Phantoms is {} phantoms ? phantoms.LeftX : 0;
        if (scale.WindowsArithmetic) origin = scale.ApplyHorizontal(origin);
        if (origin != 0 || scale.WindowsArithmetic || postTransform is not null)
            for (int index = 0; index < outline.Points.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                OpenTypeGlyphPoint point = outline.Points[index];
                long x = (long)(scale.WindowsArithmetic ? scale.ApplyHorizontal(point.X) : point.X) - origin;
                if (x < int.MinValue || x > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "An unhinted glyph origin exceeds its signed bound.");
                OpenTypeGlyphPoint mapped = new((int)x, scale.WindowsArithmetic ? scale.Apply(point.Y) : point.Y, point.IsOnCurve);
                outline.Points[index] = postTransform is {} transform ? transform.ApplyFitted(mapped) : mapped;
            }
        using OpenTypeSegmentAccumulator segments = new(budget, GlyphDecoderBudgetPhase.ExpandedSegmentCapacity, GlyphDecoderBudgetPhase.ExpandedSegmentExact, GlyphDecoderBudgetPhase.ExpandedContourBoundaryCapacity, GlyphDecoderBudgetPhase.ExpandedContourBoundaryExact, token);
        BuildQuadraticContours(outline.Points, outline.Ends, segments, true, midpointRounding, token);
        return segments.Detach();
    }
}
