// Adapts SkPathPriv::IsRectContour at the pin in THIRD_PARTY_NOTICES.txt.
// Copyright 2006 The Android Open Source Project; 2025 Google LLC.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>The bounds, explicit closure and direction of one native rectangle contour.</summary>
internal readonly record struct SvgRecognizedRectangle(float Left, float Top, float Right, float Bottom, bool Closed, bool Clockwise);

/// <summary>Recognizes a rectangle from original mapped verbs, without retaining or allocating storage.</summary>
/// <remarks>The caller maps float points and checks cancellation during traversal.
/// Open contours may be rectangles for filling; stroke callers must require explicit closure.
/// Each instance is local to one pass and owns no commands, scene objects or shared state.</remarks>
internal struct SvgRectanglePathRecognizer
{
    private int _corners, _firstDirection, _secondDirection, _previousDirection, _pointCount;
    private bool _closedOrMoved, _hasFirst, _autoClose;
    private SvgDevicePoint _first, _last, _current, _firstCorner, _thirdCorner;

    internal bool Add(SvgAuthoredPathVerb verb, SvgDevicePoint point)
    {
        if (verb == SvgAuthoredPathVerb.End) return true;
        if (verb == SvgAuthoredPathVerb.Move)
        {
            _current = point; _pointCount++;
            if (_corners == 0) { _first = point; _hasFirst = true; }
            else if (_first.X != _last.X && _first.Y != _last.Y) return false;
            _closedOrMoved = true;
            return true;
        }
        if (!_hasFirst || verb is not (SvgAuthoredPathVerb.Line or SvgAuthoredPathVerb.Close)) return false;
        bool close = verb == SvgAuthoredPathVerb.Close;
        SvgDevicePoint end;
        if (close) { end = _first; _autoClose = true; }
        else { end = _last = point; _pointCount++; }
        float dx = (float)end.X - (float)_current.X, dy = (float)end.Y - (float)_current.Y;
        if (!float.IsFinite(dx) || !float.IsFinite(dy) || dx != 0 && dy != 0) return false;
        if (dx == 0 && dy == 0) return true;
        int direction = (dx != 0 ? 1 : 0) | (dx > 0 || dy > 0 ? 2 : 0);
        if (_corners == 0)
        {
            _firstDirection = _previousDirection = direction;
            _corners = 1; _closedOrMoved = false; _current = end;
            return true;
        }
        if (_closedOrMoved) return false;
        if (_autoClose && direction == _firstDirection) return true;
        _closedOrMoved = _autoClose;
        if (direction == _previousDirection)
        {
            if (_corners == 3 && !close) _thirdCorner = end;
            _current = end;
            return true;
        }
        _previousDirection = direction;
        switch (++_corners)
        {
            case 2: _firstCorner = _current; _secondDirection = direction; break;
            case 3:
                if ((_firstDirection ^ direction) != 2) return false;
                _thirdCorner = end;
                break;
            case 4:
                if ((_secondDirection ^ direction) != 2) return false;
                break;
            default: return false;
        }
        _current = end;
        return true;
    }

    internal SvgRecognizedRectangle? Finish()
    {
        if (_pointCount < 4 || _corners < 3 || _corners > 4 || _first.X != _last.X && _first.Y != _last.Y) return null;
        return new(MathF.Min((float)_firstCorner.X, (float)_thirdCorner.X), MathF.Min((float)_firstCorner.Y, (float)_thirdCorner.Y),
            MathF.Max((float)_firstCorner.X, (float)_thirdCorner.X), MathF.Max((float)_firstCorner.Y, (float)_thirdCorner.Y),
            _autoClose, _firstDirection == ((_secondDirection + 1) & 3));
    }
}
