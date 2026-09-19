using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Owns temporary orthogonal-contour lowering storage until retained rectangle bands detach.</summary>
internal ref struct SvgOrthogonalGeometryLowering
{
    private const int IntegerStorageBytes = 4;
    private const int RectangleStorageBytes = 56;

    private readonly BudgetedParserList<SvgPoint> _points;
    private readonly string _elementName;
    private readonly bool _isPath;
    private readonly int _maximumGeometryElements;
    private readonly ParserWorkCounter _workCounter;
    private readonly CancellationToken _cancellationToken;
    private readonly BudgetedParserList<int> _boundaries;
    private readonly BudgetedParserList<int> _intersections;
    private readonly BudgetedParserList<SvgRectangle> _rectangles;
    private int _geometryElementCount;

    internal SvgOrthogonalGeometryLowering(BudgetedParserList<SvgPoint> points, string elementName, bool isPath, bool validateContour, int maximumGeometryElements, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        _points = points;
        _elementName = elementName;
        _isPath = isPath;
        _maximumGeometryElements = maximumGeometryElements;
        _workCounter = workCounter;
        _cancellationToken = cancellationToken;
        if (validateContour) ValidateSimpleContour(points, elementName, workCounter, cancellationToken);
        _geometryElementCount = points.Count;
        _boundaries = new BudgetedParserList<int>(budget, SvgMemoryCategory.Geometry, IntegerStorageBytes);
        try
        {
            for (int pointIndex = 0; pointIndex < points.Count; pointIndex++) _boundaries.Add(points[pointIndex].Y);
            _boundaries.Sort(_workCounter, _cancellationToken);
            _intersections = new BudgetedParserList<int>(budget, SvgMemoryCategory.Geometry, IntegerStorageBytes);
            try
            {
                _rectangles = new BudgetedParserList<SvgRectangle>(budget, SvgMemoryCategory.Geometry, RectangleStorageBytes);
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

    internal SvgParsedGeometry Lower(SvgColor color, bool hasFill, AxisTransform transform)
    {
        for (int boundaryIndex = 0; boundaryIndex < _boundaries.Count;)
        {
            int top = _boundaries[boundaryIndex];
            while (boundaryIndex < _boundaries.Count && _boundaries[boundaryIndex] == top) boundaryIndex++;
            if (boundaryIndex == _boundaries.Count) break;
            int bottom = _boundaries[boundaryIndex];
            _intersections.Clear();
            for (int pointIndex = 0; pointIndex < _points.Count; pointIndex++)
            {
                _workCounter.Consume(_cancellationToken);
                SvgPoint start = _points[pointIndex]; SvgPoint end = _points[(pointIndex + 1) % _points.Count];
                if (start.X == end.X && Math.Min(start.Y, end.Y) <= top && Math.Max(start.Y, end.Y) >= bottom) _intersections.Add(start.X);
            }
            _intersections.Sort(_workCounter, _cancellationToken);
            if (!_isPath && _intersections.Count == 0) continue;
            if (_intersections.Count % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, SimpleShapeMessage());
            for (int intersectionIndex = 0; intersectionIndex < _intersections.Count; intersectionIndex += 2)
            {
                int left = _intersections[intersectionIndex]; int right = _intersections[intersectionIndex + 1];
                if (right <= left) throw new SvgException(SvgErrorCode.UnsupportedFeature, SimpleShapeMessage());
                if (_geometryElementCount >= _maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, _isPath ? "Path rectangle-band count exceeds the configured path-command limit." : _elementName + " rectangle-band count exceeds the configured path-command limit.");
                _rectangles.Add(new SvgRectangle(SvgGeometrySyntax.ApplyTransform(transform.ScaleX, left, transform.OffsetX), SvgGeometrySyntax.ApplyTransform(transform.ScaleY, top, transform.OffsetY), SvgGeometrySyntax.ApplyTransformScale(transform.ScaleX, (long)right - left), SvgGeometrySyntax.ApplyTransformScale(transform.ScaleY, (long)bottom - top), color, hasFill, null));
                _geometryElementCount++;
            }
        }
        return new SvgParsedGeometry(_rectangles.Detach(), _geometryElementCount);
    }

    public void Dispose()
    {
        _rectangles.Dispose();
        _intersections.Dispose();
        _boundaries.Dispose();
    }

    internal static void ValidateSimpleContour(BudgetedParserList<SvgPoint> points, string elementName, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        for (int firstIndex = 0; firstIndex < points.Count; firstIndex++)
        {
            SvgPoint firstStart = points[firstIndex]; SvgPoint firstEnd = points[(firstIndex + 1) % points.Count];
            bool firstHorizontal = firstStart.Y == firstEnd.Y;
            for (int secondIndex = firstIndex + 1; secondIndex < points.Count; secondIndex++)
            {
                workCounter.Consume(cancellationToken);
                SvgPoint secondStart = points[secondIndex]; SvgPoint secondEnd = points[(secondIndex + 1) % points.Count];
                bool secondHorizontal = secondStart.Y == secondEnd.Y;
                bool intersects = firstHorizontal == secondHorizontal
                    ? firstHorizontal
                        ? firstStart.Y == secondStart.Y && Math.Max(Math.Min(firstStart.X, firstEnd.X), Math.Min(secondStart.X, secondEnd.X)) <= Math.Min(Math.Max(firstStart.X, firstEnd.X), Math.Max(secondStart.X, secondEnd.X))
                        : firstStart.X == secondStart.X && Math.Max(Math.Min(firstStart.Y, firstEnd.Y), Math.Min(secondStart.Y, secondEnd.Y)) <= Math.Min(Math.Max(firstStart.Y, firstEnd.Y), Math.Max(secondStart.Y, secondEnd.Y))
                    : firstHorizontal
                        ? Math.Min(firstStart.X, firstEnd.X) <= secondStart.X && secondStart.X <= Math.Max(firstStart.X, firstEnd.X) && Math.Min(secondStart.Y, secondEnd.Y) <= firstStart.Y && firstStart.Y <= Math.Max(secondStart.Y, secondEnd.Y)
                        : Math.Min(secondStart.X, secondEnd.X) <= firstStart.X && firstStart.X <= Math.Max(secondStart.X, secondEnd.X) && Math.Min(firstStart.Y, firstEnd.Y) <= secondStart.Y && secondStart.Y <= Math.Max(firstStart.Y, firstEnd.Y);
                if (!intersects) continue;
                bool adjacent = secondIndex == firstIndex + 1 || (firstIndex == 0 && secondIndex == points.Count - 1);
                SvgPoint shared = secondIndex == firstIndex + 1 ? firstEnd : firstStart;
                if (!adjacent || firstHorizontal == secondHorizontal || (firstHorizontal ? new SvgPoint(secondStart.X, firstStart.Y) : new SvgPoint(firstStart.X, secondStart.Y)) != shared) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only simple closed axis-aligned integer " + elementName + " contours are implemented.");
            }
        }
    }

    private string SimpleShapeMessage() => _isPath ? "Only simple closed absolute axis-aligned integer paths are implemented." : "Only simple closed axis-aligned integer " + _elementName + " elements are implemented.";
}

/// <summary>Owns the exact retained rectangle-band array produced by one lowered orthogonal shape.</summary>
internal readonly record struct SvgParsedGeometry(OwnedParserArray<SvgRectangle> Storage, int ElementCount) : IDisposable
{
    internal ReadOnlySpan<SvgRectangle> Rectangles => Storage.Items;

    public void Dispose() => Storage.Dispose();
}
