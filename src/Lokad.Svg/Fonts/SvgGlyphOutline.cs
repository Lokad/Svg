namespace Lokad.Svg.Fonts;

using Lokad.Svg.Rendering;
using Lokad.Svg.Parsing;

/// <summary>Converts one decoded TrueType glyph outline into SVG user coordinates.</summary>
internal static class SvgGlyphOutline
{
    internal static SvgGlyphQuadraticContour[] GetContours(SvgPreparedFont font, SvgPositionedGlyph glyph, double fontSize, CancellationToken cancellationToken)
    {
        using SvgGlyphContourStorage storage = GetContourStorage(font, glyph, fontSize, new RenderBudget(long.MaxValue), null, cancellationToken);
        return storage.Contours;
    }

    /// <summary>Converts one glyph while reserving its temporary transformed segment buffer until contour splitting completes.</summary>
    internal static SvgGlyphQuadraticContour[] GetContours(SvgPreparedFont font, SvgPositionedGlyph glyph, double fontSize, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        using SvgGlyphContourStorage storage = GetContourStorage(font, glyph, fontSize, budget, workCounter, cancellationToken);
        return storage.Contours;
    }

    /// <summary>Converts one glyph and transfers the final contour-array scene reservation to the retained glyph geometry.</summary>
    internal static SvgGlyphContourStorage GetContourStorage(SvgPreparedFont font, SvgPositionedGlyph glyph, double fontSize, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(budget);
        if (glyph.GlyphIndex == 0 || glyph.GlyphIndex >= font.GlyphCount) throw new ArgumentOutOfRangeException(nameof(glyph));
        if (!double.IsFinite(fontSize) || fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (!double.IsFinite(glyph.X) || !double.IsFinite(glyph.Y)) throw new ArgumentOutOfRangeException(nameof(glyph));
        double scale = fontSize / font.UnitsPerEm;
        if (!double.IsFinite(scale)) throw new ArgumentOutOfRangeException(nameof(fontSize));

        using OpenTypeGlyphContourStorage decoded = OpenTypeFont.GetGlyphContours(font.Bytes, glyph.GlyphIndex, font.Metrics, budget, workCounter, cancellationToken);
        OpenTypeQuadraticSegment[] source = decoded.Segments;
        if (source.Length == 0) return new([], null);
        RenderBudget.BudgetReservation reservation = budget.Reserve(checked(32L + (long)source.Length * 48L), SvgMemoryCategory.Geometry);
        try
        {
            var transformed = new SvgGlyphQuadraticSegment[source.Length];
            for (int index = 0; index < transformed.Length; index++)
            {
                Consume(workCounter, cancellationToken);
                OpenTypeQuadraticSegment segment = source[index];
                transformed[index] = new SvgGlyphQuadraticSegment(Transform(segment.Start), Transform(segment.Control), Transform(segment.End));
            }
            return SplitClosedContours(transformed, budget, workCounter, cancellationToken);
        }
        finally { reservation.Dispose(); }

        SvgGlyphOutlinePoint Transform(OpenTypeOutlinePoint point) => new(glyph.X + point.X * scale, glyph.Y - point.Y * scale);
    }

    private static SvgGlyphContourStorage SplitClosedContours(ReadOnlySpan<SvgGlyphQuadraticSegment> segments, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        if (segments.Length == 0) return new([], null);
        int contourCount = 0;
        int first = 0;
        while (first < segments.Length)
        {
            Consume(workCounter, cancellationToken);
            SvgGlyphOutlinePoint start = segments[first].Start;
            int last = first;
            while (last < segments.Length && segments[last].End != start)
            {
                Consume(workCounter, cancellationToken);
                last++;
            }
            if (last == segments.Length) throw new SvgException(SvgErrorCode.MalformedInput, "A decoded glyph contour is not closed.");
            contourCount++;
            first = last + 1;
        }

        RenderBudget.BudgetReservation reservation = budget.Reserve(SvgGlyphGeometry.GetStorageByteCount(contourCount, segments.Length), SvgMemoryCategory.Scene);
        try
        {
            var contours = new SvgGlyphQuadraticContour[contourCount];
            first = 0;
            for (int contourIndex = 0; contourIndex < contours.Length; contourIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgGlyphOutlinePoint start = segments[first].Start;
                int last = first;
                while (segments[last].End != start) last++;
                contours[contourIndex] = new SvgGlyphQuadraticContour(segments[first..(last + 1)].ToArray());
                first = last + 1;
            }
            return new(contours, reservation);
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }

    private static void Consume(ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        if (workCounter is null) cancellationToken.ThrowIfCancellationRequested();
        else workCounter.Consume(cancellationToken);
    }
}

/// <summary>Represents one font-outline point in SVG user coordinates.</summary>
internal readonly record struct SvgGlyphOutlinePoint(double X, double Y);

/// <summary>Represents one SVG-coordinate quadratic glyph segment; a line has control equal to end.</summary>
internal readonly record struct SvgGlyphQuadraticSegment(SvgGlyphOutlinePoint Start, SvgGlyphOutlinePoint Control, SvgGlyphOutlinePoint End);

/// <summary>Represents one closed sequence of quadratic segments in SVG user coordinates.</summary>
internal readonly record struct SvgGlyphQuadraticContour(SvgGlyphQuadraticSegment[] Segments);

/// <summary>Owns final glyph contour arrays and their scene reservation until ownership transfers to retained geometry.</summary>
internal sealed class SvgGlyphContourStorage : IDisposable
{
    private RenderBudget.BudgetReservation? _reservation;

    internal SvgGlyphContourStorage(SvgGlyphQuadraticContour[] contours, RenderBudget.BudgetReservation? reservation)
    {
        Contours = contours;
        _reservation = reservation;
    }

    internal SvgGlyphQuadraticContour[] Contours { get; }

    /// <summary>Transfers the scene reservation exactly once to the retained geometry that owns these arrays.</summary>
    internal RenderBudget.BudgetReservation? DetachReservation()
    {
        RenderBudget.BudgetReservation? reservation = _reservation;
        _reservation = null;
        return reservation;
    }

    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
    }
}
