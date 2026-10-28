using System.Xml;

namespace Lokad.Svg.Parsing;

internal enum SvgDeferredPaintKind { Rendered, PatternDefinition }

/// <summary>Retains one unresolved leaf's attributes, inherited context and original command position.</summary>
/// <remarks>The document owns attributes and snapshot storage. Replay is parser-thread confined;
/// original coordinate, clip and opacity scopes stay independent from definition availability.</remarks>
internal readonly record struct SvgDeferredPaintLeaf(SvgDefinitionLeafSource Source, SvgPaintableElementContext Context,
    SvgViewportFrame? Frame, int CommandIndex, int MaskScopeStart, int MaskScopeCount, SvgSceneBuilder Scene, SvgDeferredPaintKind Kind);

internal static class SvgDeferredPaintLeaves
{
    internal static bool TryRetain(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state)
    {
        if (!NeedsReplay(reader, context, state)) return false;
        Retain(reader, context, state, SvgDeferredPaintKind.Rendered);
        return true;
    }

    /// <summary>Checks paint and marker availability before choosing a scene or the single-rectangle tile route.</summary>
    internal static bool NeedsReplay(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state)
    {
        if (!SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName)) return false;
        SvgComputedStyle style = context.ResolveStyle(reader);
        SvgMarkerStyle markers = SvgMarkerStyle.Resolve(style, context.InheritedPresentation.Markers);
        return Missing(style.Fill) || Missing(style.Stroke ?? context.InheritedStroke.Stroke) || MissingMarker(markers.Start)
            || MissingMarker(markers.End) || reader.LocalName != "line" && MissingMarker(markers.Mid);

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
            // Retained marker content can depend on definitions declared after its host.
            return !state.Definitions.TryGetValue(id, out SvgDefinition definition) || definition.Marker is null
                || definition.SymbolContent is not null;
        }
    }

    /// <summary>Retains one leaf in the current scene, preserving its existing lowering route and borrowed mask scopes.</summary>
    internal static void Retain(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state, SvgDeferredPaintKind kind)
    {
        SvgDefinitionLeafSource source = SvgDefinitionLeafSource.CaptureUnconstructed(reader, context, state);
        int start = SvgReferenceResolver.SnapshotMaskScopes(state, context.WorkCounter, context.CancellationToken);
        state.DeferredPaintLeaves.Add(new(source, context with { AuthoredStyle = source.AuthoredStyle, StyleSheet = default },
            state.EffectiveFrame, state.Scene.AddPaintPlaceholder(), start, state.MaskScopeSnapshots.Count - start, state.Scene, kind));
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
            if (!ReferenceEquals(pending.Scene, state.Scene)) continue;
            SvgPaintCommandEmission emission = commands.Begin(pending.CommandIndex);
            Emit(index, root, state, limits, work, token);
            commands.Retain(emission);
        }
        commands.Complete();
    }

    /// <summary>Emits one leaf into its originating scene; the caller replaces its reserved slot in source order.</summary>
    /// <remarks>The document owns retained attributes and adopted geometry. Borrowed scene, viewport and
    /// mask bindings restore on every exit; lowering charges shared work and observes budget/cancellation.</remarks>
    internal static void Emit(int index, SvgRootAttributes root, SvgDocumentState state, SvgRenderLimits limits,
        ParserWorkCounter work, CancellationToken token)
    {
        SvgDeferredPaintLeaf pending = state.DeferredPaintLeaves[index];
        bool tile = !ReferenceEquals(pending.Scene, state.Scene);
        SvgSceneBuilder? previous = state.DefinitionReplayScene;
        bool previousPicture = state.IsResourcePicture;
        int oldMaskCount = state.MaskScopes.Count;
        int oldViewportCount = state.Viewports.Count;
        try
        {
            state.IsResourcePicture |= tile;
            // Definition lowering reads document-owned paints before adoption
            // rebinds them into the tile arena. Rendered replay emits there directly.
            if (tile && pending.Kind == SvgDeferredPaintKind.Rendered) state.DefinitionReplayScene = pending.Scene;
            if (pending.Frame is {} frame) state.Viewports.Add(frame);
            if (tile) SvgDefinitionParser.RejectPatternContentPaint(pending.Context.ResolveStyle(pending.Source), state);
            for (int scope = 0; scope < pending.MaskScopeCount; scope++)
                state.MaskScopes.Add(state.MaskScopeSnapshots[pending.MaskScopeStart + scope]);
            if (pending.Kind == SvgDeferredPaintKind.PatternDefinition)
                SvgPaintRecording.AdoptLeafScene(SvgDefinitionParser.LowerInstanceSource(pending.Source, pending.Context, state), pending.Scene, state, work, token);
            else
                SvgRenderedLeafParser.ParseResolved(pending.Source, pending.Context, root, state, limits,
                    state.Budget, null, work, token);
        }
        finally
        {
            while (state.MaskScopes.Count > oldMaskCount) state.MaskScopes.RemoveLast();
            while (state.Viewports.Count > oldViewportCount) state.Viewports.RemoveLast();
            state.DefinitionReplayScene = previous;
            state.IsResourcePicture = previousPicture;
        }
    }
}
