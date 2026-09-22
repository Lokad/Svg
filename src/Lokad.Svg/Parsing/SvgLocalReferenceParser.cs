using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Resolves policy-validated local-use presentation after bounded reference syntax is parsed.</summary>
internal static class SvgLocalReferenceParser
{
    internal static SvgUseReference ParseUse(XmlReader reader, SvgPaintableElementContext context, BudgetedParserStringStore strings)
    {
        SvgLocalReferenceSyntax syntax = SvgLocalReferenceSyntax.Parse(reader, strings);
        SvgResolvedLocalReferencePresentation presentation = SvgLocalReferencePresentationResolver.Resolve(context.ResolveStyle(reader), context.InheritedPresentation, context.NamedColors);
        return new SvgUseReference(syntax.Id, syntax.X, syntax.Y, context.ResolveTransform(reader), presentation.Opacity, presentation.IsPainted, presentation.FillOverride);
    }
}
