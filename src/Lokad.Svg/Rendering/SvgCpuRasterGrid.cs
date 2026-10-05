// CPU tile sizing and bordered geometry adapt the Chromium sources and license
// recorded in THIRD_PARTY_NOTICES.txt. Copyright 2010, 2019 The Chromium Authors.
namespace Lokad.Svg.Rendering;

/// <summary>Distinguishes a bordered edge-construction clip from its exclusively owned output pixels.</summary>
internal readonly record struct SvgCpuRasterContext(SvgAnalyticClipBounds BitmapClip, SvgAnalyticClipBounds Interior);

/// <summary>Enumerates the pinned CPU compositor's raster contexts without allocating a tile tape.</summary>
/// <remarks>Settings match the declared Windows software oracle: default 256 and maximum untiled 512, with one border texel. Contexts are canvas-clamped; interiors partition the canvas and bitmap clips overlap. Enumeration skips interiors outside the requested drawing bounds. Callers charge each visited context, construction and paint; the grid owns no storage and is immutable.</remarks>
internal readonly struct SvgCpuRasterGrid
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _tileWidth;
    private readonly int _tileHeight;

    internal SvgCpuRasterGrid(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        _width = width;
        _height = height;
        int tileWidth = 256;
        int tileHeight = 256;
        if (width < tileWidth) tileHeight = 512;
        if (height < tileHeight) tileWidth = 512;
        if (width < 512 && height < 512) { tileWidth = 512; tileHeight = 512; }
        if (width < tileWidth) tileWidth = Math.Min((int)(((long)width + 63) / 64 * 64), tileWidth);
        if (height < tileHeight) tileHeight = Math.Min((int)(((long)height + 63) / 64 * 64), tileHeight);
        _tileWidth = tileWidth;
        _tileHeight = tileHeight;
    }

    internal Enumerator GetContexts(SvgAnalyticClipBounds drawingBounds)
    {
        if (drawingBounds.Left < 0 || drawingBounds.Top < 0 || drawingBounds.Right > _width || drawingBounds.Bottom > _height || drawingBounds.Right < drawingBounds.Left || drawingBounds.Bottom < drawingBounds.Top) throw new ArgumentOutOfRangeException(nameof(drawingBounds));
        return new(_width, _height, _tileWidth, _tileHeight, drawingBounds);
    }

    internal struct Enumerator
    {
        private readonly int _width;
        private readonly int _height;
        private readonly int _tileWidth;
        private readonly int _tileHeight;
        private readonly int _columns;
        private readonly int _rows;
        private readonly int _firstColumn;
        private readonly int _lastColumn;
        private readonly int _firstRow;
        private readonly int _lastRow;
        private int _column;
        private int _row;
        private bool _started;

        internal Enumerator(int width, int height, int tileWidth, int tileHeight, SvgAnalyticClipBounds bounds)
        {
            _width = width;
            _height = height;
            _tileWidth = tileWidth;
            _tileHeight = tileHeight;
            _columns = 1 + Math.Max(0, width - 3) / (tileWidth - 2);
            _rows = 1 + Math.Max(0, height - 3) / (tileHeight - 2);
            _firstColumn = Math.Clamp((bounds.Left - 1) / (tileWidth - 2), 0, _columns - 1);
            _lastColumn = bounds.Right <= bounds.Left ? -1 : Math.Clamp((bounds.Right - 2) / (tileWidth - 2), 0, _columns - 1);
            _firstRow = Math.Clamp((bounds.Top - 1) / (tileHeight - 2), 0, _rows - 1);
            _lastRow = bounds.Bottom <= bounds.Top ? -1 : Math.Clamp((bounds.Bottom - 2) / (tileHeight - 2), 0, _rows - 1);
            _column = _firstColumn;
            _row = _firstRow;
        }

        public readonly Enumerator GetEnumerator() => this;

        public readonly SvgCpuRasterContext Current
        {
            get
            {
                int left = _column * (_tileWidth - 2);
                int top = _row * (_tileHeight - 2);
                int right = (int)Math.Min((long)left + _tileWidth, _width);
                int bottom = (int)Math.Min((long)top + _tileHeight, _height);
                return new(new(left, top, right, bottom), new(_column == 0 ? left : left + 1, _row == 0 ? top : top + 1, _column + 1 == _columns ? right : right - 1, _row + 1 == _rows ? bottom : bottom - 1));
            }
        }

        public bool MoveNext()
        {
            if (!_started) _started = true;
            else if (_column < _lastColumn) _column++;
            else { _column = _firstColumn; _row++; }
            return _column <= _lastColumn && _row <= _lastRow;
        }
    }
}
