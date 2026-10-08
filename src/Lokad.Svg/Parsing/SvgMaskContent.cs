namespace Lokad.Svg.Parsing;

/// <summary>Retains one spatial mask evaluation for ordinary leaves, groups or affine viewport clips.</summary>
/// <remarks>Legacy contours own reserved arrays and shared grayscale paint; vector content borrows
/// an immutable scene adopted by its parent. Rasterization evaluates either into one bounded
/// temporary surface, with charged replay and cancellation; cleanup never observes cancellation.</remarks>
internal readonly record struct SvgMaskContent(SvgGeometry[] Geometries, byte Luminance, bool IsAlpha)
{
    /// <summary>Borrows a replay scene adopted once by the containing scene; legacy contours remain separately owned.</summary>
    internal SvgVectorContent? VectorContent { get; init; }
}
