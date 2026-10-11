using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Collects embedded root and definition stylesheets in source order before discovery or lowering.</summary>
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
        int rootDepth = -1, definitionsDepth = -1, descriptionDepth = -1, nodes = 0;
        while (reader.Read())
        {
            workCounter.Consume(cancellationToken);
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.Depth == definitionsDepth) definitionsDepth = -1;
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
            if (reader.LocalName == "defs" && definitionsDepth < 0 && !reader.IsEmptyElement) definitionsDepth = reader.Depth;
            if (reader.LocalName != "style" || reader.Depth != rootDepth + 1 && reader.Depth != definitionsDepth + 1) continue;
            ThrowIfAttributed(reader);
            using OwnedStyleSheetText text = SvgStyleSheet.ReadElementText(reader, budget, workCounter, cancellationToken);
            SvgStyleSheet.Parse(text.Value, limits.MaximumNodes, rules, strings, budget, workCounter, cancellationToken);
        }
    }

    internal static void ThrowIfTargetsRoot(string? id, string? classes, BudgetedParserList<CssRule> rules, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        for (int index = 0; index < rules.Count; index++)
        {
            workCounter.Consume(cancellationToken);
            if (rules[index].MatchesRoot(id, classes, workCounter, cancellationToken)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet rules targeting the SVG root are not implemented.");
        }
    }

    /// <summary>Rejects stylesheet element attributes except the inert type and identifier declarations exporters emit.</summary>
    internal static void ThrowIfAttributed(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (!reader.HasAttributes) return;
        while (reader.MoveToNextAttribute())
        {
            bool allowed = (reader.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase) && reader.Value.Equals("text/css", StringComparison.OrdinalIgnoreCase))
                || reader.LocalName.Equals("id", StringComparison.OrdinalIgnoreCase);
            if (!allowed) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet attributes are not implemented.");
        }
        reader.MoveToElement();
    }

    /// <summary>Consumes an already collected stylesheet during the ordinary structural traversal.</summary>
    internal static void Consume(XmlReader reader, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ThrowIfAttributed(reader);
        using OwnedStyleSheetText text = SvgStyleSheet.ReadElementText(reader, budget, workCounter, cancellationToken);
    }
}
