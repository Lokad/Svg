using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the supported presentation values after inline, stylesheet and attribute precedence.</summary>
internal readonly record struct SvgComputedStyle(string? Color, string? Fill, string? FillOpacity, string? Stroke, string? Display, string? Visibility, string? Opacity, string? FillRule)
{
    internal static SvgComputedStyle Resolve(XmlReader reader, InlineStyle inlineStyle) => Resolve(reader, inlineStyle, default);

    internal static SvgComputedStyle Resolve(XmlReader reader, InlineStyle inlineStyle, InlineStyle styleSheet)
        => new(Select(reader, "color", inlineStyle.Color, (inlineStyle.Important & InlineImportantProperties.Color) != 0, styleSheet.Color, (styleSheet.Important & InlineImportantProperties.Color) != 0), Select(reader, "fill", inlineStyle.Fill, (inlineStyle.Important & InlineImportantProperties.Fill) != 0, styleSheet.Fill, (styleSheet.Important & InlineImportantProperties.Fill) != 0), Select(reader, "fill-opacity", inlineStyle.FillOpacity, (inlineStyle.Important & InlineImportantProperties.FillOpacity) != 0, styleSheet.FillOpacity, (styleSheet.Important & InlineImportantProperties.FillOpacity) != 0), Select(reader, "stroke", inlineStyle.Stroke, (inlineStyle.Important & InlineImportantProperties.Stroke) != 0, styleSheet.Stroke, (styleSheet.Important & InlineImportantProperties.Stroke) != 0), Select(reader, "display", inlineStyle.Display, (inlineStyle.Important & InlineImportantProperties.Display) != 0, styleSheet.Display, (styleSheet.Important & InlineImportantProperties.Display) != 0), Select(reader, "visibility", inlineStyle.Visibility, (inlineStyle.Important & InlineImportantProperties.Visibility) != 0, styleSheet.Visibility, (styleSheet.Important & InlineImportantProperties.Visibility) != 0), Select(reader, "opacity", inlineStyle.Opacity, (inlineStyle.Important & InlineImportantProperties.Opacity) != 0, styleSheet.Opacity, (styleSheet.Important & InlineImportantProperties.Opacity) != 0), Select(reader, "fill-rule", inlineStyle.FillRule, (inlineStyle.Important & InlineImportantProperties.FillRule) != 0, styleSheet.FillRule, (styleSheet.Important & InlineImportantProperties.FillRule) != 0));

    /// <summary>Reads the presentation attribute only when neither an !important nor a normal inline or stylesheet value wins.</summary>
    private static string? Select(XmlReader reader, string attribute, string? inlineValue, bool inlineImportant, string? styleSheetValue, bool styleSheetImportant)
    {
        if (inlineImportant && inlineValue is not null) return inlineValue;
        if (styleSheetImportant && styleSheetValue is not null) return styleSheetValue;
        return inlineValue ?? styleSheetValue ?? reader.GetAttribute(attribute);
    }
}
