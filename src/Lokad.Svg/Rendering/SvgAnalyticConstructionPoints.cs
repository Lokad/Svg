namespace Lokad.Svg.Rendering;

using Lokad.Svg.Parsing;

/// <summary>Borrows operands for bitmap-local uncut edge construction.</summary>
/// <remarks>Classification keeps its globally mapped points. This view maps each needed
/// source operand on demand, or shifts already mapped operands, without allocating a
/// coordinate tape. It charges work before mapping. The caller retains the immutable
/// point sets and translates constructed fixed states
/// back to canvas coordinates. The view owns no storage and cannot escape preparation.</remarks>
internal readonly ref struct SvgAnalyticConstructionPoints
{
    private readonly ReadOnlySpan<SvgDevicePoint> _points;
    private readonly SvgPreparedPathContours? _source;
    private readonly SvgAffineTransform _map;
    private readonly SvgCpuBitmapOrigin _origin;

    internal SvgAnalyticConstructionPoints(ReadOnlySpan<SvgDevicePoint> points, SvgPreparedPathContours? source,
        SvgAffineTransform map, SvgCpuBitmapOrigin origin)
    {
        _points = points;
        _source = origin.X == 0 && origin.Y == 0 ? null : source;
        _map = map;
        _origin = origin;
    }

    internal bool IsBitmapLocal => _origin.X != 0 || _origin.Y != 0;

    internal SvgDevicePoint GetPoint(int index, ref long work, long maximumWork, CancellationToken token)
        => IsBitmapLocal ? Map(_source is {} source ? source.Points[index] : _points[index], ref work, maximumWork, token) : _points[index];

    internal SvgQuadSpan GetQuad(SvgQuadSpan span, int index, ref long work, long maximumWork, CancellationToken token)
        => IsBitmapLocal ? span with { Control = Map(_source is {} source ? source.QuadSpans[index].Control : span.Control, ref work, maximumWork, token) } : span;

    internal SvgCubicSpan GetCubic(SvgCubicSpan span, int index, ref long work, long maximumWork, CancellationToken token)
    {
        if (!IsBitmapLocal) return span;
        SvgCubicSpan operands = _source is {} source ? source.CubicSpans[index] : span;
        return span with
        {
            FirstControl = Map(operands.FirstControl, ref work, maximumWork, token),
            SecondControl = Map(operands.SecondControl, ref work, maximumWork, token),
        };
    }

    private SvgDevicePoint Map(SvgDevicePoint point, ref long work, long maximumWork, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (2 > maximumWork - work) throw new SvgException(SvgErrorCode.LimitExceeded, "Bitmap-local edge mapping exceeds the remaining work limit.");
        work += 2;
        return _source is not null ? SvgAuthoredPath.MapPoint(_map, point, _origin)
            : new((float)point.X - _origin.X, (float)point.Y - _origin.Y);
    }
}
