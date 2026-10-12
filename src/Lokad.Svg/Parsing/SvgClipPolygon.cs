namespace Lokad.Svg.Parsing;

using Lokad.Svg.Rendering;

/// <summary>Retains one admitted clip-path polygon and its independent winding rule in root-user space.</summary>
/// <remarks>Points hold pre-device root-user coordinates. Every retained contour closes implicitly and has at least three vertices; ends partition the points exactly. Area-free short contours contribute no clip coverage. Compound paths replay directed winding or parity through the native clip scene, including holes, concavity, self-intersections and canceling coverage. Source commands remain borrowed from document staging.</remarks>
internal readonly record struct SvgClipPolygon(SvgDevicePoint[] Points, int[] ContourEnds)
{
    /// <summary>Borrows immutable native path commands owned by the document's clip staging registry.</summary>
    internal SvgAuthoredPath? SourcePath { get; init; }
    internal bool IsEvenOdd { get; init; }
    /// <summary>Selects authored replay for curves, compound contours and combined child paths.</summary>
    internal bool UsesNativeReplay { get; init; }
    /// <summary>Retains the native oval clip identity when its child map preserves axis-aligned oval bounds.</summary>
    internal bool IsOval { get; init; }
    /// <summary>Maps normalized content through the referencing element's geometry bounds before native replay.</summary>
    internal bool IsObjectBoundingBox { get; init; }
    /// <summary>Borrows the local clip applied to this resource's child geometry.</summary>
    internal string? NestedClipId { get; init; }
    /// <summary>Preserves the union of mapped child layout boxes independently of path union geometry.</summary>
    internal SvgPaintBounds? ResourceBounds { get; init; }
    internal int ChildCount { get; init; } = 1;
}
