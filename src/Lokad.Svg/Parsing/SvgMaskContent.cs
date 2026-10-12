using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>References one document-owned rectangle source retained for a uniform mask's spatial fallback.</summary>
internal readonly record struct SvgUniformMaskSource(int ChildIndex);

/// <summary>Retains one spatial mask evaluation for ordinary leaves, groups or affine viewport clips.</summary>
/// <remarks>Legacy contours own reserved arrays and shared grayscale paint; vector content borrows
/// an immutable scene adopted by its parent. Rasterization evaluates content into bounded mask/clip
/// surfaces, with charged replay and cancellation; cleanup never observes cancellation.</remarks>
internal readonly record struct SvgMaskContent(SvgGeometry[] Geometries, byte Luminance, bool IsAlpha)
{
    /// <summary>Borrows a parser source slot; materialized scene content never retains this staging reference.</summary>
    internal SvgUniformMaskSource? UniformSource { get; init; }

    /// <summary>Retains native clip origin/classification independently of ordinary mask content.</summary>
    internal SvgClipContentKind? ClipKind { get; init; }

    /// <summary>Borrows an earlier scene-owned rectangle clip; that rectangle has no further bounds clip.</summary>
    internal int BoundsClipContentIndex { get; init; } = -1;

    /// <summary>Borrows a replay scene adopted once by the containing scene; legacy contours remain separately owned.</summary>
    internal SvgVectorContent? VectorContent { get; init; }

    /// <summary>Retains the masked target coordinate map independently of borrowed source content.</summary>
    internal SvgAffineTransform CoordinateMap { get; init; } = SvgAffineTransform.Identity;
}
