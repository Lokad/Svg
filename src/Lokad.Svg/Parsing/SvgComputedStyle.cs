using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the supported presentation values after inline, stylesheet and attribute precedence.</summary>
internal readonly record struct SvgComputedStyle(string? Color, string? Fill, string? FillOpacity, string? Stroke, string? Display, string? Visibility, string? Opacity, string? FillRule)
{
    internal static SvgComputedStyle Resolve(XmlReader reader, InlineStyle inlineStyle) => Resolve(reader, inlineStyle, default);

    internal static SvgComputedStyle Resolve(XmlReader reader, InlineStyle inlineStyle, InlineStyle styleSheet)
        => new(Select(inlineStyle.Color, styleSheet.Color, reader.GetAttribute("color")), Select(inlineStyle.Fill, styleSheet.Fill, reader.GetAttribute("fill")), Select(inlineStyle.FillOpacity, styleSheet.FillOpacity, reader.GetAttribute("fill-opacity")), Select(inlineStyle.Stroke, styleSheet.Stroke, reader.GetAttribute("stroke")), Select(inlineStyle.Display, styleSheet.Display, reader.GetAttribute("display")), Select(inlineStyle.Visibility, styleSheet.Visibility, reader.GetAttribute("visibility")), Select(inlineStyle.Opacity, styleSheet.Opacity, reader.GetAttribute("opacity")), Select(inlineStyle.FillRule, styleSheet.FillRule, reader.GetAttribute("fill-rule")));

    private static string? Select(string? inlineValue, string? styleSheetValue, string? presentationValue) => inlineValue ?? styleSheetValue ?? presentationValue;
}
