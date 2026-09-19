using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Resolves policy-validated local-use presentation after bounded reference syntax is parsed.</summary>
internal static class SvgLocalReferenceParser
{
    internal static SvgUseReference ParseUse(XmlReader reader, SvgPaintableElementContext context, BudgetedParserStringStore strings)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        if (style.Color is not null || style.FillOpacity is not null || style.Stroke is not null || style.FillRule is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only inherited-rectangle fill styles on use elements are implemented.");
        SvgLocalReferenceSyntax syntax = SvgLocalReferenceSyntax.Parse(reader, strings);
        SvgPaint? fillOverride = style.Fill is null ? null : SvgPaintResolver.Resolve(style.Fill, null, null, context.InheritedPresentation.Paint, context.NamedColors);
        bool isPainted = context.InheritedPresentation.IsDisplayed && SvgShapePresentationResolver.ParseDisplay(style.Display) && SvgShapePresentationResolver.ParseVisibility(style.Visibility, context.InheritedPresentation.IsVisible);
        return new SvgUseReference(syntax.Id, syntax.X, syntax.Y, syntax.Transform, SvgColorParser.ParseOpacity(style.Opacity), isPainted, fillOverride);
    }
}
