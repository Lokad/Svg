using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Collects static embedded SVG stylesheets in source order before discovery or lowering.</summary>
internal static class SvgRootStyleSheetParser
{
    /// <summary>Scans bounded XML without a DOM; the caller owns the rules and copied declaration strings.</summary>
    internal static void Collect(ReadOnlyMemory<byte> svg, XmlReaderSettings settings, SvgRenderLimits limits, BudgetedParserList<CssRule> rules, BudgetedParserStringStore strings, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // XML names are literal. ASCII-compatible inputs without either spelling
        // need no second reader; wide encodings fall through to ordinary XML parsing.
        ReadOnlySpan<byte> bytes = svg.Span;
        if (!bytes.Contains((byte)0) && bytes.IndexOf("<style"u8) < 0 && bytes.IndexOf(":style"u8) < 0) return;
        using ReadOnlyMemoryStream stream = new(svg);
        using XmlReader reader = XmlReader.Create(stream, settings);
        int rootDepth = -1, descriptionDepth = -1, nodes = 0;
        while (reader.Read())
        {
            workCounter.Consume(cancellationToken);
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.Depth == descriptionDepth) descriptionDepth = -1;
                continue;
            }
            if (reader.NodeType != XmlNodeType.Element) continue;
            SvgStructuralAdmission.ValidateElementStructure(reader, limits);
            if (nodes++ >= limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
            if (rootDepth < 0)
            {
                SvgStructuralAdmission.ThrowIfNotSvgRoot(reader);
                rootDepth = reader.Depth;
                continue;
            }
            if (reader.NamespaceURI != "http://www.w3.org/2000/svg" || descriptionDepth >= 0 && reader.Depth > descriptionDepth) continue;
            if (SvgStructuralVocabulary.IsDescriptiveElement(reader.LocalName))
            {
                if (!reader.IsEmptyElement) descriptionDepth = reader.Depth;
                continue;
            }
            if (reader.LocalName != "style") continue;
            ThrowIfAttributed(reader);
            using OwnedStyleSheetText text = SvgStyleSheet.ReadElementText(reader, budget, workCounter, cancellationToken);
            SvgStyleSheet.Parse(text.Value, limits.MaximumNodes, rules, strings, budget, workCounter, cancellationToken);
        }
    }

    /// <summary>Resolves root selectors through the ordinary bounded cascade, without a parent selector context.</summary>
    /// <remarks>Declarations borrow the caller's stylesheet store until parsing completes. Matching consumes shared work and observes cancellation; the immutable result owns no storage.</remarks>
    internal static InlineStyle ResolveRoot(string? id, string? classes, BudgetedParserList<CssRule> rules, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgStyleCascade cascade = new();
        for (int index = 0; index < rules.Count; index++)
        {
            workCounter.Consume(cancellationToken);
            CssRule rule = rules[index];
            if (rule.MatchesRoot(id, classes, workCounter, cancellationToken)) cascade.Apply(rule.Style, rule.Specificity);
        }
        return cascade.ToInlineStyle();
    }

    /// <summary>Allows XML namespace declarations and the inert stylesheet type and identifier attributes.</summary>
    internal static void ThrowIfAttributed(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (!reader.HasAttributes) return;
        while (reader.MoveToNextAttribute())
        {
            if (reader.NamespaceURI == "http://www.w3.org/2000/xmlns/") continue;
            bool allowed = (reader.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase) && reader.Value.Equals("text/css", StringComparison.OrdinalIgnoreCase))
                || reader.LocalName.Equals("id", StringComparison.OrdinalIgnoreCase);
            if (!allowed) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet attributes are not implemented.");
        }
        reader.MoveToElement();
    }

    /// <summary>Consumes an already collected stylesheet with bounded scratch, without retaining its text.</summary>
    internal static void Consume(XmlReader reader, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfAttributed(reader);
        if (reader.IsEmptyElement) return;
        using XmlReader subtree = reader.ReadSubtree();
        if (!subtree.Read()) throw new XmlException("Stylesheet content is missing.");
        RenderBudget.BudgetReservation? owner = null;
        char[] chunk = [];
        try
        {
            while (subtree.Read())
            {
                workCounter.Consume(cancellationToken);
                switch (subtree.NodeType)
                {
                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                        if (chunk.Length == 0)
                        {
                            owner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<char>(4096), SvgMemoryCategory.Parser);
                            chunk = new char[4096];
                        }
                        while (true)
                        {
                            workCounter.Consume(cancellationToken);
                            if (subtree.ReadValueChunk(chunk, 0, chunk.Length) == 0) break;
                        }
                        break;
                    case XmlNodeType.Element:
                        throw new XmlException("Stylesheet content must not contain child elements.");
                    case XmlNodeType.EndElement:
                        return;
                }
            }
            throw new XmlException("Stylesheet content is not closed.");
        }
        finally { owner?.Dispose(); }
    }
}
