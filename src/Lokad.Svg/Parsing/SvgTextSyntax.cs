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
        => Parse(reader, new ParserWorkCounter(long.MaxValue), CancellationToken.None);

    /// <summary>Parses direct text font syntax while charging inline-property scans to the current conversion.</summary>
    internal static SvgTextStyle Parse(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(workCounter);
        return Parse(reader, SvgTextFontSyntax.ReadInlineProperties(reader, workCounter, cancellationToken));
    }

    /// <summary>Parses direct text font syntax from declarations already scanned by style resolution.</summary>
    internal static SvgTextStyle Parse(XmlReader reader, SvgTextInlineFontProperties inline)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string family = SvgTextFontSyntax.ParseFamily(inline.Family ?? reader.GetAttribute("font-family") ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text requires an explicit font-family in the initial supported subset."));
        int weight = SvgTextFontSyntax.ParseWeight(inline.Weight ?? reader.GetAttribute("font-weight") ?? "normal");
        bool italic = SvgTextFontSyntax.ParseItalic(inline.Style ?? reader.GetAttribute("font-style") ?? "normal");
        double fontSize = SvgGeometrySyntax.ParseTextCoordinate(inline.Size ?? reader.GetAttribute("font-size") ?? "16");
        if (fontSize <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text font-size must be positive.");
        SvgTextAnchor anchor = ParseAnchor(reader.GetAttribute("text-anchor") ?? "start");
        return new SvgTextStyle(new SvgFontFaceRequest(family, weight, italic), fontSize, SvgGeometrySyntax.ParseTextCoordinate(reader.GetAttribute("x") ?? "0"), SvgGeometrySyntax.ParseTextCoordinate(reader.GetAttribute("y") ?? "0"), SvgGeometrySyntax.ParseTextCoordinate(reader.GetAttribute("dx") ?? "0"), SvgGeometrySyntax.ParseTextCoordinate(reader.GetAttribute("dy") ?? "0"), anchor);
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

    internal static SvgTextInlineFontProperties ReadInlineProperties(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(workCounter);
        string? inline = reader.GetAttribute("style");
        if (inline is null) return default;
        string? family = null, weight = null, style = null, size = null;
        ReadOnlySpan<char> declarations = inline.AsSpan();
        int position = 0;
        int nextCheckpoint = 0;
        while (position < declarations.Length)
        {
            int start = position;
            while (position < declarations.Length)
            {
                if (position >= nextCheckpoint)
                {
                    workCounter.Consume(cancellationToken);
                    nextCheckpoint += 4096;
                }
                if (declarations[position] == ';') break;
                position++;
            }
            ReadOnlySpan<char> declaration = declarations[start..position].Trim();
            if (position < declarations.Length) position++;
            if (declaration.IsEmpty) continue;
            int separator = declaration.IndexOf(':');
            if (separator < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Malformed inline text style declarations are not implemented.");
            ReadOnlySpan<char> property = declaration[..separator].Trim();
            ReadOnlySpan<char> value = declaration[(separator + 1)..].Trim();
            if (property.Length > SvgSyntaxLimits.MaximumValueTokenCharacters || value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Inline text font property names and values exceed the configured syntax limit.");
            if (property.Equals("font-family", StringComparison.OrdinalIgnoreCase)) family = value.ToString();
            else if (property.Equals("font-weight", StringComparison.OrdinalIgnoreCase)) weight = value.ToString();
            else if (property.Equals("font-style", StringComparison.OrdinalIgnoreCase)) style = value.ToString();
            else if (property.Equals("font-size", StringComparison.OrdinalIgnoreCase)) size = value.ToString();
        }
        return new SvgTextInlineFontProperties(family, weight, style, size);
    }
}

/// <summary>Represents the direct-text font declarations found in one inline style value.</summary>
internal readonly record struct SvgTextInlineFontProperties(string? Family, string? Weight, string? Style, string? Size);

/// <summary>Represents the explicit single-font style and baseline origin of one initial text run.</summary>
internal readonly record struct SvgTextStyle(SvgFontFaceRequest Font, double FontSize, double X, double Y, double Dx, double Dy, SvgTextAnchor Anchor);

/// <summary>Identifies the admitted horizontal anchor for one direct text run.</summary>
internal enum SvgTextAnchor { Start, Middle, End }
