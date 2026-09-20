namespace Lokad.Svg.Rendering;

using Lokad.Svg.Fonts;

/// <summary>Samples closed quadratic glyph contours with the non-zero winding fill rule.</summary>
internal static class SvgGlyphCoverage
{
    internal static byte GetCoverage(ReadOnlySpan<SvgGlyphQuadraticContour> contours, int pixelX, int pixelY, int samplesPerAxis, CancellationToken cancellationToken)
        => GetCoverage(contours, pixelX, pixelY, 1, 1, samplesPerAxis, cancellationToken);

    internal static byte GetCoverage(ReadOnlySpan<SvgGlyphQuadraticContour> contours, double left, double top, double width, double height, int samplesPerAxis, CancellationToken cancellationToken)
    {
        if (samplesPerAxis is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(samplesPerAxis));
        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        int covered = 0;
        int total = checked(samplesPerAxis * samplesPerAxis);
        for (int y = 0; y < samplesPerAxis; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double sampleY = top + height * (y + 0.5) / samplesPerAxis;
            for (int x = 0; x < samplesPerAxis; x++)
            {
                if (ContainsNonZero(contours, left + width * (x + 0.5) / samplesPerAxis, sampleY)) covered++;
            }
        }
        return (byte)((covered * 255 + total / 2) / total);
    }

    private static bool ContainsNonZero(ReadOnlySpan<SvgGlyphQuadraticContour> contours, double x, double y)
    {
        int winding = 0;
        foreach (SvgGlyphQuadraticContour contour in contours)
        {
            foreach (SvgGlyphQuadraticSegment segment in contour.Segments)
            {
                SvgGlyphOutlinePoint previous = segment.Start;
                for (int step = 1; step <= 8; step++)
                {
                    double t = step / 8d;
                    double inverse = 1 - t;
                    var current = new SvgGlyphOutlinePoint(inverse * inverse * segment.Start.X + 2 * inverse * t * segment.Control.X + t * t * segment.End.X, inverse * inverse * segment.Start.Y + 2 * inverse * t * segment.Control.Y + t * t * segment.End.Y);
                    if (previous.Y <= y)
                    {
                        if (current.Y > y && IsLeft(previous, current, x, y) > 0) winding++;
                    }
                    else if (current.Y <= y && IsLeft(previous, current, x, y) < 0) winding--;
                    previous = current;
                }
            }
        }
        return winding != 0;
    }

    private static double IsLeft(SvgGlyphOutlinePoint start, SvgGlyphOutlinePoint end, double x, double y) => (end.X - start.X) * (y - start.Y) - (x - start.X) * (end.Y - start.Y);
}
