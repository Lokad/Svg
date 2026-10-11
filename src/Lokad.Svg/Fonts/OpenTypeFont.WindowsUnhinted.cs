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
    /// <summary>Returns owned Windows unhinted contours after fixed-point mapping and before strike placement.</summary>
    /// <remarks>Borrows the unchanged supplied face and reuses bounded component assembly. Point and segment owners reserve before allocation and release on success, denial and cancellation. Each decoded or mapped point and lowered contour charges the shared work counter. No instructions, native services or host fonts are acquired; calls with separate conversion owners may run concurrently.</remarks>
    internal static OpenTypeGlyphContourStorage GetWindowsUnhintedContours(ReadOnlyMemory<byte> bytes, ushort glyphIndex, OpenTypeFontMetrics metrics, SvgGlyphStrikeTransform strike, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        OpenTypeWindowsGlyphTransform transform = OpenTypeWindowsGlyphTransform.Create(strike, metrics.UnitsPerEm);
        using OpenTypeGlyphPointOutline outline = GetGlyphPointOutline(bytes, glyphIndex, metrics, null, null, budget, work, token);
        for (int index = 0; index < outline.Points.Length; index++)
        {
            work.Consume(token);
            outline.Points[index] = transform.Apply(outline.Points[index]);
        }
        using OpenTypeSegmentAccumulator segments = new(budget, GlyphDecoderBudgetPhase.ExpandedSegmentCapacity, GlyphDecoderBudgetPhase.ExpandedSegmentExact, GlyphDecoderBudgetPhase.ExpandedContourBoundaryCapacity, GlyphDecoderBudgetPhase.ExpandedContourBoundaryExact, work, token);
        BuildQuadraticContours(outline.Points, outline.Ends, segments, true, TrueTypeImpliedPointRounding.None, work, token);
        OpenTypeGlyphContourStorage result = segments.Detach();
        result.SmartDropouts = true;
        return result;
    }
}
