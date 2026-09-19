namespace Lokad.Svg.Parsing;

/// <summary>Represents the parsed root viewport dimensions before scene construction.</summary>
internal readonly record struct SvgRoot(double? Width, double? Height, SvgViewBox? ViewBox, SvgRectangle[] Rectangles, bool HasUnsupportedChild);

/// <summary>Represents the supported root viewBox-to-viewport mapping.</summary>
internal readonly record struct SvgViewBox(double X, double Y, double Width, double Height, bool UsesUniformScale);

internal readonly record struct SvgRectangle(int X, int Y, int Width, int Height, SvgColor Color, bool HasFill);
