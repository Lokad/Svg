namespace Lokad.Svg.Rendering;

using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

internal readonly record struct SvgGlyphRgbCoverage(byte Red, byte Green, byte Blue)
{
    internal byte Mean => (byte)((Red + Green + Blue) / 3);
}

/// <summary>Samples positioned quadratic outlines with the measured symmetric LCD filter.</summary>
/// <remarks>Borrows closed device contours and optional exclusive caller-owned crossing scratch;
/// retains no storage. Six horizontal samples and five vertically weighted samples feed each
/// displaced color channel. Enabled smart dropout control preserves strokes between sample
/// centers on both axes, including stubs. Every segment, crossing and sort comparison consumes
/// work and observes cancellation. Callers own positioning, bounds, clipping, preblend and
/// scratch reservations; independent calls with separate scratch may run concurrently.</remarks>
internal static class SvgGlyphLcdCoverage
{
    /// <summary>One directed scan crossing in caller-owned scratch; never retained by coverage.</summary>
    internal readonly record struct Crossing(double Position, int Direction);
    internal static SvgGlyphRgbCoverage GetCoverage(ReadOnlySpan<OpenTypeQuadraticSegment> segments, int pixelX, int pixelY, ParserWorkCounter work, CancellationToken token)
        => GetCoverage(segments, pixelX, pixelY, [], work, token);

    internal static SvgGlyphRgbCoverage GetCoverage(ReadOnlySpan<OpenTypeQuadraticSegment> segments, int pixelX, int pixelY, Span<Crossing> scratch, ParserWorkCounter work, CancellationToken token)
    {
        GlyphCoverageProbe.Started();
        work.Consume(token);
        if (!scratch.IsEmpty && scratch.Length < (long)segments.Length * 2) throw new ArgumentException("Dropout scratch must hold two crossings per segment.", nameof(scratch));
        int red = 0, green = 0, blue = 0;
        for (int row = 0; row < 5; row++)
        {
            int weight = row switch { 0 or 4 => 4, 1 or 3 => 9, _ => 10 };
            double y = pixelY + (row + .5) / 5;
            for (int column = -2; column < 8; column++)
            {
                double x = pixelX + (column + .5) / 6;
                if (!Contains(segments, x, y, work, token) && (scratch.IsEmpty || !Dropout(segments, x, y, false, scratch, work, token) && !Dropout(segments, x, y, true, scratch, work, token))) continue;
                if (column < 4) red += weight;
                if (column is >= 0 and < 6) green += weight;
                if (column >= 2) blue += weight;
            }
        }
        return new((byte)((red * 255 + 108) / 216), (byte)((green * 255 + 108) / 216), (byte)((blue * 255 + 108) / 216));

        static bool Dropout(ReadOnlySpan<OpenTypeQuadraticSegment> segments, double x, double y, bool horizontal, Span<Crossing> scratch, ParserWorkCounter work, CancellationToken token)
        {
            int count = Gather(segments, horizontal ? x : y, horizontal, scratch, work, token);
            Span<Crossing> crossings = scratch[..count];
            // An explicit bounded sort preserves categorized budget/cancellation
            // exceptions; framework comparison sorts wrap comparer exceptions.
            for (int i = 1; i < crossings.Length; i++)
            {
                work.Consume(token);
                Crossing item = crossings[i]; int j = i;
                while (j > 0)
                {
                    work.Consume(token);
                    if (crossings[j - 1].Position <= item.Position) break;
                    crossings[j] = crossings[j - 1]; j--;
                }
                crossings[j] = item;
            }
            int winding = 0; double left = 0; double scale = horizontal ? 5 : 6, sample = horizontal ? y : x;
            foreach (Crossing crossing in crossings)
            {
                work.Consume(token);
                int previous = winding; winding += crossing.Direction;
                if (previous == 0 && winding != 0) left = crossing.Position;
                else if (previous != 0 && winding == 0)
                {
                    double right = crossing.Position;
                    if (!(right > left)) continue;
                    double first = Math.Ceiling(left * scale - .5), last = Math.Floor(right * scale - .5);
                    if (first != last + 1) continue;
                    double midpoint = (left + right) * .5 * scale;
                    double selected = horizontal ? Math.Floor(midpoint) : Math.Ceiling(midpoint) - 1;
                    if (selected != Math.Floor(sample * scale)) continue;
                    double other = (selected == first ? last : first) + .5;
                    other /= scale;
                    if (!Contains(segments, horizontal ? x : other, horizontal ? other : y, work, token)) return true;
                }
            }
            return false;

            static int Gather(ReadOnlySpan<OpenTypeQuadraticSegment> segments, double sample, bool horizontal, Span<Crossing> scratch, ParserWorkCounter work, CancellationToken token)
            {
                int count = 0;
                foreach (OpenTypeQuadraticSegment original in segments)
                {
                    work.Consume(token);
                    OpenTypeOutlinePoint start = Map(original.Start), control = Map(original.Control), end = Map(original.End);
                    if (original.Control == original.End)
                    {
                        if (start.Y <= sample && sample < end.Y || end.Y <= sample && sample < start.Y)
                            scratch[count++] = new(start.X + (end.X - start.X) * (sample - start.Y) / (end.Y - start.Y), end.Y > start.Y ? 1 : -1);
                        continue;
                    }
                    double a = start.Y - 2 * control.Y + end.Y, b = 2 * (control.Y - start.Y), c = start.Y - sample;
                    if (a == 0) { if (b != 0) Include(-c / b, start, control, end, a, b, scratch, ref count); continue; }
                    double discriminant = b * b - 4 * a * c;
                    if (discriminant <= 0) continue;
                    double root = Math.Sqrt(discriminant);
                    Include((-b + root) / (2 * a), start, control, end, a, b, scratch, ref count);
                    Include((-b - root) / (2 * a), start, control, end, a, b, scratch, ref count);
                }
                return count;
                OpenTypeOutlinePoint Map(OpenTypeOutlinePoint point) => horizontal ? new(point.Y, point.X) : point;

                static void Include(double t, OpenTypeOutlinePoint start, OpenTypeOutlinePoint control, OpenTypeOutlinePoint end, double a, double b, Span<Crossing> scratch, ref int count)
                {
                    if (t is < 0 or > 1) return;
                    double derivative = b + 2 * a * t;
                    if (derivative == 0 || t == 0 && derivative < 0 || t == 1 && derivative > 0) return;
                    double inverse = 1 - t;
                    double crossing = inverse * inverse * start.X + 2 * inverse * t * control.X + t * t * end.X;
                    scratch[count++] = new(crossing, derivative > 0 ? 1 : -1);
                }
            }
        }
    }

    private static bool Contains(ReadOnlySpan<OpenTypeQuadraticSegment> segments, double x, double y, ParserWorkCounter work, CancellationToken token)
    {
        int winding = 0;
        foreach (OpenTypeQuadraticSegment segment in segments)
        {
            work.Consume(token);
            OpenTypeOutlinePoint start = segment.Start, control = segment.Control, end = segment.End;
            if (control == end)
            {
                if (start.Y <= y && y < end.Y || end.Y <= y && y < start.Y)
                {
                    double crossing = start.X + (end.X - start.X) * (y - start.Y) / (end.Y - start.Y);
                    if (crossing <= x) winding += end.Y > start.Y ? 1 : -1;
                }
                continue;
            }
            double a = start.Y - 2 * control.Y + end.Y, b = 2 * (control.Y - start.Y), c = start.Y - y;
            if (a == 0) { if (b != 0) IncludeRoot(-c / b); continue; }
            double discriminant = b * b - 4 * a * c;
            if (discriminant <= 0) continue;
            double root = Math.Sqrt(discriminant);
            IncludeRoot((-b + root) / (2 * a)); IncludeRoot((-b - root) / (2 * a));
            void IncludeRoot(double t)
            {
                if (t is < 0 or > 1) return;
                double derivative = b + 2 * a * t;
                if (t == 0 && derivative < 0 || t == 1 && derivative > 0) return;
                double inverse = 1 - t;
                double crossing = inverse * inverse * start.X + 2 * inverse * t * control.X + t * t * end.X;
                if (crossing <= x) winding += derivative > 0 ? 1 : -1;
            }
        }
        return winding != 0;
    }
}
