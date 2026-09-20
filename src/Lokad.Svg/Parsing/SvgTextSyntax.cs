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
        => Parse(reader, new ParserWorkCounter(long.MaxValue), CancellationToken.None, SvgInheritedTextStyle.Default);

    /// <summary>Parses direct text font syntax while charging inline-property scans to the current conversion.</summary>
    internal static SvgTextStyle Parse(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => Parse(reader, workCounter, cancellationToken, SvgInheritedTextStyle.Default);

    internal static SvgTextStyle Parse(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken, SvgInheritedTextStyle inherited)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(workCounter);
        return Parse(reader, SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true).TextFont, inherited);
    }

    /// <summary>Parses direct text font syntax from declarations already scanned by style resolution.</summary>
    internal static SvgTextStyle Parse(XmlReader reader, SvgTextInlineFontProperties inline)
        => Parse(reader, inline, SvgInheritedTextStyle.Default);

    /// <summary>Parses direct text font syntax from declarations already scanned by style resolution and bounded ancestor inheritance.</summary>
    internal static SvgTextStyle Parse(XmlReader reader, SvgTextInlineFontProperties inline, SvgInheritedTextStyle inherited)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string family = SvgTextFontSyntax.ParseFamily(inline.Family ?? reader.GetAttribute("font-family") ?? inherited.Family ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text requires an explicit font-family in the initial supported subset."));
        string? weightValue = inline.Weight ?? reader.GetAttribute("font-weight");
        int weight = weightValue is null ? inherited.Weight : SvgTextFontSyntax.ParseWeight(weightValue);
        string? styleValue = inline.Style ?? reader.GetAttribute("font-style");
        bool italic = styleValue is null ? inherited.Italic : SvgTextFontSyntax.ParseItalic(styleValue);
        string? sizeValue = inline.Size ?? reader.GetAttribute("font-size");
        double fontSize = sizeValue is null ? inherited.FontSize : SvgGeometrySyntax.ParseTextCoordinate(sizeValue);
        if (fontSize <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text font-size must be positive.");
        SvgTextAnchor anchor = ParseAnchor(reader.GetAttribute("text-anchor") ?? "start");
        return new SvgTextStyle(new SvgFontFaceRequest(family, weight, italic), fontSize, SvgGeometrySyntax.ParseTextCoordinate(reader.GetAttribute("x") ?? "0"), SvgGeometrySyntax.ParseTextCoordinate(reader.GetAttribute("y") ?? "0"), SvgGeometrySyntax.ParseTextCoordinate(reader.GetAttribute("dx") ?? "0"), SvgGeometrySyntax.ParseTextCoordinate(reader.GetAttribute("dy") ?? "0"), anchor, reader.GetAttribute("id") ?? string.Empty);
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

}

/// <summary>Represents the direct-text font declarations found in one inline style value.</summary>
internal readonly record struct SvgTextInlineFontProperties(string? Family, string? Weight, string? Style, string? Size)
{
    /// <summary>Uses this declaration's explicitly present values before the lower-precedence declarations.</summary>
    internal SvgTextInlineFontProperties Overlay(SvgTextInlineFontProperties lower)
        => new(Family ?? lower.Family, Weight ?? lower.Weight, Style ?? lower.Style, Size ?? lower.Size);
}

/// <summary>Represents the explicit single-font style and baseline origin of one initial text run.</summary>
internal readonly record struct SvgTextStyle(SvgFontFaceRequest Font, double FontSize, double X, double Y, double Dx, double Dy, SvgTextAnchor Anchor, string SourceElementId);

/// <summary>Identifies the admitted horizontal anchor for one direct text run.</summary>
internal enum SvgTextAnchor { Start, Middle, End }
