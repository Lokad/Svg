namespace Lokad.Svg.Fonts;

using Lokad.Svg.Rendering;
using Lokad.Svg.Parsing;

/// <summary>Converts one decoded TrueType glyph outline into SVG user coordinates.</summary>
internal static class SvgGlyphOutline
{
    /// <summary>Computes tight geometric glyph bounds through the shared native quadratic-bound evaluator.</summary>
    /// <remarks>Contours remain borrowed. Fixed stack commands reuse path extrema without retaining storage;
    /// every segment/map/root visit charges the shared work counter and observes cancellation.</remarks>
    internal static SvgRectangle Bounds(ReadOnlySpan<SvgGlyphQuadraticContour> contours, SvgAffineTransform map, ParserWorkCounter work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        double left = double.PositiveInfinity, top = double.PositiveInfinity;
        double right = double.NegativeInfinity, bottom = double.NegativeInfinity;
        Span<SvgAuthoredPathCommand> commands = stackalloc SvgAuthoredPathCommand[2];
        foreach (SvgGlyphQuadraticContour contour in contours)
        foreach (SvgGlyphQuadraticSegment segment in contour.Segments)
        {
            commands[0] = new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(segment.Start.X, segment.Start.Y), default, default);
            commands[1] = new(SvgAuthoredPathVerb.Quadratic, SvgAuthoredArcFlags.None, new(segment.Control.X, segment.Control.Y), new(segment.End.X, segment.End.Y), default);
            SvgRectangle box = SvgPathBounds.Compute(commands, map, work, token);
            left = Math.Min(left, box.X); top = Math.Min(top, box.Y);
            right = Math.Max(right, box.X + box.Width); bottom = Math.Max(bottom, box.Y + box.Height);
        }
        return double.IsPositiveInfinity(left) ? default : new(left, top, right - left, bottom - top, default, true, null);
    }

    internal static SvgGlyphQuadraticContour[] GetContours(SvgPreparedFont font, SvgPositionedGlyph glyph, double fontSize, CancellationToken cancellationToken)
    {
        using SvgGlyphContourStorage storage = GetContourStorage(font, glyph, fontSize, new RenderBudget(long.MaxValue), null, cancellationToken);
        return storage.Contours;
    }

    /// <summary>Converts one glyph and transfers the final contour-array scene reservation to the retained glyph geometry.</summary>
    internal static SvgGlyphContourStorage GetContourStorage(SvgPreparedFont font, SvgPositionedGlyph glyph, double fontSize, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => GetContourStorage(font, glyph, fontSize, SvgAffineTransform.Identity, budget, workCounter, cancellationToken);

    /// <summary>Applies a retained glyph-local affine map after ordinary font-unit outline conversion.</summary>
    internal static SvgGlyphContourStorage GetContourStorage(SvgPreparedFont font, SvgPositionedGlyph glyph, double fontSize, SvgAffineTransform glyphMap, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        double scale = Validate(font, glyph, fontSize, budget);

        using OpenTypeGlyphContourStorage decoded = OpenTypeFont.GetGlyphContours(font.Bytes, glyph.GlyphIndex, font.Metrics, budget, workCounter, cancellationToken);
        return TransformDecodedContours(glyph, scale, glyphMap, decoded, budget, workCounter, cancellationToken);
    }

    /// <summary>Converts one glyph while borrowing decoded storage that remains owned and disposed by the caller.</summary>
    internal static SvgGlyphContourStorage GetContourStorage(SvgPreparedFont font, SvgPositionedGlyph glyph, double fontSize, OpenTypeGlyphContourStorage decoded, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => GetContourStorage(font, glyph, fontSize, SvgAffineTransform.Identity, decoded, budget, workCounter, cancellationToken);

    /// <summary>Maps one glyph while borrowing immutable decoded contours; the returned owner reserves its final arrays and observes shared work/cancellation.</summary>
    internal static SvgGlyphContourStorage GetContourStorage(SvgPreparedFont font, SvgPositionedGlyph glyph, double fontSize, SvgAffineTransform glyphMap, OpenTypeGlyphContourStorage decoded, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        double scale = Validate(font, glyph, fontSize, budget);
        return TransformDecodedContours(glyph, scale, glyphMap, decoded, budget, workCounter, cancellationToken);
    }

    private static double Validate(SvgPreparedFont font, SvgPositionedGlyph glyph, double fontSize, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(budget);
        if (glyph.GlyphIndex == 0 || glyph.GlyphIndex >= font.GlyphCount) throw new ArgumentOutOfRangeException(nameof(glyph));
        if (!double.IsFinite(fontSize) || fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (!double.IsFinite(glyph.X) || !double.IsFinite(glyph.Y)) throw new ArgumentOutOfRangeException(nameof(glyph));
        double scale = fontSize / font.UnitsPerEm;
        if (!double.IsFinite(scale)) throw new ArgumentOutOfRangeException(nameof(fontSize));
        return scale;
    }

    private static SvgGlyphContourStorage TransformDecodedContours(SvgPositionedGlyph glyph, double scale, SvgAffineTransform glyphMap, OpenTypeGlyphContourStorage decoded, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        OpenTypeQuadraticSegment[] source = decoded.Segments;
        if (source.Length == 0) return new([], null);
        ReadOnlySpan<int> contourEnds = decoded.ContourEnds;
        if (contourEnds.Length == 0 || contourEnds[^1] != source.Length) throw new InvalidOperationException("Decoded glyph contour boundaries do not cover their segments.");
        RenderBudget.BudgetReservation reservation = budget.Reserve(SvgGlyphGeometry.GetStorageByteCount(contourEnds.Length, source.Length), SvgMemoryCategory.Scene);
        try
        {
            var contours = new SvgGlyphQuadraticContour[contourEnds.Length];
            int first = 0;
            for (int contourIndex = 0; contourIndex < contours.Length; contourIndex++)
            {
                Consume();
                int end = contourEnds[contourIndex];
                if (end <= first || end > source.Length) throw new InvalidOperationException("Decoded glyph contour boundaries are invalid.");
                var segments = new SvgGlyphQuadraticSegment[end - first];
                for (int index = 0; index < segments.Length; index++)
                {
                    Consume();
                    OpenTypeQuadraticSegment segment = source[first + index];
                    segments[index] = new SvgGlyphQuadraticSegment(Transform(segment.Start), Transform(segment.Control), Transform(segment.End));
                    GlyphOutlineProbe.SegmentTransformed();
                }
                contours[contourIndex] = new SvgGlyphQuadraticContour(segments);
                first = end;
            }
            return new(contours, reservation);
        }
        catch
        {
            reservation.Dispose();
            throw;
        }

        SvgGlyphOutlinePoint Transform(OpenTypeOutlinePoint point)
        {
            double x = glyph.X + point.X * scale;
            double y = glyph.Y - point.Y * scale;
            if (glyphMap != SvgAffineTransform.Identity)
            {
                SvgDevicePoint mapped = glyphMap.Apply(x, y);
                x = mapped.X; y = mapped.Y;
            }
            if (!double.IsFinite(x) || !double.IsFinite(y)) throw new SvgException(SvgErrorCode.LimitExceeded, "The transformed glyph outline exceeds the admitted numeric range.");
            return new SvgGlyphOutlinePoint(x, y);
        }

        void Consume()
        {
            if (workCounter is null) cancellationToken.ThrowIfCancellationRequested();
            else workCounter.Consume(cancellationToken);
        }
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
