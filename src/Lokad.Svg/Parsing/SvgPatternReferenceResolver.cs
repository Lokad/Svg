using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Resolves local pattern templates iteratively with charged graph storage and work.</summary>
/// <remarks>Single-conversion state; content aliases borrow a parent-owned scene. Cleanup ignores
/// cancellation; all new scenes are adopted once or disposed on failure. Missing forward targets
/// remain pending at a defs boundary and have an explicit unsupported outcome at document end.
/// Each pass visits shared blocked chains once and retries only after new definitions arrive.</remarks>
internal static class SvgPatternReferenceResolver
{
    internal static void Resolve(SvgDocumentState state, bool requireComplete, ParserWorkCounter work, CancellationToken token)
    {
        if (!state.HasPatternTemplateReferences) return;
        int version = state.PatternDefinitionVersion;
        if (!requireComplete && state.PatternResolutionVersion == version) return;
        using BudgetedParserList<SvgPatternReferenceFrame> path = new(state.Budget, SvgMemoryCategory.Parser, work, token);
        int retained = 0;
        foreach (SvgPatternReferenceFrame root in state.PendingPatternReferences.AsSpan())
        {
            work.Consume(token);
            if (state.Patterns.TryGetValue(root.Id, out _)) continue;
            path.Add(root);
            while (path.Count != 0)
            {
                work.Consume(token);
                string id = path[^1].Id;
                if (state.Patterns.TryGetValue(id, out _)) { path.RemoveLast(); continue; }
                if (!state.PatternDefinitions.TryGetValue(id, out SvgPatternDefinition definition))
                {
                    if (requireComplete) throw new SvgException(SvgErrorCode.UnsupportedFeature, "A local pattern template target is not defined.");
                    BlockPath();
                    break;
                }
                if (!requireComplete && definition.BlockedVersion == version)
                {
                    BlockPath();
                    break;
                }
                SvgPatternTemplate template = definition.Template;
                SvgPatternTile tile = definition.Tile;
                if (template.Href is {} target)
                {
                    if (!state.Patterns.TryGetValue(target, out SvgPatternTile parent))
                    {
                        if (definition.IsVisiting) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Cyclic pattern templates are not implemented.");
                        state.PatternDefinitions.Replace(id, definition with { IsVisiting = true });
                        path.Add(new(target));
                        continue;
                    }
                    if (!state.PatternDefinitions.TryGetValue(target, out SvgPatternDefinition source)) throw new InvalidOperationException("Resolved patterns retain their template metadata.");
                    template = template.Inherit(source.Template);
                    SvgPatternTile grid = template.Grid;
                    if (!tile.HasContent)
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
                }
                state.PatternDefinitions.Replace(id, definition with { Template = template, Tile = tile, IsVisiting = false });
                if (!state.Patterns.TryAdd(id, tile)) throw new InvalidOperationException("Resolved patterns register once.");
                path.RemoveLast();
            }
            if (!state.Patterns.TryGetValue(root.Id, out _)) state.PendingPatternReferences[retained++] = root;
        }
        while (state.PendingPatternReferences.Count > retained)
        {
            work.Consume(token);
            state.PendingPatternReferences.RemoveLast();
        }
        state.PatternResolutionVersion = version;

        void BlockPath()
        {
            foreach (SvgPatternReferenceFrame frame in path.AsSpan())
            {
                work.Consume(token);
                if (state.PatternDefinitions.TryGetValue(frame.Id, out SvgPatternDefinition pending))
                    state.PatternDefinitions.Replace(frame.Id, pending with { IsVisiting = false, BlockedVersion = version });
            }
            path.Clear();
        }
    }

    /// <summary>Borrows native tile replay for affine placement and scaled strokes, preserving ordinary legacy tiles.</summary>
    /// <remarks>The definition caches a parent-owned scene using the existing template promotion route.
    /// Creation charges work/storage and observes cancellation; aliases borrow the same owner. Positive
    /// axis placement keeps established legacy fill pixels; strokes retain source replay for later scaling. Failed promotion disposes every new owner.</remarks>
    internal static SvgPatternTile ForPlacement(string id, SvgPatternTile tile, SvgAffineTransform devicePlacement,
        SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        if (tile.VectorContent is not null || tile.Content is null) return tile;
        // Stroked content retains native replay even before the output scale is known.
        if (devicePlacement.IsAxisAligned && devicePlacement.A >= 0 && devicePlacement.D >= 0
            && tile.Content.Value.Rectangle.Stroke is null) return tile;
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
        SvgDefinition lowered = source.Lower(context.InheritedStroke, context.InheritedPresentation.Paint,
            context.InheritedFillRule, context.InheritedPresentation.PaintServerFill, context.InheritedPresentation.PaintOrder, context.InheritedPresentation.Markers, context.InheritedPresentation.ElementOpacity, context.InheritedPresentation.NonScalingStroke, SvgAffineTransform.Identity, context.LengthBases, false, state);
        SvgPaintRecording.AdoptLeafScene(lowered, builder, state);
        SvgLeafMaskComposition.Emit(state, builder, work, token);
        SvgVectorContent content = SvgVectorContent.Create(builder.Build(), state.Budget);
        try { state.Scene.AdoptVectorContent(content); }
        catch { content.Dispose(); throw; }
        return content;
    }
}
