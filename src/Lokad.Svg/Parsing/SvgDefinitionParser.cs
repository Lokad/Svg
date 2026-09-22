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
            SvgDefinition definition = reader.LocalName == "rect" ? ParseRectangleDefinition(reader, context) : new SvgDefinition(SvgLeafParser.ParseLine(reader, context, null), false);
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
            if (!reader.IsEmptyElement) state.SymbolDepth = reader.Depth;
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
            SvgUseReference reference = SvgLocalReferenceParser.ParseUse(reader, context, state.Strings);
            SvgReferenceResolver.ResolveOrDefer(reference, state.Definitions, state.PendingUses, state.Scene, state.EffectiveClip, workCounter, cancellationToken);
            state.HasUnsupportedContent |= !reader.IsEmptyElement;
            state.SawStyleableNode = true;
            return true;
        }
        return false;
    }

    private static SvgDefinition ParseRectangleDefinition(XmlReader reader, SvgPaintableElementContext context)
    {
        SvgParsedRectangle parsed = SvgLeafParser.ParseRectangleWithStyle(reader, context);
        return new SvgDefinition(parsed.Geometry, parsed.Geometry.Rectangle.HasFill && parsed.Style == default);
    }
}
