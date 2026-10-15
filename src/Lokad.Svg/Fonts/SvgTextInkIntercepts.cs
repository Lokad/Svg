// Adapts Skia's quadratic glyph-path stripe intercepts.
// Copyright 2018 Google LLC. See THIRD_PARTY_NOTICES.txt.
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Fonts;

internal readonly record struct SvgTextInkGap(double Left, double Right) : IComparable<SvgTextInkGap>
{
    public int CompareTo(SvgTextInkGap other) => Left.CompareTo(other.Left);
}

/// <summary>Finds bounded unhinted glyph intercepts for a decoration stripe; callers own the aggregate gap list and reuse existing contour owners.</summary>
internal static class SvgTextInkIntercepts
{
    internal static void Collect(SvgTextRun run, SvgTextPaintFragment fragment, SvgPositionedGlyph first,
        double stripeTop, double stripeBottom, double dilation, double glyphStretch, BudgetedParserList<SvgTextInkGap> gaps,
        RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        for (int index = fragment.Start; index < fragment.End; index++)
        {
            work.Consume(token);
            SvgGlyphFontStyle font = run.GetGlyphFont(index);
            SvgPositionedGlyph glyph = run.Glyphs[index];
            // Native path intercepts use an unhinted canonical 64px strike,
            // then scale the resulting interval back to the requested font size.
            float pathScale = (float)font.FontSize / 64;
            using SvgGlyphContourStorage contours = SvgGlyphOutline.GetContourStorage(font.Font, glyph with { X = 0, Y = 0 }, 64, budget, work, token);
            double originX = first.X + glyph.X - run.Glyphs[fragment.Start].X;
            double originY = first.Y + glyph.Y - run.Glyphs[fragment.Start].Y;
            double left = double.PositiveInfinity, right = double.NegativeInfinity;
            foreach (SvgGlyphQuadraticContour contour in contours.Contours)
            foreach (SvgGlyphQuadraticSegment segment in contour.Segments)
            {
                work.Consume(token);
                SvgGlyphOutlinePoint p = new((float)segment.Start.X, (float)segment.Start.Y),
                    q = new((float)segment.Control.X, (float)segment.Control.Y), r = new((float)segment.End.X, (float)segment.End.Y);
                double top = ((float)stripeTop - (float)originY) / pathScale, bottom = ((float)stripeBottom - (float)originY) / pathScale;
                if (Math.Min(p.Y, Math.Min(q.Y, r.Y)) > bottom || Math.Max(p.Y, Math.Max(q.Y, r.Y)) < top) continue;
                Intersect(p, q, r, top, ref left, ref right);
                Intersect(p, q, r, bottom, ref left, ref right);
                if (top < p.Y && p.Y < bottom) Include(p.X, ref left, ref right);
                if (top < q.Y && q.Y < bottom) Include(q.X, ref left, ref right);
                if (top < r.Y && r.Y < bottom) Include(r.X, ref left, ref right);
            }
            if (left < right)
            {
                float begin = (float)left * pathScale, end = (float)right * pathScale;
                float start = (float)originX + begin * (float)glyphStretch;
                float width = (end - begin) * (float)glyphStretch;
                float x = start - (float)dilation;
                float extent = width + 2 * (float)dilation;
                gaps.Add(new(x, x + extent));
            }
        }
        gaps.Sort(work, token);

        static void Include(double x, ref double left, ref double right) { left = Math.Min(left, x); right = Math.Max(right, x); }

        static void Intersect(SvgGlyphOutlinePoint p, SvgGlyphOutlinePoint q, SvgGlyphOutlinePoint r, double y, ref double left, ref double right)
        {
            if (q == r)
            {
                double t = (y - p.Y) / (r.Y - p.Y);
                if (t >= 0 && t < 1) Include(p.X + t * (r.X - p.X), ref left, ref right);
                return;
            }
            double a = p.Y - 2 * q.Y + r.Y, b = 2 * (q.Y - p.Y), c = p.Y - y;
            if (a == 0)
            {
                Point(-c / b, p, q, r, ref left, ref right);
                return;
            }
            double discriminant = b * b - 4 * a * c;
            if (discriminant < 0) return;
            double root = Math.Sqrt(discriminant);
            Point((-b - root) / (2 * a), p, q, r, ref left, ref right);
            Point((-b + root) / (2 * a), p, q, r, ref left, ref right);
        }

        static void Point(double t, SvgGlyphOutlinePoint p, SvgGlyphOutlinePoint q, SvgGlyphOutlinePoint r, ref double left, ref double right)
        {
            if (!(t >= 0 && t <= 1)) return;
            double u = 1 - t;
            Include(u * u * p.X + 2 * u * t * q.X + t * t * r.X, ref left, ref right);
        }
    }
}
