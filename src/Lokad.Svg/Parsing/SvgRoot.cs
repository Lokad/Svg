namespace Lokad.Svg.Parsing;

/// <summary>Represents the parsed root viewport dimensions before scene construction.</summary>
internal readonly record struct SvgRoot(double? Width, double? Height, SvgViewBox? ViewBox, SvgRectangle[] Rectangles, SvgDrawCommand[] Commands, bool HasUnsupportedChild);

/// <summary>Represents the supported root viewBox-to-viewport mapping.</summary>
internal readonly record struct SvgViewBox(double X, double Y, double Width, double Height, SvgViewBoxAlignment HorizontalAlignment, SvgViewBoxAlignment VerticalAlignment, SvgViewBoxFit Fit);

internal enum SvgViewBoxAlignment { Min, Mid, Max, None }

internal enum SvgViewBoxFit { Meet, Slice, None }

internal readonly record struct SvgRectangle(double X, double Y, double Width, double Height, SvgColor Color, bool HasFill, SvgStroke? Stroke = null);

internal readonly record struct SvgStroke(SvgColor Color, int Width);

internal enum SvgDrawCommandKind { Rectangle, BeginGroup, EndGroup }

internal readonly record struct SvgDrawCommand(SvgDrawCommandKind Kind, SvgRectangle Rectangle, byte Opacity);

internal readonly record struct SvgPoint(int X, int Y);
