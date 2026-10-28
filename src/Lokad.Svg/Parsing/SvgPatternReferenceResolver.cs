using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Resolves local pattern templates iteratively with charged graph storage and work.</summary>
/// <remarks>Single-conversion state; content aliases borrow a parent-owned scene. Cleanup ignores
/// cancellation; all new scenes are adopted once or disposed on failure. Missing forward targets
/// remain pending at a defs boundary and have an explicit unsupported outcome at document end.
/// Waiting links and an iterative ready stack visit each dependency once across definition blocks.</remarks>
internal static class SvgPatternReferenceResolver
{
    /// <summary>Queues a ready alias or links it to its unresolved target; concrete definitions wake their waiters.</summary>
    /// <remarks>Document-owned lists keep stable indices; dictionary keys borrow retained template IDs.
    /// Storage is reserved before growth, and registration charges work and observes cancellation.</remarks>
    internal static void Register(SvgDocumentState state, string id, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        if (!state.PatternDefinitions.TryGetValue(id, out SvgPatternDefinition definition)) throw new InvalidOperationException("Pattern registration requires retained metadata.");
        if (definition.Template.Href is not {} target)
        {
            WakeDependents(state, id, work, token);
            return;
        }
        int index = state.PendingPatternReferences.Count;
        bool ready = state.Patterns.TryGetValue(target, out _);
        bool waiting = state.WaitingPatternReferences.TryGetValue(target, out int first);
        state.PendingPatternReferences.Add(new(id, !ready && waiting ? first : -1));
        state.UnresolvedPatternReferences++;
        if (ready) state.ReadyPatternReferences.Add(index);
        else if (waiting) state.WaitingPatternReferences.Replace(target, index);
        else if (!state.WaitingPatternReferences.TryAdd(target, index)) throw new InvalidOperationException("Pattern waiting lists register once.");
    }

    internal static void Resolve(SvgDocumentState state, bool requireComplete, ParserWorkCounter work, CancellationToken token)
    {
        if (!state.HasPatternTemplateReferences) return;
        while (state.ReadyPatternReferences.Count != 0)
        {
            work.Consume(token);
            int index = state.ReadyPatternReferences[^1];
            state.ReadyPatternReferences.RemoveLast();
            string id = state.PendingPatternReferences[index].Id;
            if (!state.PatternDefinitions.TryGetValue(id, out SvgPatternDefinition definition)) throw new InvalidOperationException("Ready patterns retain their template metadata.");
            SvgPatternTemplate template = definition.Template;
            SvgPatternTile tile = definition.Tile;
            string target = template.Href ?? throw new InvalidOperationException("A ready pattern must be an unresolved alias.");
            if (!state.Patterns.TryGetValue(target, out SvgPatternTile parent) || !state.PatternDefinitions.TryGetValue(target, out SvgPatternDefinition source))
                throw new InvalidOperationException("A ready pattern requires a resolved target.");
            template = template.Inherit(source.Template);
            SvgPatternTile grid = template.Grid;
            if (!template.IsDisplayed || !template.HasChildElements && !source.Template.IsDisplayed) tile = grid;
            else if (!template.HasChildElements)
            {
                if (parent.VectorContent is null && source.Tile.VectorContent is not null) parent = source.Tile;
                if (parent.VectorContent is null && source.RectangleSource is {} rectangle)
                {
                    SvgVectorContent content = PromoteRectangle(rectangle, source.RectangleContext, state, work, token);
                    parent = parent with { Content = null, VectorContent = content };
                    state.PatternDefinitions.Replace(target, source with { Tile = parent });
                }
                tile = grid with { VectorContent = parent.VectorContent };
            }
            else tile = grid with { Content = tile.Content, VectorContent = tile.VectorContent };
            state.PatternDefinitions.Replace(id, definition with { Template = template, Tile = tile });
            if (!state.Patterns.TryAdd(id, tile)) throw new InvalidOperationException("Resolved patterns register once.");
            state.UnresolvedPatternReferences--;
            WakeDependents(state, id, work, token);
        }
        if (!requireComplete || !state.HasPatternTemplateReferences) return;

        // An unresolved component cannot reach a concrete target after the ready stack drains.
        // Follow its single outgoing edge iteratively to retain missing-target/cycle diagnostics.
        foreach (SvgPendingPatternReference root in state.PendingPatternReferences.AsSpan())
        {
            work.Consume(token);
            if (state.Patterns.TryGetValue(root.Id, out _)) continue;
            string id = root.Id;
            while (true)
            {
                work.Consume(token);
                if (!state.PatternDefinitions.TryGetValue(id, out SvgPatternDefinition definition))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "A local pattern template target is not defined.");
                if (definition.IsVisiting) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Cyclic pattern templates are not implemented.");
                state.PatternDefinitions.Replace(id, definition with { IsVisiting = true });
                id = definition.Template.Href ?? throw new InvalidOperationException("Unresolved patterns must retain their target.");
            }
        }
    }

    private static void WakeDependents(SvgDocumentState state, string target, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        if (!state.WaitingPatternReferences.TryGetValue(target, out int next) || next < 0) return;
        state.WaitingPatternReferences.Replace(target, -1);
        while (next >= 0)
        {
            work.Consume(token);
            state.ReadyPatternReferences.Add(next);
            next = state.PendingPatternReferences[next].Next;
        }
    }

    /// <summary>Borrows native tile replay before rectangle content is copied into referencing geometry.</summary>
    /// <remarks>The definition caches a parent-owned scene using the existing template promotion route.
    /// Creation charges work/storage and observes cancellation; aliases borrow the same owner.
    /// Callers preserve their target-map range checks separately; promotion is independent of placement.
    /// Fills and strokes retain source replay for later instance/output placement. Failed promotion disposes every new owner.</remarks>
    internal static SvgPatternTile ForReplay(string id, SvgPatternTile tile,
        SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        if (tile.VectorContent is not null || tile.Content is null) return tile;
        work.Consume(token);
        if (!state.PatternDefinitions.TryGetValue(id, out SvgPatternDefinition definition)) throw new InvalidOperationException("Pattern replay requires retained definition content.");
        SvgVectorContent content;
        if (definition.Tile.VectorContent is {} retained) content = retained;
        else
        {
            SvgDefinitionLeafSource source = definition.RectangleSource ?? throw new InvalidOperationException("Pattern promotion requires retained rectangle attributes.");
            content = PromoteRectangle(source, definition.RectangleContext, state, work, token);
            state.PatternDefinitions.Replace(id, definition with { Tile = definition.Tile with { Content = null, VectorContent = content } });
        }
        return tile with { Content = null, VectorContent = content };
    }

    private static SvgVectorContent PromoteRectangle(SvgDefinitionLeafSource source, SvgPaintableElementContext context,
        SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        using SvgSceneBuilder builder = new(state.Budget, work, token);
        bool previousPicture = state.IsResourcePicture;
        SvgDefinition lowered;
        state.IsResourcePicture = true;
        try
        {
            lowered = source.Lower(context.InheritedStroke, context.InheritedPresentation.Paint,
                context.InheritedFillRule, context.InheritedPresentation.PaintServerFill, context.InheritedPresentation.PaintOrder, context.InheritedPresentation.Markers, context.InheritedPresentation.ElementOpacity, context.InheritedPresentation.ElementDisplay, context.InheritedPresentation.TextMetrics, context.InheritedPresentation.NonScalingStroke, SvgAffineTransform.Identity, context.LengthBases, SvgSourceContourRetention.None, context.InheritedPresentation.IsDisplayed, state);
        }
        finally { state.IsResourcePicture = previousPicture; }
        SvgPaintRecording.AdoptLeafScene(lowered, builder, state, work, token);
        SvgLeafMaskComposition.Emit(state, builder, work, token);
        SvgVectorContent content = SvgVectorContent.Create(builder.Build(), state.Budget);
        try { state.Scene.AdoptVectorContent(content); }
        catch { content.Dispose(); throw; }
        return content;
    }
}
