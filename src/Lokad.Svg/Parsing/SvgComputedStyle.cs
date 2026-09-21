using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the supported presentation values after inline, stylesheet and attribute precedence.</summary>
internal readonly record struct SvgComputedStyle(string? Color, string? Fill, string? FillOpacity, string? Stroke, string? Display, string? Visibility, string? Opacity, string? FillRule)
{
    internal static SvgComputedStyle Resolve(XmlReader reader, InlineStyle inlineStyle) => Resolve(reader, inlineStyle, default);

    internal static SvgComputedStyle Resolve(XmlReader reader, InlineStyle inlineStyle, InlineStyle styleSheet)
        => new(Select(reader, "color", inlineStyle.Color, styleSheet.Color), Select(reader, "fill", inlineStyle.Fill, styleSheet.Fill), Select(reader, "fill-opacity", inlineStyle.FillOpacity, styleSheet.FillOpacity), Select(reader, "stroke", inlineStyle.Stroke, styleSheet.Stroke), Select(reader, "display", inlineStyle.Display, styleSheet.Display), Select(reader, "visibility", inlineStyle.Visibility, styleSheet.Visibility), Select(reader, "opacity", inlineStyle.Opacity, styleSheet.Opacity), Select(reader, "fill-rule", inlineStyle.FillRule, styleSheet.FillRule));

    /// <summary>Reads the presentation attribute only when inline and stylesheet values are both absent.</summary>
    private static string? Select(XmlReader reader, string attribute, string? inlineValue, string? styleSheetValue) => inlineValue ?? styleSheetValue ?? reader.GetAttribute(attribute);
}
