using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Parses one bounded group or link context before its children are traversed.</summary>
internal static class SvgGroupParser
{
    internal static SvgParsedGroup Parse(XmlReader reader, bool directChild, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisibility, AxisTransform inheritedTransform, BudgetedParserList<CssRule> styleRules, BudgetedParserList<SvgGroupScope> groupDepths, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        InlineStyle styleSheet = SvgStyleSheet.Resolve(reader, styleRules, directChild, groupDepths.Any(static group => group.Name == "g"), groupDepths.Count > 0 && groupDepths[^1].Name == "g", workCounter, cancellationToken);
        SvgComputedStyle style = SvgComputedStyle.Resolve(reader, SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken), styleSheet);
        SvgElementAttributePolicy.ValidateGroup(reader);
        if (style.FillRule is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill rules are only implemented on shapes.");
        SvgShapePresentationResolver.ValidateStroke(style.Stroke);
        SvgPaint paint = SvgPaintResolver.Resolve(style.Fill, style.FillOpacity, style.Color, inheritedPaint, namedColors);
        bool displayed = inheritedDisplayed && SvgShapePresentationResolver.ParseDisplay(style.Display);
        bool visible = SvgShapePresentationResolver.ParseVisibility(style.Visibility, inheritedVisibility);
        AxisTransform transform = inheritedTransform.Compose(SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform")));
        byte opacity = SvgColorParser.ParseOpacity(style.Opacity);
        return new SvgParsedGroup(new SvgGroupScope(reader.Depth, reader.LocalName, paint, displayed, visible, transform, opacity != 255), opacity);
    }
}

/// <summary>Couples a stored child context with the opacity required to enter its isolated surface.</summary>
internal readonly record struct SvgParsedGroup(SvgGroupScope Scope, byte Opacity);
