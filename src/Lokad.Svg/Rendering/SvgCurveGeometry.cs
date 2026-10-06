// Adapts SkGeometry.cpp at the revision in THIRD_PARTY_NOTICES.txt.
// Copyright 2006 The Android Open Source Project.
namespace Lokad.Svg.Rendering;

/// <summary>Shared bounded native curve geometry for hairline subdivision and stroke reduction.</summary>
/// <remarks>Uses fixed stack buffers and no heap storage. Callers charge the bounded numerical phase
/// and check cancellation before entry; calls share no mutable state.</remarks>
internal static class SvgCurveGeometry
{
    internal static int CubicCurvatureRoots(ReadOnlySpan<SvgDevicePoint> p, Span<float> roots)
    {
        Span<float> x = stackalloc float[4], y = stackalloc float[4];
        Formulate((float)p[0].X, (float)p[1].X, (float)p[2].X, (float)p[3].X, x);
        Formulate((float)p[0].Y, (float)p[1].Y, (float)p[2].Y, (float)p[3].Y, y);
        for (int index = 0; index < 4; index++) x[index] += y[index];
        int count;
        if (MathF.Abs(x[0]) <= 1f / 4096) count = QuadraticRoots(x[1], x[2], x[3], roots);
        else
        {
            float inverse = 1 / x[0], a = x[1] * inverse, b = x[2] * inverse, c = x[3] * inverse;
            float q = (a * a - b * 3) / 9, r = (2 * a * a * a - 9 * a * b + 27 * c) / 54;
            float q3 = q * q * q, discriminant = r * r - q3, third = a / 3;
            if (discriminant < 0)
            {
                float theta = MathF.Acos(Math.Clamp(r / MathF.Sqrt(q3), -1, 1)), scale = -2 * MathF.Sqrt(q);
                roots[0] = Math.Clamp(scale * MathF.Cos(theta / 3) - third, 0, 1);
                roots[1] = Math.Clamp(scale * MathF.Cos((theta + 2 * MathF.PI) / 3) - third, 0, 1);
                roots[2] = Math.Clamp(scale * MathF.Cos((theta - 2 * MathF.PI) / 3) - third, 0, 1);
                roots.Sort(); count = 3;
            }
            else
            {
                float value = MathF.Pow(MathF.Abs(r) + MathF.Sqrt(discriminant), .3333333f);
                if (r > 0) value = -value;
                if (value != 0) value += q / value;
                roots[0] = Math.Clamp(value - third, 0, 1); count = 1;
            }
        }
        int retained = 0;
        for (int index = 0; index < count; index++)
            if (roots[index] > 0 && roots[index] < 1 && (retained == 0 || roots[index] != roots[retained - 1])) roots[retained++] = roots[index];
        return retained;

        static void Formulate(float p0, float p1, float p2, float p3, Span<float> coefficients)
        {
            float a = p1 - p0, b = p2 - 2 * p1 + p0, c = p3 + 3 * (p1 - p2) - p0;
            coefficients[0] = c * c; coefficients[1] = 3 * b * c;
            coefficients[2] = 2 * b * b + c * a; coefficients[3] = a * b;
        }
        static int QuadraticRoots(float a, float b, float c, Span<float> values)
        {
            if (a == 0) return Divide(-c, b, out values[0]) ? 1 : 0;
            double discriminant = (double)b * b - 4 * (double)a * c;
            if (discriminant < 0) return 0;
            float root = (float)Math.Sqrt(discriminant);
            if (!float.IsFinite(root)) return 0;
            float q = b < 0 ? -(b - root) / 2 : -(b + root) / 2;
            int count = 0;
            if (Divide(q, a, out float first)) values[count++] = first;
            if (Divide(c, q, out float second)) values[count++] = second;
            if (count == 2) { if (values[0] > values[1]) (values[0], values[1]) = (values[1], values[0]); else if (values[0] == values[1]) count = 1; }
            return count;
        }
        static bool Divide(float numerator, float denominator, out float ratio)
        {
            if (numerator < 0) { numerator = -numerator; denominator = -denominator; }
            ratio = 0;
            if (denominator == 0 || numerator == 0 || numerator >= denominator) return false;
            ratio = numerator / denominator;
            return !float.IsNaN(ratio) && ratio != 0;
        }
    }

}
