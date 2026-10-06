using System.Globalization;
using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Parses the bounded root viewport syntax independently of root presentation resolution.</summary>
/// <summary>Represents one bounded root dimension as absolute pixels or viewport percentage.</summary>
internal readonly record struct SvgRootLength(double Value, bool IsPercentage)
{
    /// <summary>Resolves this dimension against an explicit viewport side, falling back to the default intrinsic side.</summary>
    internal double Resolve(double? viewportSide, double defaultSide)
    {
        if (!IsPercentage) return Value;
        double resolved = (viewportSide ?? defaultSide) * Value / 100;
        if (!double.IsFinite(resolved)) throw new SvgException(SvgErrorCode.LimitExceeded, "Percentage root dimensions exceed the admitted numeric range.");
        return resolved;
    }

    /// <summary>Returns the absolute pixel value, or null for viewport-relative percentages.</summary>
    internal double? ToAbsoluteOrNull() => IsPercentage ? null : Value;
}

internal readonly record struct SvgRootSyntax(SvgRootLength? Width, SvgRootLength? Height, SvgViewBox? ViewBox)
{
    internal static SvgRootSyntax Parse(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        // SVG 1.1 gives x and y no effect on the outermost svg element, so their
        // values are validated as finite lengths and then discarded. Nested svg
        // viewports keep their own offset handling.
        ValidateRootPosition(reader.GetAttribute("x"));
        ValidateRootPosition(reader.GetAttribute("y"));
        return new(SvgGeometrySyntax.ParseRootLength(reader.GetAttribute("width")), SvgGeometrySyntax.ParseRootLength(reader.GetAttribute("height")), SvgGeometrySyntax.ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio"), workCounter, cancellationToken));

        static void ValidateRootPosition(string? value)
        {
            if (value is null) return;
            ReadOnlySpan<char> token = value.AsSpan().Trim();
            if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Root position values exceed the configured syntax limit.");
            if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase)) token = token[..^2].TrimEnd();
            else if (token.Length > 0 && (char.IsLetter(token[^1]) || token[^1] == '%')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number and px root positions are implemented.");
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double position) || !double.IsFinite(position)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Root positions must be finite numbers.");
        }
    }
}
