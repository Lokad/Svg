namespace Lokad.Svg.Rendering;

using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

internal readonly record struct SvgGlyphRgbCoverage(byte Red, byte Green, byte Blue)
{
    internal byte Mean => (byte)((Red + Green + Blue) / 3);
}

/// <summary>Samples positioned quadratic outlines with the measured symmetric LCD filter.</summary>
/// <remarks>Borrows closed device contours and exclusive caller-owned crossing scratch;
/// owns no storage. Prepared rows borrow bounded horizontal crossing sets across
/// pixel columns; vertical gathering remains per pixel. Crossings are shared across sample rows and columns in the
/// six by five cell grid, with separate directed limits at exact vertices. Contours own center boundaries according
/// to the independently measured Windows convention; quadratic evaluation preserves
/// constant coordinates. Monotonic quadratic scans use bounded implicit conic
/// coefficients; implied points round after the sampling-grid map. Enabled smart
/// dropout control includes collapsed tips on both axes and keeps their selected
/// cells inside the mapped contour bounds. Zero-direction crossings represent
/// isolated quadratic tangencies; they affect enabled dropouts without changing winding.
/// Flat curve endpoints retain their one-sided direction so narrow caps close
/// before dropout selection, without searching neighboring contour edges.
/// Every segment, crossing, comparison and cell consumes work and observes cancellation.
/// Callers own positioning, bounds, clipping, preblend and scratch reservations;
/// independent calls with separate scratch may run concurrently.</remarks>
internal static class SvgGlyphLcdCoverage
{
    /// <summary>One directed scan crossing in borrowed scratch; never retained.</summary>
    internal readonly record struct Crossing(double Position, int Direction);

    internal readonly record struct MappedSegment(OpenTypeOutlinePoint Start, OpenTypeOutlinePoint Control, OpenTypeOutlinePoint End, bool IsLine);

    /// <summary>Owns optional immutable contour maps and horizontal crossing storage for one glyph; declines reuse when the allowance is unavailable.</summary>
    /// <remarks>Conversion-thread ownership only. The caller keeps this owner alive while row scans borrow its storage;
    /// disposal releases both array references and the aggregate allowance after the last borrower finishes.
    /// Source contours remain immutable for all row calls; mapped line flags preserve the authored line/curve distinction.</remarks>
    internal sealed class ScanStorage : IDisposable
    {
        private Crossing[]? _crossings;
        private MappedSegment[]? _mapped;
        private readonly RenderBudget.BudgetReservation _reservation;

        private ScanStorage(ReadOnlySpan<OpenTypeQuadraticSegment> segments, RenderBudget.BudgetReservation reservation, ParserWorkCounter work, CancellationToken token)
        {
            _crossings = new Crossing[segments.Length * 20];
            _mapped = new MappedSegment[segments.Length];
            for (int index = 0; index < segments.Length; index++)
            {
                work.Consume(token);
                OpenTypeQuadraticSegment segment = segments[index];
                _mapped[index] = new(Map(segment.Start, false), Map(segment.Control, false), Map(segment.End, false), segment.Control == segment.End);
            }
            _reservation = reservation;
        }

        internal Span<Crossing> Crossings => _crossings ?? throw new ObjectDisposedException(nameof(ScanStorage));
        internal ReadOnlySpan<MappedSegment> MappedSegments => _mapped ?? throw new ObjectDisposedException(nameof(ScanStorage));

        internal ScanRow PrepareRow(ReadOnlySpan<OpenTypeQuadraticSegment> segments, int pixelY, Span<int> counts, ParserWorkCounter work, CancellationToken token)
            => PrepareRowCore(segments, MappedSegments, pixelY, Crossings, counts, work, token);

        internal static ScanStorage? TryCreate(ReadOnlySpan<OpenTypeQuadraticSegment> segments, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            int segmentCount = segments.Length;
            if (segmentCount == 0 || segmentCount > int.MaxValue / 20) return null;
            int count = segmentCount * 20;
            // Dominant crossing/mapping arrays plus a conservative aggregate owner/reservation allowance.
            long bytes = ParserCollectionMemory.GetArrayByteCount<Crossing>(count) + ParserCollectionMemory.GetArrayByteCount<MappedSegment>(segmentCount) + 96;
            if (bytes > budget.AvailableBytes) return null;
            RenderBudget.BudgetReservation reservation = budget.Reserve(bytes, SvgMemoryCategory.Temporary);
            try { return new(segments, reservation, work, token); }
            catch { reservation.Dispose(); throw; }
        }

        public void Dispose()
        {
            _crossings = null;
            _mapped = null;
            _reservation.Dispose();
        }
    }

    [Flags]
    private enum CellCoverage : byte { None = 0, Interior = 1, Boundary = 2, Dropout = 4, Closed = 8 }

    /// <summary>Prepares immutable native dropout bounds once; borrows contours and retains no storage.</summary>
    internal static DropoutBounds PrepareBounds(ReadOnlySpan<OpenTypeQuadraticSegment> segments, bool dropouts, ParserWorkCounter work, CancellationToken token)
        => PrepareBounds(segments, dropouts, false, work, token);

    /// <summary>Prepares native bounds while preserving simple left/bottom selection separately from smart midpoint selection.</summary>
    internal static DropoutBounds PrepareBounds(ReadOnlySpan<OpenTypeQuadraticSegment> segments, bool smartDropouts, bool simpleDropouts, ParserWorkCounter work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        bool dropouts = smartDropouts || simpleDropouts;
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        if (dropouts)
        {
            foreach (OpenTypeQuadraticSegment segment in segments)
            {
                work.Consume(token);
                Bound(segment.Start); Bound(segment.Control); Bound(segment.End);
            }
        }
        // Native scan bounds round to cells before choosing a smart dropout.
        // Preserve a nonempty interval for strokes thinner than one cell.
        double leftBound = Math.Floor(minX + .5), topBound = Math.Floor(minY + .5), rightBound = Math.Floor(maxX + .5), bottomBound = Math.Floor(maxY + .5);
        if (leftBound == rightBound) { leftBound = Math.Floor(minX); rightBound = Math.Ceiling(maxX); }
        if (topBound == bottomBound) { topBound = Math.Floor(minY); bottomBound = Math.Ceiling(maxY); }
        return new(dropouts, leftBound, topBound, rightBound, bottomBound) { Simple = simpleDropouts };

        void Bound(OpenTypeOutlinePoint point)
        {
            OpenTypeOutlinePoint mapped = Map(point, false);
            minX = Math.Min(minX, mapped.X); maxX = Math.Max(maxX, mapped.X);
            minY = Math.Min(minY, mapped.Y); maxY = Math.Max(maxY, mapped.Y);
        }
    }

    internal readonly record struct DropoutBounds(bool Enabled, double Left, double Top, double Right, double Bottom)
    {
        internal bool Simple { get; init; }
    }

    /// <summary>Prepares the five horizontal scans and their directed endpoint limits once per pixel row.</summary>
    /// <remarks>Caller reserves and owns crossing storage, count scratch and contours. All are borrowed;
    /// they must remain unchanged until every pixel in this row has finished. Preparation charges actual
    /// gathering/sorting work and observes cancellation. No vertical scans or per-pixel cells are retained.</remarks>
    internal static ScanRow PrepareRow(ReadOnlySpan<OpenTypeQuadraticSegment> segments, int pixelY, Span<Crossing> crossings, Span<int> counts, ParserWorkCounter work, CancellationToken token)
        => PrepareRowCore(segments, [], pixelY, crossings, counts, work, token);

    private static ScanRow PrepareRowCore(ReadOnlySpan<OpenTypeQuadraticSegment> segments, ReadOnlySpan<MappedSegment> mapped, int pixelY, Span<Crossing> crossings, Span<int> counts, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        int capacity = checked(segments.Length * 2);
        if (crossings.Length < (long)capacity * 10) throw new ArgumentException("Row storage must hold ten directed crossing sets.", nameof(crossings));
        if (counts.Length < 10) throw new ArgumentException("Row count scratch must hold ten scan lengths.", nameof(counts));
        for (int row = 0; row < 5; row++)
        {
            work.Consume(token);
            double sample = pixelY * 5d + row + .5;
            int lower = row * 2, upper = lower + 1;
            counts[lower] = Gather(segments, mapped, sample, false, false, crossings.Slice(lower * capacity, capacity), work, token, out bool endpoint);
            counts[upper] = endpoint ? Gather(segments, mapped, sample, false, true, crossings.Slice(upper * capacity, capacity), work, token, out _) : -1;
        }
        return new(segments, mapped, pixelY, crossings, counts, capacity);
    }

    /// <summary>Borrowed row preparation; caller keeps its exclusive storage and immutable contours alive during all coverage calls.</summary>
    internal readonly ref struct ScanRow
    {
        private readonly ReadOnlySpan<Crossing> _crossings;
        private readonly ReadOnlySpan<int> _counts;
        private readonly int _capacity;

        internal ScanRow(ReadOnlySpan<OpenTypeQuadraticSegment> segments, ReadOnlySpan<MappedSegment> mapped, int pixelY, ReadOnlySpan<Crossing> crossings, ReadOnlySpan<int> counts, int capacity)
        {
            Segments = segments;
            MappedSegments = mapped;
            PixelY = pixelY;
            _crossings = crossings;
            _counts = counts;
            _capacity = capacity;
        }

        internal ReadOnlySpan<OpenTypeQuadraticSegment> Segments { get; }
        internal ReadOnlySpan<MappedSegment> MappedSegments { get; }
        internal int PixelY { get; }
        internal bool IsPrepared => !_counts.IsEmpty;
        internal bool HasEndpoint(int row) => _counts[row * 2 + 1] >= 0;
        internal ReadOnlySpan<Crossing> GetCrossings(int row, bool upper)
        {
            int index = row * 2 + (upper ? 1 : 0);
            return _crossings.Slice(index * _capacity, _counts[index]);
        }
    }

    internal static SvgGlyphRgbCoverage GetCoverage(ReadOnlySpan<OpenTypeQuadraticSegment> segments, int pixelX, int pixelY, Span<Crossing> scratch, bool dropouts, ParserWorkCounter work, CancellationToken token)
    {
        GlyphCoverageProbe.Started();
        work.Consume(token);
        if (scratch.Length < (long)segments.Length * 2) throw new ArgumentException("Coverage scratch must hold two crossings per segment.", nameof(scratch));
        DropoutBounds bounds = PrepareBounds(segments, dropouts, work, token);
        return GetCoverageCore(segments, pixelX, pixelY, scratch, bounds, default, work, token);
    }

    internal static SvgGlyphRgbCoverage GetCoverage(ReadOnlySpan<OpenTypeQuadraticSegment> segments, int pixelX, int pixelY, Span<Crossing> scratch, DropoutBounds bounds, ParserWorkCounter work, CancellationToken token)
    {
        GlyphCoverageProbe.Started();
        work.Consume(token);
        if (scratch.Length < (long)segments.Length * 2) throw new ArgumentException("Coverage scratch must hold two crossings per segment.", nameof(scratch));
        return GetCoverageCore(segments, pixelX, pixelY, scratch, bounds, default, work, token);
    }

    internal static SvgGlyphRgbCoverage GetCoverage(ScanRow scans, int pixelX, Span<Crossing> scratch, DropoutBounds bounds, ParserWorkCounter work, CancellationToken token)
    {
        GlyphCoverageProbe.Started();
        work.Consume(token);
        if (!scans.IsPrepared) throw new ArgumentException("Horizontal scans must be prepared for this row.", nameof(scans));
        if (scratch.Length < (long)scans.Segments.Length * 2) throw new ArgumentException("Coverage scratch must hold two crossings per segment.", nameof(scratch));
        return GetCoverageCore(scans.Segments, pixelX, scans.PixelY, scratch, bounds, scans, work, token);
    }

    private static SvgGlyphRgbCoverage GetCoverageCore(ReadOnlySpan<OpenTypeQuadraticSegment> segments, int pixelX, int pixelY, Span<Crossing> scratch, DropoutBounds bounds, ScanRow scans, ParserWorkCounter work, CancellationToken token)
    {
        bool dropouts = bounds.Enabled;
        double leftBound = bounds.Left, rightBound = bounds.Right, topBound = bounds.Top, bottomBound = bounds.Bottom;
        // Simple dropout control observes cells set by the completed first pass,
        // including its neighboring rows, before selecting the second-pass cell.
        Span<CellCoverage> cells = stackalloc CellCoverage[70]; cells.Clear();
        for (int row = bounds.Simple ? -1 : 0; row < (bounds.Simple ? 6 : 5); row++)
        {
            double sample = pixelY * 5d + row + .5;
            bool endpoint;
            ReadOnlySpan<Crossing> lowerScans;
            if (scans.IsPrepared && row is >= 0 and < 5)
            {
                lowerScans = scans.GetCrossings(row, false);
                endpoint = scans.HasEndpoint(row);
            }
            else
            {
                int count = Gather(segments, scans.MappedSegments, sample, false, false, scratch, work, token, out endpoint);
                lowerScans = scratch[..count];
            }
            for (int column = -2; column < 8; column++)
                cells[(row + 1) * 10 + column + 2] = Classify(lowerScans, pixelX * 6d + column + .5, false, dropouts, bounds.Simple, [], 0, leftBound, rightBound, work, token);
            if (endpoint)
            {
                // Compare the two directed limits only when the scan meets a
                // vertex. This owns horizontal borders and their corners without
                // counting a shared monotonic vertex twice in a single scan.
                ReadOnlySpan<Crossing> upperScans;
                if (scans.IsPrepared && row is >= 0 and < 5) upperScans = scans.GetCrossings(row, true);
                else
                {
                    int count = Gather(segments, scans.MappedSegments, sample, false, true, scratch, work, token, out _);
                    upperScans = scratch[..count];
                }
                for (int column = -2; column < 8; column++)
                {
                    int index = (row + 1) * 10 + column + 2;
                    CellCoverage lower = cells[index], upper = Classify(upperScans, pixelX * 6d + column + .5, false, dropouts, bounds.Simple, [], 0, leftBound, rightBound, work, token);
                    if ((lower & CellCoverage.Interior) != (upper & CellCoverage.Interior))
                    {
                        CellCoverage normal = ((lower | upper) & CellCoverage.Closed) != 0 ? CellCoverage.Interior : CellCoverage.None;
                        cells[index] = ((lower | upper) & CellCoverage.Dropout) | CellCoverage.Boundary | normal;
                    }
                    cells[index] |= upper & CellCoverage.Dropout;
                }
            }
        }
        for (int column = -2; column < 8; column++)
        {
            double sample = pixelX * 6d + column + .5;
            int count = Gather(segments, scans.MappedSegments, sample, true, false, scratch, work, token, out bool endpoint);
            for (int row = 0; row < 5; row++)
            {
                CellCoverage value = Classify(scratch[..count], pixelY * 5d + row + .5, true, dropouts, bounds.Simple, cells, (1 - pixelY * 5) * 10 + column + 2, topBound, bottomBound, work, token);
                int index = (row + 1) * 10 + column + 2;
                // Exact boundary ownership is checked on either axis. Dropouts
                // are applied after normal ownership on both axes.
                if ((value & CellCoverage.Boundary) != 0) cells[index] = (cells[index] & CellCoverage.Dropout) | (value & CellCoverage.Interior);
                cells[index] |= value & CellCoverage.Dropout;
            }
            if (dropouts && endpoint)
            {
                count = Gather(segments, scans.MappedSegments, sample, true, true, scratch, work, token, out _);
                for (int row = 0; row < 5; row++)
                    cells[(row + 1) * 10 + column + 2] |= Classify(scratch[..count], pixelY * 5d + row + .5, true, true, bounds.Simple, cells, (1 - pixelY * 5) * 10 + column + 2, topBound, bottomBound, work, token) & CellCoverage.Dropout;
            }
        }
        int red = 0, green = 0, blue = 0;
        for (int row = 0; row < 5; row++)
        {
            int weight = row switch { 0 or 4 => 4, 1 or 3 => 9, _ => 10 };
            for (int column = -2; column < 8; column++)
            {
                work.Consume(token);
                if ((cells[(row + 1) * 10 + column + 2] & (CellCoverage.Interior | CellCoverage.Dropout)) == 0) continue;
                if (column < 4) red += weight;
                if (column is >= 0 and < 6) green += weight;
                if (column >= 2) blue += weight;
            }
        }
        return new((byte)((red * 255 + 108) / 216), (byte)((green * 255 + 108) / 216), (byte)((blue * 255 + 108) / 216));

        static CellCoverage Classify(ReadOnlySpan<Crossing> crossings, double sample, bool horizontal, bool dropouts, bool simple, ReadOnlySpan<CellCoverage> earlier, int earlierOffset, double lowerBound, double upperBound, ParserWorkCounter work, CancellationToken token)
        {
            int winding = 0; double left = 0; CellCoverage value = CellCoverage.None;
            for (int i = 0; i < crossings.Length;)
            {
                double position = crossings[i].Position; int previous = winding;
                // Coalesce coincident transitions before deciding ownership.
                // Zero-width spans and tangencies remain eligible for enabled
                // smart dropouts, while normal winding still cancels.
                do { work.Consume(token); winding += crossings[i++].Direction; } while (i < crossings.Length && crossings[i].Position == position);
                if (previous == 0 && winding != 0) left = position;
                else if (previous != 0 && winding == 0)
                {
                    double right = position; if (!(right > left)) continue;
                    bool boundary = sample == left || sample == right;
                    // Native clockwise font contours include exact center
                    // borders; their reverse excludes them. Device Y points
                    // downward, and exchanging axes reverses the winding.
                    bool closed = horizontal ? previous > 0 : previous < 0;
                    if (sample > left && sample < right || boundary && closed) value |= CellCoverage.Interior;
                    if (closed && sample >= left && sample <= right) value |= CellCoverage.Closed;
                    if (boundary) value |= CellCoverage.Boundary;
                    if (dropouts && IsDropout(left, right, crossings, sample, horizontal, simple, earlier, earlierOffset, lowerBound, upperBound, work, token)) value |= CellCoverage.Dropout;
                }
                else if (dropouts && previous == 0 && winding == 0 && IsDropout(position, position, crossings, sample, horizontal, simple, earlier, earlierOffset, lowerBound, upperBound, work, token)) value |= CellCoverage.Dropout;
            }
            return value;

            static bool IsDropout(double start, double end, ReadOnlySpan<Crossing> crossings, double sample, bool horizontal, bool simple, ReadOnlySpan<CellCoverage> earlier, int earlierOffset, double lowerBound, double upperBound, ParserWorkCounter work, CancellationToken token)
            {
                double first = Math.Ceiling(start - .5), last = Math.Floor(end - .5);
                if (first != last + 1) return false;
                double midpoint = (start + end) * .5;
                double selected = simple ? horizontal ? first : last : horizontal ? Math.Floor(midpoint) : Math.Ceiling(midpoint) - 1;
                // A tip on an exclusive contour bound selects the other cell.
                if (selected < lowerBound || selected >= upperBound) selected = selected == first ? last : first;
                if (selected < lowerBound || selected >= upperBound || selected != Math.Floor(sample)) return false;
                double other = (selected == first ? last : first) + .5;
                if (horizontal && simple)
                {
                    int neighbor = checked((int)Math.Floor(other) * 10 + earlierOffset);
                    if ((uint)neighbor < (uint)earlier.Length && (earlier[neighbor] & (CellCoverage.Interior | CellCoverage.Dropout)) != 0) return false;
                }
                // The neighbor query disables dropouts, bounding this call to
                // one extra normal-coverage scan, including collapsed tips.
                return (Classify(crossings, other, horizontal, false, simple, [], 0, lowerBound, upperBound, work, token) & CellCoverage.Interior) == 0;
            }
        }

    }

    private static int Gather(ReadOnlySpan<OpenTypeQuadraticSegment> segments, ReadOnlySpan<MappedSegment> mapped, double sample, bool horizontal, bool upper, Span<Crossing> scratch, ParserWorkCounter work, CancellationToken token, out bool endpoint)
    {
        int count = 0; endpoint = false;
        for (int index = 0; index < segments.Length; index++)
        {
            work.Consume(token);
            MappedSegment segment = GetMappedSegment(segments, mapped, index, horizontal);
            OpenTypeOutlinePoint a = segment.Start, b = segment.Control, c = segment.End;
            endpoint |= a.Y == sample || c.Y == sample;
            if (segment.IsLine)
            {
                if (upper ? a.Y < sample && sample <= c.Y || c.Y < sample && sample <= a.Y : a.Y <= sample && sample < c.Y || c.Y <= sample && sample < a.Y)
                    scratch[count++] = new(a.X + (c.X - a.X) * (sample - a.Y) / (c.Y - a.Y), c.Y > a.Y ? 1 : -1);
                continue;
            }
            double A = a.Y - 2 * b.Y + c.Y, B = 2 * (b.Y - a.Y), C = a.Y - sample;
            if (A == 0) { if (B != 0) Include(-C / B, a, b, c, A, B, sample, horizontal, upper, scratch, work, token, ref count); continue; }
            double discriminant = B * B - 4 * A * C; if (discriminant < 0) continue;
            double root = Math.Sqrt(discriminant);
            Include((-B + root) / (2 * A), a, b, c, A, B, sample, horizontal, upper, scratch, work, token, ref count);
            if (root != 0) Include((-B - root) / (2 * A), a, b, c, A, B, sample, horizontal, upper, scratch, work, token, ref count);
        }
        for (int i = 1; i < count; i++)
        {
            work.Consume(token); Crossing item = scratch[i]; int j = i;
            // Framework comparer sorts wrap budget/cancellation failures;
            // every comparison and shift here keeps their original category.
            while (j > 0) { work.Consume(token); if (scratch[j - 1].Position <= item.Position) break; scratch[j] = scratch[j - 1]; j--; }
            scratch[j] = item;
        }
        return count;
        static void Include(double t, OpenTypeOutlinePoint a, OpenTypeOutlinePoint b, OpenTypeOutlinePoint c, double A, double B, double sample, bool horizontal, bool upper, Span<Crossing> scratch, ParserWorkCounter work, CancellationToken token, ref int count)
        {
            if (t is < 0 or > 1) return;
            double derivative = B + 2 * A * t;
            // At a flat endpoint, use the curve's one-sided direction. Only
            // interior tangencies have zero winding; a flat cap must still close.
            if (derivative == 0 && (t == 0 || t == 1)) derivative = t == 0 ? A : -A;
            if (upper ? t == 0 && derivative > 0 || t == 1 && derivative < 0 : t == 0 && derivative < 0 || t == 1 && derivative > 0) return;
            // Horner evaluation leaves a constant coordinate exact. A
            // Bernstein sum can drift across a center on a straight stem.
            double crossing = a.X + t * (2 * (b.X - a.X) + t * (a.X - 2 * b.X + c.X));
            if (t is > 0 and < 1 && derivative != 0) crossing = Adjust(crossing, sample, a, b, c, horizontal, work, token);
            scratch[count++] = new(crossing, Math.Sign(derivative));

            static double Adjust(double crossing, double sample, OpenTypeOutlinePoint a, OpenTypeOutlinePoint b, OpenTypeOutlinePoint c, bool horizontal, ParserWorkCounter work, CancellationToken token)
            {
                work.Consume(token);
                // An implicit quadratic is (Ay*x-Ax*y)^2 +
                // D*(By*x-Bx*y)=0, relative to its start. Normalize both
                // directions first, with upward font Y. Bounded integer
                // coefficient precision determines the scan equation;
                // subdivision changes these coefficients, not its tolerance.
                double ax = (horizontal ? a.Y : a.X) * 64, ay = -(horizontal ? a.X : a.Y) * 64;
                double bx = (horizontal ? b.Y : b.X) * 64, by = -(horizontal ? b.X : b.Y) * 64;
                double cx = (horizontal ? c.Y : c.X) * 64, cy = -(horizontal ? c.X : c.Y) * 64;
                if (ax == cx || ay == cy || bx < Math.Min(ax, cx) || bx > Math.Max(ax, cx) || by < Math.Min(ay, cy) || by > Math.Max(ay, cy)) return crossing;
                int sx = cx > ax ? 1 : -1, sy = cy > ay ? 1 : -1;
                long dx = (long)((bx - ax) * sx), dy = (long)((by - ay) * sy), tx = (long)((cx - ax) * sx), ty = (long)((cy - ay) * sy);
                // Native monotonic curves split spans over fifty sampling
                // cells before coefficient reduction. Each signed midpoint
                // rounds down in the upward font coordinate system.
                for (int depth = 0; Math.Max(tx, ty) > 3200; depth++)
                {
                    if (depth == 32) return crossing;
                    work.Consume(token);
                    double abx = Math.Floor((ax + bx) / 2), aby = Math.Floor((ay + by) / 2);
                    double bcx = Math.Floor((bx + cx) / 2), bcy = Math.Floor((by + cy) / 2);
                    double mx = Math.Floor((abx + bcx) / 2), my = Math.Floor((aby + bcy) / 2);
                    double target = horizontal ? sample * 64 : -sample * 64;
                    double middle = horizontal ? mx : my, start = horizontal ? ax : ay, end = horizontal ? cx : cy;
                    bool firstHalf = end > start ? target <= middle : target >= middle;
                    if (firstHalf) { bx = abx; by = aby; cx = mx; cy = my; }
                    else { ax = mx; ay = my; bx = bcx; by = bcy; }
                    sx = cx > ax ? 1 : -1; sy = cy > ay ? 1 : -1;
                    dx = (long)((bx - ax) * sx); dy = (long)((by - ay) * sy);
                    tx = (long)((cx - ax) * sx); ty = (long)((cy - ay) * sy);
                }
                double firstX = Math.Floor((ax + (sx > 0 ? 32 : -33)) / 64) * 64 + 32;
                double firstY = Math.Floor((ay + (sy > 0 ? 32 : -33)) / 64) * 64 + 32;
                long u = (long)((firstX - ax) * sx), v = (long)((firstY - ay) * sy), determinant = 2 * (dx * ty - tx * dy);
                if (determinant == 0) return crossing;
                int magnitude = Bits(Math.Max(tx, ty)), shift = Math.Max(0, (Bits(Math.Abs(determinant)) + magnitude - 27) / 3);
                if (shift != 0)
                {
                    long half = 1L << (shift - 1);
                    dx = (dx + half) >> shift; dy = (dy + half) >> shift; tx = (tx + half) >> shift; ty = (ty + half) >> shift;
                    u = (u + half) >> shift; v = (v + half) >> shift;
                    determinant = 2 * (dx * ty - tx * dy);
                }
                long step = 1L << (6 - shift), x2 = tx - 2 * dx, y2 = ty - 2 * dy;
                long P = y2 * y2, Q = x2 * x2, R = -x2 * y2, Lx = determinant * dy, Ly = -determinant * dx;
                long ownership = sx > 0 ? (sy < 0 ? 1 : 0) : (sy < 0 ? 0 : 1);
                double f, fx, fy, p, q, r;
                if (magnitude <= 7)
                {
                    f = P * u * u + Q * v * v + 2 * R * u * v + 2 * Lx * u + 2 * Ly * v + ownership;
                    fx = (P * (step + 2 * u) + 2 * R * v + 2 * Lx) * step;
                    fy = (Q * (step + 2 * v) + 2 * R * u + 2 * Ly) * step;
                    p = 2 * P * step * step; q = 2 * Q * step * step; r = 2 * R * step * step;
                }
                else
                {
                    // Evaluate the initial half-polynomial in two bounded
                    // products. Its mixed term occurs once. First and second
                    // differences then give a continuous scan equation at
                    // every sample center without a per-cell curve walk.
                    f = (((P >> 1) * u + R * v + Lx) >> (6 - shift)) * u + (((Q >> 1) * v + Ly) >> (6 - shift)) * v + ownership;
                    fx = P * (u + step / 2) + R * v + Lx; fy = Q * (v + step / 2) + R * u + Ly;
                    p = P * step; q = Q * step; r = R * step;
                }
                double k = horizontal ? (sample * 64 - firstX) / 64 * sx : (-sample * 64 - firstY) / 64 * sy;
                double qa = (horizontal ? q : p) / 2, qb = (horizontal ? fy : fx) - qa + r * k;
                double qc = f + (horizontal ? fx : fy) * k + (horizontal ? p : q) * k * (k - 1) / 2;
                double origin = horizontal ? -firstY / 64 : firstX / 64, direction = horizontal ? -sy : sx;
                if (qa == 0) return qb == 0 ? crossing : Math.Clamp(origin - direction * qc / qb, Math.Min(a.X, c.X), Math.Max(a.X, c.X));
                double discriminant = qb * qb - 4 * qa * qc;
                if (discriminant < 0) return crossing;
                double root = Math.Sqrt(discriminant), first = origin + direction * (-qb + root) / (2 * qa), second = origin + direction * (-qb - root) / (2 * qa);
                // Coefficient reduction must not extend a monotonic leaf past
                // its endpoint. The native scan bounds the reduced equation by
                // that span, including an exact center beside a curved counter.
                return Math.Clamp(Math.Abs(first - crossing) <= Math.Abs(second - crossing) ? first : second, Math.Min(a.X, c.X), Math.Max(a.X, c.X));

                static int Bits(long value) => value == 0 ? 0 : System.Numerics.BitOperations.Log2((ulong)value) + 1;
            }
        }
    }

    private static MappedSegment GetMappedSegment(ReadOnlySpan<OpenTypeQuadraticSegment> segments, ReadOnlySpan<MappedSegment> mapped, int index, bool horizontal)
    {
        MappedSegment value;
        if (mapped.IsEmpty)
        {
            OpenTypeQuadraticSegment source = segments[index];
            value = new(Map(source.Start, false), Map(source.Control, false), Map(source.End, false), source.Control == source.End);
        }
        else value = mapped[index];
        return horizontal ? new(new(value.Start.Y, value.Start.X), new(value.Control.Y, value.Control.X), new(value.End.Y, value.End.X), value.IsLine) : value;
    }

    private static OpenTypeOutlinePoint Map(OpenTypeOutlinePoint p, bool horizontal)
    {
        // Implied points round only after the six by five map, with a half
        // F26 unit rounding upward in the font's upward Y axis.
        double x = Math.Floor(p.X * 384 + .5) / 64, y = Math.Floor(p.Y * 320 + .25) / 64;
        return horizontal ? new(y, x) : new(x, y);
    }
}
