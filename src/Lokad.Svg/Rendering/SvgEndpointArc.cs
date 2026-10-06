// Endpoint conic construction adapts SkPathBuilder.cpp at the revision in THIRD_PARTY_NOTICES.txt.
// Copyright 2015 Google Inc.
namespace Lokad.Svg.Rendering;

internal readonly record struct SvgEndpointConic(SvgDevicePoint Control, SvgDevicePoint End, float Weight);

/// <summary>Constructs at most three rational quadratics for an authored endpoint arc.</summary>
/// <remarks>The caller owns the fixed buffer and charges the bounded normalization/verb phase before
/// entry. Conversion allocates no storage, checks cancellation at each conic and preserves the final
/// float endpoint. A zero count means a line. Nonfinite construction fails with LimitExceeded; calls
/// are independent. Scene admission and stroke flattening retain their existing normalization.</remarks>
internal static class SvgEndpointArc
{
    internal const int MaximumConicCount = 3;

    internal static int Convert(SvgDevicePoint start, double radiusX, double radiusY, double rotation,
        bool large, bool sweep, SvgDevicePoint end, Span<SvgEndpointConic> output, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (output.Length < MaximumConicCount) throw new ArgumentException("Arc buffer is too small.", nameof(output));
        float sx = Narrow(start.X), sy = Narrow(start.Y), ex = Narrow(end.X), ey = Narrow(end.Y);
        float rx = MathF.Abs(Narrow(radiusX)), ry = MathF.Abs(Narrow(radiusY)), angle = Narrow(rotation);
        if (rx == 0 || ry == 0 || (sx == ex && sy == ey)) return 0;
        float radians = -angle * (MathF.PI / 180);
        float sin = Snap(MathF.Sin(radians)), cos = Snap(MathF.Cos(radians));
        float dx = (sx - ex) * 0.5f, dy = (sy - ey) * 0.5f;
        float mx = dx * cos + dy * -sin, my = dx * sin + dy * cos;
        float scale = (mx * mx) / (rx * rx) + (my * my) / (ry * ry);
        if (scale > 1) { scale = MathF.Sqrt(scale); rx *= scale; ry *= scale; }
        float a = cos * (1 / rx), c = -sin * (1 / rx), b = sin * (1 / ry), d = cos * (1 / ry);
        float ux = sx * a + sy * c, uy = sx * b + sy * d;
        float vx = ex * a + ey * c, vy = ex * b + ey * d;
        dx = vx - ux; dy = vy - uy;
        float factor = MathF.Sqrt(MathF.Max(1 / (dx * dx + dy * dy) - 0.25f, 0));
        if (sweep == large) factor = -factor;
        dx *= factor; dy *= factor;
        float cx = (ux + vx) * 0.5f - dy, cy = (uy + vy) * 0.5f + dx;
        float theta1 = MathF.Atan2(uy - cy, ux - cx), theta2 = MathF.Atan2(vy - cy, vx - cx);
        float arc = theta2 - theta1;
        if (arc < 0 && sweep) arc += MathF.PI * 2;
        else if (arc > 0 && !sweep) arc -= MathF.PI * 2;
        if (MathF.Abs(arc) < MathF.PI / 1_000_000) return 0;
        if (!float.IsFinite(arc)) throw new SvgException(SvgErrorCode.LimitExceeded, "Endpoint arc exceeds the finite construction range.");
        radians = angle * (MathF.PI / 180);
        sin = Snap(MathF.Sin(radians)); cos = Snap(MathF.Cos(radians));
        a = cos * rx; c = -sin * ry; b = sin * rx; d = cos * ry;
        int count = (int)MathF.Ceiling(MathF.Abs(arc / (2 * MathF.PI / 3)));
        if (count < 1 || count > MaximumConicCount) throw new SvgException(SvgErrorCode.LimitExceeded, "Endpoint arc exceeds the conic bound.");
        float width = arc / count, tangent = MathF.Tan(0.5f * width);
        float weight = MathF.Sqrt(0.5f + MathF.Cos(width) * 0.5f);
        if (!float.IsFinite(tangent) || !float.IsFinite(weight) || weight <= 0) throw new SvgException(SvgErrorCode.LimitExceeded, "Endpoint arc has nonfinite conic controls.");
        bool integers = MathF.Abs(MathF.PI / 2 - MathF.Abs(width)) <= 1f / 4096
            && rx == MathF.Floor(rx) && ry == MathF.Floor(ry) && ex == MathF.Floor(ex) && ey == MathF.Floor(ey);
        for (int index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            float next = theta1 + width;
            float sn = Snap(MathF.Sin(next)), cs = Snap(MathF.Cos(next));
            float x = cs + cx, y = sn + cy;
            float qx = x + tangent * sn, qy = y - tangent * cs;
            float controlX = qx * a + qy * c, controlY = qx * b + qy * d;
            float endX = x * a + y * c, endY = x * b + y * d;
            if (integers)
            {
                controlX = MathF.Floor(controlX + 0.5f); controlY = MathF.Floor(controlY + 0.5f);
                endX = MathF.Floor(endX + 0.5f); endY = MathF.Floor(endY + 0.5f);
            }
            SvgDevicePoint control = new(Narrow(controlX), Narrow(controlY));
            SvgDevicePoint target = index == count - 1 ? new(ex, ey) : new(Narrow(endX), Narrow(endY));
            output[index] = new(control, target, weight);
            theta1 = next;
        }
        return count;

        static float Narrow(double value)
        {
            float result = (float)value;
            if (!float.IsFinite(result)) throw new SvgException(SvgErrorCode.LimitExceeded, "Endpoint arc exceeds the finite float range.");
            return result;
        }
        static float Snap(float value) => MathF.Abs(value) <= 1f / 65536 ? 0 : value;
    }
}
