namespace Lokad.Svg.Rendering;

/// <summary>Retains one device-space cubic segment with its control points for analytic coverage.</summary>
/// <remarks>StartIndex and EndIndex are relative to the contour slice the span belongs to (matching GetContour indexing), exactly like quadratic spans.
/// Translation drops spans whose endpoints do not survive stripping, and separation drops spans across any rewriting stage.
/// Surviving spans always tile exact flattened ranges with valid controls. The walker consumes spans only under its own
/// eligibility; every other consumer keeps the flattened points.</remarks>
internal readonly record struct SvgCubicSpan(int Contour, int StartIndex, int EndIndex, SvgDevicePoint FirstControl, SvgDevicePoint SecondControl);
