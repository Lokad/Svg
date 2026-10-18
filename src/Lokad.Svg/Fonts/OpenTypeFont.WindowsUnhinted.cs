namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

/// <summary>Maps original font points through the independently measured Windows unhinted fixed-point transform.</summary>
/// <remarks>This immutable value owns no storage and is safe for concurrent callers. Matrix normalization precedes per-product 26.6 rounding; implied points are constructed afterward. Arithmetic is bounded independently of the face. The caller charges and checks cancellation for each visited point.</remarks>
internal readonly record struct OpenTypeWindowsGlyphTransform(int XX, int XY, int YX, int YY)
{
    internal static OpenTypeWindowsGlyphTransform Create(SvgGlyphStrikeTransform strike, ushort unitsPerEm)
    {
        if (unitsPerEm == 0) throw new SvgException(SvgErrorCode.LimitExceeded, "The mapped font has no native scale divisor.");
        return new(Coefficient((float)strike.RemainingMap.A), Coefficient(-(float)strike.RemainingMap.C),
            Coefficient(-(float)strike.RemainingMap.B), Coefficient((float)strike.RemainingMap.D));

        int Coefficient(float value)
        {
            float matrix = MathF.Round(strike.DeviceSize * value * 65536, MidpointRounding.AwayFromZero);
            if (!float.IsFinite(matrix) || (double)matrix < int.MinValue || (double)matrix > int.MaxValue)
                throw new SvgException(SvgErrorCode.LimitExceeded, "The mapped font matrix exceeds its fixed-point bound.");
            long fixedValue = (int)matrix;
            return (int)(Math.Sign(fixedValue) * ((Math.Abs(fixedValue) + unitsPerEm / 2) / unitsPerEm));
        }
    }

    internal OpenTypeGlyphPoint Apply(OpenTypeGlyphPoint point)
        => ApplyFixed(point, (long)point.X * 64, (long)point.Y * 64);

    internal OpenTypeGlyphPoint ApplyFitted(OpenTypeGlyphPoint point)
        => ApplyFixed(point, point.X, point.Y);

    private OpenTypeGlyphPoint ApplyFixed(OpenTypeGlyphPoint point, long x, long y)
    {
        if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
            throw new SvgException(SvgErrorCode.LimitExceeded, "An original glyph point exceeds its fixed-point bound.");
        long mappedX = Multiply(x, XX) + Multiply(y, XY), mappedY = Multiply(x, YX) + Multiply(y, YY);
        if (mappedX < int.MinValue || mappedX > int.MaxValue || mappedY < int.MinValue || mappedY > int.MaxValue)
            throw new SvgException(SvgErrorCode.LimitExceeded, "A mapped glyph point exceeds its signed bound.");
        return new((int)mappedX, (int)mappedY, point.IsOnCurve);

        static long Multiply(long value, int coefficient)
        {
            long product = value * coefficient;
            return Math.Sign(product) * ((Math.Abs(product) + 32768) >> 16);
        }
    }
}

internal static partial class OpenTypeFont
{
    /// <summary>Returns owned ordinary Windows contours for a font-selected unfitted range, preserving native size and initial-point scaling.</summary>
    /// <remarks>Shares bounded component assembly, metric inheritance and contour ownership with the other device loaders. Programs remain unexecuted in this native range; outline structure, work, cancellation and cleanup remain enforced. Borrows immutable font bytes and retains no storage beyond the returned exclusive owner.</remarks>
    internal static OpenTypeGlyphContourStorage GetWindowsUnfittedContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, double deviceSize, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        => GetWindowsUnfittedContours(bytes, glyphIndex, metrics, deviceSize, 1, null, budget, work, token);

    /// <summary>Applies an explicit horizontal strike ratio and optional native point transform before implied-point construction, sharing the unfitted ownership and budget contract.</summary>
    internal static OpenTypeGlyphContourStorage GetWindowsUnfittedContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, double deviceSize, double horizontalRatio, OpenTypeWindowsGlyphTransform? postTransform, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        if (!double.IsFinite(deviceSize) || deviceSize <= 0 || deviceSize > 32767 || !double.IsFinite(horizontalRatio) || horizontalRatio <= 0)
            throw new SvgException(SvgErrorCode.LimitExceeded, "The unfitted Windows font size exceeds its device bound.");
        if (metrics.ForceIntegerHintingSize)
        {
            deviceSize = Math.Floor(deviceSize + .5);
            if (deviceSize == 0) return new([], [], null, null);
        }
        return GetUnhintedDeviceContours(bytes, glyphIndex, metrics, OpenTypeUnhintedDeviceScale.CreateWindows(deviceSize, deviceSize * horizontalRatio, metrics.UnitsPerEm), OpenTypeCompositeOffsetMath.DirectWrite, TrueTypeImpliedPointRounding.None, postTransform, budget, work, token);
    }

    /// <summary>Returns owned Windows unhinted contours after fixed-point mapping and before strike placement.</summary>
    /// <remarks>Borrows the unchanged supplied face and reuses bounded component assembly. Point and segment owners reserve before allocation and release on success, denial and cancellation. Each decoded or mapped point and lowered contour charges the shared work counter. No instructions, native services or host fonts are acquired; calls with separate conversion owners may run concurrently.</remarks>
    internal static OpenTypeGlyphContourStorage GetWindowsUnhintedContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, SvgGlyphStrikeTransform strike, bool hasPrograms, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        OpenTypeWindowsGlyphTransform transform = OpenTypeWindowsGlyphTransform.Create(strike, metrics.UnitsPerEm);
        using OpenTypeGlyphPointOutline outline = GetGlyphPointOutline(bytes, glyphIndex, metrics, null, null, OpenTypeCompositeOffsetMath.DirectWrite, budget, work, token);
        // Native mapping rounds the stored points and metric origin separately.
        // Rebasing before those products changes valid nonzero-origin outlines.
        OpenTypeGlyphPoint origin = transform.Apply(new(GetHorizontalOrigin(bytes, glyphIndex, metrics, work, token), 0, true));
        for (int index = 0; index < outline.Points.Length; index++)
        {
            work.Consume(token);
            OpenTypeGlyphPoint point = transform.Apply(outline.Points[index]);
            long x = (long)point.X - origin.X, y = (long)point.Y - origin.Y;
            if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
                throw new SvgException(SvgErrorCode.LimitExceeded, "A mapped glyph origin exceeds its signed bound.");
            outline.Points[index] = new((int)x, (int)y, point.IsOnCurve);
        }
        using OpenTypeSegmentAccumulator segments = new(budget, GlyphDecoderBudgetPhase.ExpandedSegmentCapacity, GlyphDecoderBudgetPhase.ExpandedSegmentExact, GlyphDecoderBudgetPhase.ExpandedContourBoundaryCapacity, GlyphDecoderBudgetPhase.ExpandedContourBoundaryExact, work, token);
        BuildQuadraticContours(outline.Points, outline.Ends, segments, true, TrueTypeImpliedPointRounding.None, work, token);
        OpenTypeGlyphContourStorage result = segments.Detach();
        result.SmartDropouts = hasPrograms;
        result.SimpleDropouts = !hasPrograms && MathF.Floor(strike.DeviceSize + .5f) <= 32;
        return result;
    }
}
