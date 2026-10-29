using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Visits the horizontal bands of a borrowed binary clip in native region order.</summary>
/// <remarks>All bounds are canvas coordinates. The caller retains immutable RGBA coverage and owns
/// the cancellation token; row/run scans check cancellation before reading. This stack-only iterator allocates
/// no storage, and adjacent rows merge only when their full bitmap coverage agrees.</remarks>
internal ref struct SvgBinaryClipRectangles
{
    private readonly ReadOnlySpan<byte> _coverage;
    private readonly int _width, _height;
    private readonly SvgAnalyticClipBounds _bitmap, _area;
    private readonly CancellationToken _token;
    private int _top, _bottom, _x;

    internal SvgBinaryClipRectangles(ReadOnlySpan<byte> coverage, int width, int height,
        SvgAnalyticClipBounds bitmap, SvgAnalyticClipBounds area, CancellationToken token)
    {
        if (coverage.Length != checked(width * height * 4)) throw new ArgumentException("Binary clip storage must match the canvas.", nameof(coverage));
        _coverage = coverage; _width = width; _height = height; _bitmap = bitmap; _area = area;
_token = token; _top = area.Top; _bottom = area.Top; _x = area.Right;
        Current = default;
    }

    internal SvgAnalyticClipBounds Current { get; private set; }

    internal bool MoveNext()
    {
        while (_top < _area.Bottom)
        {
            if (_x >= _area.Right)
            {
                if (_bottom > _top) _top = _bottom;
                if (_top >= _area.Bottom) return false;
                _bottom = _top + 1;
                while (_bottom < _area.Bottom)
                {
                    bool same = true;
                    for (int x = _bitmap.Left; x < _bitmap.Right; x++)
                    {
                        _token.ThrowIfCancellationRequested();
                        if (Alpha(x, _top) != Alpha(x, _bottom)) { same = false; break; }
                    }
                    if (!same) break;
                    _bottom++;
                }
                _x = _area.Left;
            }
            while (_x < _area.Right)
            {
                _token.ThrowIfCancellationRequested();
                if (Alpha(_x, _top) != 0) break;
                _x++;
            }
            if (_x == _area.Right) continue;
            int left = _x++;
            while (_x < _area.Right)
            {
                _token.ThrowIfCancellationRequested();
                if (Alpha(_x, _top) == 0) break;
                _x++;
            }
            Current = new(left, _top, _x, _bottom);
            return true;
        }
        return false;
    }

    private readonly byte Alpha(int x, int y)
        => (uint)x < (uint)_width && (uint)y < (uint)_height ? _coverage[(y * _width + x) * 4 + 3] : (byte)0;
}
