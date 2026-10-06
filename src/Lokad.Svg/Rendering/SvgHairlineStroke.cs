// Adapts SkDraw.cpp thin-stroke dispatch and SkScan_Hairline.cpp cap extension at the pinned revision.
// Copyright 2006, 2011 The Android Open Source Project; 2011 Google Inc.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Dispatches retained solid straight centerlines to native thin-stroke line coverage.</summary>
/// <remarks>Allocates no storage and borrows immutable commands and the caller's surface. Every command,
/// closure lookahead and coverage operation is charged and cancellable. Wider, curved and dashed strokes
/// keep their admitted outline route while their native hairline construction is implemented.</remarks>
internal static class SvgHairlineStroke
{
    internal static bool TryRasterize(SvgAuthoredPath path, SvgAffineTransform outputMap, SvgColor color,
        (double Left, double Top, double Right, double Bottom)? clip, SvgClipAntialiasing antialiasing,
        Span<byte> destination, int width, int height, ParserWorkCounter workCounter, CancellationToken cancellationToken)
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
        foreach (SvgAuthoredPathCommand command in commands)
        {
            workCounter.Consume(cancellationToken);
            if (command.Verb is SvgAuthoredPathVerb.Quadratic or SvgAuthoredPathVerb.Cubic or SvgAuthoredPathVerb.Arc) return false;
        }
        float coverage = (lengthX + lengthY) * .5f;
        byte alpha = coverage == 1 ? color.Alpha : (byte)((color.Alpha * (int)(coverage * 256)) >> 8);
        SvgHairlineCoverage raster = new(destination, width, height, SvgAnalyticPaint.Solid(color with { Alpha = alpha }), clip, antialiasing, workCounter, cancellationToken);
        SvgDevicePoint current = default, first = default, last = default;
        bool firstSegment = false, closed = false;
        for (int index = 0; index < commands.Length; index++)
        {
            workCounter.Consume(cancellationToken);
            SvgAuthoredPathCommand command = commands[index];
            switch (command.Verb)
            {
                case SvgAuthoredPathVerb.Move:
                    first = current = last = MapPoint(map, command.First);
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
                    SvgDevicePoint end = MapPoint(map, command.First), start = current;
                    current = end;
                    SvgAuthoredPathVerb next = index + 1 == commands.Length ? SvgAuthoredPathVerb.End : commands[index + 1].Verb;
                    if (style.Cap != SvgStrokeCap.Butt && (!closed || start == end))
                    {
                        float dx = (float)start.X - (float)end.X, dy = (float)start.Y - (float)end.Y;
                        float inverse = dx == 0 && dy == 0 ? 1 : (float)(1 / Math.Sqrt((double)dx * dx + (double)dy * dy));
                        if (dx == 0 && dy == 0) dx = 1;
                        float outset = style.Cap == SvgStrokeCap.Square ? .5f : MathF.PI / 8;
                        float offsetX = dx * inverse * outset, offsetY = dy * inverse * outset;
                        if (firstSegment) start = new((float)start.X + offsetX, (float)start.Y + offsetY);
                        if (next is SvgAuthoredPathVerb.End or SvgAuthoredPathVerb.Close or SvgAuthoredPathVerb.Move)
                            end = new((float)end.X - offsetX, (float)end.Y - offsetY);
                    }
                    raster.DrawLine(start, end);
                    if (firstSegment) first = start;
                    last = end;
                    firstSegment = false;
                    break;
                case SvgAuthoredPathVerb.Close:
                    if (firstSegment && style.Cap != SvgStrokeCap.Butt)
                    {
                        float outset = style.Cap == SvgStrokeCap.Square ? .5f : MathF.PI / 8;
                        last = new((float)last.X + outset, last.Y);
                        first = new((float)first.X - outset, first.Y);
                    }
                    raster.DrawLine(last, first);
                    break;
            }
        }
        return true;

        static float FastLength(float x, float y) => MathF.Max(MathF.Abs(x), MathF.Abs(y)) + MathF.Min(MathF.Abs(x), MathF.Abs(y)) * .5f;
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
