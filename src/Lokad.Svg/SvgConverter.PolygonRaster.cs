using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg;

public static partial class SvgConverter
{
    private readonly record struct SvgDashPolygonSource(SvgAuthoredDashSource Source,
        SvgAffineTransform OutlineMap, SvgAffineTransform PreparationMap);

    /// <summary>Owns one polygon's analytic or fallback traversal within a CPU raster context.</summary>
    /// <remarks>Source arrays and preparation are borrowed for the enclosing paint. Scratch reservations
    /// are released on construction failure or disposal. Rows remain ordered and observe cancellation;
    /// specialized paint arithmetic stays with the caller.</remarks>
    private ref struct PolygonRaster
    {
        private SvgAnalyticCoverage _analytic;
        private SvgScanlineCoverage _scan;
        private SvgAuthoredPath? _strokeOutline;
        private SvgPreparedPathContours? _strokeContours;
        private RenderBudget.BudgetReservation? _strokeMapping;
        private readonly int _storageStartX;
        private readonly SvgAnalyticClipBounds _paintArea;

        internal PolygonRaster(scoped ref SvgAnalyticContourPreparation preparation, MappedPolygon raster,
            SvgCpuRasterContext context, SvgAnalyticClipBounds? bitmapClip, SvgAnalyticPaintClip paintClip,
            SvgAnalyticClipBounds paintArea, RenderBudget budget,
            CancellationToken token)
        {
            this = default;
            _storageStartX = Math.Max(raster.PathStartX, context.BitmapClip.Left);
            _paintArea = paintArea;
            try
            {
                SvgAnalyticContourPreparation tiledPreparation = default;
                bool refiltered = false;
                if (raster.Dash is {} dash && dash.Source.TryFilter(raster.AuthoredMap,
                    new(bitmapClip is {} content ? Intersect(context.BitmapClip, content) : context.BitmapClip, context.Origin),
                    budget, token, out OwnedParserArray<SvgAuthoredPathCommand> filtered))
                {
                    using (filtered)
                    {
                        if (filtered.Items.Length == 0) { IsAnalytic = true; return; }
                        _strokeOutline = BuildOutline(dash, filtered.Items, budget, token);
                    }
                    _strokeContours = _strokeOutline.PrepareSource(dash.PreparationMap, preparation.IsEvenOdd, budget, token);
                    SvgDevicePoint[] points = _strokeContours.Points;
                    SvgQuadSpan[] quads = _strokeContours.QuadSpans;
                    SvgCubicSpan[] cubics = _strokeContours.CubicSpans;
                    _strokeMapping = budget.Reserve(checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(points.Length)
                        + ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(quads.Length)
                        + ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(cubics.Length)), SvgMemoryCategory.Surface);
                    SvgDevicePoint[] mapped = new SvgDevicePoint[points.Length];
                    SvgQuadSpan[] mappedQuads = MapQuadSpanControls(quads, 1, 0, 1, 0, preparation.OutputWidth,
                        preparation.OutputHeight, true, out _, raster.AuthoredMap, token);
                    SvgCubicSpan[] mappedCubics = MapCubicSpanControls(cubics, 1, 0, 1, 0, preparation.OutputWidth,
                        preparation.OutputHeight, out _, raster.AuthoredMap, token);
                    double left = double.PositiveInfinity, top = double.PositiveInfinity, right = double.NegativeInfinity, bottom = double.NegativeInfinity;
                    for (int index = 0; index < points.Length; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        mapped[index] = SvgAuthoredPath.MapPoint(raster.AuthoredMap, points[index], new(0, 0));
                        Include(mapped[index], ref left, ref top, ref right, ref bottom);
                    }
                    foreach (SvgQuadSpan quad in mappedQuads) { token.ThrowIfCancellationRequested(); Include(quad.Control, ref left, ref top, ref right, ref bottom); }
                    foreach (SvgCubicSpan cubic in mappedCubics)
                    {
                        token.ThrowIfCancellationRequested();
                        Include(cubic.FirstControl, ref left, ref top, ref right, ref bottom);
                        Include(cubic.SecondControl, ref left, ref top, ref right, ref bottom);
                    }
                    SvgDeviceBounds bounds = points.Length == 0 ? new(0, 0, 0, 0) : new(left, top, right, bottom);
                    tiledPreparation = new(mapped, _strokeContours.ContourEnds, _strokeContours.Signs ?? [], preparation.IsEvenOdd,
                        _strokeContours.Signs, preparation.Mapping, mappedQuads, mappedCubics, bounds.RoundOutForDispatch(),
                        preparation.OutputWidth, preparation.OutputHeight, _strokeContours, raster.AuthoredMap);
                    refiltered = true;
                }
                int storageEndX = Math.Min(raster.PathEndX, context.BitmapClip.Right);
                bool analytic;
                if (refiltered)
                    _analytic = SvgAnalyticCoverage.Prepare(ref tiledPreparation, _storageStartX, storageEndX,
                        raster.PathStartY, raster.PathEndY, raster.GetScanWindow(context, bitmapClip), context.Origin,
                        raster.GetConstructionClip(context, bitmapClip, token), paintClip, budget, out analytic, token);
                else
                    _analytic = SvgAnalyticCoverage.Prepare(ref preparation, _storageStartX, storageEndX,
                        raster.PathStartY, raster.PathEndY, raster.GetScanWindow(context, bitmapClip), context.Origin,
                        raster.GetConstructionClip(context, bitmapClip, token), paintClip, budget, out analytic, token);
                IsAnalytic = analytic;
                StartX = analytic ? Math.Max(raster.StartX, paintArea.Left) : raster.StartX;
                EndX = analytic ? Math.Min(raster.EndX, paintArea.Right) : raster.EndX;
                StartY = analytic ? Math.Max(raster.StartY, paintArea.Top) : raster.StartY;
                EndY = analytic ? Math.Min(raster.EndY, paintArea.Bottom) : raster.EndY;
                if (!analytic)
                {
                    _scan = refiltered
                        ? SvgScanlineCoverage.Prepare(tiledPreparation.Points, tiledPreparation.Ends, tiledPreparation.NestingSigns,
                            tiledPreparation.IsEvenOdd, tiledPreparation.Signs, raster.StartX, raster.EndX, budget, token)
                        : SvgScanlineCoverage.Prepare(raster.Points, raster.ContourEnds,
                            raster.Signs is int[] signs ? signs : Array.Empty<int>(), preparation.IsEvenOdd,
                            raster.Signs, raster.StartX, raster.EndX, budget, token);
                }
            }
            catch { Dispose(); throw; }

            static SvgAnalyticClipBounds Intersect(SvgAnalyticClipBounds first, SvgAnalyticClipBounds second)
            {
                int left = Math.Max(first.Left, second.Left), top = Math.Max(first.Top, second.Top);
                return new(left, top, Math.Max(left, Math.Min(first.Right, second.Right)), Math.Max(top, Math.Min(first.Bottom, second.Bottom)));
            }

            static SvgAuthoredPath BuildOutline(SvgDashPolygonSource source, ReadOnlySpan<SvgAuthoredPathCommand> commands,
                RenderBudget budget, CancellationToken token)
            {
                using RenderBudget.BudgetReservation owners = budget.Reserve(256, SvgMemoryCategory.Geometry);
                using BudgetedParserList<SvgStrokeOutlineCommand> outline = new(budget, SvgMemoryCategory.Geometry, token);
                using BudgetedParserList<SvgAuthoredPathCommand> verbs = new(budget, SvgMemoryCategory.Geometry, token);
                SvgNativeStrokeParameters stroke = source.Source.Parameters;
                SvgNativePathStroke.Build(commands, stroke.Width * .5f, stroke.MiterLimit, stroke.Cap, stroke.Join,
                    stroke.ResolutionScale, outline, budget, token);
                foreach (SvgStrokeOutlineCommand command in outline.AsSpan()) verbs.Add(command.ToAuthoredPath());
                OwnedParserArray<SvgAuthoredPathCommand> owned = verbs.Detach();
                try { return SvgAuthoredPath.Create(owned, source.OutlineMap, budget); }
                catch { owned.Dispose(); throw; }
            }

            static void Include(SvgDevicePoint point, ref double left, ref double top, ref double right, ref double bottom)
            {
                left = Math.Min(left, point.X); top = Math.Min(top, point.Y);
                right = Math.Max(right, point.X); bottom = Math.Max(bottom, point.Y);
            }
        }

        internal bool IsAnalytic { get; }
        internal int StartX { get; }
        internal int EndX { get; }
        internal int StartY { get; }
        internal int EndY { get; }

        internal void PrepareRow(int y, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (IsAnalytic) _analytic.PrepareRow(y, token);
            else _scan.PrepareRow(y, token);
        }

        internal SvgAnalyticScanWindow GetPaintWindow()
        {
            SvgAnalyticScanWindow window = IsAnalytic ? _analytic.GetPaintWindow(new(StartX, EndX)) : new(StartX, EndX);
            int left = IsAnalytic ? Math.Max(window.Left, _paintArea.Left) : StartX;
            int right = IsAnalytic ? Math.Min(window.Right, _paintArea.Right) : EndX;
            return new(left, right);
        }

        internal bool MightCover(int x) => _scan.MightCover(x);

        internal void PaintPixel(int x, scoped Span<byte> destination, SvgAnalyticPaint paint, SvgAnalyticClipCoverage clip)
            => _analytic.PaintPixel(x - _storageStartX, destination, paint, clip);

        public void Dispose()
        {
            _analytic.Dispose();
            _scan.Dispose();
            _strokeContours?.Dispose();
            _strokeOutline?.Dispose();
            _strokeMapping?.Dispose();
        }
    }
}
