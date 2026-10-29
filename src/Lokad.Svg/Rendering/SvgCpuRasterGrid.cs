// CPU tile sizing and bordered geometry adapt the Chromium sources and license
// recorded in THIRD_PARTY_NOTICES.txt. Copyright 2010, 2019 The Chromium Authors.
// Local gradient dither adapts Skia's pipeline. Copyright 2018 Google LLC.
// See THIRD_PARTY_NOTICES.txt for the pinned source and BSD license.
namespace Lokad.Svg.Rendering;

/// <summary>Distinguishes a bordered edge-construction clip from its exclusively owned output pixels.</summary>
internal readonly record struct SvgCpuRasterContext(SvgAnalyticClipBounds BitmapClip, SvgAnalyticClipBounds Interior)
{
    internal SvgCpuBitmapOrigin? ConstructionOrigin { get; init; }
    internal SvgCpuBitmapOrigin Origin => ConstructionOrigin ?? new(BitmapClip.Left, BitmapClip.Top);
}

/// <summary>Identifies the canvas origin of one bordered software bitmap.</summary>
internal readonly record struct SvgCpuBitmapOrigin(int X, int Y);

/// <summary>Enumerates the pinned CPU compositor's raster contexts without allocating a tile tape.</summary>
/// <remarks>Settings match the declared Windows software oracle: default 256 and maximum untiled 512, with one border texel. Contexts are canvas-clamped; interiors partition the canvas and bitmap clips overlap. Enumeration skips interiors outside the requested drawing bounds. Callers charge each visited context, construction and paint; the grid owns no storage and is immutable.</remarks>
internal readonly struct SvgCpuRasterGrid
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _tileWidth;
    private readonly int _tileHeight;

    internal SvgCpuRasterGrid(int width, int height)
        : this(width, height, false) { }

    internal SvgCpuRasterGrid(int width, int height, bool singleBitmap)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        _width = width;
        _height = height;
        if (singleBitmap)
        {
            _tileWidth = Math.Max(3, width);
            _tileHeight = Math.Max(3, height);
            return;
        }
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

    internal int ContextCount => checked((1 + Math.Max(0, _width - 3) / (_tileWidth - 2))
        * (1 + Math.Max(0, _height - 3) / (_tileHeight - 2)));

    internal int MaximumBorderLength => 2 * (_tileWidth + _tileHeight) - 4;

    internal int GetContextIndex(SvgCpuBitmapOrigin origin)
        => origin.Y / (_tileHeight - 2) * (1 + Math.Max(0, _width - 3) / (_tileWidth - 2)) + origin.X / (_tileWidth - 2);

    /// <summary>Returns the ordered gradient dither cell in the owning software bitmap's coordinates.</summary>
    /// <remarks>The last bitmap owns both remaining border pixels. The caller supplies an in-canvas pixel, checks cancellation between rows; this lookup allocates no storage.</remarks>
    internal int GetGradientDither(int x, int y)
    {
        SvgCpuBitmapOrigin origin = GetBitmapOrigin(x, y);
        int localX = x - origin.X, localY = y - origin.Y;
        int mixedY = localY ^ localX;
        return ((mixedY & 1) << 5) | ((localX & 1) << 4)
            | ((mixedY & 2) << 2) | ((localX & 2) << 1)
            | ((mixedY & 4) >> 1) | ((localX & 4) >> 2);
    }

    /// <summary>Finds the owning bitmap for an in-canvas pixel without allocating a context tape.</summary>
    /// <remarks>The final bitmap owns the remaining border pixels. Callers charge
    /// raster work and check cancellation; lookup is immutable and allocation-free.</remarks>
    internal SvgCpuBitmapOrigin GetBitmapOrigin(int x, int y)
        => new(Math.Min(x - 1, _width - 3) / (_tileWidth - 2) * (_tileWidth - 2),
            Math.Min(y - 1, _height - 3) / (_tileHeight - 2) * (_tileHeight - 2));

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
