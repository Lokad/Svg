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

    /// <summary>Resolves normalized content coordinates against each referencing target independently.</summary>
    internal bool IsObjectBoundingBox { get; init; }

    /// <summary>Binds borrowed mask content to one target without copying its scene or changing its region units.</summary>
    /// <remarks>Pure bounded mapping; the caller retains source ownership and charges target-bound collection.
    /// Empty bounds paint nothing. Existing axis-map admission remains explicit; no staging owner escapes.</remarks>
    internal SvgMaskContent PlaceForTarget(SvgAffineTransform target, SvgRectangle bounds, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        if (!IsObjectBoundingBox) return this with { CoordinateMap = target.ComposeChecked(CoordinateMap) };
        if (bounds.Width <= 0 || bounds.Height <= 0) return new([], 255, IsAlpha);
        if (!target.IsAxisAligned || !target.TryInvert(out SvgAffineTransform inverse))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Bounding-box mask content requires a nonsingular axis-aligned target map.");
        SvgRectangle local = inverse.Apply(bounds);
        SvgAffineTransform box = SvgAffineTransform.FromScaleOffset(local.Width, local.Height, local.X, local.Y);
        return this with { CoordinateMap = target.ComposeChecked(box).ComposeChecked(CoordinateMap) };
    }
}
