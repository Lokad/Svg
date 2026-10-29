using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Shares structural admission gates between font-request discovery and document lowering.</summary>
internal static class SvgStructuralAdmission
{
    /// <summary>Rejects elements that violate the configured nesting or attribute limits.</summary>
    internal static void ValidateElementStructure(XmlReader reader, SvgRenderLimits limits)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(limits);
        if (reader.Depth >= limits.MaximumXmlDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG XML nesting exceeds the configured limit.");
        if (reader.AttributeCount > limits.MaximumAttributesPerElement) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG element attributes exceed the configured limit.");
    }

    /// <summary>Consumes a geometric leaf body, admitting inert descriptions while counting every nested element and checking cancellation during reading. Borrows the reader and allocates no retained storage.</summary>
    internal static SvgLeafSubtree ConsumeLeafSubtree(XmlReader reader, SvgRenderLimits limits, int nodeCount, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(limits);
        int leafDepth = reader.Depth;
        int descriptionDepth = -1;
        bool hasElements = false;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element)
            {
                ValidateElementStructure(reader, limits);
                if (nodeCount >= limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                nodeCount++;
                bool svg = reader.NamespaceURI == "http://www.w3.org/2000/svg";
                if (descriptionDepth >= 0)
                    hasElements |= svg && SvgStructuralVocabulary.IsStyleScriptOrAnimationElement(reader.LocalName);
                else if (svg && SvgStructuralVocabulary.IsDescriptiveElement(reader.LocalName))
                {
                    if (!reader.IsEmptyElement) descriptionDepth = reader.Depth;
                }
                else hasElements = true;
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == leafDepth) return new(hasElements, nodeCount);
            else if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == descriptionDepth) descriptionDepth = -1;
        }
        return new(hasElements, nodeCount);
    }

    /// <summary>Consumes a live document leaf after its attributes are retained, updating structural counts and unsupported-child state.</summary>
    /// <remarks>Self-closing retained readers need no traversal. Live readers borrow the document's limits;
    /// the shared consumer meters every child and cancellation without allocating retained storage.</remarks>
    internal static void ConsumeLeafChildren(XmlReader reader, SvgDocumentState state, CancellationToken token)
    {
        if (reader.IsEmptyElement) return;
        SvgRenderLimits limits = state.TextReplay?.Limits ?? throw new InvalidOperationException("Leaf traversal requires document limits.");
        SvgLeafSubtree subtree = ConsumeLeafSubtree(reader, limits, state.NodeCount, token);
        state.NodeCount = subtree.NodeCount;
        state.HasUnsupportedContent |= subtree.HasUnsupportedChildren;
    }

    /// <summary>Rejects a document whose root is not an SVG element in the SVG namespace.</summary>
    internal static void ThrowIfNotSvgRoot(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.LocalName != "svg" || reader.NamespaceURI != "http://www.w3.org/2000/svg") throw new SvgException(SvgErrorCode.MalformedInput, "The document root must be an SVG element in the SVG namespace.");
    }
}

/// <summary>Reports unsupported children, excluding inert descriptions, and the resulting document node count.</summary>
internal readonly record struct SvgLeafSubtree(bool HasUnsupportedChildren, int NodeCount);
