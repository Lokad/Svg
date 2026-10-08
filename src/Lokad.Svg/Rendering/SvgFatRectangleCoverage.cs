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
        SvgRectanglePathRecognizer recognizer = new();
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
            SvgDevicePoint point = command.Verb is SvgAuthoredPathVerb.Move or SvgAuthoredPathVerb.Line
                ? MapPoint(command.First, map, origin, authored) : default;
            if (!recognizer.Add(command.Verb, point)) return null;
        }
        if (recognizer.Finish() is not SvgRecognizedRectangle rectangle) return null;
        if (8 > maximumWork - work) throw new SvgException(SvgErrorCode.LimitExceeded, "Filled rectangle clipping exceeds the remaining work limit.");
        work += 8;
        float left = MathF.Max(rectangle.Left, clip.Left - origin.X);
        float right = MathF.Min(rectangle.Right, clip.Right - origin.X);
        float top = MathF.Max(rectangle.Top, clip.Top - origin.Y);
        float bottom = MathF.Min(rectangle.Bottom, clip.Bottom - origin.Y);
        if (right <= left || bottom <= top || MathF.Ceiling(right) - MathF.Floor(left) < 3) return null;
        return new(left, top, right, bottom, origin);

        static SvgDevicePoint MapPoint(SvgDevicePoint point, SvgAffineTransform map, SvgCpuBitmapOrigin origin, bool authored)
            => authored ? SvgAuthoredPath.MapPoint(map, point, origin) : new((float)point.X - origin.X, (float)point.Y - origin.Y);
    }
}
