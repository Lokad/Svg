namespace Lokad.Svg.Parsing;

/// <summary>Validates presentation values whose admitted meanings add no static painting behavior.</summary>
internal static class SvgInertPresentationSyntax
{
    internal static void ValidateMarker(ReadOnlySpan<char> value)
    {
        if (!value.Trim().Equals("none".AsSpan(), StringComparison.OrdinalIgnoreCase))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only none marker values are implemented.");
    }

    internal static void ValidateBackgroundAccumulation(ReadOnlySpan<char> value)
    {
        if (!value.Trim().Equals("accumulate".AsSpan(), StringComparison.OrdinalIgnoreCase))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only accumulate enable-background presentation values are implemented.");
    }
}
