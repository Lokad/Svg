// Rectangle-frame coverage adapts SkScan_Antihair.cpp at the pin in THIRD_PARTY_NOTICES.txt.
// Copyright 2011 The Android Open Source Project.
namespace Lokad.Svg.Rendering;

/// <summary>Retains a stroked rectangle's separate outer hull, integer frame and inverse inner fringe.</summary>
/// <remarks>This immutable value allocates nothing and owns no storage. The caller charges preparation,
/// row lookup and up to four ordered paint operations per visited pixel, and checks cancellation per row.
/// GetOperations borrows a span of at least four bytes only for that call.
/// Fixed edges saturate outside any admitted bitmap; clipping applies to each paint operation separately.</remarks>
internal readonly record struct SvgRectangleFrameCoverage(long OuterLeft, long OuterTop, long OuterRight, long OuterBottom, long InnerLeft, long InnerTop, long InnerRight, long InnerBottom)
{
    internal static SvgRectangleFrameCoverage Create(double left, double top, double right, double bottom, double width, double height)
    {
        float l = (float)left, t = (float)top, r = (float)right, b = (float)bottom;
        float sx = (float)width, sy = (float)height;
        float rx = sx / 2, ry = sy / 2;
        if (r == l) ry = 0;
        if (b == t) rx = 0;
        long ol = Snap(l - rx), ot = Snap(t - ry), outerR = Snap(r + rx), ob = Snap(b + ry);
        rx = sx - rx; ry = sy - ry;
        long il = Snap(l + rx), it = Snap(t + ry), ir = Snap(r - rx), ib = Snap(b - ry);
        if (sx < 1 || sy < 1)
        {
            Align(ref ol, ref il); Align(ref ot, ref it);
            Align(ref ir, ref outerR); Align(ref ib, ref ob);
        }
        return new(ol, ot, outerR, ob, il, it, ir, ib);

        static long Snap(float edge)
        {
            if (float.IsNaN(edge)) throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle stroke mapping exceeds the supported numeric range.");
            return (long)Math.Clamp((double)edge * 256, -(1L << 48), 1L << 48);
        }
        static void Align(ref long first, ref long second)
        {
            if ((first >> 8) == (second >> 8)) { second -= first & 255; first &= ~255L; }
        }
    }

    /// <summary>Skips the empty inner span without skipping either inverse fringe.</summary>
    internal (int LeftEnd, int RightStart) GetRowSpans(int y, int left, int right)
        => InnerLeft >= InnerRight || InnerTop >= InnerBottom || y < ((InnerTop + 255) >> 8) || y >= (InnerBottom >> 8)
            ? (right, right)
            : ((int)Math.Clamp((InnerLeft + 255) >> 8, left, right), (int)Math.Clamp(InnerRight >> 8, left, right));

    /// <summary>Writes at most four native paint coverages in their original order.</summary>
    internal int GetOperations(int x, int y, Span<byte> alphas)
    {
        int count = 0;
        SvgRectangleCoverage hull = new(OuterLeft, OuterTop, OuterRight, OuterBottom);
        byte outer = hull.GetAlpha(x, y);
        bool outerInterior = y >= ((OuterTop + 255) >> 8) && y < (OuterBottom >> 8)
            && (OuterLeft >> 8) != ((OuterRight - 1) >> 8) && x >= ((OuterLeft + 255) >> 8) && x < (OuterRight >> 8);
        if (outer != 0 && !outerInterior) alphas[count++] = outer;
        long ol = (OuterLeft + 255) >> 8, ot = (OuterTop + 255) >> 8, outerR = OuterRight >> 8, ob = OuterBottom >> 8;
        bool collapsed = InnerLeft >= InnerRight || InnerTop >= InnerBottom;
        long il = InnerLeft >> 8, it = InnerTop >> 8, ir = (InnerRight + 255) >> 8, ib = (InnerBottom + 255) >> 8;
        bool middle = collapsed ? x >= ol && x < outerR && y >= ot && y < ob
            : x >= ol && x < outerR && (y >= ot && y < it || y >= ib && y < ob)
                || y >= it && y < ib && (x >= ol && x < il || x >= ir && x < outerR);
        if (middle) alphas[count++] = 255;
        if (collapsed) return count;
        long row = (long)y * 256;
        if (y < it || y >= ((InnerBottom + 255) >> 8)) return count;
        bool singleRow = it == ((InnerBottom - 1) >> 8);
        int inverse = singleRow ? (int)(256 - (InnerBottom - InnerTop))
            : row < InnerTop ? (int)(InnerTop & 255) : row + 256 > InnerBottom ? (int)(~InnerBottom & 255) : 0;
        if (singleRow || inverse != 0)
        {
            if (inverse == 0 || x < il || x > (InnerRight >> 8)) return count;
            int alpha;
            if (il == ((InnerRight - 1) >> 8))
            {
                if (x != il) return count;
                long width = InnerRight - InnerLeft;
                alpha = InverseMultiply(inverse, (int)(width - (width >> 8)));
            }
            else if (x == il && (InnerLeft & 255) != 0) alpha = InverseMultiply(inverse, (int)(InnerLeft & 255));
            else if (x == (InnerRight >> 8) && (InnerRight & 255) != 0) alpha = InverseMultiply(inverse, (int)(~InnerRight & 255));
            else if (x >= ((InnerLeft + 255) >> 8) && x < (InnerRight >> 8)) alpha = inverse;
            else return count;
            if (alpha != 0) alphas[count++] = (byte)alpha;
        }
        else if (y >= ((InnerTop + 255) >> 8) && y < (InnerBottom >> 8))
        {
            if (x == il && (InnerLeft & 255) != 0) alphas[count++] = (byte)(InnerLeft & 255);
            if (x == (InnerRight >> 8) && (InnerRight & 255) != 0) alphas[count++] = (byte)(~InnerRight & 255);
        }
        return count;

        static int InverseMultiply(int a, int b) => a + b - (a * b + 127) / 255;
    }
}
