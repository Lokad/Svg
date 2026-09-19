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
        if (style.FillRule is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill rules are only implemented on shapes.");
        SvgShapePresentationResolver.ValidateStroke(style.Stroke);
        SvgPaint paint = SvgPaintResolver.Resolve(style.Fill, style.FillOpacity, style.Color, context.InheritedPaint, namedColors);
        bool displayed = context.InheritedDisplayed && SvgShapePresentationResolver.ParseDisplay(style.Display);
        bool visible = SvgShapePresentationResolver.ParseVisibility(style.Visibility, context.InheritedVisibility);
        AxisTransform transform = context.ResolveTransform(reader);
        byte opacity = SvgColorParser.ParseOpacity(style.Opacity);
        SvgGroupKind kind = reader.LocalName == "g" ? SvgGroupKind.Group : SvgGroupKind.Link;
        return new SvgParsedGroup(new SvgGroupScope(reader.Depth, kind, paint, displayed, visible, transform, opacity != 255), opacity);
    }
}

/// <summary>Couples a stored child context with the opacity required to enter its isolated surface.</summary>
internal readonly record struct SvgParsedGroup(SvgGroupScope Scope, byte Opacity);
