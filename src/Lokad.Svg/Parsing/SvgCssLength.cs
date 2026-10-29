namespace Lokad.Svg.Parsing;

/// <summary>Normalizes finite fixed CSS lengths before coordinate-space resolution.</summary>
/// <remarks>Percentages and bare matrix coefficients retain their own domains. Pure bounded
/// arithmetic owns no storage; the parser observes cancellation while scanning tokens.</remarks>
internal static class SvgCssLength
{
    internal static float NormalizeFixed(double value) => (float)Math.Clamp(value, -33_554_430d, 33_554_429d);
}
