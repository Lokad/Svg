using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Resolves local pattern templates iteratively with charged graph storage and work.</summary>
/// <remarks>Single-conversion state; content aliases borrow a parent-owned scene. Cleanup ignores
/// cancellation; all new scenes are adopted once or disposed on failure. Missing forward targets
/// remain pending at a defs boundary and have an explicit unsupported outcome at document end.</remarks>
internal static class SvgPatternReferenceResolver
{
    internal static void Resolve(SvgDocumentState state, bool requireComplete, ParserWorkCounter work, CancellationToken token)
    {
        if (!state.HasPatternTemplateReferences) return;
        using BudgetedParserList<SvgPatternReferenceFrame> roots = new(state.Budget, SvgMemoryCategory.Parser, work, token);
        foreach (SvgPatternDefinition definition in state.PatternDefinitions.Values)
        {
            work.Consume(token);
            if (!state.Patterns.TryGetValue(definition.Id, out _)) roots.Add(new(definition.Id));
        }
        using BudgetedParserList<SvgPatternReferenceFrame> path = new(state.Budget, SvgMemoryCategory.Parser, work, token);
        foreach (SvgPatternReferenceFrame root in roots.AsSpan())
        {
            path.Add(root);
            while (path.Count != 0)
            {
                work.Consume(token);
                string id = path[^1].Id;
                if (state.Patterns.TryGetValue(id, out _)) { path.RemoveLast(); continue; }
                if (!state.PatternDefinitions.TryGetValue(id, out SvgPatternDefinition definition))
                {
                    if (requireComplete) throw new SvgException(SvgErrorCode.UnsupportedFeature, "A local pattern template target is not defined.");
                    foreach (SvgPatternReferenceFrame frame in path.AsSpan())
                    {
                        work.Consume(token);
                        if (state.PatternDefinitions.TryGetValue(frame.Id, out SvgPatternDefinition pending))
                            state.PatternDefinitions.Replace(frame.Id, pending with { IsVisiting = false });
                    }
                    path.Clear();
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
                    template.ValidateUnits();
                    SvgPatternTile grid = template.Grid;
                    if (!tile.HasContent)
                    {
                        if (parent.VectorContent is null && source.Tile.VectorContent is not null) parent = source.Tile;
                        if (parent.VectorContent is null && source.RectangleSource is {} rectangle)
                        {
                            SvgVectorContent content = PromoteRectangle(rectangle, source.RectangleContext);
                            parent = parent with { Content = null, VectorContent = content };
                            state.PatternDefinitions.Replace(target, source with { Tile = parent });
                        }
                        tile = grid with { VectorContent = parent.VectorContent };
                    }
                    else tile = grid with { Content = tile.Content, VectorContent = tile.VectorContent };
                }
                template.ValidateUnits();
                state.PatternDefinitions.Replace(id, definition with { Template = template, Tile = tile, IsVisiting = false });
                if (!state.Patterns.TryAdd(id, tile)) throw new InvalidOperationException("Resolved patterns register once.");
                path.RemoveLast();
            }
        }

        SvgVectorContent PromoteRectangle(SvgDefinitionLeafSource source, SvgPaintableElementContext context)
        {
            using SvgSceneBuilder builder = new(state.Budget, work, token);
            SvgDefinition lowered = source.Lower(context.InheritedStroke, context.InheritedPresentation.Paint,
                context.InheritedFillRule, context.InheritedPresentation.PaintServerFill, context.InheritedPresentation.PaintOrder, context.InheritedPresentation.Markers, context.InheritedPresentation.ElementOpacity, false, state);
            SvgPaintRecording.AdoptLeafScene(lowered, builder, state);
            SvgLeafMaskComposition.Emit(state, builder, work, token);
            SvgVectorContent content = SvgVectorContent.Create(builder.Build(), state.Budget);
            try { state.Scene.AdoptVectorContent(content); }
            catch { content.Dispose(); throw; }
            return content;
        }
    }
}
