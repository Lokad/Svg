using System.Globalization;
using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents geometry parsed from policy-validated circle and ellipse elements.</summary>
internal readonly record struct SvgEllipseSyntax(double CenterX, double CenterY, double RadiusX, double RadiusY)
{
    internal static SvgEllipseSyntax ParseCircle(XmlReader reader, SvgViewportLengthBases viewport)
    {
        double centerX = ParseCoordinate(reader.GetAttribute("cx") ?? "0", viewport.Width);
        double centerY = ParseCoordinate(reader.GetAttribute("cy") ?? "0", viewport.Height);
        double radius = ParseRadius(reader.GetAttribute("r"), viewport.NormalizedDiagonal, "Circle radius is required.", "Circle radius cannot be negative.");
        return new SvgEllipseSyntax(centerX, centerY, radius, radius);
    }

    internal static SvgEllipseSyntax ParseEllipse(XmlReader reader, SvgViewportLengthBases viewport)
    {
        double centerX = ParseCoordinate(reader.GetAttribute("cx") ?? "0", viewport.Width);
        double centerY = ParseCoordinate(reader.GetAttribute("cy") ?? "0", viewport.Height);
        double radiusX = ParseRadius(reader.GetAttribute("rx"), viewport.Width, "Ellipse horizontal radius is required.", "Ellipse radii cannot be negative.");
        double radiusY = ParseRadius(reader.GetAttribute("ry"), viewport.Height, "Ellipse vertical radius is required.", "Ellipse radii cannot be negative.");
        return new SvgEllipseSyntax(centerX, centerY, radiusX, radiusY);
    }

    private static double ParseRadius(string? value, double? basis, string missingMessage, string negativeMessage)
    {
        if (value is null) throw new SvgException(SvgErrorCode.MalformedInput, missingMessage);
        double radius = ParseCoordinate(value, basis);
        if (radius < 0) throw new SvgException(SvgErrorCode.MalformedInput, negativeMessage);
        return radius;
    }
    private static double ParseCoordinate(string value, double? basis)
    {
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (!token.EndsWith("%", StringComparison.Ordinal)) return SvgGeometrySyntax.ParseRectangleCoordinate(value);
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Oval coordinate values exceed the configured syntax limit.");
        if (basis is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Percentage oval coordinates require a known user-space viewport.");
        if (!double.TryParse(token[..^1].TrimEnd(), NumberStyles.Float, CultureInfo.InvariantCulture, out double percent) || !double.IsFinite(percent))
            throw new SvgException(SvgErrorCode.MalformedInput, "Percentage oval coordinates must be finite numbers.");
        // Native SVG lengths narrow the percentage and viewport before resolving in user space.
        float resolved = (float)percent * (float)basis.Value / 100f;
        if (!float.IsFinite(resolved) || resolved < (double)int.MinValue || resolved > (double)int.MaxValue)
            throw new SvgException(SvgErrorCode.LimitExceeded, "Percentage oval coordinates exceed the admitted numeric range.");
        return resolved;
    }
}