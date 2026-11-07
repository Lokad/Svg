namespace Lokad.Svg.Rendering;

using Lokad.Svg.Parsing;

/// <summary>Owns nonzero clip bounds and border coverage in each software bitmap.</summary>
/// <remarks>Interior coverage borrows a canvas tape or uses lazily allocated single-channel bitmaps.
/// Compact payload borrows the caller's existing RGBA capacity reservation: a canvas pixel belongs
/// to at most four overlapping bitmaps. Pointer/array headers are reserved here before allocation.
/// Border bytes belong
/// to their bitmap and may differ from the neighboring bitmap's owned output pixels. Native
/// construction bounds remain separate from compact output addressing. Storage is admitted before
/// allocation. Intersection observes cancellation during row/pixel traversal. Single-threaded replay
/// borrows this owner within its clip scope; disposal releases metadata/border reservations, while
/// the enclosing clip retains and releases the borrowed payload allowance.</remarks>
internal sealed class SvgCpuClipCoverage : IDisposable
{
    private readonly record struct BitmapCoverage(SvgAnalyticClipBounds Bitmap, SvgAnalyticClipBounds Bounds, bool Prepared);
    private readonly record struct CanvasBitmap(byte[]? Pixels, SvgAnalyticClipBounds Storage);

    private readonly SvgCpuRasterGrid _grid;
    private readonly BitmapCoverage[] _contexts;
    private readonly byte[] _borders;
    private readonly CanvasBitmap[] _samples;
    private readonly int _borderLength;
    private readonly RenderBudget.BudgetReservation _reservation;

    internal SvgCpuClipCoverage(SvgCpuRasterGrid grid, RenderBudget budget)
        : this(grid, budget, false) { }

    internal SvgCpuClipCoverage(SvgCpuRasterGrid grid, RenderBudget budget, bool compact)
    {
        _grid = grid;
        _borderLength = grid.MaximumBorderLength;
        int borderBytes = checked(grid.ContextCount * _borderLength);
        _reservation = budget.Reserve(128 + ParserCollectionMemory.GetArrayByteCount<BitmapCoverage>(grid.ContextCount)
            + ParserCollectionMemory.GetArrayByteCount<byte>(borderBytes)
            + (compact ? ParserCollectionMemory.GetArrayByteCount<CanvasBitmap>(grid.ContextCount) + 32L * grid.ContextCount : 0), SvgMemoryCategory.Surface);
        try
        {
            _contexts = new BitmapCoverage[grid.ContextCount];
            _borders = new byte[borderBytes];
            _samples = compact ? new CanvasBitmap[grid.ContextCount] : [];
        }
        catch { _reservation.Dispose(); throw; }
    }

    public void Dispose() => _reservation.Dispose();

    internal bool HasCanvasPixels => _samples.Length != 0;

    /// <summary>Writes owned output coverage; the caller separately records the bitmap's border samples.</summary>
    /// <remarks>The enclosing clip's RGBA capacity reservation outlives all samples and readers.
    /// Each bitmap allocates once, only on nonzero coverage. Replay checks cancellation before writing.</remarks>
    internal void SetCanvasAlpha(int x, int y, byte alpha)
    {
        SvgAnalyticClipBounds storage = _grid.GetOwningBitmap(x, y);
        int index = _grid.GetContextIndex(new(storage.Left, storage.Top));
        byte[]? samples = _samples[index].Pixels;
        if (samples is null)
        {
            if (alpha == 0) return;
            samples = new byte[checked((storage.Right - storage.Left) * (storage.Bottom - storage.Top))];
            _samples[index] = new(samples, storage);
        }
        samples[(y - storage.Top) * (storage.Right - storage.Left) + x - storage.Left] = alpha;
    }

    /// <summary>Reads the owning bitmap's output sample, independently of neighboring border coverage.</summary>
    internal byte GetCanvasAlpha(int x, int y)
    {
        SvgAnalyticClipBounds owner = _grid.GetOwningBitmap(x, y);
        int index = _grid.GetContextIndex(new(owner.Left, owner.Top));
        byte[]? samples = _samples[index].Pixels;
        if (samples is null) return 0;
        SvgAnalyticClipBounds bitmap = _samples[index].Storage;
        if (x < bitmap.Left || y < bitmap.Top || x >= bitmap.Right || y >= bitmap.Bottom) return 0;
        return samples[(y - bitmap.Top) * (bitmap.Right - bitmap.Left) + x - bitmap.Left];
    }

    /// <summary>Intersects owned samples and computes their enclosure without changing native border samples.</summary>
    /// <remarks>The parent mask is borrowed through this traversal. Border intersection remains in
    /// IntersectAndTrim; callers keep both clip scopes alive. Cancellation is checked per row.</remarks>
    internal SvgAnalyticClipBounds IntersectCanvas(SvgRasterClipMask parent, int width,
        SvgAnalyticClipBounds area, CancellationToken token, out bool rectangle)
    {
        int left = area.Right, top = area.Bottom, right = area.Left, bottom = area.Top;
        long nonzero = 0;
        bool binary = true;
        for (int index = 0; index < _samples.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            byte[]? samples = _samples[index].Pixels;
            if (samples is null) continue;
            SvgAnalyticClipBounds bitmap = _samples[index].Storage;
            int stride = bitmap.Right - bitmap.Left;
            for (int y = Math.Max(area.Top, bitmap.Top); y < Math.Min(area.Bottom, bitmap.Bottom); y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = Math.Max(area.Left, bitmap.Left); x < Math.Min(area.Right, bitmap.Right); x++)
                {
                    if ((x & 1023) == 0) token.ThrowIfCancellationRequested();
                    int offset = (y - bitmap.Top) * stride + x - bitmap.Left;
                    byte alpha = samples[offset];
                    if (alpha == 0) continue;
                    alpha = (byte)((alpha * parent.GetAlpha(width, x, y) + 127) / 255);
                    samples[offset] = alpha;
                    if (alpha == 0) continue;
                    nonzero++; binary &= alpha == 255;
                    left = Math.Min(left, x); top = Math.Min(top, y);
                    right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1);
                }
            }
        }
        rectangle = binary && (nonzero == 0 || nonzero == (long)(right - left) * (bottom - top));
        return left < right && top < bottom ? new(left, top, right, bottom) : new(area.Left, area.Top, area.Left, area.Top);
    }

    internal bool TryGetBounds(SvgAnalyticClipBounds bitmap, out SvgAnalyticClipBounds bounds)
    {
        BitmapCoverage context = _contexts[_grid.GetContextIndex(new(bitmap.Left, bitmap.Top))];
        bounds = context.Bounds;
        return context.Prepared && context.Bitmap == bitmap;
    }

    /// <summary>Reads one prepared bitmap's nonzero bounds for an isolated layer origin.</summary>
    internal bool TryGetBounds(SvgCpuBitmapOrigin origin, out SvgAnalyticClipBounds bounds)
    {
        BitmapCoverage context = _contexts[_grid.GetContextIndex(origin)];
        bounds = context.Bounds;
        return context.Prepared && context.Bitmap.Left == origin.X && context.Bitmap.Top == origin.Y;
    }

    internal void SetBounds(SvgAnalyticClipBounds bitmap, SvgAnalyticClipBounds bounds)
        => _contexts[_grid.GetContextIndex(new(bitmap.Left, bitmap.Top))] = new(bitmap, bounds, true);

    internal void SetBorderAlpha(SvgAnalyticClipBounds bitmap, int x, int y, byte alpha)
    {
        int border = BorderIndex(bitmap, x, y);
        if (border >= 0) _borders[_grid.GetContextIndex(new(bitmap.Left, bitmap.Top)) * _borderLength + border] = alpha;
    }

    /// <summary>Reads this bitmap's border or borrows the caller's owned interior pixel.</summary>
    internal byte GetAlpha(SvgAnalyticClipBounds bitmap, SvgRasterClipMask tape, int width, int x, int y)
    {
        int border = BorderIndex(bitmap, x, y);
        int index = _grid.GetContextIndex(new(bitmap.Left, bitmap.Top));
        return border >= 0 && _contexts[index].Prepared && _contexts[index].Bitmap == bitmap
            ? _borders[index * _borderLength + border] : tape.GetAlpha(width, x, y);
    }

    /// <summary>Intersects local borders and trims bounds after the caller intersects canvas interiors.</summary>
    internal void IntersectAndTrim(SvgCpuClipCoverage? previous, SvgRasterClipMask previousTape, byte[] intersectedTape,
        int width, CancellationToken token)
    {
        for (int index = 0; index < _contexts.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            BitmapCoverage context = _contexts[index];
            if (!context.Prepared) continue;
            SvgAnalyticClipBounds bitmap = context.Bitmap, area = context.Bounds;
            int left = area.Right, top = area.Bottom, right = area.Left, bottom = area.Top;
            for (int y = area.Top; y < area.Bottom; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = area.Left; x < area.Right; x++)
                {
                    if ((x & 1023) == 0) token.ThrowIfCancellationRequested();
                    int border = BorderIndex(bitmap, x, y);
                    byte alpha;
                    if (border >= 0)
                    {
                        int offset = index * _borderLength + border;
                        byte inherited = previous is null ? previousTape.GetAlpha(width, x, y)
                            : previous.GetAlpha(bitmap, previousTape, width, x, y);
                        alpha = (byte)((_borders[offset] * inherited + 127) / 255);
                        _borders[offset] = alpha;
                    }
                    else alpha = HasCanvasPixels ? GetCanvasAlpha(x, y) : intersectedTape[(y * width + x) * 4 + 3];
                    if (alpha == 0) continue;
                    left = Math.Min(left, x); top = Math.Min(top, y);
                    right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1);
                }
            }
            SvgAnalyticClipBounds bounds = left < right && top < bottom ? new(left, top, right, bottom)
                : new(bitmap.Left, bitmap.Top, bitmap.Left, bitmap.Top);
            _contexts[index] = context with { Bounds = bounds };
        }
    }

    private static int BorderIndex(SvgAnalyticClipBounds bitmap, int x, int y)
    {
        int width = bitmap.Right - bitmap.Left, height = bitmap.Bottom - bitmap.Top;
        if (y == bitmap.Top) return x - bitmap.Left;
        if (y == bitmap.Bottom - 1) return width + x - bitmap.Left;
        if (x == bitmap.Left) return 2 * width + y - bitmap.Top - 1;
        if (x == bitmap.Right - 1) return 2 * width + height - 2 + y - bitmap.Top - 1;
        return -1;
    }
}
