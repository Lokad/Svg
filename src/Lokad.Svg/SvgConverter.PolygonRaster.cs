using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg;

public static partial class SvgConverter
{
    /// <summary>Owns one polygon's analytic or fallback traversal within a CPU raster context.</summary>
    /// <remarks>Source arrays and preparation are borrowed for the enclosing paint. Scratch reservations
    /// are released on construction failure or disposal. Rows remain ordered, work is charged to the
    /// enclosing conversion, and specialized paint arithmetic stays with the caller.</remarks>
    private ref struct PolygonRaster
    {
        private SvgAnalyticCoverage _analytic;
        private SvgScanlineCoverage _scan;
        private readonly int _storageStartX;
        private readonly SvgAnalyticClipBounds _paintArea;

        internal PolygonRaster(scoped ref SvgAnalyticContourPreparation preparation, MappedPolygon raster,
            SvgCpuRasterContext context, SvgAnalyticClipBounds? bitmapClip, SvgAnalyticPaintClip paintClip,
            SvgAnalyticClipBounds paintArea, bool collectCoverage, RenderBudget budget,
            ref long work, long maximumWork, CancellationToken token)
        {
            this = default;
            _storageStartX = Math.Max(raster.PathStartX, context.BitmapClip.Left);
            _paintArea = paintArea;
            try
            {
                int storageEndX = Math.Min(raster.PathEndX, context.BitmapClip.Right);
                _analytic = SvgAnalyticCoverage.Prepare(ref preparation, _storageStartX, storageEndX,
                    raster.PathStartY, raster.PathEndY, raster.GetScanWindow(context, bitmapClip), context.Origin,
                    raster.GetConstructionClip(context, bitmapClip), paintClip, maximumWork - work,
                    budget, out long setup, out bool analytic, token);
                Charge(setup, ref work, maximumWork);
                IsAnalytic = analytic;
                StartX = analytic ? Math.Max(raster.StartX, paintArea.Left) : raster.StartX;
                EndX = analytic ? Math.Min(raster.EndX, paintArea.Right) : raster.EndX;
                StartY = analytic ? Math.Max(raster.StartY, paintArea.Top) : raster.StartY;
                EndY = analytic ? Math.Min(raster.EndY, paintArea.Bottom) : raster.EndY;
                long coverage = (long)(EndY - StartY) * (EndX - StartX)
                    * (analytic ? collectCoverage ? 2 : 1 : raster.Points.Length);
                if (coverage > maximumWork - work) throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill raster work exceeds the configured limit.");
                work += coverage;
                if (!analytic)
                {
                    _scan = SvgScanlineCoverage.Prepare(raster.Points, raster.ContourEnds,
                        raster.Signs is int[] signs ? signs : Array.Empty<int>(), preparation.IsEvenOdd,
                        raster.Signs, raster.StartX, raster.EndX, budget, out setup, token);
                    Charge(setup, ref work, maximumWork);
                }
            }
            catch { Dispose(); throw; }
        }

        internal bool IsAnalytic { get; }
        internal int StartX { get; }
        internal int EndX { get; }
        internal int StartY { get; }
        internal int EndY { get; }

        internal void PrepareRow(int y, ref long work, long maximumWork, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            long rowWork;
            if (IsAnalytic) _analytic.PrepareRow(y, maximumWork - work, out rowWork, token);
            else _scan.PrepareRow(y, out rowWork, token);
            Charge(rowWork, ref work, maximumWork);
        }

        internal SvgAnalyticScanWindow GetPaintWindow(ref long work, long maximumWork)
        {
            SvgAnalyticScanWindow window = IsAnalytic ? _analytic.GetPaintWindow(new(StartX, EndX)) : new(StartX, EndX);
            int left = IsAnalytic ? Math.Max(window.Left, _paintArea.Left) : StartX;
            int right = IsAnalytic ? Math.Min(window.Right, _paintArea.Right) : EndX;
            long extra = (long)right - left - (EndX - StartX);
            if (extra > maximumWork - work) throw new SvgException(SvgErrorCode.LimitExceeded, "Direct fringe painting exceeds the configured work limit.");
            work += extra;
            return new(left, right);
        }

        internal bool MightCover(int x) => _scan.MightCover(x);

        internal void PaintPixel(int x, Span<byte> destination, SvgAnalyticPaint paint, SvgAnalyticClipCoverage clip)
            => _analytic.PaintPixel(x - _storageStartX, destination, paint, clip);

        private static void Charge(long amount, ref long work, long maximum)
        {
            if (amount > maximum - work) throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage setup exceeds the configured limit.");
            work += amount;
        }

        public void Dispose()
        {
            _analytic.Dispose();
            _scan.Dispose();
        }
    }
}
