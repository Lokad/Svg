namespace Lokad.Svg.Rendering;

/// <summary>Accumulates explicitly owned device-space contours for the shared coverage path.</summary>
/// <remarks>
/// <para>Callers map vertices through <see cref="SvgAffineTransform"/> before adding; this buffer stores
/// device points only and never transforms. Quadratic, cubic and arc segments flatten eagerly with the
/// caller policy, chaining <see cref="SvgCurveFlatten"/> and <see cref="SvgEllipticalArc"/>. Figures close
/// implicitly from their last point back to their start, matching the contour-topology convention.</para>
/// <para>Storage starts small and doubles toward the construction maxima; exhausting a maximum fails with
/// <see cref="SvgErrorCode.LimitExceeded"/>. There is nothing to dispose: the arrays are plain managed
/// memory. Callers charge one geometry work unit per stored point; this buffer holds no budget itself and
/// allocates nothing after construction. One conversion owns a builder; concurrent use is not supported.</para>
/// </remarks>
internal sealed class SvgContourBuilder
{
    private SvgDevicePoint[] _points;
    private int[] _starts;
    private int[] _ends;
    private int _maxPoints;
    private int _maxContours;
    private int _pointCount;
    private int _contourCount;
    private int _openStart = -1;
    private bool _retainSpans;
    private bool _retainCubicSpans;
    private int[] _cubicContours = Array.Empty<int>();
    private int[] _cubicStarts = Array.Empty<int>();
    private int[] _cubicEnds = Array.Empty<int>();
    private SvgDevicePoint[] _cubicFirstControls = Array.Empty<SvgDevicePoint>();
    private SvgDevicePoint[] _cubicSecondControls = Array.Empty<SvgDevicePoint>();
    private int _cubicCount;
    private int[] _quadContours = Array.Empty<int>();
    private int[] _quadStarts = Array.Empty<int>();
    private int[] _quadEnds = Array.Empty<int>();
    private SvgDevicePoint[] _quadControls = Array.Empty<SvgDevicePoint>();
    private int _quadCount;

    /// <summary>Starting point storage for growing builders, doubled toward the maxima.</summary>
    private const int InitialPointCapacity = 64;

    /// <summary>Starting contour storage for growing builders, doubled toward the maxima.</summary>
    private const int InitialContourCapacity = 4;

    /// <summary>Initializes a builder with fixed point and contour capacities.</summary>
    internal SvgContourBuilder(int pointCapacity, int contourCapacity)
    {
        if (pointCapacity < 1) throw new ArgumentOutOfRangeException(nameof(pointCapacity));
        if (contourCapacity < 1) throw new ArgumentOutOfRangeException(nameof(contourCapacity));
        _points = new SvgDevicePoint[pointCapacity];
        _starts = new int[contourCapacity];
        _ends = new int[contourCapacity];
        _maxPoints = pointCapacity;
        _maxContours = contourCapacity;
    }

    /// <summary>Creates a builder that grows from small storage toward bounded maxima.</summary>
    /// <remarks>Growth doubles toward the maxima with identical admission: past-maximum appends fail with the same errors as the fixed builder, and all stored points match. Callers charge per stored point exactly as before.</remarks>
    internal static SvgContourBuilder CreateGrowing(int maxPointCapacity, int maxContourCapacity)
    {
        if (maxPointCapacity < 1) throw new ArgumentOutOfRangeException(nameof(maxPointCapacity));
        if (maxContourCapacity < 1) throw new ArgumentOutOfRangeException(nameof(maxContourCapacity));
        SvgContourBuilder builder = new(Math.Min(maxPointCapacity, InitialPointCapacity), Math.Min(maxContourCapacity, InitialContourCapacity));
        builder._maxPoints = maxPointCapacity;
        builder._maxContours = maxContourCapacity;
        return builder;
    }

    /// <summary>Gets the current point storage length, growing toward the maximum.</summary>
    internal int PointCapacity => _points.Length;

    /// <summary>Gets the current contour storage length, growing toward the maximum.</summary>
    internal int ContourCapacity => _starts.Length;

    /// <summary>Gets the stored point count across open and sealed contours.</summary>
    internal int PointCount => _pointCount;

    /// <summary>Gets the sealed contour count.</summary>
    internal int ContourCount => _contourCount;

    /// <summary>Enables quadratic span retention for subsequent verbs.</summary>
    /// <remarks>Only the path-fill lowering enables retention before parsing; every other route keeps flatten-only behavior with no extra storage. Span arrays allocate lazily on the first recorded quad and double toward the point maximum, so quad-free paths pay nothing and dense quad paths stay within the point denial bound.</remarks>
    internal void EnableQuadSpanRetention()
    {
        _retainSpans = true;
    }

    /// <summary>Enables cubic span retention for subsequent verbs.</summary>
    /// <remarks>Same lazy storage discipline as quadratic spans; the path-fill lowering enables both together when curves are present.</remarks>
    internal void EnableCubicSpanRetention()
    {
        _retainCubicSpans = true;
    }

    /// <summary>Opens one contour seeded with its start point.</summary>
    internal void BeginContour(SvgDevicePoint start)
    {
        if (_openStart >= 0) throw new InvalidOperationException("A contour is already open.");
        if (_contourCount >= _starts.Length) GrowContours();
        ValidatePoint(start, nameof(start));
        Append(start);
        _openStart = _pointCount - 1;
    }

    /// <summary>Appends a straight segment from the current point to <paramref name="end"/>.</summary>
    internal void AddLine(SvgDevicePoint end)
    {
        RequireOpenContour();
        ValidatePoint(end, nameof(end));
        Append(end);
    }

    /// <summary>Appends the flattened quadratic segment from the current point to <paramref name="end"/>.</summary>
    internal void AddQuadratic(SvgDevicePoint control, SvgDevicePoint end, SvgFlattenPolicy policy, CancellationToken cancellationToken)
    {
        RequireOpenContour();
        SvgDevicePoint current = _points[_pointCount - 1];
        int before = _pointCount;
        while (true)
        {
            try
            {
                _pointCount += SvgCurveFlatten.FlattenQuadratic(current, control, end, policy, _points.AsSpan(_pointCount), cancellationToken);
                RecordQuadSpan(before - 1 - _openStart, _pointCount - 1 - _openStart, control);
                return;
            }
            catch (SvgException)
            {
                _pointCount = before;
                GrowPoints();
            }
        }
    }

    /// <summary>Records one quadratic span with contour-slice-relative indices when retention is enabled; the flattened points stay canonical.</summary>
    private void RecordQuadSpan(int start, int end, SvgDevicePoint control)
    {
        if (!_retainSpans) return;
        if (_quadContours.Length == 0)
        {
            _quadContours = new int[4];
            _quadStarts = new int[4];
            _quadEnds = new int[4];
            _quadControls = new SvgDevicePoint[4];
        }
        else if (_quadCount >= _quadContours.Length) GrowQuadSpans();
        _quadContours[_quadCount] = _contourCount;
        _quadStarts[_quadCount] = start;
        _quadEnds[_quadCount] = end;
        _quadControls[_quadCount] = control;
        _quadCount++;
    }

    /// <summary>Records one cubic span with contour-slice-relative indices when retention is enabled; the flattened points stay canonical.</summary>
    private void RecordCubicSpan(int start, int end, SvgDevicePoint firstControl, SvgDevicePoint secondControl)
    {
        if (!_retainCubicSpans) return;
        if (_cubicContours.Length == 0)
        {
            _cubicContours = new int[4];
            _cubicStarts = new int[4];
            _cubicEnds = new int[4];
            _cubicFirstControls = new SvgDevicePoint[4];
            _cubicSecondControls = new SvgDevicePoint[4];
        }
        else if (_cubicCount >= _cubicContours.Length) GrowCubicSpans();
        _cubicContours[_cubicCount] = _contourCount;
        _cubicStarts[_cubicCount] = start;
        _cubicEnds[_cubicCount] = end;
        _cubicFirstControls[_cubicCount] = firstControl;
        _cubicSecondControls[_cubicCount] = secondControl;
        _cubicCount++;
    }

    /// <summary>Appends the flattened cubic segment from the current point to <paramref name="end"/>.</summary>
    internal void AddCubic(SvgDevicePoint firstControl, SvgDevicePoint secondControl, SvgDevicePoint end, SvgFlattenPolicy policy, CancellationToken cancellationToken)
    {
        RequireOpenContour();
        SvgDevicePoint current = _points[_pointCount - 1];
        int before = _pointCount;
        while (true)
        {
            try
            {
                _pointCount += SvgCurveFlatten.FlattenCubic(current, firstControl, secondControl, end, policy, _points.AsSpan(_pointCount), cancellationToken);
                RecordCubicSpan(before - 1 - _openStart, _pointCount - 1 - _openStart, firstControl, secondControl);
                return;
            }
            catch (SvgException)
            {
                _pointCount = before;
                GrowPoints();
            }
        }
    }

    /// <summary>Appends the endpoint arc from the current point to <paramref name="end"/> as lines or samples.</summary>
    internal void AddArc(double radiusX, double radiusY, double rotationDegrees, bool largeArc, bool sweep, SvgDevicePoint end, SvgFlattenPolicy policy, CancellationToken cancellationToken)
    {
        RequireOpenContour();
        SvgDevicePoint current = _points[_pointCount - 1];
        SvgNormalizedArc arc = SvgEllipticalArc.Normalize(current, radiusX, radiusY, rotationDegrees, largeArc, sweep, end);
        if (arc.Kind == SvgArcKind.Line) Append(end);
        else while (true)
        {
            try
            {
                _pointCount += SvgEllipticalArc.Sample(arc, policy, _points.AsSpan(_pointCount), cancellationToken);
                return;
            }
            catch (SvgException)
            {
                GrowPoints();
            }
        }
    }

    /// <summary>Appends one sampled full-ellipse loop as a sealed contour.</summary>
    /// <remarks>
    /// <para>The ellipse stands alone: an open contour is a misuse. Corner radii and rotation arrive in
    /// the device frame under the caller transform, matching endpoint arcs; general-affine ellipse axes
    /// need user-space sampling owned by a later slice.</para>
    /// </remarks>
    internal void AddEllipse(SvgDevicePoint center, double radiusX, double radiusY, double rotationRadians, SvgFlattenPolicy policy, CancellationToken cancellationToken)
    {
        if (_openStart >= 0) throw new InvalidOperationException("A contour is already open.");
        if (_contourCount >= _starts.Length) GrowContours();
        ValidatePoint(center, nameof(center));
        if (!double.IsFinite(radiusX) || !double.IsFinite(radiusY) || radiusX <= 0 || radiusY <= 0) throw new ArgumentOutOfRangeException(nameof(radiusX));
        if (!double.IsFinite(rotationRadians)) throw new ArgumentOutOfRangeException(nameof(rotationRadians));
        int start = _pointCount;
        while (true)
        {
            try
            {
                _pointCount += SvgEllipticalArc.SampleFull(center, radiusX, radiusY, rotationRadians, policy, _points.AsSpan(_pointCount), cancellationToken);
                break;
            }
            catch (SvgException)
            {
                GrowPoints();
            }
        }
        _starts[_contourCount] = start;
        _ends[_contourCount] = _pointCount;
        _contourCount++;
    }

    /// <summary>Appends one axis-aligned rounded rectangle as a sealed contour, closing arc endpoint included.</summary>
    /// <remarks>
    /// <para>Corner radii clamp to half the side lengths; a zero radius falls back to sharp corners and a
    /// zero side draws nothing. Negative sizes or radii are caller contract violations.</para>
    /// </remarks>
    internal void AddRoundedRect(double x, double y, double width, double height, double cornerX, double cornerY, SvgFlattenPolicy policy, CancellationToken cancellationToken)
    {
        if (_openStart >= 0) throw new InvalidOperationException("A contour is already open.");
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height)) throw new ArgumentOutOfRangeException(nameof(x));
        if (!double.IsFinite(cornerX) || !double.IsFinite(cornerY) || cornerX < 0 || cornerY < 0) throw new ArgumentOutOfRangeException(nameof(cornerX));
        if (width < 0 || height < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (width == 0 || height == 0) return;
        if (_contourCount >= _starts.Length) GrowContours();
        double radiusX = Math.Min(cornerX, width / 2);
        double radiusY = Math.Min(cornerY, height / 2);
        BeginContour(new SvgDevicePoint(RequireFinite(x + radiusX), y));
        if (radiusX == 0 || radiusY == 0)
        {
            AddLine(new SvgDevicePoint(RequireFinite(x + width), y));
            AddLine(new SvgDevicePoint(RequireFinite(x + width), RequireFinite(y + height)));
            AddLine(new SvgDevicePoint(x, RequireFinite(y + height)));
        }
        else
        {
            AddLine(new SvgDevicePoint(RequireFinite(x + width - radiusX), y));
            AddArc(radiusX, radiusY, 0, false, true, new SvgDevicePoint(RequireFinite(x + width), RequireFinite(y + radiusY)), policy, cancellationToken);
            AddLine(new SvgDevicePoint(RequireFinite(x + width), RequireFinite(y + height - radiusY)));
            AddArc(radiusX, radiusY, 0, false, true, new SvgDevicePoint(RequireFinite(x + width - radiusX), RequireFinite(y + height)), policy, cancellationToken);
            AddLine(new SvgDevicePoint(RequireFinite(x + radiusX), RequireFinite(y + height)));
            AddArc(radiusX, radiusY, 0, false, true, new SvgDevicePoint(x, RequireFinite(y + height - radiusY)), policy, cancellationToken);
            AddLine(new SvgDevicePoint(x, RequireFinite(y + radiusY)));
            AddArc(radiusX, radiusY, 0, false, true, new SvgDevicePoint(RequireFinite(x + radiusX), y), policy, cancellationToken);
        }

        EndContour();
    }

    /// <summary>Seals the open contour, keeping its points for coverage.</summary>
    internal void EndContour()
    {
        RequireOpenContour();
        _starts[_contourCount] = _openStart;
        _ends[_contourCount] = _pointCount;
        _contourCount++;
        _openStart = -1;
    }

    /// <summary>Returns one sealed contour, including its start point but excluding any implicit closing duplicate.</summary>
    /// <remarks>The span borrows builder storage, so callers must not retain it across mutating calls that may relocate through growth.</remarks>
    internal ReadOnlySpan<SvgDevicePoint> GetContour(int index)
    {
        if (index < 0 || index >= _contourCount) throw new ArgumentOutOfRangeException(nameof(index));
        return _points.AsSpan(_starts[index], _ends[index] - _starts[index]);
    }

    /// <summary>Returns all recorded quadratic spans in record order. Empty when retention is disabled or no quads were added.</summary>
    internal SvgQuadSpan[] GetQuadSpans()
    {
        if (_quadCount == 0) return Array.Empty<SvgQuadSpan>();
        SvgQuadSpan[] spans = new SvgQuadSpan[_quadCount];
        for (int index = 0; index < _quadCount; index++)
        {
            spans[index] = new SvgQuadSpan(_quadContours[index], _quadStarts[index], _quadEnds[index], _quadControls[index]);
        }
        return spans;
    }

    /// <summary>Returns all recorded cubic spans in record order. Empty when retention is disabled or no cubics were added.</summary>
    internal SvgCubicSpan[] GetCubicSpans()
    {
        if (_cubicCount == 0) return Array.Empty<SvgCubicSpan>();
        SvgCubicSpan[] spans = new SvgCubicSpan[_cubicCount];
        for (int index = 0; index < _cubicCount; index++)
        {
            spans[index] = new SvgCubicSpan(_cubicContours[index], _cubicStarts[index], _cubicEnds[index], _cubicFirstControls[index], _cubicSecondControls[index]);
        }
        return spans;
    }

    /// <summary>Clears all points, contours and recorded spans while keeping the allocated storage.</summary>
    internal void Clear()
    {
        _pointCount = 0;
        _contourCount = 0;
        _openStart = -1;
        _quadCount = 0;
        _cubicCount = 0;
    }

    private void Append(SvgDevicePoint point)
    {
        if (_pointCount >= _points.Length) GrowPoints();
        _points[_pointCount] = point;
        _pointCount++;
    }

    /// <summary>Doubles point storage toward its maximum, failing past it exactly like fixed storage.</summary>
    private void GrowPoints()
    {
        if (_points.Length >= _maxPoints) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour storage exceeds the supported point range.");
        Array.Resize(ref _points, _points.Length > _maxPoints / 2 ? _maxPoints : _points.Length * 2);
    }

    /// <summary>Doubles span storage toward the point maximum, failing past it exactly like point storage.</summary>
    /// <remarks>Span records trail point appends, so the point cap always trips first on legitimate inputs; reaching this cap alone reports the same range error.</remarks>
    private void GrowQuadSpans()
    {
        if (_quadContours.Length >= _maxPoints) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour storage exceeds the supported point range.");
        int grown = _quadContours.Length > _maxPoints / 2 ? _maxPoints : _quadContours.Length * 2;
        Array.Resize(ref _quadContours, grown);
        Array.Resize(ref _quadStarts, grown);
        Array.Resize(ref _quadEnds, grown);
        Array.Resize(ref _quadControls, grown);
    }

    /// <summary>Doubles cubic span storage toward the point maximum, failing past it exactly like point storage.</summary>
    /// <remarks>Span records trail point appends, so the point cap always trips first on legitimate inputs; reaching this cap alone reports the same range error.</remarks>
    private void GrowCubicSpans()
    {
        if (_cubicContours.Length >= _maxPoints) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour storage exceeds the supported point range.");
        int grown = _cubicContours.Length > _maxPoints / 2 ? _maxPoints : _cubicContours.Length * 2;
        Array.Resize(ref _cubicContours, grown);
        Array.Resize(ref _cubicStarts, grown);
        Array.Resize(ref _cubicEnds, grown);
        Array.Resize(ref _cubicFirstControls, grown);
        Array.Resize(ref _cubicSecondControls, grown);
    }

    /// <summary>Doubles contour storage toward its maximum, failing past it exactly like fixed storage.</summary>
    private void GrowContours()
    {
        if (_starts.Length >= _maxContours) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour storage exceeds the supported contour range.");
        int grown = _starts.Length > _maxContours / 2 ? _maxContours : _starts.Length * 2;
        Array.Resize(ref _starts, grown);
        Array.Resize(ref _ends, grown);
    }

    private void RequireOpenContour()
    {
        if (_openStart < 0) throw new InvalidOperationException("No contour is open.");
    }

    private static void ValidatePoint(SvgDevicePoint point, string parameterName)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new ArgumentOutOfRangeException(parameterName);
    }

    private static double RequireFinite(double value)
    {
        if (!double.IsFinite(value)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour storage exceeds the admitted numeric range.");
        return value;
    }
}