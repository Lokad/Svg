using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>References one document-owned rectangle source retained for a uniform mask's spatial fallback.</summary>
internal readonly record struct SvgUniformMaskSource(int ChildIndex);
/// <summary>Borrows a contiguous document-owned source range only during mask preparation.</summary>
internal readonly record struct SvgMaskSourceRange(int FirstChild, int ChildCount);

/// <summary>Retains one spatial mask evaluation for ordinary leaves, groups or affine viewport clips.</summary>
/// <remarks>Legacy contours own reserved arrays and shared grayscale paint; vector content borrows
/// an immutable scene adopted by its parent. Rasterization evaluates content into bounded mask/clip
/// surfaces, with cancellation during replay; cleanup never observes cancellation.</remarks>
internal readonly record struct SvgMaskContent(SvgGeometry[] Geometries, byte Luminance, bool IsAlpha)
{
    /// <summary>Marks a resource whose own display removes the binding, independently of empty mask content.</summary>
    internal bool IsDisabled { get; init; }
    /// <summary>Retains the source-enclosed union of mask and visual bounds after placement, independently of mask allocation.</summary>
    internal SvgClipBox? SourcePaintRegion { get; init; }
    /// <summary>Supplies uniform coverage without constructing a spatial mask surface.</summary>
    internal byte? ConstantAlpha { get; init; }

    /// <summary>Borrows a parser source slot; materialized scene content never retains this staging reference.</summary>
    internal SvgUniformMaskSource? UniformSource { get; init; }
    /// <summary>Borrows validated source snapshots until target-specific native replay finishes during preparation.</summary>
    internal SvgMaskSourceRange? ReplaySource { get; init; }

    /// <summary>Retains native clip origin/classification independently of ordinary mask content.</summary>
    internal SvgClipContentKind? ClipKind { get; init; }
    /// <summary>Retains the clipped consumer's canvas boundary independently of resource placement and clip bounds.</summary>
    internal SvgCanvasDrawMap? DrawMap { get; init; }

    /// <summary>Borrows an earlier scene-owned rectangle clip; that rectangle has no further bounds clip.</summary>
    internal int BoundsClipContentIndex { get; init; } = -1;

    /// <summary>Borrows a replay scene adopted once by the containing scene; legacy contours remain separately owned.</summary>
    internal SvgVectorContent? VectorContent { get; init; }

    /// <summary>Retains the masked target coordinate map independently of borrowed source content.</summary>
    internal SvgAffineTransform CoordinateMap { get; init; } = SvgAffineTransform.Identity;

    /// <summary>Resolves normalized content coordinates against each referencing target independently.</summary>
    internal bool IsObjectBoundingBox { get; init; }
    /// <summary>Retains the affine target's antialiased paint-clip consumer separately from its binary mask-source region.</summary>
    internal bool UsesAffineTarget { get; init; }

}
