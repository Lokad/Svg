namespace Lokad.Svg.Rendering;

/// <summary>Retains one polynomial or positive rational quadratic segment for analytic coverage.</summary>
/// <remarks>StartIndex and EndIndex are relative to the contour slice the span belongs to (matching GetContour indexing), so translation needs no builder offsets.
/// Translation drops spans whose endpoints do not survive stripping, and separation drops spans across any rewriting stage.
/// Surviving spans always tile exact flattened ranges with a valid control. The walker consumes spans only under its own
/// eligibility; every other consumer keeps the flattened points. A ConicWeight of one is polynomial.
/// Rational controls remain intact through output mapping; the scanner converts them to polynomial
/// quadratics at its device-space tolerance. Weight precedes Control to occupy existing alignment padding.</remarks>
internal readonly record struct SvgQuadSpan(int Contour, int StartIndex, int EndIndex, float ConicWeight, SvgDevicePoint Control)
{
    internal SvgQuadSpan(int contour, int startIndex, int endIndex, SvgDevicePoint control)
        : this(contour, startIndex, endIndex, 1f, control) { }
}
