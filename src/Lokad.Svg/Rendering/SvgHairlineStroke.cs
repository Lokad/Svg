// Adapts SkDraw.cpp thin-stroke dispatch and SkScan_Hairline.cpp cap extension at the pinned revision.
// Copyright 2006, 2011 The Android Open Source Project; 2011 Google Inc.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Dispatches retained centerlines to native thin-stroke curve and line coverage.</summary>
/// <remarks>Allocates no storage and borrows immutable commands, the caller's surface and optional canvas-sized RGBA clip tape. The caller keeps the tape alive throughout replay; each pixel visit includes its rounded clip-alpha product. Every command,
/// closure lookahead and coverage operation is charged and cancellable. Wider strokes
/// keep their admitted outline route. Numerical curve scratch is bounded stack storage.</remarks>
internal static class SvgHairlineStroke
{
    internal static bool TryRasterize(SvgAuthoredPath path, SvgAffineTransform outputMap, SvgColor color,
        (double Left, double Top, double Right, double Bottom)? clip, SvgClipAntialiasing antialiasing,
        ReadOnlySpan<byte> viewportMask, Span<byte> destination, int width, int height, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (path.StrokeStyle is not SvgAuthoredStrokeStyle style) return false;
        workCounter.Consume(cancellationToken);
        SvgAffineTransform map = outputMap.ComposeChecked(path.SceneMap);
        SvgAffineTransform widthMap = outputMap.ComposeChecked(style.WidthMap);
        float strokeWidth = (float)style.Width;
        float lengthX = FastLength((float)widthMap.A * strokeWidth, (float)widthMap.B * strokeWidth);
        float lengthY = FastLength((float)widthMap.C * strokeWidth, (float)widthMap.D * strokeWidth);
        if (!(lengthX <= 1 && lengthY <= 1)) return false;
        ReadOnlySpan<SvgAuthoredPathCommand> commands = path.Commands;
        float coverage = (lengthX + lengthY) * .5f;
        byte alpha = coverage == 1 ? color.Alpha : (byte)((color.Alpha * (int)(coverage * 256)) >> 8);
        SvgHairlineCoverage raster = new(destination, width, height, SvgAnalyticPaint.Solid(color with { Alpha = alpha }), clip, antialiasing, viewportMask, workCounter, cancellationToken);
        SvgDevicePoint current = default, contourStart = default, first = default, last = default;
        Span<SvgDevicePoint> points = stackalloc SvgDevicePoint[4];
        Span<SvgEndpointConic> conics = stackalloc SvgEndpointConic[SvgEndpointArc.MaximumConicCount];
        bool firstSegment = false, closed = false;
        for (int index = 0; index < commands.Length; index++)
        {
            workCounter.Consume(cancellationToken);
            SvgAuthoredPathCommand command = commands[index];
            SvgAuthoredPathVerb next = index + 1 == commands.Length ? SvgAuthoredPathVerb.End : commands[index + 1].Verb;
            bool finalSegment = next is SvgAuthoredPathVerb.End or SvgAuthoredPathVerb.Close or SvgAuthoredPathVerb.Move;
            switch (command.Verb)
            {
                case SvgAuthoredPathVerb.Move:
                    current = contourStart = command.First;
                    first = last = MapPoint(map, current);
                    firstSegment = true;
                    closed = false;
                    for (int scan = index + 1; scan < commands.Length; scan++)
                    {
                        workCounter.Consume(cancellationToken);
                        if (commands[scan].Verb == SvgAuthoredPathVerb.Close) { closed = true; break; }
                        if (commands[scan].Verb is SvgAuthoredPathVerb.End or SvgAuthoredPathVerb.Move) break;
                    }
                    break;
                case SvgAuthoredPathVerb.Line:
                case SvgAuthoredPathVerb.Quadratic:
                case SvgAuthoredPathVerb.Conic:
                case SvgAuthoredPathVerb.Cubic:
                    int pointCount = command.Verb == SvgAuthoredPathVerb.Line ? 2 : command.Verb == SvgAuthoredPathVerb.Cubic ? 4 : 3;
                    points[0] = MapPoint(map, current);
                    points[1] = MapPoint(map, command.First);
                    if (pointCount >= 3) points[2] = MapPoint(map, command.Second);
                    if (pointCount == 4) points[3] = MapPoint(map, command.Third);
                    current = pointCount == 2 ? command.First : pointCount == 3 ? command.Second : command.Third;
                    Extend(points[..pointCount], closed, style.Cap, firstSegment, finalSegment, workCounter, cancellationToken);
                    if (pointCount == 2) raster.DrawLine(points[0], points[1]);
                    else if (command.Verb == SvgAuthoredPathVerb.Conic) SvgHairlineCurves.DrawConic(ref raster, points[..3], (float)command.Third.X, workCounter, cancellationToken);
                    else if (pointCount == 3) SvgHairlineCurves.DrawQuadratic(ref raster, points[..3], workCounter, cancellationToken);
                    else SvgHairlineCurves.DrawCubic(ref raster, points, workCounter, cancellationToken);
                    if (firstSegment) first = points[0];
                    last = points[pointCount - 1];
                    firstSegment = false;
                    break;
                case SvgAuthoredPathVerb.Arc:
                    for (int phase = 0; phase <= SvgEndpointArc.MaximumConicCount; phase++) workCounter.Consume(cancellationToken);
                    int count = SvgEndpointArc.Convert(current, command.First.X, command.First.Y, command.Second.X,
                        (command.ArcFlags & SvgAuthoredArcFlags.Large) != 0, (command.ArcFlags & SvgAuthoredArcFlags.Sweep) != 0,
                        command.Third, conics, cancellationToken);
                    points[0] = MapPoint(map, current);
                    if (count == 0)
                    {
                        points[1] = MapPoint(map, command.Third);
                        Extend(points[..2], closed, style.Cap, firstSegment, finalSegment, workCounter, cancellationToken);
                        raster.DrawLine(points[0], points[1]);
                        if (firstSegment) first = points[0];
                        last = points[1];
                    }
                    else for (int part = 0; part < count; part++)
                    {
                        workCounter.Consume(cancellationToken);
                        points[1] = MapPoint(map, conics[part].Control);
                        points[2] = MapPoint(map, conics[part].End);
                        SvgDevicePoint mappedEnd = points[2];
                        Extend(points[..3], closed, style.Cap, firstSegment && part == 0, finalSegment && part == count - 1, workCounter, cancellationToken);
                        SvgHairlineCurves.DrawConic(ref raster, points[..3], conics[part].Weight, workCounter, cancellationToken);
                        if (firstSegment && part == 0) first = points[0];
                        last = points[2];
                        points[0] = mappedEnd;
                    }
                    current = command.Third;
                    firstSegment = false;
                    break;
                case SvgAuthoredPathVerb.Close:
                    points[0] = last; points[1] = first;
                    if (firstSegment && style.Cap != SvgStrokeCap.Butt)
                        SvgHairlineCurves.ExtendCaps(points[..2], style.Cap, true, finalSegment, workCounter, cancellationToken);
                    raster.DrawLine(points[0], points[1]);
                    current = contourStart;
                    break;
            }
        }
        return true;

        static float FastLength(float x, float y) => MathF.Max(MathF.Abs(x), MathF.Abs(y)) + MathF.Min(MathF.Abs(x), MathF.Abs(y)) * .5f;
        static void Extend(Span<SvgDevicePoint> points, bool closed, SvgStrokeCap cap, bool first, bool last, ParserWorkCounter work, CancellationToken token)
        {
            if (cap == SvgStrokeCap.Butt) return;
            if (!closed) { SvgHairlineCurves.ExtendCaps(points, cap, first, last, work, token); return; }
            bool degenerate = true;
            foreach (SvgDevicePoint point in points) { work.Consume(token); if (point != points[0]) degenerate = false; }
            if (degenerate) SvgHairlineCurves.ExtendCaps(points, cap, first, last, work, token);
        }
        static SvgDevicePoint MapPoint(SvgAffineTransform map, SvgDevicePoint point)
        {
            float x = (float)point.X, y = (float)point.Y;
            float mappedX = map.IsAxisAligned ? (float)map.A * x + (float)map.E : ((float)map.A * x + (float)map.C * y) + (float)map.E;
            float mappedY = map.IsAxisAligned ? (float)map.D * y + (float)map.F : ((float)map.B * x + (float)map.D * y) + (float)map.F;
            if (!float.IsFinite(mappedX) || !float.IsFinite(mappedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Authored stroke mapping exceeds the admitted numeric range.");
            return new(mappedX, mappedY);
        }
    }
}
