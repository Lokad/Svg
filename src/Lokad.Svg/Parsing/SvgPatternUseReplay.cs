using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal readonly record struct SvgPatternContentUse(SvgResourceUse Use, int CommandIndex);
internal readonly record struct SvgPatternContentScope(string? ClipId, string? MaskId, SvgAffineTransform Map, SvgRecordingScope Scope, bool HasOpacity);

/// <summary>The document owns the staging builder; the parent scene owns content borrowed by pattern paints.</summary>
internal readonly record struct SvgDeferredPatternContent(SvgSceneBuilder Builder, SvgVectorContent Content, int FirstUse, int UseCount, int FirstScope, int ScopeCount);

/// <summary>Resolves ordered local tile instances through existing reference and premultiplied scene machinery.</summary>
/// <remarks>References borrow document-owned strings and definitions. The staging builder retains all mapped
/// owners until sealing or failure cleanup. Replay is parser-thread confined, charged to shared work and
/// cancellation aware. Content becomes immutable before the root scene is returned.</remarks>
internal static class SvgPatternUseReplay
{
    internal static void Retain(XmlReader reader, SvgRootAttributes root, SvgDocumentState state,
        IReadOnlyDictionary<string, SvgColor> colors, SvgSceneBuilder builder, ParserWorkCounter work, CancellationToken token)
    {
        SvgResourceUse reference = SvgResourceUseReplay.RetainPattern(reader, root, state, colors, work, token);
        if (state.PatternUseCount == 0) state.PatternUseStart = state.PatternUses.Count;
        state.PatternUses.Add(new(reference, builder.AddPaintPlaceholder()));
        state.PatternUseCount++;
    }

    internal static void Resolve(SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        foreach (SvgDeferredPatternContent pending in state.DeferredPatternContents.AsSpan())
        {
            work.Consume(token);
            // Descending positions preserve every unresolved slot when a use expands to several draws.
            if (pending.UseCount != 0)
            {
                using SvgPaintCommandBatch commands = new(state, pending.Builder, work, token);
                for (int index = pending.FirstUse + pending.UseCount - 1; index >= pending.FirstUse; index--)
                {
                    work.Consume(token);
                    SvgPatternContentUse use = state.PatternUses[index];
                    SvgPaintCommandEmission emission = commands.Begin(use.CommandIndex);
                    SvgResourceUseReplay.AppendPatternUse(use.Use, pending.Builder, state, work, token);
                    commands.Retain(emission);
                }
                commands.Complete();
            }
            for (int index = pending.FirstScope; index < pending.FirstScope + pending.ScopeCount; index++)
                ApplyScope(state.PatternScopes?[index] ?? throw new InvalidOperationException("Retained pattern scopes require their source registry."), pending.Builder, state, false, work, token);
            // Clip-only marker removal changes command positions. Record mask ranges only afterwards.
            for (int index = pending.FirstScope; index < pending.FirstScope + pending.ScopeCount; index++)
            {
                SvgPatternContentScope scope = state.PatternScopes?[index] ?? throw new InvalidOperationException("Retained pattern scopes require their source registry.");
                if (scope.MaskId is not null) ApplyScope(scope, pending.Builder, state, true, work, token);
            }
        }

        // Scope identities survive use expansion, unlike absolute command offsets.
        // Bind through the ordinary resource clip machinery before the scene seals.
        static void ApplyScope(SvgPatternContentScope clip, SvgSceneBuilder builder, SvgDocumentState state, bool bindMask, ParserWorkCounter work, CancellationToken token)
        {
            int depth = 0;
            int first = -1;
            SvgLeafMaskOperation? scopeClip = bindMask ? null : BindScopeClip();
            SvgLeafMaskBinding? lastInner = null, lastBinding = null;
            for (int index = 0; index < builder.CommandCount; index++)
            {
                work.Consume(token);
                SvgDrawCommand command = builder.CommandAt(index);
                if (first < 0)
                {
                    if (command.Kind != SvgDrawCommandKind.BeginGroup || command.GeometryIndex != clip.Scope.Value) continue;
                    first = index;
                }
                if (command.Kind == SvgDrawCommandKind.BeginGroup) depth++;
                else if (command.Kind == SvgDrawCommandKind.EndGroup && --depth == 0)
                {
                    if (bindMask && clip.MaskId is {} maskId)
                    {
                        state.PendingGroupMasks.Add(new(maskId, first, index, 0, null, clip.Map, builder));
                        return;
                    }
                    if (!clip.HasOpacity && clip.MaskId is null)
                    {
                        // Clip-only groups do not create an isolated color surface.
                        // Their stable markers were needed only until uses expanded.
                        BudgetedSceneList<SvgDrawCommand> commands = new(state.Budget, work, token);
                        bool transferred = false;
                        try
                        {
                            for (int copy = 0; copy < builder.CommandCount; copy++)
                            {
                                work.Consume(token);
                                if (copy != first && copy != index) commands.Add(builder.CommandAt(copy));
                            }
                            builder.AdoptCommands(commands);
                            transferred = true;
                        }
                        finally { if (!transferred) commands.Dispose(); }
                    }
                    return;
                }
                if (bindMask || command.Kind != SvgDrawCommandKind.Geometry || clip.ClipId is not {} clipId) continue;
                SvgLeafMaskBinding? inner = builder.GeometryAt(command.GeometryIndex).LeafMask;
                if (lastBinding is null || !ReferenceEquals(lastInner, inner))
                {
                    lastInner = inner;
                    lastBinding = scopeClip is null
                        ? SvgClipPathSyntax.BindResourceClip(clipId, clip.Map, inner, state, work, token)
                        : SvgLeafMaskComposition.NewBinding(state, scopeClip, inner);
                }
                builder.AssignLeafMask(command.GeometryIndex, lastBinding);
            }
            throw new InvalidOperationException("A retained pattern scope requires a balanced source group.");

            SvgLeafMaskOperation? BindScopeClip()
            {
                if (clip.ClipId is not {} id) return null;
                bool rectangle = state.ObjectClips.TryGetValue(id, out SvgClipBox fractions);
                state.ClipPolygons.TryGetValue(id, out SvgClipPolygon polygon);
                if (!rectangle && !polygon.IsObjectBoundingBox && polygon.NestedClipId is null) return null;
                SvgSceneBuilder? previous = state.DefinitionReplayScene;
                state.DefinitionReplayScene = builder;
                try
                {
                    if (!polygon.IsObjectBoundingBox && polygon.NestedClipId is not null)
                        return SvgClipPathSyntax.DeferNestedResourceMask(id, clip.Map, state);
                    SvgObjectBoundingClip target = new(clip.Map, null);
                    int scopeDepth = 0;
                    for (int index = 0; index < builder.CommandCount; index++)
                    {
                        work.Consume(token);
                        SvgDrawCommand command = builder.CommandAt(index);
                        if (scopeDepth == 0 && (command.Kind != SvgDrawCommandKind.BeginGroup || command.GeometryIndex != clip.Scope.Value)) continue;
                        if (command.Kind == SvgDrawCommandKind.BeginGroup) scopeDepth++;
                        else if (command.Kind == SvgDrawCommandKind.EndGroup && --scopeDepth == 0)
                            return rectangle ? target.Register(fractions, state, work, token) : target.Register(polygon, state, work, token);
                        // Bounds and clip content both belong to the tile scene. Collect
                        // the complete target before other clips can trim its geometry.
                        if (command.Kind == SvgDrawCommandKind.Geometry)
                            target.Add(builder.GeometryAt(command.GeometryIndex), state, work, token);
                    }
                    throw new InvalidOperationException("A retained pattern scope requires a balanced source group.");
                }
                finally { state.DefinitionReplayScene = previous; }
            }
        }
    }

    /// <summary>Seals tile scenes after all deferred masks and clips have bound to their owning builder.</summary>
    internal static void Complete(SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        foreach (SvgDeferredPatternContent pending in state.DeferredPatternContents.AsSpan())
        {
            work.Consume(token);
            SvgLeafMaskComposition.Emit(state, pending.Builder, work, token);
            pending.Content.Complete(pending.Builder.Build());
        }
    }
}
