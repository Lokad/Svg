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
            return CopyContours(transformed, decoded.ContourEnds, budget, workCounter, cancellationToken);
        }
        finally { reservation.Dispose(); }

        SvgGlyphOutlinePoint Transform(OpenTypeOutlinePoint point)
        {
            double x = glyph.X + point.X * scale;
            double y = glyph.Y - point.Y * scale;
            if (!double.IsFinite(x) || !double.IsFinite(y)) throw new SvgException(SvgErrorCode.LimitExceeded, "The transformed glyph outline exceeds the admitted numeric range.");
            return new SvgGlyphOutlinePoint(x, y);
        }
    }

    private static SvgGlyphContourStorage CopyContours(ReadOnlySpan<SvgGlyphQuadraticSegment> segments, ReadOnlySpan<int> contourEnds, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        if (segments.Length == 0) return new([], null);
        if (contourEnds.Length == 0 || contourEnds[^1] != segments.Length) throw new InvalidOperationException("Decoded glyph contour boundaries do not cover their segments.");
        RenderBudget.BudgetReservation reservation = budget.Reserve(SvgGlyphGeometry.GetStorageByteCount(contourEnds.Length, segments.Length), SvgMemoryCategory.Scene);
        try
        {
            var contours = new SvgGlyphQuadraticContour[contourEnds.Length];
            int first = 0;
            for (int contourIndex = 0; contourIndex < contours.Length; contourIndex++)
            {
                Consume(workCounter, cancellationToken);
                int end = contourEnds[contourIndex];
                if (end <= first || end > segments.Length) throw new InvalidOperationException("Decoded glyph contour boundaries are invalid.");
                int length = end - first;
                var copied = new SvgGlyphQuadraticSegment[length];
                for (int index = 0; index < copied.Length; index++)
                {
                    Consume(workCounter, cancellationToken);
                    copied[index] = segments[first + index];
                    GlyphOutlineProbe.SegmentCopied();
                }
                contours[contourIndex] = new SvgGlyphQuadraticContour(copied);
                first = end;
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
    private SvgGlyphQuadraticContour[] _contours;
    private RenderBudget.BudgetReservation? _reservation;
    private SvgGlyphContourStorageState _state;

    internal SvgGlyphContourStorage(SvgGlyphQuadraticContour[] contours, RenderBudget.BudgetReservation? reservation)
    {
        ArgumentNullException.ThrowIfNull(contours);
        _contours = contours;
        _reservation = reservation;
        _state = SvgGlyphContourStorageState.Live;
    }

    /// <summary>Gets arrays while this storage remains live; a transferred or disposed owner cannot expose them again.</summary>
    internal SvgGlyphQuadraticContour[] Contours
        => _state == SvgGlyphContourStorageState.Live
            ? _contours
            : throw new InvalidOperationException("Glyph contour storage no longer owns live contour arrays.");

    /// <summary>Transfers arrays and their reservation exactly once to the retained geometry that will own both.</summary>
    internal SvgGlyphContourTransfer TransferOwnership()
    {
        if (_state != SvgGlyphContourStorageState.Live) throw new InvalidOperationException("Glyph contour storage can transfer ownership only while live.");
        SvgGlyphQuadraticContour[] contours = _contours;
        RenderBudget.BudgetReservation? reservation = _reservation;
        _contours = [];
        _reservation = null;
        _state = SvgGlyphContourStorageState.Transferred;
        return new SvgGlyphContourTransfer(contours, reservation);
    }

    public void Dispose()
    {
        if (_state != SvgGlyphContourStorageState.Live) return;
        _contours = [];
        _state = SvgGlyphContourStorageState.Disposed;
        _reservation?.Dispose();
        _reservation = null;
    }
}

/// <summary>Represents the one-way lifetime of glyph contour arrays before they become retained scene geometry.</summary>
internal enum SvgGlyphContourStorageState { Live, Transferred, Disposed }

/// <summary>Pairs transferred contour arrays with the reservation that must remain live until retained geometry is disposed.</summary>
internal readonly record struct SvgGlyphContourTransfer(SvgGlyphQuadraticContour[] Contours, RenderBudget.BudgetReservation? Reservation);
