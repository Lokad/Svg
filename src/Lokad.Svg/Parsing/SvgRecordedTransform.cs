using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one native recording boundary independently of flattened scene placement.</summary>
/// <remarks>The scene reserves and owns this immutable node; descendants and recorded paints borrow
/// its parent chain. Local maps combine intervening source transforms before float canvas composition.
/// Raster preparation checks cancellation per visited node and owns temporary storage.
/// Nodes contain no output-dependent cache and can be shared by independent raster draws.</remarks>
internal sealed class SvgRecordedTransform
{
    // Includes the immutable node, reservation token and scene owner-list entry.
    internal const long RetainedStorageBytes = 192;
    internal SvgRecordedTransform? Parent { get; }
    internal SvgAffineTransform Map { get; }
    internal SvgAffineTransform Local { get; }
    internal int Count { get; }

    internal SvgRecordedTransform(SvgRecordedTransform? parent, SvgAffineTransform map, SvgAffineTransform local)
    {
        Parent = parent;
        Map = map;
        Local = local;
        Count = checked((parent?.Count ?? 0) + 1);
    }

    /// <summary>Prepares a draw matrix in source recording order; false preserves the existing singular-map admission.</summary>
    /// <remarks>The authored viewport initial map joins the first source stage before narrowing.
    /// The output map belongs to the external bitmap/capture; target remains in scene coordinates.</remarks>
    internal bool TryMap(SvgShaderMatrix output, SvgAffineTransform initial, SvgAffineTransform target, RenderBudget budget,
CancellationToken token, out SvgShaderMatrix result)
    {
        result = default;
        token.ThrowIfCancellationRequested();
        if (!Map.TryInvert(out SvgAffineTransform inverse)) return false;
        using RenderBudget.BudgetReservation? owner = Count > 64
            ? budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgAffineTransform>(Count), SvgMemoryCategory.Surface) : null;
        Span<SvgAffineTransform> stages = Count <= 64 ? stackalloc SvgAffineTransform[Count] : new SvgAffineTransform[Count];
        int index = Count;
        for (SvgRecordedTransform? current = this; current is not null; current = current.Parent)
        {
            token.ThrowIfCancellationRequested();
            stages[--index] = current.Local;
        }
        result = output;
        for (int stageIndex = 0; stageIndex < stages.Length; stageIndex++)
        {
            SvgAffineTransform stage = stages[stageIndex];
            if (stageIndex == 0 && initial != SvgAffineTransform.Identity) stage = initial.Compose(stage);
            result = SvgShaderMatrix.Concat(result, SvgShaderMatrix.FromAffine(stage));
        }
        result = SvgShaderMatrix.Concat(result, SvgShaderMatrix.FromAffine(inverse.Compose(target)));
        return result.IsFinite;
    }
}
