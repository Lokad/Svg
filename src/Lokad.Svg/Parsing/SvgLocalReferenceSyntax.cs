using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Parses the policy-validated bounded local-use reference syntax before style resolution.</summary>
internal readonly record struct SvgLocalReferenceSyntax(string Id, double X, double Y, double? Width, double? Height)
{
    /// <summary>Retains placement even for an omitted/empty source; its empty identifier is never looked up.</summary>
    internal static SvgLocalReferenceSyntax Parse(XmlReader reader, BudgetedParserStringStore strings, double? baseWidth, double? baseHeight, ParserWorkCounter work, CancellationToken token)
    {
        string? href = ReadHref(reader);
        if (!string.IsNullOrEmpty(href))
        {
            if (href.Length < 2 || href[0] != '#') throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local supported-geometry use references are implemented.");
            if (href.Length - 1 > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Local reference identifiers exceed the configured syntax limit.");
        }
        double x = ParsePosition(reader.GetAttribute("x"), baseWidth, work, token);
        double y = ParsePosition(reader.GetAttribute("y"), baseHeight, work, token);
        double? width = ParseDimension(reader.GetAttribute("width"), baseWidth);
        double? height = ParseDimension(reader.GetAttribute("height"), baseHeight);
        if (string.IsNullOrEmpty(href)) return new(string.Empty, x, y, width, height);
        return new SvgLocalReferenceSyntax(strings.Copy(href.AsSpan(1)), x, y, width, height);
    }

    /// <summary>Preserves href precedence, including an empty href overriding xlink:href.</summary>
    internal static string? ReadHref(XmlReader reader)
        => reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink");

    private static double? ParseDimension(string? value, double? baseValue)
    {
        if (value is null) return null;
        double parsed = SvgGeometrySyntax.ParseViewportDimension(value, baseValue);
        if (parsed < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Use dimensions cannot be negative.");
        return parsed;
    }

    private static double ParsePosition(string? value, double? baseValue, ParserWorkCounter work, CancellationToken token)
    {
        if (value is null) return 0;
        double validated = SvgGeometrySyntax.ParseViewportDimension(value, baseValue);
        ReadOnlySpan<char> number = value.AsSpan().Trim();
        if (number.EndsWith("%")) return validated;
        if (number.EndsWith("px", StringComparison.OrdinalIgnoreCase)) number = number[..^2].TrimEnd();
        // Native local placement stores length scalars as floats before concatenating the source map.
        float native = SvgFloatNumber.ReadValidated(number, work, token);
        // Long leading-zero or rescaled mantissas can overflow intermediate float place values.
        return float.IsFinite(native) ? native : (float)validated;
    }
}
