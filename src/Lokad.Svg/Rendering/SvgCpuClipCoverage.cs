namespace Lokad.Svg.Rendering;

using Lokad.Svg.Parsing;

/// <summary>Owns nonzero clip bounds and border coverage in each software bitmap.</summary>
/// <remarks>Interior coverage remains in the caller's existing canvas tape. Border bytes belong
/// to their bitmap and may differ from the neighboring bitmap's owned output pixels. Both arrays
/// reserve before allocation; the caller charges initialization and collection. Intersection
/// charges each visited pixel before traversal and observes cancellation. Single-threaded replay
/// borrows this owner only within its clip scope; disposal releases the complete reservation.</remarks>
internal sealed class SvgCpuClipCoverage : IDisposable
{
    private readonly record struct BitmapCoverage(SvgAnalyticClipBounds Bitmap, SvgAnalyticClipBounds Bounds, bool Prepared);

    private readonly SvgCpuRasterGrid _grid;
    private readonly BitmapCoverage[] _contexts;
    private readonly byte[] _borders;
    private readonly int _borderLength;
    private readonly RenderBudget.BudgetReservation _reservation;

    internal SvgCpuClipCoverage(SvgCpuRasterGrid grid, RenderBudget budget)
    {
        _grid = grid;
        _borderLength = grid.MaximumBorderLength;
        int borderBytes = checked(grid.ContextCount * _borderLength);
        _reservation = budget.Reserve(128 + ParserCollectionMemory.GetArrayByteCount<BitmapCoverage>(grid.ContextCount)
            + ParserCollectionMemory.GetArrayByteCount<byte>(borderBytes), SvgMemoryCategory.Surface);
        try
        {
            _contexts = new BitmapCoverage[grid.ContextCount];
            _borders = new byte[borderBytes];
        }
        catch { _reservation.Dispose(); throw; }
    }

    public void Dispose() => _reservation.Dispose();

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
    internal byte GetAlpha(SvgAnalyticClipBounds bitmap, byte[] tape, int width, int x, int y)
    {
        int border = BorderIndex(bitmap, x, y);
        int index = _grid.GetContextIndex(new(bitmap.Left, bitmap.Top));
        return border >= 0 && _contexts[index].Prepared && _contexts[index].Bitmap == bitmap
            ? _borders[index * _borderLength + border] : tape[(y * width + x) * 4 + 3];
    }

    /// <summary>Intersects local borders and trims bounds after the caller intersects canvas interiors.</summary>
    internal void IntersectAndTrim(SvgCpuClipCoverage? previous, byte[] previousTape, byte[] intersectedTape,
        int width, long maximumWork, out long work, CancellationToken token)
    {
        work = 0;
        for (int index = 0; index < _contexts.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            BitmapCoverage context = _contexts[index];
            if (work >= maximumWork) throw new SvgException(SvgErrorCode.LimitExceeded, "Bitmap clip intersection exceeds the configured work limit.");
            work++;
            if (!context.Prepared) continue;
            SvgAnalyticClipBounds bitmap = context.Bitmap, area = context.Bounds;
            long pixels = (long)(area.Right - area.Left) * (area.Bottom - area.Top);
            if (pixels > maximumWork - work) throw new SvgException(SvgErrorCode.LimitExceeded, "Bitmap clip intersection exceeds the configured work limit.");
            work += pixels;
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
                        byte inherited = previous is null ? previousTape[(y * width + x) * 4 + 3]
                            : previous.GetAlpha(bitmap, previousTape, width, x, y);
                        alpha = (byte)((_borders[offset] * inherited + 127) / 255);
                        _borders[offset] = alpha;
                    }
                    else alpha = intersectedTape[(y * width + x) * 4 + 3];
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
