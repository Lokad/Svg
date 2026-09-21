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
        SvgInheritedContainer resolved = SvgContainerInheritance.Resolve(reader, inline, style, context.StyleSheet, context.InheritedPresentation, context.InheritedTextStyle, root.Opacity != 255 || state.HasOpacityScope, namedColors);
        SvgGroupKind kind = SvgStructuralVocabulary.IsGroupContainer(reader.LocalName) ? SvgGroupKind.Group : SvgGroupKind.Link;
        return new SvgParsedGroup(new SvgGroupScope(reader.Depth, kind, resolved.Presentation, resolved.TextStyle, resolved.Opacity != 255), resolved.Opacity);
    }
}

/// <summary>Couples a stored child context with the opacity required to enter its isolated surface.</summary>
internal readonly record struct SvgParsedGroup(SvgGroupScope Scope, byte Opacity);
