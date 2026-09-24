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
    private const double FlattenFlatness = 0.25;
    private const int FlattenMaxDepth = 64;
    private const int CurveSegmentsPerCommand = 1024;
    private const int ShapeSegmentsPerShape = 4096;
    private const int MaximumFlattenedContourPoints = 65536;

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

    internal SvgParsedGeometry Lower(SvgColor color, bool hasFill, AxisTransform transform, SvgViewportFrame? frame)
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
        return new SvgParsedGeometry(_geometries.Detach(), _geometryElementCount);
    }

    /// <summary>Lowers diagonal solid polygons with separated simple contours to retained device points, keeping touching and non-simple shapes rejected.</summary>
    internal SvgParsedGeometry LowerPolygonFill(SvgColor color, bool hasFill, AxisTransform transform, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        if (_contourEnds.Count == 0 || _contourEnds[_contourEnds.Count - 1] != _points.Count) throw new SvgException(SvgErrorCode.UnsupportedFeature, SimpleShapeMessage());
        if (_geometryElementCount >= _maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, _isPath ? "Path polygon point count exceeds the configured path-command limit." : _elementName + " polygon point count exceeds the configured path-command limit.");
        using BudgetedParserList<SvgDevicePoint> mapped = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
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
                (double x, double y) = transform.ApplyToVertex(vertex.X, vertex.Y);
                mapped.Add(ShiftVertex(frame, x, y));
            }

            if (mapped.Count - keptBefore < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, SimpleShapeMessage());
            ends[contour] = mapped.Count;
            contourStart = contourEnd;
        }

        return FinishPolygonFill(mapped, ends, color, hasFill, SvgPolygonMapping.QuarterEdges, frame, budget);
    }

    private static SvgDevicePoint ShiftVertex(SvgViewportFrame? viewport, double x, double y)
        => viewport is null ? new SvgDevicePoint(x, y) : new SvgDevicePoint(x * viewport.Value.ScaleX + viewport.Value.OffsetX, y * viewport.Value.ScaleY + viewport.Value.OffsetY);

    private static (SvgAffineTransform Map, double ScaleX, double ScaleY) BuildDeviceMap(AxisTransform transform, SvgViewportFrame? frame)
    {
        double frameScaleX = frame?.ScaleX ?? 1;
        double frameScaleY = frame?.ScaleY ?? 1;
        double frameOffsetX = frame?.OffsetX ?? 0;
        double frameOffsetY = frame?.OffsetY ?? 0;
        if (!double.IsFinite(frameScaleX) || !double.IsFinite(frameScaleY) || !double.IsFinite(frameOffsetX) || !double.IsFinite(frameOffsetY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Viewport mapping exceeds the admitted numeric range.");
        double scaleX = transform.ScaleX * frameScaleX;
        double scaleY = transform.ScaleY * frameScaleY;
        SvgAffineTransform deviceMap = SvgAffineTransform.FromMatrix(frameScaleX, 0, 0, frameScaleY, frameOffsetX, frameOffsetY).Compose(SvgAffineTransform.FromMatrix(transform.ScaleX, 0, 0, transform.ScaleY, transform.OffsetX, transform.OffsetY));
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
        (SvgAffineTransform deviceMap, double scaleX, double scaleY) = BuildDeviceMap(element.Transform, frame);
        double mappedRadiusX = radiusX * Math.Abs(scaleX);
        double mappedRadiusY = radiusY * Math.Abs(scaleY);
        using BudgetedParserList<SvgDevicePoint> mapped = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        int[] ends;
        if (mappedRadiusX > 0 && mappedRadiusY > 0)
        {
            SvgDevicePoint center = deviceMap.Apply(centerX, centerY);
            SvgFlattenPolicy flattenPolicy = new(FlattenFlatness, FlattenMaxDepth, ShapeSegmentsPerShape);
            using RenderBudget.BudgetReservation scratch = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(MaximumFlattenedContourPoints) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(1), SvgMemoryCategory.Geometry);
            SvgContourBuilder builder = new(MaximumFlattenedContourPoints, 1);
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

    /// <summary>Lowers one axis-aligned rounded rectangle to a sampled polygon fill.</summary>
    internal SvgParsedGeometry LowerRoundedRectFill(double x, double y, double width, double height, double radiusX, double radiusY, SvgResolvedPaintableElement element, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        (_, double scaleX, double scaleY) = BuildDeviceMap(element.Transform, frame);
        SvgRectangle placed = element.Transform.Apply(new SvgRectangle(x, y, width, height, default, false, null));
        SvgRectangle shifted = frame is null ? placed : frame.Value.Shift(placed);
        double mappedRadiusX = radiusX * Math.Abs(scaleX);
        double mappedRadiusY = radiusY * Math.Abs(scaleY);
        SvgFlattenPolicy flattenPolicy = new(FlattenFlatness, FlattenMaxDepth, ShapeSegmentsPerShape);
        using RenderBudget.BudgetReservation scratch = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(MaximumFlattenedContourPoints) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(1), SvgMemoryCategory.Geometry);
        SvgContourBuilder builder = new(MaximumFlattenedContourPoints, 1);
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
    internal SvgParsedGeometry LowerDeviceFill(string d, int commandCount, bool hasArc, SvgPolygonMapping mapping, SvgResolvedPaintableElement element, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        if (commandCount >= _maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, "Path command count exceeds the configured path-command limit.");
        (SvgAffineTransform deviceMap, double scaleX, double scaleY) = BuildDeviceMap(element.Transform, frame);
        if (hasArc && (scaleX != scaleY || scaleX <= 0)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Elliptical arcs under non-uniform polygon transforms stay unsupported.");
        SvgFlattenPolicy flattenPolicy = new(FlattenFlatness, FlattenMaxDepth, CurveSegmentsPerCommand);
        long wantedPoints = (long)_maximumGeometryElements * (CurveSegmentsPerCommand + 1);
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
        for (int contour = 0; contour < builder.ContourCount; contour++)
        {
            if (AppendStrippedContour(mapped, builder.GetContour(contour), _workCounter, _cancellationToken) < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer path contours are implemented.");
            ends[contour] = mapped.Count;
        }

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

    /// <summary>Strokes user-space fractional point-list vertices through device-space contours with butt caps and miter joins.</summary>
    internal SvgParsedGeometry LowerStrokePointList(BudgetedParserList<SvgFractionalPoint> points, bool closed, SvgResolvedPaintableElement element, SvgColor color, bool hasFill, int width, double miterLimit, SvgViewportFrame? frame, RenderBudget budget)
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
        SvgParsedGeometry stroked = LowerStrokeContours(builder, closedEnds, false, element.Transform, width, miterLimit, color, hasFill, frame, budget);
        return stroked with { ElementCount = points.Count + 1 };
    }

    /// <summary>Strokes one integer point contour with butt caps and miter joins into one nonzero outline fill.</summary>
    internal SvgParsedGeometry LowerStrokePolyline(BudgetedParserList<SvgPoint> points, bool closed, AxisTransform transform, int width, double miterLimit, SvgColor color, bool hasFill, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(budget);
        if (points.Count >= _maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, _isPath ? "Path stroke point count exceeds the configured path-command limit." : _elementName + " stroke point count exceeds the configured path-command limit.");
        double deviceWidth = StrokeDeviceWidth(width, transform, frame);
        using BudgetedParserList<SvgDevicePoint> mapped = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        for (int index = 0; index < points.Count; index++)
        {
            _workCounter.Consume(_cancellationToken);
            (double x, double y) = transform.ApplyToVertex(points[index].X, points[index].Y);
            mapped.Add(ShiftVertex(frame, x, y));
        }

        long wantedPoints = 4L * mapped.Count + 8;
        int pointCapacity = wantedPoints > MaximumFlattenedContourPoints ? MaximumFlattenedContourPoints : (int)wantedPoints;
        long scratchBytes;
        try { scratchBytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(closed ? 2 : 1)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the supported expansion range."); }
        using RenderBudget.BudgetReservation scratch = budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
        SvgContourBuilder outlines = new(pointCapacity, closed ? 2 : 1);
        if (closed) SvgStrokeOutline.StrokeClosedPolyline(mapped.AsSpan(), deviceWidth, miterLimit, outlines, _cancellationToken);
        else SvgStrokeOutline.StrokeOpenPolyline(mapped.AsSpan(), deviceWidth, miterLimit, outlines, _cancellationToken);
        return FinishStrokeFill(outlines, color, hasFill, false, frame, budget) with { ElementCount = points.Count + 1 };
    }

    /// <summary>Strokes device-space contours with butt caps and miter joins into one nonzero outline fill.</summary>
    internal SvgParsedGeometry LowerStrokeContours(SvgContourBuilder source, ReadOnlySpan<bool> closedEnds, bool hasCurves, AxisTransform transform, int width, double miterLimit, SvgColor color, bool hasFill, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(budget);
        if (closedEnds.Length != source.ContourCount) throw new InvalidOperationException("Stroke contour closure flags must match the sampled contours.");
        if (source.PointCount >= _maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, _isPath ? "Path stroke point count exceeds the configured path-command limit." : _elementName + " stroke point count exceeds the configured path-command limit.");
        double deviceWidth = StrokeDeviceWidth(width, transform, frame);
        long wantedPoints = 4L * source.PointCount + 8;
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
            if (closedEnds[contour]) SvgStrokeOutline.StrokeClosedPolyline(source.GetContour(contour), deviceWidth, miterLimit, outlines, _cancellationToken);
            else SvgStrokeOutline.StrokeOpenPolyline(source.GetContour(contour), deviceWidth, miterLimit, outlines, _cancellationToken);
        }

        return FinishStrokeFill(outlines, color, hasFill, hasCurves, frame, budget);
    }

    private double StrokeDeviceWidth(int width, AxisTransform transform, SvgViewportFrame? frame)
    {
        double scaleX = transform.ScaleX * (frame?.ScaleX ?? 1);
        double scaleY = transform.ScaleY * (frame?.ScaleY ?? 1);
        if (Math.Abs(scaleX) != Math.Abs(scaleY) || scaleX == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only uniformly scaled " + _elementName + " strokes are implemented.");
        double deviceWidth = width * Math.Abs(scaleX);
        if (!double.IsFinite(deviceWidth) || deviceWidth <= 0 || deviceWidth > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the admitted numeric range.");
        return deviceWidth;
    }

    /// <summary>Strokes path data with butt caps and miter joins into one nonzero outline fill, tracking explicit closepath seals.</summary>
    internal SvgParsedGeometry LowerStrokePath(string d, int commandCount, bool hasArc, bool hasCurves, SvgResolvedPaintableElement element, SvgColor color, bool hasFill, int width, double miterLimit, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        if (commandCount >= _maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, "Path stroke command count exceeds the configured path-command limit.");
        (SvgAffineTransform deviceMap, double scaleX, double scaleY) = BuildDeviceMap(element.Transform, frame);
        if (hasArc && (scaleX != scaleY || scaleX <= 0)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Elliptical arcs under non-uniform polygon transforms stay unsupported.");
        double deviceWidth = StrokeDeviceWidth(width, element.Transform, frame);
        SvgFlattenPolicy flattenPolicy = new(FlattenFlatness, FlattenMaxDepth, CurveSegmentsPerCommand);
        long wantedPoints = (long)_maximumGeometryElements * (CurveSegmentsPerCommand + 1);
        int pointCapacity = wantedPoints > MaximumFlattenedContourPoints ? MaximumFlattenedContourPoints : (int)wantedPoints;
        int contourCapacity = pointCapacity < _maximumGeometryElements ? pointCapacity : _maximumGeometryElements;
        long scratchBytes;
        try { scratchBytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(contourCapacity)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the supported expansion range."); }
        using RenderBudget.BudgetReservation scratch = budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
        SvgContourBuilder builder = new(pointCapacity, contourCapacity);
        using BudgetedParserList<bool> closedEnds = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        SvgPathDataParser.ParsePath(d, deviceMap, builder, flattenPolicy, _maximumGeometryElements, _workCounter, _cancellationToken, closedEnds);
        SvgParsedGeometry stroked = LowerStrokeContours(builder, closedEnds.AsSpan(), hasCurves, element.Transform, width, miterLimit, color, hasFill, frame, budget);
        return stroked with { ElementCount = commandCount + 1 };
    }

    /// <summary>Retains stroke outline loops as one nonzero fill without structure validation, which spike retraces would false-positive.</summary>
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

        SvgPolygonMapping mapping = flattenedCenterline ? SvgPolygonMapping.FlattenedCurves : fractionalVertices ? SvgPolygonMapping.FiniteEdges : SvgPolygonMapping.QuarterEdges;
        SvgDevicePoint[] retained = mapped.AsSpan().ToArray();
        ValidateDeviceRange(retained);
        return RetainPolygonFill(retained, ends, null, color, hasFill, false, mapping, frame is null ? (SvgClipBox?)null : frame.Value.Clip, budget);
    }

    /// <summary>Validates separated device contours once and retains one polygon fill with scene-owned storage.</summary>
    private SvgParsedGeometry FinishPolygonFill(BudgetedParserList<SvgDevicePoint> mapped, int[] ends, SvgColor color, bool hasFill, SvgPolygonMapping mapping, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(mapped);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(budget);
        SvgDevicePoint[] retained = mapped.AsSpan().ToArray();
        ValidateDeviceRange(retained);
        ChargePairs(_workCounter, (long)retained.Length * (retained.Length - 1) / 2, _cancellationToken);
        if (SvgContourStructure.ClassifySet(retained, ends, _cancellationToken) != SvgContourFlags.None) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only simple diagonal polygon contours are implemented; touching and self-intersecting contours stay unsupported.");
        int[]? signs = null;
        if (_isEvenOdd)
        {
            signs = new int[ends.Length];
            ChargePairs(_workCounter, (long)ends.Length * (ends.Length - 1) / 2, _cancellationToken);
            SvgContourNesting.ComputeParitySigns(retained, ends, signs, _cancellationToken);
        }

        return RetainPolygonFill(retained, ends, signs, color, hasFill, _isEvenOdd, mapping, frame is null ? (SvgClipBox?)null : frame.Value.Clip, budget);

        static void ChargePairs(ParserWorkCounter workCounter, long pairs, CancellationToken cancellationToken)
        {
            for (long charged = 0; charged < pairs; charged += ParserWorkCounter.ByteCheckpointSize) workCounter.Consume(cancellationToken);
        }
    }

    private static void ValidateDeviceRange(SvgDevicePoint[] retained)
    {
        foreach (SvgDevicePoint vertex in retained)
        {
            if (!double.IsFinite(vertex.X) || !double.IsFinite(vertex.Y) || Math.Abs(vertex.X) > int.MaxValue || Math.Abs(vertex.Y) > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill coordinates exceed the admitted numeric range.");
        }
    }

    private SvgParsedGeometry RetainPolygonFill(SvgDevicePoint[] retained, int[] ends, int[]? signs, SvgColor color, bool hasFill, bool isEvenOdd, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(retained);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(budget);
        SvgPolygonFill fill = SvgPolygonFill.Create(retained, ends, signs, color, hasFill, isEvenOdd, mapping, clip, budget);
        _geometries.Add(SvgGeometry.FromPolygonFill(fill));
        _geometryElementCount++;
        return new SvgParsedGeometry(_geometries.Detach(), _geometryElementCount);
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
            if (SvgContourTopology.Classify(points.AsSpan(), contourStart, contourEnd) == SvgContourKind.Diagonal) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned " + elementName + " contours are implemented.");
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
