namespace Lokad.Svg.Parsing;

using Lokad.Svg.Rendering;

/// <summary>Retains one admitted clip-path polygon and its independent winding rule in root-user space.</summary>
/// <remarks>Points hold pre-device root-user coordinates. Every contour closes implicitly and has at least three retained vertices; ends partition the points exactly. A single contour replays through the native clip scene and may be concave, self-intersecting or empty despite cancelling signed area. Legacy multi-contour geometric intersection still requires nonzero convex contours and the nonzero rule. Source commands remain borrowed from document staging.</remarks>
internal readonly record struct SvgClipPolygon(SvgDevicePoint[] Points, int[] ContourEnds)
{
    /// <summary>Borrows immutable native curve commands owned by the document's clip staging registry.</summary>
    internal SvgAuthoredPath? SourcePath { get; init; }
    internal bool IsEvenOdd { get; init; }
}
