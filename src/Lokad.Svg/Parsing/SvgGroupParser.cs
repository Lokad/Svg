using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Parses one bounded group or link context before its children are traversed.</summary>
internal static class SvgGroupParser
{
    internal static SvgParsedGroup Parse(XmlReader reader, bool directChild, SvgRootAttributes root, SvgDocumentState state, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgElementAttributePolicy.ValidateGroup(reader, workCounter, cancellationToken);
        SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, directChild, root, state, namedColors, workCounter, cancellationToken);
        InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
        SvgComputedStyle style = SvgComputedStyle.Resolve(reader, inline, context.StyleSheet);
        SvgResolvedPresentation presentation = SvgContainerPresentationResolver.Resolve(style, context.InheritedPresentation.Paint, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible, namedColors);
        AxisTransform transform = context.ResolveTransform(reader);
        SvgGroupKind kind = reader.LocalName == "g" ? SvgGroupKind.Group : SvgGroupKind.Link;
        SvgInheritedTextStyle inheritedTextStyle = SvgTextInheritance.Resolve(reader, context.InheritedTextStyle, inline.TextFont.Overlay(context.StyleSheet.TextFont));
        return new SvgParsedGroup(new SvgGroupScope(reader.Depth, kind, new SvgInheritedPresentation(presentation.Paint, presentation.IsDisplayed, presentation.IsVisible, transform), inheritedTextStyle, presentation.Opacity != 255), presentation.Opacity);
    }
}

/// <summary>Couples a stored child context with the opacity required to enter its isolated surface.</summary>
internal readonly record struct SvgParsedGroup(SvgGroupScope Scope, byte Opacity);
