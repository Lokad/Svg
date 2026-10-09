namespace Lokad.Svg.Parsing;

using Lokad.Svg.Rendering;

/// <summary>Retains one admitted multi-contour convex clip-path polygon in root-user space.</summary>
/// <remarks>Points hold pre-device root-user coordinates, the same space as lowered polygon points before raster mapping, so resolve-time intersection composes without a transform. Each contour closes implicitly from its last point back to its start, holds at least three distinct vertices with nonzero area, and turns consistently, which is exactly what the resolve-time Sutherland-Hodgman step requires. Contour ends partition the points exactly.</remarks>
internal readonly record struct SvgClipPolygon(SvgDevicePoint[] Points, int[] ContourEnds)
{
    /// <summary>Borrows immutable native curve commands owned by the document's clip staging registry.</summary>
    internal SvgAuthoredPath? SourcePath { get; init; }
}
