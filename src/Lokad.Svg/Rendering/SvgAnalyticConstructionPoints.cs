namespace Lokad.Svg.Rendering;

using Lokad.Svg.Parsing;

/// <summary>Borrows source operands for bitmap-local uncut edge construction.</summary>
/// <remarks>Classification keeps its globally mapped points. This view maps each needed
/// operand on demand without allocating a coordinate tape, and charges work before mapping.
/// The caller retains both immutable point sets and translates constructed fixed states
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

    internal bool IsBitmapLocal => _source is not null;

    internal SvgDevicePoint GetPoint(int index, ref long work, long maximumWork, CancellationToken token)
        => _source is {} source ? Map(source.Points[index], ref work, maximumWork, token) : _points[index];

    internal SvgQuadSpan GetQuad(SvgQuadSpan span, int index, ref long work, long maximumWork, CancellationToken token)
        => _source is {} source ? span with { Control = Map(source.QuadSpans[index].Control, ref work, maximumWork, token) } : span;

    internal SvgCubicSpan GetCubic(SvgCubicSpan span, int index, ref long work, long maximumWork, CancellationToken token)
        => _source is {} source ? span with
        {
            FirstControl = Map(source.CubicSpans[index].FirstControl, ref work, maximumWork, token),
            SecondControl = Map(source.CubicSpans[index].SecondControl, ref work, maximumWork, token),
        } : span;

    private SvgDevicePoint Map(SvgDevicePoint point, ref long work, long maximumWork, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (2 > maximumWork - work) throw new SvgException(SvgErrorCode.LimitExceeded, "Bitmap-local edge mapping exceeds the remaining work limit.");
        work += 2;
        return SvgAuthoredPath.MapPoint(_map, point, _origin);
    }
}
