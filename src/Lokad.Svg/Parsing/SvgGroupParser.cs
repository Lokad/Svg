using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Parses one bounded group or link context before its children are traversed.</summary>
internal static class SvgGroupParser
{
    internal static SvgParsedGroup Parse(XmlReader reader, bool directChild, SvgRootAttributes root, SvgDocumentState state, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgElementAttributePolicy.ValidateGroup(reader);
        SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, directChild, root, state, namedColors, workCounter, cancellationToken);
        SvgComputedStyle style = context.ResolveStyle(reader);
        SvgResolvedContainerPresentation presentation = SvgContainerPresentationResolver.Resolve(style, context.InheritedPaint, context.InheritedDisplayed, context.InheritedVisibility, namedColors);
        AxisTransform transform = context.ResolveTransform(reader);
        SvgGroupKind kind = reader.LocalName == "g" ? SvgGroupKind.Group : SvgGroupKind.Link;
        return new SvgParsedGroup(new SvgGroupScope(reader.Depth, kind, presentation.Paint, presentation.IsDisplayed, presentation.IsVisible, transform, presentation.Opacity != 255), presentation.Opacity);
    }
}

/// <summary>Couples a stored child context with the opacity required to enter its isolated surface.</summary>
internal readonly record struct SvgParsedGroup(SvgGroupScope Scope, byte Opacity);
