namespace Lokad.Svg.Parsing;

using Lokad.Svg.Rendering;

/// <summary>Represents the parsed root viewport dimensions before scene construction.</summary>
internal readonly record struct SvgRoot(double? Width, double? Height, SvgViewBox? ViewBox, SvgRectangle[] Rectangles, SvgDrawCommand[] Commands, bool HasUnsupportedChild, RenderBudget.BudgetReservation? SceneReservation) : IDisposable
{
    public void Dispose() => SceneReservation?.Dispose();
}

/// <summary>Represents the supported root viewBox-to-viewport mapping.</summary>
internal readonly record struct SvgViewBox(double X, double Y, double Width, double Height, SvgViewBoxAlignment HorizontalAlignment, SvgViewBoxAlignment VerticalAlignment, SvgViewBoxFit Fit);

internal enum SvgViewBoxAlignment { Min, Mid, Max, None }

internal enum SvgViewBoxFit { Meet, Slice, None }

internal readonly record struct SvgRectangle(double X, double Y, double Width, double Height, SvgColor Color, bool HasFill, SvgStroke? Stroke);

internal readonly record struct SvgStroke(SvgColor Color, int Width);

internal enum SvgDrawCommandKind { Rectangle, BeginGroup, EndGroup }

internal readonly record struct SvgDrawCommand(SvgDrawCommandKind Kind, SvgRectangle Rectangle, byte Opacity);

internal readonly record struct SvgPoint(int X, int Y);

internal static class SvgSceneMemory
{
    private const int ArrayHeaderBytes = 32;
    private const int RectangleBytes = 56;
    private const int DrawCommandBytes = 72;

    internal static long GetArrayByteCount(int rectangleCount, int commandCount)
        => checked((rectangleCount == 0 ? 0 : ArrayHeaderBytes + (long)rectangleCount * RectangleBytes) + (commandCount == 0 ? 0 : ArrayHeaderBytes + (long)commandCount * DrawCommandBytes));
}
