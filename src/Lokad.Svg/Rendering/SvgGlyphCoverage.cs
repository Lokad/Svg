namespace Lokad.Svg.Rendering;

using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

/// <summary>Samples closed quadratic glyph contours with the non-zero winding fill rule.</summary>
internal static class SvgGlyphCoverage
{
    private const int QuadraticSubdivisions = 8;

    /// <summary>Owns the existing eight subdivision endpoints and original start for each fallback segment.</summary>
    /// <remarks>One conversion thread and glyph. Preparation reserves dominant storage before allocation,
    /// observes cancellation at each subdivision. Coverage borrows immutable points while this owner
    /// remains alive; disposal releases the array reference and aggregate allowance after the last borrower.</remarks>
    internal sealed class PreparedContours : IDisposable
    {
        private SvgGlyphOutlinePoint[]? _points;
        private readonly RenderBudget.BudgetReservation _reservation;

        private PreparedContours(ReadOnlySpan<SvgGlyphQuadraticContour> contours, int pointCount, RenderBudget.BudgetReservation reservation, CancellationToken token)
        {
            _points = new SvgGlyphOutlinePoint[pointCount];
            int index = 0;
            foreach (SvgGlyphQuadraticContour contour in contours)
            foreach (SvgGlyphQuadraticSegment segment in contour.Segments)
            {
                _points[index++] = segment.Start;
                for (int step = 1; step <= QuadraticSubdivisions; step++)
                {
                    token.ThrowIfCancellationRequested();
                    double t = step / (double)QuadraticSubdivisions;
                    double inverse = 1 - t;
                    double currentX = inverse * inverse * segment.Start.X + 2 * inverse * t * segment.Control.X + t * t * segment.End.X;
                    double currentY = inverse * inverse * segment.Start.Y + 2 * inverse * t * segment.Control.Y + t * t * segment.End.Y;
                    if (!double.IsFinite(currentX) || !double.IsFinite(currentY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Quadratic glyph coverage exceeds the admitted numeric range.");
                    _points[index++] = new(currentX, currentY);
                }
            }
            _reservation = reservation;
        }

        internal ReadOnlySpan<SvgGlyphOutlinePoint> Points => _points ?? throw new ObjectDisposedException(nameof(PreparedContours));

        internal static PreparedContours? TryCreate(ReadOnlySpan<SvgGlyphQuadraticContour> contours, RenderBudget budget, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            long segments = GetSegmentCount(contours);
            if (segments == 0 || segments > int.MaxValue / (QuadraticSubdivisions + 1)) return null;
            int count = (int)segments * (QuadraticSubdivisions + 1);
            long bytes = ParserCollectionMemory.GetArrayByteCount<SvgGlyphOutlinePoint>(count) + 96;
            RenderBudget.BudgetReservation reservation = budget.Reserve(bytes, SvgMemoryCategory.Temporary);
            try { return new(contours, count, reservation, token); }
            catch { reservation.Dispose(); throw; }
        }

        public void Dispose()
        {
            _points = null;
            _reservation.Dispose();
        }
    }

    internal static byte GetCoverage(ReadOnlySpan<SvgGlyphQuadraticContour> contours, int pixelX, int pixelY, int samplesPerAxis, CancellationToken cancellationToken)
        => GetCoverage(contours, pixelX, pixelY, 1, 1, samplesPerAxis, cancellationToken);

    internal static byte GetCoverage(ReadOnlySpan<SvgGlyphQuadraticContour> contours, double left, double top, double width, double height, int samplesPerAxis, CancellationToken cancellationToken)
        => GetCoverageCore(contours, [], left, top, width, height, samplesPerAxis, cancellationToken);

    internal static byte GetCoverage(PreparedContours contours, double left, double top, double width, double height, int samplesPerAxis, CancellationToken cancellationToken)
        => GetCoverageCore([], contours.Points, left, top, width, height, samplesPerAxis, cancellationToken);

    private static byte GetCoverageCore(ReadOnlySpan<SvgGlyphQuadraticContour> contours, ReadOnlySpan<SvgGlyphOutlinePoint> points, double left, double top, double width, double height, int samplesPerAxis, CancellationToken cancellationToken)
    {
        if (samplesPerAxis is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(samplesPerAxis));
        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        GlyphCoverageProbe.Started();
        int covered = 0;
        int total = checked(samplesPerAxis * samplesPerAxis);
        for (int y = 0; y < samplesPerAxis; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double sampleY = top + height * (y + 0.5) / samplesPerAxis;
            if (!double.IsFinite(sampleY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Quadratic glyph coverage exceeds the admitted numeric range.");
            for (int x = 0; x < samplesPerAxis; x++)
            {
                double sampleX = left + width * (x + 0.5) / samplesPerAxis;
                if (!double.IsFinite(sampleX)) throw new SvgException(SvgErrorCode.LimitExceeded, "Quadratic glyph coverage exceeds the admitted numeric range.");
                bool contains = points.IsEmpty
                    ? ContainsNonZero(contours, sampleX, sampleY, cancellationToken)
                    : ContainsPreparedNonZero(points, sampleX, sampleY, cancellationToken);
                if (contains) covered++;
            }
        }
        return (byte)((covered * 255 + total / 2) / total);
    }

    /// <summary>Returns the bounded number of quadratic segments visited by one contour operation.</summary>
    internal static long GetSegmentCount(ReadOnlySpan<SvgGlyphQuadraticContour> contours)
    {
        long segments = 0;
        foreach (SvgGlyphQuadraticContour contour in contours) segments = checked(segments + contour.Segments.Length);
        return segments;
    }

    private static bool ContainsNonZero(ReadOnlySpan<SvgGlyphQuadraticContour> contours, double x, double y, CancellationToken cancellationToken)
    {
        int winding = 0;
        int segmentCount = 0;
        foreach (SvgGlyphQuadraticContour contour in contours)
        {
            foreach (SvgGlyphQuadraticSegment segment in contour.Segments)
            {
                if ((segmentCount++ & 127) == 0) cancellationToken.ThrowIfCancellationRequested();
                SvgGlyphOutlinePoint previous = segment.Start;
                for (int step = 1; step <= QuadraticSubdivisions; step++)
                {
                    double t = step / (double)QuadraticSubdivisions;
                    double inverse = 1 - t;
                    double currentX = inverse * inverse * segment.Start.X + 2 * inverse * t * segment.Control.X + t * t * segment.End.X;
                    double currentY = inverse * inverse * segment.Start.Y + 2 * inverse * t * segment.Control.Y + t * t * segment.End.Y;
                    if (!double.IsFinite(currentX) || !double.IsFinite(currentY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Quadratic glyph coverage exceeds the admitted numeric range.");
                    var current = new SvgGlyphOutlinePoint(currentX, currentY);
                    winding += CrossingWinding(previous, current, x, y);
                    previous = current;
                }
            }
        }
        return winding != 0;
    }

    private static bool ContainsPreparedNonZero(ReadOnlySpan<SvgGlyphOutlinePoint> points, double x, double y, CancellationToken token)
    {
        int winding = 0, segmentCount = 0;
        for (int first = 0; first < points.Length; first += QuadraticSubdivisions + 1)
        {
            if ((segmentCount++ & 127) == 0) token.ThrowIfCancellationRequested();
            SvgGlyphOutlinePoint previous = points[first];
            for (int step = 1; step <= QuadraticSubdivisions; step++)
            {
                SvgGlyphOutlinePoint current = points[first + step];
                winding += CrossingWinding(previous, current, x, y);
                previous = current;
            }
        }
        return winding != 0;
    }

    private static int CrossingWinding(SvgGlyphOutlinePoint previous, SvgGlyphOutlinePoint current, double x, double y)
    {
        if (previous.Y <= y)
        {
            if (current.Y > y && IsLeft(previous, current, x, y) > 0) return 1;
        }
        else if (current.Y <= y && IsLeft(previous, current, x, y) < 0) return -1;
        return 0;
    }

    private static double IsLeft(SvgGlyphOutlinePoint start, SvgGlyphOutlinePoint end, double x, double y)
    {
        double result = (end.X - start.X) * (y - start.Y) - (x - start.X) * (end.Y - start.Y);
        if (!double.IsFinite(result)) throw new SvgException(SvgErrorCode.LimitExceeded, "Quadratic glyph coverage exceeds the admitted numeric range.");
        return result;
    }
}
