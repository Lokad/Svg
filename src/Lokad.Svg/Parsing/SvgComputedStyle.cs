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
    internal static SvgComputedStyle Resolve(XmlReader reader, InlineStyle inlineStyle) => Resolve(reader, inlineStyle, default);

    internal static SvgComputedStyle Resolve(XmlReader reader, InlineStyle inlineStyle, InlineStyle styleSheet)
        => new(Select(reader, "color", inlineStyle.Color, (inlineStyle.Important & InlineImportantProperties.Color) != 0, styleSheet.Color, (styleSheet.Important & InlineImportantProperties.Color) != 0), Select(reader, "fill", inlineStyle.Fill, (inlineStyle.Important & InlineImportantProperties.Fill) != 0, styleSheet.Fill, (styleSheet.Important & InlineImportantProperties.Fill) != 0), Select(reader, "fill-opacity", inlineStyle.FillOpacity, (inlineStyle.Important & InlineImportantProperties.FillOpacity) != 0, styleSheet.FillOpacity, (styleSheet.Important & InlineImportantProperties.FillOpacity) != 0), Select(reader, "stroke", inlineStyle.Stroke, (inlineStyle.Important & InlineImportantProperties.Stroke) != 0, styleSheet.Stroke, (styleSheet.Important & InlineImportantProperties.Stroke) != 0), Select(reader, "display", inlineStyle.Display, (inlineStyle.Important & InlineImportantProperties.Display) != 0, styleSheet.Display, (styleSheet.Important & InlineImportantProperties.Display) != 0), Select(reader, "visibility", inlineStyle.Visibility, (inlineStyle.Important & InlineImportantProperties.Visibility) != 0, styleSheet.Visibility, (styleSheet.Important & InlineImportantProperties.Visibility) != 0), Select(reader, "opacity", inlineStyle.Opacity, (inlineStyle.Important & InlineImportantProperties.Opacity) != 0, styleSheet.Opacity, (styleSheet.Important & InlineImportantProperties.Opacity) != 0), Select(reader, "fill-rule", inlineStyle.FillRule, (inlineStyle.Important & InlineImportantProperties.FillRule) != 0, styleSheet.FillRule, (styleSheet.Important & InlineImportantProperties.FillRule) != 0))
        { Linecap = Select(reader, "stroke-linecap", inlineStyle.Linecap, (inlineStyle.Important & InlineImportantProperties.Linecap) != 0, styleSheet.Linecap, (styleSheet.Important & InlineImportantProperties.Linecap) != 0), Linejoin = Select(reader, "stroke-linejoin", inlineStyle.Linejoin, (inlineStyle.Important & InlineImportantProperties.Linejoin) != 0, styleSheet.Linejoin, (styleSheet.Important & InlineImportantProperties.Linejoin) != 0), Dasharray = Select(reader, "stroke-dasharray", inlineStyle.Dasharray, (inlineStyle.Important & InlineImportantProperties.Dasharray) != 0, styleSheet.Dasharray, (styleSheet.Important & InlineImportantProperties.Dasharray) != 0), Dashoffset = Select(reader, "stroke-dashoffset", inlineStyle.Dashoffset, (inlineStyle.Important & InlineImportantProperties.Dashoffset) != 0, styleSheet.Dashoffset, (styleSheet.Important & InlineImportantProperties.Dashoffset) != 0) };

    /// <summary>Reads the presentation attribute only when neither an !important nor a normal inline or stylesheet value wins.</summary>
    private static string? Select(XmlReader reader, string attribute, string? inlineValue, bool inlineImportant, string? styleSheetValue, bool styleSheetImportant)
    {
        if (inlineImportant && inlineValue is not null) return inlineValue;
        if (styleSheetImportant && styleSheetValue is not null) return styleSheetValue;
        string? resolved = inlineValue ?? styleSheetValue ?? reader.GetAttribute(attribute);
        if (resolved is not null && SvgInlineStyleParser.TryMapPaintKeyword(attribute.AsSpan(), resolved.AsSpan(), out string? mapped)) return mapped;
        return resolved;
    }
}
