namespace Lokad.Svg.Parsing;

/// <summary>Normalizes finite fixed CSS lengths before coordinate-space resolution.</summary>
/// <remarks>Percentages and bare matrix coefficients retain their own domains. Pure bounded
/// arithmetic owns no storage; the parser observes cancellation while scanning tokens.</remarks>
internal static class SvgCssLength
{
    internal static float NormalizeFixed(double value) => (float)Math.Clamp(value, -33_554_430d, 33_554_429d);

    /// <summary>Splits a trimmed fixed CSS length into its contiguous number and CSS-pixel factor.
    /// Leaves numeric syntax, sign, finite range and float narrowing to the caller's domain.</summary>
    internal static bool TrySplitFixedUnit(ReadOnlySpan<char> token, out ReadOnlySpan<char> number, out double pixelsPerUnit)
    {
        number = token;
        pixelsPerUnit = 1;
        int suffix = 2;
        if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase)) { }
        else if (token.EndsWith("in", StringComparison.OrdinalIgnoreCase)) pixelsPerUnit = 96;
        else if (token.EndsWith("cm", StringComparison.OrdinalIgnoreCase)) pixelsPerUnit = 96d / 2.54d;
        else if (token.EndsWith("mm", StringComparison.OrdinalIgnoreCase)) pixelsPerUnit = 96d / 25.4d;
        else if (token.EndsWith("pt", StringComparison.OrdinalIgnoreCase)) pixelsPerUnit = 96d / 72d;
        else if (token.EndsWith("pc", StringComparison.OrdinalIgnoreCase)) pixelsPerUnit = 16;
        else if (token.EndsWith("q", StringComparison.OrdinalIgnoreCase)) { pixelsPerUnit = 96d / 101.6d; suffix = 1; }
        else suffix = 0;
        number = token[..(token.Length - suffix)];
        return number.IndexOfAny("%mivc".AsSpan()) < 0
            && (number.IsEmpty || !char.IsLetter(number[^1]) && number[^1] != '%');
    }
}
