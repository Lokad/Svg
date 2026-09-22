using System.Globalization;

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
}
