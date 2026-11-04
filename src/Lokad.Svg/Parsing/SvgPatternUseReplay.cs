using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal readonly record struct SvgPatternContentUse(SvgResourceUse Use, int CommandIndex);
internal readonly record struct SvgPatternContentScope(string? ClipId, string? MaskId, SvgAffineTransform Map, int FirstCommandIndex, bool HasOpacity, bool HasOwnFilter);

/// <summary>The document owns the staging builder; the parent scene owns content borrowed by pattern paints.</summary>
internal readonly record struct SvgDeferredPatternContent(SvgSceneBuilder Builder, SvgVectorContent Content, int FirstUse, int UseCount,
    int FirstScope, int ScopeCount, SvgPendingCommandStart FirstCommands, SvgPendingCommandStart EndCommands);

/// <summary>Resolves ordered local tile instances through existing reference and premultiplied scene machinery.</summary>
/// <remarks>References borrow document-owned strings and definitions. The staging builder retains all mapped
/// owners until sealing or failure cleanup. Replay is parser-thread confined, budgeted and
/// cancellation aware. Content becomes immutable before the root scene is returned.</remarks>
internal static class SvgPatternUseReplay
{
    // Tile rasterization borrows child scenes recursively. Bound the actual
    // content path, independently of collapsed template href chains.
    internal const int MaximumContentDepth = 32;

    private readonly record struct ContentFrame(SvgVectorContent Content, int GeometryIndex,
        int MaskIndex, int MaskGeometryIndex, int Depth);
    private readonly record struct ContentVisit(bool Active, int Depth);

    private readonly record struct ScopeRange(int First, int End)
    {
        internal bool ClipsFilterOutput { get; init; }
    }

    internal static void Retain(XmlReader reader, SvgRootAttributes root, SvgDocumentState state,
        IReadOnlyDictionary<string, SvgColor> colors, SvgSceneBuilder builder, CancellationToken token)
    {
        if (SvgResourceUseReplay.RetainPattern(reader, root, state, colors, token) is not {} reference) return;
        if (state.PatternUseCount == 0) state.PatternUseStart = state.PatternUses.Count;
        state.PatternUses.Add(new(reference, builder.AddPaintPlaceholder()));
        state.PatternUseCount++;
    }

    /// <summary>Expands paint, text and uses together, locates balanced tile scopes once and compacts clip-only markers once.</summary>
    /// <remarks>Original and replay-created command ranges stay within their owning scene. Scope starts
    /// follow batch expansion directly, independently of inherited recording identities. Nested clip work
    /// visits only the containing ranges. All scratch is budget-owned and observes cancellation;
    /// failure leaves adopted scene resources with document teardown.</remarks>
    internal static void Resolve(SvgRootAttributes root, SvgDocumentState state, SvgRenderLimits limits, CancellationToken token)
    {
        foreach (SvgDeferredPatternContent pending in state.DeferredPatternContents.AsSpan())
        {
            token.ThrowIfCancellationRequested();
            SvgPendingCommandStart replayStart = state.PendingCommandEnd;
            if (pending.UseCount != 0 || pending.FirstCommands.Text != pending.EndCommands.Text || pending.FirstCommands.Paint != pending.EndCommands.Paint)
            {
                using SvgPaintCommandBatch commands = new(state, pending.Builder, pending.FirstCommands, pending.EndCommands, token);
                int textIndex = pending.EndCommands.Text - 1;
                int paintIndex = pending.EndCommands.Paint - 1;
                int useIndex = pending.FirstUse + pending.UseCount - 1;
                // All registries retain ascending source slots. Merge their tails so
                // no kind needs intermediate slot remapping or another scene rebuild.
                while (textIndex >= pending.FirstCommands.Text || paintIndex >= pending.FirstCommands.Paint || useIndex >= pending.FirstUse)
                {
                    token.ThrowIfCancellationRequested();
                    int textPosition = textIndex >= pending.FirstCommands.Text ? state.PendingTextRuns[textIndex].CommandIndex : -1;
                    int paintPosition = paintIndex >= pending.FirstCommands.Paint ? state.DeferredPaintLeaves[paintIndex].CommandIndex : -1;
                    int usePosition = useIndex >= pending.FirstUse ? state.PatternUses[useIndex].CommandIndex : -1;
                    int position = Math.Max(paintPosition, Math.Max(textPosition, usePosition));
                    SvgPaintCommandEmission emission = commands.Begin(position);
                    if (position == paintPosition) SvgDeferredPaintLeaves.Emit(paintIndex--, root, state, limits, token);
                    else if (position == textPosition) SvgTextPathLayout.Emit(state, textIndex--, limits, token);
                    else SvgResourceUseReplay.AppendPatternUse(state.PatternUses[useIndex--].Use, pending.Builder, state, token);
                    commands.Retain(emission);
                }
                Span<SvgPatternContentScope> scopes = pending.ScopeCount == 0 ? []
                    : (state.PatternScopes ?? throw new InvalidOperationException("Retained pattern scopes require their source registry.")).AsMutableSpan().Slice(pending.FirstScope, pending.ScopeCount);
                commands.Complete(scopes);
            }
            BindScopes(pending, replayStart, state.PendingCommandEnd, state, token);
        }

        static void BindScopes(SvgDeferredPatternContent pending, SvgPendingCommandStart replayStart, SvgPendingCommandStart replayEnd,
            SvgDocumentState state, CancellationToken token)
        {
            if (pending.ScopeCount == 0) return;
            SvgSceneBuilder builder = pending.Builder;
            ReadOnlySpan<SvgPatternContentScope> scopes = (state.PatternScopes
                ?? throw new InvalidOperationException("Retained pattern scopes require their source registry.")).AsSpan().Slice(pending.FirstScope, pending.ScopeCount);
            using BudgetedParserList<ScopeRange> ranges = new(state.Budget, SvgMemoryCategory.Parser, token);
            using BudgetedParserList<int> stack = new(state.Budget, SvgMemoryCategory.Parser, token);
            int nextScope = 0;
            for (int index = 0; index < builder.CommandCount; index++)
            {
                token.ThrowIfCancellationRequested();
                SvgDrawCommand command = builder.CommandAt(index);
                if (command.Kind == SvgDrawCommandKind.BeginGroup)
                {
                    int scope = nextScope < scopes.Length && scopes[nextScope].FirstCommandIndex == index ? nextScope++ : -1;
                    if (scope >= 0) ranges.Add(new(index, -1));
                    stack.Add(scope);
                }
                else if (command.Kind == SvgDrawCommandKind.EndGroup)
                {
                    if (stack.Count == 0) throw new InvalidOperationException("A retained pattern scope requires balanced groups.");
                    int scope = stack[^1];
                    stack.RemoveLast();
                    if (scope >= 0) ranges[scope] = ranges[scope] with { End = index };
                }
            }
            if (stack.Count != 0 || nextScope != scopes.Length) throw new InvalidOperationException("A retained pattern scope requires balanced source boundaries.");

            bool compact = false;
            for (int scopeIndex = 0; scopeIndex < scopes.Length; scopeIndex++)
            {
                token.ThrowIfCancellationRequested();
                SvgPatternContentScope scope = scopes[scopeIndex];
                ScopeRange range = ranges[scopeIndex];
                compact |= !scope.HasOpacity && scope.MaskId is null;
                if (scope.ClipId is not {} clipId || SvgClipPathSyntax.IsDisabled(clipId, state)) continue;
                bool clipsFilterOutput = false;
                for (int index = range.First + 1; index < range.End; index++)
                {
                    token.ThrowIfCancellationRequested();
                    if (builder.CommandAt(index) is { Kind: SvgDrawCommandKind.BeginGroup, RegionKind: SvgLayerRegionKind.Filter })
                    {
                        clipsFilterOutput = true;
                        break;
                    }
                }
                // A leaf binding would move this ancestor clip into a child filter.
                // Resolve its compacted command range through the ordinary group path.
                if (clipsFilterOutput)
                {
                    ranges[scopeIndex] = range with { ClipsFilterOutput = true };
                    continue;
                }
                SvgLeafMaskOperation? scopeClip = BindScopeClip(scope, range, builder, state, token);
                SvgLeafMaskBinding? lastInner = null, lastBinding = null;
                for (int index = range.First + 1; index < range.End; index++)
                {
                    token.ThrowIfCancellationRequested();
                    SvgDrawCommand command = builder.CommandAt(index);
                    if (command.Kind != SvgDrawCommandKind.Geometry) continue;
                    SvgLeafMaskBinding? inner = builder.GeometryAt(command.GeometryIndex).LeafMask;
                    if (lastBinding is null || !ReferenceEquals(lastInner, inner))
                    {
                        lastInner = inner;
                        lastBinding = scopeClip is null
                            ? SvgClipPathSyntax.BindResourceClip(clipId, scope.Map, inner, state, token)
                            : SvgLeafMaskComposition.NewBinding(state, scopeClip, inner);
                    }
                    builder.AssignLeafMask(command.GeometryIndex, lastBinding);
                }
            }

            int count = builder.CommandCount;
            using RenderBudget.BudgetReservation? mapOwner = compact
                ? state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<int>(count + 1), SvgMemoryCategory.Parser) : null;
            int[] positions = compact ? new int[count + 1] : [];
            if (compact)
            {
                for (int index = 0; index < scopes.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    if (!scopes[index].HasOpacity && scopes[index].MaskId is null)
                        positions[ranges[index].First] = positions[ranges[index].End] = -1;
                }
                BudgetedSceneList<SvgDrawCommand> commands = new(state.Budget, token);
                bool transferred = false;
                try
                {
                    for (int index = 0; index < count; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        bool remove = positions[index] < 0;
                        positions[index] = commands.Count;
                        if (!remove) commands.Add(builder.CommandAt(index));
                    }
                    positions[count] = commands.Count;
                    SvgReferenceResolver.RemapPendingCommandRanges(state, builder, positions, pending.FirstCommands, pending.EndCommands, token);
                    SvgReferenceResolver.RemapPendingCommandRanges(state, builder, positions, replayStart, replayEnd, token);
                    builder.AdoptCommands(commands);
                    transferred = true;
                }
                finally { if (!transferred) commands.Dispose(); }
            }
            for (int index = 0; index < scopes.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                SvgPatternContentScope scope = scopes[index];
                ScopeRange range = ranges[index];
                if (range.ClipsFilterOutput && scope.ClipId is {} clipId)
                    state.PendingGroupClips.Add(new PendingGroupClip(clipId, null, scope.Map,
                        compact ? positions[range.First] : range.First,
                        compact ? positions[range.End] : range.End, 0, builder) { UsesNativeClip = true });
                if (scope.MaskId is not {} id) continue;
                state.PendingGroupMasks.Add(new(id, compact ? positions[range.First] : range.First,
                    compact ? positions[range.End] : range.End, 0, null, scope.Map, builder, scope.HasOwnFilter));
            }
        }

        static SvgLeafMaskOperation? BindScopeClip(SvgPatternContentScope clip, ScopeRange range, SvgSceneBuilder builder,
            SvgDocumentState state, CancellationToken token)
        {
            if (clip.ClipId is not {} id || SvgClipPathSyntax.IsDisabled(id, state)) return null;
            bool rectangle = state.ObjectClips.TryGetValue(id, out SvgClipBox fractions);
            state.ClipContents.TryGetValue(id, out SvgClipContent polygon);
            if (!rectangle && !polygon.IsObjectBoundingBox && !polygon.HasIntersections) return null;
            SvgSceneBuilder? previous = state.DefinitionReplayScene;
            state.DefinitionReplayScene = builder;
            try
            {
                if (!polygon.IsObjectBoundingBox && polygon.HasIntersections)
                    return SvgClipPathSyntax.DeferNestedResourceMask(id, clip.Map, state);
                SvgObjectBoundingClip target = new(clip.Map, null);
                for (int index = range.First + 1; index < range.End; index++)
                {
                    token.ThrowIfCancellationRequested();
                    SvgDrawCommand command = builder.CommandAt(index);
                    if (command.Kind == SvgDrawCommandKind.Geometry)
                        target.Add(builder.GeometryAt(command.GeometryIndex), state, token);
                }
                return rectangle ? target.Register(fractions, state, token) : target.Register(polygon, state, token);
            }
            finally { state.DefinitionReplayScene = previous; }
        }
    }

    /// <summary>Seals tile scenes and validates their borrowed content graph before rasterization.</summary>
    /// <remarks>Scenes retain their existing parent owners. Iterative validation reserves its
    /// lookup and stack storage, checks cancellation per edge and releases scratch on every exit.
    /// Cycles and excessive recursive content depth fail before any tile is rasterized.</remarks>
    internal static void Complete(SvgDocumentState state, CancellationToken token)
    {
        foreach (SvgDeferredPatternContent pending in state.DeferredPatternContents.AsSpan())
        {
            token.ThrowIfCancellationRequested();
            SvgLeafMaskComposition.Emit(state, pending.Builder, token);
            pending.Content.Complete(pending.Builder.Build());
        }
        if (state.DeferredPatternContents.Count == 0) return;
        using BudgetedParserDictionary<SvgVectorContent, ContentVisit> visits = new(state.Budget, SvgMemoryCategory.Temporary, 96);
        using BudgetedParserList<ContentFrame> stack = new(state.Budget, SvgMemoryCategory.Temporary, token);
        foreach (SvgDeferredPatternContent pending in state.DeferredPatternContents.AsSpan())
        {
            token.ThrowIfCancellationRequested();
            if (visits.TryGetValue(pending.Content, out _)) continue;
            visits.TryAdd(pending.Content, new(true, 0));
            stack.Add(new(pending.Content, 0, 0, -1, 1));
            while (stack.Count != 0)
            {
                token.ThrowIfCancellationRequested();
                ContentFrame frame = stack[^1];
                SvgVectorContent? child = NextChild(ref frame);
                stack[^1] = frame;
                if (child is null)
                {
                    visits.Replace(frame.Content, new(false, frame.Depth));
                    stack.RemoveLast();
                    if (stack.Count != 0) IncludeDepth(frame.Depth);
                }
                else if (visits.TryGetValue(child, out ContentVisit visit))
                {
                    if (visit.Active) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Cyclic pattern content is not implemented.");
                    IncludeDepth(visit.Depth);
                }
                else
                {
                    if (stack.Count == MaximumContentDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern content nesting exceeds the supported depth.");
                    visits.TryAdd(child, new(true, 0));
                    stack.Add(new(child, 0, 0, -1, 1));
                }
            }
        }

        void IncludeDepth(int childDepth)
        {
            int depth = Math.Max(stack[^1].Depth, childDepth + 1);
            if (depth > MaximumContentDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern content nesting exceeds the supported depth.");
            stack[^1] = stack[^1] with { Depth = depth };
        }

        SvgVectorContent? NextChild(ref ContentFrame frame)
        {
            SvgSceneStorage scene = frame.Content.Scene;
            while (frame.GeometryIndex < scene.Geometries.Length)
            {
                token.ThrowIfCancellationRequested();
                SvgGeometry geometry = scene.Geometries[frame.GeometryIndex];
                frame = frame with { GeometryIndex = frame.GeometryIndex + 1 };
                if (PaintContent(geometry) is {} child) return child;
            }
            while (frame.MaskIndex < scene.MaskContents.Length)
            {
                token.ThrowIfCancellationRequested();
                SvgMaskContent mask = scene.MaskContents[frame.MaskIndex];
                if (frame.MaskGeometryIndex < 0)
                {
                    frame = frame with { MaskGeometryIndex = 0 };
                    if (mask.VectorContent is {} child) return child;
                }
                while (frame.MaskGeometryIndex < mask.Geometries.Length)
                {
                    token.ThrowIfCancellationRequested();
                    SvgGeometry geometry = mask.Geometries[frame.MaskGeometryIndex];
                    frame = frame with { MaskGeometryIndex = frame.MaskGeometryIndex + 1 };
                    if (PaintContent(geometry) is {} child) return child;
                }
                frame = frame with { MaskIndex = frame.MaskIndex + 1, MaskGeometryIndex = -1 };
            }
            return null;
        }

        static SvgVectorContent? PaintContent(SvgGeometry geometry) => geometry.Kind switch
        {
            SvgGeometryKind.PatternFill => geometry.Pattern.VectorContent,
            SvgGeometryKind.PatternPolygon => geometry.PatternPolygon.VectorContent,
            SvgGeometryKind.GlyphContours when geometry.Glyph.Shader is SvgTextPatternShader paint => paint.Pattern.VectorContent,
            _ => null
        };
    }
}
