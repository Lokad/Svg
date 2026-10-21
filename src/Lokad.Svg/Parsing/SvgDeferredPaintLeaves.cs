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
    /// <remarks>Descending command positions keep unprocessed paint slots stable. One batch rebuilds
    /// drawing order and remaps existing and newly emitted scopes. Geometry and recorded-paint owners
    /// stay in the same scene. Snapshot copies and command/range traversals charge work and cancellation;
    /// failure releases all owners through document teardown.</remarks>
    internal static void Resolve(SvgRootAttributes root, SvgDocumentState state, SvgRenderLimits limits,
        ParserWorkCounter work, CancellationToken token)
    {
        if (state.DeferredPaintLeaves.Count == 0) return;
        using SvgPaintCommandBatch commands = new(state, state.Scene, default, state.PendingCommandEnd, work, token);
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
                SvgPaintCommandEmission emission = commands.Begin(pending.CommandIndex);
                SvgRenderedLeafParser.ParseResolved(pending.Source, pending.Context, root, state, limits,
                    state.Budget, null, work, token);
                commands.Retain(emission);
            }
            finally
            {
                while (state.MaskScopes.Count > oldMaskCount) state.MaskScopes.RemoveLast();
                if (pending.Frame is not null) state.Viewports.RemoveLast();
            }
        }
        commands.Complete();
    }
}
