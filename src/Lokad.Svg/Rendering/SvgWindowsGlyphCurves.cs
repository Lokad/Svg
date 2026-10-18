namespace Lokad.Svg.Rendering;

using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

/// <summary>Prepares Windows quadratic leaves before their bounded implicit scan coefficients are formed.</summary>
/// <remarks>Borrows positioned contours and returns an exclusive reservation-backed owner. Sampling-grid rounding precedes extrema and span subdivision; each integer product rounds independently. Translation is removed before signed coordinate checks. Every visited leaf and emitted segment charges work and observes cancellation. Denial and cancellation release all intermediate storage; independent conversion owners may call concurrently.</remarks>
internal static class SvgWindowsGlyphCurves
{
    internal static OpenTypeGlyphContourStorage Prepare(OpenTypeGlyphContourStorage source, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        using OpenTypeSegmentAccumulator result = new(budget, GlyphDecoderBudgetPhase.ExpandedSegmentCapacity, GlyphDecoderBudgetPhase.ExpandedSegmentExact, GlyphDecoderBudgetPhase.ExpandedContourBoundaryCapacity, GlyphDecoderBudgetPhase.ExpandedContourBoundaryExact, work, token);
        double originX = source.Segments.Length == 0 ? 0 : Math.Floor(source.Segments[0].Start.X);
        double originY = source.Segments.Length == 0 ? 0 : Math.Floor(source.Segments[0].Start.Y);
        int verticalScale = source.WindowsLcdMode == SvgGlyphLcdMode.Natural ? 64 : 320;
        int first = 0;
        foreach (int end in source.ContourEnds)
        {
            for (int index = first; index < end; index++)
            {
                work.Consume(token);
                OpenTypeQuadraticSegment segment = source.Segments[index];
                Point a = Map(segment.Start), b = Map(segment.Control), c = Map(segment.End);
                if (segment.Control == segment.End) Add(a, c, c);
                else Split(a, b, c, 0);
            }
            result.CompleteContour();
            first = end;
        }
        OpenTypeGlyphContourStorage contours = result.Detach();
        contours.SmartDropouts = source.SmartDropouts;
        contours.SimpleDropouts = source.SimpleDropouts;
        contours.ExcludeDropoutStubs = source.ExcludeDropoutStubs;
        contours.WindowsLcdMode = source.WindowsLcdMode;
        contours.ExtendWindowsLcdSpans = source.ExtendWindowsLcdSpans;
        return contours;

        Point Map(OpenTypeOutlinePoint point)
        {
            double x = Math.Floor((point.X - originX) * 384 + .5);
            double y = -Math.Floor((point.Y - originY) * verticalScale + .25);
            if (!double.IsFinite(x) || !double.IsFinite(y) || x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
                throw new SvgException(SvgErrorCode.LimitExceeded, "The glyph scan coordinate exceeds its signed bound.");
            return new((long)x, (long)y);
        }

        void Add(Point a, Point b, Point c)
        {
            work.Consume(token);
            result.Add(new(Device(a), Device(b), Device(c)));
        }

        OpenTypeOutlinePoint Device(Point point) => new(originX + point.X / 384d, originY - point.Y / (double)verticalScale);

        void Split(Point a, Point b, Point c, int depth)
        {
            work.Consume(token);
            if (depth > 32) throw new SvgException(SvgErrorCode.LimitExceeded, "The glyph curve subdivision exceeds its bounded depth.");
            long dx = b.X - a.X, dy = b.Y - a.Y, ex = c.X - b.X, ey = c.Y - b.Y;
            Point left, middle, right;
            // The native scan divides at vertical extrema before horizontal
            // extrema. The extremum coordinate is shared by both controls.
            if (dy > 0 && ey < 0 || dy < 0 && ey > 0)
            {
                long divisor = dy - ey;
                long extreme = a.Y + Divide((Int128)dy * dy, divisor);
                left = new(a.X + Divide((Int128)dx * dy, divisor), extreme);
                right = new(b.X + Divide((Int128)ex * dy, divisor), extreme);
                middle = new(left.X + Divide((Int128)(right.X - left.X) * dy, divisor), extreme);
            }
            else if (dx > 0 && ex < 0 || dx < 0 && ex > 0)
            {
                long divisor = dx - ex;
                long extreme = a.X + Divide((Int128)dx * dx, divisor);
                left = new(extreme, a.Y + Divide((Int128)dy * dx, divisor));
                right = new(extreme, b.Y + Divide((Int128)ey * dx, divisor));
                middle = new(extreme, left.Y + Divide((Int128)(right.Y - left.Y) * dx, divisor));
            }
            else if (Math.Abs(c.X - a.X) > 3200 || Math.Abs(c.Y - a.Y) > 3200)
            {
                left = new((a.X + b.X) >> 1, (a.Y + b.Y) >> 1);
                right = new((b.X + c.X) >> 1, (b.Y + c.Y) >> 1);
                middle = new((left.X + right.X) >> 1, (left.Y + right.Y) >> 1);
            }
            else
            {
                Add(a, b, c);
                return;
            }
            Split(a, left, middle, depth + 1);
            Split(middle, right, c, depth + 1);
        }

        static long Divide(Int128 product, long divisor)
            => (long)((product + (Int128.Sign(product) == Math.Sign(divisor) ? divisor / 2 : -divisor / 2)) / divisor);
    }

    private readonly record struct Point(long X, long Y);
}
