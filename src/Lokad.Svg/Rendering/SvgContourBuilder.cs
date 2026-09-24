namespace Lokad.Svg.Rendering;

/// <summary>Accumulates explicitly owned device-space contours for the shared coverage path.</summary>
/// <remarks>
/// <para>Callers map vertices through <see cref="SvgAffineTransform"/> before adding; this buffer stores
/// device points only and never transforms. Quadratic, cubic and arc segments flatten eagerly with the
/// caller policy, chaining <see cref="SvgCurveFlatten"/> and <see cref="SvgEllipticalArc"/>. Figures close
/// implicitly from their last point back to their start, matching the contour-topology convention.</para>
/// <para>Storage is two fixed arrays sized at construction; exhausting either fails with
/// <see cref="SvgErrorCode.LimitExceeded"/>. There is nothing to dispose: the arrays are plain managed
/// memory. Callers charge one geometry work unit per stored point; this buffer holds no budget itself and
/// allocates nothing after construction. One conversion owns a builder; concurrent use is not supported.</para>
/// </remarks>
internal sealed class SvgContourBuilder
{
    private readonly SvgDevicePoint[] _points;
    private readonly int[] _starts;
    private readonly int[] _ends;
    private int _pointCount;
    private int _contourCount;
    private int _openStart = -1;

    /// <summary>Initializes a builder with fixed point and contour capacities.</summary>
    internal SvgContourBuilder(int pointCapacity, int contourCapacity)
    {
        if (pointCapacity < 1) throw new ArgumentOutOfRangeException(nameof(pointCapacity));
        if (contourCapacity < 1) throw new ArgumentOutOfRangeException(nameof(contourCapacity));
        _points = new SvgDevicePoint[pointCapacity];
        _starts = new int[contourCapacity];
        _ends = new int[contourCapacity];
    }

    /// <summary>Gets the fixed point capacity.</summary>
    internal int PointCapacity => _points.Length;

    /// <summary>Gets the fixed contour capacity.</summary>
    internal int ContourCapacity => _starts.Length;

    /// <summary>Gets the stored point count across open and sealed contours.</summary>
    internal int PointCount => _pointCount;

    /// <summary>Gets the sealed contour count.</summary>
    internal int ContourCount => _contourCount;

    /// <summary>Opens one contour seeded with its start point.</summary>
    internal void BeginContour(SvgDevicePoint start)
    {
        if (_openStart >= 0) throw new InvalidOperationException("A contour is already open.");
        if (_contourCount >= _starts.Length) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour storage exceeds the supported contour range.");
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
        _pointCount += SvgCurveFlatten.FlattenQuadratic(current, control, end, policy, _points.AsSpan(_pointCount), cancellationToken);
    }

    /// <summary>Appends the flattened cubic segment from the current point to <paramref name="end"/>.</summary>
    internal void AddCubic(SvgDevicePoint firstControl, SvgDevicePoint secondControl, SvgDevicePoint end, SvgFlattenPolicy policy, CancellationToken cancellationToken)
    {
        RequireOpenContour();
        SvgDevicePoint current = _points[_pointCount - 1];
        _pointCount += SvgCurveFlatten.FlattenCubic(current, firstControl, secondControl, end, policy, _points.AsSpan(_pointCount), cancellationToken);
    }

    /// <summary>Appends the endpoint arc from the current point to <paramref name="end"/> as lines or samples.</summary>
    internal void AddArc(double radiusX, double radiusY, double rotationDegrees, bool largeArc, bool sweep, SvgDevicePoint end, SvgFlattenPolicy policy, CancellationToken cancellationToken)
    {
        RequireOpenContour();
        SvgDevicePoint current = _points[_pointCount - 1];
        SvgNormalizedArc arc = SvgEllipticalArc.Normalize(current, radiusX, radiusY, rotationDegrees, largeArc, sweep, end);
        if (arc.Kind == SvgArcKind.Line) Append(end);
        else _pointCount += SvgEllipticalArc.Sample(arc, policy, _points.AsSpan(_pointCount), cancellationToken);
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
        if (_contourCount >= _starts.Length) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour storage exceeds the supported contour range.");
        ValidatePoint(center, nameof(center));
        if (!double.IsFinite(radiusX) || !double.IsFinite(radiusY) || radiusX <= 0 || radiusY <= 0) throw new ArgumentOutOfRangeException(nameof(radiusX));
        if (!double.IsFinite(rotationRadians)) throw new ArgumentOutOfRangeException(nameof(rotationRadians));
        int start = _pointCount;
        _pointCount += SvgEllipticalArc.SampleFull(center, radiusX, radiusY, rotationRadians, policy, _points.AsSpan(_pointCount), cancellationToken);
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
        if (_contourCount >= _starts.Length) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour storage exceeds the supported contour range.");
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
    internal ReadOnlySpan<SvgDevicePoint> GetContour(int index)
    {
        if (index < 0 || index >= _contourCount) throw new ArgumentOutOfRangeException(nameof(index));
        return _points.AsSpan(_starts[index], _ends[index] - _starts[index]);
    }

    /// <summary>Clears all points and contours while keeping the allocated storage.</summary>
    internal void Clear()
    {
        _pointCount = 0;
        _contourCount = 0;
        _openStart = -1;
    }

    private void Append(SvgDevicePoint point)
    {
        if (_pointCount >= _points.Length) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour storage exceeds the supported point range.");
        _points[_pointCount] = point;
        _pointCount++;
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