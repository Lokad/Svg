using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Routes bounded definitions, symbols and local instances while preserving streaming order.</summary>
internal static class SvgDefinitionParser
{
    internal static bool TryParse(XmlReader reader, bool directChild, bool isEligible, SvgRootAttributes root, SvgDocumentState state, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (state.DefinitionsDepth >= 0 && (reader.Depth == state.DefinitionsDepth + 1 || reader.Depth == state.SymbolDepth + 1) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName))
        {
            string? id = state.SymbolDepth >= 0 ? state.SymbolId : reader.GetAttribute("id");
            if (reader.LocalName == "rect") SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken);
            else SvgElementAttributePolicy.ValidateLine(reader, workCounter, cancellationToken);
            SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, directChild, root, state, namedColors, workCounter, cancellationToken);
            SvgViewBox? symbolViewBox = state.SymbolDepth >= 0 ? state.SymbolViewBox : null;
            SvgDefinition definition = reader.LocalName == "rect" ? ParseRectangleDefinition(reader, context, symbolViewBox) : new SvgDefinition(SvgLeafParser.ParseLine(reader, context, null), false, symbolViewBox);
            if (id is not null)
            {
                workCounter.Consume(cancellationToken);
                if (state.Definitions.TryGetValue(id, out _)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
                string retainedId = state.Strings.Copy(id);
                if (!state.Definitions.TryAdd(retainedId, definition)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
            }
            state.HasUnsupportedContent |= !reader.IsEmptyElement;
            state.SawStyleableNode = true;
            return true;
        }
        if (state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "symbol")
        {
            SvgElementAttributePolicy.ValidateSymbol(reader, workCounter, cancellationToken);
            if (SvgDocumentStyleResolver.Resolve(reader, directChild, state, workCounter, cancellationToken) != default) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet rules on symbol definitions are not implemented.");
            state.SymbolId = reader.GetAttribute("id") ?? throw new SvgException(SvgErrorCode.MalformedInput, "A symbol requires an id.");
            state.SymbolViewBox = SvgGeometrySyntax.ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio"));
            if (!reader.IsEmptyElement) state.SymbolDepth = reader.Depth;
            return true;
        }
        if (state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "clipPath")
        {
            SvgElementAttributePolicy.ValidateClipPath(reader, workCounter, cancellationToken);
            state.ClipPathId = reader.GetAttribute("id");
            state.ClipBox = null;
            if (reader.IsEmptyElement)
            {
                if (state.ClipPathId is not null) RegisterClipBox(state, workCounter, cancellationToken);
            }
            else state.ClipPathDepth = reader.Depth;
            state.SawStyleableNode = true;
            return true;
        }
        if (state.ClipPathDepth >= 0 && reader.Depth == state.ClipPathDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg")
        {
            if (reader.LocalName == "rect")
            {
                if (state.ClipBox is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-rectangle clip paths are implemented.");
                SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken);
                SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, false, root, state, namedColors, workCounter, cancellationToken);
                SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
                SvgRectangle rect = context.ResolveTransform(reader).Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, false, null));
                state.ClipBox = new SvgClipBox(rect.X, rect.Y, rect.Width, rect.Height);
                state.HasUnsupportedContent |= !reader.IsEmptyElement;
                state.SawStyleableNode = true;
                return true;
            }
            state.HasUnsupportedContent = true;
            return true;
        }
        if (state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "mask")
        {
            SvgElementAttributePolicy.ValidateMask(reader, workCounter, cancellationToken);
            state.MaskId = reader.GetAttribute("id");
            state.MaskIsAlpha = SvgMaskSyntax.ParseMaskType(reader.GetAttribute("mask-type"));
            state.MaskValue = null;
            if (reader.IsEmptyElement)
            {
                if (state.MaskId is not null) RegisterMask(state, workCounter, cancellationToken);
            }
            else state.MaskDepth = reader.Depth;
            state.SawStyleableNode = true;
            return true;
        }
        if (state.MaskDepth >= 0 && reader.Depth == state.MaskDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg")
        {
            if (reader.LocalName == "rect")
            {
                if (state.MaskValue is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-rectangle masks are implemented.");
                SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken);
                SvgRectangleSyntax.Parse(reader);
                SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, false, root, state, namedColors, workCounter, cancellationToken);
                SvgResolvedPaintableElement element = context.ResolveShape(reader, false);
                if (!element.Presentation.IsPainted) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Invisible mask content is not implemented.");
                state.MaskValue = SvgMaskSyntax.InterpretValue(element.Presentation.Color, state.MaskIsAlpha);
                state.HasUnsupportedContent |= !reader.IsEmptyElement;
                state.SawStyleableNode = true;
                return true;
            }
            state.HasUnsupportedContent = true;
            return true;
        }
        if (state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "pattern")
        {
            SvgElementAttributePolicy.ValidatePattern(reader, workCounter, cancellationToken);
            state.PatternId = reader.GetAttribute("id");
            state.PatternTile = SvgPatternSyntax.ParsePatternElement(reader);
            if (!reader.IsEmptyElement) state.PatternDepth = reader.Depth;
            else if (state.PatternId is not null) RegisterPattern(state, workCounter, cancellationToken);
            state.SawStyleableNode = true;
            return true;
        }
        if (state.PatternDepth >= 0 && reader.Depth == state.PatternDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg")
        {
            if (reader.LocalName == "rect")
            {
                if (state.PatternTile?.Content is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-rectangle pattern tiles are implemented.");
                SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken);
                SvgPaintableElementContext tileContext = SvgPaintableElementContext.Create(reader, false, root, state, namedColors, workCounter, cancellationToken);
                SvgGeometry content = SvgLeafParser.ParseRectangle(reader, tileContext, null, state);
                SvgPatternTile tile = state.PatternTile ?? throw new InvalidOperationException("Pattern tile accumulation requires pattern dimensions.");
                state.PatternTile = tile with { Content = RequirePatternTileContent(content, tile) };
                state.HasUnsupportedContent |= !reader.IsEmptyElement;
                state.SawStyleableNode = true;
                return true;
            }
            state.HasUnsupportedContent = true;
            return true;
        }
        if (state.DefinitionsDepth < 0 && directChild && reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsDefinitionsContainer(reader.LocalName))
        {
            SvgElementAttributePolicy.ValidateDefinitionContainer(reader);
            if (!reader.IsEmptyElement) state.DefinitionsDepth = reader.Depth;
            return true;
        }
        if (state.DefinitionsDepth < 0 && (directChild || isEligible) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "use")
        {
            SvgElementAttributePolicy.ValidateUse(reader, workCounter, cancellationToken);
            SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, directChild, root, state, namedColors, workCounter, cancellationToken);
            SvgViewportFrame? parentFrame = state.EffectiveFrame;
            double? baseWidth = parentFrame?.ViewportWidth ?? root.Width;
            double? baseHeight = parentFrame?.ViewportHeight ?? root.Height;
            SvgUseReference reference = SvgLocalReferenceParser.ParseUse(reader, context, state.Strings, baseWidth, baseHeight);
            SvgReferenceResolver.ResolveOrDefer(reference, state.Definitions, state.PendingUses, state.Scene, SvgClipPathSyntax.ResolveLeafClip(reader, state), SvgMaskSyntax.ResolveUseMask(reader, state), workCounter, cancellationToken);
            state.HasUnsupportedContent |= !reader.IsEmptyElement;
            state.SawStyleableNode = true;
            return true;
        }
        return false;
    }

    internal static void RegisterPattern(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        string retainedId = state.Strings.Copy(state.PatternId ?? throw new InvalidOperationException("Pattern registration requires an identifier."));
        SvgPatternTile tile = state.PatternTile ?? throw new InvalidOperationException("Pattern registration requires pattern dimensions.");
        if (!state.Patterns.TryAdd(retainedId, tile)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG pattern ids must be unique.");
    }

    internal static void RegisterMask(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        string retainedId = state.Strings.Copy(state.MaskId ?? throw new InvalidOperationException("Mask registration requires an identifier."));
        if (!state.Masks.TryAdd(retainedId, state.MaskValue ?? 0)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
    }

    internal static void RegisterClipBox(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        string retainedId = state.Strings.Copy(state.ClipPathId ?? throw new InvalidOperationException("Clip-path registration requires an identifier."));
        if (!state.Clips.TryAdd(retainedId, state.ClipBox ?? new SvgClipBox(0, 0, 0, 0))) throw new SvgException(SvgErrorCode.MalformedInput, "SVG clip-path ids must be unique.");
    }

    private static SvgGeometry RequirePatternTileContent(SvgGeometry content, SvgPatternTile tile)
    {
        if (content.Kind != SvgGeometryKind.Rectangle) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only rectangle pattern tile content is implemented.");
        SvgRectangle box = content.Rectangle;
        if (!box.HasFill) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Unpainted pattern tile content is not implemented.");
        if (box.X != Math.Truncate(box.X) || box.Y != Math.Truncate(box.Y) || box.Width != Math.Truncate(box.Width) || box.Height != Math.Truncate(box.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer pattern tile content is implemented.");
        if (box.X < tile.OriginX || box.Y < tile.OriginY || box.X + box.Width > tile.OriginX + tile.TileWidth || box.Y + box.Height > tile.OriginY + tile.TileHeight) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern tile content overflowing its tile is not implemented.");
        return content;
    }

    private static SvgDefinition ParseRectangleDefinition(XmlReader reader, SvgPaintableElementContext context, SvgViewBox? symbolViewBox)
    {
        SvgParsedRectangle parsed = SvgLeafParser.ParseRectangleWithStyle(reader, context);
        return new SvgDefinition(parsed.Geometry, parsed.Geometry.Rectangle.HasFill && parsed.Style == default, symbolViewBox);
    }
}
