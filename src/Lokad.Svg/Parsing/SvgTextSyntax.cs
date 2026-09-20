namespace Lokad.Svg.Parsing;

using Lokad.Svg.Fonts;
using System.Globalization;
using System.Xml;

/// <summary>Defines the bounded direct-text subset shared by extraction and simple layout.</summary>
internal static class SvgTextLimits
{
    internal const int MaximumCharacters = 32_768;
}

/// <summary>Parses the initial explicit single-font SVG text attribute subset.</summary>
internal static class SvgTextSyntax
{
    internal static SvgTextStyle Parse(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string family = reader.GetAttribute("font-family") ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text requires an explicit font-family in the initial supported subset.");
        family = family.Trim();
        if (family.Length >= 2 && ((family[0] == '\'' && family[^1] == '\'') || (family[0] == '"' && family[^1] == '"'))) family = family[1..^1];
        if (family.Length == 0 || family.Contains(',')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only one nonempty text font family is supported.");
        int weight = ParseWeight(reader.GetAttribute("font-weight") ?? "normal");
        bool italic = (reader.GetAttribute("font-style") ?? "normal") switch
        {
            "normal" => false,
            "italic" => true,
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only normal and italic text font styles are supported.")
        };
        double fontSize = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("font-size") ?? "16");
        if (fontSize <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text font-size must be positive.");
        return new SvgTextStyle(new SvgFontFaceRequest(family, weight, italic), fontSize, SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("x") ?? "0"), SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("y") ?? "0"));
    }

    private static int ParseWeight(string value)
        => value switch
        {
            "normal" => 400,
            "bold" => 700,
            _ when int.TryParse(value, CultureInfo.InvariantCulture, out int weight) && weight > 0 => weight,
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive numeric, normal, and bold text font weights are supported.")
        };
}

/// <summary>Represents the explicit single-font style and baseline origin of one initial text run.</summary>
internal readonly record struct SvgTextStyle(SvgFontFaceRequest Font, double FontSize, double X, double Y);
