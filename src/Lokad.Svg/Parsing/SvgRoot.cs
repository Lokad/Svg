namespace Lokad.Svg.Parsing;

/// <summary>Represents the parsed root viewport dimensions before scene construction.</summary>
internal readonly record struct SvgRoot(double? Width, double? Height, SvgRectangle[] Rectangles, bool HasUnsupportedChild);

internal readonly record struct SvgRectangle(int X, int Y, int Width, int Height, SvgColor Color);
