using System.Globalization;
using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Parses the bounded root viewport syntax independently of root presentation resolution.</summary>
internal readonly record struct SvgRootSyntax(double? Width, double? Height, SvgViewBox? ViewBox)
{
    internal static SvgRootSyntax Parse(XmlReader reader)
    {
        // SVG 1.1 gives x and y no effect on the outermost svg element, so their
        // values are validated as finite lengths and then discarded. Nested svg
        // viewports keep their own offset handling.
        ValidateRootPosition(reader.GetAttribute("x"));
        ValidateRootPosition(reader.GetAttribute("y"));
        return new(SvgGeometrySyntax.ParseLength(reader.GetAttribute("width")), SvgGeometrySyntax.ParseLength(reader.GetAttribute("height")), SvgGeometrySyntax.ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio")));

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
