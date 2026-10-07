// Rectangle recognition and coverage adapt Skia at the pin in THIRD_PARTY_NOTICES.txt.
// Copyright 2006 The Android Open Source Project; 2025 Google LLC.
namespace Lokad.Svg.Rendering;

using Lokad.Svg.Parsing;

/// <summary>Retains float fringe coverage for the native filled-path rectangle shortcut.</summary>
/// <remarks>This immutable value owns no storage. Creation borrows scene commands under a bounded,
/// cancellable mapping pass; the returned value retains neither commands nor scene owners.
/// Pixel lookup is allocation-free and belongs to the caller's raster work charge.</remarks>
internal readonly struct SvgFatRectangleCoverage
{
    private readonly float _left, _top, _right, _bottom;
    private readonly int _firstX, _firstY, _lastX, _lastY;
    private readonly SvgCpuBitmapOrigin _origin;

    internal SvgFatRectangleCoverage(float left, float top, float right, float bottom, SvgCpuBitmapOrigin origin)
    {
        _left = left; _top = top; _right = right; _bottom = bottom; _origin = origin;
        _firstX = (int)MathF.Floor(left); _firstY = (int)MathF.Floor(top);
        _lastX = (int)MathF.Ceiling(right) - 1; _lastY = (int)MathF.Ceiling(bottom) - 1;
    }

    internal byte GetAlpha(int x, int y)
    {
        x -= _origin.X; y -= _origin.Y;
        if (x < _firstX || x > _lastX || y < _firstY || y > _lastY) return 0;
        float horizontal = x == _firstX ? _firstX + 1 - _left : x == _lastX ? _right - _lastX : 1f;
        float vertical = _firstY == _lastY ? _bottom - _top : y == _firstY ? _firstY + 1 - _top : y == _lastY ? _bottom - _lastY : 1f;
        int alpha = (int)((horizontal * vertical) * 255f);
        return (byte)(alpha > 247 ? 255 : alpha < 8 ? 0 : alpha);
    }

    /// <summary>Recognizes original mapped line verbs and intersects the actual bitmap clip before fat dispatch.</summary>
    internal static SvgFatRectangleCoverage? TryCreate(ReadOnlySpan<SvgAuthoredPathCommand> commands, ReadOnlySpan<SvgDevicePoint> closedPoints, SvgAffineTransform map,
        SvgCpuBitmapOrigin origin, SvgAnalyticClipBounds clip, ref long work, long maximumWork, CancellationToken token)
    {
        // Retain the native recognizer's direction/corner state. Zero-length lines and
        // monotone collinear pieces are legal; curves and direction reversals are not.
        int corners = 0, firstDirection = -1, secondDirection = -1, previousDirection = -1;
        int pointCount = 0;
        bool closedOrMoved = false, hasFirst = false, autoClose = false;
        SvgDevicePoint first = default, last = default, current = default, firstCorner = default, thirdCorner = default;
        bool authored = !commands.IsEmpty;
        int count = authored ? commands.Length : closedPoints.Length + 1;
        for (int index = 0; index < count; index++)
        {
            SvgAuthoredPathCommand command = authored ? commands[index] : index == closedPoints.Length
                ? new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default)
                : new(index == 0 ? SvgAuthoredPathVerb.Move : SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, closedPoints[index], default, default);
            token.ThrowIfCancellationRequested();
            if (3 > maximumWork - work) throw new SvgException(SvgErrorCode.LimitExceeded, "Filled rectangle recognition exceeds the remaining work limit.");
            work += 3;
            if (command.Verb == SvgAuthoredPathVerb.End) continue; // Parser seal, not a native verb.
            if (command.Verb == SvgAuthoredPathVerb.Move)
            {
                current = MapPoint(command.First, map, origin, authored);
                pointCount++;
                if (corners == 0) { first = current; hasFirst = true; }
                else if (first.X != last.X && first.Y != last.Y) return null;
                closedOrMoved = true;
                continue;
            }
            if (!hasFirst || command.Verb is not (SvgAuthoredPathVerb.Line or SvgAuthoredPathVerb.Close)) return null;
            bool close = command.Verb == SvgAuthoredPathVerb.Close;
            SvgDevicePoint end;
            if (close) { end = first; autoClose = true; }
            else
            {
                end = MapPoint(command.First, map, origin, authored);
                last = end;
                pointCount++;
            }
            float dx = (float)end.X - (float)current.X, dy = (float)end.Y - (float)current.Y;
            if (!float.IsFinite(dx) || !float.IsFinite(dy) || dx != 0 && dy != 0) return null;
            if (dx == 0 && dy == 0) continue;
            int direction = (dx != 0 ? 1 : 0) | (dx > 0 || dy > 0 ? 2 : 0);
            if (corners == 0)
            {
                firstDirection = previousDirection = direction;
                corners = 1; closedOrMoved = false; current = end;
                continue;
            }
            if (closedOrMoved) return null;
            if (autoClose && direction == firstDirection) continue;
            closedOrMoved = autoClose;
            if (direction == previousDirection)
            {
                if (corners == 3 && !close) thirdCorner = end;
                current = end;
                continue;
            }
            previousDirection = direction;
            switch (++corners)
            {
                case 2: firstCorner = current; secondDirection = direction; break;
                case 3:
                    if ((firstDirection ^ direction) != 2) return null;
                    thirdCorner = end;
                    break;
                case 4:
                    if ((secondDirection ^ direction) != 2) return null;
                    break;
                default: return null;
            }
            current = end;
        }
        if (pointCount < 4 || corners < 3 || corners > 4 || first.X != last.X && first.Y != last.Y) return null;
        if (8 > maximumWork - work) throw new SvgException(SvgErrorCode.LimitExceeded, "Filled rectangle clipping exceeds the remaining work limit.");
        work += 8;
        float left = MathF.Max(MathF.Min((float)firstCorner.X, (float)thirdCorner.X), clip.Left - origin.X);
        float right = MathF.Min(MathF.Max((float)firstCorner.X, (float)thirdCorner.X), clip.Right - origin.X);
        float top = MathF.Max(MathF.Min((float)firstCorner.Y, (float)thirdCorner.Y), clip.Top - origin.Y);
        float bottom = MathF.Min(MathF.Max((float)firstCorner.Y, (float)thirdCorner.Y), clip.Bottom - origin.Y);
        if (right <= left || bottom <= top || MathF.Ceiling(right) - MathF.Floor(left) < 3) return null;
        return new(left, top, right, bottom, origin);

        static SvgDevicePoint MapPoint(SvgDevicePoint point, SvgAffineTransform map, SvgCpuBitmapOrigin origin, bool authored)
            => authored ? SvgAuthoredPath.MapPoint(map, point, origin) : new((float)point.X - origin.X, (float)point.Y - origin.Y);
    }
}
