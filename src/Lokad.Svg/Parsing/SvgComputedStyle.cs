using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the supported presentation values after inline, stylesheet and attribute precedence.</summary>
internal readonly record struct SvgComputedStyle(string? Color, string? Fill, string? FillOpacity, string? Stroke, string? Display, string? Visibility, string? Opacity, string? FillRule)
{
    /// <summary>Gets the cascaded stroke linecap value, falling back to the presentation attribute.</summary>
    internal string? Linecap { get; init; }

    /// <summary>Gets the cascaded stroke linejoin value, falling back to the presentation attribute.</summary>
    internal string? Linejoin { get; init; }

    /// <summary>Gets the cascaded stroke dasharray value, falling back to the presentation attribute.</summary>
    internal string? Dasharray { get; init; }

    /// <summary>Gets the cascaded stroke dashoffset value, falling back to the presentation attribute.</summary>
    internal string? Dashoffset { get; init; }
    internal string? Width { get; init; }
    internal string? Miterlimit { get; init; }
    internal string? StrokeOpacity { get; init; }
    internal string? PathLength { get; init; }
    internal string? ClipRule { get; init; }
    internal SvgPaintOrder? PaintOrder { get; init; }
    internal string? MarkerStart { get; init; }
    internal string? MarkerMid { get; init; }
    internal string? MarkerEnd { get; init; }
    internal string? Filter { get; init; }
    internal static SvgComputedStyle Resolve(XmlReader reader, InlineStyle inlineStyle) => Resolve(reader, inlineStyle, default);

    internal static SvgComputedStyle Resolve(XmlReader reader, InlineStyle inlineStyle, InlineStyle styleSheet)
    {
        string? filter = Select(reader, "filter", inlineStyle.Filter, (inlineStyle.Important & InlineImportantProperties.Filter) != 0, styleSheet.Filter, (styleSheet.Important & InlineImportantProperties.Filter) != 0);
        if (filter is not null && filter.Trim() != "none" && reader.LocalName is not ("rect" or "line" or "path" or "polygon" or "polyline" or "circle" or "ellipse"))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filters on containers, instances, text or images are not implemented.");
        if ((inlineStyle.HasVisibleOverflow || styleSheet.HasVisibleOverflow) && reader.LocalName is "svg" or "symbol" or "image" or "pattern" or "marker" or "foreignObject")
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only hidden viewport overflow is implemented.");
        return new(Select(reader, "color", inlineStyle.Color, (inlineStyle.Important & InlineImportantProperties.Color) != 0, styleSheet.Color, (styleSheet.Important & InlineImportantProperties.Color) != 0), Select(reader, "fill", inlineStyle.Fill, (inlineStyle.Important & InlineImportantProperties.Fill) != 0, styleSheet.Fill, (styleSheet.Important & InlineImportantProperties.Fill) != 0), Select(reader, "fill-opacity", inlineStyle.FillOpacity, (inlineStyle.Important & InlineImportantProperties.FillOpacity) != 0, styleSheet.FillOpacity, (styleSheet.Important & InlineImportantProperties.FillOpacity) != 0), Select(reader, "stroke", inlineStyle.Stroke, (inlineStyle.Important & InlineImportantProperties.Stroke) != 0, styleSheet.Stroke, (styleSheet.Important & InlineImportantProperties.Stroke) != 0), Select(reader, "display", inlineStyle.Display, (inlineStyle.Important & InlineImportantProperties.Display) != 0, styleSheet.Display, (styleSheet.Important & InlineImportantProperties.Display) != 0), Select(reader, "visibility", inlineStyle.Visibility, (inlineStyle.Important & InlineImportantProperties.Visibility) != 0, styleSheet.Visibility, (styleSheet.Important & InlineImportantProperties.Visibility) != 0), Select(reader, "opacity", inlineStyle.Opacity, (inlineStyle.Important & InlineImportantProperties.Opacity) != 0, styleSheet.Opacity, (styleSheet.Important & InlineImportantProperties.Opacity) != 0), Select(reader, "fill-rule", inlineStyle.FillRule, (inlineStyle.Important & InlineImportantProperties.FillRule) != 0, styleSheet.FillRule, (styleSheet.Important & InlineImportantProperties.FillRule) != 0))
        { Filter = filter, MarkerStart = Select(reader, "marker-start", inlineStyle.MarkerStart, (inlineStyle.Important & InlineImportantProperties.MarkerStart) != 0, styleSheet.MarkerStart, (styleSheet.Important & InlineImportantProperties.MarkerStart) != 0), MarkerMid = Select(reader, "marker-mid", inlineStyle.MarkerMid, (inlineStyle.Important & InlineImportantProperties.MarkerMid) != 0, styleSheet.MarkerMid, (styleSheet.Important & InlineImportantProperties.MarkerMid) != 0), MarkerEnd = Select(reader, "marker-end", inlineStyle.MarkerEnd, (inlineStyle.Important & InlineImportantProperties.MarkerEnd) != 0, styleSheet.MarkerEnd, (styleSheet.Important & InlineImportantProperties.MarkerEnd) != 0), PaintOrder = SvgPaintOrderSyntax.Parse(Select(reader, "paint-order", inlineStyle.PaintOrder, (inlineStyle.Important & InlineImportantProperties.PaintOrder) != 0, styleSheet.PaintOrder, (styleSheet.Important & InlineImportantProperties.PaintOrder) != 0)), ClipRule = Select(reader, "clip-rule", inlineStyle.ClipRule, (inlineStyle.Important & InlineImportantProperties.ClipRule) != 0, styleSheet.ClipRule, (styleSheet.Important & InlineImportantProperties.ClipRule) != 0), Linecap = Select(reader, "stroke-linecap", inlineStyle.Linecap, (inlineStyle.Important & InlineImportantProperties.Linecap) != 0, styleSheet.Linecap, (styleSheet.Important & InlineImportantProperties.Linecap) != 0), Linejoin = Select(reader, "stroke-linejoin", inlineStyle.Linejoin, (inlineStyle.Important & InlineImportantProperties.Linejoin) != 0, styleSheet.Linejoin, (styleSheet.Important & InlineImportantProperties.Linejoin) != 0), Dasharray = Select(reader, "stroke-dasharray", inlineStyle.Dasharray, (inlineStyle.Important & InlineImportantProperties.Dasharray) != 0, styleSheet.Dasharray, (styleSheet.Important & InlineImportantProperties.Dasharray) != 0), Dashoffset = Select(reader, "stroke-dashoffset", inlineStyle.Dashoffset, (inlineStyle.Important & InlineImportantProperties.Dashoffset) != 0, styleSheet.Dashoffset, (styleSheet.Important & InlineImportantProperties.Dashoffset) != 0), Width = Select(reader, "stroke-width", inlineStyle.Width, (inlineStyle.Important & InlineImportantProperties.Width) != 0, styleSheet.Width, (styleSheet.Important & InlineImportantProperties.Width) != 0), Miterlimit = Select(reader, "stroke-miterlimit", inlineStyle.Miterlimit, (inlineStyle.Important & InlineImportantProperties.Miterlimit) != 0, styleSheet.Miterlimit, (styleSheet.Important & InlineImportantProperties.Miterlimit) != 0), StrokeOpacity = Select(reader, "stroke-opacity", inlineStyle.StrokeOpacity, (inlineStyle.Important & InlineImportantProperties.StrokeOpacity) != 0, styleSheet.StrokeOpacity, (styleSheet.Important & InlineImportantProperties.StrokeOpacity) != 0), PathLength = Select(reader, "pathLength", inlineStyle.PathLength, (inlineStyle.Important & InlineImportantProperties.PathLength) != 0, styleSheet.PathLength, (styleSheet.Important & InlineImportantProperties.PathLength) != 0) };

    }

    /// <summary>Reads the presentation attribute only when neither an !important nor a normal inline or stylesheet value wins.</summary>
    private static string? Select(XmlReader reader, string attribute, string? inlineValue, bool inlineImportant, string? styleSheetValue, bool styleSheetImportant)
    {
        string? resolved = inlineImportant && inlineValue is not null ? inlineValue
            : styleSheetImportant && styleSheetValue is not null ? styleSheetValue
            : inlineValue ?? styleSheetValue ?? reader.GetAttribute(attribute);
        if (attribute.StartsWith("marker-", StringComparison.Ordinal)) return resolved;
        if (attribute != "paint-order" && resolved is not null && SvgInlineStyleParser.TryMapPaintKeyword(attribute.AsSpan(), resolved.AsSpan(), out string? mapped))
            return mapped == SvgInlineStyleParser.InheritedValue && SvgInlineStyleParser.IsInheritedPaintProperty(attribute.AsSpan()) ? null : mapped;
        return resolved;
    }
}
