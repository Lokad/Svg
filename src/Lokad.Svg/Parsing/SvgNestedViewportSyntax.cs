using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the bounded nested viewport attributes before clip-box construction.</summary>
internal readonly record struct SvgNestedViewportSyntax(double X, double Y, double Width, double Height)
{
    internal static SvgNestedViewportSyntax Parse(XmlReader reader, double? baseWidth, double? baseHeight)
    {
        double x = ParsePosition(reader.GetAttribute("x"), baseWidth);
        double y = ParsePosition(reader.GetAttribute("y"), baseHeight);
        double width = ParseDimension(reader.GetAttribute("width"), baseWidth);
        double height = ParseDimension(reader.GetAttribute("height"), baseHeight);
        return new SvgNestedViewportSyntax(x, y, width, height);
    }

    /// <summary>Validates the admitted viewport overflow shared by attributes and style declarations.</summary>
    internal static void ValidateOverflow(ReadOnlySpan<char> value)
    {
        if (value.Trim().Equals("hidden".AsSpan(), StringComparison.OrdinalIgnoreCase)) return;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only hidden viewport overflow is implemented.");
    }

    private static double ParseDimension(string? value, double? baseValue)
    {
        if (value is null)
        {
            if (baseValue is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Omitted nested viewport dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            return baseValue.Value;
        }
        double parsed = SvgGeometrySyntax.ParseViewportDimension(value, baseValue);
        if (parsed < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Nested viewport dimensions cannot be negative.");
        return parsed;
    }

    private static double ParsePosition(string? value, double? baseValue)
    {
        if (value is null) return 0;
        return SvgGeometrySyntax.ParseViewportDimension(value, baseValue);
    }
}
/// <summary>Retains one admitted viewport size with its XML depth while discovering font requests.</summary>
internal readonly record struct SvgViewportSize(double? Width, double? Height, int Depth);
