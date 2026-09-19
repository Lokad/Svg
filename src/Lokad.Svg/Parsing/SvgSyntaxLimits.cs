namespace Lokad.Svg.Parsing;

/// <summary>Defines the fixed token bounds for the currently supported SVG syntax subset.</summary>
internal static class SvgSyntaxLimits
{
    internal const int MaximumIdentifierCharacters = 1024;
    internal const int MaximumValueTokenCharacters = 256;
    internal const int MaximumTransformCharacters = 4096;
}
