namespace Lokad.Svg.Rendering;

using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

internal readonly record struct SvgGlyphRgbCoverage(byte Red, byte Green, byte Blue)
{
    internal byte Mean => (byte)((Red + Green + Blue) / 3);
}

/// <summary>Samples positioned quadratic outlines with the measured symmetric LCD filter.</summary>
/// <remarks>Borrows closed device contours and exclusive caller-owned crossing scratch;
/// retains no storage. Crossings are shared across sample rows and columns in the
/// six by five cell grid, with separate directed limits at exact vertices. Contours own center boundaries according
/// to the independently measured Windows convention; quadratic evaluation preserves
/// constant coordinates. Monotonic quadratic scans use bounded implicit conic
/// coefficients; implied points round after the sampling-grid map. Enabled smart
/// dropout control includes collapsed tips on both axes and keeps their selected
/// cells inside the mapped contour bounds. Zero-direction crossings represent
/// isolated quadratic tangencies; they affect enabled dropouts without changing winding.
/// Monotonic axis edges own their smooth curve continuations; searching those
/// borrowed edges charges work and observes cancellation without retaining storage.
/// Every segment, crossing, comparison and cell consumes work and observes cancellation.
/// Callers own positioning, bounds, clipping, preblend and scratch reservations;
/// independent calls with separate scratch may run concurrently.</remarks>
internal static class SvgGlyphLcdCoverage
{
    /// <summary>One directed scan crossing in borrowed scratch; never retained.</summary>
    internal readonly record struct Crossing(double Position, int Direction);

    [Flags]
    private enum CellCoverage : byte { None = 0, Interior = 1, Boundary = 2, Dropout = 4, Closed = 8 }

    internal static SvgGlyphRgbCoverage GetCoverage(ReadOnlySpan<OpenTypeQuadraticSegment> segments, int pixelX, int pixelY, Span<Crossing> scratch, bool dropouts, ParserWorkCounter work, CancellationToken token)
    {
        GlyphCoverageProbe.Started();
        work.Consume(token);
        if (scratch.Length < (long)segments.Length * 2) throw new ArgumentException("Coverage scratch must hold two crossings per segment.", nameof(scratch));
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
        Span<CellCoverage> cells = stackalloc CellCoverage[50]; cells.Clear();
        for (int row = 0; row < 5; row++)
        {
            double sample = pixelY * 5d + row + .5;
            int count = Gather(segments, sample, false, false, scratch, work, token, out bool endpoint);
            for (int column = -2; column < 8; column++)
                cells[row * 10 + column + 2] = Classify(scratch[..count], pixelX * 6d + column + .5, false, dropouts, leftBound, rightBound, work, token);
            if (endpoint)
            {
                // Compare the two directed limits only when the scan meets a
                // vertex. This owns horizontal borders and their corners without
                // counting a shared monotonic vertex twice in a single scan.
                count = Gather(segments, sample, false, true, scratch, work, token, out _);
                for (int column = -2; column < 8; column++)
                {
                    int index = row * 10 + column + 2;
                    CellCoverage lower = cells[index], upper = Classify(scratch[..count], pixelX * 6d + column + .5, false, dropouts, leftBound, rightBound, work, token);
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
            int count = Gather(segments, sample, true, false, scratch, work, token, out bool endpoint);
            for (int row = 0; row < 5; row++)
            {
                CellCoverage value = Classify(scratch[..count], pixelY * 5d + row + .5, true, dropouts, topBound, bottomBound, work, token);
                int index = row * 10 + column + 2;
                // Exact boundary ownership is checked on either axis. Dropouts
                // are applied after normal ownership on both axes.
                if ((value & CellCoverage.Boundary) != 0) cells[index] = (cells[index] & CellCoverage.Dropout) | (value & CellCoverage.Interior);
                cells[index] |= value & CellCoverage.Dropout;
            }
            if (dropouts && endpoint)
            {
                count = Gather(segments, sample, true, true, scratch, work, token, out _);
                for (int row = 0; row < 5; row++)
                    cells[row * 10 + column + 2] |= Classify(scratch[..count], pixelY * 5d + row + .5, true, true, topBound, bottomBound, work, token) & CellCoverage.Dropout;
            }
        }
        int red = 0, green = 0, blue = 0;
        for (int row = 0; row < 5; row++)
        {
            int weight = row switch { 0 or 4 => 4, 1 or 3 => 9, _ => 10 };
            for (int column = -2; column < 8; column++)
            {
                work.Consume(token);
                if ((cells[row * 10 + column + 2] & (CellCoverage.Interior | CellCoverage.Dropout)) == 0) continue;
                if (column < 4) red += weight;
                if (column is >= 0 and < 6) green += weight;
                if (column >= 2) blue += weight;
            }
        }
        return new((byte)((red * 255 + 108) / 216), (byte)((green * 255 + 108) / 216), (byte)((blue * 255 + 108) / 216));

        void Bound(OpenTypeOutlinePoint point)
        {
            OpenTypeOutlinePoint mapped = Map(point, false);
            minX = Math.Min(minX, mapped.X); maxX = Math.Max(maxX, mapped.X);
            minY = Math.Min(minY, mapped.Y); maxY = Math.Max(maxY, mapped.Y);
        }

        static CellCoverage Classify(ReadOnlySpan<Crossing> crossings, double sample, bool horizontal, bool dropouts, double lowerBound, double upperBound, ParserWorkCounter work, CancellationToken token)
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
                    if (dropouts && IsDropout(left, right, crossings, sample, horizontal, lowerBound, upperBound, work, token)) value |= CellCoverage.Dropout;
                }
                else if (dropouts && previous == 0 && winding == 0 && IsDropout(position, position, crossings, sample, horizontal, lowerBound, upperBound, work, token)) value |= CellCoverage.Dropout;
            }
            return value;

            static bool IsDropout(double start, double end, ReadOnlySpan<Crossing> crossings, double sample, bool horizontal, double lowerBound, double upperBound, ParserWorkCounter work, CancellationToken token)
            {
                double first = Math.Ceiling(start - .5), last = Math.Floor(end - .5);
                if (first != last + 1) return false;
                double midpoint = (start + end) * .5;
                double selected = horizontal ? Math.Floor(midpoint) : Math.Ceiling(midpoint) - 1;
                // A tip on an exclusive contour bound selects the other cell.
                if (selected < lowerBound || selected >= upperBound) selected = selected == first ? last : first;
                if (selected < lowerBound || selected >= upperBound || selected != Math.Floor(sample)) return false;
                double other = (selected == first ? last : first) + .5;
                // The neighbor query disables dropouts, bounding this call to
                // one extra normal-coverage scan, including collapsed tips.
                return (Classify(crossings, other, horizontal, false, lowerBound, upperBound, work, token) & CellCoverage.Interior) == 0;
            }
        }

        static int Gather(ReadOnlySpan<OpenTypeQuadraticSegment> segments, double sample, bool horizontal, bool upper, Span<Crossing> scratch, ParserWorkCounter work, CancellationToken token, out bool endpoint)
        {
            int count = 0; endpoint = false;
            foreach (OpenTypeQuadraticSegment segment in segments)
            {
                work.Consume(token);
                OpenTypeOutlinePoint a = Map(segment.Start, horizontal), b = Map(segment.Control, horizontal), c = Map(segment.End, horizontal);
                endpoint |= a.Y == sample || c.Y == sample;
                if (segment.Control == segment.End)
                {
                    if (upper ? a.Y < sample && sample <= c.Y || c.Y < sample && sample <= a.Y : a.Y <= sample && sample < c.Y || c.Y <= sample && sample < a.Y)
                        scratch[count++] = new(a.X + (c.X - a.X) * (sample - a.Y) / (c.Y - a.Y), c.Y > a.Y ? 1 : -1);
                    continue;
                }
                double A = a.Y - 2 * b.Y + c.Y, B = 2 * (b.Y - a.Y), C = a.Y - sample;
                if (A == 0) { if (B != 0) Include(-C / B, a, b, c, A, B, sample, horizontal, upper, segments, scratch, work, token, ref count); continue; }
                double discriminant = B * B - 4 * A * C; if (discriminant < 0) continue;
                double root = Math.Sqrt(discriminant);
                Include((-B + root) / (2 * A), a, b, c, A, B, sample, horizontal, upper, segments, scratch, work, token, ref count);
                if (root != 0) Include((-B - root) / (2 * A), a, b, c, A, B, sample, horizontal, upper, segments, scratch, work, token, ref count);
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
            static void Include(double t, OpenTypeOutlinePoint a, OpenTypeOutlinePoint b, OpenTypeOutlinePoint c, double A, double B, double sample, bool horizontal, bool upper, ReadOnlySpan<OpenTypeQuadraticSegment> segments, Span<Crossing> scratch, ParserWorkCounter work, CancellationToken token, ref int count)
            {
                if (t is < 0 or > 1) return;
                double derivative = B + 2 * A * t;
                if (upper ? t == 0 && derivative > 0 || t == 1 && derivative < 0 : t == 0 && derivative < 0 || t == 1 && derivative > 0) return;
                // A monotonic axis edge owns its smooth continuation into a
                // curve. Curved extrema and isolated tangencies still retain dropouts.
                if (derivative == 0 && (t == 0 || t == 1) && AxisEdgeContinues(t == 0 ? a : c, b, t == 0 ? c : a, segments, horizontal, work, token)) return;
                // Horner evaluation leaves a constant coordinate exact. A
                // Bernstein sum can drift across a center on a straight stem.
                double crossing = a.X + t * (2 * (b.X - a.X) + t * (a.X - 2 * b.X + c.X));
                if (t is > 0 and < 1 && derivative != 0) crossing = Adjust(crossing, sample, a, b, c, horizontal, work, token);
                scratch[count++] = new(crossing, Math.Sign(derivative));

                static bool AxisEdgeContinues(OpenTypeOutlinePoint endpoint, OpenTypeOutlinePoint control, OpenTypeOutlinePoint other, ReadOnlySpan<OpenTypeQuadraticSegment> segments, bool horizontal, ParserWorkCounter work, CancellationToken token)
                {
                    double direction = control.X - endpoint.X;
                    if (direction == 0) direction = other.X - endpoint.X;
                    if (direction == 0) return false;
                    foreach (OpenTypeQuadraticSegment segment in segments)
                    {
                        work.Consume(token);
                        OpenTypeOutlinePoint start = Map(segment.Start, horizontal), end = Map(segment.End, horizontal);
                        if (start.Y != endpoint.Y || end.Y != endpoint.Y || start != endpoint && end != endpoint) continue;
                        OpenTypeOutlinePoint middle = Map(segment.Control, horizontal);
                        if (middle.Y != endpoint.Y || middle.X < Math.Min(start.X, end.X) || middle.X > Math.Max(start.X, end.X)) continue;
                        OpenTypeOutlinePoint neighbor = start == endpoint ? end : start;
                        if (direction > 0 ? neighbor.X < endpoint.X : neighbor.X > endpoint.X) return true;
                    }
                    return false;
                }

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
                    if (Math.Max(tx, ty) > 3200) return crossing;
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
                    if (qa == 0) return qb == 0 ? crossing : origin - direction * qc / qb;
                    double discriminant = qb * qb - 4 * qa * qc;
                    if (discriminant < 0) return crossing;
                    double root = Math.Sqrt(discriminant), first = origin + direction * (-qb + root) / (2 * qa), second = origin + direction * (-qb - root) / (2 * qa);
                    return Math.Abs(first - crossing) <= Math.Abs(second - crossing) ? first : second;

                    static int Bits(long value) => value == 0 ? 0 : System.Numerics.BitOperations.Log2((ulong)value) + 1;
                }
            }
        }

        static OpenTypeOutlinePoint Map(OpenTypeOutlinePoint p, bool horizontal)
        {
            // Implied points round only after the six by five map, with a half
            // F26 unit rounding upward in the font's upward Y axis.
            double x = Math.Floor(p.X * 384 + .5) / 64, y = Math.Floor(p.Y * 320 + .25) / 64;
            return horizontal ? new(y, x) : new(x, y);
        }
    }
}
