using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg;

public static partial class SvgConverter
{
    /// <summary>Owns mapped polygon points with raster bounds, clip and their surface reservation until the rasterizer releases them.</summary>
    private readonly record struct MappedPolygon(SvgDevicePoint[] Points, int StartX, int StartY, int EndX, int EndY, int PathStartX, int PathStartY, int PathEndX, int PathEndY, (double Left, double Top, double Right, double Bottom)? Clip, SvgClipAntialiasing ClipAntialiasing, bool ClipContainsPath, SvgDeviceBounds CompleteBounds, SvgQuadSpan[] QuadSpans, SvgCubicSpan[] CubicSpans, RenderBudget.BudgetReservation? Reservation, int[] ContourEnds, int[]? Signs, SvgPreparedPathContours? Authored, SvgAffineTransform AuthoredMap, SvgPolygonMapping Mapping) : IDisposable
    {
        /// <summary>Borrows the retained dash source and its outline/preparation maps through this paint.</summary>
        internal SvgDashPolygonSource? Dash { get; init; }
        public void Dispose() { Reservation?.Dispose(); Authored?.Dispose(); }

        /// <summary>Gets bordered construction when complete path/control bounds escape this bitmap or the SVG clip.</summary>
        /// <remarks>Global bounds are only an initial containment check: moving translation into the
        /// bitmap before mapping can put a retained point just outside an apparently touching edge.
        /// The raw-operand check borrows source storage, allocates nothing and observes cancellation.</remarks>
        internal SvgAnalyticClipContext? GetConstructionClip(SvgCpuRasterContext context, SvgAnalyticClipBounds? contentClip, CancellationToken token)
        {
            SvgAnalyticClipBounds clip = GetBitmapClip(context, contentClip);
            bool contained = ClipContainsPath && CompleteBounds.IsContainedBy(clip);
            SvgCpuBitmapOrigin origin = context.Origin;
            if (contained && Authored is {} source && (origin.X != 0 || origin.Y != 0))
            {
                int quad = 0, cubic = 0;
                for (int index = 0; index < source.Points.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    if (!Contains(source.Points[index], AuthoredMap, origin, clip)) { contained = false; break; }
                    // Retained curve endpoints and controls define native bounds. Interior
                    // fallback samples are not operands of the source drawing.
                    if (quad < source.QuadSpans.Length && source.QuadSpans[quad].StartIndex == index)
                    {
                        SvgQuadSpan span = source.QuadSpans[quad++];
                        if (!Contains(span.Control, AuthoredMap, origin, clip)) { contained = false; break; }
                        index = span.EndIndex - 1;
                    }
                    else if (cubic < source.CubicSpans.Length && source.CubicSpans[cubic].StartIndex == index)
                    {
                        SvgCubicSpan span = source.CubicSpans[cubic++];
                        if (!Contains(span.FirstControl, AuthoredMap, origin, clip) || !Contains(span.SecondControl, AuthoredMap, origin, clip)) { contained = false; break; }
                        index = span.EndIndex - 1;
                    }
                }
            }
            return contained ? null : new(clip, origin);

            static bool Contains(SvgDevicePoint point, SvgAffineTransform map, SvgCpuBitmapOrigin origin, SvgAnalyticClipBounds clip)
            {
                SvgDevicePoint local = SvgAuthoredPath.MapPoint(map, point, origin);
                return local.X >= (double)clip.Left - origin.X && local.X <= (double)clip.Right - origin.X
                    && local.Y >= (double)clip.Top - origin.Y && local.Y <= (double)clip.Bottom - origin.Y;
            }
        }

        /// <summary>Keeps bitmap walking distinct from whole-path storage and exclusively owned painting.</summary>
        internal SvgAnalyticScanWindow GetScanWindow(SvgCpuRasterContext context, SvgAnalyticClipBounds? contentClip)
        {
            SvgAnalyticClipBounds clip = GetBitmapClip(context, contentClip);
            return new(clip.Left, clip.Right);
        }

        private SvgAnalyticClipBounds GetBitmapClip(SvgCpuRasterContext context, SvgAnalyticClipBounds? contentClip)
        {
            SvgAnalyticClipBounds bitmap = context.BitmapClip;
            if (contentClip is {} content)
            {
                int left = Math.Max(bitmap.Left, content.Left), top = Math.Max(bitmap.Top, content.Top);
                bitmap = new(left, top, Math.Max(left, Math.Min(bitmap.Right, content.Right)), Math.Max(top, Math.Min(bitmap.Bottom, content.Bottom)));
            }
            return Clip is {} bounds
                ? new((int)Math.Clamp((ClipAntialiasing & SvgClipAntialiasing.BinaryLeft) != 0 ? Math.Floor(bounds.Left + .5) : Math.Floor(bounds.Left), bitmap.Left, bitmap.Right),
                    (int)Math.Clamp((ClipAntialiasing & SvgClipAntialiasing.BinaryTop) != 0 ? Math.Floor(bounds.Top + .5) : Math.Floor(bounds.Top), bitmap.Top, bitmap.Bottom),
                    (int)Math.Clamp((ClipAntialiasing & SvgClipAntialiasing.BinaryRight) != 0 ? Math.Floor(bounds.Right + .5) : Math.Ceiling(bounds.Right), bitmap.Left, bitmap.Right),
                    (int)Math.Clamp((ClipAntialiasing & SvgClipAntialiasing.BinaryBottom) != 0 ? Math.Floor(bounds.Bottom + .5) : Math.Ceiling(bounds.Bottom), bitmap.Top, bitmap.Bottom))
                : bitmap;
        }
    }

    /// <summary>Complete mapped vertex/control bounds for construction and whole-path mask/run dispatch.</summary>
    private readonly record struct SvgDeviceBounds(double Left, double Top, double Right, double Bottom)
    {
        /// <summary>Rounds complete bounds for mask/run dispatch without widening bounded raster storage.</summary>
        internal SvgAnalyticClipBounds RoundOutForDispatch()
        {
            // Native safeRoundOut limits coordinates before mask/run width arithmetic.
            // This rectangle selects the blitter only; row storage stays canvas-clamped.
            const int limit = int.MaxValue >> 2;
            return new((int)Math.Clamp(Math.Floor(Left), -limit, limit), (int)Math.Clamp(Math.Floor(Top), -limit, limit),
                (int)Math.Clamp(Math.Ceiling(Right), -limit, limit), (int)Math.Clamp(Math.Ceiling(Bottom), -limit, limit));
        }

        internal bool IsContainedBy(SvgAnalyticClipBounds clip)
            => Left >= clip.Left && Top >= clip.Top && Right <= clip.Right && Bottom <= clip.Bottom;
    }

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

        internal PolygonRaster(scoped ref SvgAnalyticContourPreparation preparation, in MappedPolygon raster,
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
