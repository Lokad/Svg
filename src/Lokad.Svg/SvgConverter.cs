namespace Lokad.Svg;

using Lokad.Svg.Parsing;
using Lokad.Svg.Encoding;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;
using System.Xml;

/// <summary>Converts static SVG bytes to PNG bytes.</summary>
public static class SvgConverter
{
    private const int GlyphSamplesPerAxis = 4;
    private enum PathCoordinateSpace { User, Device }
    /// <summary>Owns mapped polygon points with raster bounds, clip and their surface reservation until the rasterizer releases them.</summary>
    private readonly record struct MappedPolygon(SvgDevicePoint[] Points, int StartX, int StartY, int EndX, int EndY, int PathStartX, int PathStartY, int PathEndX, int PathEndY, (double Left, double Top, double Right, double Bottom)? Clip, SvgClipAntialiasing ClipAntialiasing, bool ClipContainsPath, SvgDeviceBounds CompleteBounds, bool HasFractionalEdge, SvgQuadSpan[] QuadSpans, SvgCubicSpan[] CubicSpans, RenderBudget.BudgetReservation? Reservation, int[] ContourEnds, int[]? Signs, SvgPreparedPathContours? Authored, SvgAffineTransform AuthoredMap, SvgPolygonMapping Mapping) : IDisposable
    {
        public void Dispose() { Reservation?.Dispose(); Authored?.Dispose(); }

        /// <summary>Gets bordered construction when complete mapped path/control bounds escape this bitmap or the SVG clip.</summary>
        internal SvgAnalyticClipContext? GetConstructionClip(SvgCpuRasterContext context, SvgAnalyticClipBounds? contentClip)
            => ClipContainsPath && CompleteBounds.IsContainedBy(GetBitmapClip(context, contentClip)) ? null : new(GetBitmapClip(context, contentClip), context.Origin);

        /// <summary>Keeps bitmap walking distinct from whole-path storage and exclusively owned painting.</summary>
        internal SvgAnalyticScanWindow GetScanWindow(SvgCpuRasterContext context, SvgAnalyticClipBounds? contentClip)
        {
            SvgAnalyticClipBounds clip = GetBitmapClip(context, contentClip);
            return new(clip.Left, clip.Right);
        }

        /// <summary>Retains AA paint routing even when the complete path needs no construction clipping.</summary>
        internal SvgAnalyticPaintClip GetPaintClip(SvgCpuRasterContext context)
            => Clip is {} bounds ? SvgAnalyticRectangleClip.Classify(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, context.BitmapClip, ClipAntialiasing) : SvgAnalyticPaintClip.Binary;

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

    /// <summary>Converts SVG bytes to an independently owned PNG result.</summary>
    public static ReadOnlyMemory<byte> ConvertToPng(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        using RasterizedConversion conversion = PrepareRasterization(svg, options, cancellationToken);
        return PngEncoder.Encode(conversion.Surface.Width, conversion.Surface.Height, conversion.Surface.Pixels, conversion.Budget, options.Limits.MaximumEncodedPngBytes, conversion.Surface.WorkUnits, options.Limits.MaximumWorkUnits, cancellationToken);
    }

    /// <summary>Schedules one conversion on the thread pool.</summary>
    public static Task<ReadOnlyMemory<byte>> ConvertToPngAsync(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        return Task.Run(() => ConvertToPng(svg, options, cancellationToken), cancellationToken);
    }

    /// <summary>Converts SVG bytes into caller-owned destination memory.</summary>
    public static int ConvertToPng(ReadOnlyMemory<byte> svg, Memory<byte> destination, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ValidateInvocation(svg, options, cancellationToken);
        if (svg.Span.Overlaps(destination.Span)) throw new ArgumentException("PNG destination must not overlap SVG input memory.", nameof(destination));
        using RasterizedConversion conversion = PrepareRasterizationCore(svg, options, cancellationToken);
        return PngEncoder.EncodeInto(conversion.Surface.Width, conversion.Surface.Height, conversion.Surface.Pixels, destination, conversion.Budget, options.Limits.MaximumEncodedPngBytes, conversion.Surface.WorkUnits, options.Limits.MaximumWorkUnits, cancellationToken);
    }

    /// <summary>Converts SVG bytes to caller-owned writable stream output.</summary>
    public static int ConvertToPng(ReadOnlyMemory<byte> svg, Stream destination, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("PNG destination must be writable.", nameof(destination));
        using RasterizedConversion conversion = PrepareRasterization(svg, options, cancellationToken);
        return PngEncoder.EncodeTo(conversion.Surface.Width, conversion.Surface.Height, conversion.Surface.Pixels, destination, conversion.Budget, options.Limits.MaximumEncodedPngBytes, conversion.Surface.WorkUnits, options.Limits.MaximumWorkUnits, cancellationToken);
    }

    /// <summary>Schedules one destination conversion on the thread pool.</summary>
    public static Task<int> ConvertToPngAsync(ReadOnlyMemory<byte> svg, Memory<byte> destination, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        return Task.Run(() => ConvertToPng(svg, destination, options, cancellationToken), cancellationToken);
    }

    /// <summary>Schedules one conversion with asynchronous writes to a caller-owned writable stream.</summary>
    public static Task<int> ConvertToPngAsync(ReadOnlyMemory<byte> svg, Stream destination, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (!destination.CanWrite) throw new ArgumentException("PNG destination must be writable.", nameof(destination));
            using RasterizedConversion conversion = PrepareRasterization(svg, options, cancellationToken);
            return await PngEncoder.EncodeToAsync(conversion.Surface.Width, conversion.Surface.Height, conversion.Surface.Pixels, destination, conversion.Budget, options.Limits.MaximumEncodedPngBytes, conversion.Surface.WorkUnits, options.Limits.MaximumWorkUnits, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>Converts one retained prepared scene to an independently owned PNG result.</summary>
    internal static ReadOnlyMemory<byte> ConvertToPng(SvgRoot root, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
    {
        ValidatePreparedInvocation(options, budget, cancellationToken);
        using RasterizedSurface surface = Rasterize(root, options, budget, cancellationToken);
        return PngEncoder.Encode(surface.Width, surface.Height, surface.Pixels, budget, options.Limits.MaximumEncodedPngBytes, surface.WorkUnits, options.Limits.MaximumWorkUnits, cancellationToken);
    }

    /// <summary>Converts one retained prepared scene into caller-owned destination memory.</summary>
    internal static int ConvertToPng(SvgRoot root, Memory<byte> destination, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
    {
        ValidatePreparedInvocation(options, budget, cancellationToken);
        using RasterizedSurface surface = Rasterize(root, options, budget, cancellationToken);
        return PngEncoder.EncodeInto(surface.Width, surface.Height, surface.Pixels, destination, budget, options.Limits.MaximumEncodedPngBytes, surface.WorkUnits, options.Limits.MaximumWorkUnits, cancellationToken);
    }

    /// <summary>Converts one retained prepared scene to a caller-owned writable stream.</summary>
    internal static int ConvertToPng(SvgRoot root, Stream destination, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("PNG destination must be writable.", nameof(destination));
        ValidatePreparedInvocation(options, budget, cancellationToken);
        using RasterizedSurface surface = Rasterize(root, options, budget, cancellationToken);
        return PngEncoder.EncodeTo(surface.Width, surface.Height, surface.Pixels, destination, budget, options.Limits.MaximumEncodedPngBytes, surface.WorkUnits, options.Limits.MaximumWorkUnits, cancellationToken);
    }

    /// <summary>Schedules one retained prepared-scene conversion on the thread pool.</summary>
    internal static Task<ReadOnlyMemory<byte>> ConvertToPngAsync(SvgRoot root, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
        => Task.Run(() => ConvertToPng(root, options, budget, cancellationToken), cancellationToken);

    /// <summary>Schedules one retained prepared-scene destination conversion on the thread pool.</summary>
    internal static Task<int> ConvertToPngAsync(SvgRoot root, Memory<byte> destination, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
        => Task.Run(() => ConvertToPng(root, destination, options, budget, cancellationToken), cancellationToken);

    /// <summary>Schedules one retained prepared-scene conversion with asynchronous writes to a caller-owned writable stream.</summary>
    internal static Task<int> ConvertToPngAsync(SvgRoot root, Stream destination, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (!destination.CanWrite) throw new ArgumentException("PNG destination must be writable.", nameof(destination));
            ValidatePreparedInvocation(options, budget, cancellationToken);
            using RasterizedSurface surface = Rasterize(root, options, budget, cancellationToken);
            return await PngEncoder.EncodeToAsync(surface.Width, surface.Height, surface.Pixels, destination, budget, options.Limits.MaximumEncodedPngBytes, surface.WorkUnits, options.Limits.MaximumWorkUnits, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    private static void ValidateInvocation(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (svg.Length > options.Limits.MaximumInputBytes)
        {
            throw new SvgException(SvgErrorCode.LimitExceeded, "SVG input exceeds the configured input limit.");
        }
    }

    private static void ValidatePreparedInvocation(SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(budget);
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Creates the one budgeted parse-and-raster lifetime shared by every output API.</summary>
    private static RasterizedConversion PrepareRasterization(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ValidateInvocation(svg, options, cancellationToken);
        return PrepareRasterizationCore(svg, options, cancellationToken);
    }

    private static RasterizedConversion PrepareRasterizationCore(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
        => PrepareRasterizationCore(svg, options, null, cancellationToken);

    private static RasterizedConversion PrepareRasterizationCore(ReadOnlyMemory<byte> svg, SvgRenderOptions options, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        RenderBudget budget = new(options.Limits.MaximumAccountedBytes);
        SvgRoot root = ParseRoot(svg, options, budget, workCounter, cancellationToken);
        try
        {
            if (root.HasUnsupportedChild) throw new SvgException(SvgErrorCode.UnsupportedFeature, "This SVG child element is not implemented yet.");
            return new RasterizedConversion(budget, root, Rasterize(root, options, budget, cancellationToken));
        }
        catch
        {
            root.Dispose();
            throw;
        }
    }

    /// <summary>Renders one parsed scene while retaining its surface reservation until the returned surface is disposed.</summary>
    internal static RasterizedSurface Rasterize(SvgRoot root, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
        => Rasterize(root, options, budget, RasterSurfaceMode.Capture, cancellationToken);

    /// <summary>Renders a borrowed scene with shared budget/cancellation; pattern surfaces remain premultiplied for image-shader sampling.</summary>
    private static RasterizedSurface Rasterize(SvgRoot root, SvgRenderOptions options, RenderBudget budget, RasterSurfaceMode surfaceMode, CancellationToken cancellationToken)
        => Rasterize(root, options, budget, surfaceMode, null, cancellationToken);

    private readonly record struct RasterFrame(int Width, int Height, double ScaleX, double OffsetX, double ScaleY, double OffsetY)
    {
        // Native filter sources use one padded device, with its content clip
        // separate from the physical bitmap origin.
        internal bool SingleBitmap { get; init; }
        internal SvgAnalyticClipBounds? ContentClip { get; init; }
        // Keep the parent's rounded CTM before changing to the source device.
        internal SvgCpuBitmapOrigin? ParentOrigin { get; init; }
        internal SvgCpuBitmapOrigin CaptureOrigin { get; init; }
    }

    private static RasterizedSurface Rasterize(SvgRoot root, SvgRenderOptions options, RenderBudget budget, RasterSurfaceMode surfaceMode, RasterFrame? frame, CancellationToken cancellationToken)
    {
        (double width, double height) = ResolveOutputDimensions(options.Viewport, root.Width, root.Height, root.ViewBox);
        double scaledWidth = frame?.Width ?? Math.Round(width * options.OutputScale, MidpointRounding.AwayFromZero);
        double scaledHeight = frame?.Height ?? Math.Round(height * options.OutputScale, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(scaledWidth) || !double.IsFinite(scaledHeight) || scaledWidth <= 0 || scaledHeight <= 0 || scaledWidth != Math.Truncate(scaledWidth) || scaledHeight != Math.Truncate(scaledHeight)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive finite integral output dimensions are implemented currently.");
        if (scaledWidth > options.Limits.MaximumDimension || scaledHeight > options.Limits.MaximumDimension) throw new SvgException(SvgErrorCode.LimitExceeded, "Output dimensions exceed the configured dimension limit.");
        int outputWidth = (int)scaledWidth;
        int outputHeight = (int)scaledHeight;
        SvgCpuRasterGrid rasterGrid = new(outputWidth, outputHeight, frame?.SingleBitmap == true);
        long pixelCountLong = (long)outputWidth * outputHeight;
        if (pixelCountLong > options.Limits.MaximumPixels || pixelCountLong > int.MaxValue / 4) throw new SvgException(SvgErrorCode.LimitExceeded, "Output pixel count exceeds the configured contiguous RGBA surface limit.");
        int pixelCount = (int)pixelCountLong;
        int surfaceByteCount = pixelCount * 4;
        long workUnits = root.ParserWorkUnits;
        if (workUnits > options.Limits.MaximumWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Scene parser work exceeds the configured rendering limit.");
        if (options.BackgroundColor != SvgColor.Transparent)
        {
            if (pixelCount > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Background initialization exceeds the configured work limit.");
            workUnits += pixelCount;
        }
        (double horizontalScale, double horizontalOffset, double verticalScale, double verticalOffset) = GetViewBoxTransform(root.ViewBox, width, height, options.OutputScale);
        if (frame is {} filterFrame) (horizontalScale, horizontalOffset, verticalScale, verticalOffset) = (filterFrame.ScaleX, filterFrame.OffsetX, filterFrame.ScaleY, filterFrame.OffsetY);
        if (!double.IsFinite(horizontalScale) || !double.IsFinite(horizontalOffset) || !double.IsFinite(verticalScale) || !double.IsFinite(verticalOffset) || horizontalScale <= 0 || verticalScale <= 0)
        {
            throw new SvgException(SvgErrorCode.LimitExceeded, "Output scaling and viewBox mapping exceed the admitted numeric range.");
        }
        if (horizontalScale != 1 || verticalScale != 1)
        {
            foreach (SvgGeometry geometry in root.Geometries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (geometry.Kind == SvgGeometryKind.PatternFill && !TryMapPatternTile(geometry.Pattern, out _)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills under viewBox or output scaling are not implemented.");
                bool isCurveFill = (geometry.Kind == SvgGeometryKind.PolygonFill && geometry.Polygon.Mapping == SvgPolygonMapping.FlattenedCurves)
                    || (geometry.Kind == SvgGeometryKind.GradientPolygon && geometry.GradientPolygon.Mapping == SvgPolygonMapping.FlattenedCurves)
                    || (geometry.Kind == SvgGeometryKind.RadialPolygon && geometry.RadialPolygon.Mapping == SvgPolygonMapping.FlattenedCurves);
                if (horizontalScale != verticalScale && isCurveFill && geometry.AuthoredPath is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Curve fills under non-uniform viewBox or output scaling require retained source commands.");
            }
        }
        SvgCpuClipCoverage? layerClipCoverage = null;
        float foldedOpacity = 1;
        SvgAnalyticClipBounds layerBounds = frame?.ContentClip ?? new(0, 0, outputWidth, outputHeight);
        bool maskClipAntialiased = false;
        bool collectingViewportClip = false;
        byte[] activeViewportClip = [];
        bool activeViewportIsBinary = true;
        SvgCpuClipCoverage? collectingClipCoverage = null, activeViewportCoverage = null, activeConstructionCoverage = null;
        byte[] activeConstructionClip = [];
        SvgAnalyticClipBounds? activeConstructionBounds = null;
        SvgCpuClipCoverage? clipWindowCoverage = null;
        SvgAnalyticClipBounds? clipWindowBounds = null;
        SvgAnalyticClipBounds? activeViewportBounds = null;
        SvgRecordedPaintValue activePaint = default;
        SvgRecordedPaintValue activeStrokePaint = default;
        SvgPatternTileCache? patternTileCache = null;
        SvgPatternGeometry? nextPatternTile = null;
        RenderBudget.BudgetReservation surface = budget.Reserve(surfaceByteCount, SvgMemoryCategory.Surface);
        try
        {
            byte[] pixels = new byte[surfaceByteCount];
            if (options.BackgroundColor != SvgColor.Transparent)
            {
                BackgroundInitializationProbe.Started();
                for (int pixel = 0; pixel < pixelCount; pixel++)
                {
                    if ((pixel & 4_095) == 0 && cancellationToken.IsCancellationRequested)
                    {
                        BackgroundInitializationProbe.CancellationObserved();
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    int offset = pixel * 4;
                    pixels[offset] = options.BackgroundColor.Red;
                    pixels[offset + 1] = options.BackgroundColor.Green;
                    pixels[offset + 2] = options.BackgroundColor.Blue;
                    pixels[offset + 3] = options.BackgroundColor.Alpha;
                }
            }
            if (surfaceMode == RasterSurfaceMode.Capture && TryResolveSolidCanvas(out SvgColor solidColor))
            {
                int red = (solidColor.Red * solidColor.Alpha + 127) / 255;
                int green = (solidColor.Green * solidColor.Alpha + 127) / 255;
                int blue = (solidColor.Blue * solidColor.Alpha + 127) / 255;
                if (pixelCount > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle raster work exceeds the configured limit.");
                workUnits += pixelCount;
                for (int y = 0; y < outputHeight; y++)
                {
                    SolidCanvasRasterizationProbe.Row(y);
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int x = 0; x < outputWidth; x++)
                    {
                        if ((x & 4_095) == 0) cancellationToken.ThrowIfCancellationRequested();
                        int offset = (y * outputWidth + x) * 4;
                        pixels[offset] = (byte)red;
                        pixels[offset + 1] = (byte)green;
                        pixels[offset + 2] = (byte)blue;
                        pixels[offset + 3] = solidColor.Alpha;
                    }
                }
            }
            else
            {
                int commandIndex = RenderCommands(0, pixels, false);
                if (commandIndex != root.Commands.Length) throw new SvgException(SvgErrorCode.MalformedInput, "SVG group structure is unbalanced.");
            }
            if (surfaceMode == RasterSurfaceMode.Capture) ApplyCaptureUnpremultiply(pixels, pixelCount, cancellationToken);
            return new RasterizedSurface(outputWidth, outputHeight, pixels, surface, workUnits);
        }
        catch
        {
            surface.Dispose();
            throw;
        }
        finally { patternTileCache?.Dispose(); }

        SvgPaintBounds FilterRegion(SvgFilterBinding binding, SvgFilterResource resource, ParserWorkCounter counter)
        {
            SvgFilterRegion region = resource.Region;
            SvgPaintBounds box = binding.ObjectBounds;
            bool objectUnits = region.Units == SvgFilterUnits.ObjectBoundingBox;
            double basisX = objectUnits ? box.Right - box.Left : binding.LengthBases.Width ?? width;
            double basisY = objectUnits ? box.Bottom - box.Top : binding.LengthBases.Height ?? height;
            double x = Length(region.X, basisX), y = Length(region.Y, basisY);
            double w = Length(region.Width, basisX), h = Length(region.Height, basisY);
            if (w <= 0 || h <= 0) return default;
            SvgAffineTransform map = SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset);
            if (objectUnits) { x += box.Left; y += box.Top; }
            else map = map.ComposeChecked(binding.Transform);
            SvgPaintBounds bounds = new SvgPaintBounds((float)x, (float)y, (float)(x + w), (float)(y + h)).Map(map, counter, cancellationToken);
            return bounds;

            double Length(SvgFilterLength length, double basis) => length.Percentage ? length.Value * basis / 100 : objectUnits ? length.Value * basis : length.Value;
        }

        int RenderFilter(int first, SvgDrawCommand command, byte[] destination)
        {
            SvgFilterArena arena = root.SceneStorage?.Filters ?? throw new SvgException(SvgErrorCode.MalformedInput, "Filter resources are missing.");
            SvgFilterBinding binding = arena.BindingAt(command.MaskContentIndex);
            SvgFilterResource resource = arena.ResourceAt(binding.ResourceIndex);
            if (binding.Transform.B != 0 || binding.Transform.C != 0)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filters under rotated or skewed placement are not implemented.");
            int end = first, depth = 0, nestedDepth = -1;
            SvgPaintBounds? nestedSourceBounds = null;
            for (; end < root.Commands.Length; end++)
            {
                if (workUnits == options.Limits.MaximumWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Filter source traversal exceeds the work limit.");
                workUnits++;
                cancellationToken.ThrowIfCancellationRequested();
                SvgDrawCommand item = root.Commands[end];
                if (item.Kind == SvgDrawCommandKind.EndGroup)
                {
                    if (depth-- == 0) break;
                    if (nestedDepth == depth) nestedDepth = -1;
                }
                if (item.Kind == SvgDrawCommandKind.BeginGroup)
                {
                    if (item.RegionKind == SvgLayerRegionKind.Filter && nestedDepth < 0)
                    {
                        // A filtered child's output, including its halo or displaced ink,
                        // is this source's input. Descendants cannot escape that child's
                        // final filter region. Keep object bounds independent of effects.
                        SvgFilterBinding child = arena.BindingAt(item.MaskContentIndex);
                        ParserWorkCounter childRegionWork = new(options.Limits.MaximumWorkUnits - workUnits);
                        SvgPaintBounds childRegion = FilterRegion(child, arena.ResourceAt(child.ResourceIndex), childRegionWork);
                        workUnits += childRegionWork.Used;
                        if (childRegion.Right > childRegion.Left && childRegion.Bottom > childRegion.Top)
                            nestedSourceBounds = nestedSourceBounds is {} previous
                                ? new(MathF.Min(previous.Left, childRegion.Left), MathF.Min(previous.Top, childRegion.Top), MathF.Max(previous.Right, childRegion.Right), MathF.Max(previous.Bottom, childRegion.Bottom))
                                : childRegion;
                        nestedDepth = depth;
                    }
                    depth++;
                }
            }
            if (end == root.Commands.Length) throw new SvgException(SvgErrorCode.MalformedInput, "Filter source scope is unbalanced.");
            ParserWorkCounter regionWork = new(options.Limits.MaximumWorkUnits - workUnits);
            SvgPaintBounds region = FilterRegion(binding, resource, regionWork);
            workUnits += regionWork.Used;
            if (region.Right <= region.Left || region.Bottom <= region.Top) return end + 1;
            double primitiveScaleX = resource.PrimitiveUnits == SvgFilterUnits.ObjectBoundingBox ? binding.ObjectBounds.Right - binding.ObjectBounds.Left : binding.Transform.A;
            double primitiveScaleY = resource.PrimitiveUnits == SvgFilterUnits.ObjectBoundingBox ? binding.ObjectBounds.Bottom - binding.ObjectBounds.Top : binding.Transform.D;
            SvgFilterDeviceMap primitiveMap = new(primitiveScaleX * horizontalScale, primitiveScaleY * verticalScale);
            SvgPaintBounds visual = binding.VisualBounds;
            double sourceLeft = Math.Floor(visual.Left * horizontalScale + horizontalOffset), sourceTop = Math.Floor(visual.Top * verticalScale + verticalOffset);
            double sourceRight = Math.Ceiling(visual.Right * horizontalScale + horizontalOffset), sourceBottom = Math.Ceiling(visual.Bottom * verticalScale + verticalOffset);
            if (nestedSourceBounds is {} nested)
            {
                sourceLeft = Math.Min(sourceLeft, Math.Floor(nested.Left)); sourceTop = Math.Min(sourceTop, Math.Floor(nested.Top));
                sourceRight = Math.Max(sourceRight, Math.Ceiling(nested.Right)); sourceBottom = Math.Max(sourceBottom, Math.Ceiling(nested.Bottom));
            }
            if (TryRenderOffsetSources()) return end + 1;
            double paddingX = 0, paddingY = 0;
            double offsetPaddingX = 0, offsetPaddingY = 0;
            foreach (SvgFilterPrimitive primitive in resource.Primitives.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (workUnits == options.Limits.MaximumWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Filter extent traversal exceeds the work limit.");
                workUnits++;
                paddingX += Math.Ceiling(Math.Abs(primitive.SigmaX * primitiveMap.ScaleX) * 4) + Math.Ceiling(Math.Abs(primitive.OffsetX * primitiveMap.ScaleX));
                paddingY += Math.Ceiling(Math.Abs(primitive.SigmaY * primitiveMap.ScaleY) * 4) + Math.Ceiling(Math.Abs(primitive.OffsetY * primitiveMap.ScaleY));
                offsetPaddingX += Math.Ceiling(Math.Abs(primitive.OffsetX * primitiveMap.ScaleX));
                offsetPaddingY += Math.Ceiling(Math.Abs(primitive.OffsetY * primitiveMap.ScaleY));
            }
            double left = sourceLeft, top = sourceTop, right = sourceRight, bottom = sourceBottom;
            // The source grid only needs the captured image and requested output.
            // Kernel samples beyond this union are transparent; allocating the
            // entire halo makes large blurs needlessly exhaust work and storage.
            double requestedLeft = Math.Max(region.Left, layerBounds.Left), requestedTop = Math.Max(region.Top, layerBounds.Top);
            double requestedRight = Math.Min(region.Right, layerBounds.Right), requestedBottom = Math.Min(region.Bottom, layerBounds.Bottom);
            if (activeViewportBounds is {} filterClip)
            {
                requestedLeft = Math.Max(requestedLeft, filterClip.Left); requestedTop = Math.Max(requestedTop, filterClip.Top);
                requestedRight = Math.Min(requestedRight, filterClip.Right); requestedBottom = Math.Min(requestedBottom, filterClip.Bottom);
            }
            if (requestedRight <= requestedLeft || requestedBottom <= requestedTop) return end + 1;
            if (right + paddingX < requestedLeft || bottom + paddingY < requestedTop || left - paddingX > requestedRight || top - paddingY > requestedBottom) return end + 1;
            if (activeViewportBounds is not null || nestedSourceBounds is not null)
            {
                bool objectSourceUnits = resource.PrimitiveUnits == SvgFilterUnits.ObjectBoundingBox;
                SvgAffineTransform sourceCoordinates = objectSourceUnits
                    ? SvgAffineTransform.FromScaleOffset(primitiveMap.ScaleX, primitiveMap.ScaleY,
                        binding.ObjectBounds.Left * horizontalScale + horizontalOffset, binding.ObjectBounds.Top * verticalScale + verticalOffset)
                    : SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset).ComposeChecked(binding.Transform);
                SvgFilterRegionContext sourceContext = new(sourceCoordinates,
                    objectSourceUnits ? new(1, 1) : new(binding.LengthBases.Width ?? width, binding.LengthBases.Height ?? height), region);
                ParserWorkCounter sourceRequestWork = new(options.Limits.MaximumWorkUnits - workUnits);
                SvgPaintBounds needed = SvgFilterPlayback.RequiredSourceBounds(resource, primitiveMap, sourceContext,
                    new((float)requestedLeft, (float)requestedTop, (float)requestedRight, (float)requestedBottom), budget, sourceRequestWork, cancellationToken);
                workUnits += sourceRequestWork.Used;
                double captureLeft = Math.Max(left, needed.Left), captureTop = Math.Max(top, needed.Top);
                double captureRight = Math.Min(right, needed.Right), captureBottom = Math.Min(bottom, needed.Bottom);
                if (captureRight > captureLeft && captureBottom > captureTop)
                    (left, top, right, bottom) = (captureLeft, captureTop, captureRight, captureBottom);
            }
            // Shifted and chained results may feed visible pixels from outside the
            // final output window. A single blur needs only the complete source;
            // graphs retain conservative intermediate halo/shift allowances.
            double replayPaddingX = resource.Primitives.Items.Length > 1 ? paddingX : offsetPaddingX;
            double replayPaddingY = resource.Primitives.Items.Length > 1 ? paddingY : offsetPaddingY;
            double frameLeft = Math.Floor(Math.Min(left, requestedLeft - replayPaddingX)), frameTop = Math.Floor(Math.Min(top, requestedTop - replayPaddingY));
            double frameWidth = Math.Ceiling(Math.Max(right, requestedRight + replayPaddingX)) - frameLeft;
            double frameHeight = Math.Ceiling(Math.Max(bottom, requestedBottom + replayPaddingY)) - frameTop;
            if (right <= left || bottom <= top) return end + 1;
            if (!double.IsFinite(frameWidth) || !double.IsFinite(frameHeight) || frameWidth > options.Limits.MaximumDimension || frameHeight > options.Limits.MaximumDimension
                || frameWidth * frameHeight > options.Limits.MaximumPixels || frameWidth * frameHeight > int.MaxValue / 4)
                throw new SvgException(SvgErrorCode.LimitExceeded, "Filter source and halo exceed the configured surface limits.");
            int fw = (int)frameWidth, fh = (int)frameHeight, sw = (int)(right - left), sh = (int)(bottom - top);
            bool objectPrimitiveUnits = resource.PrimitiveUnits == SvgFilterUnits.ObjectBoundingBox;
            SvgAffineTransform primitiveCoordinates = objectPrimitiveUnits
                ? SvgAffineTransform.FromScaleOffset(primitiveMap.ScaleX, primitiveMap.ScaleY,
                    binding.ObjectBounds.Left * horizontalScale + horizontalOffset - frameLeft,
                    binding.ObjectBounds.Top * verticalScale + verticalOffset - frameTop)
                : SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset - frameLeft, verticalOffset - frameTop).ComposeChecked(binding.Transform);
            SvgPaintBounds gridRegion = new((float)(region.Left - frameLeft), (float)(region.Top - frameTop),
                (float)(region.Right - frameLeft), (float)(region.Bottom - frameTop));
            primitiveMap = primitiveMap with { Regions = new(primitiveCoordinates,
                objectPrimitiveUnits ? new(1, 1) : new(binding.LengthBases.Width ?? width, binding.LengthBases.Height ?? height), gridRegion)
                { RasterPlacement = new(new((int)frameLeft, (int)frameTop), rasterGrid, layerBounds), SourceBounds = new((float)(left - frameLeft), (float)(top - frameTop), (float)(right - frameLeft), (float)(bottom - frameTop)), IntermediateOutput = surfaceMode == RasterSurfaceMode.PremultipliedFilter } };
            long copyWork = (long)sw * sh * 4 + end - first;
            if (copyWork >= options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Filter source capture exceeds the work limit.");
            workUnits += copyWork;
            using RenderBudget.BudgetReservation commandsOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(end - first), SvgMemoryCategory.Scene);
            SvgDrawCommand[] sourceCommands = new SvgDrawCommand[end - first];
            for (int index = first; index < end; index++) { cancellationToken.ThrowIfCancellationRequested(); sourceCommands[index - first] = root.Commands[index]; }
            SvgRenderLimits limits = options.Limits;
            SvgRenderLimits sourceLimits = new(limits.MaximumAccountedBytes, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels, limits.MaximumEncodedPngBytes,
                limits.MaximumNodes, limits.MaximumPathCommands, checked((int)(limits.MaximumWorkUnits - workUnits)), limits.MaximumXmlDepth, limits.MaximumAttributesPerElement);
            SvgRenderOptions sourceOptions = new(options.Viewport, options.OutputScale, SvgColor.Transparent, sourceLimits, options.FontSnapshot);
            SvgRoot borrowed = root with { Commands = sourceCommands, ParserWorkUnits = 0 };
            using RasterizedSurface captured = Rasterize(borrowed, sourceOptions, budget, RasterSurfaceMode.PremultipliedFilter,
                new RasterFrame(sw + 2, sh + 2, horizontalScale, horizontalOffset - left + 1, verticalScale, verticalOffset - top + 1)
                { SingleBitmap = true, ContentClip = new(1, 1, sw + 1, sh + 1), ParentOrigin = new(0, 0), CaptureOrigin = new((int)left - 1, (int)top - 1) }, cancellationToken);
            workUnits += captured.WorkUnits;
            int frameBytes = fw * fh * 4;
            using RenderBudget.BudgetReservation frameOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<byte>(frameBytes), SvgMemoryCategory.Surface);
            byte[] source = new byte[frameBytes];
            int px = (int)(left - frameLeft), py = (int)(top - frameTop);
            for (int row = 0; row < sh; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                captured.Pixels.AsSpan(((row + 1) * (sw + 2) + 1) * 4, sw * 4).CopyTo(source.AsSpan(((row + py) * fw + px) * 4, sw * 4));
            }
            using SvgFilterResult filtered = SvgFilterPlayback.Apply(source, fw, fh, resource, primitiveMap, budget, limits.MaximumWorkUnits - workUnits, cancellationToken);
            workUnits += filtered.WorkUnits;
            int restoreLeft = Math.Max(layerBounds.Left, ClampFloor(Math.Max(frameLeft, region.Left), outputWidth));
            int restoreTop = Math.Max(layerBounds.Top, ClampFloor(Math.Max(frameTop, region.Top), outputHeight));
            int restoreRight = Math.Min(layerBounds.Right, ClampCeiling(Math.Min(frameLeft + fw, region.Right), outputWidth));
            int restoreBottom = Math.Min(layerBounds.Bottom, ClampCeiling(Math.Min(frameTop + fh, region.Bottom), outputHeight));
            long restoreWork = (long)Math.Max(0, restoreRight - restoreLeft) * Math.Max(0, restoreBottom - restoreTop);
            if (restoreWork > limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Filter restore exceeds the work limit.");
            workUnits += restoreWork;
            for (int y = restoreTop; y < restoreBottom; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = restoreLeft; x < restoreRight; x++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int target = (y * outputWidth + x) * 4, origin = ((int)(y - frameTop) * fw + (int)(x - frameLeft)) * 4;
                    float opacity = command.SourceOpacity.Value * foldedOpacity;
                    if (filtered.Samples is {} shaderSamples)
                    {
                        SvgShaderPaint sample = shaderSamples[origin / 4];
                        new SvgShaderPaint(sample.Red * opacity, sample.Green * opacity, sample.Blue * opacity, sample.Alpha * opacity)
                            .Blend(destination.AsSpan(target, 4), activeViewportClip.Length == 0 ? (byte)255 : activeViewportClip[target + 3]);
                        continue;
                    }
                    // A stored filter image uses native packed-lane alpha modulation;
                    // unquantized shader results above retain their separate float route.
                    int opacityScale = Math.Clamp((int)MathF.Floor(opacity * 255 + .5f), 0, 255) + 1;
                    if (activeViewportClip.Length != 0)
                    {
                        byte clip = activeViewportClip[target + 3];
                        if (clip != 0) BlendClippedLayerPixel(destination.AsSpan(target, 4), filtered.Pixels.AsSpan(origin, 4), (byte)(opacityScale - 1), 255, clip);
                        continue;
                    }
                    int red = filtered.Pixels[origin] * opacityScale >> 8, green = filtered.Pixels[origin + 1] * opacityScale >> 8;
                    int blue = filtered.Pixels[origin + 2] * opacityScale >> 8, alpha = filtered.Pixels[origin + 3] * opacityScale >> 8;
                    BlendPremultipliedOver(destination.AsSpan(target, 4), red, green, blue, alpha);
                }
            }
            return end + 1;

            bool TryRenderOffsetSources()
            {
                ReadOnlySpan<SvgFilterPrimitive> primitives = resource.Primitives.Items;
                if (primitives.Length == 0) return false;
                for (int index = 0; index < primitives.Length; index++)
                {
                    SvgFilterPrimitive primitive = primitives[index];
                    if (primitive.Kind == SvgFilterPrimitiveKind.Offset
                        && (primitive.OffsetX * primitiveMap.ScaleX != Math.Truncate(primitive.OffsetX * primitiveMap.ScaleX)
                            || primitive.OffsetY * primitiveMap.ScaleY != Math.Truncate(primitive.OffsetY * primitiveMap.ScaleY))) return false;
                    if (primitive.Region is not null || (primitive.Kind != SvgFilterPrimitiveKind.Offset
                        && (index != primitives.Length - 1 || primitive.Kind != SvgFilterPrimitiveKind.Merge))) return false;
                }
                ParserWorkCounter planWork = new(options.Limits.MaximumWorkUnits - workUnits);
                using BudgetedParserList<SvgOffsetFilterPlacement> placements = new(budget, SvgMemoryCategory.Temporary, planWork, cancellationToken);
                SvgFilterPrimitive final = primitives[^1];
                if (final.Kind == SvgFilterPrimitiveKind.Merge)
                    foreach (SvgFilterInput input in resource.MergeInputs.Items.AsSpan(final.MergeFirst, final.MergeCount)) Add(input);
                else Add(new(primitives.Length - 1));
                workUnits += planWork.Used;
                if (placements.Count == 0) return true;
                using RenderBudget.BudgetReservation commandOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(end - first), SvgMemoryCategory.Scene);
                SvgDrawCommand[] draws = new SvgDrawCommand[end - first];
                Charge(draws.Length);
                for (int index = first; index < end; index++) draws[index - first] = root.Commands[index];
                SvgRoot scene = root with { Commands = draws, ParserWorkUnits = 0 };
                // The native saveLayer hint includes drawable source and filtered
                // output bounds. A branch without source ink may still enlarge the
                // shared capture; pruning it changes clipped curve construction.
                double captureLeft = sourceLeft, captureTop = sourceTop, captureRight = sourceRight, captureBottom = sourceBottom;
                for (int placementIndex = 0; placementIndex < placements.Count; placementIndex++)
                {
                    SvgOffsetFilterPlacement placement = placements[placementIndex];
                    Charge(1);
                    double x1 = Math.Max(sourceLeft + placement.X, placement.Clip.Left), y1 = Math.Max(sourceTop + placement.Y, placement.Clip.Top);
                    double x2 = Math.Min(sourceRight + placement.X, placement.Clip.Right), y2 = Math.Min(sourceBottom + placement.Y, placement.Clip.Bottom);
                    if (x2 <= x1 || y2 <= y1) continue;
                    captureLeft = Math.Min(captureLeft, x1); captureTop = Math.Min(captureTop, y1);
                    captureRight = Math.Max(captureRight, x2); captureBottom = Math.Max(captureBottom, y2);
                }
                SvgAnalyticClipBounds requested = new(Math.Max(layerBounds.Left, ClampFloor(region.Left, outputWidth)), Math.Max(layerBounds.Top, ClampFloor(region.Top, outputHeight)),
                    Math.Min(layerBounds.Right, ClampCeiling(region.Right, outputWidth)), Math.Min(layerBounds.Bottom, ClampCeiling(region.Bottom, outputHeight)));
                foreach (SvgCpuRasterContext tile in rasterGrid.GetContexts(requested))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double left = double.PositiveInfinity, top = double.PositiveInfinity, right = double.NegativeInfinity, bottom = double.NegativeInfinity;
                    int active = 0;
                    for (int placementIndex = 0; placementIndex < placements.Count; placementIndex++)
                    {
                        SvgOffsetFilterPlacement placement = placements[placementIndex];
                        Charge(1);
                        double x1 = Math.Max(captureLeft, Math.Max(tile.BitmapClip.Left, placement.Clip.Left) - placement.X);
                        double y1 = Math.Max(captureTop, Math.Max(tile.BitmapClip.Top, placement.Clip.Top) - placement.Y);
                        double x2 = Math.Min(captureRight, Math.Min(tile.BitmapClip.Right, placement.Clip.Right) - placement.X);
                        double y2 = Math.Min(captureBottom, Math.Min(tile.BitmapClip.Bottom, placement.Clip.Bottom) - placement.Y);
                        if (x2 <= x1 || y2 <= y1) continue;
                        active++;
                        left = Math.Min(left, x1); top = Math.Min(top, y1); right = Math.Max(right, x2); bottom = Math.Max(bottom, y2);
                    }
                    if (active == 0) continue;
                    left = Math.Floor(left) - 1; top = Math.Floor(top) - 1; right = Math.Ceiling(right) + 1; bottom = Math.Ceiling(bottom) + 1;
                    SvgRenderLimits limits = options.Limits;
                    double sourceWidth = right - left, sourceHeight = bottom - top;
                    if (sourceWidth > limits.MaximumDimension || sourceHeight > limits.MaximumDimension
                        || sourceWidth * sourceHeight > limits.MaximumPixels || sourceWidth * sourceHeight > int.MaxValue / 4
                        || left < int.MinValue || left > int.MaxValue || top < int.MinValue || top > int.MaxValue)
                        throw new SvgException(SvgErrorCode.LimitExceeded, "Offset source capture exceeds the configured surface limits.");
                    long remaining = limits.MaximumWorkUnits - workUnits;
                    if (remaining <= 0) throw new SvgException(SvgErrorCode.LimitExceeded, "Offset source capture exceeds the configured work limit.");
                    SvgRenderLimits captureLimits = new(limits.MaximumAccountedBytes, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels,
                        limits.MaximumEncodedPngBytes, limits.MaximumNodes, limits.MaximumPathCommands, checked((int)remaining), limits.MaximumXmlDepth, limits.MaximumAttributesPerElement);
                    SvgRenderOptions captureOptions = new(options.Viewport, options.OutputScale, SvgColor.Transparent, captureLimits, options.FontSnapshot);
                    using RasterizedSurface captured = Rasterize(scene, captureOptions, budget, RasterSurfaceMode.PremultipliedFilter,
                        new RasterFrame((int)sourceWidth, (int)sourceHeight, horizontalScale, horizontalOffset - left, verticalScale, verticalOffset - top)
                        { SingleBitmap = true, ContentClip = new(1, 1, (int)sourceWidth - 1, (int)sourceHeight - 1), ParentOrigin = tile.Origin, CaptureOrigin = new((int)left, (int)top) }, cancellationToken);
                    Charge(captured.WorkUnits);
                    int tw = tile.BitmapClip.Right - tile.BitmapClip.Left, th = tile.BitmapClip.Bottom - tile.BitmapClip.Top;
                    using RenderBudget.BudgetReservation tileOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<byte>(checked(tw * th * 4)), SvgMemoryCategory.Surface);
                    byte[] pixels = new byte[tw * th * 4];
                    bool linear = placements.Count > 1 && resource.ColorSpace == SvgFilterColorSpace.LinearRgb;
                    for (int placementIndex = 0; placementIndex < placements.Count; placementIndex++)
                    {
                        SvgOffsetFilterPlacement placement = placements[placementIndex];
                        int x1 = Math.Max(tile.BitmapClip.Left, (int)Math.Max(left + placement.X, Math.Floor(placement.Clip.Left)));
                        int y1 = Math.Max(tile.BitmapClip.Top, (int)Math.Max(top + placement.Y, Math.Floor(placement.Clip.Top)));
                        int x2 = Math.Min(tile.BitmapClip.Right, (int)Math.Min(right + placement.X, Math.Ceiling(placement.Clip.Right)));
                        int y2 = Math.Min(tile.BitmapClip.Bottom, (int)Math.Min(bottom + placement.Y, Math.Ceiling(placement.Clip.Bottom)));
                        Charge((long)Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1));
                        for (int y = y1; y < y2; y++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            for (int x = x1; x < x2; x++)
                            {
                                int sourceIndex = ((int)(y - top - placement.Y) * captured.Width + (int)(x - left - placement.X)) * 4;
                                const float byteScale = 1f / 255f;
                                SvgShaderPaint paint = placement.AlphaOnly ? new(0, 0, 0, captured.Pixels[sourceIndex + 3] * byteScale)
                                    : new(captured.Pixels[sourceIndex] * byteScale, captured.Pixels[sourceIndex + 1] * byteScale,
                                        captured.Pixels[sourceIndex + 2] * byteScale, captured.Pixels[sourceIndex + 3] * byteScale);
                                if (linear) paint = SvgFilterColorTransfer.Convert(paint, SvgFilterColorSpace.LinearRgb);
                                paint.Blend(pixels.AsSpan(((y - tile.BitmapClip.Top) * tw + x - tile.BitmapClip.Left) * 4, 4), 255);
                            }
                        }
                    }
                    int restoreLeft = Math.Max(requested.Left, tile.Interior.Left), restoreTop = Math.Max(requested.Top, tile.Interior.Top);
                    int restoreRight = Math.Min(requested.Right, tile.Interior.Right), restoreBottom = Math.Min(requested.Bottom, tile.Interior.Bottom);
                    Charge((long)Math.Max(0, restoreRight - restoreLeft) * Math.Max(0, restoreBottom - restoreTop));
                    for (int y = restoreTop; y < restoreBottom; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        for (int x = restoreLeft; x < restoreRight; x++)
                        {
                            int p = ((y - tile.BitmapClip.Top) * tw + x - tile.BitmapClip.Left) * 4;
                            const float byteScale = 1f / 255f;
                            SvgShaderPaint paint = new(pixels[p] * byteScale, pixels[p + 1] * byteScale, pixels[p + 2] * byteScale, pixels[p + 3] * byteScale);
                            if (linear) paint = SvgFilterColorTransfer.Convert(paint, SvgFilterColorSpace.Srgb);
                            float opacity = command.SourceOpacity.Value * foldedOpacity;
                            new SvgShaderPaint(paint.Red * opacity, paint.Green * opacity, paint.Blue * opacity, paint.Alpha * opacity)
                                .Blend(destination.AsSpan((y * outputWidth + x) * 4, 4), activeViewportClip.Length == 0 ? (byte)255 : activeViewportClip[(y * outputWidth + x) * 4 + 3]);
                        }
                    }
                }
                return true;

                void Add(SvgFilterInput input)
                {
                    double dx = 0, dy = 0;
                    SvgPaintBounds clip = region;
                    while (input.Index >= 0)
                    {
                        planWork.Consume(cancellationToken);
                        if ((uint)input.Index >= (uint)resource.Primitives.Items.Length) throw new SvgException(SvgErrorCode.MalformedInput, "Offset input lies outside its graph.");
                        SvgFilterPrimitive primitive = resource.Primitives.Items[input.Index];
                        clip = new((float)Math.Max(clip.Left, region.Left + dx), (float)Math.Max(clip.Top, region.Top + dy),
                            (float)Math.Min(clip.Right, region.Right + dx), (float)Math.Min(clip.Bottom, region.Bottom + dy));
                        dx += primitive.OffsetX * primitiveMap.ScaleX; dy += primitive.OffsetY * primitiveMap.ScaleY;
                        if (!double.IsFinite(dx) || !double.IsFinite(dy) || Math.Abs(dx) > int.MaxValue || Math.Abs(dy) > int.MaxValue)
                            throw new SvgException(SvgErrorCode.LimitExceeded, "Offset chain exceeds the supported coordinate range.");
                        if (dx != Math.Truncate(dx) || dy != Math.Truncate(dy)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fractional filter offsets are not implemented.");
                        input = primitive.Input;
                    }
                    placements.Add(new(dx, dy, input == SvgFilterInput.SourceAlpha, clip));
                }

                void Charge(long amount)
                {
                    if (amount > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Offset source replay exceeds the configured work limit.");
                    workUnits += amount;
                }
            }

        }

        // The recipe-3 page paints its opaque HTML and body backgrounds separately.
        // The matched tile analyzer permits five draws, including those backgrounds.
        // Analyze source draw commands, never inferred uniformity of the raster bitmap.
        bool TryResolveSolidCanvas(out SvgColor color)
        {
            const int maximumDraws = 5;
            color = default;
            int drawCount = options.BackgroundColor.Alpha == 255 ? 2 : 0;
            if (options.BackgroundColor.Alpha is not (0 or 255) || root.Commands.Length == 0 || root.Commands.Length > maximumDraws - drawCount) return false;
            SvgDrawCommand first = root.Commands[0];
            if (first.Kind != SvgDrawCommandKind.Geometry || (uint)first.GeometryIndex >= (uint)root.Geometries.Length || root.Geometries[first.GeometryIndex].SolidPaint is null) return false;
            float red = options.BackgroundColor.Red / 255f;
            float green = options.BackgroundColor.Green / 255f;
            float blue = options.BackgroundColor.Blue / 255f;
            float alpha = options.BackgroundColor.Alpha / 255f;
            bool isSolid = true;
            foreach (SvgDrawCommand command in root.Commands)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (workUnits == options.Limits.MaximumWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Solid paint analysis exceeds the configured work limit.");
                workUnits++;
                if (command.Kind != SvgDrawCommandKind.Geometry || (uint)command.GeometryIndex >= (uint)root.Geometries.Length) return false;
                SvgGeometry geometry = root.Geometries[command.GeometryIndex];
                if (geometry.Kind != SvgGeometryKind.Rectangle || geometry.SolidPaint is not SvgSolidPaint paint || geometry.Rectangle.Stroke is not null) return false;
                if (geometry.RecordedPaint.Identity != 0) paint = paint with { Alpha = GetRecordedPaintValue(geometry.RecordedPaint).Alpha };
                SvgRectangle rectangle = geometry.Rectangle;
                if (!rectangle.HasFill || paint.Alpha == 0 || rectangle.Width == 0 || rectangle.Height == 0) continue;
                if (++drawCount > maximumDraws) return false;
                double left = MapEdge(rectangle.X, horizontalScale, horizontalOffset);
                double top = MapEdge(rectangle.Y, verticalScale, verticalOffset);
                double right = MapEdge(rectangle.X + rectangle.Width, horizontalScale, horizontalOffset);
                double bottom = MapEdge(rectangle.Y + rectangle.Height, verticalScale, verticalOffset);
                if (rectangle.Clip is {} retainedClip && (MapEdge(retainedClip.X, horizontalScale, horizontalOffset) > 0 || MapEdge(retainedClip.Y, verticalScale, verticalOffset) > 0 || MapEdge(retainedClip.Right, horizontalScale, horizontalOffset) < outputWidth || MapEdge(retainedClip.Bottom, verticalScale, verticalOffset) < outputHeight)
                    || left > 0 || top > 0 || right < outputWidth || bottom < outputHeight)
                {
                    isSolid = false;
                    continue;
                }
                if (!isSolid && paint.Alpha != 1) continue;
                float sourceRed = paint.Color.Red / 255f;
                float sourceGreen = paint.Color.Green / 255f;
                float sourceBlue = paint.Color.Blue / 255f;
                if (paint.Alpha == 1)
                {
                    red = sourceRed; green = sourceGreen; blue = sourceBlue; alpha = 1;
                }
                else
                {
                    float outAlpha = paint.Alpha + alpha * (1 - paint.Alpha);
                    float inverse = 1 / outAlpha;
                    float sourceWeight = paint.Alpha * inverse;
                    float destinationWeight = alpha * (1 - paint.Alpha) * inverse;
                    red = sourceRed * sourceWeight + red * destinationWeight;
                    green = sourceGreen * sourceWeight + green * destinationWeight;
                    blue = sourceBlue * sourceWeight + blue * destinationWeight;
                    alpha = outAlpha;
                }
                isSolid = true;
            }
            if (!isSolid) return false;
            color = new SvgColor(Quantize(red), Quantize(green), Quantize(blue), Quantize(alpha));
            return true;

            static byte Quantize(float lane) => (byte)Math.Clamp((int)MathF.Round(lane * 255, MidpointRounding.AwayFromZero), 0, 255);
        }

        static void ApplyCaptureUnpremultiply(byte[] destination, int count, CancellationToken cancellationToken)
        {
            // Match the pinned raster pipeline's byte-to-float load, reciprocal-alpha
            // unpremultiplication and nearest-even RGBA8 store in that order. Integer
            // round-half-up differs at material/coverage ties. Source premultiplication
            // depends on the paint route; all surfaces remain premultiplied through
            // composition and use this shared capture conversion once at output.
            for (int pixel = 0; pixel < count; pixel++)
            {
                if ((pixel & 4095) == 0 && cancellationToken.IsCancellationRequested) cancellationToken.ThrowIfCancellationRequested();
                int offset = pixel * 4;
                int alpha = destination[offset + 3];
                if (alpha <= 0 || alpha >= 255) continue;
                const float byteScale = 1f / 255f;
                float inverseAlpha = 1f / (alpha * byteScale);
                destination[offset] = (byte)Math.Clamp((int)MathF.Round(destination[offset] * byteScale * inverseAlpha * 255f), 0, 255);
                destination[offset + 1] = (byte)Math.Clamp((int)MathF.Round(destination[offset + 1] * byteScale * inverseAlpha * 255f), 0, 255);
                destination[offset + 2] = (byte)Math.Clamp((int)MathF.Round(destination[offset + 2] * byteScale * inverseAlpha * 255f), 0, 255);
            }
        }

        SvgRecordedPaintValue GetRecordedPaintValue(SvgRecordedPaint reference)
        {
            if (reference.Identity == 0) return default;
            if (root.SceneStorage is null || (uint)(reference.Identity - 1) >= (uint)root.SceneStorage.RecordedPaintValues.Length) throw new SvgException(SvgErrorCode.MalformedInput, "Paint reference is outside its owned scene arena.");
            return root.SceneStorage.RecordedPaintValues[reference.Identity - 1];
        }

        SvgColor ResolveSolidColor(SvgColor color)
        {
            if (activePaint.Identity.Value == 0 && foldedOpacity == 1) return color;
            float alpha = (activePaint.Identity.Value == 0 ? color.Alpha / 255f : activePaint.Alpha) * foldedOpacity;
            return new(color.Red, color.Green, color.Blue, (byte)Math.Clamp((int)(alpha * 255f + 0.5f), 0, 255));
        }
        // Picture-backed pattern tiles use a constant raster-pipeline paint. Their rounded
        // premultiplied lanes and coverage scale differ from the ordinary solid blitter.
        SvgConstantShaderPaint PatternSolidPaint(SvgColor color)
            => SvgShaderPaint.ConstantGradient(new SvgGradientStop(0, color) { Alpha = ShaderAlpha(color.Alpha) }, 1);

        SvgAnalyticPaint ResolveSolidPaint(SvgColor color)
            => surfaceMode == RasterSurfaceMode.PremultipliedPattern
                ? SvgAnalyticPaint.Constant(PatternSolidPaint(color))
                : SvgAnalyticPaint.Solid(ResolveSolidColor(color));

        float ShaderAlpha(byte opacity) => (activePaint.Identity.Value == 0 ? opacity / 255f : activePaint.Alpha) * foldedOpacity;

        SvgCpuRasterContext LayerRasterContext(SvgCpuRasterContext tile)
        {
            SvgAnalyticClipBounds bitmap = new(Math.Max(tile.BitmapClip.Left, layerBounds.Left), Math.Max(tile.BitmapClip.Top, layerBounds.Top),
                Math.Min(tile.BitmapClip.Right, layerBounds.Right), Math.Min(tile.BitmapClip.Bottom, layerBounds.Bottom));
            SvgAnalyticClipBounds interior = new(Math.Max(tile.Interior.Left, layerBounds.Left), Math.Max(tile.Interior.Top, layerBounds.Top),
                Math.Min(tile.Interior.Right, layerBounds.Right), Math.Min(tile.Interior.Bottom, layerBounds.Bottom));
            return new(bitmap, interior) { ConstructionOrigin = frame?.ContentClip is not null ? tile.Origin : null };
        }

        SvgCpuBitmapOrigin ShaderBitmapOrigin(int x, int y)
        {
            SvgCpuBitmapOrigin tile = rasterGrid.GetBitmapOrigin(x, y);
            int left = Math.Max(tile.X, layerBounds.Left), top = Math.Max(tile.Y, layerBounds.Top);
            if (layerClipCoverage is {} coverage && coverage.TryGetBounds(tile, out SvgAnalyticClipBounds bitmapBounds))
            {
                left = Math.Max(left, bitmapBounds.Left);
                top = Math.Max(top, bitmapBounds.Top);
            }
            return new(left, top);
        }

        int GradientDither(int x, int y)
        {
            SvgCpuBitmapOrigin origin = ShaderBitmapOrigin(x, y);
            int localX = x - origin.X, localY = y - origin.Y;
            int mixedY = localY ^ localX;
            return ((mixedY & 1) << 5) | ((localX & 1) << 4)
                | ((mixedY & 2) << 2) | ((localX & 2) << 1)
                | ((mixedY & 4) >> 1) | ((localX & 4) >> 2);
        }

        SvgAnalyticClipBounds LayerPaintBounds(int first, SvgAnalyticClipBounds parent, SvgClipBox? retainedRegion)
        {
            ParserWorkCounter boundsWork = new(options.Limits.MaximumWorkUnits - workUnits);
            double left = double.PositiveInfinity, top = double.PositiveInfinity;
            double right = double.NegativeInfinity, bottom = double.NegativeInfinity;
            SvgAffineTransform outputMap = SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset);
            int depth = 0;
            // Content and mask bounds both contribute to the isolated layer;
            // the mask region clips the mask surface during composition.
            if (retainedRegion is SvgClipBox region) IncludeBox(new(region.X, region.Y, region.Width, region.Height, default, false, null), outputMap, 0);
            for (int cursor = first; cursor < root.Commands.Length; cursor++)
            {
                boundsWork.Consume(cancellationToken);
                SvgDrawCommand value = root.Commands[cursor];
                if (value.Kind == SvgDrawCommandKind.BeginGroup)
                {
                    if (value.RegionKind == SvgLayerRegionKind.Filter && root.SceneStorage?.Filters is {} filters)
                    {
                        SvgFilterBinding binding = filters.BindingAt(value.MaskContentIndex);
                        SvgPaintBounds filtered = FilterRegion(binding, filters.ResourceAt(binding.ResourceIndex), boundsWork);
                        left = Math.Min(left, filtered.Left); top = Math.Min(top, filtered.Top);
                        right = Math.Max(right, filtered.Right); bottom = Math.Max(bottom, filtered.Bottom);
                    }
                    if (value.RegionKind == SvgLayerRegionKind.Mask && value.MaskContentIndex >= 0 && value.MaskRegion is {} spatialRegion)
                        IncludeBox(new(spatialRegion.X, spatialRegion.Y, spatialRegion.Width, spatialRegion.Height, default, false, null), outputMap, 0);
                    depth++; continue;
                }
                if (value.Kind == SvgDrawCommandKind.EndGroup)
                {
                    if (depth-- == 0) break;
                    continue;
                }
                SvgGeometry geometry = root.Geometries[value.GeometryIndex];
                SvgPaintBounds? visualBounds = GetRecordedPaintValue(geometry.RecordedPaint).VisualBounds;
                if (visualBounds is {} visual)
                {
                    IncludeBox(new(visual.Left, visual.Top, visual.Right - visual.Left, visual.Bottom - visual.Top, default, false, null), outputMap, 0);
                    continue;
                }
                SvgAuthoredPath? source = geometry.AuthoredPath?.StrokeOutline ?? geometry.AuthoredPath;
                if (source is not null)
                {
                    SvgRectangle box = SvgPathBounds.Compute(source.Commands, boundsWork, cancellationToken);
                    // SVG visual bounds enclose source ink before the layer's device map.
                    double sourceRight = Math.Ceiling(box.X + box.Width), sourceBottom = Math.Ceiling(box.Y + box.Height);
                    box = box with { X = Math.Floor(box.X), Y = Math.Floor(box.Y), Width = sourceRight - Math.Floor(box.X), Height = sourceBottom - Math.Floor(box.Y) };
                    IncludeBox(box, outputMap.ComposeChecked(source.SceneMap), 0);
                    continue;
                }
                switch (geometry.Kind)
                {
                    case SvgGeometryKind.EmbeddedImage:
                        if (geometry.Image.Box.HasFill) IncludeBox(geometry.Image.Box, outputMap, 0);
                        break;
                    case SvgGeometryKind.Rectangle:
                    case SvgGeometryKind.OrthogonalBand:
                        SvgRectangle rect = geometry.Rectangle;
                        if (rect.HasFill || rect.Stroke is not null) IncludeBox(rect, outputMap, rect.Stroke?.Width / 2d ?? 0);
                        break;
                    case SvgGeometryKind.GradientFill:
                        if (geometry.Gradient.Fill.HasFill || geometry.Gradient.Stroke is not null)
                        {
                            IncludeBox(geometry.Gradient.Box, outputMap, geometry.Gradient.Stroke?.Width / 2d ?? 0);
                            IncludeBox(geometry.Gradient.Fill, outputMap, geometry.Gradient.Stroke?.Width / 2d ?? 0);
                        }
                        break;
                    case SvgGeometryKind.RadialGradient:
                        if (geometry.Radial.Fill.HasFill || geometry.Radial.Stroke is not null)
                        {
                            IncludeBox(geometry.Radial.Box, outputMap, geometry.Radial.Stroke?.Width / 2d ?? 0);
                            IncludeBox(geometry.Radial.Fill, outputMap, geometry.Radial.Stroke?.Width / 2d ?? 0);
                        }
                        break;
                    case SvgGeometryKind.PatternFill:
                        if (geometry.Pattern.Fill.HasFill || geometry.Pattern.Stroke is not null)
                            IncludeBox(geometry.Pattern.Fill, outputMap, geometry.Pattern.Stroke?.Width / 2d ?? 0);
                        break;
                    case SvgGeometryKind.GradientStroke:
                        IncludeBox(geometry.GradientStroke.OuterBox(), outputMap, 0);
                        break;
                    case SvgGeometryKind.AxisAlignedLine:
                        SvgAxisAlignedLine line = geometry.AxisAlignedLine;
                        if (line.IsPainted)
                            IncludeBox(new(Math.Min(line.X1,line.X2), Math.Min(line.Y1,line.Y2), Math.Abs(line.X2-line.X1), Math.Abs(line.Y2-line.Y1), default, true, null), outputMap, line.StrokeWidth / 2d);
                        break;
                    case SvgGeometryKind.PolygonFill:
                        IncludePoints(geometry.Polygon.Points);
                        break;
                    case SvgGeometryKind.GradientPolygon:
                        IncludePoints(geometry.GradientPolygon.Points);
                        break;
                    case SvgGeometryKind.RadialPolygon:
                        IncludePoints(geometry.RadialPolygon.Points);
                        break;
                    case SvgGeometryKind.PatternPolygon:
                        IncludePoints(geometry.PatternPolygon.Points);
                        break;
                    case SvgGeometryKind.GlyphContours:
                        foreach (SvgGlyphQuadraticContour contour in geometry.Glyph.Contours)
                        foreach (SvgGlyphQuadraticSegment segment in contour.Segments)
                        {
                            if (geometry.Glyph.StrokeBoundsInflation > 0)
                            {
                                double inflation = geometry.Glyph.StrokeBoundsInflation;
                                IncludeBox(new(segment.Start.X, segment.Start.Y, 0, 0, default, false, null), outputMap, inflation);
                                IncludeBox(new(segment.Control.X, segment.Control.Y, 0, 0, default, false, null), outputMap, inflation);
                                IncludeBox(new(segment.End.X, segment.End.Y, 0, 0, default, false, null), outputMap, inflation);
                            }
                            else
                            {
                                Include(new(segment.Start.X, segment.Start.Y), outputMap);
                                Include(new(segment.Control.X, segment.Control.Y), outputMap);
                                Include(new(segment.End.X, segment.End.Y), outputMap);
                            }
                        }
                        break;
                    default:
                        throw new SvgException(SvgErrorCode.MalformedInput, "Layer references invalid geometry.");
                }
            }
            workUnits += boundsWork.Used;
            if (double.IsPositiveInfinity(left)) return parent;
            return new((int)Math.Clamp(Math.Floor(left), parent.Left, parent.Right),
                (int)Math.Clamp(Math.Floor(top), parent.Top, parent.Bottom),
                (int)Math.Clamp(Math.Ceiling(right), parent.Left, parent.Right),
                (int)Math.Clamp(Math.Ceiling(bottom), parent.Top, parent.Bottom));

            void IncludeBox(SvgRectangle box, SvgAffineTransform map, double inflation)
            {
                Include(new(box.X-inflation, box.Y-inflation), map);
                Include(new(box.X+box.Width+inflation, box.Y-inflation), map);
                Include(new(box.X-inflation, box.Y+box.Height+inflation), map);
                Include(new(box.X+box.Width+inflation, box.Y+box.Height+inflation), map);
            }
            void IncludePoints(ReadOnlySpan<SvgDevicePoint> points)
            {
                foreach (SvgDevicePoint point in points) Include(point, outputMap);
            }
            void Include(SvgDevicePoint point, SvgAffineTransform map)
            {
                boundsWork.Consume(cancellationToken);
                SvgDevicePoint mapped = SvgAuthoredPath.MapPoint(map, point, new(0,0));
                if (!double.IsFinite(mapped.X) || !double.IsFinite(mapped.Y)) throw new SvgException(SvgErrorCode.LimitExceeded, "Layer bounds exceed the admitted numeric range.");
                left = Math.Min(left,mapped.X); top = Math.Min(top,mapped.Y);
                right = Math.Max(right,mapped.X); bottom = Math.Max(bottom,mapped.Y);
            }
        }

        bool TrySinglePaint(int first, SvgRecordingScope scope, out int end)
        {
            SvgPaintOperationIdentity identity = default;
            end = first;
            for (; end < root.Commands.Length; end++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (workUnits >= options.Limits.MaximumWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Paint-record inspection exceeds configured work.");
                workUnits++;
                SvgDrawCommand candidate = root.Commands[end];
                if (candidate.Kind == SvgDrawCommandKind.EndGroup) return identity.Value != 0;
                if (candidate.Kind != SvgDrawCommandKind.Geometry) return false;
                SvgGeometry geometry = root.Geometries[candidate.GeometryIndex];
                SvgRecordedPaintValue paint = GetRecordedPaintValue(geometry.RecordedPaint);
                if (geometry.Kind != SvgGeometryKind.Rectangle && geometry.RecordedStrokePaint.Identity != 0)
                {
                    bool fill = geometry.Kind switch
                    {
                        SvgGeometryKind.GradientFill => geometry.Gradient.Fill.HasFill,
                        SvgGeometryKind.RadialGradient => geometry.Radial.Fill.HasFill,
                        SvgGeometryKind.PatternFill => geometry.Pattern.Fill.HasFill,
                        SvgGeometryKind.GradientStroke => geometry.GradientStroke.Fill.HasFill,
                        _ => throw new SvgException(SvgErrorCode.MalformedInput, "Unexpected shader stroke recording."),
                    };
                    if (fill) return false;
                    paint = GetRecordedPaintValue(geometry.RecordedStrokePaint);
                }
                if (geometry.Kind == SvgGeometryKind.Rectangle)
                {
                    if (!geometry.Rectangle.HasFill)
                    {
                        if (geometry.Rectangle.Stroke is null) continue;
                        paint = GetRecordedPaintValue(geometry.RecordedStrokePaint);
                    }
                    else if (geometry.Rectangle.Stroke is not null) return false;
                }
                else if (geometry.Kind == SvgGeometryKind.PolygonFill && !geometry.Polygon.HasFill) continue;
                else if (geometry.Kind == SvgGeometryKind.OrthogonalBand && !geometry.Rectangle.HasFill) continue;
                if (paint.Identity.Value == 0 || !paint.Foldable || paint.Scope != scope) return false;
                if (identity.Value != 0 && identity != paint.Identity) return false;
                identity = paint.Identity;
            }
            return false;
        }

        int RenderCommands(int startIndex, byte[] destination, bool stopsAtGroupEnd)
        {
            int index = startIndex;
            while (index < root.Commands.Length)
            {
                SvgDrawCommand command = root.Commands[index++];
                if (command.Kind != SvgDrawCommandKind.Geometry) ReleasePatternTile();
                if (command.Kind == SvgDrawCommandKind.EndGroup)
                {
                    if (!stopsAtGroupEnd) throw new SvgException(SvgErrorCode.MalformedInput, "SVG group structure is unbalanced.");
                    return index;
                }
                if (command.Kind == SvgDrawCommandKind.BeginGroup)
                {
                    if (command.RegionKind == SvgLayerRegionKind.Filter)
                    {
                        index = RenderFilter(index, command, destination);
                        continue;
                    }
                    if (command.MaskRegion is null && command.MaskContentIndex < 0 && command.MaskAlpha == 255
                        && TrySinglePaint(index, command.RecordingScope, out int paintEnd))
                    {
                        float previousOpacity = foldedOpacity;
                        foldedOpacity *= command.SourceOpacity.Value;
                        try
                        {
                            for (; index < paintEnd; index++)
                            {
                                nextPatternTile = NextPatternTile(index + 1, paintEnd);
                                RasterizeGeometry(root.Geometries[root.Commands[index].GeometryIndex], destination);
                            }
                        }
                        finally { foldedOpacity = previousOpacity; }
                        index++;
                        continue;
                    }
                    SvgAnalyticClipBounds previousLayer = layerBounds;
                    bool previousMaskClip = maskClipAntialiased;
                    bool viewportClip = command.RegionKind == SvgLayerRegionKind.Viewport && command.MaskRegion is not null;
                    bool affineViewportClip = command.RegionKind == SvgLayerRegionKind.AffineViewport && command.MaskContentIndex >= 0;
                    (double regionLeft, double regionTop, double regionRight, double regionBottom) = command.MaskRegion is SvgClipBox maskRegion ? MapClipBounds(maskRegion, horizontalScale, horizontalOffset, verticalScale, verticalOffset) : (0, 0, outputWidth, outputHeight);
                    SvgAnalyticClipBounds layer = affineViewportClip ? previousLayer : LayerPaintBounds(index, previousLayer, viewportClip ? null : command.MaskRegion);
                    if (activeViewportBounds is {} inheritedBounds)
                    {
                        int left = Math.Max(layer.Left, inheritedBounds.Left), top = Math.Max(layer.Top, inheritedBounds.Top);
                        layer = new(left, top, Math.Max(left, Math.Min(layer.Right, inheritedBounds.Right)), Math.Max(top, Math.Min(layer.Bottom, inheritedBounds.Bottom)));
                    }
                    bool shaderRestore = false;
                    if (viewportClip || affineViewportClip)
                    {
                        const long clipSetupWork = 8;
                        if (clipSetupWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Viewport clip setup exceeds the configured limit.");
                        workUnits += clipSetupWork;
                        int left = Math.Max(layer.Left, ClampFloor(regionLeft, outputWidth));
                        int top = Math.Max(layer.Top, ClampFloor(regionTop, outputHeight));
                        int right = Math.Min(layer.Right, ClampCeiling(regionRight, outputWidth));
                        int bottom = Math.Min(layer.Bottom, ClampCeiling(regionBottom, outputHeight));
                        layer = new(left, top, Math.Max(left, right), Math.Max(top, bottom));
                        var bounds = (Left: regionLeft, Top: regionTop, Right: regionRight, Bottom: regionBottom);
                        SvgClipAntialiasing antialiasing = command.MaskRegion.GetValueOrDefault().Antialiasing;
                        // SkDraw::clipHandlesSprite permits the direct image blitter for a BW
                        // clip or an image wholly contained in opaque AA runs. Otherwise the
                        // image shader shades opacity first, then applies clip coverage.
                        shaderRestore = affineViewportClip || SvgAnalyticRectangleClip.Classify(regionLeft, regionTop, regionRight, regionBottom, previousLayer, antialiasing) == SvgAnalyticPaintClip.Antialiased
                            && (SvgAnalyticRectangleClip.GetCoverage(bounds, antialiasing, layer.Left, layer.Top).Apply(255) != 255
                                || SvgAnalyticRectangleClip.GetCoverage(bounds, antialiasing, layer.Right - 1, layer.Bottom - 1).Apply(255) != 255);
                    }
                    if (affineViewportClip && command.SourceOpacity.Value == 1 && command.MaskAlpha == 255)
                    {
                        if (root.SceneStorage is null || (uint)command.MaskContentIndex >= (uint)root.SceneStorage.MaskContents.Length)
                            throw new SvgException(SvgErrorCode.MalformedInput, "SVG viewport references missing clip content.");
                        using RenderBudget.BudgetReservation clipReservation = budget.Reserve(surfaceByteCount, SvgMemoryCategory.Surface);
                        byte[] clipPixels = new byte[surfaceByteCount];
                        byte[] previousClip = activeViewportClip;
                        bool previousBinary = activeViewportIsBinary;
                        SvgMaskContent clipContent = root.SceneStorage.MaskContents[command.MaskContentIndex];
                        using SvgCpuClipCoverage? clipCoverage = RasterizeMaskContent(clipContent, clipPixels, true, clipContent.ClipKind is SvgClipContentKind.AffineViewport or SvgClipContentKind.BinaryViewport ? layer : previousLayer);
                        if (previousClip.Length != 0 && clipContent.BoundsClipContentIndex < 0)
                        {
                            if (pixelCount > options.Limits.MaximumWorkUnits - workUnits)
                                throw new SvgException(SvgErrorCode.LimitExceeded, "Viewport clip intersection exceeds the configured limit.");
                            workUnits += pixelCount;
                            for (int pixel = 0; pixel < pixelCount; pixel++)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                int offset = pixel * 4 + 3;
                                clipPixels[offset] = (byte)((clipPixels[offset] * previousClip[offset] + 127) / 255);
                            }
                            if (clipCoverage is not null)
                            {
                                clipCoverage.IntersectAndTrim(activeViewportCoverage, previousClip, clipPixels, outputWidth,
                                    options.Limits.MaximumWorkUnits - workUnits, out long intersectionWork, cancellationToken);
                                workUnits += intersectionWork;
                            }
                        }
                        layer = CoverageBounds(clipPixels, layer);
                        SvgAnalyticClipBounds? previousBounds = activeViewportBounds;
                        SvgCpuClipCoverage? previousCoverage = activeViewportCoverage;
                        activeViewportCoverage = clipCoverage;
                        activeViewportClip = clipPixels;
                        activeViewportIsBinary = clipContent.ClipKind == SvgClipContentKind.BinaryViewport && previousBinary;
                        activeViewportBounds = layer;
                        try { index = RenderCommands(index, destination, true); }
                        finally { activeViewportClip = previousClip; activeViewportIsBinary = previousBinary; activeViewportBounds = previousBounds; activeViewportCoverage = previousCoverage; }
                        continue;
                    }
                    using RenderBudget.BudgetReservation groupReservation = budget.Reserve(checked((long)pixelCount * 4), SvgMemoryCategory.Surface);
                    byte[] groupPixels = new byte[checked(pixelCount * 4)];
                    byte[] parentViewportClip = activeViewportClip;
                    activeViewportClip = [];
                    layerBounds = layer;
                    // An inner isolated surface paints before its parent's clip;
                    // folded single paints keep the existing consumer instead.
                    maskClipAntialiased = false;
                    if (command.RegionKind == SvgLayerRegionKind.Mask && command.MaskRegion is {} paintRegion)
                    {
                        const long maskClipSetupWork = 8;
                        if (maskClipSetupWork > options.Limits.MaximumWorkUnits - workUnits)
                            throw new SvgException(SvgErrorCode.LimitExceeded, "Mask paint-clip setup exceeds the configured limit.");
                        workUnits += maskClipSetupWork;
                        // Native mask contents use an enclosing source rectangle as
                        // their paint clip. An AA consumer forces additive runs even
                        // when the shape is wholly contained; the mask paints separately.
                        SvgAffineTransform outputMap = SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset);
                        SvgDevicePoint first = SvgAuthoredPath.MapPoint(outputMap,
                            new(MathF.Floor((float)paintRegion.X), MathF.Floor((float)paintRegion.Y)), new(0, 0));
                        SvgDevicePoint last = SvgAuthoredPath.MapPoint(outputMap,
                            new(MathF.Ceiling((float)(paintRegion.X + paintRegion.Width)), MathF.Ceiling((float)(paintRegion.Y + paintRegion.Height))), new(0, 0));
                        maskClipAntialiased = SvgAnalyticRectangleClip.Classify(first.X, first.Y, last.X, last.Y, previousLayer, SvgClipAntialiasing.All) == SvgAnalyticPaintClip.Antialiased;
                    }
                    try { index = RenderCommands(index, groupPixels, true); }
                    finally { layerBounds = previousLayer; activeViewportClip = parentViewportClip; maskClipAntialiased = previousMaskClip; }
                    long compositionWork = pixelCount;
                    if (compositionWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Group opacity composition exceeds the configured limit.");
                    workUnits += compositionWork;
                    // Skia hard-rectangle clipping rounds each mapped edge to the nearest integer
                    // (Copyright 2006 The Android Open Source Project; see THIRD_PARTY_NOTICES.txt);
                    // half-pixel ties move toward positive infinity on both sides.
                    SvgAnalyticClipBounds maskBounds = new(ClampFloor(regionLeft + 0.5, outputWidth), ClampFloor(regionTop + 0.5, outputHeight),
                        ClampFloor(regionRight + 0.5, outputWidth), ClampFloor(regionBottom + 0.5, outputHeight));
                    byte[] maskSurface = [];
                    byte maskLuminance = 255;
                    bool maskIsAlpha = false;
                    bool variableLuminance = false;
                    bool hasMaskContent = command.MaskContentIndex >= 0;
                    RenderBudget.BudgetReservation? maskReservation = null;
                    if (hasMaskContent)
                    {
                        if (root.SceneStorage is null || (uint)command.MaskContentIndex >= (uint)root.SceneStorage.MaskContents.Length) throw new SvgException(SvgErrorCode.MalformedInput, "SVG group references missing mask content.");
                        SvgMaskContent maskContent = root.SceneStorage.MaskContents[command.MaskContentIndex];
                        maskLuminance = maskContent.Luminance;
                        maskIsAlpha = maskContent.IsAlpha;
                        variableLuminance = maskContent.VectorContent is not null && !maskIsAlpha;
                        maskReservation = budget.Reserve(checked((long)pixelCount * 4), SvgMemoryCategory.Surface);
                        try
                        {
                            maskSurface = new byte[checked(pixelCount * 4)];
                            SvgAnalyticClipBounds contentBounds = affineViewportClip ? layer
                                : new(Math.Max(layer.Left, maskBounds.Left), Math.Max(layer.Top, maskBounds.Top), Math.Min(layer.Right, maskBounds.Right), Math.Min(layer.Bottom, maskBounds.Bottom));
                            using SvgCpuClipCoverage? maskCoverage = RasterizeMaskContent(maskContent, maskSurface, affineViewportClip, contentBounds);
                        }
                        catch
                        {
                            maskReservation.Dispose();
                            throw;
                        }
                    }
                    using RenderBudget.BudgetReservation? maskLease = maskReservation;
                    for (int pixel = 0; pixel < pixelCount; pixel++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int offset = pixel * 4;
                        byte extra = command.Opacity;
                        byte maskAlpha = command.MaskAlpha;
                        if (hasMaskContent)
                        {
                            byte contentAlpha = maskSurface[offset + 3];
                            byte maskValue = maskIsAlpha ? contentAlpha
                                : variableLuminance
                                    ? (byte)Math.Clamp((int)MathF.Round(maskSurface[offset] * 0.2126f + maskSurface[offset + 1] * 0.7152f + maskSurface[offset + 2] * 0.0722f), 0, 255)
                                    : MultiplyAlpha(contentAlpha, maskLuminance);
                            if (!affineViewportClip) maskAlpha = MultiplyAlpha(maskAlpha, maskValue);
                        }
                        byte clipAlpha = 255;
                        if (affineViewportClip) clipAlpha = maskSurface[offset + 3];
                        else if (viewportClip)
                            clipAlpha = SvgAnalyticRectangleClip.GetCoverage((regionLeft, regionTop, regionRight, regionBottom), command.MaskRegion.GetValueOrDefault().Antialiasing, pixel % outputWidth, pixel / outputWidth).Apply(255);
                        else if (command.MaskRegion is not null)
                        {
                            int x = pixel % outputWidth;
                            int y = pixel / outputWidth;
                            if (x < maskBounds.Left || x >= maskBounds.Right || y < maskBounds.Top || y >= maskBounds.Bottom)
                                maskAlpha = 0;
                        }
                        if (parentViewportClip.Length != 0)
                            clipAlpha = (byte)((clipAlpha * parentViewportClip[offset + 3] + 127) / 255);
                        if (extra == 0 || maskAlpha == 0 || clipAlpha == 0 || groupPixels[offset + 3] == 0) continue;
                        // A binary viewport permits the native sprite restore;
                        // fractional clip coverage requires the separate shader path.
                        if (shaderRestore || parentViewportClip.Length != 0 && !activeViewportIsBinary) BlendClippedLayerPixel(destination.AsSpan(offset, 4), groupPixels.AsSpan(offset, 4), extra, maskAlpha, clipAlpha);
                        else BlendLayerPixel(destination.AsSpan(offset, 4), groupPixels.AsSpan(offset, 4), extra, maskAlpha);
                    }
                    continue;
                }
                if ((uint)command.GeometryIndex >= (uint)root.Geometries.Length) throw new SvgException(SvgErrorCode.MalformedInput, "SVG draw command references missing geometry.");
                nextPatternTile = NextPatternTile(index, root.Commands.Length);
                RasterizeGeometry(root.Geometries[command.GeometryIndex], destination);
            }
            if (stopsAtGroupEnd) throw new SvgException(SvgErrorCode.MalformedInput, "SVG group structure is unbalanced.");
            return index;

            // The native image blitter sums scaled source/destination lanes before
            // their final byte shift. Masks affect the isolated surface first;
            // outer opacity remains a separate image-draw scale.
            static void BlendLayerPixel(Span<byte> destination, ReadOnlySpan<byte> source, byte opacity, byte maskAlpha)
            {
                int sourceScale = opacity + 1;
                int inverse = 65535 - MultiplyAlpha(source[3], maskAlpha) * sourceScale;
                int destinationScale = (inverse + (inverse >> 8)) >> 8;
                for (int lane = 0; lane < 4; lane++)
                    destination[lane] = (byte)((MultiplyAlpha(source[lane], maskAlpha) * sourceScale + destination[lane] * destinationScale) >> 8);
            }


        }

        static void BlendClippedLayerPixel(Span<byte> destination, ReadOnlySpan<byte> source, byte opacity, byte maskAlpha, byte coverage)
        {
            // SkBitmapProcState's unfiltered shader scales each premultiplied lane
            // before SkARGB32_Shader_Blitter applies the AA run through SkBlendARGB32.
            int opacityScale = opacity + 1;
            int alpha = MultiplyAlpha(source[3], maskAlpha) * opacityScale >> 8;
            int sourceScale = coverage + 1;
            int inverse = 65535 - alpha * sourceScale;
            int destinationScale = (inverse + (inverse >> 8)) >> 8;
            for (int lane = 0; lane < 4; lane++)
            {
                int shaded = MultiplyAlpha(source[lane], maskAlpha) * opacityScale >> 8;
                destination[lane] = (byte)((shaded * sourceScale + destination[lane] * destinationScale) >> 8);
            }
        }

        // Borrow caller-reserved destination storage. Clip collection forces ordered AA runs;
        // mask/clip content is independent of the surrounding paint opacity and clip stack.
        // Geometry traversal retains the existing work, cancellation and temporary-owner contracts.
        SvgCpuClipCoverage? RasterizeMaskContent(SvgMaskContent content, byte[] destination, bool viewport, SvgAnalyticClipBounds contentBounds)
        {
            ReleasePatternTile();
            nextPatternTile = null;
            byte[] previousClip = activeViewportClip, previousConstruction = activeConstructionClip;
            SvgAnalyticClipBounds? previousBounds = activeViewportBounds, previousConstructionBounds = activeConstructionBounds;
            SvgCpuClipCoverage? previousCollected = collectingClipCoverage, previousCoverage = activeViewportCoverage,
                previousConstructionCoverage = activeConstructionCoverage;
            SvgCpuClipCoverage? previousWindowCoverage = clipWindowCoverage;
            SvgAnalyticClipBounds? previousWindowBounds = clipWindowBounds;
            // A native clip builds within the parent bitmap's nonzero bounds.
            // Coverage intersects afterward; the bitmap origin stays unchanged.
            clipWindowCoverage = viewport ? previousCoverage ?? clipWindowCoverage : null;
            clipWindowBounds = viewport ? previousBounds ?? clipWindowBounds : null;
            SvgCpuClipCoverage? previousLayerClipCoverage = layerClipCoverage;
            if (!viewport) layerClipCoverage = previousCoverage ?? layerClipCoverage;
            float previousOpacity = foldedOpacity;
            bool previousCollector = collectingViewportClip;
            bool previousMaskClip = maskClipAntialiased;
            maskClipAntialiased = false;
            SvgAnalyticClipBounds previousLayer = layerBounds;
            SvgRoot previousRoot = root;
            double previousScaleX = horizontalScale, previousScaleY = verticalScale, previousOffsetX = horizontalOffset, previousOffsetY = verticalOffset;
            SvgAffineTransform maskMap = content.CoordinateMap;
            if (!maskMap.IsAxisAligned) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned mask replay maps are implemented.");
            horizontalOffset += maskMap.E * horizontalScale;
            verticalOffset += maskMap.F * verticalScale;
            horizontalScale *= maskMap.A;
            verticalScale *= maskMap.D;
            activeViewportClip = []; activeViewportBounds = null; activeViewportCoverage = null; foldedOpacity = 1;
            activeConstructionClip = []; activeConstructionBounds = null; activeConstructionCoverage = null;
            collectingViewportClip = viewport; collectingClipCoverage = null;
            layerBounds = contentBounds;
            RenderBudget.BudgetReservation? boundsReservation = null;
            SvgCpuClipCoverage? coverage = null, boundsCoverage = null;
            bool transferred = false;
            try
            {
                if (viewport)
                {
                    long initializationWork = rasterGrid.ClipInitializationWork;
                    if (initializationWork > options.Limits.MaximumWorkUnits - workUnits)
                        throw new SvgException(SvgErrorCode.LimitExceeded, "Bitmap clip storage exceeds the configured work limit.");
                    workUnits += initializationWork;
                    coverage = new(rasterGrid, budget);
                    collectingClipCoverage = coverage;
                }
                byte[] boundsPixels = [];
                if (content.BoundsClipContentIndex >= 0)
                {
                    SvgSceneStorage storage = root.SceneStorage ?? throw new SvgException(SvgErrorCode.MalformedInput, "Missing clip bounds content.");
                    if ((uint)content.BoundsClipContentIndex >= (uint)storage.MaskContents.Length || storage.MaskContents[content.BoundsClipContentIndex].BoundsClipContentIndex >= 0)
                        throw new SvgException(SvgErrorCode.MalformedInput, "Invalid clip bounds content.");
                    boundsReservation = budget.Reserve(surfaceByteCount, SvgMemoryCategory.Surface);
                    boundsPixels = new byte[surfaceByteCount];
                    // Native clipPath constructs against the already clipped enclosing rectangle.
                    // Its bounds and border bytes belong to each bitmap; its alpha remains separate.
                    boundsCoverage = RasterizeMaskContent(storage.MaskContents[content.BoundsClipContentIndex], boundsPixels, true, contentBounds);
                    if (viewport && previousClip.Length != 0)
                    {
                        // The enclosing rectangle intersects the existing native clip
                        // before the path is constructed. Its trimmed bitmap bounds
                        // control curve clipping; coverage applies only once.
                        if (pixelCount > options.Limits.MaximumWorkUnits - workUnits)
                            throw new SvgException(SvgErrorCode.LimitExceeded, "Clip construction intersection exceeds the configured limit.");
                        workUnits += pixelCount;
                        for (int pixel = 0; pixel < pixelCount; pixel++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            int offset = pixel * 4 + 3;
                            boundsPixels[offset] = (byte)((boundsPixels[offset] * previousClip[offset] + 127) / 255);
                        }
                        if (boundsCoverage is not null)
                        {
                            boundsCoverage.IntersectAndTrim(previousCoverage, previousClip, boundsPixels, outputWidth,
                                options.Limits.MaximumWorkUnits - workUnits, out long intersectionWork, cancellationToken);
                            workUnits += intersectionWork;
                        }
                    }
                    activeConstructionCoverage = boundsCoverage;
                    activeConstructionClip = boundsPixels;
                    activeConstructionBounds = CoverageBounds(boundsPixels, contentBounds);
                }
                if (content.VectorContent is {} vector)
                {
                    SvgSceneStorage scene = vector.Scene;
                    root = root with { Geometries = scene.Geometries, Commands = scene.Commands, SceneStorage = scene };
                    RenderCommands(0, destination, false);
                }
                else if (content.ClipKind == SvgClipContentKind.BinaryViewport)
                {
                    if (content.Geometries.Length != 1 || content.Geometries[0].AuthoredPath is not {} viewportPath || coverage is null)
                        throw new SvgException(SvgErrorCode.MalformedInput, "A binary viewport requires retained rectangle geometry.");
                    workUnits += SvgBinaryViewportCoverage.Rasterize(viewportPath,
                        SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset), rasterGrid,
                        contentBounds, destination, outputWidth, coverage, options.Limits.MaximumWorkUnits - workUnits, cancellationToken);
                }
                else foreach (SvgGeometry geometry in content.Geometries) RasterizeGeometry(geometry, destination);
                if (boundsPixels.Length != 0)
                {
                    if (pixelCount > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Clip bounds intersection exceeds the configured limit.");
                    workUnits += pixelCount;
                    for (int pixel = 0; pixel < pixelCount; pixel++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int offset = pixel * 4 + 3;
                        destination[offset] = (byte)((destination[offset] * boundsPixels[offset] + 127) / 255);
                    }
                }
                transferred = true;
                return coverage;
            }
            finally
            {
                boundsCoverage?.Dispose(); boundsReservation?.Dispose();
                if (!transferred) coverage?.Dispose();
                layerClipCoverage = previousLayerClipCoverage;
                clipWindowCoverage = previousWindowCoverage; clipWindowBounds = previousWindowBounds;
                collectingClipCoverage = previousCollected;
                activeConstructionClip = previousConstruction; activeConstructionBounds = previousConstructionBounds;
                activeConstructionCoverage = previousConstructionCoverage;
                activeViewportClip = previousClip; activeViewportBounds = previousBounds; activeViewportCoverage = previousCoverage;
                foldedOpacity = previousOpacity; collectingViewportClip = previousCollector; root = previousRoot;
                horizontalScale = previousScaleX; verticalScale = previousScaleY; horizontalOffset = previousOffsetX; verticalOffset = previousOffsetY;
                layerBounds = previousLayer;
                maskClipAntialiased = previousMaskClip;
                ReleasePatternTile();
            }
        }

        void RasterizeGeometry(SvgGeometry geometry, byte[] destination)
        {
            bool reusablePattern = geometry.Kind == SvgGeometryKind.PatternFill && geometry.Pattern.Stroke is null && geometry.LeafMask is null;
            if (!reusablePattern || patternTileCache is {} retained && (!retained.MatchesSource(geometry.Pattern) || NeedsPatternMapping(geometry.Pattern) && budget.AvailableBytes < 512)) ReleasePatternTile();
            SvgRecordedPaintValue previousPaint = activePaint;
            SvgRecordedPaintValue previousStroke = activeStrokePaint;
            activePaint = GetRecordedPaintValue(geometry.RecordedPaint);
            activeStrokePaint = GetRecordedPaintValue(geometry.RecordedStrokePaint);
            try
            {
                bool combined = geometry.Kind switch
                {
                    SvgGeometryKind.Rectangle => geometry.Rectangle.HasFill && geometry.Rectangle.Stroke is not null,
                    SvgGeometryKind.PatternFill => geometry.Pattern.Fill.HasFill && geometry.Pattern.Stroke is not null,
                    SvgGeometryKind.GradientFill => geometry.Gradient.Fill.HasFill && geometry.Gradient.Stroke is not null,
                    SvgGeometryKind.GradientStroke => geometry.GradientStroke.Fill.HasFill && geometry.GradientStroke.HasStroke,
                    SvgGeometryKind.RadialGradient => geometry.Radial.Fill.HasFill && geometry.Radial.Stroke is not null,
                    _ => false
                };
                if (combined && activePaint.PaintOrder.StrokeFirst())
                {
                    RasterizeGeometryCore(geometry, destination, SvgPaintPass.Stroke);
                    RasterizeGeometryCore(geometry, destination, SvgPaintPass.Fill);
                }
                else RasterizeGeometryCore(geometry, destination, SvgPaintPass.All);
            }
            finally { activePaint = previousPaint; activeStrokePaint = previousStroke; }
        }

        void RasterizeGeometryCore(SvgGeometry geometry, byte[] destination, SvgPaintPass pass)
        {
            if (geometry.Kind == SvgGeometryKind.PolygonFill && geometry.AuthoredPath is SvgAuthoredPath centerline
                && centerline.StrokeStyle is SvgAuthoredStrokeStyle strokeStyle
                && (strokeStyle.Shape == SvgAuthoredStrokeShape.Path || !centerline.SceneMap.PreservesAxisAlignedRectangles))
            {
                ParserWorkCounter hairlineWork = new(options.Limits.MaximumWorkUnits - workUnits);
                SvgPolygonFill stroke = geometry.Polygon;
                bool rendered = stroke.HasFill && SvgHairlineStroke.TryRasterize(centerline,
                    SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset), ResolveSolidColor(stroke.Color), surfaceMode == RasterSurfaceMode.PremultipliedPattern ? PatternSolidPaint(stroke.Color) : null,
                    stroke.Clip is SvgClipBox clip ? MapClipBounds(clip, horizontalScale, horizontalOffset, verticalScale, verticalOffset) : null,
                    stroke.Clip?.Antialiasing ?? SvgClipAntialiasing.None, activeViewportClip, destination, outputWidth, outputHeight, hairlineWork, cancellationToken);
                workUnits += hairlineWork.Used;
                if (rendered) return;
            }
            switch (geometry.Kind)
            {
                case SvgGeometryKind.EmbeddedImage:
                    RasterizeImage(geometry.Image, geometry.AuthoredPath, destination);
                    return;
                case SvgGeometryKind.Rectangle:
                    RasterizeRectangle(geometry.Rectangle, destination, pass);
                    return;
                case SvgGeometryKind.AxisAlignedLine:
                    RasterizeAxisAlignedLine(geometry.AxisAlignedLine, destination);
                    return;
                case SvgGeometryKind.OrthogonalBand:
                    RasterizeOrthogonalBand(geometry.Rectangle, destination);
                    return;
                case SvgGeometryKind.GlyphContours:
                    RasterizeGlyphContours(geometry.Glyph, destination);
                    return;
                case SvgGeometryKind.PatternFill:
                    RasterizePatternFill(geometry.Pattern, destination, pass);
                    return;
                case SvgGeometryKind.GradientFill:
                    RasterizeGradientFill(geometry.Gradient, destination, pass);
                    return;
                case SvgGeometryKind.GradientStroke:
                    RasterizeGradientStrokeFill(geometry.GradientStroke, destination, pass);
                    return;
                case SvgGeometryKind.RadialGradient:
                    RasterizeRadialFill(geometry.Radial, destination, pass);
                    return;
                case SvgGeometryKind.PolygonFill:
                    RasterizePolygonFill(geometry.Polygon, geometry.AuthoredPath, destination, ReadOnlySpan<byte>.Empty);
                    return;
                case SvgGeometryKind.GradientPolygon:
                    RasterizeGradientPolygon(geometry.GradientPolygon, geometry.AuthoredPath, destination);
                    return;
                case SvgGeometryKind.RadialPolygon:
                    RasterizeRadialPolygon(geometry.RadialPolygon, geometry.AuthoredPath, destination);
                    return;
                case SvgGeometryKind.PatternPolygon:
                    RasterizePatternPolygon(geometry.PatternPolygon, geometry.AuthoredPath, destination);
                    return;
                default:
                    throw new SvgException(SvgErrorCode.MalformedInput, "SVG scene contains invalid geometry.");
            }
        }

        void RasterizeAxisAlignedLine(SvgAxisAlignedLine line, byte[] destination)
        {
            if (line.IsPainted) RasterizeSolidRectangle(line.ToStrokeBand(), destination);
        }

        // Bitmap span mapping/interpolation and covered blending adapt Skia at
        // f8b66b7597c4cc859d3ed190e9c6872241e6721c. Copyright 2008, 2011 Google Inc.
        // See THIRD_PARTY_NOTICES.txt; existing shader matrix preparation is shared.
        void RasterizeImage(SvgImageGeometry image, SvgAuthoredPath? authoredPath, byte[] destination)
        {
            byte opacity = (byte)Math.Clamp((int)(ShaderAlpha(image.Opacity) * 255f + .5f), 0, 255);
            if (!image.Box.HasFill || opacity == 0 || image.Box.Width == 0 || image.Box.Height == 0) return;
            SvgRectangleRaster raster = PrepareRectangle(image.Box);
            raster = raster with { Left = Math.Max(raster.Left, layerBounds.Left), Top = Math.Max(raster.Top, layerBounds.Top),
                Right = Math.Min(raster.Right, layerBounds.Right), Bottom = Math.Min(raster.Bottom, layerBounds.Bottom) };
            if (raster.Left >= raster.Right || raster.Top >= raster.Bottom) return;
            using RasterizedSurface? mask = authoredPath is null ? null : PrepareImageMask(authoredPath, image.Box.Clip, raster);
            long sampleWork = (long)(raster.Right - raster.Left) * (raster.Bottom - raster.Top) * 4;
            if (sampleWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Embedded image sampling exceeds the configured work limit.");
            workUnits += sampleWork;
            SvgShaderMatrix output = new((float)horizontalScale, 0, (float)horizontalOffset, 0, (float)verticalScale, (float)verticalOffset);
            SvgShaderCoordinates coordinates = SvgShaderCoordinates.CreateImage(SvgShaderMatrix.Concat(output, image.ReferenceMap), image.LocalMap);
            byte[] source = image.UniformSample is null ? image.Pixels : [];
            for (int y = raster.Top; y < raster.Bottom; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgCpuBitmapOrigin previousOrigin = default;
                byte previousCoverage = 0;
                bool seeded = false, filtering = true;
                long fixedX = 0, fixedY = 0, stepX = 0;
                for (int x = raster.Left; x < raster.Right; x++)
                {
                    byte coverage = ViewportAlpha(mask is {} coverageMask
                        ? coverageMask.Pixels[((y - raster.Top) * coverageMask.Width + x - raster.Left) * 4 + 3]
                        : raster.GetAlpha(x, y), x, y);
                    if (coverage == 0) { seeded = false; continue; }
                    SvgCpuBitmapOrigin origin = ShaderBitmapOrigin(x, y);
                    if (authoredPath is not null)
                    {
                        if (!coordinates.TryGetMatrix(origin, out SvgShaderMatrix affineInverse))
                            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Embedded image coordinates are not finite and invertible.");
                        float pixelX = x - origin.X + .5f, pixelY = y - origin.Y + .5f;
                        // Low-precision pipeline matrix stages round products separately.
                        float sampleX = pixelX * affineInverse.ScaleX + (pixelY * affineInverse.SkewX + affineInverse.TranslateX);
                        float sampleY = pixelX * affineInverse.SkewY + (pixelY * affineInverse.ScaleY + affineInverse.TranslateY);
                        SampleAffine(sampleX, sampleY).Blend(destination.AsSpan((y * outputWidth + x) * 4, 4), coverage);
                        continue;
                    }
                    if (!seeded || origin != previousOrigin || coverage != previousCoverage)
                    {
                        if (!coordinates.TryGetMatrix(origin, out SvgShaderMatrix inverse))
                            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Embedded image coordinates are not finite and invertible.");
                        // The legacy bitmap route normalizes nearly-translation
                        // maps and selects nearest sampling for integral phase.
                        if (inverse.ScaleX != 1 || inverse.ScaleY != 1)
                            if (inverse.TryInvert(out SvgShaderMatrix forward) && MathF.Abs(forward.ScaleX - 1) <= 1f / 32768f
                                && MathF.Abs(forward.ScaleY - 1) <= 1f / 32768f)
                                inverse = new(1, 0, -forward.TranslateX, 0, 1, -forward.TranslateY);
                        filtering = image.Width < 16384 && image.Height < 16384
                            && !(inverse.ScaleX == 1 && inverse.ScaleY == 1
                                && MathF.Abs(inverse.TranslateX - MathF.Floor(inverse.TranslateX + .5f)) <= 1f / 256f
                                && MathF.Abs(inverse.TranslateY - MathF.Floor(inverse.TranslateY + .5f)) <= 1f / 256f);
                        float pointX = (x - origin.X + .5f) * inverse.ScaleX + inverse.TranslateX;
                        float pointY = (y - origin.Y + .5f) * inverse.ScaleY + inverse.TranslateY;
                        fixedX = (long)((double)pointX * 4294967296d);
                        fixedY = (long)((double)pointY * 4294967296d);
                        stepX = (long)((double)inverse.ScaleX * 4294967296d);
                        previousOrigin = origin; previousCoverage = coverage; seeded = true;
                    }
                    SvgConstantShaderPaint sample = Sample(fixedX, fixedY, filtering);
                    Blend(destination.AsSpan((y * outputWidth + x) * 4, 4), sample, coverage);
                    fixedX += stepX;
                }
            }

            // SkBlitRow_D32 keeps covered source and destination products together
            // until the final shift; quantizing covered source first loses a unit.
            static void Blend(Span<byte> destination, SvgConstantShaderPaint sample, byte coverage)
            {
                if (coverage == 255)
                {
                    BlendPremultipliedOver(destination, sample.Red, sample.Green, sample.Blue, sample.Alpha);
                    return;
                }
                int sourceScale = coverage + 1;
                int inverse = 65535 - sample.Alpha * sourceScale;
                int destinationScale = (inverse + (inverse >> 8)) >> 8;
                destination[0] = (byte)((sample.Red * sourceScale + destination[0] * destinationScale) >> 8);
                destination[1] = (byte)((sample.Green * sourceScale + destination[1] * destinationScale) >> 8);
                destination[2] = (byte)((sample.Blue * sourceScale + destination[2] * destinationScale) >> 8);
                destination[3] = (byte)((sample.Alpha * sourceScale + destination[3] * destinationScale) >> 8);
            }

            // Native bitmap interpolation retains four-bit fractions and truncates
            // the bilerp before independent packed paint alpha is applied.
            SvgConstantShaderPaint Sample(long x, long y, bool filtering)
            {
                if (image.UniformSample is {} uniform) return ApplyOpacity(uniform, false);
                long bias = filtering ? 2147483648L : 65536L;
                x -= bias; y -= bias;
                int left = (int)Math.Clamp(x >> 32, -1, image.Width), top = (int)Math.Clamp(y >> 32, -1, image.Height);
                int fx = filtering ? (int)(x >> 28 & 15) : 0, fy = filtering ? (int)(y >> 28 & 15) : 0;
                int x0 = Math.Clamp(left, 0, image.Width - 1), x1 = Math.Clamp(left + 1, 0, image.Width - 1);
                int y0 = Math.Clamp(top, 0, image.Height - 1), y1 = Math.Clamp(top + 1, 0, image.Height - 1);
                int a = (y0 * image.Width + x0) * 4, b = (y0 * image.Width + x1) * 4;
                int c = (y1 * image.Width + x0) * 4, d = (y1 * image.Width + x1) * 4;
                SvgConstantShaderPaint sample = SvgConstantShaderPaint.Interpolate(Pixel(source, a), Pixel(source, b), Pixel(source, c), Pixel(source, d), fx, fy);
                return ApplyOpacity(sample, false);

                static SvgConstantShaderPaint Pixel(ReadOnlySpan<byte> pixels, int at)
                    => new(pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]);
            }

            SvgConstantShaderPaint ApplyOpacity(SvgConstantShaderPaint sample, bool pipeline)
            {
                if (pipeline)
                    return new((byte)((sample.Red * opacity + 255) >> 8), (byte)((sample.Green * opacity + 255) >> 8),
                        (byte)((sample.Blue * opacity + 255) >> 8), (byte)((sample.Alpha * opacity + 255) >> 8));
                int opacityScale = opacity + 1;
                return new((byte)(sample.Red * opacityScale >> 8), (byte)(sample.Green * opacityScale >> 8),
                    (byte)(sample.Blue * opacityScale >> 8), (byte)(sample.Alpha * opacityScale >> 8));
            }

            // Pinned raster-pipeline bilerp_clamp_8888 retains Q15 intermediate
            // fractions; the separate axis-aligned bitmap route retains four bits.
            // Copyright 2016 Google Inc.; 2018, 2022 Google LLC.
            // See THIRD_PARTY_NOTICES.txt for the BSD license and source pin.
            SvgConstantShaderPaint SampleAffine(float x, float y)
            {
                if (image.UniformSample is {} uniform) return ApplyOpacity(uniform, true);
                long qx = (long)MathF.Floor(65536f * Math.Clamp(x, -1, image.Width) + .5f) - 32768;
                long qy = (long)MathF.Floor(65536f * Math.Clamp(y, -1, image.Height) + .5f) - 32768;
                int tx = unchecked((short)(qx ^ 0x8000)), ty = unchecked((short)(qy ^ 0x8000));
                int left = (int)(qx >> 16), top = (int)(qy >> 16);
                int x0 = Math.Clamp(left, 0, image.Width - 1), x1 = Math.Clamp(left + 1, 0, image.Width - 1);
                int y0 = Math.Clamp(top, 0, image.Height - 1), y1 = Math.Clamp(top + 1, 0, image.Height - 1);
                int a = (y0 * image.Width + x0) * 4, b = (y0 * image.Width + x1) * 4;
                int c = (y1 * image.Width + x0) * 4, d = (y1 * image.Width + x1) * 4;
                return ApplyOpacity(SvgConstantShaderPaint.InterpolatePipeline(Pixel(a), Pixel(b), Pixel(c), Pixel(d), tx, ty), true);

                SvgConstantShaderPaint Pixel(int at) => new(source[at], source[at + 1], source[at + 2], source[at + 3]);
            }

            // Borrow the retained source rectangle into the existing bounded path rasterizer.
            // Its temporary surface carries coverage only; source pixels and paint opacity
            // remain separate and are blended once into the parent destination.
            RasterizedSurface PrepareImageMask(SvgAuthoredPath sourcePath, SvgClipBox? clip, SvgRectangleRaster bounds)
            {
                ParserWorkCounter preparation = new(options.Limits.MaximumWorkUnits - workUnits);
                SvgAffineTransform deviceMap = SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale,
                    horizontalOffset - bounds.Left, verticalOffset - bounds.Top);
                using SvgAuthoredPath path = sourcePath.Copy(deviceMap, budget, preparation, cancellationToken);
                workUnits += preparation.Used;
                SvgClipBox? localClip = clip is {} sourceClip ? deviceMap.Apply(sourceClip) : null;
                using SvgPolygonFill fill = SvgPolygonFill.Create([], [], null, new(0, 0, 0, 255), true, false, SvgPolygonMapping.NativeFillEdges, localClip, null, budget);
                using RenderBudget.BudgetReservation sceneOwner = budget.Reserve(256 + ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(1)
                    + ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(1), SvgMemoryCategory.Geometry);
                SvgGeometry[] geometries = [SvgGeometry.FromPolygonFill(fill) with { AuthoredPath = path }];
                SvgDrawCommand[] commands = [new(SvgDrawCommandKind.Geometry, 0, 255, null, -1, 255)];
                SvgRoot scene = new(null, null, null, geometries, commands, false, 0, null);
                SvgRenderLimits limits = options.Limits;
                long remaining = limits.MaximumWorkUnits - workUnits;
                if (remaining <= 0) throw new SvgException(SvgErrorCode.LimitExceeded, "Image coverage exceeds the configured work limit.");
                SvgRenderLimits bounded = new(limits.MaximumAccountedBytes, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels,
                    limits.MaximumEncodedPngBytes, limits.MaximumNodes, limits.MaximumPathCommands, checked((int)remaining), limits.MaximumXmlDepth, limits.MaximumAttributesPerElement);
                SvgRenderOptions maskOptions = new(new SvgViewport(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top), 1, SvgColor.Transparent, bounded);
                RasterizedSurface result = Rasterize(scene, maskOptions, budget, RasterSurfaceMode.PremultipliedGlyph, cancellationToken);
                workUnits += result.WorkUnits;
                return result;
            }
        }

        void RasterizeOrthogonalBand(SvgRectangle band, byte[] destination)
        {
            if (band.HasFill) RasterizeSolidRectangle(band, destination);
        }

        double PixelCoverage(MappedPolygon raster, ReadOnlySpan<int> contourEnds, ReadOnlySpan<int> nestingSigns, bool isEvenOdd, int[]? signs, int x, int y, CancellationToken cancellationToken)
        {
            double coverage = isEvenOdd
                ? SvgPolygonCoverage.GetEvenOddCoverage(raster.Points, contourEnds, nestingSigns, x, y, cancellationToken)
                : signs is int[] roles
                    ? SvgPolygonCoverage.GetWindingCoverage(raster.Points, contourEnds, roles, x, y, cancellationToken)
                    : Math.Clamp(Math.Abs(SvgPolygonCoverage.GetWindingCoverage(raster.Points, contourEnds, x, y, cancellationToken)), 0, 1);
            if (raster.Clip is not null) coverage *= ClipBoxCoverage(raster.Clip.Value, x, y);
            return coverage;
        }

        MappedPolygon MapPolygon(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, bool isEvenOdd, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, SvgPolygonMapping mapping, SvgClipBox? clip, SvgAuthoredPath? authoredPath, PathCoordinateSpace coordinateSpace)
        {
            SvgPreparedPathContours? authored = null;
            SvgAffineTransform authoredMap = SvgAffineTransform.Identity;
            SvgAuthoredPath? replay = authoredPath;
            // Wider strokes replay their generated source outline; the original
            // centerline remains separate for hairline coverage and width dispatch.
            if (authoredPath?.StrokeStyle is not null) replay = authoredPath.StrokeOutline;
            if (replay is not null)
            {
                ParserWorkCounter preparationWork = new(options.Limits.MaximumWorkUnits - workUnits);
                bool binaryViewport = clip is {} binaryClip && (binaryClip.Antialiasing & SvgClipAntialiasing.Binary) != 0
                    || activeViewportClip.Length != 0 && activeViewportIsBinary;
                SvgAffineTransform outputMap = coordinateSpace == PathCoordinateSpace.Device ? SvgAffineTransform.Identity : binaryViewport
                    ? SvgAffineTransform.FromScaleOffset((float)horizontalScale, (float)verticalScale, (float)horizontalOffset, (float)verticalOffset)
                    : SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset);
                authoredMap = outputMap.ComposeChecked(replay.SceneMap);
                if (authoredPath?.StrokeStyle?.NonScalingMap is {} nonScaling)
                {
                    preparationWork.Consume(cancellationToken);
                    if (!nonScaling.StrokeTransform.TryInvert(out SvgAffineTransform inverse))
                        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Non-scaling stroke transform must be invertible.");
                    authoredMap = outputMap.ComposeNative(nonScaling.SourceMap).ComposeNative(inverse);
                }
                if (frame is { ParentOrigin: {} parent } sourceFrame)
                {
                    // SkDevice::setGlobalCTM rounds in the parent bitmap before
                    // post-concatenating the source device's integer origin.
                    // Combining the translations loses that native float rounding.
                    float tx = (float)(authoredMap.E + sourceFrame.CaptureOrigin.X) - parent.X;
                    float ty = (float)(authoredMap.F + sourceFrame.CaptureOrigin.Y) - parent.Y;
                    authoredMap = authoredMap with { E = tx + (parent.X - sourceFrame.CaptureOrigin.X), F = ty + (parent.Y - sourceFrame.CaptureOrigin.Y) };
                }
                authored = replay.PrepareSource(outputMap, isEvenOdd, budget, preparationWork, cancellationToken);
                workUnits += preparationWork.Used;
                points = authored.Points; contourEnds = authored.ContourEnds; signs = authored.Signs;
                mapping = authoredPath?.StrokeStyle is null ? SvgPolygonMapping.NativeFillEdges : SvgPolygonMapping.NativeStrokeEdges;
                quadSpans = authored.QuadSpans; cubicSpans = authored.CubicSpans;
            }
            if (points.Length == 0)
            {
                // Device-size fitting can collapse every contour. Release replay
                // scratch and return a nonpainting owner without reserving zero bytes.
                authored?.Dispose();
                return new([], 0, 0, 0, 0, 0, 0, 0, 0, null, SvgClipAntialiasing.None, true, default, false, [], [], null, [], null, null, SvgAffineTransform.Identity, mapping);
            }
            bool transferred = false;
            try
            {
                double pointScaleX = authored is null && coordinateSpace == PathCoordinateSpace.User ? horizontalScale : 1;
                double pointScaleY = authored is null && coordinateSpace == PathCoordinateSpace.User ? verticalScale : 1;
                double pointOffsetX = authored is null && coordinateSpace == PathCoordinateSpace.User ? horizontalOffset : 0;
                double pointOffsetY = authored is null && coordinateSpace == PathCoordinateSpace.User ? verticalOffset : 0;
                long mappingWork = (long)points.Length + 2L * quadSpans.Length + 2L * cubicSpans.Length;
                if (mappingWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon vertex mapping exceeds the configured limit.");
                workUnits += mappingWork;
                RenderBudget.BudgetReservation mappedReservation = budget.Reserve(checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(points.Length) + ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(quadSpans.Length) + ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(cubicSpans.Length)), SvgMemoryCategory.Surface);
                try
                {
                    SvgDevicePoint[] mapped = new SvgDevicePoint[points.Length];
                    SvgQuadSpan[] mappedSpans = MapQuadSpanControls(quadSpans, pointScaleX, pointOffsetX, pointScaleY, pointOffsetY, outputWidth, outputHeight, mapping is SvgPolygonMapping.NativeStrokeEdges or SvgPolygonMapping.NativeFillEdges or SvgPolygonMapping.FlattenedCurves, out bool canvasContainsQuads, authored is null ? null : authoredMap, cancellationToken);
                    SvgCubicSpan[] mappedCubics = MapCubicSpanControls(cubicSpans, pointScaleX, pointOffsetX, pointScaleY, pointOffsetY, outputWidth, outputHeight, out bool canvasContainsCubics, authored is null ? null : authoredMap, cancellationToken);
                    bool hasFractionalEdge = false;
                    bool nativeConics = Array.Exists(quadSpans, static span => span.ConicWeight != 1f);
                    double left = double.PositiveInfinity;
                    double top = double.PositiveInfinity;
                    double right = double.NegativeInfinity;
                    double bottom = double.NegativeInfinity;
                    for (int index = 0; index < points.Length; index++)
                    {
                        if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                        double edgeX = nativeConics || mapping is SvgPolygonMapping.QuarterEdges or SvgPolygonMapping.FiniteEdges or SvgPolygonMapping.NativeStrokeEdges or SvgPolygonMapping.NativeFillEdges or SvgPolygonMapping.FlattenedCurves ? MapNativePolygonEdge(points[index].X, pointScaleX, pointOffsetX) : MapEdge(points[index].X, pointScaleX, pointOffsetX);
                        double edgeY = nativeConics || mapping is SvgPolygonMapping.QuarterEdges or SvgPolygonMapping.FiniteEdges or SvgPolygonMapping.NativeStrokeEdges or SvgPolygonMapping.NativeFillEdges or SvgPolygonMapping.FlattenedCurves ? MapNativePolygonEdge(points[index].Y, pointScaleY, pointOffsetY) : MapEdge(points[index].Y, pointScaleY, pointOffsetY);
                        if (authored is not null)
                        {
                            SvgDevicePoint native = SvgAuthoredPath.MapPoint(authoredMap, points[index], new(0, 0));
                            edgeX = native.X; edgeY = native.Y;
                        }
                        mapped[index] = new SvgDevicePoint(edgeX, edgeY);
                        if (edgeX != Math.Truncate(edgeX) || edgeY != Math.Truncate(edgeY)) hasFractionalEdge = true;
                        if (edgeX < left) left = edgeX;
                        if (edgeY < top) top = edgeY;
                        if (edgeX > right) right = edgeX;
                        if (edgeY > bottom) bottom = edgeY;
                    }
                    int startY = ClampFloor(top, outputHeight);
                    int endY = ClampCeiling(bottom, outputHeight);
                    int startX = ClampFloor(left, outputWidth);
                    int endX = ClampCeiling(right, outputWidth);
                    int pathStartX = startX;
                    int pathStartY = startY;
                    int pathEndX = endX;
                    int pathEndY = endY;
                    double completeLeft = left;
                    double completeTop = top;
                    double completeRight = right;
                    double completeBottom = bottom;
                    for (int index = 0; index < mappedSpans.Length; index++)
                    {
                        if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                        IncludeControl(mappedSpans[index].Control, ref completeLeft, ref completeTop, ref completeRight, ref completeBottom);
                    }
                    for (int index = 0; index < mappedCubics.Length; index++)
                    {
                        if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                        IncludeControl(mappedCubics[index].FirstControl, ref completeLeft, ref completeTop, ref completeRight, ref completeBottom);
                        IncludeControl(mappedCubics[index].SecondControl, ref completeLeft, ref completeTop, ref completeRight, ref completeBottom);
                    }
                    SvgDeviceBounds completeBounds = new(completeLeft, completeTop, completeRight, completeBottom);
                    // Native path bounds include raw controls. Flattened samples
                    // can miss an extremum in an adjacent boundary pixel.
                    startX = pathStartX = ClampFloor(completeLeft, outputWidth);
                    startY = pathStartY = ClampFloor(completeTop, outputHeight);
                    endX = pathEndX = ClampCeiling(completeRight, outputWidth);
                    endY = pathEndY = ClampCeiling(completeBottom, outputHeight);
                    (double Left, double Top, double Right, double Bottom)? mappedClip = null;
                    bool clipContainsPath = left >= 0 && top >= 0 && right <= outputWidth && bottom <= outputHeight && canvasContainsQuads && canvasContainsCubics;
                    if (clip is SvgClipBox clipBox)
                    {
                        (startX, startY, endX, endY) = IntersectMappedClip(clipBox, horizontalScale, horizontalOffset, verticalScale, verticalOffset, outputWidth, outputHeight, startX, startY, endX, endY);
                        mappedClip = MapClipBounds(clipBox, horizontalScale, horizontalOffset, verticalScale, verticalOffset);
                        var bounds = mappedClip.Value;
                        // Native hard viewport bounds are rounded before containment
                        // dispatch; their fractional source bounds are not an AA clip.
                        if ((clipBox.Antialiasing & SvgClipAntialiasing.BinaryLeft) != 0) bounds.Left = MathF.Floor((float)bounds.Left + .5f);
                        if ((clipBox.Antialiasing & SvgClipAntialiasing.BinaryTop) != 0) bounds.Top = MathF.Floor((float)bounds.Top + .5f);
                        if ((clipBox.Antialiasing & SvgClipAntialiasing.BinaryRight) != 0) bounds.Right = MathF.Floor((float)bounds.Right + .5f);
                        if ((clipBox.Antialiasing & SvgClipAntialiasing.BinaryBottom) != 0) bounds.Bottom = MathF.Floor((float)bounds.Bottom + .5f);
                        clipContainsPath = clipContainsPath && bounds.Left <= Math.Floor(left) && bounds.Top <= Math.Floor(top) && bounds.Right >= Math.Ceiling(right) && bounds.Bottom >= Math.Ceiling(bottom)
                            && bounds.Left <= completeLeft && bounds.Top <= completeTop && bounds.Right >= completeRight && bounds.Bottom >= completeBottom;
                    }
                    transferred = true;
                    return new MappedPolygon(mapped, startX, startY, endX, endY, pathStartX, pathStartY, pathEndX, pathEndY, mappedClip, clip?.Antialiasing ?? SvgClipAntialiasing.None, clipContainsPath, completeBounds, hasFractionalEdge, mappedSpans, mappedCubics, mappedReservation, contourEnds, signs, authored, authoredMap, mapping);
                }
                catch
                {
                    mappedReservation.Dispose();
                    authored?.Dispose();
                    throw;
                }

            }
            finally { if (!transferred) authored?.Dispose(); }

            static void IncludeControl(SvgDevicePoint point, ref double left, ref double top, ref double right, ref double bottom)
            {
                left = Math.Min(left, point.X);
                top = Math.Min(top, point.Y);
                right = Math.Max(right, point.X);
                bottom = Math.Max(bottom, point.Y);
            }
        }

        // Borrow the current viewport tape; its scope owner charges preparation/intersection and
        // releases it after command replay. Each raster consumer's existing pixel charge covers lookup.
        byte ViewportAlpha(byte alpha, int x, int y)
            => activeViewportClip.Length == 0 ? alpha : (byte)((alpha * activeViewportClip[(y * outputWidth + x) * 4 + 3] + 127) / 255);

        SvgAnalyticClipCoverage PolygonClipCoverage(MappedPolygon raster, int x, int y)
        {
            SvgAnalyticClipCoverage coverage = raster.Clip is null ? SvgAnalyticClipCoverage.Full
                : SvgAnalyticRectangleClip.GetCoverage(raster.Clip.Value, raster.ClipAntialiasing, x, y);
            return activeViewportClip.Length == 0 ? coverage
                : SvgAnalyticClipCoverage.FromMask(coverage.Apply(activeViewportClip[(y * outputWidth + x) * 4 + 3]));
        }

        SvgAnalyticClipBounds CoverageBounds(byte[] pixels, SvgAnalyticClipBounds area)
        {
            long boundsWork = (long)Math.Max(0, area.Right - area.Left) * Math.Max(0, area.Bottom - area.Top);
            if (boundsWork > options.Limits.MaximumWorkUnits - workUnits)
                throw new SvgException(SvgErrorCode.LimitExceeded, "Clip coverage bounds exceed the configured work limit.");
            workUnits += boundsWork;
            int left = area.Right, top = area.Bottom, right = area.Left, bottom = area.Top;
            for (int y = area.Top; y < area.Bottom; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = area.Left; x < area.Right; x++)
                {
                    if ((x & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    if (pixels[(y * outputWidth + x) * 4 + 3] == 0) continue;
                    left = Math.Min(left, x); top = Math.Min(top, y);
                    right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1);
                }
            }
            return left < right && top < bottom ? new(left, top, right, bottom) : new(area.Left, area.Top, area.Left, area.Top);
        }

        SvgAnalyticClipBounds? ActiveClipBounds(SvgCpuRasterContext context)
        {
            SvgCpuClipCoverage? coverage = activeConstructionClip.Length != 0 ? activeConstructionCoverage : activeViewportCoverage;
            SvgAnalyticClipBounds? current = activeConstructionClip.Length != 0 ? activeConstructionBounds : activeViewportBounds;
            if (coverage is not null && coverage.TryGetBounds(context.BitmapClip, out SvgAnalyticClipBounds local)) current = local;
            else
            {
                byte[] tape = activeConstructionClip.Length != 0 ? activeConstructionClip : activeViewportClip;
                if (tape.Length != 0 && current is {} bounds)
                {
                    int left = Math.Max(bounds.Left, context.BitmapClip.Left), top = Math.Max(bounds.Top, context.BitmapClip.Top);
                    int right = Math.Max(left, Math.Min(bounds.Right, context.BitmapClip.Right)), bottom = Math.Max(top, Math.Min(bounds.Bottom, context.BitmapClip.Bottom));
                    current = CoverageBounds(tape, new(left, top, right, bottom));
                }
            }
            SvgAnalyticClipBounds? window = clipWindowCoverage is {} inherited && inherited.TryGetBounds(context.BitmapClip, out SvgAnalyticClipBounds bitmapWindow)
                ? bitmapWindow : clipWindowBounds;
            if (window is not {} clipWindow) return current;
            SvgAnalyticClipBounds area = current ?? context.BitmapClip;
            int x1 = Math.Max(area.Left, clipWindow.Left), y1 = Math.Max(area.Top, clipWindow.Top);
            return new(x1, y1, Math.Max(x1, Math.Min(area.Right, clipWindow.Right)), Math.Max(y1, Math.Min(area.Bottom, clipWindow.Bottom)));
        }

        SvgAnalyticPaintClip PolygonPaintClip(MappedPolygon raster, SvgCpuRasterContext context)
        {
            if (collectingViewportClip || maskClipAntialiased) return SvgAnalyticPaintClip.Antialiased;
            SvgAnalyticPaintClip paintClip = raster.GetPaintClip(context);
            if (activeViewportClip.Length == 0 || activeViewportIsBinary) return paintClip;
            SvgAnalyticClipBounds clip = context.BitmapClip;
            if (activeViewportBounds is {} bounds)
                clip = new(Math.Max(clip.Left, bounds.Left), Math.Max(clip.Top, bounds.Top), Math.Min(clip.Right, bounds.Right), Math.Min(clip.Bottom, bounds.Bottom));
            // Native raster clips collapse a wholly opaque rectangular bitmap
            // intersection to BW. Preserve its ordinary mask/run material routing.
            for (int y = clip.Top; y < clip.Bottom; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = clip.Left; x < clip.Right; x++)
                {
                    if (workUnits >= options.Limits.MaximumWorkUnits)
                        throw new SvgException(SvgErrorCode.LimitExceeded, "Raster clip classification exceeds the configured limit.");
                    workUnits++;
                    if (activeViewportClip[(y * outputWidth + x) * 4 + 3] != 255) return SvgAnalyticPaintClip.Antialiased;
                }
            }
            return paintClip;
        }

        void RasterizePolygonFill(SvgPolygonFill polygon, SvgAuthoredPath? authoredPath, byte[] destination, ReadOnlySpan<byte> fontPreblend)
        {
            // A glyph bitmap scene borrows only its source commands. Ordinary
            // lowered empty shapes still suppress their retained source replay.
            if (!polygon.HasFill || (polygon.Points.Length < 3 && fontPreblend.IsEmpty
                && (surfaceMode != RasterSurfaceMode.PremultipliedGlyph || authoredPath is null))) return;
            using MappedPolygon raster = MapPolygon(polygon.Points, polygon.ContourEnds, polygon.Signs, polygon.IsEvenOdd, polygon.QuadSpans, polygon.CubicSpans, polygon.Mapping, polygon.Clip, authoredPath, fontPreblend.Length == 0 ? PathCoordinateSpace.User : PathCoordinateSpace.Device);
            if (raster.EndY <= raster.StartY || raster.EndX <= raster.StartX) return;
            SvgAnalyticPaint glyphPaint = ResolveSolidPaint(polygon.Color);
            SvgAnalyticPaint analyticPaint = fontPreblend.Length == 0 ? glyphPaint : SvgAnalyticPaint.Solid(new SvgColor(0, 0, 0, 255));
            if (activeViewportClip.Length != 0 && activeViewportIsBinary) analyticPaint = analyticPaint.ThroughBinaryRegion();
            ReadOnlySpan<int> contourEnds = raster.ContourEnds;
            ReadOnlySpan<int> nestingSigns = raster.Signs is int[] signs ? signs : Array.Empty<int>();
            SvgAnalyticContourPreparation preparation = new(raster.Points, contourEnds, nestingSigns, polygon.IsEvenOdd, raster.Signs, raster.Mapping, raster.QuadSpans, raster.CubicSpans, raster.CompleteBounds.RoundOutForDispatch(), outputWidth, outputHeight, raster.Authored, raster.AuthoredMap);
            preparation.RectangleFrame = PrepareRectangleFrame(authoredPath);
            Span<byte> clipPixel = stackalloc byte[4];
            foreach (SvgCpuRasterContext cpuTile in rasterGrid.GetContexts(new(raster.StartX, raster.StartY, raster.EndX, raster.EndY)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgCpuRasterContext context = LayerRasterContext(cpuTile);
                if (context.Interior.Right <= context.Interior.Left || context.Interior.Bottom <= context.Interior.Top) continue;
                if (workUnits >= options.Limits.MaximumWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "CPU raster context traversal exceeds the configured limit.");
                workUnits++;
                if (raster.Clip is not null)
                {
                    if (4 > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle paint-clip classification exceeds the configured limit.");
                    workUnits += 4;
                }
                SvgAnalyticClipBounds? bitmapClipBounds = ActiveClipBounds(context);
                int storageStartX = Math.Max(raster.PathStartX, context.BitmapClip.Left);
                int storageEndX = Math.Min(raster.PathEndX, context.BitmapClip.Right);
                using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, storageStartX, storageEndX, raster.PathStartY, raster.PathEndY, raster.GetScanWindow(context, bitmapClipBounds), context.Origin, raster.GetConstructionClip(context, bitmapClipBounds), PolygonPaintClip(raster, context), options.Limits.MaximumWorkUnits - workUnits, budget, out long analyticSetup, out bool useAnalytic, cancellationToken);
                if (analyticSetup > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage setup exceeds the configured limit.");
                workUnits += analyticSetup;
                // Charge the taken traversal: analytic rows cost pixels while the fan evaluates every contour per pixel.
                SvgAnalyticClipBounds paintArea = collectingClipCoverage is null ? context.Interior : context.BitmapClip;
                int startX = useAnalytic ? Math.Max(raster.StartX, paintArea.Left) : raster.StartX;
                int endX = useAnalytic ? Math.Min(raster.EndX, paintArea.Right) : raster.EndX;
                int startY = useAnalytic ? Math.Max(raster.StartY, paintArea.Top) : raster.StartY;
                int endY = useAnalytic ? Math.Min(raster.EndY, paintArea.Bottom) : raster.EndY;
                int clipLeft = context.BitmapClip.Right, clipTop = context.BitmapClip.Bottom, clipRight = context.BitmapClip.Left, clipBottom = context.BitmapClip.Top;
                long coverage = useAnalytic ? (long)(endY - startY) * (endX - startX) * (collectingClipCoverage is null ? 1 : 2) : (long)(endY - startY) * (endX - startX) * raster.Points.Length;
                if (coverage > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill raster work exceeds the configured limit.");
                workUnits += coverage;
                long scanSetup = 0;
                using SvgScanlineCoverage scan = useAnalytic ? default : SvgScanlineCoverage.Prepare(raster.Points, contourEnds, nestingSigns, polygon.IsEvenOdd, raster.Signs, raster.StartX, raster.EndX, budget, out scanSetup, cancellationToken);
                if (!useAnalytic)
                {
                    if (scanSetup > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage setup exceeds the configured limit.");
                    workUnits += scanSetup;
                }
                for (int y = startY; y < endY; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (useAnalytic)
                    {
                        walker.PrepareRow(y, options.Limits.MaximumWorkUnits - workUnits, out long analyticRowWork, cancellationToken);
                        if (analyticRowWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage setup exceeds the configured limit.");
                        workUnits += analyticRowWork;
                    }
                    else
                    {
                        scan.PrepareRow(y, out long rowWork, cancellationToken);
                        if (rowWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage setup exceeds the configured limit.");
                        workUnits += rowWork;
                    }
                    SvgAnalyticScanWindow paintWindow = useAnalytic ? walker.GetPaintWindow(new(startX, endX)) : new(startX, endX);
                    int rowStartX = useAnalytic ? Math.Max(paintWindow.Left, paintArea.Left) : startX;
                    int rowEndX = useAnalytic ? Math.Min(paintWindow.Right, paintArea.Right) : endX;
                    long extraPixels = (long)rowEndX - rowStartX - (endX - startX);
                    if (extraPixels > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Direct fringe painting exceeds the configured work limit.");
                    workUnits += extraPixels;
                    for (int x = rowStartX; x < rowEndX; x++)
                    {
                        if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                        double pixelCoverage = !useAnalytic && scan.MightCover(x) ? PixelCoverage(raster, contourEnds, nestingSigns, polygon.IsEvenOdd, raster.Signs, x, y, cancellationToken) : 0.0;
                        int offset = (y * outputWidth + x) * 4;
                        if (fontPreblend.Length != 0)
                        {
                            clipPixel.Clear();
                            if (useAnalytic) walker.PaintPixel(x - storageStartX, clipPixel, analyticPaint, SvgAnalyticClipCoverage.Full);
                            else clipPixel[3] = CoverageAlpha(255, pixelCoverage);
                            byte alpha = PolygonClipCoverage(raster, x, y).Apply(fontPreblend[clipPixel[3]]);
                            glyphPaint.Blend(destination.AsSpan(offset, 4), alpha, SvgAnalyticPaintKind.Mask);
                        }
                        else if (useAnalytic && collectingClipCoverage is not null)
                        {
                            clipPixel.Clear();
                            walker.PaintPixel(x - storageStartX, clipPixel, analyticPaint, PolygonClipCoverage(raster, x, y));
                            byte boundedAlpha = clipPixel[3];
                            if (activeConstructionClip.Length != 0)
                            {
                                byte enclosingAlpha = activeConstructionCoverage is null ? activeConstructionClip[offset + 3]
                                    : activeConstructionCoverage.GetAlpha(context.BitmapClip, activeConstructionClip, outputWidth, x, y);
                                boundedAlpha = (byte)((boundedAlpha * enclosingAlpha + 127) / 255);
                            }
                            collectingClipCoverage.SetBorderAlpha(context.BitmapClip, x, y, boundedAlpha);
                            if (boundedAlpha != 0)
                            {
                                clipLeft = Math.Min(clipLeft, x); clipTop = Math.Min(clipTop, y);
                                clipRight = Math.Max(clipRight, x + 1); clipBottom = Math.Max(clipBottom, y + 1);
                            }
                            if (x >= context.Interior.Left && x < context.Interior.Right && y >= context.Interior.Top && y < context.Interior.Bottom) clipPixel.CopyTo(destination.AsSpan(offset, 4));
                        }
                        else if (useAnalytic) walker.PaintPixel(x - storageStartX, destination.AsSpan(offset, 4), analyticPaint, PolygonClipCoverage(raster, x, y));
                        else if (surfaceMode == RasterSurfaceMode.PremultipliedPattern) analyticPaint.Blend(destination.AsSpan(offset, 4), ViewportAlpha(CoverageAlpha(255, pixelCoverage), x, y), SvgAnalyticPaintKind.Direct);
                        else BlendSourceOver(destination.AsSpan(offset, 4), WithCoverage(polygon.Color, pixelCoverage * (activeViewportClip.Length == 0 ? 1 : activeViewportClip[offset + 3] / 255.0)));
                    }
                }
                if (collectingClipCoverage is not null && useAnalytic)
                {
                    SvgAnalyticClipBounds nonzero = clipLeft < clipRight && clipTop < clipBottom ? new(clipLeft, clipTop, clipRight, clipBottom)
                        : new(context.BitmapClip.Left, context.BitmapClip.Top, context.BitmapClip.Left, context.BitmapClip.Top);
                    collectingClipCoverage.SetBounds(context.BitmapClip, nonzero);
                }
                // Structural fallback belongs to the original fill and runs once.
                if (!useAnalytic) break;
            }
        }
        SvgGradientRamp PrepareGradientRamp(ReadOnlySpan<SvgGradientStop> stops, SvgColor start, SvgColor end, double offset1, double offset2)
        {
            ParserWorkCounter preparation = new(options.Limits.MaximumWorkUnits - workUnits);
            SvgGradientRamp ramp = SvgGradientRamp.Create(stops, start, end, offset1, offset2, budget, preparation, cancellationToken);
            workUnits += preparation.Used;
            return ramp;
        }

        void RasterizeGradientPolygon(SvgGradientPolygon outline, SvgAuthoredPath? authoredPath, byte[] destination)
        {
            if (!outline.HasFill || outline.Points.Length < 3) return;
            // An objectBoundingBox paint on a zero-area box covers nothing: the stroke outline keeps its own extents while the paint box stays degenerate.
            if (!outline.Gradient.IsUserSpace && (outline.Box.Width <= 0 || outline.Box.Height <= 0)) return;
            using MappedPolygon raster = MapPolygon(outline.Points, outline.ContourEnds, outline.Signs, outline.IsEvenOdd, outline.QuadSpans, outline.CubicSpans, outline.Mapping, outline.Clip, authoredPath, PathCoordinateSpace.User);
            if (raster.EndY <= raster.StartY || raster.EndX <= raster.StartX) return;
            SvgLinearGradient definition = outline.Gradient;
            SvgRectangle box = outline.Box;
            double directionX = definition.X2 - definition.X1;
            double directionY = definition.Y2 - definition.Y1;
            double denominator = directionX * directionX + directionY * directionY;
            double offsetSpan = definition.Offset2 - definition.Offset1;
            SvgShaderCoordinates nativeCoordinates = SvgShaderCoordinates.CreateLinear(definition, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset);
            if (offsetSpan <= 0 && definition.Stops.Length != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only two-stop increasing-offset linear gradients are implemented.");
            using SvgGradientRamp ramp = denominator != 0 || definition.Stops.Length == 1
                ? PrepareGradientRamp(definition.Stops, definition.Start, definition.End, definition.Offset1, definition.Offset2) : default;

            ReadOnlySpan<int> contourEnds = raster.ContourEnds;
            ReadOnlySpan<int> nestingSigns = raster.Signs is int[] signs ? signs : Array.Empty<int>();
            SvgAnalyticContourPreparation preparation = new(raster.Points, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, raster.Mapping, raster.QuadSpans, raster.CubicSpans, raster.CompleteBounds.RoundOutForDispatch(), outputWidth, outputHeight, raster.Authored, raster.AuthoredMap);
            preparation.RectangleFrame = PrepareRectangleFrame(authoredPath);
            foreach (SvgCpuRasterContext cpuTile in rasterGrid.GetContexts(new(raster.StartX, raster.StartY, raster.EndX, raster.EndY)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgCpuRasterContext context = LayerRasterContext(cpuTile);
                if (context.Interior.Right <= context.Interior.Left || context.Interior.Bottom <= context.Interior.Top) continue;
                if (workUnits >= options.Limits.MaximumWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "CPU raster context traversal exceeds the configured limit.");
                workUnits++;
                if (raster.Clip is not null)
                {
                    if (4 > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle paint-clip classification exceeds the configured limit.");
                    workUnits += 4;
                }
                SvgAnalyticClipBounds? bitmapClipBounds = ActiveClipBounds(context);
                int storageStartX = Math.Max(raster.PathStartX, context.BitmapClip.Left);
                int storageEndX = Math.Min(raster.PathEndX, context.BitmapClip.Right);
                using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, storageStartX, storageEndX, raster.PathStartY, raster.PathEndY, raster.GetScanWindow(context, bitmapClipBounds), context.Origin, raster.GetConstructionClip(context, bitmapClipBounds), PolygonPaintClip(raster, context), options.Limits.MaximumWorkUnits - workUnits, budget, out long analyticSetup, out bool useAnalytic, cancellationToken);
                if (analyticSetup > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage setup exceeds the configured limit.");
                workUnits += analyticSetup;
                int startX = useAnalytic ? Math.Max(raster.StartX, context.Interior.Left) : raster.StartX;
                int endX = useAnalytic ? Math.Min(raster.EndX, context.Interior.Right) : raster.EndX;
                int startY = useAnalytic ? Math.Max(raster.StartY, context.Interior.Top) : raster.StartY;
                int endY = useAnalytic ? Math.Min(raster.EndY, context.Interior.Bottom) : raster.EndY;
                long coverage = useAnalytic ? (long)(endY - startY) * (endX - startX) : (long)(endY - startY) * (endX - startX) * raster.Points.Length;
                if (coverage > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill raster work exceeds the configured limit.");
                workUnits += coverage;
                long scanSetup = 0;
                using SvgScanlineCoverage scan = useAnalytic ? default : SvgScanlineCoverage.Prepare(raster.Points, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, raster.StartX, raster.EndX, budget, out scanSetup, cancellationToken);
                if (!useAnalytic)
                {
                    if (scanSetup > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage setup exceeds the configured limit.");
                    workUnits += scanSetup;
                }
                if (denominator != 0 || definition.Stops.Length == 1)
                for (int y = startY; y < endY; y++)
                {
                cancellationToken.ThrowIfCancellationRequested();
                if (useAnalytic)
                {
                    walker.PrepareRow(y, options.Limits.MaximumWorkUnits - workUnits, out long analyticRowWork, cancellationToken);
                    if (analyticRowWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage setup exceeds the configured limit.");
                    workUnits += analyticRowWork;
                }
                else
                {
                    scan.PrepareRow(y, out long rowWork, cancellationToken);
                    if (rowWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage setup exceeds the configured limit.");
                    workUnits += rowWork;
                }
                double deviceY = (y + 0.5 - verticalOffset) / verticalScale;
                SvgAnalyticScanWindow paintWindow = useAnalytic ? walker.GetPaintWindow(new(startX, endX)) : new(startX, endX);
                int rowStartX = useAnalytic ? Math.Max(paintWindow.Left, context.Interior.Left) : startX;
                int rowEndX = useAnalytic ? Math.Min(paintWindow.Right, context.Interior.Right) : endX;
                long extraPixels = (long)rowEndX - rowStartX - (endX - startX);
                if (extraPixels > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Direct fringe painting exceeds the configured work limit.");
                workUnits += extraPixels;
                for (int x = rowStartX; x < rowEndX; x++)
                {
                    if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                    double pixelCoverage = !useAnalytic && scan.MightCover(x) ? PixelCoverage(raster, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, x, y, cancellationToken) : 0.0;
                    double deviceX = (x + 0.5 - horizontalOffset) / horizontalScale;
                    double position;
                    if (!nativeCoordinates.TryLinear(x, y, ShaderBitmapOrigin(x, y), out position))
                    {
                        if (definition.SourcePaint is { Data.IsMapped: true } sourcePaint) position = sourcePaint.Position(deviceX, deviceY);
                        else if (definition.IsUserSpace && Math.Abs(definition.ReferenceScaleX) != Math.Abs(definition.ReferenceScaleY))
                        {
                            double relativeX = (deviceX - definition.X1) / definition.ReferenceScaleX;
                            double relativeY = (deviceY - definition.Y1) / definition.ReferenceScaleY;
                            double axisX = directionX / definition.ReferenceScaleX;
                            double axisY = directionY / definition.ReferenceScaleY;
                            position = (relativeX * axisX + relativeY * axisY) / (axisX * axisX + axisY * axisY);
                        }
                        else if (definition.IsUserSpace) position = ((deviceX - definition.X1) * directionX + (deviceY - definition.Y1) * directionY) / denominator;
                        else
                        {
                            double normalizedX = (deviceX - box.X) / box.Width;
                            double normalizedY = (deviceY - box.Y) / box.Height;
                            position = ((normalizedX - definition.X1) * directionX + (normalizedY - definition.Y1) * directionY) / denominator;
                        }
                    }
                    double local = definition.Stops.Length == 1 ? 0 : (position - definition.Offset1) / offsetSpan;
                    if (definition.Spread == SvgGradientSpread.Repeat) local -= Math.Floor(local);
                    else if (definition.Spread == SvgGradientSpread.Reflect)
                    {
                        double cycle = local - 2 * Math.Floor(local / 2);
                        local = cycle > 1 ? 2 - cycle : cycle;
                    }
                    else
                    {
                        if (local < 0) local = 0;
                        if (local > 1) local = 1;
                    }
                    position = local;
                    int offset = (y * outputWidth + x) * 4;
                    SvgShaderPaint sample = ramp.SampleLinear(position, ShaderAlpha(outline.Opacity), GradientDither(x, y));
                    if (useAnalytic) walker.PaintPixel(x - storageStartX, destination.AsSpan(offset, 4), SvgAnalyticPaint.Shader(sample), PolygonClipCoverage(raster, x, y));
                    else sample.Blend(destination.AsSpan(offset, 4), ViewportAlpha(CoverageAlpha(255, pixelCoverage), x, y));
                }
                }
                // Structural fallback belongs to the original fill and runs once.
                if (!useAnalytic) break;
            }
        }
        void RasterizeRadialPolygon(SvgRadialPolygon outline, SvgAuthoredPath? authoredPath, byte[] destination)
        {
            if (!outline.HasFill || outline.Points.Length < 3) return;
            // An objectBoundingBox paint on a zero-area box covers nothing: the stroke outline keeps its own extents while the paint box stays degenerate.
            if (!outline.Gradient.IsUserSpace && (outline.Box.Width <= 0 || outline.Box.Height <= 0)) return;
            using MappedPolygon raster = MapPolygon(outline.Points, outline.ContourEnds, outline.Signs, outline.IsEvenOdd, outline.QuadSpans, outline.CubicSpans, outline.Mapping, outline.Clip, authoredPath, PathCoordinateSpace.User);
            if (raster.EndY <= raster.StartY || raster.EndX <= raster.StartX) return;
            SvgRadialGradient definition = outline.Gradient;
            SvgRectangle box = outline.Box;
            SvgShaderCoordinates nativeCoordinates = SvgShaderCoordinates.CreateRadial(definition, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset);
            if (definition.Radius <= 0 && definition.Stops.Length != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive-radius radial gradients are implemented.");
            using SvgGradientRamp ramp = PrepareGradientRamp(definition.Stops, definition.Start, definition.End, definition.Offset1, definition.Offset2);

            ReadOnlySpan<int> contourEnds = raster.ContourEnds;
            ReadOnlySpan<int> nestingSigns = raster.Signs is int[] signs ? signs : Array.Empty<int>();
            SvgAnalyticContourPreparation preparation = new(raster.Points, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, raster.Mapping, raster.QuadSpans, raster.CubicSpans, raster.CompleteBounds.RoundOutForDispatch(), outputWidth, outputHeight, raster.Authored, raster.AuthoredMap);
            preparation.RectangleFrame = PrepareRectangleFrame(authoredPath);
            foreach (SvgCpuRasterContext cpuTile in rasterGrid.GetContexts(new(raster.StartX, raster.StartY, raster.EndX, raster.EndY)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgCpuRasterContext context = LayerRasterContext(cpuTile);
                if (context.Interior.Right <= context.Interior.Left || context.Interior.Bottom <= context.Interior.Top) continue;
                if (workUnits >= options.Limits.MaximumWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "CPU raster context traversal exceeds the configured limit.");
                workUnits++;
                if (raster.Clip is not null)
                {
                    if (4 > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle paint-clip classification exceeds the configured limit.");
                    workUnits += 4;
                }
                SvgAnalyticClipBounds? bitmapClipBounds = ActiveClipBounds(context);
                int storageStartX = Math.Max(raster.PathStartX, context.BitmapClip.Left);
                int storageEndX = Math.Min(raster.PathEndX, context.BitmapClip.Right);
                using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, storageStartX, storageEndX, raster.PathStartY, raster.PathEndY, raster.GetScanWindow(context, bitmapClipBounds), context.Origin, raster.GetConstructionClip(context, bitmapClipBounds), PolygonPaintClip(raster, context), options.Limits.MaximumWorkUnits - workUnits, budget, out long analyticSetup, out bool useAnalytic, cancellationToken);
                if (analyticSetup > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage setup exceeds the configured limit.");
                workUnits += analyticSetup;
                int startX = useAnalytic ? Math.Max(raster.StartX, context.Interior.Left) : raster.StartX;
                int endX = useAnalytic ? Math.Min(raster.EndX, context.Interior.Right) : raster.EndX;
                int startY = useAnalytic ? Math.Max(raster.StartY, context.Interior.Top) : raster.StartY;
                int endY = useAnalytic ? Math.Min(raster.EndY, context.Interior.Bottom) : raster.EndY;
                long coverage = useAnalytic ? (long)(endY - startY) * (endX - startX) : (long)(endY - startY) * (endX - startX) * raster.Points.Length;
                if (coverage > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill raster work exceeds the configured limit.");
                workUnits += coverage;
                long scanSetup = 0;
                using SvgScanlineCoverage scan = useAnalytic ? default : SvgScanlineCoverage.Prepare(raster.Points, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, raster.StartX, raster.EndX, budget, out scanSetup, cancellationToken);
                if (!useAnalytic)
                {
                    if (scanSetup > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage setup exceeds the configured limit.");
                    workUnits += scanSetup;
                }
                for (int y = startY; y < endY; y++)
                {
                cancellationToken.ThrowIfCancellationRequested();
                if (useAnalytic)
                {
                    walker.PrepareRow(y, options.Limits.MaximumWorkUnits - workUnits, out long analyticRowWork, cancellationToken);
                    if (analyticRowWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage setup exceeds the configured limit.");
                    workUnits += analyticRowWork;
                }
                else
                {
                    scan.PrepareRow(y, out long rowWork, cancellationToken);
                    if (rowWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage setup exceeds the configured limit.");
                    workUnits += rowWork;
                }
                double deviceY = (y + 0.5 - verticalOffset) / verticalScale;
                SvgAnalyticScanWindow paintWindow = useAnalytic ? walker.GetPaintWindow(new(startX, endX)) : new(startX, endX);
                int rowStartX = useAnalytic ? Math.Max(paintWindow.Left, context.Interior.Left) : startX;
                int rowEndX = useAnalytic ? Math.Min(paintWindow.Right, context.Interior.Right) : endX;
                long extraPixels = (long)rowEndX - rowStartX - (endX - startX);
                if (extraPixels > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Direct fringe painting exceeds the configured work limit.");
                workUnits += extraPixels;
                for (int x = rowStartX; x < rowEndX; x++)
                {
                    if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                    double pixelCoverage = !useAnalytic && scan.MightCover(x) ? PixelCoverage(raster, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, x, y, cancellationToken) : 0.0;
                    double deviceX = (x + 0.5 - horizontalOffset) / horizontalScale;
                    double sampleX;
                    double sampleY;
                    double focalX;
                    double focalY;
                    double circleX;
                    double circleY;
                    if (definition.IsUserSpace)
                    {
                        sampleX = deviceX;
                        sampleY = deviceY;
                        if (definition.HasGeneralTransform) { (sampleX, sampleY) = definition.Inverse.Apply(sampleX, sampleY); }
                        focalX = definition.FocalX;
                        focalY = definition.FocalY;
                        circleX = definition.CenterX;
                        circleY = definition.CenterY;
                    }
                    else
                    {
                        sampleX = (deviceX - box.X) / box.Width;
                        sampleY = (deviceY - box.Y) / box.Height;
                        if (definition.HasGeneralTransform) { (sampleX, sampleY) = definition.Inverse.Apply(sampleX, sampleY); }
                        focalX = definition.FocalX;
                        focalY = definition.FocalY;
                        circleX = definition.CenterX;
                        circleY = definition.CenterY;
                    }
                    double radius = definition.Radius;
                    double focalRadius = definition.FocalRadius;
                    if (definition.IsUserSpace && Math.Abs(definition.ReferenceScaleX) != Math.Abs(definition.ReferenceScaleY))
                    {
                        sampleX = (sampleX - circleX) / definition.ReferenceScaleX;
                        sampleY = (sampleY - circleY) / definition.ReferenceScaleY;
                        focalX = (focalX - circleX) / definition.ReferenceScaleX;
                        focalY = (focalY - circleY) / definition.ReferenceScaleY;
                        circleX = 0;
                        circleY = 0;
                        radius = definition.Radius / Math.Abs(definition.ReferenceScaleX);
                        focalRadius = definition.FocalRadius / Math.Abs(definition.ReferenceScaleX);
                    }
                    double offsetX = circleX - focalX;
                    double offsetY = circleY - focalY;
                    double rayX = sampleX - focalX;
                    double rayY = sampleY - focalY;
                    double position;
                    if (definition.Stops.Length == 1) position = 0;
                    else
                    if (nativeCoordinates.TryRadial(x, y, ShaderBitmapOrigin(x, y), out position)) { }
                    else if (offsetX == 0 && offsetY == 0)
                    {
                        position = (Math.Sqrt(rayX * rayX + rayY * rayY) - focalRadius) / (radius - focalRadius);
                    }
                    else if (rayX == 0 && rayY == 0)
                    {
                        position = 0;
                    }
                    else if (focalRadius == 0)
                    {
                        double dot = rayX * offsetX + rayY * offsetY;
                        double raySquared = rayX * rayX + rayY * rayY;
                        double discriminant = dot * dot + raySquared * (radius * radius - offsetX * offsetX - offsetY * offsetY);
                        if (discriminant < 0) discriminant = 0;
                        position = raySquared / (dot + Math.Sqrt(discriminant));
                    }
                    else
                    {
                        double span = radius - focalRadius;
                        double dot = rayX * offsetX + rayY * offsetY;
                        double raySquared = rayX * rayX + rayY * rayY;
                        double linear = dot + focalRadius * span;
                        double quadratic = offsetX * offsetX + offsetY * offsetY - span * span;
                        double constant = raySquared - focalRadius * focalRadius;
                        double discriminant = linear * linear - quadratic * constant;
                        if (discriminant < 0) discriminant = 0;
                        position = (linear - Math.Sqrt(discriminant)) / quadratic;
                    }
                    if (definition.Spread == SvgGradientSpread.Repeat) position -= Math.Floor(position);
                    else if (definition.Spread == SvgGradientSpread.Reflect)
                    {
                        double cycle = position - 2 * Math.Floor(position / 2);
                        position = cycle > 1 ? 2 - cycle : cycle;
                    }
                    else
                    {
                        if (position < 0) position = 0;
                        if (position > 1) position = 1;
                    }
                    int offset = (y * outputWidth + x) * 4;
                    SvgShaderPaint sample = ramp.SampleRadial(position, ShaderAlpha(outline.Opacity), GradientDither(x, y));
                    if (useAnalytic) walker.PaintPixel(x - storageStartX, destination.AsSpan(offset, 4), SvgAnalyticPaint.Shader(sample), PolygonClipCoverage(raster, x, y));
                    else sample.Blend(destination.AsSpan(offset, 4), ViewportAlpha(CoverageAlpha(255, pixelCoverage), x, y));
                }
                }
                // Structural fallback belongs to the original fill and runs once.
                if (!useAnalytic) break;
            }
        }

        void RasterizePatternPolygon(SvgPatternPolygon outline, SvgAuthoredPath? authoredPath, byte[] destination)
        {
            if (!outline.HasFill || outline.Points.Length < 3) return;
            using MappedPolygon raster = MapPolygon(outline.Points, outline.ContourEnds, outline.Signs, outline.IsEvenOdd, outline.QuadSpans, outline.CubicSpans, outline.Mapping, outline.Clip, authoredPath, PathCoordinateSpace.User);
            if (raster.EndY <= raster.StartY || raster.EndX <= raster.StartX) return;
            using RenderBudget.BudgetReservation sourcePatternOwner = budget.Reserve(512, SvgMemoryCategory.Geometry);
            SvgPatternGeometry source = new(outline.Content, outline.OriginX, outline.OriginY, outline.TileWidth, outline.TileHeight, default, outline.Opacity, null) { ContentPaint = outline.ContentPaint, VectorContent = outline.VectorContent, ContentViewBox = outline.ContentViewBox, ShaderPlacement = outline.ShaderPlacement };
            using RenderBudget.BudgetReservation? mappedPatternOwner = NeedsPatternMapping(source) ? budget.Reserve(512, SvgMemoryCategory.Geometry) : null;
            if (!TryMapPatternTile(source, out SvgPatternGeometry? mappedTile)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills under viewBox or output scaling are not implemented.");
            SvgPatternGeometry tile = mappedTile ?? source;
            using SvgPatternTileLease texture = BorrowPatternTile(source, tile, false);
            byte[] blitted = texture.Surface.Pixels;
            SvgShaderCoordinates imageCoordinates = tile.NativeImageMap is {} imageMap ? SvgShaderCoordinates.CreateImage(imageMap, tile.NativeImageLocal ?? throw new InvalidOperationException("Image sampling requires its local matrix.")) : default;
            ReadOnlySpan<int> contourEnds = raster.ContourEnds;
            ReadOnlySpan<int> nestingSigns = raster.Signs is int[] signs ? signs : Array.Empty<int>();
            SvgAnalyticContourPreparation preparation = new(raster.Points, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, raster.Mapping, raster.QuadSpans, raster.CubicSpans, raster.CompleteBounds.RoundOutForDispatch(), outputWidth, outputHeight, raster.Authored, raster.AuthoredMap);
            preparation.RectangleFrame = PrepareRectangleFrame(authoredPath);
            foreach (SvgCpuRasterContext cpuTile in rasterGrid.GetContexts(new(raster.StartX, raster.StartY, raster.EndX, raster.EndY)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgCpuRasterContext context = LayerRasterContext(cpuTile);
                if (context.Interior.Right <= context.Interior.Left || context.Interior.Bottom <= context.Interior.Top) continue;
                if (workUnits >= options.Limits.MaximumWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "CPU raster context traversal exceeds the configured limit.");
                workUnits++;
                if (raster.Clip is not null)
                {
                    if (4 > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle paint-clip classification exceeds the configured limit.");
                    workUnits += 4;
                }
                SvgAnalyticClipBounds? bitmapClipBounds = ActiveClipBounds(context);
                int storageStartX = Math.Max(raster.PathStartX, context.BitmapClip.Left);
                int storageEndX = Math.Min(raster.PathEndX, context.BitmapClip.Right);
                using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, storageStartX, storageEndX, raster.PathStartY, raster.PathEndY, raster.GetScanWindow(context, bitmapClipBounds), context.Origin, raster.GetConstructionClip(context, bitmapClipBounds), PolygonPaintClip(raster, context), options.Limits.MaximumWorkUnits - workUnits, budget, out long analyticSetup, out bool useAnalytic, cancellationToken);
                if (analyticSetup > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage setup exceeds the configured limit.");
                workUnits += analyticSetup;
                int startX = useAnalytic ? Math.Max(raster.StartX, context.Interior.Left) : raster.StartX;
                int endX = useAnalytic ? Math.Min(raster.EndX, context.Interior.Right) : raster.EndX;
                int startY = useAnalytic ? Math.Max(raster.StartY, context.Interior.Top) : raster.StartY;
                int endY = useAnalytic ? Math.Min(raster.EndY, context.Interior.Bottom) : raster.EndY;
                long coverage = useAnalytic ? (long)(endY - startY) * (endX - startX) : (long)(endY - startY) * (endX - startX) * raster.Points.Length;
                if (coverage > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill raster work exceeds the configured limit.");
                workUnits += coverage;
                long scanSetup = 0;
                using SvgScanlineCoverage scan = useAnalytic ? default : SvgScanlineCoverage.Prepare(raster.Points, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, raster.StartX, raster.EndX, budget, out scanSetup, cancellationToken);
                if (!useAnalytic)
                {
                    if (scanSetup > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage setup exceeds the configured limit.");
                    workUnits += scanSetup;
                }
                for (int y = startY; y < endY; y++)
                {
                cancellationToken.ThrowIfCancellationRequested();
                if (useAnalytic)
                {
                    walker.PrepareRow(y, options.Limits.MaximumWorkUnits - workUnits, out long analyticRowWork, cancellationToken);
                    if (analyticRowWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage setup exceeds the configured limit.");
                    workUnits += analyticRowWork;
                }
                else
                {
                    scan.PrepareRow(y, out long rowWork, cancellationToken);
                    if (rowWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage setup exceeds the configured limit.");
                    workUnits += rowWork;
                }
                SvgAnalyticScanWindow paintWindow = useAnalytic ? walker.GetPaintWindow(new(startX, endX)) : new(startX, endX);
                int rowStartX = useAnalytic ? Math.Max(paintWindow.Left, context.Interior.Left) : startX;
                int rowEndX = useAnalytic ? Math.Min(paintWindow.Right, context.Interior.Right) : endX;
                long extraPixels = (long)rowEndX - rowStartX - (endX - startX);
                if (extraPixels > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Direct fringe painting exceeds the configured work limit.");
                workUnits += extraPixels;
                for (int x = rowStartX; x < rowEndX; x++)
                {
                    if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                    double pixelCoverage = !useAnalytic && scan.MightCover(x) ? PixelCoverage(raster, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, x, y, cancellationToken) : 0.0;
                    int offset = (y * outputWidth + x) * 4;
                    SvgShaderPaint sample = SamplePattern(tile, blitted, x, y, ref imageCoordinates);
                    if (useAnalytic) walker.PaintPixel(x - storageStartX, destination.AsSpan(offset, 4), SvgAnalyticPaint.Shader(sample), PolygonClipCoverage(raster, x, y));
                    else sample.Blend(destination.AsSpan(offset, 4), ViewportAlpha(CoverageAlpha(255, pixelCoverage), x, y));
                }
                }
                // Structural fallback belongs to the original fill and runs once.
                if (!useAnalytic) break;
            }
        }

        bool NeedsPatternMapping(SvgPatternGeometry pattern)
            => pattern.VectorContent is not null || horizontalScale != 1 || verticalScale != 1 || horizontalOffset != 0 || verticalOffset != 0 || pattern.ShaderPlacement != SvgAffineTransform.Identity;

        bool TryMapPatternTile(SvgPatternGeometry pattern, out SvgPatternGeometry? mapped)
        {
            mapped = null;
            SvgAffineTransform shader = pattern.ShaderPlacement;
            if (horizontalScale == 1 && verticalScale == 1 && horizontalOffset == 0 && verticalOffset == 0 && shader == SvgAffineTransform.Identity && pattern.VectorContent is null) return true;
            bool positiveAxis = shader.IsAxisAligned && shader.A > 0 && shader.D > 0;
            if (pattern.VectorContent is null && !positiveAxis) return false;
            double scaleX = shader.A * horizontalScale, scaleY = shader.D * verticalScale;
            if (pattern.Content.Stroke is not null && (scaleX != 1 || scaleY != 1)) return false;
            double originX = (pattern.OriginX * shader.A + shader.E) * horizontalScale + horizontalOffset;
            double originY = (pattern.OriginY * shader.D + shader.F) * verticalScale + verticalOffset;
            double tileWidth = pattern.TileWidth * scaleX, tileHeight = pattern.TileHeight * scaleY;
            if (pattern.VectorContent is not null)
            {
                float width = (float)pattern.TileWidth, height = (float)pattern.TileHeight;
                SvgShaderMatrix outputMap = new((float)horizontalScale, 0, (float)horizontalOffset, 0, (float)verticalScale, (float)verticalOffset);
                SvgShaderMatrix localMap = new((float)shader.A, (float)shader.C, (float)shader.E, (float)shader.B, (float)shader.D, (float)shader.F);
                SvgShaderMatrix map = SvgShaderMatrix.Concat(outputMap, localMap);
                // Picture tiles retain their source axes. Rotation/reflection change the
                // sampling matrix, while column lengths choose the bounded texture scale.
                (float rasterScaleX, float rasterScaleY) = positiveAxis
                    ? (map.ScaleX, map.ScaleY) : map.GetPictureScale();
                double pixelsX = MathF.Ceiling(width * rasterScaleX), pixelsY = MathF.Ceiling(height * rasterScaleY);
                if (pixelsX <= 0 || pixelsY <= 0 || pixelsX > options.Limits.MaximumDimension || pixelsY > options.Limits.MaximumDimension)
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern tile dimensions exceed the configured limit.");
                float tileScaleX = (float)pixelsX / width, tileScaleY = (float)pixelsY / height;
                SvgShaderMatrix imageLocal = SvgShaderMatrix.Concat(localMap, new(1f / tileScaleX, 0, (float)pattern.OriginX, 0, 1f / tileScaleY, (float)pattern.OriginY));
                SvgShaderMatrix imageMap = SvgShaderMatrix.Concat(outputMap, imageLocal);
                if (!imageMap.TryInvert(out _)) return false;
                mapped = new(default, 0, 0, pixelsX, pixelsY, pattern.Fill, pattern.Opacity, pattern.Stroke)
                {
                    VectorContent = pattern.VectorContent, ContentViewBox = pattern.ContentViewBox,
                    ShaderPlacement = SvgAffineTransform.FromScaleOffset(1, 1, originX, originY),
                    RasterSourceBox = new(0, 0, width, height, SvgViewBoxAlignment.None, SvgViewBoxAlignment.None, SvgViewBoxFit.None),
                    NativeImageMap = positiveAxis && IsDeviceInteger(tileWidth) && IsDeviceInteger(tileHeight) ? null : outputMap,
                    NativeImageLocal = positiveAxis && IsDeviceInteger(tileWidth) && IsDeviceInteger(tileHeight) ? null : imageLocal,
                };
                return true;
            }
            if (!IsDeviceInteger(tileWidth) || !IsDeviceInteger(tileHeight) || !float.IsFinite((float)originX) || !float.IsFinite((float)originY)) return false;
            double contentX = (pattern.Content.X - pattern.OriginX) * scaleX;
            double contentY = (pattern.Content.Y - pattern.OriginY) * scaleY;
            double contentWidth = pattern.Content.Width * scaleX, contentHeight = pattern.Content.Height * scaleY;
            SvgClipBox? contentClip = pattern.Content.Clip is {} clip ? new SvgClipBox((clip.X - pattern.OriginX) * scaleX, (clip.Y - pattern.OriginY) * scaleY, clip.Width * scaleX, clip.Height * scaleY) { Antialiasing = clip.Antialiasing } : null;
            mapped = new SvgPatternGeometry(new SvgRectangle(contentX, contentY, contentWidth, contentHeight, pattern.Content.Color, pattern.Content.HasFill, pattern.Content.Stroke) { Clip = contentClip }, 0, 0, (int)tileWidth, (int)tileHeight, pattern.Fill, pattern.Opacity, pattern.Stroke)
                { ShaderPlacement = SvgAffineTransform.FromScaleOffset(1, 1, originX, originY), ContentPaint = pattern.ContentPaint, VectorContent = pattern.VectorContent, ContentViewBox = pattern.ContentViewBox };
            return true;

            static bool IsDeviceInteger(double value)
                => double.IsFinite(value) && value == Math.Truncate(value) && value >= int.MinValue && value <= int.MaxValue;
        }

        void RasterizePatternFill(SvgPatternGeometry pattern, byte[] destination, SvgPaintPass pass)
        {
            if (!pattern.Fill.HasFill && pattern.Stroke is null) return;
            using RenderBudget.BudgetReservation? mappedPatternOwner = NeedsPatternMapping(pattern) ? budget.Reserve(512, SvgMemoryCategory.Geometry) : null;
            if (!TryMapPatternTile(pattern, out SvgPatternGeometry? mapped)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills under viewBox or output scaling are not implemented.");
            SvgPatternGeometry source = mapped ?? pattern;
            if (pass != SvgPaintPass.Stroke && source.Fill.HasFill)
            {
                bool retain = source.Stroke is null && nextPatternTile is {} next && SvgPatternTileCache.SameTile(pattern, next);
                if (!retain && patternTileCache is null)
                {
                    using RasterizedSurface texture = PreparePatternTile(source);
                    StampPatternFill(source, texture.Pixels, destination);
                }
                else
                {
                    using SvgPatternTileLease texture = BorrowPatternTile(pattern, source, retain);
                    StampPatternFill(source, texture.Surface.Pixels, destination);
                }
            }
            if (pass != SvgPaintPass.Fill && source.Stroke is SvgStroke stroke) RasterizePatternStroke(source, stroke, destination);
        }

        void ReleasePatternTile()
        {
            patternTileCache?.Dispose();
            patternTileCache = null;
        }

        SvgPatternGeometry? NextPatternTile(int index, int end)
        {
            if (index >= end) return null;
            SvgDrawCommand next = root.Commands[index];
            if (next.Kind != SvgDrawCommandKind.Geometry || (uint)next.GeometryIndex >= (uint)root.Geometries.Length) return null;
            SvgGeometry geometry = root.Geometries[next.GeometryIndex];
            return geometry.Kind == SvgGeometryKind.PatternFill && geometry.Pattern.Stroke is null && geometry.LeafMask is null ? geometry.Pattern : null;
        }

        SvgPatternTileLease BorrowPatternTile(SvgPatternGeometry original, SvgPatternGeometry mapped, bool retain)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (patternTileCache is {} cached && cached.TryBorrow(mapped, out RasterizedSurface existing)) return new(existing, false);
            ReleasePatternTile();
            RasterizedSurface prepared = PreparePatternTile(mapped);
            try
            {
                patternTileCache = retain ? SvgPatternTileCache.TryRetain(original, mapped, prepared, budget) : null;
                return new(prepared, patternTileCache is null);
            }
            catch { prepared.Dispose(); throw; }
        }

        // Tile scenes borrow their parent-owned storage and use the same native geometry/paint
        // machinery as ordinary scenes. Only the final capture conversion is omitted.
        RasterizedSurface PreparePatternTile(SvgPatternGeometry pattern)
        {
            long tilePixels = (long)pattern.TextureWidth * pattern.TextureHeight;
            if (tilePixels <= 0 || tilePixels > options.Limits.MaximumPixels || tilePixels > int.MaxValue / 4) throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern tile surface exceeds the configured pixel limit.");
            cancellationToken.ThrowIfCancellationRequested();
            if (pattern.VectorContent is {} content)
            {
                long remaining = options.Limits.MaximumWorkUnits - workUnits;
                if (remaining <= 0) throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern tile rendering exceeds the configured work limit.");
                using RenderBudget.BudgetReservation setup = budget.Reserve(256, SvgMemoryCategory.Geometry);
                SvgRenderLimits limits = options.Limits;
                SvgRenderLimits tileLimits = new(limits.MaximumAccountedBytes, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels, limits.MaximumEncodedPngBytes, limits.MaximumNodes, limits.MaximumPathCommands, checked((int)remaining), limits.MaximumXmlDepth, limits.MaximumAttributesPerElement);
                SvgRenderOptions tileOptions = new(new SvgViewport(pattern.TextureWidth, pattern.TextureHeight), 1, SvgColor.Transparent, tileLimits);
                SvgSceneStorage scene = content.Scene;
                SvgRoot tileRoot = new(null, null, pattern.RasterSourceBox ?? throw new InvalidOperationException("Vector tile replay requires a mapped source grid."), scene.Geometries, scene.Commands, false, 0, scene);
                RasterFrame? contentFrame = null;
                if (pattern.ContentViewBox is {} contentViewBox && pattern.RasterSourceBox is {} sourceBox)
                {
                    (double sx, double sy, double tx, double ty) = SvgGeometrySyntax.MapViewBox(contentViewBox, sourceBox.Width, sourceBox.Height);
                    SvgShaderMatrix contentMap = new((float)sx, 0, (float)tx, 0, (float)sy, (float)ty);
                    SvgShaderMatrix pixelMap = new(pattern.TextureWidth / (float)sourceBox.Width, 0, 0, 0, pattern.TextureHeight / (float)sourceBox.Height, 0);
                    SvgShaderMatrix map = SvgShaderMatrix.Concat(pixelMap, contentMap);
                    contentFrame = new(pattern.TextureWidth, pattern.TextureHeight, map.ScaleX, map.TranslateX, map.ScaleY, map.TranslateY);
                }
                RasterizedSurface rendered = Rasterize(tileRoot, tileOptions, budget, RasterSurfaceMode.PremultipliedPattern, contentFrame, cancellationToken);
                workUnits += rendered.WorkUnits;
                return rendered;
            }
            RenderBudget.BudgetReservation reservation = budget.Reserve(checked(tilePixels * 4), SvgMemoryCategory.Surface);
            try
            {
                byte[] pixels = new byte[(int)tilePixels * 4];
                BlitPatternTile(pattern, pixels);
                return new RasterizedSurface(pattern.TextureWidth, pattern.TextureHeight, pixels, reservation, 0);
            }
            catch { reservation.Dispose(); throw; }
        }

        void BlitPatternTile(SvgPatternGeometry pattern, byte[] tile)
        {
            BlitPatternBox(pattern.Content, tile, pattern, pattern.ContentPaint);
            if (pattern.Content.Stroke is SvgStroke stroke)
            {
                double halfWidth = stroke.Width / 2d;
                BlitPatternBox(new SvgRectangle(pattern.Content.X - halfWidth, pattern.Content.Y - halfWidth, pattern.Content.Width + stroke.Width, stroke.Width, stroke.Color, true, null) with { Clip = pattern.Content.Clip }, tile, pattern, null);
                BlitPatternBox(new SvgRectangle(pattern.Content.X - halfWidth, pattern.Content.Y + pattern.Content.Height - halfWidth, pattern.Content.Width + stroke.Width, stroke.Width, stroke.Color, true, null) with { Clip = pattern.Content.Clip }, tile, pattern, null);
                BlitPatternBox(new SvgRectangle(pattern.Content.X - halfWidth, pattern.Content.Y + halfWidth, stroke.Width, pattern.Content.Height - stroke.Width, stroke.Color, true, null) with { Clip = pattern.Content.Clip }, tile, pattern, null);
                BlitPatternBox(new SvgRectangle(pattern.Content.X + pattern.Content.Width - halfWidth, pattern.Content.Y + halfWidth, stroke.Width, pattern.Content.Height - stroke.Width, stroke.Color, true, null) with { Clip = pattern.Content.Clip }, tile, pattern, null);
            }
        }

        void BlitPatternBox(SvgRectangle box, byte[] tile, SvgPatternGeometry pattern, SvgSolidPaint? sourcePaint)
        {
            int setupVisits = box.Clip is null ? 4 : 8;
            if (setupVisits > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern rectangle preparation exceeds the configured work limit.");
            workUnits += setupVisits;
            cancellationToken.ThrowIfCancellationRequested();
            double left = box.X - pattern.OriginX, top = box.Y - pattern.OriginY;
            double right = left + box.Width, bottom = top + box.Height;
            SvgRectangleCoverage coverage = SvgRectangleCoverage.Create(left, top, right, bottom, pattern.TextureWidth, pattern.TextureHeight);
            SvgClipBox? clip = box.Clip is {} bounds ? new SvgClipBox(bounds.X - pattern.OriginX, bounds.Y - pattern.OriginY, bounds.Width, bounds.Height) { Antialiasing = bounds.Antialiasing } : null;
            int startX = ClampFloor(clip is {} cx ? Math.Max(left, cx.X) : left, pattern.TextureWidth);
            int startY = ClampFloor(clip is {} cy ? Math.Max(top, cy.Y) : top, pattern.TextureHeight);
            int endX = Math.Max(startX, ClampCeiling(clip is {} ex ? Math.Min(right, ex.Right) : right, pattern.TextureWidth));
            int endY = Math.Max(startY, ClampCeiling(clip is {} ey ? Math.Min(bottom, ey.Bottom) : bottom, pattern.TextureHeight));
            long blitted = (long)(endY - startY) * (endX - startX);
            if (blitted > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern tile raster work exceeds the configured limit.");
            workUnits += blitted;
            // Pattern content is rasterized into an image before repeat sampling.
            // Its constant-shader byte coverage retains the native rounded lane scale.
            SvgConstantShaderPaint paint = sourcePaint is {} solid
                ? new(Quantize(solid.Color.Red * (1f / 255f) * solid.Alpha), Quantize(solid.Color.Green * (1f / 255f) * solid.Alpha), Quantize(solid.Color.Blue * (1f / 255f) * solid.Alpha), Quantize(solid.Alpha))
                : new(Premultiply(box.Color.Red, box.Color.Alpha), Premultiply(box.Color.Green, box.Color.Alpha), Premultiply(box.Color.Blue, box.Color.Alpha), box.Color.Alpha);
            for (int y = startY; y < endY; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = startX; x < endX; x++)
                {
                    if ((x & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                    int offset = (y * pattern.TextureWidth + x) * 4;
                    byte alpha = coverage.GetAlpha(x, y);
                    if (clip is {} paintClip) alpha = SvgAnalyticRectangleClip.GetCoverage((paintClip.X, paintClip.Y, paintClip.Right, paintClip.Bottom), paintClip.Antialiasing, x, y).Apply(alpha);
                    paint.Blend(tile.AsSpan(offset, 4), alpha);
                }
            }

            static byte Premultiply(byte color, byte alpha) => (byte)((color * alpha + 127) / 255);
            static byte Quantize(float value) => (byte)Math.Clamp((int)(value * 255f + 0.5f), 0, 255);
        }

        void StampPatternFill(SvgPatternGeometry pattern, byte[] tile, byte[] destination)
        {
            SvgShaderCoordinates imageCoordinates = pattern.NativeImageMap is {} imageMap ? SvgShaderCoordinates.CreateImage(imageMap, pattern.NativeImageLocal ?? throw new InvalidOperationException("Image sampling requires its local matrix.")) : default;
            SvgRectangleRaster raster = PrepareRectangle(pattern.Fill);
            int startX = raster.Left, endX = raster.Right, startY = raster.Top, endY = raster.Bottom;
            for (int y = startY; y < endY; y++)
            {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = startX; x < endX; x++)
            {
                int offset = (y * outputWidth + x) * 4;
                if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                SamplePattern(pattern, tile, x, y, ref imageCoordinates).Blend(destination.AsSpan(offset, 4), ViewportAlpha(raster.GetAlpha(x, y), x, y));
            }
            }
        }

        // Bounded adaptation of SkRasterPipeline_opts.h repeat bilinear sampling
        // at f8b66b7597c4cc859d3ed190e9c6872241e6721c; BSD notice is retained.
        // The repeat shader samples premultiplied tile lanes. Integral phase keeps
        // the existing single-tap route; fractional phase uses four bounded native
        // bilinear contributions before the independent shader alpha is applied.
        // Contributions keep the native row order (NX/NY, PX/NY, NX/PY, PX/PY).
        SvgShaderPaint SamplePattern(SvgPatternGeometry pattern, byte[] tile, int x, int y, ref SvgShaderCoordinates imageCoordinates)
        {
            float sampleX = x - (float)(pattern.OriginX + pattern.ShaderPlacement.E);
            float sampleY = y - (float)(pattern.OriginY + pattern.ShaderPlacement.F);
            float nativeFractionX = 0, nativeFractionY = 0;
            if (pattern.NativeImageMap is not null)
            {
                if (!imageCoordinates.TryMap(x, y, ShaderBitmapOrigin(x, y), out (float X, float Y) point))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern image coordinates are not finite and invertible.");
                (sampleX, sampleY) = point;
                float weightX = sampleX + .5f, weightY = sampleY + .5f;
                nativeFractionX = weightX - MathF.Floor(weightX);
                nativeFractionY = weightY - MathF.Floor(weightY);
                sampleX -= .5f;
                sampleY -= .5f;
            }
            float floorX = MathF.Floor(sampleX), floorY = MathF.Floor(sampleY);
            int tileX = (int)((floorX % pattern.TextureWidth + pattern.TextureWidth) % pattern.TextureWidth);
            int tileY = (int)((floorY % pattern.TextureHeight + pattern.TextureHeight) % pattern.TextureHeight);
            float fx = pattern.NativeImageMap is null ? sampleX - floorX : nativeFractionX, fy = pattern.NativeImageMap is null ? sampleY - floorY : nativeFractionY;
            float opacity = ShaderAlpha(pattern.Opacity);
            if (fx == 0 && fy == 0)
            {
                int offset = (tileY * pattern.TextureWidth + tileX) * 4;
                return SvgShaderPaint.Pattern(tile[offset], tile[offset + 1], tile[offset + 2], tile[offset + 3], opacity);
            }
            if (4 > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern filter work exceeds the configured limit.");
            workUnits += 4;
            int nextX = tileX + 1 == pattern.TextureWidth ? 0 : tileX + 1;
            int nextY = tileY + 1 == pattern.TextureHeight ? 0 : tileY + 1;
            float red = 0, green = 0, blue = 0, alpha = 0;
            Add(tileX, tileY, (1 - fx) * (1 - fy));
            Add(nextX, tileY, fx * (1 - fy));
            Add(tileX, nextY, (1 - fx) * fy);
            Add(nextX, nextY, fx * fy);
            return new(red * opacity, green * opacity, blue * opacity, alpha * opacity);

            void Add(int column, int row, float weight)
            {
                int offset = (row * pattern.TextureWidth + column) * 4;
                const float scale = 1f / 255f;
                red = MathF.FusedMultiplyAdd(weight, tile[offset] * scale, red);
                green = MathF.FusedMultiplyAdd(weight, tile[offset + 1] * scale, green);
                blue = MathF.FusedMultiplyAdd(weight, tile[offset + 2] * scale, blue);
                alpha = MathF.FusedMultiplyAdd(weight, tile[offset + 3] * scale, alpha);
            }
        }

        void RasterizePatternStroke(SvgPatternGeometry pattern, SvgStroke stroke, byte[] destination)
        {
            RasterizeShaderRectangleStroke(pattern.Fill, stroke, destination);
        }

        void RasterizeGradientFill(SvgGradientGeometry gradient, byte[] destination, SvgPaintPass pass)
        {
            if (!gradient.Fill.HasFill && gradient.Stroke is null) return;
            if (pass != SvgPaintPass.Stroke) RasterizeGradientArea(gradient.Gradient, gradient.Box, gradient.Fill, gradient.Opacity, gradient.Fill.HasFill, destination);
            if (pass != SvgPaintPass.Fill && gradient.Stroke is SvgStroke stroke) RasterizeGradientStroke(gradient, stroke, destination);
        }

        void RasterizeGradientStrokeFill(SvgGradientStrokeGeometry stroke, byte[] destination, SvgPaintPass pass)
        {
            if (pass != SvgPaintPass.Stroke && stroke.Fill.HasFill) RasterizeSolidRectangle(stroke.Fill, destination);
            if (pass == SvgPaintPass.Fill || !stroke.HasStroke) return;
            SvgRecordedPaintValue previous = activePaint;
            if (activeStrokePaint.Identity.Value != 0) activePaint = activeStrokePaint;
            try
            {
                RasterizeGradientArea(stroke.Gradient, stroke.Box, stroke.Top, stroke.Opacity, true, destination);
                RasterizeGradientArea(stroke.Gradient, stroke.Box, stroke.Bottom, stroke.Opacity, true, destination);
                RasterizeGradientArea(stroke.Gradient, stroke.Box, stroke.Left, stroke.Opacity, true, destination);
                RasterizeGradientArea(stroke.Gradient, stroke.Box, stroke.Right, stroke.Opacity, true, destination);
            }
            finally { activePaint = previous; }
        }

        void RasterizeGradientArea(SvgLinearGradient definition, SvgRectangle box, SvgRectangle area, byte opacity, bool paint, byte[] destination)
        {
            if (definition.Stops.Length > 1 && definition.X1 == definition.X2 && definition.Y1 == definition.Y2)
            {
                RasterizeConstantGradient(definition.Stops[^1], area, opacity, paint, destination);
                return;
            }
            SvgRectangleRaster raster = PrepareRectangle(area);
            int startX = raster.Left, endX = raster.Right, startY = raster.Top, endY = raster.Bottom;
            double directionX = definition.X2 - definition.X1;
            double directionY = definition.Y2 - definition.Y1;
            double denominator = directionX * directionX + directionY * directionY;
            double offsetSpan = definition.Offset2 - definition.Offset1;
            SvgShaderCoordinates nativeCoordinates = SvgShaderCoordinates.CreateLinear(definition, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset);
            if (paint && offsetSpan <= 0 && definition.Stops.Length != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only two-stop increasing-offset linear gradients are implemented.");
            if (!paint || endX <= startX || endY <= startY || (denominator == 0 && definition.Stops.Length != 1)) return;
            using SvgGradientRamp ramp = PrepareGradientRamp(definition.Stops, definition.Start, definition.End, definition.Offset1, definition.Offset2);

            if (paint && (denominator != 0 || definition.Stops.Length == 1))
            for (int y = startY; y < endY; y++)
            {
            cancellationToken.ThrowIfCancellationRequested();
            double deviceY = (y + 0.5 - verticalOffset) / verticalScale;
            for (int x = startX; x < endX; x++)
            {
                double deviceX = (x + 0.5 - horizontalOffset) / horizontalScale;
                double position;
                if (!nativeCoordinates.TryLinear(x, y, ShaderBitmapOrigin(x, y), out position))
                {
                    if (definition.SourcePaint is { Data.IsMapped: true } sourcePaint) position = sourcePaint.Position(deviceX, deviceY);
                    else if (definition.IsUserSpace && Math.Abs(definition.ReferenceScaleX) != Math.Abs(definition.ReferenceScaleY))
                    {
                        double relativeX = (deviceX - definition.X1) / definition.ReferenceScaleX;
                        double relativeY = (deviceY - definition.Y1) / definition.ReferenceScaleY;
                        double axisX = directionX / definition.ReferenceScaleX;
                        double axisY = directionY / definition.ReferenceScaleY;
                        position = (relativeX * axisX + relativeY * axisY) / (axisX * axisX + axisY * axisY);
                    }
                    else if (definition.IsUserSpace) position = ((deviceX - definition.X1) * directionX + (deviceY - definition.Y1) * directionY) / denominator;
                    else
                    {
                        double normalizedX = (deviceX - box.X) / box.Width;
                        double normalizedY = (deviceY - box.Y) / box.Height;
                        position = ((normalizedX - definition.X1) * directionX + (normalizedY - definition.Y1) * directionY) / denominator;
                    }
                }
                double local = definition.Stops.Length == 1 ? 0 : (position - definition.Offset1) / offsetSpan;
                if (definition.Spread == SvgGradientSpread.Repeat) local -= Math.Floor(local);
                else if (definition.Spread == SvgGradientSpread.Reflect)
                {
                    double cycle = local - 2 * Math.Floor(local / 2);
                    local = cycle > 1 ? 2 - cycle : cycle;
                }
                else
                {
                    if (local < 0) local = 0;
                    if (local > 1) local = 1;
                }
                position = local;
                int offset = (y * outputWidth + x) * 4;
                ramp.SampleLinear(position, ShaderAlpha(opacity), GradientDither(x, y)).Blend(destination.AsSpan(offset, 4), ViewportAlpha(raster.GetAlpha(x, y), x, y));
            }
            }

        }
        void RasterizeRadialFill(SvgRadialGeometry radial, byte[] destination, SvgPaintPass pass)
        {
            if (!radial.Fill.HasFill && radial.Stroke is null) return;
            if (pass != SvgPaintPass.Stroke) RasterizeRadialArea(radial.Gradient, radial.Box, radial.Fill, radial.Opacity, radial.Fill.HasFill, destination);
            if (pass != SvgPaintPass.Fill && radial.Stroke is SvgStroke stroke)
            {
                RasterizeShaderRectangleStroke(radial.Fill, stroke, destination);
            }
        }

        void RasterizeRadialArea(SvgRadialGradient definition, SvgRectangle box, SvgRectangle area, byte opacity, bool paint, byte[] destination)
        {
            if (definition.Stops.Length > 1 && definition.Radius == 0)
            {
                RasterizeConstantGradient(definition.Stops[^1], area, opacity, paint, destination);
                return;
            }
            SvgRectangleRaster raster = PrepareRectangle(area);
            SvgShaderCoordinates nativeCoordinates = SvgShaderCoordinates.CreateRadial(definition, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset);
            int startX = raster.Left, endX = raster.Right, startY = raster.Top, endY = raster.Bottom;
            if (paint && (definition.Radius <= 0 && definition.Stops.Length != 1)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive-radius radial gradients are implemented.");
            if (!paint || endX <= startX || endY <= startY) return;
            using SvgGradientRamp ramp = PrepareGradientRamp(definition.Stops, definition.Start, definition.End, definition.Offset1, definition.Offset2);

            if (paint)
            for (int y = startY; y < endY; y++)
            {
            cancellationToken.ThrowIfCancellationRequested();
            double deviceY = (y + 0.5 - verticalOffset) / verticalScale;
            for (int x = startX; x < endX; x++)
            {
                double deviceX = (x + 0.5 - horizontalOffset) / horizontalScale;
                double sampleX;
                double sampleY;
                double focalX;
                double focalY;
                double circleX;
                double circleY;
                if (definition.IsUserSpace)
                {
                    sampleX = deviceX;
                    sampleY = deviceY;
                    if (definition.HasGeneralTransform) { (sampleX, sampleY) = definition.Inverse.Apply(sampleX, sampleY); }
                    focalX = definition.FocalX;
                    focalY = definition.FocalY;
                    circleX = definition.CenterX;
                    circleY = definition.CenterY;
                }
                else
                {
                    sampleX = (deviceX - box.X) / box.Width;
                    sampleY = (deviceY - box.Y) / box.Height;
                    if (definition.HasGeneralTransform) { (sampleX, sampleY) = definition.Inverse.Apply(sampleX, sampleY); }
                    focalX = definition.FocalX;
                    focalY = definition.FocalY;
                    circleX = definition.CenterX;
                    circleY = definition.CenterY;
                }
                double radius = definition.Radius;
                double focalRadius = definition.FocalRadius;
                if (definition.IsUserSpace && Math.Abs(definition.ReferenceScaleX) != Math.Abs(definition.ReferenceScaleY))
                {
                    sampleX = (sampleX - circleX) / definition.ReferenceScaleX;
                    sampleY = (sampleY - circleY) / definition.ReferenceScaleY;
                    focalX = (focalX - circleX) / definition.ReferenceScaleX;
                    focalY = (focalY - circleY) / definition.ReferenceScaleY;
                    circleX = 0;
                    circleY = 0;
                    radius = definition.Radius / Math.Abs(definition.ReferenceScaleX);
                    focalRadius = definition.FocalRadius / Math.Abs(definition.ReferenceScaleX);
                }
                double offsetX = circleX - focalX;
                double offsetY = circleY - focalY;
                double rayX = sampleX - focalX;
                double rayY = sampleY - focalY;
                double position;
                if (definition.Stops.Length == 1) position = 0;
                else
                if (offsetX == 0 && offsetY == 0)
                {
                    if (!nativeCoordinates.TryRadial(x, y, ShaderBitmapOrigin(x, y), out position))
                        position = (Math.Sqrt(rayX * rayX + rayY * rayY) - focalRadius) / (radius - focalRadius);
                }
                else if (rayX == 0 && rayY == 0)
                {
                    position = 0;
                }
                else if (focalRadius == 0)
                {
                    double dot = rayX * offsetX + rayY * offsetY;
                    double raySquared = rayX * rayX + rayY * rayY;
                    double discriminant = dot * dot + raySquared * (radius * radius - offsetX * offsetX - offsetY * offsetY);
                    if (discriminant < 0) discriminant = 0;
                    position = raySquared / (dot + Math.Sqrt(discriminant));
                }
                else
                {
                    double span = radius - focalRadius;
                    double dot = rayX * offsetX + rayY * offsetY;
                    double raySquared = rayX * rayX + rayY * rayY;
                    double linear = dot + focalRadius * span;
                    double quadratic = offsetX * offsetX + offsetY * offsetY - span * span;
                    double constant = raySquared - focalRadius * focalRadius;
                    double discriminant = linear * linear - quadratic * constant;
                    if (discriminant < 0) discriminant = 0;
                    position = (linear - Math.Sqrt(discriminant)) / quadratic;
                }
                if (definition.Spread == SvgGradientSpread.Repeat) position -= Math.Floor(position);
                else if (definition.Spread == SvgGradientSpread.Reflect)
                {
                    double cycle = position - 2 * Math.Floor(position / 2);
                    position = cycle > 1 ? 2 - cycle : cycle;
                }
                else
                {
                    if (position < 0) position = 0;
                    if (position > 1) position = 1;
                }
                int offset = (y * outputWidth + x) * 4;
                ramp.SampleRadial(position, ShaderAlpha(opacity), GradientDither(x, y)).Blend(destination.AsSpan(offset, 4), ViewportAlpha(raster.GetAlpha(x, y), x, y));
            }
            }
        }

        void RasterizeConstantGradient(SvgGradientStop stop, SvgRectangle area, byte opacity, bool paint, byte[] destination)
        {
            SvgRectangleRaster raster = PrepareRectangle(area);
            if (!paint) return;
            SvgConstantShaderPaint color = SvgShaderPaint.ConstantGradient(stop, ShaderAlpha(opacity));
            for (int y = raster.Top; y < raster.Bottom; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = raster.Left; x < raster.Right; x++)
                    color.Blend(destination.AsSpan((y * outputWidth + x) * 4, 4), ViewportAlpha(raster.GetAlpha(x, y), x, y));
            }
        }

        void RasterizeGradientStroke(SvgGradientGeometry gradient, SvgStroke stroke, byte[] destination)
        {
            RasterizeShaderRectangleStroke(gradient.Fill, stroke, destination);
        }
        void RasterizeShaderRectangleStroke(SvgRectangle fill, SvgStroke stroke, byte[] destination)
        {
            SvgRecordedPaintValue previous = activePaint;
            if (activeStrokePaint.Identity.Value != 0) activePaint = activeStrokePaint;
            try
            {
                RasterizeRectangleStroke(fill, stroke, destination);
            }
            finally { activePaint = previous; }
        }
        void RasterizeGlyphContours(SvgGlyphGeometry glyph, byte[] destination)
        {
            if (glyph.Contours.Length == 0) return;
            if (glyph.Source is SvgGlyphRasterSource source && (OperatingSystem.IsLinux() || OperatingSystem.IsWindows()))
            {
                ParserWorkCounter preparationWork = new(options.Limits.MaximumWorkUnits - workUnits);
                if (glyph.StrokePaint is {} strokePaint)
                {
                    using SvgAuthoredPath path = source.CreateStrokePath(strokePaint, horizontalScale, horizontalOffset, verticalScale, verticalOffset, budget, preparationWork, cancellationToken);
                    Span<byte> preblend = stackalloc byte[256];
                    SvgColor color = ResolveSolidColor(glyph.Color);
                    if (OperatingSystem.IsWindows()) SvgFontMaskPreblend.BuildWindowsGrayTable(preblend, color, preparationWork, cancellationToken);
                    else SvgFontMaskPreblend.BuildLinuxGrayTable(preblend, color, preparationWork, cancellationToken);
                    RasterizeMappedBitmap(path, preblend, preparationWork);
                    return;
                }
                if (source.HasGlyphMap && OperatingSystem.IsLinux())
                {
                    using SvgAuthoredPath path = source.CreateMappedPath(horizontalScale, horizontalOffset, verticalScale, verticalOffset, budget, preparationWork, cancellationToken);
                    Span<byte> preblend = stackalloc byte[256];
                    SvgColor color = ResolveSolidColor(glyph.Color);
                    SvgFontMaskPreblend.BuildLinuxGrayTable(preblend, color, preparationWork, cancellationToken);
                    RasterizeMappedBitmap(path, preblend, preparationWork);
                    return;
                }
                if (source.HasGlyphMap || source.UsesDeviceFitting(budget, preparationWork, cancellationToken))
                {
                    if (OperatingSystem.IsWindows() && (source.HasGlyphMap || source.HasTrueTypePrograms(budget, preparationWork, cancellationToken)))
                    {
                        using OpenTypeGlyphContourStorage contours = source.CreateWindowsContours(horizontalScale, horizontalOffset, verticalScale, verticalOffset, budget, preparationWork, cancellationToken);
                        int crossingCount = checked(contours.Segments.Length * 2);
                        using RenderBudget.BudgetReservation? crossingReservation = crossingCount == 0 ? null : budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGlyphLcdCoverage.Crossing>(crossingCount), SvgMemoryCategory.Temporary);
                        SvgGlyphLcdCoverage.Crossing[] crossings = crossingCount == 0 ? [] : new SvgGlyphLcdCoverage.Crossing[crossingCount];
                        Span<byte> preblend = stackalloc byte[256];
                        SvgColor color = ResolveSolidColor(glyph.Color);
                        SvgFontMaskPreblend.BuildWindowsGrayTable(preblend, color, preparationWork, cancellationToken);
                        double lcdMinimumX = double.PositiveInfinity, lcdMinimumY = double.PositiveInfinity, lcdMaximumX = double.NegativeInfinity, lcdMaximumY = double.NegativeInfinity;
                        foreach (OpenTypeQuadraticSegment segment in contours.Segments)
                        {
                            preparationWork.Consume(cancellationToken);
                            IncludeLcd(segment.Start); IncludeLcd(segment.Control); IncludeLcd(segment.End);
                        }
                        if (contours.Segments.Length != 0)
                        {
                            int lcdStartX = ClampFloor(lcdMinimumX - 1d / 3, outputWidth), lcdEndX = ClampCeiling(lcdMaximumX + 1d / 3, outputWidth);
                            int lcdStartY = ClampFloor(lcdMinimumY, outputHeight), lcdEndY = ClampCeiling(lcdMaximumY, outputHeight);
                            SvgGlyphLcdCoverage.DropoutBounds dropoutBounds = SvgGlyphLcdCoverage.PrepareBounds(contours.Segments, contours.SmartDropouts, preparationWork, cancellationToken);
                            // Ten horizontal directed scans are reused across one row. This
                            // optional preparation yields to the existing low-memory path.
                            using SvgGlyphLcdCoverage.ScanStorage? rowStorage = lcdStartX < lcdEndX && lcdStartY < lcdEndY ? SvgGlyphLcdCoverage.ScanStorage.TryCreate(contours.Segments, budget, preparationWork, cancellationToken) : null;
                            Span<int> rowCounts = stackalloc int[10];
                            for (int y = lcdStartY; y < lcdEndY; y++)
                            {
                                SvgGlyphLcdCoverage.ScanRow scans = rowStorage is null ? default : rowStorage.PrepareRow(contours.Segments, y, rowCounts, preparationWork, cancellationToken);
                                for (int x = lcdStartX; x < lcdEndX; x++)
                                {
                                    preparationWork.Consume(cancellationToken);
                                    SvgGlyphRgbCoverage lcdCoverage = scans.IsPrepared
                                        ? SvgGlyphLcdCoverage.GetCoverage(scans, x, crossings, dropoutBounds, preparationWork, cancellationToken)
                                        : SvgGlyphLcdCoverage.GetCoverage(contours.Segments, x, y, crossings, dropoutBounds, preparationWork, cancellationToken);
                                    byte alpha = ViewportAlpha(preblend[lcdCoverage.Mean], x, y);
                                    if (alpha != 0) ResolveSolidPaint(glyph.Color).Blend(destination.AsSpan((y * outputWidth + x) * 4, 4), alpha, SvgAnalyticPaintKind.Mask);
                                }
                            }
                        }
                        workUnits += preparationWork.Used;
                        return;

                        void IncludeLcd(OpenTypeOutlinePoint point)
                        {
                            lcdMinimumX = Math.Min(lcdMinimumX, point.X); lcdMinimumY = Math.Min(lcdMinimumY, point.Y);
                            lcdMaximumX = Math.Max(lcdMaximumX, point.X); lcdMaximumY = Math.Max(lcdMaximumY, point.Y);
                        }
                    }
                    if (OperatingSystem.IsLinux())
                    {
                        using SvgAuthoredPath path = source.CreateLatinLightPath(horizontalScale, horizontalOffset, verticalScale, verticalOffset, budget, preparationWork, cancellationToken);
                        if (path.Commands.IsEmpty) { workUnits += preparationWork.Used; return; }
                        Span<byte> preblend = stackalloc byte[256];
                        SvgFontMaskPreblend.BuildLinuxGrayTable(preblend, ResolveSolidColor(glyph.Color), preparationWork, cancellationToken);
                        RasterizeMappedBitmap(path, preblend, preparationWork);
                        return;
                    }
                }
                // Previously admitted composites retain their existing coverage;
                // shared composite hinting remains an explicit G9 dependency.
                workUnits += preparationWork.Used;
            }
            long segmentCount;
            long sampleWork;
            try
            {
                segmentCount = SvgGlyphCoverage.GetSegmentCount(glyph.Contours);
                sampleWork = checked(segmentCount * 8);
            }
            catch (OverflowException)
            {
                throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph raster work exceeds the supported accounting range.");
            }
            if (segmentCount > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph raster work exceeds the configured work limit.");
            workUnits += segmentCount;
            double minimumX = double.PositiveInfinity;
            double minimumY = double.PositiveInfinity;
            double maximumX = double.NegativeInfinity;
            double maximumY = double.NegativeInfinity;
            int boundsSegments = 0;
            foreach (SvgGlyphQuadraticContour contour in glyph.Contours)
            foreach (SvgGlyphQuadraticSegment segment in contour.Segments)
            {
                if ((boundsSegments++ & 127) == 0) cancellationToken.ThrowIfCancellationRequested();
                Include(segment.Start);
                Include(segment.Control);
                Include(segment.End);
            }
            int startX = ClampFloor(MapEdge(minimumX, horizontalScale, horizontalOffset), outputWidth);
            int endX = ClampCeiling(MapEdge(maximumX, horizontalScale, horizontalOffset), outputWidth);
            int startY = ClampFloor(MapEdge(minimumY, verticalScale, verticalOffset), outputHeight);
            int endY = ClampCeiling(MapEdge(maximumY, verticalScale, verticalOffset), outputHeight);
            long coverage = (long)(endY - startY) * (endX - startX);
            long coverageWork;
            try
            {
                long samplesPerPixel = checked(GlyphSamplesPerAxis * GlyphSamplesPerAxis);
                coverageWork = checked(coverage * checked(sampleWork * samplesPerPixel + 1));
            }
            catch (OverflowException)
            {
                throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph raster work exceeds the supported accounting range.");
            }
            if (coverageWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph raster work exceeds the configured work limit.");
            workUnits += coverageWork;
            if (startX == endX || startY == endY) return;
            double sourceLeft = (startX - horizontalOffset) / horizontalScale;
            double sourceRight = (endX - horizontalOffset) / horizontalScale;
            double sourceTop = (startY - verticalOffset) / verticalScale;
            double sourceBottom = (endY - verticalOffset) / verticalScale;
            double sourcePixelWidth = 1 / horizontalScale;
            double sourcePixelHeight = 1 / verticalScale;
            if (!double.IsFinite(sourceLeft) || !double.IsFinite(sourceRight) || !double.IsFinite(sourceTop) || !double.IsFinite(sourceBottom) || !double.IsFinite(sourcePixelWidth) || !double.IsFinite(sourcePixelHeight) || sourcePixelWidth <= 0 || sourcePixelHeight <= 0)
            {
                throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph coverage coordinates exceed the admitted numeric range.");
            }
            ParserWorkCounter fallbackPreparation = new(options.Limits.MaximumWorkUnits - workUnits);
            // Optional preparation yields to both memory and the existing tight work boundary.
            using SvgGlyphCoverage.PreparedContours? prepared = sampleWork <= options.Limits.MaximumWorkUnits - workUnits
                ? SvgGlyphCoverage.PreparedContours.TryCreate(glyph.Contours, budget, fallbackPreparation, cancellationToken) : null;
            workUnits += fallbackPreparation.Used;
            for (int y = startY; y < endY; y++)
            for (int x = startX; x < endX; x++)
            {
                byte coverageAlpha = prepared is null
                    ? SvgGlyphCoverage.GetCoverage(glyph.Contours, sourceLeft + (x - startX) * sourcePixelWidth, sourceTop + (y - startY) * sourcePixelHeight, sourcePixelWidth, sourcePixelHeight, GlyphSamplesPerAxis, cancellationToken)
                    : SvgGlyphCoverage.GetCoverage(prepared, sourceLeft + (x - startX) * sourcePixelWidth, sourceTop + (y - startY) * sourcePixelHeight, sourcePixelWidth, sourcePixelHeight, GlyphSamplesPerAxis, cancellationToken);
                byte alpha = ViewportAlpha(coverageAlpha, x, y);
                if (alpha == 0) continue;
                BlendSourceOver(destination.AsSpan((y * outputWidth + x) * 4, 4), new SvgColor(glyph.Color.Red, glyph.Color.Green, glyph.Color.Blue, MultiplyAlpha(glyph.Color.Alpha, alpha)));
            }

            void Include(SvgGlyphOutlinePoint point)
            {
                minimumX = Math.Min(minimumX, point.X); minimumY = Math.Min(minimumY, point.Y);
                maximumX = Math.Max(maximumX, point.X); maximumY = Math.Max(maximumY, point.Y);
            }

            // Native glyph strikes construct their mask in a small, independent bitmap.
            // Keep the fractional origin and transformed controls in that local float
            // domain; adding the document's integer origin before edge construction
            // loses precision. The child scene borrows these scoped path/fill owners.
            // It uses the ordinary geometry rasterizer, the same conversion budget,
            // remaining work and cancellation token. Only final mask painting belongs
            // to the parent SVG viewport/opacity scope. No font ownership transfers.
            void RasterizeMappedBitmap(SvgAuthoredPath sourcePath, ReadOnlySpan<byte> preblend, ParserWorkCounter preparationWork)
            {
                bool horizontalStrokeFilter = glyph.StrokePaint is not null && OperatingSystem.IsWindows();
                int sampleScale = horizontalStrokeFilter ? 4 : 1;
                SvgAffineTransform map = sourcePath.SceneMap;
                double originX = Math.Floor(map.E), originY = Math.Floor(map.F);
                float phaseX = (float)(map.E - originX), phaseY = (float)(map.F - originY);
                double left = double.PositiveInfinity, top = double.PositiveInfinity;
                double right = double.NegativeInfinity, bottom = double.NegativeInfinity;
                using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Geometry, preparationWork, cancellationToken);
                foreach (SvgAuthoredPathCommand command in sourcePath.Commands)
                {
                    preparationWork.Consume(cancellationToken);
                    commands.Add(command.Verb switch
                    {
                        SvgAuthoredPathVerb.Move or SvgAuthoredPathVerb.Line => command with { First = MapLocal(command.First) },
                        SvgAuthoredPathVerb.Quadratic => command with { First = MapLocal(command.First), Second = MapLocal(command.Second) },
                        SvgAuthoredPathVerb.Cubic => command with { First = MapLocal(command.First), Second = MapLocal(command.Second), Third = MapLocal(command.Third) },
                        SvgAuthoredPathVerb.Conic => command with { First = MapLocal(command.First), Second = MapLocal(command.Second) },
                        SvgAuthoredPathVerb.Close => command,
                        _ => throw new InvalidOperationException("A glyph mask contains an unexpected outline verb."),
                    });
                }
                workUnits += preparationWork.Used;
                if (commands.Count == 0) return;
                left = Math.Floor(left); top = Math.Floor(top);
                right = Math.Ceiling(right); bottom = Math.Ceiling(bottom);
                if (horizontalStrokeFilter) { left--; right++; }
                double bitmapWidth = right - left, bitmapHeight = bottom - top;
                if (!double.IsFinite(bitmapWidth) || !double.IsFinite(bitmapHeight))
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph bitmap bounds exceed the device range.");
                if (bitmapWidth <= 0 || bitmapHeight <= 0 || originX + right <= 0 || originY + bottom <= 0
                    || originX + left >= outputWidth || originY + top >= outputHeight) return;
                SvgRenderLimits limits = options.Limits;
                if (bitmapWidth * sampleScale > limits.MaximumDimension || bitmapHeight > limits.MaximumDimension
                    || bitmapWidth * bitmapHeight * sampleScale > limits.MaximumPixels || bitmapWidth * bitmapHeight * sampleScale > int.MaxValue / 4)
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph bitmap exceeds the configured surface limits.");
                int width = (int)bitmapWidth * sampleScale, height = (int)bitmapHeight;
                long remaining = limits.MaximumWorkUnits - workUnits;
                if (remaining <= 0) throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph bitmap exceeds the configured work limit.");
                OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
                SvgAuthoredPath localPath;
                try { localPath = SvgAuthoredPath.Create(owned, SvgAffineTransform.FromScaleOffset(sampleScale, 1, -left * sampleScale, -top), budget); }
                catch { owned.Dispose(); throw; }
                using (localPath)
                using (SvgPolygonFill fill = SvgPolygonFill.Create([], [], null, new SvgColor(0, 0, 0, 255), true, false, SvgPolygonMapping.NativeFillEdges, null, null, budget))
                using (RenderBudget.BudgetReservation scene = budget.Reserve(256 + ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(1) + ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(1), SvgMemoryCategory.Geometry))
                {
                    SvgGeometry[] geometries = [SvgGeometry.FromPolygonFill(fill) with { AuthoredPath = localPath }];
                    SvgDrawCommand[] draws = [new(SvgDrawCommandKind.Geometry, 0, 255, null, -1, 255)];
                    SvgRoot bitmapRoot = new(null, null, null, geometries, draws, false, 0, null);
                    SvgRenderLimits bitmapLimits = new(limits.MaximumAccountedBytes, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels,
                        limits.MaximumEncodedPngBytes, limits.MaximumNodes, limits.MaximumPathCommands, checked((int)remaining), limits.MaximumXmlDepth, limits.MaximumAttributesPerElement);
                    SvgRenderOptions bitmapOptions = new(new SvgViewport(width, height), 1, SvgColor.Transparent, bitmapLimits);
                    using RasterizedSurface bitmap = Rasterize(bitmapRoot, bitmapOptions, budget, RasterSurfaceMode.PremultipliedGlyph, cancellationToken);
                    workUnits += bitmap.WorkUnits;
                    double deviceLeft = originX + left, deviceTop = originY + top;
                    int startX = Math.Max(layerBounds.Left, ClampFloor(deviceLeft, outputWidth));
                    int startY = Math.Max(layerBounds.Top, ClampFloor(deviceTop, outputHeight));
                    int endX = Math.Min(layerBounds.Right, ClampCeiling(deviceLeft + bitmapWidth, outputWidth));
                    int endY = Math.Min(layerBounds.Bottom, ClampCeiling(deviceTop + height, outputHeight));
                    long paintWork = (long)Math.Max(0, endX - startX) * Math.Max(0, endY - startY) * (horizontalStrokeFilter ? 37 : 1);
                    if (paintWork > limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph mask painting exceeds the configured work limit.");
                    workUnits += paintWork;
                    SvgAnalyticPaint paint = ResolveSolidPaint(glyph.Color);
                    for (int y = startY; y < endY; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        for (int x = startX; x < endX; x++)
                        {
                            if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                            int row = (int)(y - deviceTop) * width * 4;
                            int sampleX = (int)(x - deviceLeft) * sampleScale;
                            byte coverage = horizontalStrokeFilter ? FilterStroke(bitmap.Pixels.AsSpan(row, width * 4), sampleX) : bitmap.Pixels[row + sampleX * 4 + 3];
                            byte alpha = ViewportAlpha(preblend[coverage], x, y);
                            paint.Blend(destination.AsSpan((y * outputWidth + x) * 4, 4), alpha, SvgAnalyticPaintKind.Mask);
                        }
                    }
                }

                SvgDevicePoint MapLocal(SvgDevicePoint point)
                {
                    preparationWork.Consume(cancellationToken);
                    float x = (float)map.A * (float)point.X + (float)map.C * (float)point.Y;
                    float y = (float)map.B * (float)point.X + (float)map.D * (float)point.Y;
                    x += phaseX; y += phaseY;
                    if (!float.IsFinite(x) || !float.IsFinite(y)) throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph bitmap controls exceed the device range.");
                    left = Math.Min(left, x); top = Math.Min(top, y);
                    right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                    return new(x, y);
                }

                // Pinned Skia SkScalerContext.cpp pack4xHToMask; Copyright 2012 Google Inc.
                // See THIRD_PARTY_NOTICES.txt. The caller charges the bounded 36 filter products.
                static byte FilterStroke(ReadOnlySpan<byte> row, int center)
                {
                    ReadOnlySpan<byte> red = [3, 11, 28, 51, 64, 57, 36, 16, 5, 1, 0, 0];
                    ReadOnlySpan<byte> green = [0, 2, 8, 22, 43, 61, 61, 43, 22, 8, 2, 0];
                    ReadOnlySpan<byte> blue = [0, 0, 1, 5, 16, 36, 57, 64, 51, 28, 11, 3];
                    int r = 0, g = 0, b = 0;
                    for (int index = 0; index < 12; index++)
                    {
                        int offset = (center - 4 + index) * 4 + 3;
                        if (offset < 0 || offset >= row.Length) continue;
                        int alpha = row[offset];
                        r += alpha * red[index]; g += alpha * green[index]; b += alpha * blue[index];
                    }
                    return (byte)((Math.Min(r >> 8, 255) + Math.Min(g >> 8, 255) + Math.Min(b >> 8, 255)) / 3);
                }
            }
        }

        void RasterizeRectangle(SvgRectangle rectangle, byte[] destination, SvgPaintPass pass)
        {
            if (pass != SvgPaintPass.Stroke && rectangle.HasFill) RasterizeSolidRectangle(rectangle, destination);
            if (pass != SvgPaintPass.Fill && rectangle.Stroke is SvgStroke stroke)
            {
                SvgRecordedPaintValue previous = activePaint;
                activePaint = activeStrokePaint;
                try
                {
                    RasterizeRectangleStroke(rectangle, stroke, destination);
                }
                finally { activePaint = previous; }
            }
        }

        SvgRectangleFrameCoverage? PrepareRectangleFrame(SvgAuthoredPath? source)
        {
            if (source?.StrokeStyle is not { Shape: SvgAuthoredStrokeShape.Rectangle } style) return null;
            SvgAffineTransform outputMap = SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset);
            SvgAffineTransform map = outputMap.ComposeChecked(source.SceneMap);
            SvgAffineTransform widthMap = outputMap.ComposeChecked(style.WidthMap);
            if (!map.PreservesAxisAlignedRectangles || !widthMap.PreservesAxisAlignedRectangles) return null;
            SvgRectanglePathRecognizer recognizer = new();
            foreach (SvgAuthoredPathCommand command in source.Commands)
            {
                if (workUnits >= options.Limits.MaximumWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle source preparation exceeds the remaining work limit.");
                workUnits++;
                cancellationToken.ThrowIfCancellationRequested();
                if (!recognizer.Add(command.Verb, command.First)) return null;
            }
            if (recognizer.Finish() is not SvgRecognizedRectangle { Closed: true } rectangle) return null;
            bool exchangeAxes = !map.IsAxisAligned;
            double x1 = MapRectangleEdge(exchangeAxes ? rectangle.Top : rectangle.Left, exchangeAxes ? map.C : map.A, map.E);
            double x2 = MapRectangleEdge(exchangeAxes ? rectangle.Bottom : rectangle.Right, exchangeAxes ? map.C : map.A, map.E);
            double y1 = MapRectangleEdge(exchangeAxes ? rectangle.Left : rectangle.Top, exchangeAxes ? map.B : map.D, map.F);
            double y2 = MapRectangleEdge(exchangeAxes ? rectangle.Right : rectangle.Bottom, exchangeAxes ? map.B : map.D, map.F);
            return SvgRectangleFrameCoverage.Create(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2),
                style.Width * Math.Abs(widthMap.IsAxisAligned ? widthMap.A : widthMap.C),
                style.Width * Math.Abs(widthMap.IsAxisAligned ? widthMap.D : widthMap.B));
        }

        void RasterizeRectangleStroke(SvgRectangle rectangle, SvgStroke stroke, byte[] destination)
        {
            int setup = rectangle.Clip is null ? 8 : 12;
            if (setup > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle frame preparation exceeds the configured work limit.");
            workUnits += setup;
            cancellationToken.ThrowIfCancellationRequested();
            SvgRectangleFrameCoverage frame = SvgRectangleFrameCoverage.Create(
                MapRectangleEdge(rectangle.X, horizontalScale, horizontalOffset), MapRectangleEdge(rectangle.Y, verticalScale, verticalOffset),
                MapRectangleEdge(rectangle.X + rectangle.Width, horizontalScale, horizontalOffset), MapRectangleEdge(rectangle.Y + rectangle.Height, verticalScale, verticalOffset),
                MapEdge(stroke.Width, horizontalScale, 0), MapEdge(stroke.Width, verticalScale, 0));
            SvgClipBox? clip = null;
            if (rectangle.Clip is {} sourceClip)
            {
                (double l, double t, double r, double b) = MapClipBounds(sourceClip, horizontalScale, horizontalOffset, verticalScale, verticalOffset);
                clip = new SvgClipBox(l, t, Math.Max(0, r - l), Math.Max(0, b - t)) { Antialiasing = sourceClip.Antialiasing };
            }
            int left = (int)Math.Clamp(frame.OuterLeft >> 8, 0, outputWidth), right = (int)Math.Clamp((frame.OuterRight + 255) >> 8, left, outputWidth);
            int top = (int)Math.Clamp(frame.OuterTop >> 8, 0, outputHeight), bottom = (int)Math.Clamp((frame.OuterBottom + 255) >> 8, top, outputHeight);
            if (clip is {} bounds)
            {
                left = Math.Max(left, ClampFloor(bounds.X, outputWidth)); right = Math.Max(left, Math.Min(right, ClampCeiling(bounds.Right, outputWidth)));
                top = Math.Max(top, ClampFloor(bounds.Y, outputHeight)); bottom = Math.Max(top, Math.Min(bottom, ClampCeiling(bounds.Bottom, outputHeight)));
            }
            SvgAnalyticPaint paint = ResolveSolidPaint(stroke.Color);
            Span<byte> alphas = stackalloc byte[4];
            foreach (SvgCpuRasterContext cpuTile in rasterGrid.GetContexts(new(left, top, right, bottom)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgCpuRasterContext context = LayerRasterContext(cpuTile);
                SvgCpuBitmapOrigin origin = context.Origin;
                // Native frame insets are evaluated in the owning bitmap before
                // fixed-edge conversion; global insets lose float fringe precision.
                SvgRectangleFrameCoverage local = frame;
                if (origin != default)
                {
                    if (8 > options.Limits.MaximumWorkUnits - workUnits)
                        throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle bitmap-frame preparation exceeds the configured work limit.");
                    workUnits += 8;
                    local = SvgRectangleFrameCoverage.Create(
                        MapRectangleEdge(rectangle.X, horizontalScale, (float)horizontalOffset - origin.X),
                        MapRectangleEdge(rectangle.Y, verticalScale, (float)verticalOffset - origin.Y),
                        MapRectangleEdge(rectangle.X + rectangle.Width, horizontalScale, (float)horizontalOffset - origin.X),
                        MapRectangleEdge(rectangle.Y + rectangle.Height, verticalScale, (float)verticalOffset - origin.Y),
                        MapEdge(stroke.Width, horizontalScale, 0), MapEdge(stroke.Width, verticalScale, 0));
                }
                int x1 = Math.Max(left, context.Interior.Left), x2 = Math.Min(right, context.Interior.Right);
                int y1 = Math.Max(top, context.Interior.Top), y2 = Math.Min(bottom, context.Interior.Bottom);
                if (x2 <= x1 || y2 <= y1) continue;
                long rows = 2L * (y2 - y1);
                if (rows > options.Limits.MaximumWorkUnits - workUnits)
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle frame rows exceed the configured work limit.");
                workUnits += rows;
                long pixels = 0;
                for (int y = y1; y < y2; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    (int end, int start) = local.GetRowSpans(y - origin.Y, x1 - origin.X, x2 - origin.X);
                    pixels += end - (x1 - origin.X) + (x2 - origin.X) - Math.Max(end, start);
                }
                long rasterWork = 5 * pixels;
                if (rasterWork > options.Limits.MaximumWorkUnits - workUnits)
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle frame raster exceeds the configured work limit.");
                workUnits += rasterWork;
                for (int y = y1; y < y2; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    (int localEnd, int localStart) = local.GetRowSpans(y - origin.Y, x1 - origin.X, x2 - origin.X);
                    int end = localEnd + origin.X, start = localStart + origin.X;
                    int second = Math.Max(end, start), visited = end - x1 + x2 - second;
                    for (int index = 0; index < visited; index++)
                    {
                        int x = index < end - x1 ? x1 + index : second + index - (end - x1);
                        int count = local.GetOperations(x - origin.X, y - origin.Y, alphas);
                        SvgAnalyticClipCoverage coverage = clip is {} paintClip ? SvgAnalyticRectangleClip.GetCoverage((paintClip.X, paintClip.Y, paintClip.Right, paintClip.Bottom), paintClip.Antialiasing, x, y) : SvgAnalyticClipCoverage.Full;
                        Span<byte> pixel = destination.AsSpan((y * outputWidth + x) * 4, 4);
                        for (int operation = 0; operation < count; operation++)
                            paint.Blend(pixel, ViewportAlpha(coverage.Apply(alphas[operation]), x, y), SvgAnalyticPaintKind.Direct);
                    }
                }
            }
        }

        void RasterizeSolidRectangle(SvgRectangle rectangle, byte[] destination)
        {
            SvgRectangleRaster raster = PrepareRectangle(rectangle);
            int startX = raster.Left, endX = raster.Right, startY = raster.Top, endY = raster.Bottom;
            SvgAnalyticPaint rectanglePaint = ResolveSolidPaint(rectangle.Color);
            for (int y = startY; y < endY; y++)
            {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = startX; x < endX; x++)
            {
                byte rectangleAlpha = ViewportAlpha(raster.GetAlpha(x, y), x, y);
                int offset = (y * outputWidth + x) * 4;
                rectanglePaint.Blend(destination.AsSpan(offset, 4), rectangleAlpha, SvgAnalyticPaintKind.Direct);
            }
            }
        }

        SvgRectangleRaster PrepareRectangle(SvgRectangle rectangle)
        {
            int setupVisits = rectangle.Clip is null ? 4 : 8;
            if (setupVisits > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle preparation exceeds the configured work limit.");
            workUnits += setupVisits;
            cancellationToken.ThrowIfCancellationRequested();
            double left = MapRectangleEdge(rectangle.X, horizontalScale, horizontalOffset);
            double top = MapRectangleEdge(rectangle.Y, verticalScale, verticalOffset);
            double right = MapRectangleEdge(rectangle.X + rectangle.Width, horizontalScale, horizontalOffset);
            double bottom = MapRectangleEdge(rectangle.Y + rectangle.Height, verticalScale, verticalOffset);
            SvgRectangleCoverage coverage = SvgRectangleCoverage.Create(left, top, right, bottom, outputWidth, outputHeight);
            SvgClipBox? clip = null;
            if (rectangle.Clip is {} sourceClip)
            {
                double clipLeft = MapEdge(sourceClip.X, horizontalScale, horizontalOffset);
                double clipTop = MapEdge(sourceClip.Y, verticalScale, verticalOffset);
                double clipRight = MapEdge(sourceClip.Right, horizontalScale, horizontalOffset);
                double clipBottom = MapEdge(sourceClip.Bottom, verticalScale, verticalOffset);
                clip = new SvgClipBox(clipLeft, clipTop, Math.Max(0, clipRight - clipLeft), Math.Max(0, clipBottom - clipTop)) { Antialiasing = sourceClip.Antialiasing };
                left = Math.Max(left, clipLeft); top = Math.Max(top, clipTop);
                right = Math.Min(right, clipRight); bottom = Math.Min(bottom, clipBottom);
            }
            int startX = ClampFloor(left, outputWidth), startY = ClampFloor(top, outputHeight);
            int endX = Math.Max(startX, ClampCeiling(right, outputWidth)), endY = Math.Max(startY, ClampCeiling(bottom, outputHeight));
            long pixels = (long)(endX - startX) * (endY - startY);
            if (pixels > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle raster work exceeds the configured limit.");
            workUnits += pixels;
            return new(startX, startY, endX, endY, coverage, clip);
        }

        // Maps retained cubic controls through the output scale without grid snapping, like quadratic controls.
        static SvgCubicSpan[] MapCubicSpanControls(SvgCubicSpan[] spans, double horizontalScale, double horizontalOffset, double verticalScale, double verticalOffset, int outputWidth, int outputHeight, out bool canvasContainsControls, SvgAffineTransform? authoredMap, CancellationToken cancellationToken)
        {
            canvasContainsControls = true;
            if (spans.Length == 0) return Array.Empty<SvgCubicSpan>();
            SvgCubicSpan[] mapped = new SvgCubicSpan[spans.Length];
            for (int index = 0; index < spans.Length; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                SvgDevicePoint first = spans[index].FirstControl;
                SvgDevicePoint second = spans[index].SecondControl;
                SvgDevicePoint mappedFirst = authoredMap is {} firstMap ? SvgAuthoredPath.MapPoint(firstMap, first, new(0, 0)) : new(MapNativePolygonEdge(first.X, horizontalScale, horizontalOffset), MapNativePolygonEdge(first.Y, verticalScale, verticalOffset));
                SvgDevicePoint mappedSecond = authoredMap is {} secondMap ? SvgAuthoredPath.MapPoint(secondMap, second, new(0, 0)) : new(MapNativePolygonEdge(second.X, horizontalScale, horizontalOffset), MapNativePolygonEdge(second.Y, verticalScale, verticalOffset));
                canvasContainsControls &= mappedFirst.X >= 0 && mappedFirst.X <= outputWidth && mappedFirst.Y >= 0 && mappedFirst.Y <= outputHeight
                    && mappedSecond.X >= 0 && mappedSecond.X <= outputWidth && mappedSecond.Y >= 0 && mappedSecond.Y <= outputHeight;
                mapped[index] = new SvgCubicSpan(spans[index].Contour, spans[index].StartIndex, spans[index].EndIndex, mappedFirst, mappedSecond);
            }
            return mapped;
        }

                // Maps retained quadratic controls through the output scale without grid snapping; curve data never takes the quarter discipline.
        static SvgQuadSpan[] MapQuadSpanControls(SvgQuadSpan[] spans, double horizontalScale, double horizontalOffset, double verticalScale, double verticalOffset, int outputWidth, int outputHeight, bool nativeMapping, out bool canvasContainsControls, SvgAffineTransform? authoredMap, CancellationToken cancellationToken)
        {
            canvasContainsControls = true;
            if (spans.Length == 0) return Array.Empty<SvgQuadSpan>();
            SvgQuadSpan[] mapped = new SvgQuadSpan[spans.Length];
            for (int index = 0; index < spans.Length; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                SvgDevicePoint control = spans[index].Control;
                bool native = nativeMapping || spans[index].ConicWeight != 1f;
                SvgDevicePoint mappedControl = authoredMap is {} map ? SvgAuthoredPath.MapPoint(map, control, new(0, 0)) : native
                    ? new(MapNativePolygonEdge(control.X, horizontalScale, horizontalOffset), MapNativePolygonEdge(control.Y, verticalScale, verticalOffset))
                    : new(MapEdge(control.X, horizontalScale, horizontalOffset), MapEdge(control.Y, verticalScale, verticalOffset));
                canvasContainsControls &= mappedControl.X >= 0 && mappedControl.X <= outputWidth && mappedControl.Y >= 0 && mappedControl.Y <= outputHeight;
                mapped[index] = spans[index] with { Control = mappedControl };
            }
            return mapped;
        }

                static double MapEdge(double value, double scale, double offset)
        {
            double scaled = value * scale + offset;
            if (!double.IsFinite(scaled)) throw new SvgException(SvgErrorCode.LimitExceeded, "Output scaling and viewBox mapping exceed the admitted numeric range.");
            return scaled;
        }

        static double MapRectangleEdge(double value, double scale, double offset)
        {
            return MapNativePolygonEdge(value, scale, offset);
        }

        static double MapNativePolygonEdge(double value, double scale, double offset)
        {
            double mapped = MapEdge(value, scale, offset);
            // SkMatrix::Scale_pts narrows each operand and rounds the product
            // before adding translation, including retained curve controls and endpoints.
            float native = (float)value * (float)scale + (float)offset;
            return float.IsFinite(native) ? native : mapped;
        }

        static int ClampFloor(double value, int maximum) => value <= 0 ? 0 : value >= maximum ? maximum : checked((int)Math.Floor(value));

        static (double Left, double Top, double Right, double Bottom) MapClipBounds(SvgClipBox clip, double horizontalScale, double horizontalOffset, double verticalScale, double verticalOffset)
        {
            return (
                (clip.Antialiasing & SvgClipAntialiasing.BinaryLeft) != 0 ? MapNativePolygonEdge(clip.X, horizontalScale, horizontalOffset) : MapEdge(clip.X, horizontalScale, horizontalOffset),
                (clip.Antialiasing & SvgClipAntialiasing.BinaryTop) != 0 ? MapNativePolygonEdge(clip.Y, verticalScale, verticalOffset) : MapEdge(clip.Y, verticalScale, verticalOffset),
                (clip.Antialiasing & SvgClipAntialiasing.BinaryRight) != 0 ? MapNativePolygonEdge(clip.Right, horizontalScale, horizontalOffset) : MapEdge(clip.Right, horizontalScale, horizontalOffset),
                (clip.Antialiasing & SvgClipAntialiasing.BinaryBottom) != 0 ? MapNativePolygonEdge(clip.Bottom, verticalScale, verticalOffset) : MapEdge(clip.Bottom, verticalScale, verticalOffset));
        }

        static double ClipBoxCoverage((double Left, double Top, double Right, double Bottom) bounds, int x, int y)
        {
            double horizontal = Math.Clamp(Math.Min(bounds.Right, x + 1) - Math.Max(bounds.Left, x), 0, 1);
            double vertical = Math.Clamp(Math.Min(bounds.Bottom, y + 1) - Math.Max(bounds.Top, y), 0, 1);
            return horizontal * vertical;
        }

        static (int StartX, int StartY, int EndX, int EndY) IntersectMappedClip(SvgClipBox clip, double horizontalScale, double horizontalOffset, double verticalScale, double verticalOffset, int outputWidth, int outputHeight, int startX, int startY, int endX, int endY)
        {
            (double left, double top, double right, double bottom) = MapClipBounds(clip, horizontalScale, horizontalOffset, verticalScale, verticalOffset);
            return (
                Math.Max(startX, ClampFloor(left, outputWidth)),
                Math.Max(startY, ClampFloor(top, outputHeight)),
                Math.Min(endX, ClampCeiling(right, outputWidth)),
                Math.Min(endY, ClampCeiling(bottom, outputHeight)));
        }

        static int ClampCeiling(double value, int maximum) => value <= 0 ? 0 : value >= maximum ? maximum : checked((int)Math.Ceiling(value));

    }

    /// <summary>Resolves output dimensions from an explicit viewport or root dimensions with viewBox aspect handling.</summary>
    private static (double Width, double Height) ResolveOutputDimensions(SvgViewport? viewport, SvgRootLength? rootWidth, SvgRootLength? rootHeight, SvgViewBox? viewBox)
        => viewport is SvgViewport explicitViewport
            ? (ResolveAgainstViewport(rootWidth, explicitViewport.Width, 300), ResolveAgainstViewport(rootHeight, explicitViewport.Height, 150))
            : (rootWidth, rootHeight) switch
            {
                (SvgRootLength explicitWidth, SvgRootLength explicitHeight) => (explicitWidth.Resolve(null, 300), explicitHeight.Resolve(null, 150)),
                (null, null) => (300, 150),
                (SvgRootLength explicitWidth, null) when viewBox is SvgViewBox widthBox => (explicitWidth.Resolve(null, 300), explicitWidth.Resolve(null, 300) * widthBox.Height / widthBox.Width),
                (null, SvgRootLength explicitHeight) when viewBox is SvgViewBox heightBox => (explicitHeight.Resolve(null, 150) * heightBox.Width / heightBox.Height, explicitHeight.Resolve(null, 150)),
                _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Both root dimensions or neither root dimension are required currently.")
            };

    /// <summary>Resolves one root dimension for an explicit viewport, keeping the viewport size except for viewport-relative percentages.</summary>
    private static double ResolveAgainstViewport(SvgRootLength? length, double viewportSide, double defaultSide) => length?.IsPercentage == true ? length.Value.Resolve(viewportSide, defaultSide) : viewportSide;

    private static (double HorizontalScale, double HorizontalOffset, double VerticalScale, double VerticalOffset) GetViewBoxTransform(SvgViewBox? viewBox, double width, double height, double outputScale)
    {
        if (viewBox is not SvgViewBox box) return (outputScale, 0, outputScale, 0);
        (double horizontalScale, double verticalScale, double horizontalOffset, double verticalOffset) = SvgGeometrySyntax.MapViewBox(box, width, height);
        return (horizontalScale * outputScale, horizontalOffset * outputScale, verticalScale * outputScale, verticalOffset * outputScale);
    }

    /// <summary>Probes root dimensions and the output transform without lowering, falling back to unit scales when the root cannot be resolved.</summary>
    internal static (double HorizontalScale, double VerticalScale) TryResolveOutputScales(ReadOnlyMemory<byte> svg, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
    {
        SvgAffineTransform map = TryResolveOutputMap(svg, options, budget, cancellationToken);
        return (map.A, map.D);
    }

    internal static SvgAffineTransform TryResolveOutputMap(ReadOnlyMemory<byte> svg, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
    {
        try
        {
            using RenderBudget.BudgetReservation workspace = budget.Reserve(SvgRootParser.GetXmlReaderWorkspaceBytes(svg.Length, options.Limits.MaximumAttributesPerElement), SvgMemoryCategory.Parser);
            ParserWorkCounter probe = new(options.Limits.MaximumWorkUnits);
            XmlReaderSettings settings = SvgXmlReaderPolicy.Create(svg, options.Limits, budget, probe, cancellationToken);
            using BudgetedParserList<CssRule> rules = new(budget, SvgMemoryCategory.Parser, probe, cancellationToken);
            using BudgetedParserStringStore strings = new(budget);
            SvgRootStyleSheetParser.Collect(svg, settings, options.Limits, rules, strings, budget, probe, cancellationToken);
            using ReadOnlyMemoryStream stream = new(svg);
            using XmlReader reader = XmlReader.Create(stream, settings);
            while (reader.Read())
            {
                probe.Consume(cancellationToken);
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (!reader.LocalName.Equals("svg", StringComparison.Ordinal)) return SvgAffineTransform.Identity;
                SvgRootAttributes root = SvgRootAttributes.Parse(reader, SvgRootParser.CssNamedColors, rules, probe, cancellationToken);
                (double width, double height) = ResolveOutputDimensions(options.Viewport, root.Width, root.Height, root.ViewBox);
                (double horizontalScale, double offsetX, double verticalScale, double offsetY) = GetViewBoxTransform(root.ViewBox, width, height, options.OutputScale);
                if (!double.IsFinite(horizontalScale) || !double.IsFinite(verticalScale) || horizontalScale <= 0 || verticalScale <= 0) return SvgAffineTransform.Identity;
                return SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, offsetX, offsetY);
            }

            return SvgAffineTransform.Identity;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is SvgException or XmlException or InvalidOperationException or OverflowException)
        {
            return SvgAffineTransform.Identity;
        }
    }

    private static SvgRoot ParseRoot(ReadOnlyMemory<byte> svg, SvgRenderOptions options, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        SvgAffineTransform outputMap = TryResolveOutputMap(svg, options, budget, cancellationToken);
        ParserWorkCounter counter = workCounter ?? new ParserWorkCounter(options.Limits.MaximumWorkUnits);
        try { return SvgRootParser.Parse(svg, options.Limits, budget, options.FontSnapshot, counter, cancellationToken, outputMap, options.Viewport); }
        catch (XmlException exception) { throw new SvgException(SvgErrorCode.MalformedInput, "SVG XML is malformed: " + exception.Message); }
    }

    private static void BlendSourceOver(Span<byte> destination, SvgColor source)
    {
        if (source.Alpha == 255) { destination[0] = source.Red; destination[1] = source.Green; destination[2] = source.Blue; destination[3] = 255; return; }
        if (source.Alpha == 0) return;
        BlendPremultipliedOver(
            destination,
            (source.Red * (source.Alpha + 1)) >> 8,
            (source.Green * (source.Alpha + 1)) >> 8,
            (source.Blue * (source.Alpha + 1)) >> 8,
            source.Alpha);
    }

    /// <summary>Composites one premultiplied source pixel over the premultiplied destination pixel.</summary>
    /// <remarks>Surfaces store premultiplied colors with SkAlphaMulQ premultiply and SkPMSrcOver compositing (src/core/SkColorPriv.h at the release pin); the capture un-premultiplies once at the end (see ApplyCaptureUnpremultiply). Straight paints premultiply on entry; layer content already is premultiplied. Color lanes saturate per the pin; the alpha lane provably stays within a byte for valid premultiplied inputs.</remarks>
    internal static void BlendPremultipliedOver(Span<byte> destination, int sourceRed, int sourceGreen, int sourceBlue, int sourceAlpha)
    {
        int scale = 256 - sourceAlpha;
        destination[0] = (byte)Math.Min(((destination[0] * scale) >> 8) + sourceRed, 255);
        destination[1] = (byte)Math.Min(((destination[1] * scale) >> 8) + sourceGreen, 255);
        destination[2] = (byte)Math.Min(((destination[2] * scale) >> 8) + sourceBlue, 255);
        destination[3] = (byte)(((destination[3] * scale) >> 8) + sourceAlpha);
    }

    private static SvgColor WithCoverage(SvgColor color, double coverage) => coverage == 1 ? color : new SvgColor(color.Red, color.Green, color.Blue, CoverageAlpha(color.Alpha, coverage));

    /// <summary>Quantizes one geometric coverage step to byte alpha, truncating opaque coverage times 256 per the matched browser source while translucent coverage keeps its verified rounding.</summary>
    internal static byte CoverageAlpha(byte alpha, double coverage)
    {
        if (alpha == 255)
        {
            int truncated = (int)(coverage * 256);
            if (truncated < 0) return 0;
            if (truncated > 255) return 255;
            return (byte)truncated;
        }

        return checked((byte)Math.Round(alpha * coverage, MidpointRounding.AwayFromZero));
    }

    private static byte MultiplyAlpha(byte left, byte right) => (byte)((left * right + 127) / 255);

    private enum RasterSurfaceMode { Capture, PremultipliedPattern, PremultipliedFilter, PremultipliedGlyph }

    /// <summary>Owns the pixel surface and its budget reservation until encoding or a failed caller releases it.</summary>
    internal readonly record struct RasterizedSurface(int Width, int Height, byte[] Pixels, RenderBudget.BudgetReservation Reservation, long WorkUnits) : IDisposable
    {
        public void Dispose() => Reservation.Dispose();
    }

    /// <summary>Owns the parsed scene and surface reservations until one output path has completed or failed.</summary>
    private sealed class RasterizedConversion : IDisposable
    {
        private readonly SvgRoot _root;

        internal RasterizedConversion(RenderBudget budget, SvgRoot root, RasterizedSurface surface)
        {
            Budget = budget;
            _root = root;
            Surface = surface;
        }

        internal RenderBudget Budget { get; }

        internal RasterizedSurface Surface { get; }

        public void Dispose()
        {
            Surface.Dispose();
            _root.Dispose();
        }
    }
}
