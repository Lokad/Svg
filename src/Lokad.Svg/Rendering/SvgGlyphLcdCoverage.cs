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
/// constant coordinates. Enabled smart dropout control includes stubs on both axes.
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
        Span<CellCoverage> cells = stackalloc CellCoverage[50]; cells.Clear();
        for (int row = 0; row < 5; row++)
        {
            double sample = pixelY * 5d + row + .5;
            int count = Gather(segments, sample, false, false, scratch, work, token, out bool endpoint);
            for (int column = -2; column < 8; column++)
                cells[row * 10 + column + 2] = Classify(scratch[..count], pixelX * 6d + column + .5, false, dropouts, work, token);
            if (endpoint)
            {
                // Compare the two directed limits only when the scan meets a
                // vertex. This owns horizontal borders and their corners without
                // counting a shared monotonic vertex twice in a single scan.
                count = Gather(segments, sample, false, true, scratch, work, token, out _);
                for (int column = -2; column < 8; column++)
                {
                    int index = row * 10 + column + 2;
                    CellCoverage lower = cells[index], upper = Classify(scratch[..count], pixelX * 6d + column + .5, false, dropouts, work, token);
                    if ((lower & CellCoverage.Interior) != (upper & CellCoverage.Interior))
                    {
                        CellCoverage normal = ((lower | upper) & CellCoverage.Closed) != 0 ? CellCoverage.Interior : CellCoverage.None;
                        cells[index] = ((lower | upper) & CellCoverage.Dropout) | CellCoverage.Boundary | normal;
                    }
                }
            }
        }
        for (int column = -2; column < 8; column++)
        {
            int count = Gather(segments, pixelX * 6d + column + .5, true, false, scratch, work, token, out _);
            for (int row = 0; row < 5; row++)
            {
                CellCoverage value = Classify(scratch[..count], pixelY * 5d + row + .5, true, dropouts, work, token);
                int index = row * 10 + column + 2;
                // Exact boundary ownership is checked on either axis. Dropouts
                // are applied after normal ownership on both axes.
                if ((value & CellCoverage.Boundary) != 0) cells[index] = (cells[index] & CellCoverage.Dropout) | (value & CellCoverage.Interior);
                cells[index] |= value & CellCoverage.Dropout;
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

        static CellCoverage Classify(ReadOnlySpan<Crossing> crossings, double sample, bool horizontal, bool dropouts, ParserWorkCounter work, CancellationToken token)
        {
            int winding = 0; double left = 0; CellCoverage value = CellCoverage.None;
            for (int i = 0; i < crossings.Length;)
            {
                double position = crossings[i].Position; int previous = winding;
                // Coalesce coincident transitions before deciding ownership;
                // opposite contours must also cancel at exact centers.
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
                    if (dropouts)
                    {
                        double first = Math.Ceiling(left - .5), last = Math.Floor(right - .5);
                        if (first == last + 1)
                        {
                            double midpoint = (left + right) * .5;
                            double selected = horizontal ? Math.Floor(midpoint) : Math.Ceiling(midpoint) - 1;
                            if (selected == Math.Floor(sample))
                            {
                                double other = (selected == first ? last : first) + .5;
                                // The neighbor query disables dropouts, bounding
                                // this call to one extra normal-coverage scan.
                                if ((Classify(crossings, other, horizontal, false, work, token) & CellCoverage.Interior) == 0)
                                    value |= CellCoverage.Dropout;
                            }
                        }
                    }
                }
            }
            return value;
        }

        static int Gather(ReadOnlySpan<OpenTypeQuadraticSegment> segments, double sample, bool horizontal, bool upper, Span<Crossing> scratch, ParserWorkCounter work, CancellationToken token, out bool endpoint)
        {
            int count = 0; endpoint = false;
            foreach (OpenTypeQuadraticSegment segment in segments)
            {
                work.Consume(token);
                OpenTypeOutlinePoint a = Map(segment.Start), b = Map(segment.Control), c = Map(segment.End);
                endpoint |= a.Y == sample || c.Y == sample;
                if (segment.Control == segment.End)
                {
                    if (upper ? a.Y < sample && sample <= c.Y || c.Y < sample && sample <= a.Y : a.Y <= sample && sample < c.Y || c.Y <= sample && sample < a.Y)
                        scratch[count++] = new(a.X + (c.X - a.X) * (sample - a.Y) / (c.Y - a.Y), c.Y > a.Y ? 1 : -1);
                    continue;
                }
                double A = a.Y - 2 * b.Y + c.Y, B = 2 * (b.Y - a.Y), C = a.Y - sample;
                if (A == 0) { if (B != 0) Include(-C / B, a, b, c, A, B, upper, scratch, ref count); continue; }
                double discriminant = B * B - 4 * A * C; if (discriminant <= 0) continue;
                double root = Math.Sqrt(discriminant);
                Include((-B + root) / (2 * A), a, b, c, A, B, upper, scratch, ref count);
                Include((-B - root) / (2 * A), a, b, c, A, B, upper, scratch, ref count);
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
            OpenTypeOutlinePoint Map(OpenTypeOutlinePoint p) => horizontal ? new(p.Y * 5, p.X * 6) : new(p.X * 6, p.Y * 5);
            static void Include(double t, OpenTypeOutlinePoint a, OpenTypeOutlinePoint b, OpenTypeOutlinePoint c, double A, double B, bool upper, Span<Crossing> scratch, ref int count)
            {
                if (t is < 0 or > 1) return;
                double derivative = B + 2 * A * t;
                if (derivative == 0 || (upper ? t == 0 && derivative > 0 || t == 1 && derivative < 0 : t == 0 && derivative < 0 || t == 1 && derivative > 0)) return;
                // Horner evaluation leaves a constant coordinate exact. A
                // Bernstein sum can drift across a center on a straight stem.
                double crossing = a.X + t * (2 * (b.X - a.X) + t * (a.X - 2 * b.X + c.X));
                scratch[count++] = new(crossing, derivative > 0 ? 1 : -1);
            }
        }
    }
}
