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
        string family = SvgTextFontSyntax.ParseFamily(SvgTextFontSyntax.GetProperty(reader, "font-family") ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text requires an explicit font-family in the initial supported subset."));
        int weight = SvgTextFontSyntax.ParseWeight(SvgTextFontSyntax.GetProperty(reader, "font-weight") ?? "normal");
        bool italic = SvgTextFontSyntax.ParseItalic(SvgTextFontSyntax.GetProperty(reader, "font-style") ?? "normal");
        double fontSize = SvgGeometrySyntax.ParseRectangleCoordinate(SvgTextFontSyntax.GetProperty(reader, "font-size") ?? "16");
        if (fontSize <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text font-size must be positive.");
        SvgTextAnchor anchor = ParseAnchor(reader.GetAttribute("text-anchor") ?? "start");
        return new SvgTextStyle(new SvgFontFaceRequest(family, weight, italic), fontSize, SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("x") ?? "0"), SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("y") ?? "0"), SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("dx") ?? "0"), SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("dy") ?? "0"), anchor);
    }

    private static SvgTextAnchor ParseAnchor(string value)
    {
        string token = value.Trim();
        if (token.Equals("start", StringComparison.OrdinalIgnoreCase)) return SvgTextAnchor.Start;
        if (token.Equals("middle", StringComparison.OrdinalIgnoreCase)) return SvgTextAnchor.Middle;
        if (token.Equals("end", StringComparison.OrdinalIgnoreCase)) return SvgTextAnchor.End;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only start, middle, and end text-anchor values are supported.");
    }
}

/// <summary>Normalizes the explicit single-family text font tokens shared by discovery and direct rendering.</summary>
internal static class SvgTextFontSyntax
{
    internal static string ParseFamily(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string family = value.Trim();
        if (family.Length >= 2 && ((family[0] == '\'' && family[^1] == '\'') || (family[0] == '"' && family[^1] == '"'))) family = family[1..^1];
        if (family.Length == 0 || family.Contains(',')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only one nonempty text font family is supported.");
        return family;
    }

    internal static int ParseWeight(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string token = value.Trim();
        if (token.Equals("normal", StringComparison.OrdinalIgnoreCase)) return 400;
        if (token.Equals("bold", StringComparison.OrdinalIgnoreCase)) return 700;
        if (int.TryParse(token, CultureInfo.InvariantCulture, out int weight) && weight > 0) return weight;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive numeric, normal, and bold text font weights are supported.");
    }

    internal static bool ParseItalic(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Trim().Equals("normal", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Trim().Equals("italic", StringComparison.OrdinalIgnoreCase)) return true;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only normal and italic text font styles are supported.");
    }

    internal static string? GetProperty(XmlReader reader, string name)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string? inline = reader.GetAttribute("style");
        string? value = null;
        if (inline is not null)
        {
            foreach (string declaration in inline.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = declaration.IndexOf(':');
                if (separator < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Malformed inline text style declarations are not implemented.");
                if (declaration[..separator].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) value = declaration[(separator + 1)..].Trim();
            }
        }
        return value ?? reader.GetAttribute(name);
    }
}

/// <summary>Represents the explicit single-font style and baseline origin of one initial text run.</summary>
internal readonly record struct SvgTextStyle(SvgFontFaceRequest Font, double FontSize, double X, double Y, double Dx, double Dy, SvgTextAnchor Anchor);

/// <summary>Identifies the admitted horizontal anchor for one direct text run.</summary>
internal enum SvgTextAnchor { Start, Middle, End }
