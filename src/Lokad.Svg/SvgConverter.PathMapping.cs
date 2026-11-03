using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg;

public static partial class SvgConverter
{
    // Shared by complete-path preparation and context-bound stroke replay.
    // Retained source controls keep their native float map without grid snapping.
    private static SvgCubicSpan[] MapCubicSpanControls(SvgCubicSpan[] spans, double horizontalScale, double horizontalOffset, double verticalScale, double verticalOffset, int outputWidth, int outputHeight, out bool canvasContainsControls, SvgAffineTransform? authoredMap, CancellationToken cancellationToken)
    {
        canvasContainsControls = true;
        if (spans.Length == 0) return Array.Empty<SvgCubicSpan>();
        SvgCubicSpan[] mapped = new SvgCubicSpan[spans.Length];
        for (int index = 0; index < spans.Length; index++)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            SvgDevicePoint first = spans[index].FirstControl;
            SvgDevicePoint second = spans[index].SecondControl;
            SvgDevicePoint mappedFirst = authoredMap is {} firstMap ? SvgAuthoredPath.MapPoint(firstMap, first, new(0, 0)) : new(MapNativePolygonEdge(first.X, horizontalScale, horizontalOffset), MapNativePolygonEdge(first.Y, verticalScale, verticalOffset));
            SvgDevicePoint mappedSecond = authoredMap is {} secondMap ? SvgAuthoredPath.MapPoint(secondMap, second, new(0, 0)) : new(MapNativePolygonEdge(second.X, horizontalScale, horizontalOffset), MapNativePolygonEdge(second.Y, verticalScale, verticalOffset));
            canvasContainsControls &= mappedFirst.X >= 0 && mappedFirst.X <= outputWidth && mappedFirst.Y >= 0 && mappedFirst.Y <= outputHeight
                && mappedSecond.X >= 0 && mappedSecond.X <= outputWidth && mappedSecond.Y >= 0 && mappedSecond.Y <= outputHeight;
            mapped[index] = new SvgCubicSpan(spans[index].Contour, spans[index].StartIndex, spans[index].EndIndex, mappedFirst, mappedSecond);
        }
        return mapped;
    }

    private static SvgQuadSpan[] MapQuadSpanControls(SvgQuadSpan[] spans, double horizontalScale, double horizontalOffset, double verticalScale, double verticalOffset, int outputWidth, int outputHeight, bool nativeMapping, out bool canvasContainsControls, SvgAffineTransform? authoredMap, CancellationToken cancellationToken)
    {
        canvasContainsControls = true;
        if (spans.Length == 0) return Array.Empty<SvgQuadSpan>();
        SvgQuadSpan[] mapped = new SvgQuadSpan[spans.Length];
        for (int index = 0; index < spans.Length; index++)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            SvgDevicePoint control = spans[index].Control;
            bool native = nativeMapping || spans[index].ConicWeight != 1f;
            SvgDevicePoint mappedControl = authoredMap is {} map ? SvgAuthoredPath.MapPoint(map, control, new(0, 0)) : native
                ? new(MapNativePolygonEdge(control.X, horizontalScale, horizontalOffset), MapNativePolygonEdge(control.Y, verticalScale, verticalOffset))
                : new(MapEdge(control.X, horizontalScale, horizontalOffset), MapEdge(control.Y, verticalScale, verticalOffset));
            canvasContainsControls &= mappedControl.X >= 0 && mappedControl.X <= outputWidth && mappedControl.Y >= 0 && mappedControl.Y <= outputHeight;
            mapped[index] = spans[index] with { Control = mappedControl };
        }
        return mapped;
    }

    private static double MapEdge(double value, double scale, double offset)
    {
        double scaled = value * scale + offset;
        if (!double.IsFinite(scaled)) throw new SvgException(SvgErrorCode.LimitExceeded, "Output scaling and viewBox mapping exceed the admitted numeric range.");
        return scaled;
    }

    private static double MapNativePolygonEdge(double value, double scale, double offset)
    {
        double mapped = MapEdge(value, scale, offset);
        // SkMatrix::Scale_pts narrows each operand and rounds the product
        // before adding translation, including retained curve controls and endpoints.
        float native = (float)value * (float)scale + (float)offset;
        return float.IsFinite(native) ? native : mapped;
    }
}
