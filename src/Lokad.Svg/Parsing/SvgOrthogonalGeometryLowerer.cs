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

        return FinishPolygonFill(mapped, ends, color, hasFill, false, frame, budget);

        static SvgDevicePoint ShiftVertex(SvgViewportFrame? viewport, double x, double y)
            => viewport is null ? new SvgDevicePoint(x, y) : new SvgDevicePoint(x * viewport.Value.ScaleX + viewport.Value.OffsetX, y * viewport.Value.ScaleY + viewport.Value.OffsetY);
    }

    /// <summary>Lowers integer-coordinate curved path data through affine flattening into one polygon fill.</summary>
    internal SvgParsedGeometry LowerCurveFill(string d, int commandCount, bool hasArc, SvgResolvedPaintableElement element, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        if (commandCount >= _maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, "Path curve command count exceeds the configured path-command limit.");
        double frameScaleX = frame?.ScaleX ?? 1;
        double frameScaleY = frame?.ScaleY ?? 1;
        double frameOffsetX = frame?.OffsetX ?? 0;
        double frameOffsetY = frame?.OffsetY ?? 0;
        if (!double.IsFinite(frameScaleX) || !double.IsFinite(frameScaleY) || !double.IsFinite(frameOffsetX) || !double.IsFinite(frameOffsetY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Viewport mapping exceeds the admitted numeric range.");
        AxisTransform transform = element.Transform;
        double scaleX = transform.ScaleX * frameScaleX;
        double scaleY = transform.ScaleY * frameScaleY;
        if (hasArc && (scaleX != scaleY || scaleX <= 0)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Elliptical arcs under non-uniform polygon transforms stay unsupported.");
        const int FlattenSegmentsPerCommand = 1024;
        const int MaximumFlattenedContourPoints = 65536;
        SvgFlattenPolicy flattenPolicy = new(0.25, 64, FlattenSegmentsPerCommand);
        long wantedPoints = (long)_maximumGeometryElements * (FlattenSegmentsPerCommand + 1);
        int pointCapacity = wantedPoints > MaximumFlattenedContourPoints ? MaximumFlattenedContourPoints : (int)wantedPoints;
        int contourCapacity = pointCapacity < _maximumGeometryElements ? pointCapacity : _maximumGeometryElements;
        long scratchBytes;
        try { scratchBytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(contourCapacity)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Curve flattening exceeds the supported expansion range."); }
        using RenderBudget.BudgetReservation scratch = budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
        SvgContourBuilder builder = new(pointCapacity, contourCapacity);
        SvgAffineTransform deviceMap = SvgAffineTransform.FromMatrix(frameScaleX, 0, 0, frameScaleY, frameOffsetX, frameOffsetY).Compose(SvgAffineTransform.FromMatrix(transform.ScaleX, 0, 0, transform.ScaleY, transform.OffsetX, transform.OffsetY));
        SvgPathDataParser.ParsePath(d, deviceMap, builder, flattenPolicy, _maximumGeometryElements, _workCounter, _cancellationToken);
        using BudgetedParserList<SvgDevicePoint> mapped = new(budget, SvgMemoryCategory.Geometry, _workCounter, _cancellationToken);
        int[] ends = new int[builder.ContourCount];
        for (int contour = 0; contour < builder.ContourCount; contour++)
        {
            ReadOnlySpan<SvgDevicePoint> source = builder.GetContour(contour);
            SvgDevicePoint previous = default;
            SvgDevicePoint first = default;
            bool hasKept = false;
            int keptBefore = mapped.Count;
            for (int index = 0; index < source.Length; index++)
            {
                _workCounter.Consume(_cancellationToken);
                SvgDevicePoint vertex = source[index];
                if (hasKept && previous == vertex) continue;
                if (hasKept && vertex == first && index == source.Length - 1) continue;
                if (!hasKept) first = vertex;
                previous = vertex;
                hasKept = true;
                mapped.Add(vertex);
            }

            if (mapped.Count - keptBefore < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer path contours are implemented.");
            ends[contour] = mapped.Count;
        }

        return FinishPolygonFill(mapped, ends, element.Presentation.Color, element.Presentation.HasFill, true, frame, budget);
    }

    /// <summary>Validates separated device contours once and retains one polygon fill with scene-owned storage.</summary>
    private SvgParsedGeometry FinishPolygonFill(BudgetedParserList<SvgDevicePoint> mapped, int[] ends, SvgColor color, bool hasFill, bool hasCurves, SvgViewportFrame? frame, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(mapped);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(budget);
        SvgDevicePoint[] retained = mapped.AsSpan().ToArray();
        foreach (SvgDevicePoint vertex in retained)
        {
            if (!double.IsFinite(vertex.X) || !double.IsFinite(vertex.Y) || Math.Abs(vertex.X) > int.MaxValue || Math.Abs(vertex.Y) > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill coordinates exceed the admitted numeric range.");
        }

        ChargePairs(_workCounter, (long)retained.Length * (retained.Length - 1) / 2, _cancellationToken);
        if (SvgContourStructure.ClassifySet(retained, ends, _cancellationToken) != SvgContourFlags.None) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only simple diagonal polygon contours are implemented; touching and self-intersecting contours stay unsupported.");
        int[]? signs = null;
        if (_isEvenOdd)
        {
            signs = new int[ends.Length];
            ChargePairs(_workCounter, (long)ends.Length * (ends.Length - 1) / 2, _cancellationToken);
            SvgContourNesting.ComputeParitySigns(retained, ends, signs, _cancellationToken);
        }

        SvgPolygonFill fill = SvgPolygonFill.Create(retained, ends, signs, color, hasFill, _isEvenOdd, hasCurves, frame is null ? (SvgClipBox?)null : frame.Value.Clip, budget);
        _geometries.Add(SvgGeometry.FromPolygonFill(fill));
        _geometryElementCount++;
        return new SvgParsedGeometry(_geometries.Detach(), _geometryElementCount);

        static void ChargePairs(ParserWorkCounter workCounter, long pairs, CancellationToken cancellationToken)
        {
            for (long charged = 0; charged < pairs; charged += ParserWorkCounter.ByteCheckpointSize) workCounter.Consume(cancellationToken);
        }
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
