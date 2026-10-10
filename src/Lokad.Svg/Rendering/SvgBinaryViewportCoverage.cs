// Fixed line setup and winding spans adapt Skia's SkEdge.cpp and SkScan_Path.cpp.
// Copyright 2006 The Android Open Source Project. See THIRD_PARTY_NOTICES.txt.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Rasterizes a transformed rectangular viewport with native binary line edges.</summary>
/// <remarks>Borrowed destination and clip coverage remain caller-owned. Four edges use stack storage;
/// bitmap and row traversal charge bounded work and cancellation. No curve or paint semantics are added.</remarks>
internal static class SvgBinaryViewportCoverage
{
    private readonly record struct Edge(long X, long Step, int First, int Last, int Winding);

    internal static long Rasterize(SvgAuthoredPath source, SvgAffineTransform outputMap, SvgCpuRasterGrid grid,
        SvgAnalyticClipBounds bounds, byte[] destination, int width, SvgCpuClipCoverage coverage,
        long maximumWork, CancellationToken token)
    {
        Span<SvgDevicePoint> corners = stackalloc SvgDevicePoint[4];
        int count = 0;
        foreach (SvgAuthoredPathCommand command in source.Commands)
            if (command.Verb is SvgAuthoredPathVerb.Move or SvgAuthoredPathVerb.Line)
            {
                if (count == corners.Length) throw new SvgException(SvgErrorCode.MalformedInput, "A binary viewport requires four corners.");
                corners[count++] = command.First;
            }
        if (count != 4) throw new SvgException(SvgErrorCode.MalformedInput, "A binary viewport requires four corners.");
        SvgAffineTransform map = outputMap.ComposeChecked(source.SceneMap);
        Span<Edge> edges = stackalloc Edge[4];
        Span<Edge> active = stackalloc Edge[4];
        Span<SvgDevicePoint> placed = stackalloc SvgDevicePoint[4];
        long work = 0;
        foreach (SvgCpuRasterContext context in grid.GetContexts(bounds))
        {
            Charge(9);
            SvgAnalyticClipBounds bitmap = context.BitmapClip;
            SvgCpuBitmapOrigin origin = new(bitmap.Left, bitmap.Top);
            for (int index = 0; index < 4; index++) placed[index] = SvgAuthoredPath.MapPoint(map, corners[index], origin);
            int edgeCount = 0;
            for (int index = 0; index < 4; index++)
            {
                SvgDevicePoint first = placed[index], last = placed[(index + 1) & 3];
                long x0 = Dot6(first.X), y0 = Dot6(first.Y), x1 = Dot6(last.X), y1 = Dot6(last.Y);
                int winding = 1;
                if (y0 > y1) { (x0, x1) = (x1, x0); (y0, y1) = (y1, y0); winding = -1; }
                int top = checked((int)((y0 + 32) >> 6)), bottom = checked((int)((y1 + 32) >> 6));
                if (top == bottom) continue;
                long slope = ((x1 - x0) << 16) / (y1 - y0);
                long x = (x0 + ((slope * (32 + ((long)top << 6) - y0)) >> 16)) << 10;
                edges[edgeCount++] = new(x, slope, top, bottom, winding);
            }
            int left = Math.Max(bitmap.Left, bounds.Left), right = Math.Min(bitmap.Right, bounds.Right);
            int topRow = Math.Max(bitmap.Top, bounds.Top), bottomRow = Math.Min(bitmap.Bottom, bounds.Bottom);
            int keptLeft = right, keptTop = bottomRow, keptRight = left, keptBottom = topRow;
            for (int y = topRow; y < bottomRow; y++)
            {
                Charge(5);
                int localY = y - origin.Y, activeCount = 0;
                for (int index = 0; index < edgeCount; index++)
                {
                    Edge edge = edges[index];
                    if (localY < edge.First || localY >= edge.Last) continue;
                    Edge current = edge with { X = edge.X + edge.Step * ((long)localY - edge.First) };
                    int at = activeCount++;
                    while (at > 0 && active[at - 1].X > current.X) { active[at] = active[at - 1]; at--; }
                    active[at] = current;
                }
                int winding = 0, start = left;
                for (int index = 0; index < activeCount; index++)
                {
                    int x = (int)Math.Clamp(((active[index].X + 32768) >> 16) + origin.X, left, right);
                    if (winding == 0) start = x;
                    winding += active[index].Winding;
                    if (winding != 0) continue;
                    Charge(x - start);
                    for (int pixel = start; pixel < x; pixel++)
                    {
                        coverage.SetBorderAlpha(bitmap, pixel, y, 255);
                        if (pixel >= context.Interior.Left && pixel < context.Interior.Right && y >= context.Interior.Top && y < context.Interior.Bottom)
                            destination[(y * width + pixel) * 4 + 3] = 255;
                    }
                    if (x > start) { keptLeft = Math.Min(keptLeft, start); keptTop = Math.Min(keptTop, y); keptRight = Math.Max(keptRight, x); keptBottom = y + 1; }
                }
            }
            coverage.SetBounds(bitmap, keptRight > keptLeft && keptBottom > keptTop
                ? new(keptLeft, keptTop, keptRight, keptBottom) : new(left, topRow, left, topRow));
        }
        return work;

        void Charge(long amount)
        {
            token.ThrowIfCancellationRequested();
            if (amount > maximumWork - work) throw new SvgException(SvgErrorCode.LimitExceeded, "Binary viewport construction exceeds the configured work limit.");
            work += amount;
        }
        static long Dot6(double value)
        {
            if (Math.Abs(value) > int.MaxValue / 64d) throw new SvgException(SvgErrorCode.LimitExceeded, "Binary viewport coordinates exceed the native fixed range.");
            return (long)((float)value * 64f);
        }
    }
}
