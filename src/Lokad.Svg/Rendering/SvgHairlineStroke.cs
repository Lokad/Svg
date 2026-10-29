// Adapts SkDraw.cpp thin-stroke dispatch and SkScan_Hairline.cpp cap extension at the pinned revision.
// Copyright 2006, 2011 The Android Open Source Project; 2011 Google Inc.
using Lokad.Svg.Parsing;
using System.Runtime.InteropServices;

namespace Lokad.Svg.Rendering;

/// <summary>Dispatches retained centerlines to native thin-stroke curve and line coverage.</summary>
/// <remarks>Allocates no storage and borrows immutable commands, the caller's surface and optional
/// canvas-sized RGBA clip tape. Each bordered CPU bitmap constructs curves in local coordinates
/// and paints only its owned interior. The caller keeps the clip tape alive throughout replay;
/// pixel visits include its rounded clip-alpha product. Bounds, context visits, commands, closure
/// lookahead and coverage consume cancellable work. Wider strokes keep their outline route.
/// Numerical curve scratch is bounded stack storage shared across context visits. AA clip containment
/// scans mapped controls and complete borrowed bitmap rows, charging every visit before reading.</remarks>
internal static class SvgHairlineStroke
{
    internal static bool TryRasterize(SvgAuthoredPath path, SvgAffineTransform outputMap, SvgColor color, SvgConstantShaderPaint? constantPaint,
        (double Left, double Top, double Right, double Bottom)? clip, SvgClipAntialiasing antialiasing,
        ReadOnlySpan<byte> viewportMask, bool binaryViewport, Span<byte> destination, int width, int height, CancellationToken cancellationToken)
        => TryRasterizeCore(path, outputMap, color, constantPaint, Span<SvgShaderSampler>.Empty,
            clip, antialiasing, viewportMask, binaryViewport, destination, width, height, cancellationToken);

    /// <summary>Replays the same native hairline geometry with a borrowed per-pixel shader material.</summary>
    /// <remarks>Sampler/ramp/texture ownership remains with the caller. Eligible replay updates the
    /// draw-local paint alpha before sampling; ineligible wider strokes leave the sampler unchanged.
    /// This synchronous call retains no sampler reference and shares geometry work/cancellation.</remarks>
    internal static bool TryRasterize(SvgAuthoredPath path, SvgAffineTransform outputMap, ref SvgShaderSampler shader,
        (double Left, double Top, double Right, double Bottom)? clip, SvgClipAntialiasing antialiasing,
        ReadOnlySpan<byte> viewportMask, bool binaryViewport, Span<byte> destination, int width, int height, CancellationToken cancellationToken)
        => TryRasterizeCore(path, outputMap, default, null, MemoryMarshal.CreateSpan(ref shader, 1),
            clip, antialiasing, viewportMask, binaryViewport, destination, width, height, cancellationToken);

    private static bool TryRasterizeCore(SvgAuthoredPath path, SvgAffineTransform outputMap, SvgColor color, SvgConstantShaderPaint? constantPaint,
        Span<SvgShaderSampler> shaders, (double Left, double Top, double Right, double Bottom)? clip, SvgClipAntialiasing antialiasing,
        ReadOnlySpan<byte> viewportMask, bool binaryViewport, Span<byte> destination, int width, int height, CancellationToken cancellationToken)
    {
        if (path.StrokeStyle is not SvgAuthoredStrokeStyle style) return false;
        cancellationToken.ThrowIfCancellationRequested();
        SvgAffineTransform map = outputMap.ComposeChecked(path.SceneMap);
        SvgAffineTransform widthMap = outputMap.ComposeChecked(style.WidthMap);
        float strokeWidth = (float)style.Width;
        float lengthX = FastLength((float)widthMap.A * strokeWidth, (float)widthMap.B * strokeWidth);
        float lengthY = FastLength((float)widthMap.C * strokeWidth, (float)widthMap.D * strokeWidth);
        if (!(lengthX <= 1 && lengthY <= 1)) return false;
        if (!viewportMask.IsEmpty && viewportMask.Length != checked(width * height * 4))
            throw new ArgumentException("Viewport mask must match the destination canvas.", nameof(viewportMask));
        scoped ReadOnlySpan<SvgAuthoredPathCommand> commands = path.Commands;
        Span<SvgAuthoredPathCommand> rectangleCommands = stackalloc SvgAuthoredPathCommand[5];
        if (style.Shape == SvgAuthoredStrokeShape.Rectangle && commands.Length == 5)
        {
            // General-mapped drawRect falls back to a path starting at top-left.
            // The rounded-rectangle source starts at bottom-left. Hairline segment
            // overlap makes that otherwise equivalent contour order observable.
            rectangleCommands[0] = commands[1] with { Verb = SvgAuthoredPathVerb.Move };
            rectangleCommands[1] = commands[2];
            rectangleCommands[2] = commands[3];
            rectangleCommands[3] = commands[0] with { Verb = SvgAuthoredPathVerb.Line };
            rectangleCommands[4] = commands[4];
            commands = rectangleCommands;
        }
        float coverage = (lengthX + lengthY) * .5f;
        if (!shaders.IsEmpty) shaders[0].ApplyHairlineCoverage(coverage);
        byte alpha = coverage == 1 ? color.Alpha : (byte)((color.Alpha * (int)(coverage * 256)) >> 8);
        SvgAnalyticPaint paint = constantPaint is {} constant
            ? SvgAnalyticPaint.Constant(coverage == 1 ? constant : SvgShaderPaint.ConstantGradient(new SvgGradientStop(0, color) { Alpha = alpha / 255f }, 1))
            : SvgAnalyticPaint.Solid(color with { Alpha = alpha });
        SvgRectangle sourceBounds = SvgPathBounds.Compute(commands, cancellationToken);
        Span<SvgDevicePoint> corners = stackalloc SvgDevicePoint[4];
        corners[0] = new(sourceBounds.X, sourceBounds.Y);
        corners[1] = new(sourceBounds.X + sourceBounds.Width, sourceBounds.Y);
        corners[2] = new(sourceBounds.X, sourceBounds.Y + sourceBounds.Height);
        corners[3] = new(sourceBounds.X + sourceBounds.Width, sourceBounds.Y + sourceBounds.Height);
        double left = double.PositiveInfinity, top = double.PositiveInfinity, right = double.NegativeInfinity, bottom = double.NegativeInfinity;
        foreach (SvgDevicePoint corner in corners)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SvgDevicePoint mapped = SvgAuthoredPath.MapPoint(map, corner, new(0, 0));
            left = Math.Min(left, mapped.X); right = Math.Max(right, mapped.X);
            top = Math.Min(top, mapped.Y); bottom = Math.Max(bottom, mapped.Y);
        }
        SvgAnalyticClipBounds drawingBounds = new((int)Math.Clamp(Math.Floor(left) - 2, 0, width),
            (int)Math.Clamp(Math.Floor(top) - 2, 0, height), (int)Math.Clamp(Math.Ceiling(right) + 2, 0, width), (int)Math.Clamp(Math.Ceiling(bottom) + 2, 0, height));
        Span<SvgDevicePoint> points = stackalloc SvgDevicePoint[4];
        Span<SvgEndpointConic> conics = stackalloc SvgEndpointConic[SvgEndpointArc.MaximumConicCount];
        SvgCpuRasterGrid grid = new(width, height);
        foreach (SvgCpuRasterContext context in grid.GetContexts(drawingBounds))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SvgCpuBitmapOrigin origin = new(context.BitmapClip.Left, context.BitmapClip.Top);
            // Native AA clip wrappers preserve scalar dispatch unless the entire
            // expanded path fits one opaque row band, including its full row shape.
            ReadOnlySpan<byte> paintMask = !binaryViewport && !viewportMask.IsEmpty
                && ContainsPath(viewportMask, width, commands, map, style.Cap, context.BitmapClip, cancellationToken)
                ? [] : viewportMask;
            SvgHairlineCoverage raster = shaders.IsEmpty
                ? new(destination, width, height, paint, clip, antialiasing, paintMask, binaryViewport, context, cancellationToken)
                : new(destination, width, height, shaders, clip, antialiasing, paintMask, binaryViewport, context, cancellationToken);
            SvgDevicePoint current = default, contourStart = default, first = default, last = default;
            bool firstSegment = false, closed = false;
            for (int index = 0; index < commands.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgAuthoredPathCommand command = commands[index];
                SvgAuthoredPathVerb next = index + 1 == commands.Length ? SvgAuthoredPathVerb.End : commands[index + 1].Verb;
                bool finalSegment = next is SvgAuthoredPathVerb.End or SvgAuthoredPathVerb.Close or SvgAuthoredPathVerb.Move;
                switch (command.Verb)
                {
                    case SvgAuthoredPathVerb.Move:
                        current = contourStart = command.First;
                        first = last = SvgAuthoredPath.MapPoint(map, current, origin);
                        firstSegment = true;
                        closed = false;
                        for (int scan = index + 1; scan < commands.Length; scan++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (commands[scan].Verb == SvgAuthoredPathVerb.Close) { closed = true; break; }
                            if (commands[scan].Verb is SvgAuthoredPathVerb.End or SvgAuthoredPathVerb.Move) break;
                        }
                        break;
                    case SvgAuthoredPathVerb.Line:
                    case SvgAuthoredPathVerb.Quadratic:
                    case SvgAuthoredPathVerb.Conic:
                    case SvgAuthoredPathVerb.Cubic:
                        int pointCount = command.Verb == SvgAuthoredPathVerb.Line ? 2 : command.Verb == SvgAuthoredPathVerb.Cubic ? 4 : 3;
                        points[0] = SvgAuthoredPath.MapPoint(map, current, origin);
                        points[1] = SvgAuthoredPath.MapPoint(map, command.First, origin);
                        if (pointCount >= 3) points[2] = SvgAuthoredPath.MapPoint(map, command.Second, origin);
                        if (pointCount == 4) points[3] = SvgAuthoredPath.MapPoint(map, command.Third, origin);
                        current = pointCount == 2 ? command.First : pointCount == 3 ? command.Second : command.Third;
                        Extend(points[..pointCount], closed, style.Cap, firstSegment, finalSegment, cancellationToken);
                        if (pointCount == 2) raster.DrawLine(points[0], points[1]);
                        else if (command.Verb == SvgAuthoredPathVerb.Conic) SvgHairlineCurves.DrawConic(ref raster, points[..3], (float)command.Third.X, cancellationToken);
                        else if (pointCount == 3) SvgHairlineCurves.DrawQuadratic(ref raster, points[..3], cancellationToken);
                        else SvgHairlineCurves.DrawCubic(ref raster, points, cancellationToken);
                        if (firstSegment) first = points[0];
                        last = points[pointCount - 1];
                        firstSegment = false;
                        break;
                    case SvgAuthoredPathVerb.Arc:
                        cancellationToken.ThrowIfCancellationRequested();
                        int count = SvgEndpointArc.Convert(current, command.First.X, command.First.Y, command.Second.X,
                            (command.ArcFlags & SvgAuthoredArcFlags.Large) != 0, (command.ArcFlags & SvgAuthoredArcFlags.Sweep) != 0,
                            command.Third, conics, cancellationToken);
                        points[0] = SvgAuthoredPath.MapPoint(map, current, origin);
                        if (count == 0)
                        {
                            points[1] = SvgAuthoredPath.MapPoint(map, command.Third, origin);
                            Extend(points[..2], closed, style.Cap, firstSegment, finalSegment, cancellationToken);
                            raster.DrawLine(points[0], points[1]);
                            if (firstSegment) first = points[0];
                            last = points[1];
                        }
                        else for (int part = 0; part < count; part++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            points[1] = SvgAuthoredPath.MapPoint(map, conics[part].Control, origin);
                            points[2] = SvgAuthoredPath.MapPoint(map, conics[part].End, origin);
                            SvgDevicePoint mappedEnd = points[2];
                            Extend(points[..3], closed, style.Cap, firstSegment && part == 0, finalSegment && part == count - 1, cancellationToken);
                            SvgHairlineCurves.DrawConic(ref raster, points[..3], conics[part].Weight, cancellationToken);
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
                            SvgHairlineCurves.ExtendCaps(points[..2], style.Cap, true, finalSegment, cancellationToken);
                        raster.DrawLine(points[0], points[1]);
                        current = contourStart;
                        break;
                }
            }
        }
        return true;

        static bool ContainsPath(ReadOnlySpan<byte> mask, int width, ReadOnlySpan<SvgAuthoredPathCommand> commands,
            SvgAffineTransform map, SvgStrokeCap cap, SvgAnalyticClipBounds bitmap, CancellationToken token)
        {
            float left = float.PositiveInfinity, top = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.NegativeInfinity;
            SvgDevicePoint current = default, first = default;
            Span<SvgEndpointConic> arcs = stackalloc SvgEndpointConic[SvgEndpointArc.MaximumConicCount];
            SvgCpuBitmapOrigin origin = new(bitmap.Left, bitmap.Top);
            foreach (SvgAuthoredPathCommand command in commands)
            {
                token.ThrowIfCancellationRequested();
                switch (command.Verb)
                {
                    case SvgAuthoredPathVerb.Move: current = first = command.First; Include(current); break;
                    case SvgAuthoredPathVerb.Line: current = command.First; Include(current); break;
                    case SvgAuthoredPathVerb.Quadratic:
                    case SvgAuthoredPathVerb.Conic:
                        Include(command.First); Include(command.Second); current = command.Second; break;
                    case SvgAuthoredPathVerb.Cubic:
                        Include(command.First); Include(command.Second); Include(command.Third); current = command.Third; break;
                    case SvgAuthoredPathVerb.Arc:
                        token.ThrowIfCancellationRequested();
                        int count = SvgEndpointArc.Convert(current, command.First.X, command.First.Y, command.Second.X,
                            (command.ArcFlags & SvgAuthoredArcFlags.Large) != 0, (command.ArcFlags & SvgAuthoredArcFlags.Sweep) != 0,
                            command.Third, arcs, token);
                        for (int index = 0; index < count; index++) { Include(arcs[index].Control); Include(arcs[index].End); }
                        current = command.Third; Include(current); break;
                    case SvgAuthoredPathVerb.Close:
                    case SvgAuthoredPathVerb.End: current = first; break;
                }
            }
            int outset = cap == SvgStrokeCap.Butt ? 1 : 2;
            double l = MathF.Floor(left) - (double)outset, t = MathF.Floor(top) - (double)outset;
            double r = MathF.Ceiling(right) + (double)outset, b = MathF.Ceiling(bottom) + (double)outset;
            if (!(l >= 0 && t >= 0 && r <= bitmap.Right - bitmap.Left && b < bitmap.Bottom - bitmap.Top)) return false;
            int xStart = bitmap.Left + (int)l, xEnd = bitmap.Left + (int)r;
            int yStart = bitmap.Top + (int)t, yEnd = bitmap.Top + (int)b;
            for (int x = xStart; x < xEnd; x++)
            {
                token.ThrowIfCancellationRequested();
                if (mask[(yStart * width + x) * 4 + 3] != 255) return false;
            }
            // Native quickContains requires one complete compressed row band,
            // whose last row also contains the exclusive bottom boundary.
            for (int y = yStart + 1; y <= yEnd; y++)
                for (int x = bitmap.Left; x < bitmap.Right; x++)
                {
                    token.ThrowIfCancellationRequested();
                    if (mask[(yStart * width + x) * 4 + 3] != mask[(y * width + x) * 4 + 3]) return false;
                }
            return true;

            void Include(SvgDevicePoint point)
            {
                token.ThrowIfCancellationRequested();
                SvgDevicePoint mapped = SvgAuthoredPath.MapPoint(map, point, origin);
                left = MathF.Min(left, (float)mapped.X); right = MathF.Max(right, (float)mapped.X);
                top = MathF.Min(top, (float)mapped.Y); bottom = MathF.Max(bottom, (float)mapped.Y);
            }
        }

        static float FastLength(float x, float y) => MathF.Max(MathF.Abs(x), MathF.Abs(y)) + MathF.Min(MathF.Abs(x), MathF.Abs(y)) * .5f;
        static void Extend(Span<SvgDevicePoint> points, bool closed, SvgStrokeCap cap, bool first, bool last, CancellationToken token)
        {
            if (cap == SvgStrokeCap.Butt) return;
            if (!closed) { SvgHairlineCurves.ExtendCaps(points, cap, first, last, token); return; }
            bool degenerate = true;
            foreach (SvgDevicePoint point in points) { token.ThrowIfCancellationRequested(); if (point != points[0]) degenerate = false; }
            if (degenerate) SvgHairlineCurves.ExtendCaps(points, cap, first, last, token);
        }
    }
}
