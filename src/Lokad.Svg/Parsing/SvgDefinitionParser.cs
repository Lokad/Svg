using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Routes bounded definitions, symbols and local instances while preserving streaming order.</summary>
internal static class SvgDefinitionParser
{
    internal static bool TryParse(XmlReader reader, bool directChild, SvgRootAttributes root, SvgDocumentState state, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (state.DefinitionsDepth >= 0 && (reader.Depth == state.DefinitionsDepth + 1 || reader.Depth == state.SymbolDepth + 1) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "rect")
        {
            string? id = state.SymbolDepth >= 0 ? state.SymbolId : reader.GetAttribute("id");
            SvgRectangle rectangle = SvgLeafParser.ParseRectangle(reader, root.Paint, root.IsDisplayed, root.IsVisible, AxisTransform.Identity, SvgDocumentStyleResolver.Resolve(reader, directChild, state, workCounter, cancellationToken), namedColors, workCounter, cancellationToken);
            if (id is not null)
            {
                workCounter.Consume(cancellationToken);
                if (!state.Definitions.TryAdd(id, rectangle)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
            }
            state.HasUnsupportedContent |= !reader.IsEmptyElement;
            state.SawStyleableNode = true;
            return true;
        }
        if (state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "symbol")
        {
            SvgElementAttributePolicy.ValidateSymbol(reader);
            if (SvgDocumentStyleResolver.Resolve(reader, directChild, state, workCounter, cancellationToken) != default) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet rules on symbol definitions are not implemented.");
            state.SymbolId = reader.GetAttribute("id") ?? throw new SvgException(SvgErrorCode.MalformedInput, "A symbol requires an id.");
            if (!reader.IsEmptyElement) state.SymbolDepth = reader.Depth;
            return true;
        }
        if (state.DefinitionsDepth < 0 && directChild && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "defs")
        {
            SvgElementAttributePolicy.ValidateDefinitionContainer(reader);
            if (!reader.IsEmptyElement) state.DefinitionsDepth = reader.Depth;
            return true;
        }
        if (state.DefinitionsDepth < 0 && directChild && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "use")
        {
            SvgUseReference reference = SvgLeafParser.ParseUse(reader, SvgDocumentStyleResolver.Resolve(reader, directChild, state, workCounter, cancellationToken), workCounter, cancellationToken);
            workCounter.Consume(cancellationToken);
            if (state.Definitions.TryGetValue(reference.Id, out SvgRectangle definition)) state.Scene.AddRectangle(SvgReferenceResolver.Apply(definition, reference));
            else state.PendingUses.Add(new PendingUse(state.Scene.AddReferencePlaceholder(), reference));
            state.HasUnsupportedContent |= !reader.IsEmptyElement;
            state.SawStyleableNode = true;
            return true;
        }
        return false;
    }
}
