using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one unresolved leaf's attributes, inherited context and original command position.</summary>
/// <remarks>The document owns attributes and snapshot storage. Replay is parser-thread confined;
/// original coordinate, clip and opacity scopes stay independent from definition availability.</remarks>
internal readonly record struct SvgDeferredPaintLeaf(SvgDefinitionLeafSource Source, SvgPaintableElementContext Context,
    SvgViewportFrame? Frame, int CommandIndex, int MaskScopeStart, int MaskScopeCount);

internal static class SvgDeferredPaintLeaves
{
    internal static bool TryRetain(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state)
    {
        if (!SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName)) return false;
        SvgComputedStyle style = context.ResolveStyle(reader);
        SvgMarkerStyle markers = SvgMarkerStyle.Resolve(style, context.InheritedPresentation.Markers);
        if (!Missing(style.Fill) && !Missing(style.Stroke ?? context.InheritedStroke.Stroke) && !MissingMarker(markers.Start) && !MissingMarker(markers.End) && (reader.LocalName == "line" || !MissingMarker(markers.Mid))) return false;
        SvgDefinitionLeafSource source = SvgDefinitionLeafSource.CaptureUnconstructed(reader, context, state);
        int start = SvgReferenceResolver.SnapshotMaskScopes(state, context.WorkCounter, context.CancellationToken);
        state.DeferredPaintLeaves.Add(new(source, context with { AuthoredStyle = source.AuthoredStyle, StyleSheet = default },
            state.EffectiveFrame, state.Scene.AddPaintPlaceholder(), start, state.MaskScopeSnapshots.Count - start));
        return true;

        bool Missing(string? paint)
        {
            if (!SvgGradientSyntax.IsGradientFill(paint)) return false;
            context.WorkCounter.Consume(context.CancellationToken);
            string id = SvgGradientSyntax.ParsePaintId(paint);
            return !state.Gradients.TryGetValue(id, out _) && !state.RadialGradients.TryGetValue(id, out _) && !state.Patterns.TryGetValue(id, out _);
        }

        bool MissingMarker(string? value)
        {
            if (!SvgMarkerStyle.Active(value)) return false;
            context.WorkCounter.Consume(context.CancellationToken);
            string id = SvgGradientSyntax.ParsePaintId(value);
            return !state.Definitions.TryGetValue(id, out SvgDefinition definition) || definition.Marker is null;
        }
    }

    /// <summary>Replays retained leaves through the ordinary lowerers after all local paint definitions resolve.</summary>
    /// <remarks>Descending command positions keep unprocessed paint slots stable. Appended commands move
    /// into their original slots; geometry and recorded-paint owners stay in the same scene. Every snapshot
    /// copy, command move and range shift charges work and observes cancellation. Failure releases all owners
    /// through document teardown. Existing use, clip and mask ranges follow each inserted command.</remarks>
    internal static void Resolve(SvgRootAttributes root, SvgDocumentState state, SvgRenderLimits limits,
        ParserWorkCounter work, CancellationToken token)
    {
        for (int index = state.DeferredPaintLeaves.Count - 1; index >= 0; index--)
        {
            work.Consume(token);
            SvgDeferredPaintLeaf pending = state.DeferredPaintLeaves[index];
            int oldMaskCount = state.MaskScopes.Count;
            if (pending.Frame is {} frame) state.Viewports.Add(frame);
            try
            {
                for (int scope = 0; scope < pending.MaskScopeCount; scope++)
                    state.MaskScopes.Add(state.MaskScopeSnapshots[pending.MaskScopeStart + scope]);
                int tail = state.Scene.CommandCount;
                SvgRenderedLeafParser.ParseResolved(pending.Source, pending.Context, root, state, limits,
                    state.Budget, null, work, token);
                int inserted = state.Scene.ResolvePaintPlaceholder(pending.CommandIndex, tail);
                SvgReferenceResolver.ShiftPendingCommandRanges(state, pending.CommandIndex + 1, inserted, true, work, token);
                if (inserted != 0)
                {
                    for (int useIndex = 0; useIndex < state.PendingUses.Count; useIndex++)
                    {
                        work.Consume(token);
                        PendingUse use = state.PendingUses[useIndex];
                        if (use.Slot.CommandIndex > pending.CommandIndex)
                            state.PendingUses[useIndex] = use with { Slot = use.Slot with { CommandIndex = use.Slot.CommandIndex + inserted } };
                    }
                }
            }
            finally
            {
                while (state.MaskScopes.Count > oldMaskCount) state.MaskScopes.RemoveLast();
                if (pending.Frame is not null) state.Viewports.RemoveLast();
            }
        }
    }
}
