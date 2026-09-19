namespace Lokad.Svg.Parsing;

/// <summary>Represents the parsed root viewport dimensions before scene construction.</summary>
internal readonly record struct SvgRoot(double? Width, double? Height, bool HasChildElement);
