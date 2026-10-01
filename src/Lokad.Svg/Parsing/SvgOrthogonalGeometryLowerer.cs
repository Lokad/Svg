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

    /// <summary>Bounds near-span conforming deviation to one device pixel.</summary>
    /// <remarks>Variant arcs flatten to spans that share endpoints but weave within a pixel, which exact subdivision can never align; conforming such lenses moves boundaries within accepted union fringe (the pinned oracle adjudicates per admission), far above weld dust yet far below feature scale. Deterministic bound; allocates nothing.</remarks>
    private const double UnionConformEpsilon = 1.0;

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
        return AppendMappedContour(mapped, source, null, workCounter, cancellationToken);
    }

    /// <summary>Appends one stripped contour while recording each source index surviving mapped index into indexMap, or -1 for dropped vertices.</summary>
    /// <remarks>Callers pass a map sized to the source slice only when translating retained spans; every other route passes null with identical stripping. The map lets span translation resolve endpoints without value matching.</remarks>
    private static int AppendMappedContour(BudgetedParserList<SvgDevicePoint> mapped, ReadOnlySpan<SvgDevicePoint> source, int[]? indexMap, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mapped);
        ArgumentNullException.ThrowIfNull(workCounter);
        if (indexMap is not null && indexMap.Length != source.Length) throw new ArgumentOutOfRangeException(nameof(indexMap));
        SvgDevicePoint previous = default;
        SvgDevicePoint first = default;
        bool hasKept = false;
        int keptBefore = mapped.Count;
        for (int index = 0; index < source.Length; index++)
        {
            workCounter.Consume(cancellationToken);
            SvgDevicePoint vertex = source[index];
            if (hasKept && previous == vertex)
            {
                if (indexMap is not null) indexMap[index] = -1;
                continue;
            }
            if (hasKept && vertex == first && index == source.Length - 1)
            {
                if (indexMap is not null) indexMap[index] = -1;
                continue;
            }
            if (!hasKept) first = vertex;
            previous = vertex;
            hasKept = true;
            if (indexMap is not null) indexMap[index] = mapped.Count;
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
        // Contours start at most once per command plus the implicit first, so command-bounded contour arrays stay below the point bound without changing admission.
        int contourCapacity = commandCount + 1 > pointCapacity ? pointCapacity : commandCount + 1;
        if (contourCapacity > _maximumGeometryElements) contourCapacity = _maximumGeometryElements;
        long scratchBytes;
        try { scratchBytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(contourCapacity) + 2 * ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(commandCount) + 2 * ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(commandCount) + ParserCollectionMemory.GetArrayByteCount<int>(pointCapacity)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Curve flattening exceeds the supported expansion range."); }
        using RenderBudget.BudgetReservation scratch = budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
        SvgContourBuilder builder = hasCurve ? SvgContourBuilder.CreateGrowing(pointCapacity, contourCapacity) : new(pointCapacity, contourCapacity);
        if (hasCurve)
        {
            builder.EnableQuadSpanRetention();
            builder.EnableCubicSpanRetention();
        }
        SvgPathDataParser.ParsePath(d, deviceMap, builder, flattenPolicy, _maximumGeometryElements, _workCounter, _cancellationToken, null);
        using BudgetedParserList<SvgDevicePoint> mapped = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        SvgQuadSpan[] recorded = builder.GetQuadSpans();
        List<SvgQuadSpan> keptSpans = new(recorded.Length);
        SvgCubicSpan[] recordedCubics = builder.GetCubicSpans();
        List<SvgCubicSpan> keptCubicSpans = new(recordedCubics.Length);
        int[] ends = new int[builder.ContourCount];
        int kept = 0;
        int spanCursor = 0;
        int cubicSpanCursor = 0;
        for (int contour = 0; contour < builder.ContourCount; contour++)
        {
            int before = mapped.Count;
            ReadOnlySpan<SvgDevicePoint> slice = builder.GetContour(contour);
            while (spanCursor < recorded.Length && recorded[spanCursor].Contour < contour) spanCursor++;
            int spanRunEnd = spanCursor;
            while (spanRunEnd < recorded.Length && recorded[spanRunEnd].Contour == contour) spanRunEnd++;
            while (cubicSpanCursor < recordedCubics.Length && recordedCubics[cubicSpanCursor].Contour < contour) cubicSpanCursor++;
            int cubicSpanRunEnd = cubicSpanCursor;
            while (cubicSpanRunEnd < recordedCubics.Length && recordedCubics[cubicSpanRunEnd].Contour == contour) cubicSpanRunEnd++;
            int[]? indexMap = (spanRunEnd > spanCursor || cubicSpanRunEnd > cubicSpanCursor) ? new int[slice.Length] : null;
            if (AppendMappedContour(mapped, slice, indexMap, _workCounter, _cancellationToken) < 3)
            {
                // Degenerate contours enclose no area while strokes outline them separately, mirroring singleton-drop tolerance.
                while (mapped.Count > before) mapped.RemoveLast();
                continue;
            }
            ends[kept++] = mapped.Count;
            if (indexMap is not null)
            {
                TranslateQuadSpans(recorded, spanCursor, spanRunEnd, kept - 1, indexMap, keptSpans);
                TranslateCubicSpans(recordedCubics, cubicSpanCursor, cubicSpanRunEnd, kept - 1, indexMap, keptCubicSpans);
            }
        }
        if (kept != ends.Length) Array.Resize(ref ends, kept);

        return FinishPolygonFill(mapped, ends, keptSpans.ToArray(), keptCubicSpans.ToArray(), element.Presentation.Color, element.Presentation.HasFill, mapping, frame, budget);
    }

    /// <summary>Translates one contour slice run of recorded spans into mapped-global indices, renumbering to the kept contour and dropping endpoints lost to stripping.</summary>
    private static void TranslateQuadSpans(SvgQuadSpan[] recorded, int runStart, int runEnd, int keptContour, int[] indexMap, List<SvgQuadSpan> keptSpans)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(indexMap);
        ArgumentNullException.ThrowIfNull(keptSpans);
        for (int index = runStart; index < runEnd; index++)
        {
            SvgQuadSpan span = recorded[index];
            if (span.StartIndex < 0 || span.StartIndex >= indexMap.Length || span.EndIndex < 0 || span.EndIndex >= indexMap.Length) continue;
            int mappedStart = indexMap[span.StartIndex];
            int mappedEnd = indexMap[span.EndIndex];
            if (mappedStart < 0 || mappedEnd < 0 || mappedEnd <= mappedStart) continue;
            keptSpans.Add(new SvgQuadSpan(keptContour, mappedStart, mappedEnd, span.Control));
        }
    }

    /// <summary>Translates one contour slice run of recorded cubic spans into mapped-global indices, renumbering to the kept contour and dropping endpoints lost to stripping.</summary>
    private static void TranslateCubicSpans(SvgCubicSpan[] recorded, int runStart, int runEnd, int keptContour, int[] indexMap, List<SvgCubicSpan> keptSpans)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(indexMap);
        ArgumentNullException.ThrowIfNull(keptSpans);
        for (int index = runStart; index < runEnd; index++)
        {
            SvgCubicSpan span = recorded[index];
            if (span.StartIndex < 0 || span.StartIndex >= indexMap.Length || span.EndIndex < 0 || span.EndIndex >= indexMap.Length) continue;
            int mappedStart = indexMap[span.StartIndex];
            int mappedEnd = indexMap[span.EndIndex];
            if (mappedStart < 0 || mappedEnd < 0 || mappedEnd <= mappedStart) continue;
            keptSpans.Add(new SvgCubicSpan(keptContour, mappedStart, mappedEnd, span.FirstControl, span.SecondControl));
        }
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
    internal SvgParsedGeometry LowerStrokePointList(BudgetedParserList<SvgFractionalPoint> points, bool closed, bool hasCurves, SvgResolvedPaintableElement element, SvgColor color, bool hasFill, double width, double miterLimit, SvgStrokeCap cap, SvgStrokeJoin join, double[]? dashes, double dashOffset, double? pathLength, bool nonScaling, SvgViewportFrame? frame, RenderBudget budget)
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
        SvgParsedGeometry stroked = LowerStrokeContours(builder, closedEnds, hasCurves, element.Transform, width, miterLimit, cap, join, dashes, dashOffset, pathLength, nonScaling, color, hasFill, frame, budget);
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
        StrokeDashedContour(mapped.AsSpan(), closed, pattern, scaledOffset, deviceWidth, miterLimit, cap, join, false, outlines, leading, current);
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
    private void StrokeDashedContour(ReadOnlySpan<SvgDevicePoint> contour, bool closed, double[] pattern, double offset, double width, double miterLimit, SvgStrokeCap cap, SvgStrokeJoin join, bool hasCurves, SvgContourBuilder outlines, BudgetedParserList<SvgDevicePoint> leading, BudgetedParserList<SvgDevicePoint> current)
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
        int leadOpenSeg = -1;
        double leadOpenFrac = 0;
        int leadCloseSeg = -1;
        double leadCloseFrac = 1;
        int currOpenSeg = -1;
        double currOpenFrac = 0;
        int currCloseSeg = -1;
        double currCloseFrac = 1;
        int lastSeg = -1;
        BudgetedParserList<SvgDevicePoint> active = leading;
        int segments = closed ? contour.Length : contour.Length - 1;
        for (int index = 0; index < segments; index++)
        {
            if ((index & 63) == 0) _cancellationToken.ThrowIfCancellationRequested();
            SvgDevicePoint segmentStart = contour[index];
            SvgDevicePoint segmentEnd = contour[(index + 1) % contour.Length];
            if (segmentStart == segmentEnd) continue;
            lastSeg = index;
            _workCounter.Consume(_cancellationToken);
            double segmentLength = Math.Sqrt((segmentEnd.X - segmentStart.X) * (segmentEnd.X - segmentStart.X) + (segmentEnd.Y - segmentStart.Y) * (segmentEnd.Y - segmentStart.Y));
            double traveled = 0;
            while (true)
            {
                if (onEntry && !runOpen)
                {
                    double openFrac = traveled == 0 ? 0 : traveled / segmentLength;
                    active.Add(traveled == 0 ? segmentStart : DashCutPoint(segmentStart, segmentEnd, openFrac));
                    if (runs == 0) { leadOpenSeg = index; leadOpenFrac = openFrac; }
                    else { currOpenSeg = index; currOpenFrac = openFrac; }
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
                    double closeFrac = finished ? 1 : traveled / segmentLength;
                    active.Add(finished ? segmentEnd : DashCutPoint(segmentStart, segmentEnd, closeFrac));
                    int openSeg = runs == 0 ? leadOpenSeg : currOpenSeg;
                    double openFrac = runs == 0 ? leadOpenFrac : currOpenFrac;
                    if (runs == 0) { leadCloseSeg = index; leadCloseFrac = closeFrac; }
                    else { currCloseSeg = index; currCloseFrac = closeFrac; }
                    if (runs == 0) active = current;
                    else
                    {
                        if (!SvgStrokeOutline.IsAllIdentical(active.AsSpan())) StrokeDashRun(active.AsSpan(), width, miterLimit, cap, join, contour, closed, openSeg, openFrac, index, closeFrac, hasCurves, outlines);
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
            if (runs == 0) { leadCloseSeg = lastSeg; leadCloseFrac = 1; }
            else { currCloseSeg = lastSeg; currCloseFrac = 1; }
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
            if (!SvgStrokeOutline.IsAllIdentical(current.AsSpan())) StrokeDashRun(current.AsSpan(), width, miterLimit, cap, join, contour, closed, currOpenSeg, currOpenFrac, leadCloseSeg, leadCloseFrac, hasCurves, outlines);
        }
        else
        {
            if (!SvgStrokeOutline.IsAllIdentical(leading.AsSpan())) StrokeDashRun(leading.AsSpan(), width, miterLimit, cap, join, contour, closed, leadOpenSeg, leadOpenFrac, leadCloseSeg, leadCloseFrac, hasCurves, outlines);
            if (endedAtEnd && current.Count > 0 && !SvgStrokeOutline.IsAllIdentical(current.AsSpan())) StrokeDashRun(current.AsSpan(), width, miterLimit, cap, join, contour, closed, currOpenSeg, currOpenFrac, currCloseSeg, currCloseFrac, hasCurves, outlines);
        }

        static SvgDevicePoint DashCutPoint(SvgDevicePoint start, SvgDevicePoint end, double fraction)
            => new(start.X + (end.X - start.X) * fraction, start.Y + (end.Y - start.Y) * fraction);
    }

    /// <summary>Strokes one dash run with parabolic end tangents on curves and chord caps elsewhere.</summary>
    private void StrokeDashRun(ReadOnlySpan<SvgDevicePoint> run, double width, double miterLimit, SvgStrokeCap cap, SvgStrokeJoin join, ReadOnlySpan<SvgDevicePoint> contour, bool closed, int openSeg, double openFrac, int closeSeg, double closeFrac, bool hasCurves, SvgContourBuilder outlines)
    {
        if (!hasCurves) { SvgStrokeOutline.StrokeOpenPolyline(run, width, miterLimit, cap, join, outlines, _cancellationToken); return; }
        SvgStrokeOutline.StrokeOpenPolyline(run, width, miterLimit, cap, join, DashEndpointTangent(contour, closed, openSeg, openFrac), DashEndpointTangent(contour, closed, closeSeg, closeFrac), outlines, _cancellationToken);
    }

    /// <summary>Differentiates a chordal parabola through the cut neighborhood for a dash-cut tangent, falling back to the cut chord.</summary>
    /// <remarks>Vertex cuts keep chord directions so offset-zero dash behavior holds bitwise; interior cuts on curves differentiate the Lagrange parabola through the previous joint, the cut start and the cut end at the cut position, which matches uniformly sampled tangents and stays second-order otherwise. Pure deterministic arithmetic with no budget or cancellation footprint.</remarks>
    private static (double X, double Y) DashEndpointTangent(ReadOnlySpan<SvgDevicePoint> contour, bool closed, int edgeIndex, double fraction)
    {
        int count = contour.Length;
        SvgDevicePoint cutStart = contour[edgeIndex];
        SvgDevicePoint cutEnd = closed ? contour[(edgeIndex + 1) % count] : contour[edgeIndex + 1];
        (double chordX, double chordY) = SvgStrokeOutline.UnitDirection(cutStart, cutEnd);
        if (!(fraction >= 0) || !(fraction <= 1)) return (chordX, chordY);
        int beforeIndex = closed ? ((edgeIndex - 1 + count) % count) : edgeIndex - 1;
        if (beforeIndex < 0 || beforeIndex >= count) return (chordX, chordY);
        SvgDevicePoint joint = contour[beforeIndex];
        if (joint == cutStart || joint == cutEnd) return (chordX, chordY);
        double firstLeg = ChordLength(joint, cutStart);
        double secondLeg = ChordLength(cutStart, cutEnd);
        if (firstLeg == 0 || secondLeg == 0) return (chordX, chordY);
        double position = firstLeg + fraction * secondLeg;
        double total = firstLeg + secondLeg;
        double firstWeight = ((position - firstLeg) + (position - firstLeg - secondLeg)) / ((0 - firstLeg) * (0 - firstLeg - secondLeg));
        double secondWeight = ((position - 0) + (position - firstLeg - secondLeg)) / ((firstLeg - 0) * (firstLeg - firstLeg - secondLeg));
        double thirdWeight = ((position - 0) + (position - firstLeg)) / ((firstLeg + secondLeg - 0) * (firstLeg + secondLeg - firstLeg));
        double tangentX = firstWeight * joint.X + secondWeight * cutStart.X + thirdWeight * cutEnd.X;
        double tangentY = firstWeight * joint.Y + secondWeight * cutStart.Y + thirdWeight * cutEnd.Y;
        double length = System.Math.Sqrt(tangentX * tangentX + tangentY * tangentY);
        if (!double.IsFinite(length) || length == 0) return (chordX, chordY);
        return (tangentX / length, tangentY / length);

        static double ChordLength(SvgDevicePoint from, SvgDevicePoint to)
        {
            double deltaX = from.X - to.X;
            double deltaY = from.Y - to.Y;
            return System.Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        }
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
            StrokeDashedContour(source.GetContour(contour), closedEnds[contour], pattern, scaledOffset, deviceWidth, miterLimit, cap, join, hasCurves, outlines, leading, current);
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
        // Contours start at most once per command plus the implicit first, so command-bounded contour arrays stay below the point bound without changing admission.
        int contourCapacity = commandCount + 1 > pointCapacity ? pointCapacity : commandCount + 1;
        if (contourCapacity > _maximumGeometryElements) contourCapacity = _maximumGeometryElements;
        long scratchBytes;
        try { scratchBytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(contourCapacity)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the supported expansion range."); }
        using RenderBudget.BudgetReservation scratch = budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
        SvgContourBuilder builder = hasCurves ? SvgContourBuilder.CreateGrowing(pointCapacity, contourCapacity) : new(pointCapacity, contourCapacity);
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
        RenderBudget.BudgetReservation? reservation = ReserveScene(retainedOutlines.Length, ends.Length, ends.Length, 0, 0, budget);
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
            return RetainPolygonFill(retainedOutlines, ends, roles, Array.Empty<SvgQuadSpan>(), Array.Empty<SvgCubicSpan>(), color, hasFill, false, mapping, frame is null ? (SvgClipBox?)null : frame.Value.Clip, reservation, budget);
        }
        catch
        {
            reservation?.Dispose();
            throw;
        }
    }

    /// <summary>Validated separated contour storage with resolved combination weights and its scene reservation, ready for paint-specific retention. The reservation transfers to the retained fill.</summary>
    private readonly record struct SeparatedContours(SvgDevicePoint[] Points, int[] Ends, int[] Signs, SvgQuadSpan[] Spans, SvgCubicSpan[] CubicSpans, SvgPolygonMapping Mapping, RenderBudget.BudgetReservation? SceneReservation);

    /// <summary>Reserves exact scene storage for retained contour and span arrays before allocation, returning null when nothing is retained.</summary>
    private static RenderBudget.BudgetReservation? ReserveScene(int pointCount, int endCount, int signCount, int spanCount, int cubicSpanCount, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        long bytes;
        try { bytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCount) + ParserCollectionMemory.GetArrayByteCount<int>(endCount) + ParserCollectionMemory.GetArrayByteCount<int>(signCount) + ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(spanCount) + ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(cubicSpanCount)); }
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

    /// <summary>Separates contours without recorded curves; quadratic spans stay empty on this route.</summary>
    private SeparatedContours PrepareSeparatedContours(BudgetedParserList<SvgDevicePoint> mapped, int[] ends, SvgPolygonMapping mapping, RenderBudget budget)
    {
        return PrepareSeparatedContours(mapped, ends, Array.Empty<SvgQuadSpan>(), Array.Empty<SvgCubicSpan>(), mapping, budget);
    }

    /// <summary>Separates contours carrying quadratic spans, dropping them across any rewriting stage.</summary>
    /// <remarks>Weld compaction, identical-contour merging and every crossing resolver rewrite point indices, so spans survive only the clean path where points flow through bitwise; the walker re-gates on structure anyway.</remarks>
    private SeparatedContours PrepareSeparatedContours(BudgetedParserList<SvgDevicePoint> mapped, int[] ends, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, SvgPolygonMapping mapping, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(mapped);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(quadSpans);
        ArgumentNullException.ThrowIfNull(cubicSpans);
        ArgumentNullException.ThrowIfNull(budget);
        RenderBudget.BudgetReservation? reservation = ReserveScene(mapped.Count, ends.Length, ends.Length, quadSpans.Length, cubicSpans.Length, budget);
        try
        {
            SvgDevicePoint[] retained = mapped.AsSpan().ToArray();
            ValidateDeviceRange(retained);
            int[] weldedEnds = new int[ends.Length];
            int weldedTotal = WeldDustJoints(retained, ends, weldedEnds, _workCounter, _cancellationToken);
            if (weldedTotal < retained.Length)
            {
                quadSpans = Array.Empty<SvgQuadSpan>();
                cubicSpans = Array.Empty<SvgCubicSpan>();
                reservation?.Dispose();
                reservation = ReserveScene(weldedTotal, ends.Length, ends.Length, quadSpans.Length, cubicSpans.Length, budget);
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
                quadSpans = Array.Empty<SvgQuadSpan>();
                cubicSpans = Array.Empty<SvgCubicSpan>();
                reservation?.Dispose();
                reservation = ReserveScene(mergedPoints.Length, mergedEnds.Length, mergedEnds.Length, quadSpans.Length, cubicSpans.Length, budget);
                retained = mergedPoints;
                ends = mergedEnds;
            }
            SvgContourFlags structure = SvgContourStructure.ClassifySet(retained, ends, _cancellationToken);
            bool touchResolved = false;
            bool unionResolved = false;
            bool mixedResolved = false;
            bool interLoopResolved = false;
            bool subdividedCrossings = false;
            if (structure != SvgContourFlags.None)
            {
                quadSpans = Array.Empty<SvgQuadSpan>();
                cubicSpans = Array.Empty<SvgCubicSpan>();
                using BudgetedParserList<SvgDevicePoint> splitPoints = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
                using BudgetedParserList<int> splitEnds = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
                if (!TrySplitCrossingContours(structure, retained, ends, _isEvenOdd, splitPoints, splitEnds, budget, out interLoopResolved))
                {
                    splitPoints.Clear();
                    splitEnds.Clear();
                    if (!TryResolveCoincidentUnion(structure, retained, ends, splitPoints, splitEnds, out unionResolved))
                    {
                        splitPoints.Clear();
                        splitEnds.Clear();
                        if (!TryResolveTouchContours(structure, retained, ends, splitPoints, splitEnds, out subdividedCrossings))
                        {
                            splitPoints.Clear();
                            splitEnds.Clear();
                            if (!TryResolveMixedContactUnion(structure, retained, ends, splitPoints, splitEnds, out mixedResolved)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only simple diagonal polygon contours are implemented; touching and self-intersecting contours stay unsupported.");
                        }
                        else touchResolved = true;
                    }
                }
                reservation?.Dispose();
                reservation = ReserveScene(splitPoints.Count, splitEnds.Count, splitEnds.Count, quadSpans.Length, cubicSpans.Length, budget);
                retained = splitPoints.AsSpan().ToArray();
                ends = splitEnds.AsSpan().ToArray();
                ValidateDeviceRange(retained);
                mapping = UpgradeSplitMapping(retained, mapping);
            }
            int[] signs = new int[ends.Length];
            ChargePairs(_workCounter, (long)ends.Length * (ends.Length - 1) / 2, _cancellationToken);
            bool skipSharedVertices = touchResolved || interLoopResolved || unionResolved || mixedResolved;
            if (_isEvenOdd) SvgContourNesting.ComputeParitySigns(retained, ends, signs, skipSharedVertices, _cancellationToken);
            else SvgContourNesting.ComputeWindingRoles(retained, ends, signs, skipSharedVertices, _cancellationToken);
            if (_isEvenOdd && touchResolved && subdividedCrossings)
            {
                if (ends.Length != 2) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only simple diagonal polygon contours are implemented; touching and self-intersecting contours stay unsupported.");
                signs[0] = 1;
                signs[1] = -1;
            }
            return new SeparatedContours(retained, ends, signs, quadSpans, cubicSpans, mapping, reservation);
        }
        catch
        {
            reservation?.Dispose();
            throw;
        }
    }

    /// <summary>Retains one polygon fill without recorded curves; quadratic spans stay empty on this route.</summary>
    private SvgParsedGeometry FinishPolygonFill(BudgetedParserList<SvgDevicePoint> mapped, int[] ends, SvgColor color, bool hasFill, SvgPolygonMapping mapping, SvgViewportFrame? frame, RenderBudget budget)
    {
        return FinishPolygonFill(mapped, ends, Array.Empty<SvgQuadSpan>(), Array.Empty<SvgCubicSpan>(), color, hasFill, mapping, frame, budget);
    }

    /// <summary>Retains one polygon fill carrying translated quadratic spans into separation.</summary>
    private SvgParsedGeometry FinishPolygonFill(BudgetedParserList<SvgDevicePoint> mapped, int[] ends, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, SvgColor color, bool hasFill, SvgPolygonMapping mapping, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(mapped);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(budget);
        SeparatedContours separated = PrepareSeparatedContours(mapped, ends, quadSpans, cubicSpans, mapping, budget);
        return RetainPolygonFill(separated.Points, separated.Ends, separated.Signs, separated.Spans, separated.CubicSpans, color, hasFill, _isEvenOdd, separated.Mapping, frame is null ? (SvgClipBox?)null : frame.Value.Clip, separated.SceneReservation, budget);
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

    /// <summary>Unions same-fill loops that share coincident boundary spans into simple loops.</summary>
    /// <remarks>Nonzero sets resolve contained-drop and single-edge splices by union semantics while even-odd sets keep those gates; both fill rules share bitwise endpoint subdivision, simultaneous multi-span splicing with remainder linking, guard arbitration and nesting resolution, which stay exact because subdivision preserves geometry and linked boundaries resolve through parity depth. Admits sets whose only non-touch defect is positive-length collinear overlap between loops: edges subdivide at overlap endpoints with bitwise copies of existing vertices, loops contained in larger same-direction loops drop, and loop pairs sharing exactly one duplicate edge splice into a single boundary while pairs sharing two or more opposite spans splice every span at once with remainder linking. Opposite-direction containment, same-directed multi-span sharing, crossings, spikes and degenerate edges stay rejected. No computed points ever appear. Output lists follow the crossing-split convention for the shared reservation, mapping and nesting steps, reporting resolved loops so nesting tests skip bitwise-shared vertices through the union flag. Charges pair estimates per sweep round plus one work unit per copied vertex, and observes cancellation. Partial output on denial stays caller-discarded.</remarks>
    private bool TryResolveCoincidentUnion(SvgContourFlags structure, SvgDevicePoint[] points, int[] ends, BudgetedParserList<SvgDevicePoint> splitPoints, BudgetedParserList<int> splitEnds, out bool unionResolved)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(splitPoints);
        ArgumentNullException.ThrowIfNull(splitEnds);
        unionResolved = false;
        bool nonzeroOnly = !_isEvenOdd;
        bool singleEvenOdd = false;
        if (_isEvenOdd && (structure & SvgContourFlags.SelfTouch) == SvgContourFlags.None)
        {
            ChargePairs(_workCounter, (long)points.Length * points.Length, _cancellationToken);
            singleEvenOdd = !SvgContourStructure.TryFindInterLoopProperCrossing(points, ends, out _, out _, out _, out _, out _, _cancellationToken);
        }
        if ((structure & SvgContourFlags.CoincidentOverlap) == SvgContourFlags.None) return false;
        if ((structure & SvgContourFlags.ProperCrossing) != SvgContourFlags.None) return false;
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
            ChargePairs(_workCounter, (long)workPoints.Length * workPoints.Length, _cancellationToken);
            if (SubdivideCoincidentOverlapEndpoints(ref workPoints, ref workEnds)) continue;
            if (nonzeroOnly && TryDropContainedUnionLoop(ref workPoints, ref workEnds)) continue;
            if (nonzeroOnly && TrySpliceSharedUnionEdge(ref workPoints, ref workEnds, false)) continue;
            if (singleEvenOdd && TrySpliceSharedUnionEdge(ref workPoints, ref workEnds, true)) continue;
            if (TrySpliceSharedUnionEdges(ref workPoints, ref workEnds)) continue;
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
        if (!SvgContourStructure.AreSplitLoopsAdmissible(splitPoints.AsSpan(), splitEnds.AsSpan(), _cancellationToken)) return false;
        unionResolved = true;
        return true;
    }

    /// <summary>Splits one inter-loop coincident edge at an overlap endpoint with a bitwise copy.</summary>
    /// <remarks>The reported overlap values always equal an endpoint of at least one of the two edges, so the inserted value copies an existing vertex and no computed points appear. Splits at most one edge per call so indices stay valid while the sweep re-finds the next pair. Reports whether a subdivision happened.</remarks>
    private bool SubdivideCoincidentOverlapEndpoints(ref SvgDevicePoint[] workPoints, ref int[] workEnds)
    {
        if (!SvgContourStructure.TryFindInterLoopCoincidentOverlap(workPoints, workEnds, out int firstLoop, out int firstEdge, out int secondLoop, out int secondEdge, out SvgDevicePoint overlapFirst, out SvgDevicePoint overlapSecond, _cancellationToken)) return false;
        if (SubdivideLoopEdgeAtValue(ref workPoints, ref workEnds, firstLoop, firstEdge, overlapFirst)) return true;
        if (SubdivideLoopEdgeAtValue(ref workPoints, ref workEnds, firstLoop, firstEdge, overlapSecond)) return true;
        if (SubdivideLoopEdgeAtValue(ref workPoints, ref workEnds, secondLoop, secondEdge, overlapFirst)) return true;
        if (SubdivideLoopEdgeAtValue(ref workPoints, ref workEnds, secondLoop, secondEdge, overlapSecond)) return true;
        return false;
    }

    /// <summary>Splits one loop edge at a known vertex value when it lies strictly inside the edge.</summary>
    /// <remarks>Reuses the touch-edge subdivision so charging and cancellation match the touch path. Reports whether the edge changed.</remarks>
    private bool SubdivideLoopEdgeAtValue(ref SvgDevicePoint[] workPoints, ref int[] workEnds, int loop, int edge, SvgDevicePoint value)
    {
        int start = loop == 0 ? 0 : workEnds[loop - 1];
        int end = workEnds[loop];
        int count = end - start;
        if (value == workPoints[start + edge]) return false;
        if (value == workPoints[start + ((edge + 1) % count)]) return false;
        int occurrence = -1;
        for (int index = 0; index < workPoints.Length; index++)
        {
            _workCounter.Consume(_cancellationToken);
            if (workPoints[index] == value) { occurrence = index; break; }
        }
        if (occurrence < 0) return false;
        SubdivideTouchEdge(ref workPoints, ref workEnds, loop, edge, occurrence);
        return true;
    }

    /// <summary>Drops zero-area loops whose edges all duplicate other loops exactly.</summary>
    /// <remarks>Retraced spikes cancel pairwise to weld-scale area while their edges coincide with surviving boundaries, so removing them changes no coverage: zero-area loops contribute no winding under either fill rule while their fringe coincides with the duplicated edges. Opposite-sign pairs, nonzero areas and non-duplicated edges stay rejected. Charges one work unit per examined pair and vertex, and observes cancellation.</remarks>
    private bool TryDropSpikeUnionLoop(ref SvgDevicePoint[] workPoints, ref int[] workEnds)
    {
        for (int inner = 0; inner < workEnds.Length; inner++)
        {
            int innerStart = inner == 0 ? 0 : workEnds[inner - 1];
            int innerEnd = workEnds[inner];
            if (innerEnd - innerStart < 3) continue;
            if (Math.Abs(UnionDoubledArea(workPoints, innerStart, innerEnd)) > 1e-9) continue;
            bool allDuplicate = true;
            for (int edge = 0; edge < innerEnd - innerStart && allDuplicate; edge++)
            {
                SvgDevicePoint edgeStart = workPoints[innerStart + edge];
                SvgDevicePoint edgeEnd = workPoints[innerStart + ((edge + 1) % (innerEnd - innerStart))];
                if (edgeStart == edgeEnd) continue;
                bool found = false;
                for (int other = 0; other < workEnds.Length && !found; other++)
                {
                    if (other == inner) continue;
                    int otherStart = other == 0 ? 0 : workEnds[other - 1];
                    int otherCount = workEnds[other] - otherStart;
                    for (int otherEdge = 0; otherEdge < otherCount; otherEdge++)
                    {
                        _workCounter.Consume(_cancellationToken);
                        SvgDevicePoint otherEdgeStart = workPoints[otherStart + otherEdge];
                        SvgDevicePoint otherEdgeEnd = workPoints[otherStart + ((otherEdge + 1) % otherCount)];
                        if (otherEdgeStart == edgeStart && otherEdgeEnd == edgeEnd) { found = true; break; }
                        if (otherEdgeStart == edgeEnd && otherEdgeEnd == edgeStart) { found = true; break; }
                    }
                }
                if (!found) allDuplicate = false;
            }
            if (!allDuplicate) continue;
            DropUnionLoop(ref workPoints, ref workEnds, inner);
            return true;
        }
        return false;
    }

    /// <summary>Drops one loop contained in a larger same-direction loop, keeping holes.</summary>
    /// <remarks>Containment needs every inner vertex shared with, strictly inside, or on the boundary of the outer loop, a strictly smaller same-sign doubled area, and no containment the other way. Opposite-direction containment stays for hole rendering. Reports whether a loop dropped.</remarks>
    private bool TryDropContainedUnionLoop(ref SvgDevicePoint[] workPoints, ref int[] workEnds)
    {
        for (int inner = 0; inner < workEnds.Length; inner++)
        {
            int innerStart = inner == 0 ? 0 : workEnds[inner - 1];
            int innerEnd = workEnds[inner];
            double innerArea = UnionDoubledArea(workPoints, innerStart, innerEnd);
            if (innerArea == 0) continue;
            for (int outer = 0; outer < workEnds.Length; outer++)
            {
                if (outer == inner) continue;
                int outerStart = outer == 0 ? 0 : workEnds[outer - 1];
                int outerEnd = workEnds[outer];
                double outerArea = UnionDoubledArea(workPoints, outerStart, outerEnd);
                if (outerArea == 0 || Math.Sign(outerArea) != Math.Sign(innerArea) || Math.Abs(outerArea) <= Math.Abs(innerArea)) continue;
                if (!LoopContainsLoop(workPoints, outerStart, outerEnd, innerStart, innerEnd)) continue;
                if (LoopContainsLoop(workPoints, innerStart, innerEnd, outerStart, outerEnd)) continue;
                DropUnionLoop(ref workPoints, ref workEnds, inner);
                return true;
            }
        }
        return false;
    }

    /// <summary>Reports whether every vertex of one loop is shared with, inside, or on the other loop.</summary>
    private bool LoopContainsLoop(SvgDevicePoint[] points, int outerStart, int outerEnd, int innerStart, int innerEnd)
    {
        for (int index = innerStart; index < innerEnd; index++)
        {
            _workCounter.Consume(_cancellationToken);
            if (IsSharedLoopVertex(points[index], points, outerStart, outerEnd)) continue;
            if (SvgContourStructure.IsPointOnLoopBoundary(points, outerStart, outerEnd, points[index])) continue;
            if (SvgContourNesting.ContainsPoint(points, outerStart, outerEnd, points[index].X, points[index].Y)) continue;
            return false;
        }
        return true;
    }

    /// <summary>Reports whether a vertex matches any vertex of another loop by value.</summary>
    private static bool IsSharedLoopVertex(SvgDevicePoint candidate, SvgDevicePoint[] points, int start, int end)
    {
        for (int index = start; index < end; index++)
        {
            if (points[index] == candidate) return true;
        }
        return false;
    }

    /// <summary>Computes the doubled signed area of one loop slice.</summary>
    private static double UnionDoubledArea(SvgDevicePoint[] points, int start, int end)
    {
        double twice = 0;
        for (int index = start; index < end; index++)
        {
            SvgDevicePoint first = points[index];
            SvgDevicePoint second = index + 1 < end ? points[index + 1] : points[start];
            twice += (second.X - first.X) * (second.Y + first.Y);
        }
        return twice;
    }

    /// <summary>Removes one loop while keeping every other loop bitwise.</summary>
    private void DropUnionLoop(ref SvgDevicePoint[] workPoints, ref int[] workEnds, int loop)
    {
        int start = loop == 0 ? 0 : workEnds[loop - 1];
        int end = workEnds[loop];
        SvgDevicePoint[] nextPoints = new SvgDevicePoint[workPoints.Length - (end - start)];
        int[] nextEnds = new int[workEnds.Length - 1];
        int target = 0;
        int targetEnd = 0;
        for (int other = 0; other < workEnds.Length; other++)
        {
            if (other == loop) continue;
            int otherStart = other == 0 ? 0 : workEnds[other - 1];
            int otherEnd = workEnds[other];
            for (int source = otherStart; source < otherEnd; source++)
            {
                _workCounter.Consume(_cancellationToken);
                nextPoints[target++] = workPoints[source];
            }
            _workCounter.Consume(_cancellationToken);
            nextEnds[targetEnd++] = target;
        }
        workPoints = nextPoints;
        workEnds = nextEnds;
    }

    /// <summary>Splices two loops sharing exactly one duplicate edge into a single boundary.</summary>
    /// <remarks>Same-directed sharing between opposite-wound loops drops the shared span and reverses the second remainder while opposite-directed sharing concatenates remainders; same-wound doubled spans cross-chain through the doubled-span link instead. Either loop containing the other, multi-edge sharing and degenerate results stay rejected. With opposite-only admission for even-odd fills, same-directed pairs never count and strictly overlapping pairs stay gated because splicing would absorb their lens. Reports whether a splice happened.</remarks>
    private bool TrySpliceSharedUnionEdge(ref SvgDevicePoint[] workPoints, ref int[] workEnds, bool oppositeOnly)
    {
        for (int first = 0; first < workEnds.Length; first++)
        {
            int firstStart = first == 0 ? 0 : workEnds[first - 1];
            int firstCount = workEnds[first] - firstStart;
            for (int second = first + 1; second < workEnds.Length; second++)
            {
                int secondStart = second == 0 ? 0 : workEnds[second - 1];
                int secondCount = workEnds[second] - secondStart;
                if (LoopContainsLoop(workPoints, firstStart, firstStart + firstCount, secondStart, secondStart + secondCount)) continue;
                if (LoopContainsLoop(workPoints, secondStart, secondStart + secondCount, firstStart, firstStart + firstCount)) continue;
                int sharedFirst = -1;
                int sharedSecond = -1;
                int sharedPairs = 0;
                for (int firstEdge = 0; firstEdge < firstCount; firstEdge++)
                {
                    SvgDevicePoint firstEdgeStart = workPoints[firstStart + firstEdge];
                    SvgDevicePoint firstEdgeEnd = workPoints[firstStart + ((firstEdge + 1) % firstCount)];
                    if (firstEdgeStart == firstEdgeEnd) continue;
                    for (int secondEdge = 0; secondEdge < secondCount; secondEdge++)
                    {
                        _workCounter.Consume(_cancellationToken);
                        SvgDevicePoint secondEdgeStart = workPoints[secondStart + secondEdge];
                        SvgDevicePoint secondEdgeEnd = workPoints[secondStart + ((secondEdge + 1) % secondCount)];
                        if (secondEdgeStart == secondEdgeEnd) continue;
                        if (!((firstEdgeStart == secondEdgeStart && firstEdgeEnd == secondEdgeEnd) || (firstEdgeStart == secondEdgeEnd && firstEdgeEnd == secondEdgeStart))) continue;
                        if (oppositeOnly && firstEdgeStart == secondEdgeStart) continue;
                        sharedFirst = firstEdge;
                        sharedSecond = secondEdge;
                        sharedPairs++;
                    }
                }
                if (sharedPairs != 1) continue;
                if (oppositeOnly && SvgContourStructure.LoopsOverlapStrictly(workPoints, firstStart, firstCount, secondStart, secondCount, _cancellationToken)) continue;
                if (SpliceUnionLoops(ref workPoints, ref workEnds, first, sharedFirst, second, sharedSecond)) return true;
            }
        }
        return false;
    }

    /// <summary>Splices the shared duplicate edge out of two loops, keeping one combined boundary.</summary>
    /// <remarks>The first remainder runs from the shared end back to its start while the second remainder either reverses for opposite-wound same-directed sharing or concatenates for opposite-directed sharing; same-wound doubled spans leave through the doubled-span link before this path. Results that fail the clean-contour check stay rejected so no self-crossing boundary ever enters the output. Reports whether the splice landed.</remarks>
    private bool SpliceUnionLoops(ref SvgDevicePoint[] workPoints, ref int[] workEnds, int first, int sharedFirst, int second, int sharedSecond)
    {
        int firstStart = first == 0 ? 0 : workEnds[first - 1];
        int firstCount = workEnds[first] - firstStart;
        int secondStart = second == 0 ? 0 : workEnds[second - 1];
        int secondCount = workEnds[second] - secondStart;
        SvgDevicePoint edgeStart = workPoints[firstStart + sharedFirst];
        SvgDevicePoint edgeEnd = workPoints[firstStart + ((sharedFirst + 1) % firstCount)];
        bool sameDirected = workPoints[secondStart + sharedSecond] == edgeStart;
        if (sameDirected && IsDoubledSpan(workPoints, firstStart, firstCount, secondStart, secondCount)) return false;
        SvgDevicePoint[] result = new SvgDevicePoint[firstCount + secondCount - 2];
        int target = 0;
        for (int offset = 0; offset < firstCount; offset++)
        {
            _workCounter.Consume(_cancellationToken);
            result[target++] = workPoints[firstStart + ((sharedFirst + 1 + offset) % firstCount)];
        }
        if (sameDirected)
        {
            for (int offset = secondCount - 1; offset >= 2; offset--)
            {
                _workCounter.Consume(_cancellationToken);
                result[target++] = workPoints[secondStart + ((sharedSecond + offset) % secondCount)];
            }
        }
        else
        {
            for (int offset = 2; offset < secondCount; offset++)
            {
                _workCounter.Consume(_cancellationToken);
                result[target++] = workPoints[secondStart + ((sharedSecond + offset) % secondCount)];
            }
        }
        if (target != result.Length) return false;
        if (SvgContourStructure.ClassifyContour(result, 0, result.Length, _cancellationToken) != SvgContourFlags.None) return false;
        SvgDevicePoint[] nextPoints = new SvgDevicePoint[workPoints.Length - 2];
        int[] nextEnds = new int[workEnds.Length - 1];
        target = 0;
        int targetEnd = 0;
        for (int other = 0; other < workEnds.Length; other++)
        {
            if (other == first || other == second) continue;
            int otherStart = other == 0 ? 0 : workEnds[other - 1];
            int otherEnd = workEnds[other];
            for (int source = otherStart; source < otherEnd; source++)
            {
                _workCounter.Consume(_cancellationToken);
                nextPoints[target++] = workPoints[source];
            }
            _workCounter.Consume(_cancellationToken);
            nextEnds[targetEnd++] = target;
        }
        for (int index = 0; index < result.Length; index++)
        {
            _workCounter.Consume(_cancellationToken);
            nextPoints[target++] = result[index];
        }
        _workCounter.Consume(_cancellationToken);
        nextEnds[targetEnd++] = target;
        workPoints = nextPoints;
        workEnds = nextEnds;
        return true;
    }

    /// <summary>Reports whether two loops sharing one same-directed span wind the same way, doubling the span instead of interfacing two regions.</summary>
    /// <remarks>Zero-area loops never count: without a winding side the reversal path keeps its existing behavior. Deterministic sign comparison; allocates nothing and holds no budget.</remarks>
    private static bool IsDoubledSpan(SvgDevicePoint[] points, int firstStart, int firstCount, int secondStart, int secondCount)
    {
        double firstArea = UnionDoubledArea(points, firstStart, firstStart + firstCount);
        double secondArea = UnionDoubledArea(points, secondStart, secondStart + secondCount);
        return firstArea != 0 && Math.Sign(firstArea) == Math.Sign(secondArea);
    }

    /// <summary>Links one same-wound doubled span between two loops into outer ring plus doubled island.</summary>
    /// <remarks>Scans loop pairs like the shared-edge splice but admits only same-directed exact spans with no opposite-directed sharing between uncontained loops; the doubled-span link itself rechecks orientation signs, common vertices and the split-loop guard. At most one link fires per sweep through the caller spent flag, so linked outputs never re-trigger the same pair. Charges one work unit per examined pair and delegates copy charging to the link.</remarks>
    /// <summary>Conforms near-coincident spans between shared vertices to bitwise duplicates.</summary>
    /// <remarks>Variant arcs flatten to spans that share endpoints but weave within a pixel: exact subdivision finds no vertex to copy while computed crossings never saturate, so the sweep grinds without admitting. When two same-wound uncontained loops share distinct vertices u and v with interior-vertex-free spans on both sides deviating at most one device pixel symmetrically, the higher-index span takes bitwise copies of the lower-index span interior, turning the lens into exact spans for the splice and drop steps. Opposite-wound pairs, zero-area loops, contained pairs and wider deviations stay rejected; exact spans never re-trigger, so each conform strictly reduces differing-span pairs with the insertion cap as backstop. Charges one work unit per examined pair and copied vertex, and observes cancellation.</remarks>
    private bool TryConformNearSpans(ref SvgDevicePoint[] workPoints, ref int[] workEnds)
    {
        for (int first = 0; first < workEnds.Length; first++)
        {
            int firstStart = first == 0 ? 0 : workEnds[first - 1];
            int firstCount = workEnds[first] - firstStart;
            double firstArea = UnionDoubledArea(workPoints, firstStart, firstStart + firstCount);
            if (firstArea == 0) continue;
            for (int second = first + 1; second < workEnds.Length; second++)
            {
                int secondStart = second == 0 ? 0 : workEnds[second - 1];
                int secondCount = workEnds[second] - secondStart;
                double secondArea = UnionDoubledArea(workPoints, secondStart, secondStart + secondCount);
                if (secondArea == 0 || Math.Sign(secondArea) != Math.Sign(firstArea)) continue;
                if (LoopContainsLoop(workPoints, firstStart, firstStart + firstCount, secondStart, secondStart + secondCount)) continue;
                if (LoopContainsLoop(workPoints, secondStart, secondStart + secondCount, firstStart, firstStart + firstCount)) continue;
                if (TryConformNearSpanPair(ref workPoints, ref workEnds, first, second)) return true;
            }
        }
        return false;
    }

    /// <summary>Reports whether two spans carry bitwise-equal interiors in order.</summary>
    private static bool SpanInteriorsMatch(SvgDevicePoint[] points, int firstStart, int firstCount, int firstBegin, int firstLength, int secondStart, int secondCount, int secondBegin, int secondLength)
    {
        if (firstLength != secondLength) return false;
        for (int offset = 1; offset < firstLength; offset++)
        {
            if (points[firstStart + ((firstBegin + offset) % firstCount)] != points[secondStart + ((secondBegin + offset) % secondCount)]) return false;
        }
        return true;
    }

    /// <summary>Measures the symmetric-free one-sided deviation of one span interior from another polyline.</summary>
    /// <remarks>Empty interiors read zero; projection clamps to each segment like the classifier box rule. Exact double arithmetic consistent with orientation predicates; callers compare against the conform bound.</remarks>
    private static double SpanInteriorDeviation(SvgDevicePoint[] points, int firstStart, int firstCount, int firstBegin, int firstLength, int secondStart, int secondCount, int secondBegin, int secondLength)
    {
        double maximum = 0;
        for (int offset = 1; offset < firstLength; offset++)
        {
            SvgDevicePoint candidate = points[firstStart + ((firstBegin + offset) % firstCount)];
            for (int edge = 0; edge < secondLength; edge++)
            {
                SvgDevicePoint edgeStart = points[secondStart + ((secondBegin + edge) % secondCount)];
                SvgDevicePoint edgeEnd = points[secondStart + ((secondBegin + edge + 1) % secondCount)];
                double distance = PointToSegmentDistance(candidate, edgeStart, edgeEnd);
                if (distance > maximum) maximum = distance;
            }
        }
        return maximum;
    }

    /// <summary>Measures the Euclidean distance from one point to one segment with clamped projection.</summary>
    private static double PointToSegmentDistance(SvgDevicePoint candidate, SvgDevicePoint edgeStart, SvgDevicePoint edgeEnd)
    {
        double deltaX = edgeEnd.X - edgeStart.X;
        double deltaY = edgeEnd.Y - edgeStart.Y;
        double squared = deltaX * deltaX + deltaY * deltaY;
        if (squared == 0) return Math.Sqrt((candidate.X - edgeStart.X) * (candidate.X - edgeStart.X) + (candidate.Y - edgeStart.Y) * (candidate.Y - edgeStart.Y));
        double position = ((candidate.X - edgeStart.X) * deltaX + (candidate.Y - edgeStart.Y) * deltaY) / squared;
        if (position < 0) position = 0;
        else if (position > 1) position = 1;
        double closestX = edgeStart.X + position * deltaX;
        double closestY = edgeStart.Y + position * deltaY;
        return Math.Sqrt((candidate.X - closestX) * (candidate.X - closestX) + (candidate.Y - closestY) * (candidate.Y - closestY));
    }

    /// <summary>Replaces one span interior with bitwise copies of another span interior.</summary>
    /// <remarks>Both spans share endpoints with the donor interior already validated within the conform bound; copies reuse existing donor vertices in receiver traversal order so no computed points appear, with reversed copies producing opposite exact spans for the well-tested opposite splice. The receiver keeps the span endpoints and all off-span vertices in cyclic order while later loops shift by the interior-count delta, so cyclic spans wrapping past the loop end stay consistent. Charges one work unit per copied vertex, and observes cancellation.</remarks>
    private void ConformSpanInterior(ref SvgDevicePoint[] workPoints, ref int[] workEnds, int first, int firstBegin, int firstLength, int second, int secondAnchor, int secondLength, bool receiverReversed)
    {
        int firstStart = first == 0 ? 0 : workEnds[first - 1];
        int firstCount = workEnds[first] - firstStart;
        int secondStart = second == 0 ? 0 : workEnds[second - 1];
        int secondCount = workEnds[second] - secondStart;
        int donorInterior = firstLength - 1;
        int receiverInterior = secondLength - 1;
        int delta = donorInterior - receiverInterior;
        int spanEnd = (secondAnchor + secondLength) % secondCount;
        SvgDevicePoint anchorValue = workPoints[secondStart + secondAnchor];
        SvgDevicePoint endValue = workPoints[secondStart + spanEnd];
        SvgDevicePoint[] nextPoints = new SvgDevicePoint[workPoints.Length + delta];
        int[] nextEnds = new int[workEnds.Length];
        int target = 0;
        int targetEnd = 0;
        for (int other = 0; other < workEnds.Length; other++)
        {
            int otherStart = other == 0 ? 0 : workEnds[other - 1];
            int otherEnd = workEnds[other];
            if (other != second)
            {
                for (int source = otherStart; source < otherEnd; source++)
                {
                    _workCounter.Consume(_cancellationToken);
                    nextPoints[target++] = workPoints[source];
                }
            }
            else
            {
                _workCounter.Consume(_cancellationToken);
                nextPoints[target++] = anchorValue;
                for (int offset = 0; offset < donorInterior; offset++)
                {
                    _workCounter.Consume(_cancellationToken);
                    int donorOffset = receiverReversed ? donorInterior - offset : 1 + offset;
                    nextPoints[target++] = workPoints[firstStart + ((firstBegin + donorOffset) % firstCount)];
                }
                _workCounter.Consume(_cancellationToken);
                nextPoints[target++] = endValue;
                int rest = secondCount - receiverInterior - 2;
                for (int offset = 1; offset <= rest; offset++)
                {
                    _workCounter.Consume(_cancellationToken);
                    nextPoints[target++] = workPoints[otherStart + ((spanEnd + offset) % secondCount)];
                }
            }
            _workCounter.Consume(_cancellationToken);
            nextEnds[targetEnd++] = target;
        }
        if (target != nextPoints.Length || targetEnd != nextEnds.Length) return;
        workPoints = nextPoints;
        workEnds = nextEnds;
    }

    /// <summary>Conforms one near-coincident span pair between two loops, copying the lower-index span.</summary>
    /// <remarks>Walks the first loop forward from each shared vertex to its next shared vertex and pairs it with the second loop forward span between the same endpoints; both spans must carry no other shared vertex while deviating within the conform bound symmetrically, and differing interiors are required so exact spans never re-trigger. Reports whether a span conformed.</remarks>
    private bool TryConformNearSpanPair(ref SvgDevicePoint[] workPoints, ref int[] workEnds, int first, int second)
    {
        int firstStart = first == 0 ? 0 : workEnds[first - 1];
        int firstCount = workEnds[first] - firstStart;
        int secondStart = second == 0 ? 0 : workEnds[second - 1];
        int secondCount = workEnds[second] - secondStart;
        for (int firstEdge = 0; firstEdge < firstCount; firstEdge++)
        {
            _workCounter.Consume(_cancellationToken);
            SvgDevicePoint spanStart = workPoints[firstStart + firstEdge];
            if (!IsSharedLoopVertex(spanStart, workPoints, secondStart, secondStart + secondCount)) continue;
            for (int firstLength = 2; firstLength < firstCount; firstLength++)
            {
                _workCounter.Consume(_cancellationToken);
                SvgDevicePoint spanEnd = workPoints[firstStart + ((firstEdge + firstLength) % firstCount)];
                if (spanEnd == spanStart) continue;
                if (!IsSharedLoopVertex(spanEnd, workPoints, secondStart, secondStart + secondCount)) continue;
                if (TryConformSpanEndpoints(ref workPoints, ref workEnds, first, firstEdge, firstLength, second, spanStart, spanEnd, false)) return true;
                if (TryConformSpanEndpoints(ref workPoints, ref workEnds, first, firstEdge, firstLength, second, spanEnd, spanStart, true)) return true;
            }
        }
        return false;
    }

    /// <summary>Conforms the second-loop span between two shared endpoints to the first-loop span.</summary>
    /// <remarks>Walks the second loop forward from the anchor to the far endpoint regardless of intermediate shared vertices; the flipped call pairs the same unordered endpoints in opposite traversal. Both spans need differing interiors with symmetric deviation within the conform bound. Reports whether a span conformed.</remarks>
    private bool TryConformSpanEndpoints(ref SvgDevicePoint[] workPoints, ref int[] workEnds, int first, int firstBegin, int firstLength, int second, SvgDevicePoint spanStart, SvgDevicePoint spanEnd, bool receiverReversed)
    {
        int firstStart = first == 0 ? 0 : workEnds[first - 1];
        int firstCount = workEnds[first] - firstStart;
        int secondStart = second == 0 ? 0 : workEnds[second - 1];
        int secondCount = workEnds[second] - secondStart;
        int secondAnchor = -1;
        for (int secondEdge = 0; secondEdge < secondCount; secondEdge++)
        {
            _workCounter.Consume(_cancellationToken);
            if (workPoints[secondStart + secondEdge] == spanStart) { secondAnchor = secondEdge; break; }
        }
        if (secondAnchor < 0) return false;
        int secondLength = 1;
        while (secondLength < secondCount && workPoints[secondStart + ((secondAnchor + secondLength) % secondCount)] != spanEnd) secondLength++;
        if (secondLength >= secondCount) return false;
        if (firstLength < 2 && secondLength < 2) return false;
        if (SpanInteriorsMatch(workPoints, firstStart, firstCount, firstBegin, firstLength, secondStart, secondCount, secondAnchor, secondLength)) return false;
        if (SpanInteriorDeviation(workPoints, firstStart, firstCount, firstBegin, firstLength, secondStart, secondCount, secondAnchor, secondLength) > UnionConformEpsilon) return false;
        if (SpanInteriorDeviation(workPoints, secondStart, secondCount, secondAnchor, secondLength, firstStart, firstCount, firstBegin, firstLength) > UnionConformEpsilon) return false;
        ConformSpanInterior(ref workPoints, ref workEnds, first, firstBegin, firstLength, second, secondAnchor, secondLength, receiverReversed);
        return true;
    }

    /// <summary>Links boundary arcs at shared vertices into union rings, dropping interior arcs.</summary>
    /// <remarks>Same-wound uncontained loop pairs split at every shared vertex; arcs strictly inside the other loop drop as covered while boundary arcs link deterministically where each shared vertex keeps exactly two boundary ends, otherwise the pair stays rejected for tangent weaving. Opposite-wound pairs, contained pairs, zero-area loops, repeated shared values and degenerate arcs stay rejected, and each firing drops at least one interior arc while linking creates no vertices, so the sweep cannot revisit the same pair. The split-loop guard still arbitrates. Charges one work unit per examined pair and copied vertex, and observes cancellation.</remarks>
    private bool TryLinkSharedVertexArcs(ref SvgDevicePoint[] workPoints, ref int[] workEnds)
    {
        for (int first = 0; first < workEnds.Length; first++)
        {
            int firstStart = first == 0 ? 0 : workEnds[first - 1];
            int firstCount = workEnds[first] - firstStart;
            if (firstCount < 3) continue;
            double firstArea = UnionDoubledArea(workPoints, firstStart, firstStart + firstCount);
            if (firstArea == 0) continue;
            for (int second = first + 1; second < workEnds.Length; second++)
            {
                int secondStart = second == 0 ? 0 : workEnds[second - 1];
                int secondCount = workEnds[second] - secondStart;
                if (secondCount < 3) continue;
                double secondArea = UnionDoubledArea(workPoints, secondStart, secondStart + secondCount);
                if (secondArea == 0 || Math.Sign(secondArea) != Math.Sign(firstArea)) continue;
                if (LoopContainsLoop(workPoints, firstStart, firstStart + firstCount, secondStart, secondStart + secondCount)) continue;
                if (LoopContainsLoop(workPoints, secondStart, secondStart + secondCount, firstStart, firstStart + firstCount)) continue;
                if (TryLinkLoopPairArcs(ref workPoints, ref workEnds, first, second)) return true;
            }
        }
        return false;
    }

    /// <summary>Collects the relative indices whose values occur in another loop, in loop order.</summary>
    private int[] CollectSharedVertices(SvgDevicePoint[] points, int start, int count, int otherStart, int otherCount)
    {
        int[] found = new int[count];
        int total = 0;
        for (int index = 0; index < count; index++)
        {
            _workCounter.Consume(_cancellationToken);
            if (IsSharedLoopVertex(points[start + index], points, otherStart, otherStart + otherCount)) found[total++] = index;
        }
        int[] shared = new int[total];
        for (int index = 0; index < total; index++)
        {
            _workCounter.Consume(_cancellationToken);
            shared[index] = found[index];
        }
        return shared;
    }

    /// <summary>Reports whether every collected shared vertex occurs exactly once in its loop.</summary>
    private bool SharedVerticesAreSimple(SvgDevicePoint[] points, int start, int[] shared)
    {
        for (int first = 0; first < shared.Length; first++)
        {
            for (int second = first + 1; second < shared.Length; second++)
            {
                _workCounter.Consume(_cancellationToken);
                if (points[start + shared[first]] == points[start + shared[second]]) return false;
            }
        }
        return true;
    }

    /// <summary>Reports whether one arc lies strictly inside another loop, endpoints excluded.</summary>
    /// <remarks>Endpoints sit on the boundary by construction, so only interior vertices and edge midpoints witness, mirroring the strict-inside rule of the overlap test. Deterministic order; allocates nothing and holds no budget, with callers charging the pair estimate.</remarks>
    private static bool IsArcStrictlyInside(SvgDevicePoint[] points, int start, int count, int begin, int length, int otherStart, int otherCount)
    {
        for (int offset = 1; offset < length; offset++)
        {
            if (!IsStrictlyInside(points[start + ((begin + offset) % count)], points, otherStart, otherCount)) return false;
        }
        for (int edge = 0; edge < length; edge++)
        {
            SvgDevicePoint edgeStart = points[start + ((begin + edge) % count)];
            SvgDevicePoint edgeEnd = points[start + ((begin + edge + 1) % count)];
            SvgDevicePoint middle = new((edgeStart.X + edgeEnd.X) / 2, (edgeStart.Y + edgeEnd.Y) / 2);
            if (middle == edgeStart || middle == edgeEnd) continue;
            if (!IsStrictlyInside(middle, points, otherStart, otherCount)) return false;
        }
        return true;
    }

    /// <summary>Reports whether one vertex lies strictly inside a loop, excluding shared and boundary points.</summary>
    private static bool IsStrictlyInside(SvgDevicePoint candidate, SvgDevicePoint[] points, int start, int count)
    {
        if (IsSharedLoopVertex(candidate, points, start, start + count)) return false;
        if (SvgContourStructure.IsPointOnLoopBoundary(points, start, start + count, candidate)) return false;
        return SvgContourNesting.ContainsPoint(points, start, start + count, candidate.X, candidate.Y);
    }

    /// <summary>Replaces two loops with linked rings, keeping every other loop bitwise.</summary>
    /// <remarks>Ring vertices reuse existing points in walked order; later loop indices shift with the count delta. Charges one work unit per copied vertex, and observes cancellation.</remarks>
    private bool ReplaceLoopPair(ref SvgDevicePoint[] workPoints, ref int[] workEnds, int first, int second, SvgDevicePoint[] ringVerts, int[] ringEnds, int ringCount, int ringTotal)
    {
        if (ringCount <= 0) return false;
        int firstStart = first == 0 ? 0 : workEnds[first - 1];
        int firstEnd = workEnds[first];
        int secondStart = second == 0 ? 0 : workEnds[second - 1];
        int secondEnd = workEnds[second];
        SvgDevicePoint[] nextPoints = new SvgDevicePoint[workPoints.Length - (firstEnd - firstStart) - (secondEnd - secondStart) + ringTotal];
        int[] nextEnds = new int[workEnds.Length - 2 + ringCount];
        int target = 0;
        int targetEnd = 0;
        for (int other = 0; other < workEnds.Length; other++)
        {
            if (other == first || other == second) continue;
            int otherStart = other == 0 ? 0 : workEnds[other - 1];
            int otherEnd = workEnds[other];
            for (int source = otherStart; source < otherEnd; source++)
            {
                _workCounter.Consume(_cancellationToken);
                nextPoints[target++] = workPoints[source];
            }
            _workCounter.Consume(_cancellationToken);
            nextEnds[targetEnd++] = target;
        }
        int ringStart = 0;
        for (int ring = 0; ring < ringCount; ring++)
        {
            for (int source = ringStart; source < ringEnds[ring]; source++)
            {
                _workCounter.Consume(_cancellationToken);
                nextPoints[target++] = ringVerts[source];
            }
            _workCounter.Consume(_cancellationToken);
            nextEnds[targetEnd++] = target;
            ringStart = ringEnds[ring];
        }
        if (target != nextPoints.Length || targetEnd != nextEnds.Length) return false;
        workPoints = nextPoints;
        workEnds = nextEnds;
        return true;
    }

    /// <summary>Links two loops boundary arcs at their shared vertices into union rings.</summary>
    /// <remarks>Walks unvisited boundary arcs forward from each vertex, preferring traversals from the other loop and stopping only with no candidates left; closed walks become rings while dead ends and ambiguous vertices fail the pair. Each step consumes an unvisited arc while linking creates no vertices, so walks terminate short of the step cap with forward chaining preserving parent orientation. Reports whether the pair relinked.</remarks>
    private bool LinkBoundaryArcs(ref SvgDevicePoint[] workPoints, ref int[] workEnds, int first, int second, int[] firstShared, bool[] firstInterior, int[] secondShared, bool[] secondInterior)
    {
        int firstStart = first == 0 ? 0 : workEnds[first - 1];
        int firstCount = workEnds[first] - firstStart;
        int secondStart = second == 0 ? 0 : workEnds[second - 1];
        int secondCount = workEnds[second] - secondStart;
        int firstArcs = firstShared.Length;
        int secondArcs = secondShared.Length;
        int[] arcLoop = new int[firstArcs + secondArcs];
        int[] arcBegin = new int[firstArcs + secondArcs];
        int[] arcLength = new int[firstArcs + secondArcs];
        int boundaryTotal = 0;
        for (int arc = 0; arc < firstArcs; arc++)
        {
            if (firstInterior[arc]) continue;
            _workCounter.Consume(_cancellationToken);
            int begin = firstShared[arc];
            int length = (firstShared[(arc + 1) % firstArcs] - begin + firstCount) % firstCount;
            if (length <= 0 || length >= firstCount) return false;
            arcLoop[boundaryTotal] = 0;
            arcBegin[boundaryTotal] = begin;
            arcLength[boundaryTotal] = length;
            boundaryTotal++;
        }
        for (int arc = 0; arc < secondArcs; arc++)
        {
            if (secondInterior[arc]) continue;
            _workCounter.Consume(_cancellationToken);
            int begin = secondShared[arc];
            int length = (secondShared[(arc + 1) % secondArcs] - begin + secondCount) % secondCount;
            if (length <= 0 || length >= secondCount) return false;
            arcLoop[boundaryTotal] = 1;
            arcBegin[boundaryTotal] = begin;
            arcLength[boundaryTotal] = length;
            boundaryTotal++;
        }
        if (boundaryTotal == 0) return false;
        bool[] visited = new bool[boundaryTotal];
        SvgDevicePoint[] ringVerts = new SvgDevicePoint[firstCount + secondCount];
        int[] ringEnds = new int[boundaryTotal];
        int ringCount = 0;
        int ringTotal = 0;
        for (int seed = 0; seed < boundaryTotal; seed++)
        {
            if (visited[seed]) continue;
            visited[seed] = true;
            int seedLoop = arcLoop[seed];
            int seedStart = seedLoop == 0 ? firstStart : secondStart;
            int seedCount = seedLoop == 0 ? firstCount : secondCount;
            SvgDevicePoint walkStart = workPoints[seedStart + arcBegin[seed]];
            SvgDevicePoint walkVertex = workPoints[seedStart + ((arcBegin[seed] + arcLength[seed]) % seedCount)];
            for (int offset = 1; offset <= arcLength[seed]; offset++)
            {
                _workCounter.Consume(_cancellationToken);
                ringVerts[ringTotal++] = workPoints[seedStart + ((arcBegin[seed] + offset) % seedCount)];
            }
            int arrivalLoop = seedLoop;
            int steps = 0;
            while (true)
            {
                if (++steps > boundaryTotal + 1) return false;
                _workCounter.Consume(_cancellationToken);
                int otherChoice = -1;
                int sameChoice = -1;
                for (int candidate = 0; candidate < boundaryTotal; candidate++)
                {
                    if (visited[candidate]) continue;
                    int candidateLoop = arcLoop[candidate];
                    int candidateStart = candidateLoop == 0 ? firstStart : secondStart;
                    int candidateCount = candidateLoop == 0 ? firstCount : secondCount;
                    if (workPoints[candidateStart + arcBegin[candidate]] != walkVertex) continue;
                    if (candidateLoop != arrivalLoop)
                    {
                        if (otherChoice >= 0) return false;
                        otherChoice = candidate;
                    }
                    else
                    {
                        if (sameChoice >= 0) return false;
                        sameChoice = candidate;
                    }
                }
                int choice = otherChoice >= 0 ? otherChoice : sameChoice;
                if (choice < 0)
                {
                    if (!(walkVertex == walkStart)) return false;
                    break;
                }
                visited[choice] = true;
                int choiceLoop = arcLoop[choice];
                int choiceStart = choiceLoop == 0 ? firstStart : secondStart;
                int choiceCount = choiceLoop == 0 ? firstCount : secondCount;
                for (int offset = 1; offset <= arcLength[choice]; offset++)
                {
                    _workCounter.Consume(_cancellationToken);
                    ringVerts[ringTotal++] = workPoints[choiceStart + ((arcBegin[choice] + offset) % choiceCount)];
                }
                walkVertex = workPoints[choiceStart + ((arcBegin[choice] + arcLength[choice]) % choiceCount)];
                arrivalLoop = choiceLoop;
            }
            ringEnds[ringCount++] = ringTotal;
        }
        return ReplaceLoopPair(ref workPoints, ref workEnds, first, second, ringVerts, ringEnds, ringCount, ringTotal);
    }

    /// <summary>Links one loop pair through shared-vertex arc surgery into union rings.</summary>
    /// <remarks>Both loops split at every shared vertex; arcs strictly inside the other loop drop as covered while boundary arcs link head-to-tail. Reports whether the pair relinked.</remarks>
    private bool TryLinkLoopPairArcs(ref SvgDevicePoint[] workPoints, ref int[] workEnds, int first, int second)
    {
        int firstStart = first == 0 ? 0 : workEnds[first - 1];
        int firstCount = workEnds[first] - firstStart;
        int secondStart = second == 0 ? 0 : workEnds[second - 1];
        int secondCount = workEnds[second] - secondStart;
        int[] firstShared = CollectSharedVertices(workPoints, firstStart, firstCount, secondStart, secondCount);
        int[] secondShared = CollectSharedVertices(workPoints, secondStart, secondCount, firstStart, firstCount);
        if (firstShared.Length < 2 || secondShared.Length < 2) return false;
        if (!SharedVerticesAreSimple(workPoints, firstStart, firstShared) || !SharedVerticesAreSimple(workPoints, secondStart, secondShared)) return false;
        int firstArcs = firstShared.Length;
        int secondArcs = secondShared.Length;
        bool[] firstInterior = new bool[firstArcs];
        bool[] secondInterior = new bool[secondArcs];
        int dropped = 0;
        for (int arc = 0; arc < firstArcs; arc++)
        {
            int begin = firstShared[arc];
            int end = firstShared[(arc + 1) % firstArcs];
            int length = (end - begin + firstCount) % firstCount;
            if (length == 0) return false;
            if (IsArcStrictlyInside(workPoints, firstStart, firstCount, begin, length, secondStart, secondCount)) { firstInterior[arc] = true; dropped++; }
        }
        for (int arc = 0; arc < secondArcs; arc++)
        {
            int begin = secondShared[arc];
            int end = secondShared[(arc + 1) % secondArcs];
            int length = (end - begin + secondCount) % secondCount;
            if (length == 0) return false;
            if (IsArcStrictlyInside(workPoints, secondStart, secondCount, begin, length, firstStart, firstCount)) { secondInterior[arc] = true; dropped++; }
        }
        if (dropped == 0) return false;
        return LinkBoundaryArcs(ref workPoints, ref workEnds, first, second, firstShared, firstInterior, secondShared, secondInterior);
    }

    private bool TryLinkDoubledSpanPair(ref SvgDevicePoint[] workPoints, ref int[] workEnds)
    {
        for (int first = 0; first < workEnds.Length; first++)
        {
            int firstStart = first == 0 ? 0 : workEnds[first - 1];
            int firstCount = workEnds[first] - firstStart;
            for (int second = first + 1; second < workEnds.Length; second++)
            {
                int secondStart = second == 0 ? 0 : workEnds[second - 1];
                int secondCount = workEnds[second] - secondStart;
                if (LoopContainsLoop(workPoints, firstStart, firstStart + firstCount, secondStart, secondStart + secondCount)) continue;
                if (LoopContainsLoop(workPoints, secondStart, secondStart + secondCount, firstStart, firstStart + firstCount)) continue;
                int sharedFirst = -1;
                int sharedSecond = -1;
                int sharedPairs = 0;
                bool oppositeSharing = false;
                for (int firstEdge = 0; firstEdge < firstCount; firstEdge++)
                {
                    SvgDevicePoint firstEdgeStart = workPoints[firstStart + firstEdge];
                    SvgDevicePoint firstEdgeEnd = workPoints[firstStart + ((firstEdge + 1) % firstCount)];
                    if (firstEdgeStart == firstEdgeEnd) continue;
                    for (int secondEdge = 0; secondEdge < secondCount; secondEdge++)
                    {
                        _workCounter.Consume(_cancellationToken);
                        SvgDevicePoint secondEdgeStart = workPoints[secondStart + secondEdge];
                        SvgDevicePoint secondEdgeEnd = workPoints[secondStart + ((secondEdge + 1) % secondCount)];
                        if (secondEdgeStart == secondEdgeEnd) continue;
                        if (firstEdgeStart == secondEdgeStart && firstEdgeEnd == secondEdgeEnd)
                        {
                            sharedFirst = firstEdge;
                            sharedSecond = secondEdge;
                            sharedPairs++;
                        }
                        else if (firstEdgeStart == secondEdgeEnd && firstEdgeEnd == secondEdgeStart)
                        {
                            oppositeSharing = true;
                        }
                    }
                }
                if (sharedPairs != 1 || oppositeSharing) continue;
                if (TrySpliceDoubledSpan(ref workPoints, ref workEnds, first, sharedFirst, second, sharedSecond)) return true;
            }
        }
        return false;
    }

    /// <summary>Links two same-wound loops sharing one same-directed span into outer ring plus doubled island.</summary>
    /// <remarks>Reversal would cancel same-side area, so the shared span stays once while both remainders split at their first common interior vertex and cross-chain around it: every segment lands exactly once with both loops keeping the parents orientation. Missing common vertices, degenerate outputs and opposite-wound pairs stay rejected for the reversal path while the split-loop guard still arbitrates. Charges one work unit per examined pair and copied vertex, and observes cancellation.</remarks>
    private bool TrySpliceDoubledSpan(ref SvgDevicePoint[] workPoints, ref int[] workEnds, int first, int sharedFirst, int second, int sharedSecond)
    {
        int firstStart = first == 0 ? 0 : workEnds[first - 1];
        int firstCount = workEnds[first] - firstStart;
        int secondStart = second == 0 ? 0 : workEnds[second - 1];
        int secondCount = workEnds[second] - secondStart;
        if (!IsDoubledSpan(workPoints, firstStart, firstCount, secondStart, secondCount)) return false;
        SvgDevicePoint spanStart = workPoints[firstStart + sharedFirst];
        int firstLength = firstCount - 1;
        int secondLength = secondCount - 1;
        int firstSplit = -1;
        int secondSplit = -1;
        for (int firstOffset = 1; firstOffset < firstLength && firstSplit < 0; firstOffset++)
        {
            _workCounter.Consume(_cancellationToken);
            SvgDevicePoint candidate = workPoints[firstStart + ((sharedFirst + 1 + firstOffset) % firstCount)];
            if (candidate == spanStart || candidate == workPoints[firstStart + ((sharedFirst + 1) % firstCount)]) continue;
            for (int secondOffset = 1; secondOffset < secondLength; secondOffset++)
            {
                _workCounter.Consume(_cancellationToken);
                if (workPoints[secondStart + ((sharedSecond + 1 + secondOffset) % secondCount)] == candidate)
                {
                    firstSplit = firstOffset;
                    secondSplit = secondOffset;
                    break;
                }
            }
        }
        if (firstSplit < 0) return false;
        int secondHead = secondSplit + 1;
        int secondTail = secondLength - secondHead;
        int firstHead = firstSplit + 1;
        int firstTail = firstLength - firstHead;
        SvgDevicePoint[] outer = new SvgDevicePoint[1 + firstHead + secondTail];
        SvgDevicePoint[] island = new SvgDevicePoint[1 + secondHead + firstTail];
        outer[0] = spanStart;
        island[0] = spanStart;
        for (int offset = 0; offset < firstHead; offset++)
        {
            _workCounter.Consume(_cancellationToken);
            outer[1 + offset] = workPoints[firstStart + ((sharedFirst + 1 + offset) % firstCount)];
        }
        for (int offset = 0; offset < secondTail; offset++)
        {
            _workCounter.Consume(_cancellationToken);
            outer[1 + firstHead + offset] = workPoints[secondStart + ((sharedSecond + 1 + secondHead + offset) % secondCount)];
        }
        for (int offset = 0; offset < secondHead; offset++)
        {
            _workCounter.Consume(_cancellationToken);
            island[1 + offset] = workPoints[secondStart + ((sharedSecond + 1 + offset) % secondCount)];
        }
        for (int offset = 0; offset < firstTail; offset++)
        {
            _workCounter.Consume(_cancellationToken);
            island[1 + secondHead + offset] = workPoints[firstStart + ((sharedFirst + 1 + firstHead + offset) % firstCount)];
        }
        if (outer.Length < 3 || island.Length < 3) return false;
        if (!IsSpliceResultAdmissible(outer)) return false;
        if (!IsSpliceResultAdmissible(island)) return false;
        SvgDevicePoint[] nextPoints = new SvgDevicePoint[workPoints.Length];
        int[] nextEnds = new int[workEnds.Length];
        int target = 0;
        int targetEnd = 0;
        for (int other = 0; other < workEnds.Length; other++)
        {
            if (other == first || other == second) continue;
            int otherStart = other == 0 ? 0 : workEnds[other - 1];
            int otherEnd = workEnds[other];
            for (int source = otherStart; source < otherEnd; source++)
            {
                _workCounter.Consume(_cancellationToken);
                nextPoints[target++] = workPoints[source];
            }
            _workCounter.Consume(_cancellationToken);
            nextEnds[targetEnd++] = target;
        }
        for (int index = 0; index < outer.Length; index++)
        {
            _workCounter.Consume(_cancellationToken);
            nextPoints[target++] = outer[index];
        }
        _workCounter.Consume(_cancellationToken);
        nextEnds[targetEnd++] = target;
        for (int index = 0; index < island.Length; index++)
        {
            _workCounter.Consume(_cancellationToken);
            nextPoints[target++] = island[index];
        }
        _workCounter.Consume(_cancellationToken);
        nextEnds[targetEnd++] = target;
        if (target != nextPoints.Length || targetEnd != nextEnds.Length) return false;
        workPoints = nextPoints;
        workEnds = nextEnds;
        return true;
    }

    /// <summary>Reports whether one spliced boundary is a clean contour.</summary>
    private bool IsSpliceResultAdmissible(SvgDevicePoint[] loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        SvgContourFlags spliced = SvgContourStructure.ClassifyContour(loop, 0, loop.Length, _cancellationToken);
        return spliced == SvgContourFlags.None;
    }
    /// <summary>Splices loop pairs sharing two or more opposite duplicate edges into union boundaries.</summary>
    /// <remarks>Collects every opposite-directed exact-span duplicate between loop pairs that neither contain, requires a remainder edge on both loops, then drops every shared span and links remainder ends across loops at each shared vertex while keeping same-loop continuation at merely touched values. Adjacent shared edges form chains the walk skips in one jump from the chain-entry vertex, so multi-edge wordmark chains link exactly like disjoint spans. Deterministic walks cover each remainder edge once with a step cap as backstop; degenerate or non-simple results keep the set rejected so the split-loop guard still arbitrates. Single shared spans stay with the single-edge splice, same-directed sharing and fully-shared pairs stay gated, and even-odd sets keep their coincident ban at the caller. Charges one work unit per examined pair and copied vertex, and observes cancellation.</remarks>
    private bool TrySpliceSharedUnionEdges(ref SvgDevicePoint[] workPoints, ref int[] workEnds)
    {
        for (int first = 0; first < workEnds.Length; first++)
        {
            int firstStart = first == 0 ? 0 : workEnds[first - 1];
            int firstCount = workEnds[first] - firstStart;
            for (int second = first + 1; second < workEnds.Length; second++)
            {
                int secondStart = second == 0 ? 0 : workEnds[second - 1];
                int secondCount = workEnds[second] - secondStart;
                if (LoopContainsLoop(workPoints, firstStart, firstStart + firstCount, secondStart, secondStart + secondCount)) continue;
                if (LoopContainsLoop(workPoints, secondStart, secondStart + secondCount, firstStart, firstStart + firstCount)) continue;
                int sharedPairs = 0;
                bool sameDirected = false;
                for (int firstEdge = 0; firstEdge < firstCount; firstEdge++)
                {
                    SvgDevicePoint firstEdgeStart = workPoints[firstStart + firstEdge];
                    SvgDevicePoint firstEdgeEnd = workPoints[firstStart + ((firstEdge + 1) % firstCount)];
                    if (firstEdgeStart == firstEdgeEnd) continue;
                    for (int secondEdge = 0; secondEdge < secondCount; secondEdge++)
                    {
                        _workCounter.Consume(_cancellationToken);
                        SvgDevicePoint secondEdgeStart = workPoints[secondStart + secondEdge];
                        SvgDevicePoint secondEdgeEnd = workPoints[secondStart + ((secondEdge + 1) % secondCount)];
                        if (secondEdgeStart == secondEdgeEnd) continue;
                        if (!((firstEdgeStart == secondEdgeStart && firstEdgeEnd == secondEdgeEnd) || (firstEdgeStart == secondEdgeEnd && firstEdgeEnd == secondEdgeStart))) continue;
                        if (firstEdgeStart == secondEdgeStart) sameDirected = true;
                        sharedPairs++;
                    }
                }
                if (sharedPairs < 2 || sameDirected) continue;
                if (SpliceSharedUnionEdges(ref workPoints, ref workEnds, first, second, sharedPairs)) return true;
            }
        }
        return false;
    }

    /// <summary>Splices every opposite duplicate edge between two loops into union boundary loops.</summary>
    /// <remarks>Recollects the counted opposite pairs, requires vertex-disjoint spans with remainder edges on both loops, then walks remainder edges alternating loops at shared-span endpoints. Holds no budget beyond per-vertex copies; callers charge the pair scan.</remarks>
    private bool SpliceSharedUnionEdges(ref SvgDevicePoint[] workPoints, ref int[] workEnds, int first, int second, int sharedPairs)
    {
        int firstStart = first == 0 ? 0 : workEnds[first - 1];
        int firstCount = workEnds[first] - firstStart;
        int secondStart = second == 0 ? 0 : workEnds[second - 1];
        int secondCount = workEnds[second] - secondStart;
        int[] firstEdges = new int[sharedPairs];
        int[] secondEdges = new int[sharedPairs];
        int found = 0;
        for (int firstEdge = 0; firstEdge < firstCount; firstEdge++)
        {
            SvgDevicePoint firstEdgeStart = workPoints[firstStart + firstEdge];
            SvgDevicePoint firstEdgeEnd = workPoints[firstStart + ((firstEdge + 1) % firstCount)];
            if (firstEdgeStart == firstEdgeEnd) continue;
            for (int secondEdge = 0; secondEdge < secondCount; secondEdge++)
            {
                _workCounter.Consume(_cancellationToken);
                SvgDevicePoint secondEdgeStart = workPoints[secondStart + secondEdge];
                SvgDevicePoint secondEdgeEnd = workPoints[secondStart + ((secondEdge + 1) % secondCount)];
                if (secondEdgeStart == secondEdgeEnd) continue;
                if (!(firstEdgeStart == secondEdgeEnd && firstEdgeEnd == secondEdgeStart)) continue;
                if (found >= sharedPairs) return false;
                firstEdges[found] = firstEdge;
                secondEdges[found] = secondEdge;
                found++;
            }
        }
        if (found != sharedPairs) return false;
        bool[] firstShared = new bool[firstCount];
        bool[] secondShared = new bool[secondCount];
        for (int index = 0; index < found; index++)
        {
            firstShared[firstEdges[index]] = true;
            secondShared[secondEdges[index]] = true;
        }
        int firstRemainder = 0;
        for (int edge = 0; edge < firstCount; edge++)
        {
            _workCounter.Consume(_cancellationToken);
            if (!firstShared[edge]) firstRemainder++;
        }
        int secondRemainder = 0;
        for (int edge = 0; edge < secondCount; edge++)
        {
            _workCounter.Consume(_cancellationToken);
            if (!secondShared[edge]) secondRemainder++;
        }
        if (firstRemainder == 0 || secondRemainder == 0) return false;
        bool[] firstSeen = new bool[firstCount];
        bool[] secondSeen = new bool[secondCount];
        SvgDevicePoint[] linked = new SvgDevicePoint[(firstRemainder + secondRemainder) * 2];
        int[] linkedEnds = new int[workEnds.Length + firstRemainder + secondRemainder];
        int linkedTotal = 0;
        int linkedLoops = 0;
        for (int lookLoop = 0; lookLoop < 2; lookLoop++)
        {
            int lookCount = lookLoop == 0 ? firstCount : secondCount;
            bool[] lookSeen = lookLoop == 0 ? firstSeen : secondSeen;
            bool[] lookShared = lookLoop == 0 ? firstShared : secondShared;
            for (int lookEdge = 0; lookEdge < lookCount; lookEdge++)
            {
                if (lookShared[lookEdge] || lookSeen[lookEdge]) continue;
                int walkStartLoop = lookLoop;
                int walkStartEdge = lookEdge;
                int walkLoop = lookLoop;
                int walkEdge = lookEdge;
                int steps = 0;
                while (true)
                {
                    if (++steps > firstRemainder + secondRemainder + 1) return false;
                    _workCounter.Consume(_cancellationToken);
                    if (walkLoop == 0) firstSeen[walkEdge] = true;
                    else secondSeen[walkEdge] = true;
                    int walkStart = walkLoop == 0 ? firstStart : secondStart;
                    int walkCount = walkLoop == 0 ? firstCount : secondCount;
                    linked[linkedTotal++] = workPoints[walkStart + walkEdge];
                    int next = (walkEdge + 1) % walkCount;
                    bool[] walkShared = walkLoop == 0 ? firstShared : secondShared;
                    if (!walkShared[next])
                    {
                        walkEdge = next;
                    }
                    else
                    {
                        SvgDevicePoint vertex = workPoints[walkStart + next];
                        int other = 1 - walkLoop;
                        int otherStart = other == 0 ? firstStart : secondStart;
                        int otherCount = other == 0 ? firstCount : secondCount;
                        bool[] otherShared = other == 0 ? firstShared : secondShared;
                        int continuation = -1;
                        for (int otherEdge = 0; otherEdge < otherCount; otherEdge++)
                        {
                            _workCounter.Consume(_cancellationToken);
                            if (otherShared[otherEdge] || workPoints[otherStart + otherEdge] != vertex) continue;
                            if (continuation >= 0) return false;
                            continuation = otherEdge;
                        }
                        if (continuation < 0) return false;
                        walkLoop = other;
                        walkEdge = continuation;
                    }
                    if (walkLoop == walkStartLoop && walkEdge == walkStartEdge) break;
                }
                linkedEnds[linkedLoops++] = linkedTotal;
            }
        }
        for (int loop = 0; loop < linkedLoops; loop++)
        {
            int start = loop == 0 ? 0 : linkedEnds[loop - 1];
            if (SvgContourStructure.ClassifyContour(linked, start, linkedEnds[loop], _cancellationToken) != SvgContourFlags.None) return false;
        }
        SvgDevicePoint[] nextPoints = new SvgDevicePoint[workPoints.Length - firstCount - secondCount + linkedTotal];
        int[] nextEnds = new int[workEnds.Length - 2 + linkedLoops];
        int target = 0;
        int targetEnd = 0;
        for (int other = 0; other < workEnds.Length; other++)
        {
            if (other == first || other == second) continue;
            int otherStart = other == 0 ? 0 : workEnds[other - 1];
            int otherEnd = workEnds[other];
            for (int source = otherStart; source < otherEnd; source++)
            {
                _workCounter.Consume(_cancellationToken);
                nextPoints[target++] = workPoints[source];
            }
            _workCounter.Consume(_cancellationToken);
            nextEnds[targetEnd++] = target;
        }
        for (int index = 0; index < linkedTotal; index++)
        {
            _workCounter.Consume(_cancellationToken);
            nextPoints[target++] = linked[index];
        }
        for (int loop = 0; loop < linkedLoops; loop++)
        {
            _workCounter.Consume(_cancellationToken);
            nextEnds[targetEnd++] = target - linkedTotal + linkedEnds[loop];
        }
        workPoints = nextPoints;
        workEnds = nextEnds;
        return true;
    }

    /// <summary>Resolves point-touching contours into simple loops sharing only vertices.</summary>
    /// <remarks>Admits sets whose only defects are endpoint contacts plus strict crossings between loops: nonzero sets of any loop count resolve crossings alongside touches, while even-odd resolution admits exactly two-loop sets without self-touches, including pure-crossing sets with no touch to anchor on (opposite parity signs are exact for every two-loop configuration under absolute-value combination, so crossing subdivision reports through subdividedCrossings for sign forcing and larger even-odd sets stay gated). Edges split at interior touch vertices with bitwise copies, inter-loop crossings subdivide both edges at the computed crossing, and self-touching rings split at shared vertices into lobes. Every loop keeps a vertex that is neither shared nor on another loop boundary for robust nesting tests, and the split-loop guard verifies the result. Intra-loop crossings, collinear overlaps and degenerate edges stay rejected, as do fully-inscribed loops and larger even-odd crossing sets. Output lists follow the crossing-split convention for the shared reservation, mapping and nesting steps. Charges pair estimates for scans plus one work unit per copied vertex, and observes cancellation.</remarks>
    private bool TryResolveTouchContours(SvgContourFlags structure, SvgDevicePoint[] points, int[] ends, BudgetedParserList<SvgDevicePoint> splitPoints, BudgetedParserList<int> splitEnds, out bool subdividedCrossings)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(splitPoints);
        ArgumentNullException.ThrowIfNull(splitEnds);
        subdividedCrossings = false;
        bool allowEvenOddCrossings = _isEvenOdd && (structure & SvgContourFlags.SelfTouch) == SvgContourFlags.None && ends.Length == 2;
        if (structure == SvgContourFlags.None) return false;
        bool pureEvenOddCrossing = allowEvenOddCrossings && structure == SvgContourFlags.ProperCrossing;
        if (!pureEvenOddCrossing && (structure & ~(SvgContourFlags.SelfTouch | SvgContourFlags.TouchesOtherContour)) != SvgContourFlags.None) return false;
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
            if (!_isEvenOdd || allowEvenOddCrossings)
            {
                ChargePairs(_workCounter, (long)workPoints.Length * workPoints.Length, _cancellationToken);
                if (SvgContourStructure.TryFindInterLoopProperCrossing(workPoints, workEnds, out int crossingFirstLoop, out int crossingFirstEdge, out int crossingSecondLoop, out int crossingSecondEdge, out SvgDevicePoint crossing, _cancellationToken))
                {
                    InsertSplitVertex(ref workPoints, workEnds, crossingFirstLoop, crossingFirstEdge, crossing);
                    InsertSplitVertex(ref workPoints, workEnds, crossingSecondLoop, crossingSecondEdge, crossing);
                    subdividedCrossings = true;
                    continue;
                }
            }

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
    /// <summary>Unions nonzero loops mixing crossings, coincident spans and touches through one subdivision sweep.</summary>
    /// <remarks>Earlier stages refuse mixed sets at their entry gates even though every needed operation already exists: vertex-on-edge subdivision already covers intra-loop coincident endpoints with bitwise copies, self-touch splitting turns the mixed single loop into loop pairs, inter-loop crossing subdivision shares the computed crossing as a bitwise value between loops, and contained-drop, spike-drop plus shared-edge splicing remove the resulting exact duplicates, with near-span conforming aligning variant arcs first, with at most one doubled-span link per sweep so linked outputs never re-trigger the same pair. The sweep repeats those operations to a fixpoint so the retraced-span-plus-crossing class resolves while pure-crossing, pure-touch and even-odd sets keep their existing stages and gates. Computed crossing points appear exactly like the touch sweep; every other inserted value copies an existing vertex. The split-loop guard still arbitrates the final set. Charges pair estimates per sweep round plus one work unit per copied vertex, and observes cancellation. Inserted vertices stay within twice the initial points plus ends because near-duplicate weaving would otherwise subdivide without saturating; deeper sets stay honestly rejected. Partial output on denial stays caller-discarded.</remarks>
    private bool TryResolveMixedContactUnion(SvgContourFlags structure, SvgDevicePoint[] points, int[] ends, BudgetedParserList<SvgDevicePoint> splitPoints, BudgetedParserList<int> splitEnds, out bool mixedResolved)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(splitPoints);
        ArgumentNullException.ThrowIfNull(splitEnds);
        mixedResolved = false;
        if (_isEvenOdd) return false;
        if (structure == SvgContourFlags.None) return false;
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
        long maximumInsertions = checked(2 * (long)points.Length + 2 * ends.Length + 8);
        long conforms = 0;
        long maximumConforms = checked(8 * (long)ends.Length + 8);
        bool doubledSpent = false;
        while (true)
        {
            if (++sweeps > maximumSweeps) return false;
            if (workPoints.Length > points.Length + maximumInsertions) return false;
            ChargePairs(_workCounter, (long)workPoints.Length * workPoints.Length, _cancellationToken);
            if (SubdivideCoincidentOverlapEndpoints(ref workPoints, ref workEnds)) continue;
            if (SvgContourStructure.TryFindInterLoopProperCrossing(workPoints, workEnds, out int crossingFirstLoop, out int crossingFirstEdge, out int crossingSecondLoop, out int crossingSecondEdge, out SvgDevicePoint crossing, _cancellationToken))
            {
                InsertSplitVertex(ref workPoints, workEnds, crossingFirstLoop, crossingFirstEdge, crossing);
                InsertSplitVertex(ref workPoints, workEnds, crossingSecondLoop, crossingSecondEdge, crossing);
                continue;
            }
            if (SvgContourStructure.TryFindVertexOnEdge(workPoints, workEnds, out int touchLoop, out int touchEdge, out int touchVertex, _cancellationToken))
            {
                SubdivideTouchEdge(ref workPoints, ref workEnds, touchLoop, touchEdge, touchVertex);
                continue;
            }
            if (TrySplitSelfTouchLoop(ref workPoints, ref workEnds, out bool blocked)) continue;
            if (blocked) return false;
            if (TryDropContainedUnionLoop(ref workPoints, ref workEnds)) continue;
            if (TryDropSpikeUnionLoop(ref workPoints, ref workEnds)) continue;
            if (TrySpliceSharedUnionEdge(ref workPoints, ref workEnds, false)) continue;
            if (TrySpliceSharedUnionEdges(ref workPoints, ref workEnds)) continue;
            if (!doubledSpent && TryLinkDoubledSpanPair(ref workPoints, ref workEnds)) { doubledSpent = true; continue; }
            if (conforms < maximumConforms && TryConformNearSpans(ref workPoints, ref workEnds)) { conforms++; continue; }
            if (TryLinkSharedVertexArcs(ref workPoints, ref workEnds)) continue;
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
        if (!SvgContourStructure.AreSplitLoopsAdmissible(splitPoints.AsSpan(), splitEnds.AsSpan(), _cancellationToken)) return false;
        mixedResolved = true;
        return true;
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

    /// <summary>Reports whether every loop keeps a vertex neither shared with nor lying on any other loop.</summary>
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
                if (!shared)
                {
                    for (int other = 0; other < workEnds.Length && !shared; other++)
                    {
                        if (other == loop) continue;
                        int otherStart = other == 0 ? 0 : workEnds[other - 1];
                        if (SvgContourStructure.IsPointOnLoopBoundary(workPoints, otherStart, workEnds[other], workPoints[index])) { shared = true; break; }
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


    internal static void ChargePairs(ParserWorkCounter workCounter, long pairs, CancellationToken cancellationToken)
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

    /// <summary>Retains one polygon fill carrying translated spans from the path-fill route.</summary>
    private SvgParsedGeometry RetainPolygonFill(SvgDevicePoint[] retained, int[] ends, int[]? signs, SvgQuadSpan[] spans, SvgCubicSpan[] cubicSpans, SvgColor color, bool hasFill, bool isEvenOdd, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget.BudgetReservation? reservation, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(retained);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(budget);
        SvgPolygonFill fill = SvgPolygonFill.Create(retained, ends, signs, spans, cubicSpans, color, hasFill, isEvenOdd, mapping, clip, reservation, budget);
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
