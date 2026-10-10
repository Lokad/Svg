namespace Lokad.Svg.Rendering;

using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

internal readonly record struct SvgGlyphRgbCoverage(byte Red, byte Green, byte Blue)
{
    internal byte Mean => (byte)((Red + Green + Blue) / 3);
}

/// <summary>Samples positioned quadratic outlines with the measured symmetric LCD filter.</summary>
/// <remarks>Borrows closed device-coordinate contours and retains no storage. Six horizontal
/// samples and five vertically weighted samples feed each displaced color channel. Exact
/// quadratic crossings preserve counters and winding without fixed curve subdivision.
/// Every segment visit consumes work and observes cancellation. Callers own positioning,
/// bounds, preblend, clipping and paint alpha; independent calls may run concurrently.</remarks>
internal static class SvgGlyphLcdCoverage
{
    internal static SvgGlyphRgbCoverage GetCoverage(ReadOnlySpan<OpenTypeQuadraticSegment> segments, int pixelX, int pixelY, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        int red = 0, green = 0, blue = 0;
        for (int row = 0; row < 5; row++)
        {
            int weight = row switch { 0 or 4 => 4, 1 or 3 => 9, _ => 10 };
            double y = pixelY + (row + .5) / 5;
            // Ten shared positions cover all three displaced six-sample windows.
            for (int column = -2; column < 8; column++)
            {
                double x = pixelX + (column + .5) / 6;
                if (!Contains(segments, x, y, work, token)) continue;
                if (column < 4) red += weight;
                if (column is >= 0 and < 6) green += weight;
                if (column >= 2) blue += weight;
            }
        }
        return new((byte)((red * 255 + 108) / 216), (byte)((green * 255 + 108) / 216), (byte)((blue * 255 + 108) / 216));
        static bool Contains(ReadOnlySpan<OpenTypeQuadraticSegment> segments, double x, double y, ParserWorkCounter work, CancellationToken token)
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
                if (a == 0)
                {
                    if (b != 0) Include(-c / b);
                    continue;
                }
                double discriminant = b * b - 4 * a * c;
                if (discriminant <= 0) continue; // A tangent contributes no winding.
                double root = Math.Sqrt(discriminant);
                Include((-b + root) / (2 * a)); Include((-b - root) / (2 * a));

                void Include(double t)
                {
                    if (t is < 0 or > 1) return;
                    double derivative = b + 2 * a * t;
                    // Own the lower endpoint, matching directed line crossings.
                    // A descending curve owns its end rather than its start.
                    if (t == 0 && derivative < 0 || t == 1 && derivative > 0) return;
                    double inverse = 1 - t;
                    double crossing = inverse * inverse * start.X + 2 * inverse * t * control.X + t * t * end.X;
                    if (crossing <= x) winding += derivative > 0 ? 1 : -1;
                }
            }
            return winding != 0;
        }
    }
}
