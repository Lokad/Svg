// Adapts SkScan_Hairline.cpp, SkGeometry.cpp and SkPoint.cpp at the pinned revision.
// Copyright 2006, 2008 The Android Open Source Project.
using System.Numerics;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Subdivides mapped hairline curves and extends endpoint caps with native float operations.</summary>
/// <remarks>All buffers are bounded stack storage. Cubics have at most four pieces and 512 lines per
/// piece; quadratics have at most 32 lines and conics at most 32 quadratics. Each polynomial is checked
/// for finite interior points before painting any of its lines. Every evaluation, subdivision and paint
/// is cancellable. Calls share no mutable state.</remarks>
internal static class SvgHairlineCurves
{
    internal static void DrawQuadratic(ref SvgHairlineCoverage raster, scoped ReadOnlySpan<SvgDevicePoint> points,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        float x0 = (float)points[0].X, y0 = (float)points[0].Y;
        float x1 = (float)points[1].X, y1 = (float)points[1].Y;
        float x2 = (float)points[2].X, y2 = (float)points[2].Y;
        uint dx = Distance(MathF.Abs((x0 + x2) / 2 - x1)), dy = Distance(MathF.Abs((y0 + y2) / 2 - y1));
        uint distance = Math.Max(dx, dy) + (Math.Min(dx, dy) >> 1);
        int level = Math.Min(5, (33 - BitOperations.LeadingZeroCount(distance)) >> 1);
        Polynomial polynomial = new(false, 0, 0, x2 - 2 * x1 + x0, y2 - 2 * y1 + y0,
            2 * (x1 - x0), 2 * (y1 - y0), x0, y0);
        DrawPolynomial(ref raster, points[0], points[2], 1 << level, polynomial, token);

        static uint Distance(float value) => value >= int.MaxValue || float.IsNaN(value) ? int.MaxValue : (uint)MathF.Ceiling(value);
    }

    internal static void DrawConic(ref SvgHairlineCoverage raster, scoped ReadOnlySpan<SvgDevicePoint> points, float weight,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Span<SvgDevicePoint> quads = stackalloc SvgDevicePoint[SvgRationalQuadratic.MaximumPointCount];
        int count = SvgRationalQuadratic.Convert(points[0], points[1], points[2], weight,
            SvgRationalQuadratic.DeviceTolerance, quads, token);
        for (int index = 0; index < count; index++) DrawQuadratic(ref raster, quads.Slice(2 * index, 3), token);
    }

    internal static void DrawCubic(ref SvgHairlineCoverage raster, scoped ReadOnlySpan<SvgDevicePoint> points,
        CancellationToken token)
    {
        // Reserve the constant root-solving and chopping phase before computing it.
        token.ThrowIfCancellationRequested();
        if (Nice(points)) { DrawPiece(ref raster, points, token); return; }
        Span<float> roots = stackalloc float[3];
        int count = SvgCurveGeometry.CubicCurvatureRoots(points, roots);
        Span<SvgDevicePoint> pieces = stackalloc SvgDevicePoint[13];
        Chop(points, roots[..count], pieces);
        for (int index = 0; index <= count; index++) DrawPiece(ref raster, pieces.Slice(index * 3, 4), token);

        static bool Nice(ReadOnlySpan<SvgDevicePoint> p) => Dot(p[1], p[0], p[3]) >= 0 && Dot(p[2], p[0], p[3]) >= 0
            && Dot(p[1], p[3], p[0]) >= 0 && Dot(p[2], p[3], p[0]) >= 0;
        static float Dot(SvgDevicePoint a, SvgDevicePoint pivot, SvgDevicePoint b) =>
            ((float)a.X - (float)pivot.X) * ((float)b.X - (float)pivot.X)
            + ((float)a.Y - (float)pivot.Y) * ((float)b.Y - (float)pivot.Y);
        static void DrawPiece(ref SvgHairlineCoverage painter, scoped ReadOnlySpan<SvgDevicePoint> p, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            const float third = 1f / 3, twoThirds = 2f / 3;
            float x0 = (float)p[0].X, y0 = (float)p[0].Y, x1 = (float)p[1].X, y1 = (float)p[1].Y;
            float x2 = (float)p[2].X, y2 = (float)p[2].Y, x3 = (float)p[3].X, y3 = (float)p[3].Y;
            float difference = MathF.Max(MathF.Max(MathF.Abs(x1 - (third * x3 + twoThirds * x0)), MathF.Abs(y1 - (third * y3 + twoThirds * y0))),
                MathF.Max(MathF.Abs(x2 - (third * x0 + twoThirds * x3)), MathF.Abs(y2 - (third * y0 + twoThirds * y3))));
            int level = 0;
            for (float tolerance = .125f; level < 9 && !(difference < tolerance); level++) tolerance *= 4;
            Polynomial polynomial = new(true, x3 + 3 * (x1 - x2) - x0, y3 + 3 * (y1 - y2) - y0,
                3 * (x2 - 2 * x1 + x0), 3 * (y2 - 2 * y1 + y0), 3 * (x1 - x0), 3 * (y1 - y0), x0, y0);
            DrawPolynomial(ref painter, p[0], p[3], 1 << level, polynomial, cancellation);
        }

        static void Chop(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<float> roots, Span<SvgDevicePoint> output)
        {
            points.CopyTo(output);
            int offset = 0, index = 0;
            for (; index < roots.Length - 1; index += 2)
            {
                float t0 = roots[index], t1 = roots[index + 1];
                if (index != 0) { float last = roots[index - 1]; t0 = Math.Clamp((t0 - last) / (1 - last), 0, 1); t1 = Math.Clamp((t1 - last) / (1 - last), 0, 1); }
                Span<SvgDevicePoint> p = output.Slice(offset, 10);
                SvgDevicePoint p0 = p[0], p1 = p[1], p2 = p[2], p3 = p[3];
                if (t1 == 1) { Single(p[..7], t0); p[7] = p[8] = p[9] = p3; }
                else
                {
                    SvgDevicePoint ab0 = Mix(p0, p1, t0), bc0 = Mix(p1, p2, t0), cd0 = Mix(p2, p3, t0);
                    SvgDevicePoint ab1 = Mix(p0, p1, t1), bc1 = Mix(p1, p2, t1), cd1 = Mix(p2, p3, t1);
                    SvgDevicePoint abc0 = Mix(ab0, bc0, t0), bcd0 = Mix(bc0, cd0, t0);
                    SvgDevicePoint abc1 = Mix(ab1, bc1, t1), bcd1 = Mix(bc1, cd1, t1);
                    p[0] = p0; p[1] = ab0; p[2] = abc0; p[3] = Mix(abc0, bcd0, t0);
                    p[4] = Mix(abc0, bcd0, t1); p[5] = Mix(abc1, bcd1, t0); p[6] = Mix(abc1, bcd1, t1);
                    p[7] = bcd1; p[8] = cd1; p[9] = p3;
                }
                offset += 6;
            }
            if (index < roots.Length)
            {
                float t = roots[index];
                if (index != 0) { float last = roots[index - 1]; t = Math.Clamp((t - last) / (1 - last), 0, 1); }
                Single(output.Slice(offset, 7), t);
            }

            static SvgDevicePoint Mix(SvgDevicePoint a, SvgDevicePoint b, float t) => new(
                (float)a.X + ((float)b.X - (float)a.X) * t, (float)a.Y + ((float)b.Y - (float)a.Y) * t);
            static void Single(Span<SvgDevicePoint> p, float t)
            {
                SvgDevicePoint p0 = p[0], p1 = p[1], p2 = p[2], p3 = p[3];
                if (t == 1) { p[4] = p[5] = p[6] = p3; return; }
                SvgDevicePoint ab = Mix(p0, p1, t), bc = Mix(p1, p2, t), cd = Mix(p2, p3, t);
                SvgDevicePoint abc = Mix(ab, bc, t), bcd = Mix(bc, cd, t);
                p[0] = p0; p[1] = ab; p[2] = abc; p[3] = Mix(abc, bcd, t); p[4] = bcd; p[5] = cd; p[6] = p3;
            }
        }
    }

    internal static void ExtendCaps(Span<SvgDevicePoint> points, SvgStrokeCap cap, bool first, bool last,
        CancellationToken token)
    {
        if (cap == SvgStrokeCap.Butt) return;
        token.ThrowIfCancellationRequested();
        float outset = cap == SvgStrokeCap.Square ? .5f : MathF.PI / 8;
        if (first) Extend(points, 0, 1, 1);
        if (last) Extend(points, points.Length - 1, -1, -1);

        void Extend(Span<SvgDevicePoint> p, int endpoint, int step, float fallback)
        {
            int control = endpoint, controls = p.Length - 1;
            float dx, dy;
            do
            {
                token.ThrowIfCancellationRequested();
                control += step;
                dx = (float)p[endpoint].X - (float)p[control].X;
                dy = (float)p[endpoint].Y - (float)p[control].Y;
            } while (dx == 0 && dy == 0 && --controls > 0);
            if (dx == 0 && dy == 0) { dx = fallback; controls = p.Length - 1; }
            else
            {
                double scale = 1 / Math.Sqrt((double)dx * dx + (double)dy * dy);
                dx = (float)(dx * scale); dy = (float)(dy * scale);
                if (!float.IsFinite(dx) || !float.IsFinite(dy) || (dx == 0 && dy == 0)) { dx = 0; dy = 0; }
            }
            do
            {
                token.ThrowIfCancellationRequested();
                p[endpoint] = new((float)p[endpoint].X + dx * outset, (float)p[endpoint].Y + dy * outset);
                endpoint += step;
            } while (++controls < p.Length);
        }
    }

    private readonly record struct Polynomial(bool Cubic, float AX, float AY, float BX, float BY, float CX, float CY, float DX, float DY)
    {
        internal SvgDevicePoint Evaluate(float t) => Cubic
            ? new(((AX * t + BX) * t + CX) * t + DX, ((AY * t + BY) * t + CY) * t + DY)
            : new((BX * t + CX) * t + DX, (BY * t + CY) * t + DY);
    }

    private static void DrawPolynomial(ref SvgHairlineCoverage raster, SvgDevicePoint start, SvgDevicePoint end,
        int lines, Polynomial polynomial, CancellationToken token)
    {
        // Validate first, then replay: native drops the whole polynomial if any interior point is nonfinite.
        // Two bounded passes avoid the native cubic's 513-point stack buffer.
        float dt = 1f / lines, t = 0;
        for (int index = 1; index < lines; index++)
        {
            token.ThrowIfCancellationRequested();
            t += dt;
            SvgDevicePoint point = polynomial.Evaluate(t);
            if (!float.IsFinite((float)point.X) || !float.IsFinite((float)point.Y)) return;
        }
        t = 0;
        for (int index = 1; index <= lines; index++)
        {
            token.ThrowIfCancellationRequested();
            t += dt;
            SvgDevicePoint point = index == lines ? end : polynomial.Evaluate(t);
            raster.DrawLine(start, point);
            start = point;
        }
    }
}
