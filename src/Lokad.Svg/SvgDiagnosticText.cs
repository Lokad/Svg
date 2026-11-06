namespace Lokad.Svg;

/// <summary>Bounds retained diagnostic text without splitting a complete UTF-16 surrogate pair.</summary>
internal static class SvgDiagnosticText
{
    private const int MaximumLength = 256;

    internal static string Bound(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length <= MaximumLength) return value;
        int length = MaximumLength;
        if (char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length])) length--;
        return value[..length];
    }
}
