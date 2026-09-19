using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Traverses one bounded SVG document after reader setup without materializing a DOM.</summary>
internal static class SvgDocumentTraversal
{
    internal static SvgRoot Parse(XmlReader reader, SvgRenderLimits limits, RenderBudget budget, IReadOnlyDictionary<string, SvgColor> namedColors, CancellationToken cancellationToken)
    {
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element) continue;
            ValidateElementStructure(reader, limits);
            if (reader.LocalName != "svg" || reader.NamespaceURI != "http://www.w3.org/2000/svg") throw new SvgException(SvgErrorCode.MalformedInput, "The document root must be an SVG element in the SVG namespace.");
            ParserWorkCounter workCounter = new(limits.MaximumWorkUnits);
            SvgRootAttributes root = SvgRootAttributes.Parse(reader, namedColors, workCounter, cancellationToken);
            return TraverseRoot(reader, limits, budget, namedColors, cancellationToken, workCounter, root);
        }
        throw new SvgException(SvgErrorCode.MalformedInput, "SVG input has no root element.");
    }

    private static SvgRoot TraverseRoot(XmlReader reader, SvgRenderLimits limits, RenderBudget budget, IReadOnlyDictionary<string, SvgColor> namedColors, CancellationToken cancellationToken, ParserWorkCounter workCounter, SvgRootAttributes root)
    {
        if (reader.IsEmptyElement) return CompleteRoot(reader, cancellationToken, new SvgRoot(root.Width, root.Height, root.ViewBox, [], [], false, 0, null));
        int rootDepth = reader.Depth;
        using SvgDocumentState state = new(budget, workCounter, cancellationToken);
        if (root.Opacity != 255) state.Scene.BeginOpacityScope(root.Opacity);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element)
            {
                ValidateElementStructure(reader, limits);
                if (state.NodeCount >= limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                state.NodeCount++;
                bool directChild = reader.Depth == rootDepth + 1;
                bool nestedInGroup = state.Groups.Count > 0;
                if (SvgDefinitionParser.TryParse(reader, directChild, root, state, namedColors, workCounter, cancellationToken)) { }
                else if (state.SymbolDepth >= 0 && reader.Depth > state.SymbolDepth) state.HasUnsupportedContent = true;
                else if (SvgRenderedLeafParser.TryParse(reader, directChild, directChild || nestedInGroup, root, state, limits, budget, namedColors, workCounter, cancellationToken))
                {
                    state.HasUnsupportedContent |= !reader.IsEmptyElement;
                    state.SawStyleableNode = true;
                }
                else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && (reader.LocalName is "g" or "a"))
                {
                    SvgParsedGroup parsedGroup = SvgGroupParser.Parse(reader, directChild, root, state, namedColors, workCounter, cancellationToken);
                    if (parsedGroup.Scope.IsolatesOpacity && (root.Opacity != 255 || state.Groups.Any(static current => current.IsolatesOpacity))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Nested group opacity requires separate compositing calibration.");
                    if (!reader.IsEmptyElement) { if (parsedGroup.Scope.IsolatesOpacity) state.Scene.BeginOpacityScope(parsedGroup.Opacity); state.Groups.Add(parsedGroup.Scope); }
                }
                else if (directChild && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "style")
                {
                    SvgRootStyleSheetParser.Parse(reader, root, state, limits.MaximumNodes, budget, workCounter, cancellationToken);
                }
                else if (reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName is "style" or "script" or "animate" or "animateMotion" or "animateTransform" or "set") state.HasUnsupportedContent = true;
                else if (directChild && (reader.LocalName is "title" or "desc" or "metadata" or "defs")) { }
                else if (directChild || nestedInGroup) state.HasUnsupportedContent = true;
            }
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.Depth == state.DefinitionsDepth) state.DefinitionsDepth = -1;
                if (reader.Depth == state.SymbolDepth) { state.SymbolDepth = -1; state.SymbolId = null; }
                if (state.Groups.Count > 0 && state.Groups[^1].Depth == reader.Depth) { SvgGroupScope group = state.Groups[^1]; state.Groups.RemoveLast(); if (group.IsolatesOpacity) state.Scene.EndOpacityScope(); }
                if (reader.Depth == rootDepth)
                {
                    SvgReferenceResolver.ResolvePendingUses(state.Definitions, state.PendingUses, state.Scene, workCounter, cancellationToken);
                    if (root.Opacity != 255) state.Scene.EndOpacityScope();
                    SvgSceneStorage sceneStorage = state.Scene.Build();
                    try { return CompleteRoot(reader, cancellationToken, new SvgRoot(root.Width, root.Height, root.ViewBox, sceneStorage.Geometries, sceneStorage.Commands, state.HasUnsupportedContent, workCounter.Used, sceneStorage)); }
                    catch { sceneStorage.Dispose(); throw; }
                }
            }
        }
        throw new SvgException(SvgErrorCode.MalformedInput, "SVG root element is not closed.");
    }

    private static SvgRoot CompleteRoot(XmlReader reader, CancellationToken cancellationToken, SvgRoot root)
    {
        try
        {
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace) continue;
                throw new SvgException(SvgErrorCode.MalformedInput, "SVG input has content after its root element.");
            }
            return root;
        }
        catch { root.Dispose(); throw; }
    }

    private static void ValidateElementStructure(XmlReader reader, SvgRenderLimits limits)
    {
        if (reader.Depth >= limits.MaximumXmlDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG XML nesting exceeds the configured limit.");
        if (reader.AttributeCount > limits.MaximumAttributesPerElement) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG element attributes exceed the configured limit.");
    }
}
