using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one vertical orthogonal edge crossing with its nonzero winding contribution.</summary>
internal readonly record struct SvgOrthogonalEdgeIntersection(int X, int WindingDelta) : IComparable<SvgOrthogonalEdgeIntersection>
{
    public int CompareTo(SvgOrthogonalEdgeIntersection other)
    {
        int order = X.CompareTo(other.X);
        return order != 0 ? order : WindingDelta.CompareTo(other.WindingDelta);
    }
}

/// <summary>Owns temporary orthogonal-contour lowering storage until retained rectangle bands detach.</summary>
internal ref struct SvgOrthogonalGeometryLowering
{
    internal const double FlattenFlatness = 0.25;

    /// <summary>Collapses consecutive device-space joints within this Chebyshev distance during fill lowering.</summary>
    /// <remarks>Curve endpoints that agree in source can diverge by mapping dust (observed 1e-13) once the finite-double affine map applies different operation orders to flattened-curve ends versus directly mapped starts. The bound exceeds worst-case divergence for admitted coordinates (a handful of roundings at magnitude 32767 with unit roundoff 2^-53, totalling below 1e-10) while sitting far below the 1/256 coverage quantum, hence invisibly.</remarks>
    private const double WeldEpsilon = 1e-9;

    /// <summary>Scales flattening tolerance down with root and output magnification, never coarsening past the nominal flatness.</summary>
    internal static double ScaledFlatness(double outputMagnification)
        => outputMagnification <= 1 ? FlattenFlatness : FlattenFlatness / outputMagnification;
    internal const int FlattenMaxDepth = 64;
    internal const int CurveSegmentsPerCommand = 1024;
    private const int ShapeSegmentsPerShape = 4096;
    internal const int MaximumFlattenedContourPoints = 65536;

    /// <summary>Bounds sampling storage for shape outlines: each sampled loop appends at most the policy segment cap, plus line-vertex headroom.</summary>
    internal static int SampledOutlineCapacity(int loops, int maximumSegments)
        => checked(loops * (maximumSegments + 1) + 8);

    private readonly BudgetedParserList<SvgPoint> _points;
    private readonly BudgetedParserList<int> _contourEnds;
    private readonly string _elementName;
    private readonly bool _isPath;
    private readonly bool _isEvenOdd;
    private readonly int _maximumGeometryElements;
    private readonly ParserWorkCounter _workCounter;
    private readonly CancellationToken _cancellationToken;
    private readonly BudgetedParserList<int> _boundaries;
    private readonly BudgetedParserList<SvgOrthogonalEdgeIntersection> _intersections;
    private readonly BudgetedParserList<SvgGeometry> _geometries;
    private int _geometryElementCount;

    internal SvgOrthogonalGeometryLowering(BudgetedParserList<SvgPoint> points, BudgetedParserList<int> contourEnds, string elementName, bool isPath, bool isEvenOdd, int maximumGeometryElements, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(contourEnds);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(workCounter);
        _points = points;
        _contourEnds = contourEnds;
        _elementName = elementName;
        _isPath = isPath;
        _isEvenOdd = isEvenOdd;
        _maximumGeometryElements = maximumGeometryElements;
        _workCounter = workCounter;
        _cancellationToken = cancellationToken;
        _geometryElementCount = points.Count;
        _boundaries = new BudgetedParserList<int>(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
        try
        {
            for (int pointIndex = 0; pointIndex < points.Count; pointIndex++) _boundaries.Add(points[pointIndex].Y);
            _boundaries.Sort(_workCounter, _cancellationToken);
            _intersections = new BudgetedParserList<SvgOrthogonalEdgeIntersection>(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            try
            {
                _geometries = new BudgetedParserList<SvgGeometry>(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            }
            catch
            {
                _intersections.Dispose();
                throw;
            }
        }
        catch
        {
            _boundaries.Dispose();
            throw;
        }
    }

    internal SvgParsedGeometry Lower(SvgColor color, bool hasFill, SvgAffineTransform transform, SvgViewportFrame? frame)
    {
        for (int boundaryIndex = 0; boundaryIndex < _boundaries.Count;)
        {
            int top = _boundaries[boundaryIndex];
            while (boundaryIndex < _boundaries.Count && _boundaries[boundaryIndex] == top) boundaryIndex++;
            if (boundaryIndex == _boundaries.Count) break;
            int bottom = _boundaries[boundaryIndex];
            _intersections.Clear();
            int contourStart = 0;
            for (int contourIndex = 0; contourIndex < _contourEnds.Count; contourIndex++)
            {
                int contourEnd = _contourEnds[contourIndex];
                for (int pointIndex = contourStart; pointIndex < contourEnd; pointIndex++)
                {
                    _workCounter.Consume(_cancellationToken);
                    SvgPoint start = _points[pointIndex]; SvgPoint end = pointIndex + 1 < contourEnd ? _points[pointIndex + 1] : _points[contourStart];
                    if ((start.X == end.X) == (start.Y == end.Y)) throw new SvgException(SvgErrorCode.UnsupportedFeature, SimpleShapeMessage());
                    if (start.X != end.X) continue;
                    if (Math.Min(start.Y, end.Y) <= top && Math.Max(start.Y, end.Y) >= bottom) _intersections.Add(new SvgOrthogonalEdgeIntersection(start.X, end.Y > start.Y ? 1 : -1));
                }
                contourStart = contourEnd;
            }
            _intersections.Sort(_workCounter, _cancellationToken);
            if (_intersections.Count == 0) continue;
            if (_isEvenOdd)
            {
                int intersectionIndex = 0;
                while (intersectionIndex < _intersections.Count)
                {
                    if (intersectionIndex + 1 < _intersections.Count && _intersections[intersectionIndex].X == _intersections[intersectionIndex + 1].X)
                    {
                        intersectionIndex += 2;
                        continue;
                    }
                    if (intersectionIndex + 1 >= _intersections.Count) throw new SvgException(SvgErrorCode.UnsupportedFeature, SimpleShapeMessage());
                    int left = _intersections[intersectionIndex].X; int right = _intersections[intersectionIndex + 1].X;
                    if (right <= left) throw new SvgException(SvgErrorCode.UnsupportedFeature, SimpleShapeMessage());
                    if (_geometryElementCount >= _maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, _isPath ? "Path rectangle-band count exceeds the configured path-command limit." : _elementName + " rectangle-band count exceeds the configured path-command limit.");
                    SvgRectangle band = transform.ApplyOrthogonalRectangle(left, top, (long)right - left, (long)bottom - top, color, hasFill);
                    _geometries.Add(SvgGeometry.FromOrthogonalBand(frame is null ? band : frame.Value.ClipShifted(band)));
                    _geometryElementCount++;
                    intersectionIndex += 2;
                }
            }
            else
            {
                int winding = 0;
                int intersectionIndex = 0;
                while (intersectionIndex < _intersections.Count)
                {
                    int left = _intersections[intersectionIndex].X;
                    int groupDelta = 0;
                    while (intersectionIndex < _intersections.Count && _intersections[intersectionIndex].X == left)
                    {
                        groupDelta += _intersections[intersectionIndex].WindingDelta;
                        intersectionIndex++;
                    }
                    winding += groupDelta;
                    if (intersectionIndex >= _intersections.Count) break;
                    int right = _intersections[intersectionIndex].X;
                    if (winding != 0)
                    {
                        if (right <= left) throw new SvgException(SvgErrorCode.UnsupportedFeature, SimpleShapeMessage());
                        if (_geometryElementCount >= _maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, _isPath ? "Path rectangle-band count exceeds the configured path-command limit." : _elementName + " rectangle-band count exceeds the configured path-command limit.");
                        SvgRectangle band = transform.ApplyOrthogonalRectangle(left, top, (long)right - left, (long)bottom - top, color, hasFill);
                        _geometries.Add(SvgGeometry.FromOrthogonalBand(frame is null ? band : frame.Value.ClipShifted(band)));
                        _geometryElementCount++;
                    }
                }
                if (winding != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, SimpleShapeMessage());
            }
        }
        try { return new SvgParsedGeometry(_geometries.Detach(), _geometryElementCount); }
        catch { foreach (SvgGeometry geometry in _geometries.AsSpan()) geometry.Dispose(); throw; }
    }

    /// <summary>Lowers diagonal solid polygons with separated simple contours to retained device points, keeping touching and non-simple shapes rejected.</summary>
    internal SvgParsedGeometry LowerPolygonFill(SvgColor color, bool hasFill, SvgAffineTransform transform, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        using BudgetedParserList<SvgDevicePoint> mapped = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        int[] ends = MapDiagonalContours(transform, frame, mapped);
        return FinishPolygonFill(mapped, ends, color, hasFill, SvgPolygonMapping.QuarterEdges, frame, budget);
    }

    /// <summary>Lowers diagonal linear-gradient polygons with separated simple contours to retained device points with their paint box.</summary>
    internal SvgParsedGeometry LowerGradientPolygonFill(SvgLinearGradient gradient, SvgRectangle box, bool hasFill, byte opacity, SvgAffineTransform transform, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        using BudgetedParserList<SvgDevicePoint> mapped = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        int[] ends = MapDiagonalContours(transform, frame, mapped);
        return FinishGradientPolygonFill(mapped, ends, gradient, box, hasFill, opacity, SvgPolygonMapping.QuarterEdges, frame, budget);
    }

    /// <summary>Lowers diagonal radial-gradient polygons with separated simple contours to retained device points with their paint box.</summary>
    internal SvgParsedGeometry LowerRadialPolygonFill(SvgRadialGradient gradient, SvgRectangle box, bool hasFill, byte opacity, SvgAffineTransform transform, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        using BudgetedParserList<SvgDevicePoint> mapped = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        int[] ends = MapDiagonalContours(transform, frame, mapped);
        return FinishRadialPolygonFill(mapped, ends, gradient, box, hasFill, opacity, SvgPolygonMapping.QuarterEdges, frame, budget);
    }

    private int[] MapDiagonalContours(SvgAffineTransform transform, SvgViewportFrame? frame, BudgetedParserList<SvgDevicePoint> mapped)
    {
        ArgumentNullException.ThrowIfNull(mapped);
        if (_contourEnds.Count == 0 || _contourEnds[_contourEnds.Count - 1] != _points.Count) throw new SvgException(SvgErrorCode.UnsupportedFeature, SimpleShapeMessage());
        if (_geometryElementCount >= _maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, _isPath ? "Path polygon point count exceeds the configured path-command limit." : _elementName + " polygon point count exceeds the configured path-command limit.");
        int[] ends = new int[_contourEnds.Count];
        int contourStart = 0;
        for (int contour = 0; contour < _contourEnds.Count; contour++)
        {
            int contourEnd = _contourEnds[contour];
            if (contourEnd <= contourStart) throw new SvgException(SvgErrorCode.UnsupportedFeature, SimpleShapeMessage());
            SvgPoint previous = default;
            SvgPoint first = default;
            bool hasKept = false;
            int keptBefore = mapped.Count;
            for (int index = contourStart; index < contourEnd; index++)
            {
                _workCounter.Consume(_cancellationToken);
                SvgPoint vertex = _points[index];
                if (hasKept && previous == vertex) continue;
                if (hasKept && vertex == first && index == contourEnd - 1) continue;
                if (!hasKept) first = vertex;
                previous = vertex;
                hasKept = true;
                SvgDevicePoint mappedVertex = transform.Apply(vertex.X, vertex.Y);
                mapped.Add(ShiftVertex(frame, mappedVertex.X, mappedVertex.Y));
            }

            if (mapped.Count - keptBefore < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, SimpleShapeMessage());
            ends[contour] = mapped.Count;
            contourStart = contourEnd;
        }

        return ends;
    }

    private static SvgDevicePoint ShiftVertex(SvgViewportFrame? viewport, double x, double y)
        => viewport is null ? new SvgDevicePoint(x, y) : new SvgDevicePoint(x * viewport.Value.ScaleX + viewport.Value.OffsetX, y * viewport.Value.ScaleY + viewport.Value.OffsetY);

    private static (SvgAffineTransform Map, double ScaleX, double ScaleY) BuildDeviceMap(SvgAffineTransform transform, SvgViewportFrame? frame)
    {
        double frameScaleX = frame?.ScaleX ?? 1;
        double frameScaleY = frame?.ScaleY ?? 1;
        double frameOffsetX = frame?.OffsetX ?? 0;
        double frameOffsetY = frame?.OffsetY ?? 0;
        if (!double.IsFinite(frameScaleX) || !double.IsFinite(frameScaleY) || !double.IsFinite(frameOffsetX) || !double.IsFinite(frameOffsetY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Viewport mapping exceeds the admitted numeric range.");
        double userScaleX;
        double userScaleY;
        if (transform.IsAxisAligned)
        {
            userScaleX = transform.A;
            userScaleY = transform.D;
        }
        else if (!transform.TryGetUniformScale(out double uniformUserScale)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only uniformly scaled polygon transforms are implemented.");
        else
        {
            userScaleX = uniformUserScale;
            userScaleY = uniformUserScale;
        }
        double scaleX = userScaleX * frameScaleX;
        double scaleY = userScaleY * frameScaleY;
        SvgAffineTransform deviceMap = SvgAffineTransform.FromMatrix(frameScaleX, 0, 0, frameScaleY, frameOffsetX, frameOffsetY).Compose(transform);
        return (deviceMap, scaleX, scaleY);
    }

    private static int AppendStrippedContour(BudgetedParserList<SvgDevicePoint> mapped, ReadOnlySpan<SvgDevicePoint> source, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mapped);
        ArgumentNullException.ThrowIfNull(workCounter);
        SvgDevicePoint previous = default;
        SvgDevicePoint first = default;
        bool hasKept = false;
        int keptBefore = mapped.Count;
        for (int index = 0; index < source.Length; index++)
        {
            workCounter.Consume(cancellationToken);
            SvgDevicePoint vertex = source[index];
            if (hasKept && previous == vertex) continue;
            if (hasKept && vertex == first && index == source.Length - 1) continue;
            if (!hasKept) first = vertex;
            previous = vertex;
            hasKept = true;
            mapped.Add(vertex);
        }

        return mapped.Count - keptBefore;
    }

    /// <summary>Lowers one axis-aligned ellipse or circle to a sampled polygon fill.</summary>
    internal SvgParsedGeometry LowerEllipseFill(double centerX, double centerY, double radiusX, double radiusY, SvgResolvedPaintableElement element, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        if (radiusX != radiusY && !element.Transform.IsAxisAligned) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rotated ellipse transforms are not implemented.");
        (SvgAffineTransform deviceMap, double scaleX, double scaleY) = BuildDeviceMap(element.Transform, frame);
        double mappedRadiusX = radiusX * Math.Abs(scaleX);
        double mappedRadiusY = radiusY * Math.Abs(scaleY);
        using BudgetedParserList<SvgDevicePoint> mapped = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        int[] ends;
        if (mappedRadiusX > 0 && mappedRadiusY > 0)
        {
            SvgDevicePoint center = deviceMap.Apply(centerX, centerY);
            SvgFlattenPolicy flattenPolicy = new(ScaledFlatness(element.OutputMagnification), FlattenMaxDepth, ShapeSegmentsPerShape);
            using RenderBudget.BudgetReservation scratch = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(SampledOutlineCapacity(1, ShapeSegmentsPerShape)) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(1), SvgMemoryCategory.Geometry);
            SvgContourBuilder builder = new(SampledOutlineCapacity(1, ShapeSegmentsPerShape), 1);
            builder.AddEllipse(center, mappedRadiusX, mappedRadiusY, 0, flattenPolicy, _cancellationToken);
            ends = new int[builder.ContourCount];
            for (int contour = 0; contour < builder.ContourCount; contour++)
            {
                if (AppendStrippedContour(mapped, builder.GetContour(contour), _workCounter, _cancellationToken) < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer path contours are implemented.");
                ends[contour] = mapped.Count;
            }
        }
        else ends = [];

        return FinishPolygonFill(mapped, ends, element.Presentation.Color, element.Presentation.HasFill, SvgPolygonMapping.FlattenedCurves, frame, budget);
    }

    /// <summary>Lowers one circle stroke ring to two opposite-wound sampled loops sharing one nonzero fill.</summary>
    internal SvgParsedGeometry LowerCircleStrokeFill(double centerX, double centerY, double radius, SvgResolvedPaintableElement element, SvgStroke stroke, bool nonScaling, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        (SvgAffineTransform deviceMap, double scaleX, double scaleY) = BuildDeviceMap(element.Transform, frame);
        if (scaleX != scaleY || scaleX <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Circle strokes under non-uniform polygon transforms stay unsupported.");
        double deviceRadius = radius * scaleX;
        double deviceWidth = nonScaling ? stroke.Width : stroke.Width * scaleX;
        if (!double.IsFinite(deviceRadius) || !double.IsFinite(deviceWidth)) throw new SvgException(SvgErrorCode.LimitExceeded, "Circle stroke coordinates exceed the admitted numeric range.");
        using BudgetedParserList<SvgDevicePoint> mapped = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        int[] ends;
        if (deviceRadius <= 0) ends = [];
        else
        {
            double outerRadius = deviceRadius + deviceWidth / 2;
            double innerRadius = deviceRadius - deviceWidth / 2;
            SvgDevicePoint center = deviceMap.Apply(centerX, centerY);
            SvgFlattenPolicy flattenPolicy = new(ScaledFlatness(element.OutputMagnification), FlattenMaxDepth, ShapeSegmentsPerShape);
            using RenderBudget.BudgetReservation scratch = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(SampledOutlineCapacity(2, ShapeSegmentsPerShape)) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(2), SvgMemoryCategory.Geometry);
            SvgContourBuilder builder = new(SampledOutlineCapacity(2, ShapeSegmentsPerShape), 2);
            builder.AddEllipse(center, outerRadius, outerRadius, 0, flattenPolicy, _cancellationToken);
            if (innerRadius > 0) builder.AddEllipse(center, innerRadius, innerRadius, 0, flattenPolicy, _cancellationToken);
            ends = new int[builder.ContourCount];
            for (int contour = 0; contour < builder.ContourCount; contour++)
            {
                if (AppendStrippedContour(mapped, builder.GetContour(contour), _workCounter, _cancellationToken) < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed circle stroke contours are implemented.");
                ends[contour] = mapped.Count;
            }

            if (ends.Length == 2)
            {
                for (int left = ends[0], right = ends[1] - 1; left < right; left++, right--)
                {
                    _workCounter.Consume(_cancellationToken);
                    (mapped[left], mapped[right]) = (mapped[right], mapped[left]);
                }
            }
        }

        return FinishPolygonFill(mapped, ends, stroke.Color, element.Presentation.IsPainted, SvgPolygonMapping.FlattenedCurves, frame, budget);
    }

    /// <summary>Lowers one axis-aligned ellipse stroke into opposite-wound sampled ring fills.</summary>
    internal SvgParsedGeometry LowerEllipseStrokeFill(double centerX, double centerY, double radiusX, double radiusY, SvgResolvedPaintableElement element, SvgStroke stroke, bool nonScaling, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        if (radiusX != radiusY && !element.Transform.IsAxisAligned) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rotated ellipse transforms are not implemented.");
        (SvgAffineTransform deviceMap, double scaleX, double scaleY) = BuildDeviceMap(element.Transform, frame);
        if (scaleX != scaleY || scaleX <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Ellipse strokes under non-uniform polygon transforms stay unsupported.");
        double deviceRadiusX = radiusX * scaleX;
        double deviceRadiusY = radiusY * scaleX;
        double deviceWidth = nonScaling ? stroke.Width : stroke.Width * scaleX;
        if (!double.IsFinite(deviceRadiusX) || !double.IsFinite(deviceRadiusY) || !double.IsFinite(deviceWidth)) throw new SvgException(SvgErrorCode.LimitExceeded, "Ellipse stroke coordinates exceed the admitted numeric range.");
        using BudgetedParserList<SvgDevicePoint> mapped = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        int[] ends;
        if (deviceRadiusX <= 0 || deviceRadiusY <= 0) ends = [];
        else
        {
            double outerRadiusX = deviceRadiusX + deviceWidth / 2;
            double outerRadiusY = deviceRadiusY + deviceWidth / 2;
            double innerRadiusX = deviceRadiusX - deviceWidth / 2;
            double innerRadiusY = deviceRadiusY - deviceWidth / 2;
            SvgDevicePoint center = deviceMap.Apply(centerX, centerY);
            SvgFlattenPolicy flattenPolicy = new(ScaledFlatness(element.OutputMagnification), FlattenMaxDepth, ShapeSegmentsPerShape);
            using RenderBudget.BudgetReservation scratch = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(SampledOutlineCapacity(2, ShapeSegmentsPerShape)) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(2), SvgMemoryCategory.Geometry);
            SvgContourBuilder builder = new(SampledOutlineCapacity(2, ShapeSegmentsPerShape), 2);
            builder.AddEllipse(center, outerRadiusX, outerRadiusY, 0, flattenPolicy, _cancellationToken);
            if (innerRadiusX > 0 && innerRadiusY > 0) builder.AddEllipse(center, innerRadiusX, innerRadiusY, 0, flattenPolicy, _cancellationToken);
            ends = new int[builder.ContourCount];
            for (int contour = 0; contour < builder.ContourCount; contour++)
            {
                if (AppendStrippedContour(mapped, builder.GetContour(contour), _workCounter, _cancellationToken) < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed ellipse stroke contours are implemented.");
                ends[contour] = mapped.Count;
            }

            if (ends.Length == 2)
            {
                for (int left = ends[0], right = ends[1] - 1; left < right; left++, right--)
                {
                    _workCounter.Consume(_cancellationToken);
                    (mapped[left], mapped[right]) = (mapped[right], mapped[left]);
                }
            }
        }

        return FinishPolygonFill(mapped, ends, stroke.Color, element.Presentation.IsPainted, SvgPolygonMapping.FlattenedCurves, frame, budget);
    }

    /// <summary>Lowers one axis-aligned rounded rectangle to a sampled polygon fill.</summary>
    internal SvgParsedGeometry LowerRoundedRectFill(double x, double y, double width, double height, double radiusX, double radiusY, SvgResolvedPaintableElement element, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        (_, double scaleX, double scaleY) = BuildDeviceMap(element.Transform, frame);
        SvgRectangle placed = element.Transform.Apply(new SvgRectangle(x, y, width, height, default, false, null));
        SvgRectangle shifted = frame is null ? placed : frame.Value.Shift(placed);
        double mappedRadiusX = radiusX * Math.Abs(scaleX);
        double mappedRadiusY = radiusY * Math.Abs(scaleY);
        SvgFlattenPolicy flattenPolicy = new(ScaledFlatness(element.OutputMagnification), FlattenMaxDepth, ShapeSegmentsPerShape);
        using RenderBudget.BudgetReservation scratch = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(SampledOutlineCapacity(4, ShapeSegmentsPerShape)) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(1), SvgMemoryCategory.Geometry);
        SvgContourBuilder builder = new(SampledOutlineCapacity(4, ShapeSegmentsPerShape), 1);
        builder.AddRoundedRect(shifted.X, shifted.Y, shifted.Width, shifted.Height, mappedRadiusX, mappedRadiusY, flattenPolicy, _cancellationToken);
        using BudgetedParserList<SvgDevicePoint> mapped = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        int[] ends = new int[builder.ContourCount];
        for (int contour = 0; contour < builder.ContourCount; contour++)
        {
            if (AppendStrippedContour(mapped, builder.GetContour(contour), _workCounter, _cancellationToken) < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer path contours are implemented.");
            ends[contour] = mapped.Count;
        }

        return FinishPolygonFill(mapped, ends, element.Presentation.Color, element.Presentation.HasFill, SvgPolygonMapping.FlattenedCurves, frame, budget);
    }

    /// <summary>Lowers device-space path data through affine flattening into one polygon fill with the caller mapping discipline.</summary>
    internal SvgParsedGeometry LowerDeviceFill(string d, int commandCount, bool hasArc, bool hasCurve, SvgPolygonMapping mapping, SvgResolvedPaintableElement element, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        if (commandCount >= _maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, "Path command count exceeds the configured path-command limit.");
        (SvgAffineTransform deviceMap, double scaleX, double scaleY) = BuildDeviceMap(element.Transform, frame);
        if (hasArc && !element.Transform.IsAxisAligned) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Elliptical arcs under rotated polygon transforms stay unsupported.");
        if (hasArc && (scaleX != scaleY || scaleX <= 0)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Elliptical arcs under non-uniform polygon transforms stay unsupported.");
        SvgFlattenPolicy flattenPolicy = new(ScaledFlatness(element.OutputMagnification), FlattenMaxDepth, CurveSegmentsPerCommand);
        long wantedPoints = hasCurve
            ? (long)commandCount * (CurveSegmentsPerCommand + 1)
            : (long)d.Length + commandCount + 1;
        int pointCapacity = wantedPoints > MaximumFlattenedContourPoints ? MaximumFlattenedContourPoints : (int)wantedPoints;
        int contourCapacity = pointCapacity < _maximumGeometryElements ? pointCapacity : _maximumGeometryElements;
        long scratchBytes;
        try { scratchBytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(contourCapacity)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Curve flattening exceeds the supported expansion range."); }
        using RenderBudget.BudgetReservation scratch = budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
        SvgContourBuilder builder = new(pointCapacity, contourCapacity);
        SvgPathDataParser.ParsePath(d, deviceMap, builder, flattenPolicy, _maximumGeometryElements, _workCounter, _cancellationToken, null);
        using BudgetedParserList<SvgDevicePoint> mapped = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        int[] ends = new int[builder.ContourCount];
        int kept = 0;
        bool droppedDegenerate = false;
        for (int contour = 0; contour < builder.ContourCount; contour++)
        {
            int before = mapped.Count;
            if (AppendStrippedContour(mapped, builder.GetContour(contour), _workCounter, _cancellationToken) < 3)
            {
                // Sealed degenerate contours enclose no area; drop them instead of rejecting.
                while (mapped.Count > before) mapped.RemoveLast();
                droppedDegenerate = true;
                continue;
            }
            ends[kept++] = mapped.Count;
        }
        if (droppedDegenerate && kept > 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer path contours are implemented.");
        if (kept != ends.Length) Array.Resize(ref ends, kept);

        return FinishPolygonFill(mapped, ends, element.Presentation.Color, element.Presentation.HasFill, mapping, frame, budget);
    }

    /// <summary>Lowers user-space fractional point-list vertices through the device map into one polygon fill.</summary>
    internal SvgParsedGeometry LowerFractionalPointList(BudgetedParserList<SvgFractionalPoint> points, SvgPolygonMapping mapping, SvgResolvedPaintableElement element, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(budget);
        if (points.Count >= _maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, _elementName + " vertex count exceeds the configured path-command limit.");
        (SvgAffineTransform deviceMap, double _, double _) = BuildDeviceMap(element.Transform, frame);
        using BudgetedParserList<SvgDevicePoint> mapped = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        SvgFractionalPoint previous = default;
        SvgFractionalPoint first = default;
        bool hasKept = false;
        for (int index = 0; index < points.Count; index++)
        {
            _workCounter.Consume(_cancellationToken);
            SvgFractionalPoint vertex = points[index];
            if (hasKept && previous == vertex) continue;
            if (hasKept && vertex == first && index == points.Count - 1) continue;
            if (!hasKept) first = vertex;
            previous = vertex;
            hasKept = true;
            mapped.Add(deviceMap.Apply(vertex.X, vertex.Y));
        }

        if (mapped.Count < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed fractional " + _elementName + " contours are implemented.");
        int[] ends = [mapped.Count];
        return FinishPolygonFill(mapped, ends, element.Presentation.Color, element.Presentation.HasFill, mapping, frame, budget);
    }

    /// <summary>Strokes user-space fractional point-list vertices through device-space contours with the caller cap and join styles.</summary>
    internal SvgParsedGeometry LowerStrokePointList(BudgetedParserList<SvgFractionalPoint> points, bool closed, SvgResolvedPaintableElement element, SvgColor color, bool hasFill, double width, double miterLimit, SvgStrokeCap cap, SvgStrokeJoin join, double[]? dashes, double dashOffset, double? pathLength, bool nonScaling, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(budget);
        if (points.Count >= _maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, _elementName + " stroke point count exceeds the configured path-command limit.");
        if (points.Count < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed fractional " + _elementName + " contours are implemented.");
        (SvgAffineTransform deviceMap, double _, double _) = BuildDeviceMap(element.Transform, frame);
        int pointCapacity = points.Count + 1 > MaximumFlattenedContourPoints ? MaximumFlattenedContourPoints : points.Count + 1;
        long scratchBytes;
        try { scratchBytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(1)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the supported expansion range."); }
        using RenderBudget.BudgetReservation scratch = budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
        SvgContourBuilder builder = new(pointCapacity, 1);
        builder.BeginContour(deviceMap.Apply(points[0].X, points[0].Y));
        for (int index = 1; index < points.Count; index++)
        {
            _workCounter.Consume(_cancellationToken);
            builder.AddLine(deviceMap.Apply(points[index].X, points[index].Y));
        }

        builder.EndContour();
        bool[] closedEnds = [closed];
        SvgParsedGeometry stroked = LowerStrokeContours(builder, closedEnds, false, element.Transform, width, miterLimit, cap, join, dashes, dashOffset, pathLength, nonScaling, color, hasFill, frame, budget);
        return stroked with { ElementCount = points.Count + 1 };
    }

    /// <summary>Strokes one integer point contour with the caller cap and join styles into one nonzero outline fill.</summary>
    internal SvgParsedGeometry LowerStrokePolyline(BudgetedParserList<SvgPoint> points, bool closed, SvgAffineTransform transform, double width, double miterLimit, SvgStrokeCap cap, SvgStrokeJoin join, double[]? dashes, double dashOffset, double? pathLength, bool nonScaling, SvgColor color, bool hasFill, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(budget);
        if (points.Count >= _maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, _isPath ? "Path stroke point count exceeds the configured path-command limit." : _elementName + " stroke point count exceeds the configured path-command limit.");
        double deviceWidth = StrokeDeviceWidth(width, transform, frame, nonScaling);
        using BudgetedParserList<SvgDevicePoint> mapped = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        for (int index = 0; index < points.Count; index++)
        {
            _workCounter.Consume(_cancellationToken);
            SvgDevicePoint mappedVertex = transform.Apply(points[index].X, points[index].Y);
            mapped.Add(ShiftVertex(frame, mappedVertex.X, mappedVertex.Y));
        }

        if (dashes is null)
        {
            // Open miter joins emit up to five outline points per centerline vertex (inner butt pair plus outer ear triple across both chains).
            long wantedPoints = 6L * mapped.Count + 8;
            if ((cap != SvgStrokeCap.Butt && cap != SvgStrokeCap.Square) || join == SvgStrokeJoin.Round)
            {
                int fanSegments = SvgStrokeOutline.MaxArcFanSegments(deviceWidth / 2, Math.PI);
                try { wantedPoints = checked(4L * mapped.Count * (fanSegments + 1) + 32); }
                catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the supported expansion range."); }
            }
            int pointCapacity = wantedPoints > MaximumFlattenedContourPoints ? MaximumFlattenedContourPoints : (int)wantedPoints;
            long scratchBytes;
            try { scratchBytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(closed ? 2 : 1)); }
            catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the supported expansion range."); }
            using RenderBudget.BudgetReservation scratch = budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
            SvgContourBuilder outlines = new(pointCapacity, closed ? 2 : 1);
            if (closed) SvgStrokeOutline.StrokeClosedPolyline(mapped.AsSpan(), deviceWidth, miterLimit, join, outlines, _cancellationToken);
            else SvgStrokeOutline.StrokeOpenPolyline(mapped.AsSpan(), deviceWidth, miterLimit, cap, join, outlines, _cancellationToken);
            return FinishStrokeFill(outlines, color, hasFill, false, frame, budget) with { ElementCount = points.Count + 1 };
        }
        double dashScale = Math.Abs(DashDeviceScale(transform) * (frame?.ScaleX ?? 1));
        return LowerDashedStrokePolyline(mapped, closed, deviceWidth, dashScale, miterLimit, cap, join, dashes, dashOffset, pathLength, color, hasFill, frame, budget) with { ElementCount = points.Count + 1 };
    }

    /// <summary>Strokes one mapped contour through the dash pattern into one nonzero outline fill.</summary>
    private SvgParsedGeometry LowerDashedStrokePolyline(BudgetedParserList<SvgDevicePoint> mapped, bool closed, double deviceWidth, double dashScale, double miterLimit, SvgStrokeCap cap, SvgStrokeJoin join, double[] dashes, double dashOffset, double? pathLength, SvgColor color, bool hasFill, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(mapped);
        ArgumentNullException.ThrowIfNull(dashes);
        ArgumentNullException.ThrowIfNull(budget);
        double[] pattern = ScaleDashPattern(dashes, dashScale);

        double contourLength = DashContourLength(mapped.AsSpan(), closed);
        double dashFactor = RescaleDashPattern(pattern, contourLength / dashScale, pathLength);
        double minimumStep = DashMinimumStep(pattern);
        long cuts;
        try { cuts = double.IsFinite(contourLength) ? checked((long)(contourLength / minimumStep) + 2) : MaximumFlattenedContourPoints; }
        catch (OverflowException) { cuts = MaximumFlattenedContourPoints; }
        // Open miter joins emit up to five outline points per centerline vertex (inner butt pair plus outer ear triple across both chains).
            long wantedPoints = checked(6L * (mapped.Count + cuts) + 8);
        if (cap == SvgStrokeCap.Round || join == SvgStrokeJoin.Round)
        {
            int fanSegments = SvgStrokeOutline.MaxArcFanSegments(deviceWidth / 2, Math.PI);
            try { wantedPoints = checked(4L * (mapped.Count + cuts) * (fanSegments + 1) + 32); }
            catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the supported expansion range."); }
        }

        int pointCapacity = wantedPoints > MaximumFlattenedContourPoints ? MaximumFlattenedContourPoints : (int)wantedPoints;
        long contourCapacity = cuts + 3 > MaximumFlattenedContourPoints ? MaximumFlattenedContourPoints : cuts + 3;
        long scratchBytes;
        try { scratchBytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity) + 2 * ParserCollectionMemory.GetArrayByteCount<int>((int)contourCapacity)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the supported expansion range."); }
        using RenderBudget.BudgetReservation scratch = budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
        SvgContourBuilder outlines = new(pointCapacity, (int)contourCapacity);
        using BudgetedParserList<SvgDevicePoint> leading = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        using BudgetedParserList<SvgDevicePoint> current = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        double scaledOffset = dashOffset * dashScale * dashFactor;
        if (!double.IsFinite(scaledOffset)) throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the admitted numeric range.");
        StrokeDashedContour(mapped.AsSpan(), closed, pattern, scaledOffset, deviceWidth, miterLimit, cap, join, outlines, leading, current);
        return FinishStrokeFill(outlines, color, hasFill, false, frame, budget);
    }

    /// <summary>Scales one dash pattern into device units.</summary>
    private static double[] ScaleDashPattern(double[] dashes, double dashScale)
    {
        ArgumentNullException.ThrowIfNull(dashes);
        double[] pattern = new double[dashes.Length];
        for (int index = 0; index < dashes.Length; index++) pattern[index] = dashes[index] * dashScale;

        return pattern;
    }

    /// <summary>Rescales device-space dash entries by the path-length factor, reporting the applied factor.</summary>
    internal static double RescaleDashPattern(double[] pattern, double userLength, double? pathLength)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (pathLength is null || !(userLength > 0)) return 1;
        double factor = userLength / pathLength.Value;
        if (!double.IsFinite(factor)) throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the admitted numeric range.");
        for (int index = 0; index < pattern.Length; index++)
        {
            pattern[index] *= factor;
            if (!double.IsFinite(pattern[index])) throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the admitted numeric range.");
        }

        return factor;
    }

    /// <summary>Reports the smallest positive dash step for cut estimation.</summary>
    private static double DashMinimumStep(double[] pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        double minimumStep = double.PositiveInfinity;
        foreach (double step in pattern)
        {
            if (step > 0 && step < minimumStep) minimumStep = step;
        }

        return minimumStep;
    }

    /// <summary>Measures all device-space contours along their segments.</summary>
    private double DashTotalLength(SvgContourBuilder source, ReadOnlySpan<bool> closedEnds)
    {
        ArgumentNullException.ThrowIfNull(source);
        double total = 0;
        for (int contour = 0; contour < source.ContourCount; contour++)
        {
            if ((contour & 63) == 0) _cancellationToken.ThrowIfCancellationRequested();
            total += DashContourLength(source.GetContour(contour), closedEnds[contour]);
        }

        return total;
    }

    /// <summary>Measures one device-space contour along its segments, wrapping closed loops through the seam.</summary>
    private double DashContourLength(ReadOnlySpan<SvgDevicePoint> contour, bool closed)
    {
        if (contour.Length == 0) return 0;
        double length = 0;
        int segments = closed ? contour.Length : contour.Length - 1;
        for (int index = 0; index < segments; index++)
        {
            if ((index & 63) == 0) _cancellationToken.ThrowIfCancellationRequested();
            SvgDevicePoint start = contour[index];
            SvgDevicePoint end = contour[(index + 1) % contour.Length];
            if (start == end) continue;
            length += Math.Sqrt((end.X - start.X) * (end.X - start.X) + (end.Y - start.Y) * (end.Y - start.Y));
        }

        return length;
    }

    /// <summary>Strokes one device-space contour as pattern runs sharing one outline fill, merging seam-touching runs on closed loops.</summary>
    private void StrokeDashedContour(ReadOnlySpan<SvgDevicePoint> contour, bool closed, double[] pattern, double offset, double width, double miterLimit, SvgStrokeCap cap, SvgStrokeJoin join, SvgContourBuilder outlines, BudgetedParserList<SvgDevicePoint> leading, BudgetedParserList<SvgDevicePoint> current)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(outlines);
        ArgumentNullException.ThrowIfNull(leading);
        ArgumentNullException.ThrowIfNull(current);
        if (contour.Length == 0) return;
        leading.Clear();
        current.Clear();
        double total = 0;
        foreach (double patternEntry in pattern) total += patternEntry;
        if (!(total > 0)) return;
        double phase = offset - Math.Floor(offset / total) * total;
        if (phase < 0) phase = 0;
        else if (phase >= total) phase = 0;
        int entry = 0;
        double entered = 0;
        while (entered + pattern[entry] <= phase)
        {
            entered += pattern[entry];
            entry = (entry + 1) % pattern.Length;
        }

        double remaining = entered + pattern[entry] - phase;
        while (remaining == 0)
        {
            entry = (entry + 1) % pattern.Length;
            entered = phase;
            remaining = pattern[entry];
        }

        bool onEntry = (entry & 1) == 0;
        bool runOpen = false;
        bool runOpenInitial = onEntry;
        int runs = 0;
        BudgetedParserList<SvgDevicePoint> active = leading;
        int segments = closed ? contour.Length : contour.Length - 1;
        for (int index = 0; index < segments; index++)
        {
            if ((index & 63) == 0) _cancellationToken.ThrowIfCancellationRequested();
            SvgDevicePoint segmentStart = contour[index];
            SvgDevicePoint segmentEnd = contour[(index + 1) % contour.Length];
            if (segmentStart == segmentEnd) continue;
            _workCounter.Consume(_cancellationToken);
            double segmentLength = Math.Sqrt((segmentEnd.X - segmentStart.X) * (segmentEnd.X - segmentStart.X) + (segmentEnd.Y - segmentStart.Y) * (segmentEnd.Y - segmentStart.Y));
            double traveled = 0;
            while (true)
            {
                if (onEntry && !runOpen)
                {
                    active.Add(traveled == 0 ? segmentStart : DashCutPoint(segmentStart, segmentEnd, traveled / segmentLength));
                    runOpen = true;
                }

                double rest = segmentLength - traveled;
                double take = remaining < rest ? remaining : rest;
                traveled += take;
                remaining -= take;
                bool finished = take == rest;
                if (remaining > 0) break;
                if (onEntry)
                {
                    active.Add(finished ? segmentEnd : DashCutPoint(segmentStart, segmentEnd, traveled / segmentLength));
                    if (runs == 0) active = current;
                    else
                    {
                        if (!SvgStrokeOutline.IsAllIdentical(active.AsSpan())) SvgStrokeOutline.StrokeOpenPolyline(active.AsSpan(), width, miterLimit, cap, join, outlines, _cancellationToken);
                        active.Clear();
                    }

                    runs++;
                    runOpen = false;
                }

                do
                {
                    entry = (entry + 1) % pattern.Length;
                    remaining = pattern[entry];
                }
                while (remaining == 0);
                onEntry = (entry & 1) == 0;
                if (finished) break;
            }

            if (onEntry)
            {
                active.Add(segmentEnd);
                runOpen = true;
            }
        }

        bool endedAtEnd = runOpen;
        if (runOpen)
        {
            runs++;
            runOpen = false;
        }

        if (runs == 0) return;
        if (closed && runs == 1 && runOpenInitial && endedAtEnd)
        {
            SvgStrokeOutline.StrokeClosedPolyline(contour, width, miterLimit, join, outlines, _cancellationToken);
            return;
        }

        if (closed && runOpenInitial && endedAtEnd && runs >= 2)
        {
            ReadOnlySpan<SvgDevicePoint> head = leading.AsSpan();
            ReadOnlySpan<SvgDevicePoint> tail = current.AsSpan();
            current.AddRange(head.Length > 0 && tail.Length > 0 && head[0] == tail[tail.Length - 1] ? head[1..] : head);
            if (!SvgStrokeOutline.IsAllIdentical(current.AsSpan())) SvgStrokeOutline.StrokeOpenPolyline(current.AsSpan(), width, miterLimit, cap, join, outlines, _cancellationToken);
        }
        else
        {
            if (!SvgStrokeOutline.IsAllIdentical(leading.AsSpan())) SvgStrokeOutline.StrokeOpenPolyline(leading.AsSpan(), width, miterLimit, cap, join, outlines, _cancellationToken);
            if (endedAtEnd && current.Count > 0 && !SvgStrokeOutline.IsAllIdentical(current.AsSpan())) SvgStrokeOutline.StrokeOpenPolyline(current.AsSpan(), width, miterLimit, cap, join, outlines, _cancellationToken);
        }

        static SvgDevicePoint DashCutPoint(SvgDevicePoint start, SvgDevicePoint end, double fraction)
            => new(start.X + (end.X - start.X) * fraction, start.Y + (end.Y - start.Y) * fraction);
    }

    /// <summary>Strokes sampled contours through the dash pattern into one nonzero outline fill.</summary>
    private SvgParsedGeometry LowerDashedStrokeContours(SvgContourBuilder source, ReadOnlySpan<bool> closedEnds, bool hasCurves, SvgAffineTransform transform, double deviceWidth, double miterLimit, SvgStrokeCap cap, SvgStrokeJoin join, double[] dashes, double dashOffset, double? pathLength, SvgColor color, bool hasFill, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dashes);
        ArgumentNullException.ThrowIfNull(budget);
        double dashScale = Math.Abs(DashDeviceScale(transform) * (frame?.ScaleX ?? 1));
        double[] pattern = ScaleDashPattern(dashes, dashScale);
        double totalDeviceLength = DashTotalLength(source, closedEnds);
        double dashFactor = RescaleDashPattern(pattern, totalDeviceLength / dashScale, pathLength);
        double minimumStep = DashMinimumStep(pattern);

        long totalCuts = 0;
        for (int contour = 0; contour < source.ContourCount; contour++)
        {
            if ((contour & 63) == 0) _cancellationToken.ThrowIfCancellationRequested();
            double contourLength = DashContourLength(source.GetContour(contour), closedEnds[contour]);
            try { totalCuts = checked(totalCuts + (double.IsFinite(contourLength) ? (long)(contourLength / minimumStep) + 2 : MaximumFlattenedContourPoints)); }
            catch (OverflowException) { totalCuts = MaximumFlattenedContourPoints; }
            if (totalCuts >= MaximumFlattenedContourPoints) { totalCuts = MaximumFlattenedContourPoints; break; }
        }

        // Open miter joins emit up to five outline points per centerline vertex (inner butt pair plus outer ear triple across both chains).
            long wantedPoints = checked(6L * (source.PointCount + totalCuts) + 8);
        if (cap == SvgStrokeCap.Round || join == SvgStrokeJoin.Round)
        {
            int fanSegments = SvgStrokeOutline.MaxArcFanSegments(deviceWidth / 2, Math.PI);
            try { wantedPoints = checked(4L * (source.PointCount + totalCuts) * (fanSegments + 1) + 32); }
            catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the supported expansion range."); }
        }

        int pointCapacity = wantedPoints > MaximumFlattenedContourPoints ? MaximumFlattenedContourPoints : (int)wantedPoints;
        long contourCapacity;
        try { contourCapacity = checked(2L * source.ContourCount + totalCuts + 2); }
        catch (OverflowException) { contourCapacity = MaximumFlattenedContourPoints; }
        if (contourCapacity > MaximumFlattenedContourPoints) contourCapacity = MaximumFlattenedContourPoints;
        long scratchBytes;
        try { scratchBytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity) + 2 * ParserCollectionMemory.GetArrayByteCount<int>((int)contourCapacity)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the supported expansion range."); }
        using RenderBudget.BudgetReservation scratch = budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
        SvgContourBuilder outlines = new(pointCapacity, (int)contourCapacity);
        using BudgetedParserList<SvgDevicePoint> leading = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        using BudgetedParserList<SvgDevicePoint> current = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        double scaledOffset = dashOffset * dashScale * dashFactor;
        if (!double.IsFinite(scaledOffset)) throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the admitted numeric range.");
        for (int contour = 0; contour < source.ContourCount; contour++)
        {
            if ((contour & 63) == 0) _cancellationToken.ThrowIfCancellationRequested();
            StrokeDashedContour(source.GetContour(contour), closedEnds[contour], pattern, scaledOffset, deviceWidth, miterLimit, cap, join, outlines, leading, current);
        }

        return FinishStrokeFill(outlines, color, hasFill, hasCurves, frame, budget);
    }

    /// <summary>Strokes device-space contours with the caller cap and join styles into one nonzero outline fill.</summary>
    internal SvgParsedGeometry LowerStrokeContours(SvgContourBuilder source, ReadOnlySpan<bool> closedEnds, bool hasCurves, SvgAffineTransform transform, double width, double miterLimit, SvgStrokeCap cap, SvgStrokeJoin join, double[]? dashes, double dashOffset, double? pathLength, bool nonScaling, SvgColor color, bool hasFill, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(budget);
        if (closedEnds.Length != source.ContourCount) throw new InvalidOperationException("Stroke contour closure flags must match the sampled contours.");
        if (source.PointCount >= _maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, _isPath ? "Path stroke point count exceeds the configured path-command limit." : _elementName + " stroke point count exceeds the configured path-command limit.");
        double deviceWidth = StrokeDeviceWidth(width, transform, frame, nonScaling);
        if (dashes is not null) return LowerDashedStrokeContours(source, closedEnds, hasCurves, transform, deviceWidth, miterLimit, cap, join, dashes, dashOffset, pathLength, color, hasFill, frame, budget);
        // Open miter joins emit up to five outline points per centerline vertex (inner butt pair plus outer ear triple across both chains).
            long wantedPoints = 6L * source.PointCount + 8;
        if ((cap != SvgStrokeCap.Butt && cap != SvgStrokeCap.Square) || join == SvgStrokeJoin.Round)
        {
            int fanSegments = SvgStrokeOutline.MaxArcFanSegments(deviceWidth / 2, Math.PI);
            try { wantedPoints = checked(4L * source.PointCount * (fanSegments + 1) + 32); }
            catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the supported expansion range."); }
        }
        int pointCapacity = wantedPoints > MaximumFlattenedContourPoints ? MaximumFlattenedContourPoints : (int)wantedPoints;
        long wantedContours;
        try { wantedContours = checked(2L * source.ContourCount + 2); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the supported expansion range."); }
        int contourCapacity = wantedContours > MaximumFlattenedContourPoints ? MaximumFlattenedContourPoints : (int)wantedContours;
        long scratchBytes;
        try { scratchBytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(contourCapacity)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the supported expansion range."); }
        using RenderBudget.BudgetReservation scratch = budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
        SvgContourBuilder outlines = new(pointCapacity, contourCapacity);
        for (int contour = 0; contour < source.ContourCount; contour++)
        {
            if ((contour & 63) == 0) _cancellationToken.ThrowIfCancellationRequested();
            if (closedEnds[contour]) SvgStrokeOutline.StrokeClosedPolyline(source.GetContour(contour), deviceWidth, miterLimit, join, outlines, _cancellationToken);
            else SvgStrokeOutline.StrokeOpenPolyline(source.GetContour(contour), deviceWidth, miterLimit, cap, join, outlines, _cancellationToken);
        }

        return FinishStrokeFill(outlines, color, hasFill, hasCurves, frame, budget);
    }

    private static double DashDeviceScale(SvgAffineTransform transform)
    {
        if (transform.IsAxisAligned) return transform.A;
        if (!transform.TryGetUniformScale(out double uniform)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only uniformly scaled dash transforms are implemented.");
        return uniform;
    }

    private double StrokeDeviceWidth(double width, SvgAffineTransform transform, SvgViewportFrame? frame, bool nonScaling)
    {
        double frameScaleX = frame?.ScaleX ?? 1;
        double frameScaleY = frame?.ScaleY ?? 1;
        if (Math.Abs(frameScaleX) != Math.Abs(frameScaleY)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only uniformly scaled " + _elementName + " strokes are implemented.");
        double userScaleX;
        double userScaleY;
        if (transform.IsAxisAligned)
        {
            userScaleX = transform.A;
            userScaleY = transform.D;
        }
        else if (!transform.TryGetUniformScale(out double uniformUserScale)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only uniformly scaled " + _elementName + " strokes are implemented.");
        else
        {
            userScaleX = uniformUserScale;
            userScaleY = uniformUserScale;
        }
        double scaleX = userScaleX * frameScaleX;
        double scaleY = userScaleY * frameScaleY;
        if (!nonScaling && (Math.Abs(scaleX) != Math.Abs(scaleY) || scaleX == 0)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only uniformly scaled " + _elementName + " strokes are implemented.");
        if (nonScaling && (userScaleX == 0 || userScaleY == 0 || frameScaleX == 0 || frameScaleY == 0)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only uniformly scaled " + _elementName + " strokes are implemented.");
        double deviceWidth = nonScaling ? width : width * Math.Abs(scaleX);
        if (Math.Abs(deviceWidth - Math.Round(deviceWidth)) <= 1e-9) deviceWidth = Math.Round(deviceWidth);
        if (!double.IsFinite(deviceWidth) || deviceWidth <= 0 || deviceWidth > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the admitted numeric range.");
        return deviceWidth;
    }

    /// <summary>Strokes path data with the caller cap and join styles into one nonzero outline fill, tracking explicit closepath seals.</summary>
    internal SvgParsedGeometry LowerStrokePath(string d, int commandCount, bool hasArc, bool hasCurves, SvgResolvedPaintableElement element, SvgColor color, bool hasFill, double width, double miterLimit, SvgStrokeCap cap, SvgStrokeJoin join, double[]? dashes, double dashOffset, double? pathLength, bool nonScaling, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        if (commandCount >= _maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, "Path stroke command count exceeds the configured path-command limit.");
        (SvgAffineTransform deviceMap, double scaleX, double scaleY) = BuildDeviceMap(element.Transform, frame);
        if (hasArc && !element.Transform.IsAxisAligned) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Elliptical arcs under rotated polygon transforms stay unsupported.");
        if (hasArc && (scaleX != scaleY || scaleX <= 0)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Elliptical arcs under non-uniform polygon transforms stay unsupported.");
        double deviceWidth = StrokeDeviceWidth(width, element.Transform, frame, nonScaling);
        SvgFlattenPolicy flattenPolicy = new(ScaledFlatness(element.OutputMagnification), FlattenMaxDepth, CurveSegmentsPerCommand);
        long wantedPoints = hasCurves
            ? (long)commandCount * (CurveSegmentsPerCommand + 1)
            : (long)d.Length + commandCount + 1;
        int pointCapacity = wantedPoints > MaximumFlattenedContourPoints ? MaximumFlattenedContourPoints : (int)wantedPoints;
        int contourCapacity = pointCapacity < _maximumGeometryElements ? pointCapacity : _maximumGeometryElements;
        long scratchBytes;
        try { scratchBytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(contourCapacity)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the supported expansion range."); }
        using RenderBudget.BudgetReservation scratch = budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
        SvgContourBuilder builder = new(pointCapacity, contourCapacity);
        using BudgetedParserList<bool> closedEnds = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        SvgPathDataParser.ParsePath(d, deviceMap, builder, flattenPolicy, _maximumGeometryElements, _workCounter, _cancellationToken, closedEnds);
        SvgParsedGeometry stroked = LowerStrokeContours(builder, closedEnds.AsSpan(), hasCurves, element.Transform, width, miterLimit, cap, join, dashes, dashOffset, pathLength, nonScaling, color, hasFill, frame, budget);
        return stroked with { ElementCount = commandCount + 1 };
    }

    /// <summary>Retains stroke outline loops as one nonzero fill without structure validation, which spike retraces would false-positive.</summary>
    /// <remarks>Winding roles resolve closed outer and inner rings, then role zero lifts to plus one for non-degenerate loops: a zero role on such a loop always means a same-wound painted island (holes resolve to minus one and degenerate loops contribute nothing), so overlapping bands union instead of cancelling each other while ring holes stay holes. Dashed runs inherit the same rule, which subsumes the earlier dash-only union flag.</remarks>
    private SvgParsedGeometry FinishStrokeFill(SvgContourBuilder outlines, SvgColor color, bool hasFill, bool flattenedCenterline, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(outlines);
        ArgumentNullException.ThrowIfNull(budget);
        using BudgetedParserList<SvgDevicePoint> mapped = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        int[] ends = new int[outlines.ContourCount];
        bool fractionalVertices = false;
        for (int contour = 0; contour < outlines.ContourCount; contour++)
        {
            foreach (SvgDevicePoint vertex in outlines.GetContour(contour))
            {
                _workCounter.Consume(_cancellationToken);
                mapped.Add(vertex);
                if (Math.Abs(vertex.X * 4 - Math.Round(vertex.X * 4, MidpointRounding.AwayFromZero)) > 0.000000001 || Math.Abs(vertex.Y * 4 - Math.Round(vertex.Y * 4, MidpointRounding.AwayFromZero)) > 0.000000001) fractionalVertices = true;
            }

            ends[contour] = mapped.Count;
        }

        ChargePairs(_workCounter, (long)ends.Length * (ends.Length - 1) / 2, _cancellationToken);
        SvgDevicePoint[] retainedOutlines = mapped.AsSpan().ToArray();
        SvgContourNesting.MergeIdenticalContours(retainedOutlines, ends, _workCounter, out SvgDevicePoint[] mergedOutlines, out int[] mergedEnds, _cancellationToken);
        retainedOutlines = mergedOutlines;
        ends = mergedEnds;

        SvgPolygonMapping mapping = flattenedCenterline ? SvgPolygonMapping.FlattenedCurves : fractionalVertices ? SvgPolygonMapping.FiniteEdges : SvgPolygonMapping.QuarterEdges;
        RenderBudget.BudgetReservation? reservation = ReserveScene(retainedOutlines.Length, ends.Length, ends.Length, budget);
        try
        {
            ValidateDeviceRange(retainedOutlines);
            int[] roles = new int[ends.Length];
            ChargePairs(_workCounter, (long)ends.Length * (ends.Length - 1) / 2, _cancellationToken);
            SvgContourNesting.ComputeWindingRoles(retainedOutlines, ends, roles, false, _cancellationToken);
            for (int contour = 0; contour < ends.Length; contour++)
            {
                if ((contour & 63) == 0) _cancellationToken.ThrowIfCancellationRequested();
                int start = contour == 0 ? 0 : ends[contour - 1];
                if (roles[contour] == 0 && ends[contour] - start >= 3) roles[contour] = 1;
            }
            return RetainPolygonFill(retainedOutlines, ends, roles, color, hasFill, false, mapping, frame is null ? (SvgClipBox?)null : frame.Value.Clip, reservation, budget);
        }
        catch
        {
            reservation?.Dispose();
            throw;
        }
    }

    /// <summary>Validated separated contour storage with resolved combination weights and its scene reservation, ready for paint-specific retention. The reservation transfers to the retained fill.</summary>
    private readonly record struct SeparatedContours(SvgDevicePoint[] Points, int[] Ends, int[] Signs, SvgPolygonMapping Mapping, RenderBudget.BudgetReservation? SceneReservation);

    /// <summary>Reserves exact scene storage for retained contour arrays before allocation, returning null when nothing is retained.</summary>
    private static RenderBudget.BudgetReservation? ReserveScene(int pointCount, int endCount, int signCount, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        long bytes;
        try { bytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCount) + ParserCollectionMemory.GetArrayByteCount<int>(endCount) + ParserCollectionMemory.GetArrayByteCount<int>(signCount)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill storage exceeds the supported accounting range."); }
        return bytes == 0 ? null : budget.Reserve(bytes, SvgMemoryCategory.Scene);
    }

    /// <summary>Compacts dust-scale consecutive joints (including each contour closing joint) in place, writing cumulative compacted ends and returning the compacted total.</summary>
    /// <remarks>Snapping keeps the earlier bitwise value so no computed points ever appear and the touch pipeline keeps its bitwise invariant; strictly-bitwise-equal joints keep the existing zero-length path untouched. Results under three points per contour flow to the existing degenerate guards exactly like any degenerate input. Charges one work unit per examined point and observes cancellation through the counter; compacted ends reuse caller-reserved ends-sized storage.</remarks>
    internal static int WeldDustJoints(SvgDevicePoint[] points, int[] ends, int[] weldedEnds, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(weldedEnds);
        ArgumentNullException.ThrowIfNull(workCounter);
        if (weldedEnds.Length != ends.Length) throw new ArgumentOutOfRangeException(nameof(weldedEnds));
        int total = 0;
        int previousEnd = 0;
        for (int contour = 0; contour < ends.Length; contour++)
        {
            if ((contour & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            int start = previousEnd;
            int end = ends[contour];
            previousEnd = end;
            if (start < 0 || end > points.Length || end < start) throw new ArgumentOutOfRangeException(nameof(ends));
            int write = total;
            SvgDevicePoint anchor = default;
            bool hasAnchor = false;
            for (int index = start; index < end; index++)
            {
                workCounter.Consume(cancellationToken);
                if (hasAnchor && IsDustJoint(anchor, points[index]))
                {
                    continue;
                }
                anchor = points[index];
                hasAnchor = true;
                points[write] = points[index];
                write++;
            }
            if (hasAnchor && write - total > 2 && IsDustJoint(points[total], anchor))
            {
                write--;
            }
            total = write;
            weldedEnds[contour] = total;
        }
        return total;

        static bool IsDustJoint(SvgDevicePoint kept, SvgDevicePoint candidate)
        {
            if (kept.X == candidate.X && kept.Y == candidate.Y) return false;
            return Math.Abs(kept.X - candidate.X) <= WeldEpsilon && Math.Abs(kept.Y - candidate.Y) <= WeldEpsilon;
        }
    }

    /// <summary>Validates separated device contours once and resolves per-contour combination weights, returning paint-independent retained storage with its scene reservation, which transfers to the retained fill.</summary>
    private SeparatedContours PrepareSeparatedContours(BudgetedParserList<SvgDevicePoint> mapped, int[] ends, SvgPolygonMapping mapping, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(mapped);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(budget);
        RenderBudget.BudgetReservation? reservation = ReserveScene(mapped.Count, ends.Length, ends.Length, budget);
        try
        {
            SvgDevicePoint[] retained = mapped.AsSpan().ToArray();
            ValidateDeviceRange(retained);
            int[] weldedEnds = new int[ends.Length];
            int weldedTotal = WeldDustJoints(retained, ends, weldedEnds, _workCounter, _cancellationToken);
            if (weldedTotal < retained.Length)
            {
                reservation?.Dispose();
                reservation = ReserveScene(weldedTotal, ends.Length, ends.Length, budget);
                SvgDevicePoint[] tightPoints = new SvgDevicePoint[weldedTotal];
                for (int index = 0; index < weldedTotal; index++)
                {
                    _workCounter.Consume(_cancellationToken);
                    tightPoints[index] = retained[index];
                }
                retained = tightPoints;
                ends = weldedEnds;
            }
            ChargePairs(_workCounter, (long)weldedTotal * (weldedTotal - 1) / 2, _cancellationToken);
            ChargePairs(_workCounter, (long)ends.Length * (ends.Length - 1) / 2, _cancellationToken);
            if (SvgContourNesting.MergeIdenticalContours(retained, ends, _workCounter, out SvgDevicePoint[] mergedPoints, out int[] mergedEnds, _cancellationToken))
            {
                reservation?.Dispose();
                reservation = ReserveScene(mergedPoints.Length, mergedEnds.Length, mergedEnds.Length, budget);
                retained = mergedPoints;
                ends = mergedEnds;
            }
            SvgContourFlags structure = SvgContourStructure.ClassifySet(retained, ends, _cancellationToken);
            bool touchResolved = false;
            bool interLoopResolved = false;
            if (structure != SvgContourFlags.None)
            {
                using BudgetedParserList<SvgDevicePoint> splitPoints = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
                using BudgetedParserList<int> splitEnds = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
                if (!TrySplitCrossingContours(structure, retained, ends, _isEvenOdd, splitPoints, splitEnds, budget, out interLoopResolved))
                {
                    splitPoints.Clear();
                    splitEnds.Clear();
                    if (!TryResolveTouchContours(structure, retained, ends, splitPoints, splitEnds)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only simple diagonal polygon contours are implemented; touching and self-intersecting contours stay unsupported.");
                    touchResolved = true;
                }
                reservation?.Dispose();
                reservation = ReserveScene(splitPoints.Count, splitEnds.Count, splitEnds.Count, budget);
                retained = splitPoints.AsSpan().ToArray();
                ends = splitEnds.AsSpan().ToArray();
                ValidateDeviceRange(retained);
                mapping = UpgradeSplitMapping(retained, mapping);
            }
            int[] signs = new int[ends.Length];
            ChargePairs(_workCounter, (long)ends.Length * (ends.Length - 1) / 2, _cancellationToken);
            bool skipSharedVertices = touchResolved || interLoopResolved;
            if (_isEvenOdd) SvgContourNesting.ComputeParitySigns(retained, ends, signs, skipSharedVertices, _cancellationToken);
            else SvgContourNesting.ComputeWindingRoles(retained, ends, signs, skipSharedVertices, _cancellationToken);
            return new SeparatedContours(retained, ends, signs, mapping, reservation);
        }
        catch
        {
            reservation?.Dispose();
            throw;
        }
    }

    /// <summary>Retains one polygon fill from validated separated contours with scene-owned storage.</summary>
    private SvgParsedGeometry FinishPolygonFill(BudgetedParserList<SvgDevicePoint> mapped, int[] ends, SvgColor color, bool hasFill, SvgPolygonMapping mapping, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(mapped);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(budget);
        SeparatedContours separated = PrepareSeparatedContours(mapped, ends, mapping, budget);
        return RetainPolygonFill(separated.Points, separated.Ends, separated.Signs, color, hasFill, _isEvenOdd, separated.Mapping, frame is null ? (SvgClipBox?)null : frame.Value.Clip, separated.SceneReservation, budget);
    }

    /// <summary>Splits self-crossing contours at interior crossings into simple loops sharing only split vertices, then eliminates crossings between loops the same way.</summary>
    /// <remarks>Every queued loop classifies either clean, joining the output, or as a pure proper crossing, splitting again; any other flag rejects, so touches, spikes and degenerate edges never enter the output. Even-odd keeps the single-contour single-crossing gate because first-vertex parity signs are meaningless for vertex-sharing loops, while nonzero fill iterates to a fixpoint where each split strictly reduces the remaining intra-loop crossings. Admitted loop sets pass the pairwise split-loop guard, so loops meet at most at bitwise-shared split vertices with nesting resolved by roles. When the guard still sees inter-loop crossings under nonzero fill, a second sweep subdivides both edges at each computed crossing until no strict crossing remains; each round strictly reduces the crossing count, and the guard still rejects touches, overlaps and degenerates exactly as before. Even-odd never enters the second sweep. Sets resolved across loops report through interLoopResolved so nesting tests skip bitwise-shared vertices. Output lists are caller-owned budgeted storage filled after the caller reserves them; each popped loop charges its pair estimate, each copied vertex charges one work unit, and the final guard charges the quadratic pair estimate; observes cancellation in classification, search, copy and guard. Partial output on denial stays caller-discarded.</remarks>
    private bool TrySplitCrossingContours(SvgContourFlags structure, ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> ends, bool isEvenOdd, BudgetedParserList<SvgDevicePoint> splitPoints, BudgetedParserList<int> splitEnds, RenderBudget budget, out bool interLoopResolved)
    {
        ArgumentNullException.ThrowIfNull(splitPoints);
        ArgumentNullException.ThrowIfNull(splitEnds);
        ArgumentNullException.ThrowIfNull(budget);
        interLoopResolved = false;
        if (structure == SvgContourFlags.None) return false;
        if (isEvenOdd && (structure != SvgContourFlags.ProperCrossing || ends.Length != 1 || ends[0] != points.Length)) return false;
        using BudgetedParserList<SvgDevicePoint> workPoints = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        using BudgetedParserList<int> pending = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        for (int index = 0; index < points.Length; index++)
        {
            _workCounter.Consume(_cancellationToken);
            workPoints.Add(points[index]);
        }

        int previousEnd = 0;
        foreach (int contourEnd in ends)
        {
            _workCounter.Consume(_cancellationToken);
            pending.Add(previousEnd);
            _workCounter.Consume(_cancellationToken);
            pending.Add(contourEnd);
            previousEnd = contourEnd;
        }

        bool splitDone = false;
        long pops = 0;
        long maximumPops = checked((long)points.Length * points.Length + ends.Length + 1);
        for (int cursor = 0; cursor < pending.Count;)
        {
            if (++pops > maximumPops) return false;
            int start = pending[cursor];
            int end = pending[cursor + 1];
            cursor += 2;
            int count = end - start;
            if (count < 3) return false;
            ChargePairs(_workCounter, (long)count * (count - 1) / 2, _cancellationToken);
            SvgContourFlags loop = SvgContourStructure.ClassifyContour(workPoints.AsSpan(), start, end, _cancellationToken);
            if (loop == SvgContourFlags.None)
            {
                for (int index = start; index < end; index++)
                {
                    _workCounter.Consume(_cancellationToken);
                    splitPoints.Add(workPoints[index]);
                }

                _workCounter.Consume(_cancellationToken);
                splitEnds.Add(splitPoints.Count);
                continue;
            }

            if (loop != SvgContourFlags.ProperCrossing || (isEvenOdd && splitDone)) return false;
            if (!SvgContourStructure.TryFindProperCrossing(workPoints.AsSpan(), start, end, _cancellationToken, out int firstEdge, out int secondEdge, out SvgDevicePoint crossing)) return false;
            int firstLoopCount = secondEdge - firstEdge + 1;
            int secondLoopCount = count - (secondEdge - firstEdge) + 1;
            if (firstLoopCount < 3 || secondLoopCount < 3) return false;
            int firstLoopStart = workPoints.Count;
            for (int index = start + firstEdge + 1; index <= start + secondEdge; index++)
            {
                _workCounter.Consume(_cancellationToken);
                workPoints.Add(workPoints[index]);
            }

            _workCounter.Consume(_cancellationToken);
            workPoints.Add(crossing);
            int secondLoopStart = workPoints.Count;
            for (int index = start + secondEdge + 1; index < end; index++)
            {
                _workCounter.Consume(_cancellationToken);
                workPoints.Add(workPoints[index]);
            }

            for (int index = start; index <= start + firstEdge; index++)
            {
                _workCounter.Consume(_cancellationToken);
                workPoints.Add(workPoints[index]);
            }

            _workCounter.Consume(_cancellationToken);
            workPoints.Add(crossing);
            _workCounter.Consume(_cancellationToken);
            pending.Add(firstLoopStart);
            _workCounter.Consume(_cancellationToken);
            pending.Add(secondLoopStart);
            _workCounter.Consume(_cancellationToken);
            pending.Add(secondLoopStart);
            _workCounter.Consume(_cancellationToken);
            pending.Add(workPoints.Count);
            splitDone = true;
        }

        long totalPoints = splitPoints.Count;
        ChargePairs(_workCounter, totalPoints * totalPoints, _cancellationToken);
        if (SvgContourStructure.AreSplitLoopsAdmissible(splitPoints.AsSpan(), splitEnds.AsSpan(), _cancellationToken)) return true;
        if (isEvenOdd) return false;
        if (!TrySplitInterLoopCrossings(splitPoints, splitEnds)) return false;
        interLoopResolved = true;
        return true;
    }

    /// <summary>Eliminates proper crossings between split loops by subdividing both edges at each computed crossing.</summary>
    /// <remarks>Phase-one outputs are intra-clean, so only inter-loop strict crossings remain; each subdivision replaces the crossing pair with endpoint contacts at one bitwise-shared vertex while every other contact stays untouched, so the crossing count strictly decreases and the sweep ends at the round cap at the latest. The earlier loop index always subdivides first so the later loop range stays valid. Touches, overlaps and degenerate edges are never repaired: the split-loop guard still arbitrates the final set and rejects them exactly as before, and even-odd never enters this sweep. On failure the phase-one output stays untouched for the caller to discard; on success the output lists hold the subdivided set for the shared reservation, mapping and nesting steps. Charges pair estimates per sweep round plus one work unit per copied vertex, and observes cancellation.</remarks>
    private bool TrySplitInterLoopCrossings(BudgetedParserList<SvgDevicePoint> splitPoints, BudgetedParserList<int> splitEnds)
    {
        ArgumentNullException.ThrowIfNull(splitPoints);
        ArgumentNullException.ThrowIfNull(splitEnds);
        SvgDevicePoint[] workPoints = new SvgDevicePoint[splitPoints.Count];
        int[] workEnds = new int[splitEnds.Count];
        for (int index = 0; index < workPoints.Length; index++)
        {
            _workCounter.Consume(_cancellationToken);
            workPoints[index] = splitPoints[index];
        }
        for (int index = 0; index < workEnds.Length; index++)
        {
            _workCounter.Consume(_cancellationToken);
            workEnds[index] = splitEnds[index];
        }
        long rounds = 0;
        long maximumRounds = checked((long)workPoints.Length * workPoints.Length + workEnds.Length + 1);
        while (true)
        {
            if (++rounds > maximumRounds) return false;
            ChargePairs(_workCounter, (long)workPoints.Length * workPoints.Length, _cancellationToken);
            if (!SvgContourStructure.TryFindInterLoopProperCrossing(workPoints, workEnds, out int firstLoop, out int firstEdge, out int secondLoop, out int secondEdge, out SvgDevicePoint crossing, _cancellationToken)) break;
            InsertSplitVertex(ref workPoints, workEnds, firstLoop, firstEdge, crossing);
            InsertSplitVertex(ref workPoints, workEnds, secondLoop, secondEdge, crossing);
        }
        splitPoints.Clear();
        splitEnds.Clear();
        for (int index = 0; index < workPoints.Length; index++)
        {
            _workCounter.Consume(_cancellationToken);
            splitPoints.Add(workPoints[index]);
        }
        for (int index = 0; index < workEnds.Length; index++)
        {
            _workCounter.Consume(_cancellationToken);
            splitEnds.Add(workEnds[index]);
        }
        ChargePairs(_workCounter, (long)workPoints.Length * workPoints.Length, _cancellationToken);
        return SvgContourStructure.AreSplitLoopsAdmissible(splitPoints.AsSpan(), splitEnds.AsSpan(), _cancellationToken);
    }

    /// <summary>Inserts one computed crossing vertex strictly inside a loop edge, splitting it into two nonzero edges.</summary>
    private void InsertSplitVertex(ref SvgDevicePoint[] workPoints, int[] workEnds, int loop, int edge, SvgDevicePoint vertex)
    {
        ArgumentNullException.ThrowIfNull(workPoints);
        ArgumentNullException.ThrowIfNull(workEnds);
        int loopStart = loop == 0 ? 0 : workEnds[loop - 1];
        int insertAt = loopStart + edge + 1;
        SvgDevicePoint[] nextPoints = new SvgDevicePoint[workPoints.Length + 1];
        for (int index = 0; index < insertAt; index++)
        {
            _workCounter.Consume(_cancellationToken);
            nextPoints[index] = workPoints[index];
        }
        _workCounter.Consume(_cancellationToken);
        nextPoints[insertAt] = vertex;
        for (int index = insertAt; index < workPoints.Length; index++)
        {
            _workCounter.Consume(_cancellationToken);
            nextPoints[index + 1] = workPoints[index];
        }
        for (int index = loop; index < workEnds.Length; index++)
        {
            _workCounter.Consume(_cancellationToken);
            workEnds[index] = workEnds[index] + 1;
        }
        workPoints = nextPoints;
    }

    /// <summary>Resolves point-touching contours into simple loops sharing only vertices.</summary>
    /// <remarks>Admits sets whose only defects are endpoint contacts at identical coordinates. Edges split at interior touch vertices with bitwise copies and self-touching rings split at shared vertices into lobes, so no computed points ever appear. Every loop keeps a non-shared vertex for robust nesting tests, and the split-loop guard verifies the result. Crossings, collinear overlaps and degenerate edges stay rejected, as do fully-inscribed loops and mixed sets. Output lists follow the crossing-split convention for the shared reservation, mapping and nesting steps. Charges pair estimates for scans plus one work unit per copied vertex, and observes cancellation.</remarks>
    private bool TryResolveTouchContours(SvgContourFlags structure, SvgDevicePoint[] points, int[] ends, BudgetedParserList<SvgDevicePoint> splitPoints, BudgetedParserList<int> splitEnds)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(splitPoints);
        ArgumentNullException.ThrowIfNull(splitEnds);
        if (structure == SvgContourFlags.None) return false;
        if ((structure & ~(SvgContourFlags.SelfTouch | SvgContourFlags.TouchesOtherContour)) != SvgContourFlags.None) return false;
        splitPoints.Clear();
        splitEnds.Clear();
        SvgDevicePoint[] workPoints = new SvgDevicePoint[points.Length];
        int[] workEnds = new int[ends.Length];
        for (int index = 0; index < points.Length; index++)
        {
            _workCounter.Consume(_cancellationToken);
            workPoints[index] = points[index];
        }
        for (int index = 0; index < ends.Length; index++)
        {
            _workCounter.Consume(_cancellationToken);
            workEnds[index] = ends[index];
        }
        long sweeps = 0;
        long maximumSweeps = checked((long)points.Length * points.Length + ends.Length + 1);
        while (true)
        {
            if (++sweeps > maximumSweeps) return false;
            if (SvgContourStructure.TryFindVertexOnEdge(workPoints, workEnds, out int loop, out int edge, out int vertex, _cancellationToken))
            {
                SubdivideTouchEdge(ref workPoints, ref workEnds, loop, edge, vertex);
                continue;
            }
            if (TrySplitSelfTouchLoop(ref workPoints, ref workEnds, out bool blocked))
            {
                continue;
            }
            if (blocked) return false;
            break;
        }
        if (!EveryLoopHasUnsharedVertex(workPoints, workEnds)) return false;
        for (int index = 0; index < workPoints.Length; index++)
        {
            _workCounter.Consume(_cancellationToken);
            splitPoints.Add(workPoints[index]);
        }
        for (int index = 0; index < workEnds.Length; index++)
        {
            _workCounter.Consume(_cancellationToken);
            splitEnds.Add(workEnds[index]);
        }
        ChargePairs(_workCounter, (long)workPoints.Length * workPoints.Length, _cancellationToken);
        return SvgContourStructure.AreSplitLoopsAdmissible(splitPoints.AsSpan(), splitEnds.AsSpan(), _cancellationToken);
    }

    /// <summary>Splits one edge at an interior touch vertex by inserting a bitwise copy.</summary>
    private void SubdivideTouchEdge(ref SvgDevicePoint[] workPoints, ref int[] workEnds, int loop, int edge, int vertex)
    {
        int loopStart = loop == 0 ? 0 : workEnds[loop - 1];
        int insertAt = loopStart + edge + 1;
        SvgDevicePoint[] nextPoints = new SvgDevicePoint[workPoints.Length + 1];
        int[] nextEnds = new int[workEnds.Length];
        for (int index = 0; index < insertAt; index++)
        {
            _workCounter.Consume(_cancellationToken);
            nextPoints[index] = workPoints[index];
        }
        _workCounter.Consume(_cancellationToken);
        nextPoints[insertAt] = workPoints[vertex];
        for (int index = insertAt; index < workPoints.Length; index++)
        {
            _workCounter.Consume(_cancellationToken);
            nextPoints[index + 1] = workPoints[index];
        }
        for (int index = 0; index < workEnds.Length; index++)
        {
            _workCounter.Consume(_cancellationToken);
            nextEnds[index] = workEnds[index] + (insertAt <= workEnds[index] ? 1 : 0);
        }
        workPoints = nextPoints;
        workEnds = nextEnds;
    }

    /// <summary>Splits one self-touching ring at a shared vertex into two lobes sharing only that value.</summary>
    /// <remarks>Out-and-back spikes under three edges drop only when the tip is unshared elsewhere and the main lobe keeps three vertices. Reports through blocked when a found touch cannot resolve: callers fail the whole set because the split-loop guard cannot see defects inside a single loop. Reports false with blocked clear only when no loop holds a self-touch.</remarks>
    private bool TrySplitSelfTouchLoop(ref SvgDevicePoint[] workPoints, ref int[] workEnds, out bool blocked)
    {
        blocked = false;
        for (int loop = 0; loop < workEnds.Length; loop++)
        {
            int start = loop == 0 ? 0 : workEnds[loop - 1];
            int end = workEnds[loop];
            int count = end - start;
            if (!SvgContourStructure.TryFindSelfTouch(workPoints, start, end, out int firstEdge, out int secondEdge, out SvgDevicePoint touch, _cancellationToken)) continue;
            int firstOccurrence = FindTouchOccurrence(workPoints, start, count, firstEdge, touch);
            int secondOccurrence = FindTouchOccurrence(workPoints, start, count, secondEdge, touch);
            if (firstOccurrence < 0 || secondOccurrence < 0) { blocked = true; return false; }
            int firstLength = (secondOccurrence - firstOccurrence + count) % count;
            int secondLength = count - firstLength;
            if (firstLength < 3 || secondLength < 3)
            {
                int spikeStart = firstLength < 3 ? firstOccurrence : secondOccurrence;
                int spikeLength = firstLength < 3 ? firstLength : secondLength;
                int mainLength = count - spikeLength;
                if (mainLength < 3) { blocked = true; return false; }
                for (int offset = 0; offset < spikeLength; offset++)
                {
                    SvgDevicePoint spikeVertex = workPoints[start + ((spikeStart - start + offset) % count)];
                    if (spikeVertex == touch) continue;
                    for (int other = 0; other < workEnds.Length; other++)
                    {
                        if (other == loop) continue;
                        int otherStart = other == 0 ? 0 : workEnds[other - 1];
                        for (int otherIndex = otherStart; otherIndex < workEnds[other]; otherIndex++)
                        {
                            _workCounter.Consume(_cancellationToken);
                            if (spikeVertex == workPoints[otherIndex]) { blocked = true; return false; }
                        }
                    }
                }
                DropSpikeOccurrences(ref workPoints, ref workEnds, loop, spikeStart, spikeLength);
                return true;
            }
            SvgDevicePoint[] nextPoints = new SvgDevicePoint[workPoints.Length];
            int[] nextEnds = new int[workEnds.Length + 1];
            int target = 0;
            int targetEnd = 0;
            for (int other = 0; other < loop; other++)
            {
                for (int source = other == 0 ? 0 : workEnds[other - 1]; source < workEnds[other]; source++)
                {
                    _workCounter.Consume(_cancellationToken);
                    nextPoints[target++] = workPoints[source];
                }
                _workCounter.Consume(_cancellationToken);
                nextEnds[targetEnd++] = target;
            }
            for (int offset = 0; offset < firstLength; offset++)
            {
                _workCounter.Consume(_cancellationToken);
                nextPoints[target++] = workPoints[start + ((firstOccurrence - start + offset) % count)];
            }
            _workCounter.Consume(_cancellationToken);
            nextEnds[targetEnd++] = target;
            for (int offset = 0; offset < secondLength; offset++)
            {
                _workCounter.Consume(_cancellationToken);
                nextPoints[target++] = workPoints[start + ((secondOccurrence - start + offset) % count)];
            }
            _workCounter.Consume(_cancellationToken);
            nextEnds[targetEnd++] = target;
            for (int other = loop + 1; other < workEnds.Length; other++)
            {
                for (int source = workEnds[other - 1]; source < workEnds[other]; source++)
                {
                    _workCounter.Consume(_cancellationToken);
                    nextPoints[target++] = workPoints[source];
                }
                _workCounter.Consume(_cancellationToken);
                nextEnds[targetEnd++] = target;
            }
            workPoints = nextPoints;
            workEnds = nextEnds;
            return true;
        }
        return false;
    }

    /// <summary>Drops zero-area spike occurrences from one ring, keeping the main lobe.</summary>
    private void DropSpikeOccurrences(ref SvgDevicePoint[] workPoints, ref int[] workEnds, int loop, int spikeStart, int spikeLength)
    {
        int start = loop == 0 ? 0 : workEnds[loop - 1];
        int end = workEnds[loop];
        int count = end - start;
        SvgDevicePoint[] nextPoints = new SvgDevicePoint[workPoints.Length - spikeLength];
        int[] nextEnds = new int[workEnds.Length];
        int target = 0;
        int targetEnd = 0;
        for (int other = 0; other < workEnds.Length; other++)
        {
            int otherStart = other == 0 ? 0 : workEnds[other - 1];
            int otherEnd = workEnds[other];
            if (other == loop)
            {
                for (int offset = spikeLength; offset < count; offset++)
                {
                    _workCounter.Consume(_cancellationToken);
                    nextPoints[target++] = workPoints[start + ((spikeStart - start + offset) % count)];
                }
            }
            else
            {
                for (int source = otherStart; source < otherEnd; source++)
                {
                    _workCounter.Consume(_cancellationToken);
                    nextPoints[target++] = workPoints[source];
                }
            }
            _workCounter.Consume(_cancellationToken);
            nextEnds[targetEnd++] = target;
        }
        workPoints = nextPoints;
        workEnds = nextEnds;
    }

    private static int FindTouchOccurrence(SvgDevicePoint[] workPoints, int start, int count, int edge, SvgDevicePoint touch)
    {
        int first = start + edge;
        int second = start + ((edge + 1) % count);
        if (workPoints[first] == touch) return first;
        if (workPoints[second] == touch) return second;
        return -1;
    }

    /// <summary>Reports whether every loop keeps a vertex shared with no other loop.</summary>
    private bool EveryLoopHasUnsharedVertex(SvgDevicePoint[] workPoints, int[] workEnds)
    {
        for (int loop = 0; loop < workEnds.Length; loop++)
        {
            int start = loop == 0 ? 0 : workEnds[loop - 1];
            int end = workEnds[loop];
            bool found = false;
            for (int index = start; index < end && !found; index++)
            {
                _workCounter.Consume(_cancellationToken);
                bool shared = false;
                for (int other = 0; other < workEnds.Length && !shared; other++)
                {
                    if (other == loop) continue;
                    int otherStart = other == 0 ? 0 : workEnds[other - 1];
                    for (int otherIndex = otherStart; otherIndex < workEnds[other]; otherIndex++)
                    {
                        _workCounter.Consume(_cancellationToken);
                        if (workPoints[index] == workPoints[otherIndex]) { shared = true; break; }
                    }
                }
                if (!shared) found = true;
            }
            if (!found) return false;
        }
        return true;
    }



    /// <summary>Upgrades quarter-edge mapping to finite edges when split vertices leave the quarter grid.</summary>
    /// <remarks>Split crossings land at arbitrary finite coordinates while polygon lowering assumes quarter-grid vertices; the scan mirrors the stroke-outline fractionality check. Charges one work unit per scanned vertex and observes cancellation.</remarks>
    private SvgPolygonMapping UpgradeSplitMapping(SvgDevicePoint[] retained, SvgPolygonMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(retained);
        if (mapping != SvgPolygonMapping.QuarterEdges) return mapping;
        foreach (SvgDevicePoint vertex in retained)
        {
            _workCounter.Consume(_cancellationToken);
            if (Math.Abs(vertex.X * 4 - Math.Round(vertex.X * 4, MidpointRounding.AwayFromZero)) > 0.000000001 || Math.Abs(vertex.Y * 4 - Math.Round(vertex.Y * 4, MidpointRounding.AwayFromZero)) > 0.000000001) return SvgPolygonMapping.FiniteEdges;
        }

        return mapping;
    }


    private static void ChargePairs(ParserWorkCounter workCounter, long pairs, CancellationToken cancellationToken)
    {
        for (long charged = 0; charged < pairs; charged += ParserWorkCounter.ByteCheckpointSize) workCounter.Consume(cancellationToken);
    }

    private static void ValidateDeviceRange(SvgDevicePoint[] retained)
    {
        foreach (SvgDevicePoint vertex in retained)
        {
            if (!double.IsFinite(vertex.X) || !double.IsFinite(vertex.Y) || Math.Abs(vertex.X) > int.MaxValue || Math.Abs(vertex.Y) > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill coordinates exceed the admitted numeric range.");
        }
    }

    private SvgParsedGeometry RetainPolygonFill(SvgDevicePoint[] retained, int[] ends, int[]? signs, SvgColor color, bool hasFill, bool isEvenOdd, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget.BudgetReservation? reservation, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(retained);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(budget);
        SvgPolygonFill fill = SvgPolygonFill.Create(retained, ends, signs, color, hasFill, isEvenOdd, mapping, clip, reservation, budget);
        try { _geometries.Add(SvgGeometry.FromPolygonFill(fill)); }
        catch { fill.Dispose(); throw; }
        _geometryElementCount++;
        try { return new SvgParsedGeometry(_geometries.Detach(), _geometryElementCount); }
        catch { foreach (SvgGeometry geometry in _geometries.AsSpan()) geometry.Dispose(); throw; }
    }

    /// <summary>Retains one gradient polygon fill from validated separated contours with scene-owned storage.</summary>
    private SvgParsedGeometry FinishGradientPolygonFill(BudgetedParserList<SvgDevicePoint> mapped, int[] ends, SvgLinearGradient gradient, SvgRectangle box, bool hasFill, byte opacity, SvgPolygonMapping mapping, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(mapped);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(budget);
        SeparatedContours separated = PrepareSeparatedContours(mapped, ends, mapping, budget);
        return RetainGradientPolygonFill(separated.Points, separated.Ends, separated.Signs, gradient, box, hasFill, opacity, _isEvenOdd, separated.Mapping, frame is null ? (SvgClipBox?)null : frame.Value.Clip, separated.SceneReservation, budget);
    }

    private SvgParsedGeometry RetainGradientPolygonFill(SvgDevicePoint[] retained, int[] ends, int[]? signs, SvgLinearGradient gradient, SvgRectangle box, bool hasFill, byte opacity, bool isEvenOdd, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget.BudgetReservation? reservation, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(retained);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(budget);
        SvgGradientPolygon fill = SvgGradientPolygon.Create(retained, ends, signs, gradient, box, hasFill, isEvenOdd, opacity, mapping, clip, reservation, budget);
        try { _geometries.Add(SvgGeometry.FromGradientPolygon(fill)); }
        catch { fill.Dispose(); throw; }
        _geometryElementCount++;
        try { return new SvgParsedGeometry(_geometries.Detach(), _geometryElementCount); }
        catch { foreach (SvgGeometry geometry in _geometries.AsSpan()) geometry.Dispose(); throw; }
    }

    /// <summary>Retains one radial polygon fill from validated separated contours with scene-owned storage.</summary>
    private SvgParsedGeometry FinishRadialPolygonFill(BudgetedParserList<SvgDevicePoint> mapped, int[] ends, SvgRadialGradient gradient, SvgRectangle box, bool hasFill, byte opacity, SvgPolygonMapping mapping, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(mapped);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(budget);
        SeparatedContours separated = PrepareSeparatedContours(mapped, ends, mapping, budget);
        return RetainRadialPolygonFill(separated.Points, separated.Ends, separated.Signs, gradient, box, hasFill, opacity, _isEvenOdd, separated.Mapping, frame is null ? (SvgClipBox?)null : frame.Value.Clip, separated.SceneReservation, budget);
    }

    private SvgParsedGeometry RetainRadialPolygonFill(SvgDevicePoint[] retained, int[] ends, int[]? signs, SvgRadialGradient gradient, SvgRectangle box, bool hasFill, byte opacity, bool isEvenOdd, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget.BudgetReservation? reservation, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(retained);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(budget);
        SvgRadialPolygon fill = SvgRadialPolygon.Create(retained, ends, signs, gradient, box, hasFill, isEvenOdd, opacity, mapping, clip, reservation, budget);
        try { _geometries.Add(SvgGeometry.FromRadialPolygon(fill)); }
        catch { fill.Dispose(); throw; }
        _geometryElementCount++;
        try { return new SvgParsedGeometry(_geometries.Detach(), _geometryElementCount); }
        catch { foreach (SvgGeometry geometry in _geometries.AsSpan()) geometry.Dispose(); throw; }
    }

    public void Dispose()
    {
        _geometries.Dispose();
        _intersections.Dispose();
        _boundaries.Dispose();
    }

    internal static void ValidateSimpleContour(BudgetedParserList<SvgPoint> points, string elementName, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (SvgContourTopology.Classify(points.AsSpan(), 0, points.Count) == SvgContourKind.Diagonal) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned " + elementName + " contours are implemented.");
        for (int firstIndex = 0; firstIndex < points.Count; firstIndex++)
        {
            SvgPoint firstStart = points[firstIndex]; SvgPoint firstEnd = points[(firstIndex + 1) % points.Count];
            bool firstHorizontal = firstStart.Y == firstEnd.Y;
            for (int secondIndex = firstIndex + 1; secondIndex < points.Count; secondIndex++)
            {
                workCounter.Consume(cancellationToken);
                SvgPoint secondStart = points[secondIndex]; SvgPoint secondEnd = points[(secondIndex + 1) % points.Count];
                bool secondHorizontal = secondStart.Y == secondEnd.Y;
                if (!EdgesIntersect(firstStart, firstEnd, firstHorizontal, secondStart, secondEnd, secondHorizontal)) continue;
                bool adjacent = secondIndex == firstIndex + 1 || (firstIndex == 0 && secondIndex == points.Count - 1);
                SvgPoint shared = secondIndex == firstIndex + 1 ? firstEnd : firstStart;
                if (adjacent && firstHorizontal != secondHorizontal && (firstHorizontal ? new SvgPoint(secondStart.X, firstStart.Y) : new SvgPoint(firstStart.X, secondStart.Y)) != shared) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer " + elementName + " contours are implemented.");
            }
        }
    }

    internal static void ValidateContours(BudgetedParserList<SvgPoint> points, BudgetedParserList<int> contourEnds, string elementName, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => ValidateContours(points, contourEnds, elementName, workCounter, cancellationToken, false);

    /// <summary>Validates band contours, deferring diagonal simplicity checks to outline lowering when admitted; spatial classification still rejects touching contours there.</summary>
    internal static void ValidateContours(BudgetedParserList<SvgPoint> points, BudgetedParserList<int> contourEnds, string elementName, ParserWorkCounter workCounter, CancellationToken cancellationToken, bool allowDiagonal)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(contourEnds);
        ArgumentNullException.ThrowIfNull(workCounter);
        if (contourEnds.Count == 0 || contourEnds[contourEnds.Count - 1] != points.Count) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer path contours are implemented.");
        int contourStart = 0;
        for (int contourIndex = 0; contourIndex < contourEnds.Count; contourIndex++)
        {
            int contourEnd = contourEnds[contourIndex];
            if (contourEnd <= contourStart || contourEnd - contourStart < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer path contours are implemented.");
            contourStart = contourEnd;
        }
        contourStart = 0;
        for (int contourIndex = 0; contourIndex < contourEnds.Count; contourIndex++)
        {
            int contourEnd = contourEnds[contourIndex];
            if (SvgContourTopology.Classify(points.AsSpan(), contourStart, contourEnd) == SvgContourKind.Diagonal && !allowDiagonal) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned " + elementName + " contours are implemented.");
            ValidateContourSlice(points, contourStart, contourEnd, elementName, workCounter, cancellationToken);
            contourStart = contourEnd;
        }
    }

    private static void ValidateContourSlice(BudgetedParserList<SvgPoint> points, int contourStart, int contourEnd, string elementName, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        for (int firstIndex = contourStart; firstIndex < contourEnd; firstIndex++)
        {
            SvgPoint firstStart = points[firstIndex]; SvgPoint firstEnd = firstIndex + 1 < contourEnd ? points[firstIndex + 1] : points[contourStart];
            bool firstHorizontal = firstStart.Y == firstEnd.Y;
            for (int secondIndex = firstIndex + 1; secondIndex < contourEnd; secondIndex++)
            {
                workCounter.Consume(cancellationToken);
                SvgPoint secondStart = points[secondIndex]; SvgPoint secondEnd = secondIndex + 1 < contourEnd ? points[secondIndex + 1] : points[contourStart];
                bool secondHorizontal = secondStart.Y == secondEnd.Y;
                if (!EdgesIntersect(firstStart, firstEnd, firstHorizontal, secondStart, secondEnd, secondHorizontal)) continue;
                bool adjacent = secondIndex == firstIndex + 1 || (firstIndex == contourStart && secondIndex == contourEnd - 1);
                SvgPoint shared = secondIndex == firstIndex + 1 ? firstEnd : firstStart;
                if (adjacent && firstHorizontal != secondHorizontal && (firstHorizontal ? new SvgPoint(secondStart.X, firstStart.Y) : new SvgPoint(firstStart.X, secondStart.Y)) != shared) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer " + elementName + " contours are implemented.");
            }
        }
    }

    private static bool EdgesIntersect(SvgPoint firstStart, SvgPoint firstEnd, bool firstHorizontal, SvgPoint secondStart, SvgPoint secondEnd, bool secondHorizontal)
        => firstHorizontal == secondHorizontal
            ? firstHorizontal
                ? firstStart.Y == secondStart.Y && Math.Max(Math.Min(firstStart.X, firstEnd.X), Math.Min(secondStart.X, secondEnd.X)) <= Math.Min(Math.Max(firstStart.X, firstEnd.X), Math.Max(secondStart.X, secondEnd.X))
                : firstStart.X == secondStart.X && Math.Max(Math.Min(firstStart.Y, firstEnd.Y), Math.Min(secondStart.Y, secondEnd.Y)) <= Math.Min(Math.Max(firstStart.Y, firstEnd.Y), Math.Max(secondStart.Y, secondEnd.Y))
            : firstHorizontal
                ? Math.Min(firstStart.X, firstEnd.X) <= secondStart.X && secondStart.X <= Math.Max(firstStart.X, firstEnd.X) && Math.Min(secondStart.Y, secondEnd.Y) <= firstStart.Y && firstStart.Y <= Math.Max(secondStart.Y, secondEnd.Y)
                : Math.Min(secondStart.X, secondEnd.X) <= firstStart.X && firstStart.X <= Math.Max(secondStart.X, secondEnd.X) && Math.Min(firstStart.Y, firstEnd.Y) <= secondStart.Y && secondStart.Y <= Math.Max(firstStart.Y, firstEnd.Y);

    private string SimpleShapeMessage() => _isPath ? "Only closed axis-aligned integer path contours are implemented." : "Only closed axis-aligned integer " + _elementName + " contours are implemented.";
}

/// <summary>Owns the exact retained typed-geometry array produced by one lowered orthogonal shape.</summary>
internal readonly record struct SvgParsedGeometry(OwnedParserArray<SvgGeometry> Storage, int ElementCount) : IDisposable
{
    internal ReadOnlySpan<SvgGeometry> Geometries => Storage.Items;

    public void Dispose() => Storage.Dispose();
}
