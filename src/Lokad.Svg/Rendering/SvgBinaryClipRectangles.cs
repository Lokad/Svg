using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Visits the horizontal bands of a borrowed binary clip in native region order.</summary>
/// <remarks>All bounds are canvas coordinates. The caller retains immutable RGBA coverage and owns
/// the cancellation token; rectangles intersect directly while tape row/run scans check cancellation before reading. This stack-only iterator allocates
/// no storage, and adjacent rows merge only when their full bitmap coverage agrees.</remarks>
internal ref struct SvgBinaryClipRectangles
{
    private readonly SvgRasterClipMask _coverage;
    private readonly int _width, _height;
    private readonly SvgAnalyticClipBounds _bitmap, _area;
    private readonly CancellationToken _token;
    private int _top, _bottom, _x;
    private bool _rectangleVisited;

    internal SvgBinaryClipRectangles(SvgRasterClipMask coverage, int width, int height,
        SvgAnalyticClipBounds bitmap, SvgAnalyticClipBounds area, CancellationToken token)
    {
        if (!coverage.Pixels.IsEmpty && coverage.Pixels.Length != checked(width * height * 4)) throw new ArgumentException("Binary clip storage must match the canvas.", nameof(coverage));
        _coverage = coverage; _width = width; _height = height; _bitmap = bitmap; _area = area;
        _token = token; _top = area.Top; _bottom = area.Top; _x = area.Right;
        _rectangleVisited = false;
        Current = default;
    }

    internal SvgAnalyticClipBounds Current { get; private set; }

    internal bool MoveNext()
    {
        if (_coverage.Rectangle is {} rectangle)
        {
            if (_rectangleVisited) return false;
            _rectangleVisited = true;
            _token.ThrowIfCancellationRequested();
            int left = Math.Max(_area.Left, Round(rectangle.X, _bitmap.Left, _bitmap.Right));
            int top = Math.Max(_area.Top, Round(rectangle.Y, _bitmap.Top, _bitmap.Bottom));
            int right = Math.Min(_area.Right, Round(rectangle.Right, _bitmap.Left, _bitmap.Right));
            int bottom = Math.Min(_area.Bottom, Round(rectangle.Bottom, _bitmap.Top, _bitmap.Bottom));
            Current = new(left, top, Math.Max(left, right), Math.Max(top, bottom));
            return right > left && bottom > top;
        }
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

        static int Round(double edge, int minimum, int maximum)
            => edge <= minimum ? minimum : edge >= maximum ? maximum : (int)MathF.Floor((float)edge + .5f);
    }

    private readonly byte Alpha(int x, int y)
        => (uint)x < (uint)_width && (uint)y < (uint)_height ? _coverage.GetAlpha(_width, x, y) : (byte)0;
}
