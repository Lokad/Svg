namespace Lokad.Svg.Parsing;

/// <summary>Retains one masked-group mask content evaluation with its paint luminance.</summary>
/// <remarks>Geometries hold root-user-space outlines that rasterize exactly like fills into a temporary mask surface; the stored red channel of their shared grayscale paint scales the surface alpha into mask values. Arrays stay reserved until scene disposal.</remarks>
internal readonly record struct SvgMaskContent(SvgGeometry[] Geometries, byte Luminance, bool IsAlpha);
