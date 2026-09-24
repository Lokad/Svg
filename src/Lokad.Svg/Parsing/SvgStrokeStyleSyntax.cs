using System.Globalization;
using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Validates the admitted no-op stroke style subset shared by attributes and style declarations.</summary>
internal static class SvgStrokeStyleSyntax
{
    /// <summary>Validates one stroke style attribute value, ignoring namespaces already cleared by the caller.</summary>
    internal static void ValidateAttribute(string localName, string value)
    {
        if (localName == "stroke-linecap") ValidateLinecap(value.AsSpan());
        else if (localName == "stroke-linejoin") ValidateLinejoin(value.AsSpan());
        else if (localName == "stroke-dasharray") ValidateDasharray(value.AsSpan());
        else if (localName == "stroke-dashoffset") ValidateDashoffset(value.AsSpan());
    }

    private static void ValidateLinecap(ReadOnlySpan<char> value)
    {
        if (value.Trim().Equals("butt".AsSpan(), StringComparison.OrdinalIgnoreCase)) return;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only butt stroke linecaps are implemented.");
    }

    private static void ValidateLinejoin(ReadOnlySpan<char> value)
    {
        if (value.Trim().Equals("miter".AsSpan(), StringComparison.OrdinalIgnoreCase)) return;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only miter stroke linejoins are implemented.");
    }

    private static void ValidateDashoffset(ReadOnlySpan<char> value)
    {
        ReadOnlySpan<char> token = value.Trim();
        if (token.EndsWith("px".AsSpan(), StringComparison.OrdinalIgnoreCase)) token = token[..^2].TrimEnd();
        else if (token.Length > 0 && (char.IsLetter(token[^1]) || token[^1] == '%')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number and px dash offsets are implemented.");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number and px dash offsets are implemented.");
    }

    private static void ValidateDasharray(ReadOnlySpan<char> value)
    {
        if (value.Trim().Equals("none".AsSpan(), StringComparison.OrdinalIgnoreCase)) return;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only solid (none) stroke dash arrays are implemented.");
    }

    /// <summary>Rejects paint-server strokes before color resolution, keeping solid colors and currentColor admitted.</summary>
    internal static void RejectPaintServerStroke(string? stroke, string elementName)
    {
        if (stroke is not null && SvgLocalUrl.TryParseId(stroke, out _)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern and gradient strokes on " + elementName + " elements are not implemented.");
    }

    /// <summary>Parses an even positive stroke width in the admitted calibration subset.</summary>
    internal static int ParseStrokeWidth(string? value, string elementName)
    {
        int strokeWidth = SvgGeometrySyntax.ParseInteger(value ?? "1");
        if (strokeWidth <= 0 || strokeWidth % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only even-width " + elementName + " strokes are implemented.");
        return strokeWidth;
    }

    /// <summary>Resolves one admitted solid butt-and-miter stroke, or nothing when the element carries no stroke paint.</summary>
    internal readonly record struct SvgAdmittedStroke(SvgColor Color, int Width, double MiterLimit);

    /// <summary>Validates the stroke subset shared by stroked shapes, rejecting paint servers, odd widths and small limits.</summary>
    internal static SvgAdmittedStroke? ResolveAdmittedStroke(XmlReader reader, SvgComputedStyle style, SvgResolvedPaintableElement shape, string elementName)
    {
        ArgumentNullException.ThrowIfNull(reader);
        RejectPaintServerStroke(style.Stroke, elementName);
        if (shape.Presentation.StrokeColor is null) return null;
        int width = ParseStrokeWidth(reader.GetAttribute("stroke-width"), elementName);
        double miterLimit = ParseMiterLimit(reader.GetAttribute("stroke-miterlimit"));
        SvgColor color = SvgPaintResolver.ApplyOpacity(shape.Presentation.StrokeColor.Value, shape.Presentation.Opacity);
        return new SvgAdmittedStroke(color, width, miterLimit);
    }

    /// <summary>Parses a miter limit of at least one, defaulting to four like SVG.</summary>
    internal static double ParseMiterLimit(string? value)
    {
        string token = value ?? "4";
        if (token.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke miter limit values exceed the configured syntax limit.");
        if (!double.TryParse(token.AsSpan().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double miterLimit) || !double.IsFinite(miterLimit) || miterLimit < 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only stroke miter limits of at least one are implemented.");
        return miterLimit;
    }
}
