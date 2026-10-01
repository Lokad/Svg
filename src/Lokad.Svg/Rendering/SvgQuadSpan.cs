namespace Lokad.Svg.Rendering;

/// <summary>Retains one device-space quadratic segment with its control point for analytic coverage.</summary>
/// <remarks>StartIndex and EndIndex are relative to the contour slice the span belongs to (matching GetContour indexing), so translation needs no builder offsets.
/// Translation drops spans whose endpoints do not survive stripping, and separation drops spans across any rewriting stage.
/// Surviving spans always tile exact flattened ranges with a valid control. The walker consumes spans only under its own
/// eligibility; every other consumer keeps the flattened points.</remarks>
internal readonly record struct SvgQuadSpan(int Contour, int StartIndex, int EndIndex, SvgDevicePoint Control);
