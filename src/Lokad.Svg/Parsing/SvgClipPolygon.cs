namespace Lokad.Svg.Parsing;

using Lokad.Svg.Rendering;

/// <summary>Retains one admitted single-contour convex clip-path polygon in root-user space.</summary>
/// <remarks>Points hold pre-device root-user coordinates, the same space as lowered polygon points before raster mapping, so resolve-time intersection composes without a transform. The contour closes implicitly from its last point back to its start, holds at least three distinct vertices with nonzero area, and turns consistently, which is exactly what the resolve-time Sutherland-Hodgman step requires.</remarks>
internal readonly record struct SvgClipPolygon(SvgDevicePoint[] Points);
