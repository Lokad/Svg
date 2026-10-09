namespace Lokad.Svg.Parsing;

using Lokad.Svg.Rendering;

/// <summary>Retains one admitted clip-path polygon and its independent winding rule in root-user space.</summary>
/// <remarks>Points hold pre-device root-user coordinates. Every contour closes implicitly and has at least three retained vertices; ends partition the points exactly. Single contours and retained compound curves replay through the native clip scene and may include holes, concavity, self-intersections or empty coverage despite cancelling signed area. Legacy multi-contour geometric intersection still requires nonzero convex contours and the nonzero rule. Source commands remain borrowed from document staging.</remarks>
internal readonly record struct SvgClipPolygon(SvgDevicePoint[] Points, int[] ContourEnds)
{
    /// <summary>Borrows immutable native path commands owned by the document's clip staging registry.</summary>
    internal SvgAuthoredPath? SourcePath { get; init; }
    internal bool IsEvenOdd { get; init; }
    /// <summary>Selects authored replay for curves and combined child paths; legacy straight intersections stay distinct.</summary>
    internal bool UsesNativeReplay { get; init; }
    /// <summary>Retains the native oval clip identity when its child map preserves axis-aligned oval bounds.</summary>
    internal bool IsOval { get; init; }
    /// <summary>Preserves the union of mapped child layout boxes independently of path union geometry.</summary>
    internal SvgPaintBounds? ResourceBounds { get; init; }
    internal int ChildCount { get; init; } = 1;
}
