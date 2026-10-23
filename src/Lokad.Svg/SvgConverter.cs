namespace Lokad.Svg;

using Lokad.Svg.Parsing;
using Lokad.Svg.Encoding;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;
using System.Xml;

/// <summary>Converts static SVG bytes to PNG bytes.</summary>
public static partial class SvgConverter
{
    /// <summary>Inspects root sizing using the conversion CSS/unit rules without constructing a scene or accessing fonts.</summary>
    /// <remarks>Uses the logical viewport and parsing limits from options. Output scale, background, font snapshot and raster limits do not affect the result. Unsupported drawing content is not a renderability check.</remarks>
    public static SvgDocumentDimensions InspectDimensions(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ValidateInvocation(svg, options, cancellationToken);
        RenderBudget budget = new(options.Limits.MaximumAccountedBytes);
        try { return SvgDocumentSizing.Inspect(svg, options, budget, new(options.Limits.MaximumWorkUnits), cancellationToken); }
        catch (XmlException exception) { throw new SvgException(SvgErrorCode.MalformedInput, "SVG XML is malformed: " + exception.Message); }
    }

    private const int GlyphSamplesPerAxis = 4;
    private enum PathCoordinateSpace { User, Device }
    /// <summary>Owns mapped polygon points with raster bounds, clip and their surface reservation until the rasterizer releases them.</summary>
    private readonly record struct MappedPolygon(SvgDevicePoint[] Points, int StartX, int StartY, int EndX, int EndY, int PathStartX, int PathStartY, int PathEndX, int PathEndY, (double Left, double Top, double Right, double Bottom)? Clip, SvgClipAntialiasing ClipAntialiasing, bool ClipContainsPath, SvgDeviceBounds CompleteBounds, SvgQuadSpan[] QuadSpans, SvgCubicSpan[] CubicSpans, RenderBudget.BudgetReservation? Reservation, int[] ContourEnds, int[]? Signs, SvgPreparedPathContours? Authored, SvgAffineTransform AuthoredMap, SvgPolygonMapping Mapping) : IDisposable
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
    {
        RenderBudget budget = new(options.Limits.MaximumAccountedBytes);
        ParserWorkCounter counter = new(options.Limits.MaximumWorkUnits);
        SvgRoot root;
        try
        {
            using SvgDocumentPreparation preparation = SvgDocumentPreparation.Create(svg, options.Limits, budget, counter, cancellationToken);
            options = preparation.ResolveRasterOptions(options);
            SvgAffineTransform outputMap = preparation.GetOutputMap(options);
            root = SvgRootParser.ParsePrepared(svg, options.Limits, budget, options.FontSnapshot, counter, cancellationToken,
                outputMap, options.Viewport, options.OutputScale, preparation);
        }
        catch (XmlException exception) { throw new SvgException(SvgErrorCode.MalformedInput, "SVG XML is malformed: " + exception.Message); }
        try
        {
            if (root.HasUnsupportedChild) throw new SvgException(SvgErrorCode.UnsupportedFeature, "This SVG child element is not implemented yet.", null, root.UnsupportedDiagnostic);
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
        /// <summary>Retains the native padded source-bitmap origin in the parent's device coordinates.</summary>
        internal SvgCpuBitmapOrigin? ImageShaderOrigin { get; init; }
        /// <summary>Substitutes the one outer binding of a borrowed layer-space source scene.</summary>
        internal SvgFilterReplayBinding? FilterBinding { get; init; }
        /// <summary>Maps borrowed source-scene coordinates into the scale-only filter layer; resources retain their separate local coordinates.</summary>
        internal SvgAffineTransform SourcePlacement { get; init; } = SvgAffineTransform.Identity;
        /// <summary>Retains native source-image bounds independently of compact result storage; capture adds its transparent border.</summary>
        internal SvgPaintBounds? NativeSourceBounds { get; init; }
        /// <summary>Retains the exact source/layer relation when nested filters share their authored ancestry.</summary>
        internal SvgFilterSourceMap? FilterSourceMap { get; init; }
        /// <summary>Maps authored scene coordinates through the native source device, retaining each float concatenation.</summary>
        internal SvgAffineTransform? NativeCapturePlacement { get; init; }
    }

    private readonly record struct SvgFilterReplayBinding(int Index, SvgFilterBinding Binding);
    private readonly record struct SvgFilterSourceMap(SvgAffineTransform Source, SvgAffineTransform Layer, SvgAffineTransform NativePlacement);

    private static RasterizedSurface Rasterize(SvgRoot root, SvgRenderOptions options, RenderBudget budget, RasterSurfaceMode surfaceMode, RasterFrame? frame, CancellationToken cancellationToken)
    {
        (double width, double height) = ResolveOutputDimensions(options.Viewport, root.Width, root.Height, root.ViewBox);
        double scaledWidth = frame?.Width ?? options.ResolvedRasterSize?.Width ?? Math.Round(width * options.OutputScale, MidpointRounding.AwayFromZero);
        double scaledHeight = frame?.Height ?? options.ResolvedRasterSize?.Height ?? Math.Round(height * options.OutputScale, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(scaledWidth) || !double.IsFinite(scaledHeight) || scaledWidth <= 0 || scaledHeight <= 0 || scaledWidth != Math.Truncate(scaledWidth) || scaledHeight != Math.Truncate(scaledHeight)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive finite integral output dimensions are implemented currently.");
        if (scaledWidth > options.Limits.MaximumDimension || scaledHeight > options.Limits.MaximumDimension) throw new SvgException(SvgErrorCode.LimitExceeded, "Output dimensions exceed the configured dimension limit.");
        int outputWidth = (int)scaledWidth;
        int outputHeight = (int)scaledHeight;
        SvgCpuRasterGrid rasterGrid = new(outputWidth, outputHeight, frame?.SingleBitmap == true);
        SvgAffineTransform sourcePlacement = frame?.SourcePlacement ?? SvgAffineTransform.Identity;
        if (!sourcePlacement.TryInvert(out SvgAffineTransform sourceInverse))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filter source placement must be invertible.");
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
        // The native root viewport is a binary clip. Fit mode can retain a
        // one-pixel canvas after that clip rounds to empty; padding adds no content.
        bool emptyRootViewport = frame is null && options.RasterBounds is not null
            && (MathF.Floor((float)(width * options.OutputScale) + .5f) == 0
                || MathF.Floor((float)(height * options.OutputScale) + .5f) == 0);
        bool maskClipAntialiased = false;
        bool maskLuminanceLayer = false;
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
        SvgFilterDeferredImage? deferredImage = null;
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
            if (!emptyRootViewport && surfaceMode == RasterSurfaceMode.Capture && TryResolveSolidCanvas(out SvgColor solidColor))
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
            else if (!emptyRootViewport)
            {
                int commandIndex = RenderCommands(0, pixels, false);
                if (commandIndex != root.Commands.Length) throw new SvgException(SvgErrorCode.MalformedInput, "SVG group structure is unbalanced.");
            }
            if (surfaceMode == RasterSurfaceMode.Capture) ApplyCaptureUnpremultiply(pixels, pixelCount, cancellationToken);
            return new RasterizedSurface(outputWidth, outputHeight, pixels, surface, workUnits) { DeferredImage = deferredImage };
        }
        catch
        {
            surface.Dispose();
            throw;
        }
        finally { patternTileCache?.Dispose(); }

        SvgPaintBounds FilterRegion(SvgFilterBinding binding, SvgFilterResource resource, ParserWorkCounter counter, SvgAffineTransform output)
        {
            SvgFilterRegion region = resource.Region;
            SvgPaintBounds box = binding.ObjectBounds;
            bool objectUnits = region.Units == SvgFilterUnits.ObjectBoundingBox;
            double basisX = objectUnits ? box.Right - box.Left : binding.LengthBases.Width ?? width;
            double basisY = objectUnits ? box.Bottom - box.Top : binding.LengthBases.Height ?? height;
            double x = Length(region.X, basisX), y = Length(region.Y, basisY);
            double w = Length(region.Width, basisX), h = Length(region.Height, basisY);
            if (w <= 0 || h <= 0) return default;
            SvgAffineTransform map = output;
            if (objectUnits) { x += box.Left; y += box.Top; }
            else map = map.ComposeChecked(binding.Transform);
            SvgPaintBounds bounds = new SvgPaintBounds((float)x, (float)y, (float)(x + w), (float)(y + h)).Map(map, counter, cancellationToken);
            return bounds;

            double Length(SvgFilterLength length, double basis) => length.Percentage ? length.Value * basis / 100 : objectUnits ? length.Value * basis : length.Value;
        }

        // Borrow source geometry when rebasing a nested binding. Effects remain
        // separate from raw object/paint bounds; each bounded visit charges work.
        SvgFilterBinding PlaceFilterBinding(SvgFilterBinding binding, int first, ParserWorkCounter work)
        {
            if (sourcePlacement == SvgAffineTransform.Identity) return binding;
            SvgAffineTransform transform = frame?.FilterSourceMap is {} relation && relation.Source == binding.Transform
                ? relation.Layer : sourcePlacement.ComposeChecked(binding.Transform);
            SvgPaintBounds? objects = null, visuals = null;
            int depth = 0;
            for (int index = first; index < root.Commands.Length; index++)
            {
                work.Consume(cancellationToken);
                SvgDrawCommand command = root.Commands[index];
                if (command.Kind == SvgDrawCommandKind.BeginGroup) depth++;
                else if (command.Kind == SvgDrawCommandKind.EndGroup)
                {
                    if (depth-- == 0) return binding with { Transform = transform, ObjectBounds = objects ?? default, VisualBounds = visuals ?? objects ?? default };
                }
                else if (command.Kind == SvgDrawCommandKind.Geometry)
                {
                    SvgGeometry geometry = root.Geometries[command.GeometryIndex];
                    SvgRecordedPaintValue paint = GetRecordedPaintValue(geometry.RecordedPaint);
                    if (paint.IsEffectAnchor && paint.ObjectBounds is null) continue;
                    SvgPaintBounds box, visual;
                    if (geometry.AuthoredPath is {} path)
                    {
                        box = PathBounds(path);
                        visual = PathBounds(path.StrokeOutline ?? path);
                    }
                    else if (geometry.Kind == SvgGeometryKind.GlyphContours && geometry.Glyph.Source is {} glyph)
                    {
                        box = glyph.LayoutBounds(sourcePlacement, work, cancellationToken);
                        if (!binding.Transform.TryInvert(out SvgAffineTransform inverse)) return binding with { Transform = transform, ObjectBounds = default, VisualBounds = default };
                        SvgPaintBounds layout = glyph.LayoutBounds(inverse, work, cancellationToken);
                        visual = new SvgPaintBounds(MathF.Floor(layout.Left), MathF.Floor(layout.Top), MathF.Ceiling(layout.Right), MathF.Ceiling(layout.Bottom)).Map(transform, work, cancellationToken);
                        float inflation = (float)geometry.Glyph.StrokeBoundsInflation;
                        visual = new(visual.Left - inflation, visual.Top - inflation, visual.Right + inflation, visual.Bottom + inflation);
                    }
                    else
                    {
                        box = (paint.ObjectBounds ?? default).Map(sourcePlacement, work, cancellationToken);
                        visual = (paint.VisualBounds ?? paint.ObjectBounds ?? default).Map(sourcePlacement, work, cancellationToken);
                    }
                    if (!paint.IsMarkerContent) objects = Union(objects, box);
                    visuals = Union(visuals, visual);
                }
            }
            throw new SvgException(SvgErrorCode.MalformedInput, "Nested filter source is unbalanced.");

            SvgPaintBounds PathBounds(SvgAuthoredPath path)
            {
                SvgRectangle box = SvgPathBounds.Compute(path.Commands, work, cancellationToken);
                return new SvgPaintBounds((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height))
                    .Map(sourcePlacement.ComposeChecked(path.SceneMap), work, cancellationToken);
            }
            static SvgPaintBounds Union(SvgPaintBounds? prior, SvgPaintBounds value)
                => prior is {} box ? new(MathF.Min(box.Left, value.Left), MathF.Min(box.Top, value.Top), MathF.Max(box.Right, value.Right), MathF.Max(box.Bottom, value.Bottom)) : value;
        }

        SvgAffineTransform? PlaceNativeSource(SvgCpuBitmapOrigin origin)
        {
            if (frame?.NativeCapturePlacement is {} parent)
                return SvgAffineTransform.FromTranslation(-origin.X, -origin.Y).ComposeNative(parent);
            return frame?.FilterSourceMap is {} relation
                ? SvgAffineTransform.FromTranslation(horizontalOffset - origin.X, verticalOffset - origin.Y).ComposeNative(relation.NativePlacement) : null;
        }

        int RenderFilter(int first, SvgDrawCommand command, byte[] destination)
        {
            SvgFilterArena arena = root.FilterResources ?? throw new SvgException(SvgErrorCode.MalformedInput, "Filter resources are missing.");
            SvgFilterBinding binding = frame?.FilterBinding is {} replay && replay.Index == command.MaskContentIndex
                ? replay.Binding : arena.BindingAt(command.MaskContentIndex);
            SvgFilterResource resource = arena.ResourceAt(binding.ResourceIndex);
            int end = first, depth = 0, nestedDepth = -1;
            bool hasSourceImages = false;
            SvgPaintBounds? nestedSourceBounds = null;
            for (; end < root.Commands.Length; end++)
            {
                if (workUnits == options.Limits.MaximumWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Filter source traversal exceeds the work limit.");
                workUnits++;
                cancellationToken.ThrowIfCancellationRequested();
                SvgDrawCommand item = root.Commands[end];
                if (nestedDepth < 0 && item.Kind == SvgDrawCommandKind.Geometry && root.Geometries[item.GeometryIndex].Kind == SvgGeometryKind.EmbeddedImage)
                    hasSourceImages = true;
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
                        child = PlaceFilterBinding(child, end + 1, childRegionWork);
                        SvgPaintBounds childRegion = FilterRegion(child, arena.ResourceAt(child.ResourceIndex), childRegionWork,
                            SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset));
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
            if (frame?.FilterBinding is not {} placed || placed.Index != command.MaskContentIndex)
            {
                ParserWorkCounter placementWork = new(options.Limits.MaximumWorkUnits - workUnits);
                binding = PlaceFilterBinding(binding, first, placementWork);
                workUnits += placementWork.Used;
            }
            if (binding.Transform.B != 0 || binding.Transform.C != 0)
                return RenderAffineFilter();
            ParserWorkCounter regionWork = new(options.Limits.MaximumWorkUnits - workUnits);
            SvgPaintBounds region = FilterRegion(binding, resource, regionWork,
                SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset));
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
            bool boundedSource = activeViewportBounds is not null || layerBounds.Left != 0 || layerBounds.Top != 0
                || layerBounds.Right != outputWidth || layerBounds.Bottom != outputHeight;
            if (boundedSource && frame?.NativeSourceBounds is null)
            {
                // Native saveLayer rounds its visual hint in parameter space before
                // mapping to the clipped device. A tight ink box changes clipped curves.
                ParserWorkCounter hintWork = new(options.Limits.MaximumWorkUnits - workUnits);
                bool objectSourceUnits = resource.PrimitiveUnits == SvgFilterUnits.ObjectBoundingBox;
                SvgAffineTransform coordinates = objectSourceUnits
                    ? SvgAffineTransform.FromScaleOffset(primitiveMap.ScaleX, primitiveMap.ScaleY,
                        binding.ObjectBounds.Left * horizontalScale + horizontalOffset, binding.ObjectBounds.Top * verticalScale + verticalOffset)
                    : SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset).ComposeChecked(binding.Transform);
                SvgFilterRegionContext context = new(coordinates,
                    objectSourceUnits ? new(1, 1) : new(binding.LengthBases.Width ?? width, binding.LengthBases.Height ?? height), region);
                SvgPaintBounds hint = SvgFilterPlayback.SourceLayerBounds(resource, primitiveMap, context,
                    new((float)(visual.Left * horizontalScale + horizontalOffset), (float)(visual.Top * verticalScale + verticalOffset),
                        (float)(visual.Right * horizontalScale + horizontalOffset), (float)(visual.Bottom * verticalScale + verticalOffset)), budget, hintWork, cancellationToken);
                float sx = (float)(binding.Transform.A * horizontalScale), sy = (float)(binding.Transform.D * verticalScale);
                float tx = (float)(binding.Transform.E * horizontalScale + horizontalOffset), ty = (float)(binding.Transform.F * verticalScale + verticalOffset);
                if (sx > 0 && sy > 0)
                    hint = new(MathF.Floor((hint.Left - tx) / sx) * sx + tx, MathF.Floor((hint.Top - ty) / sy) * sy + ty,
                        MathF.Ceiling((hint.Right - tx) / sx) * sx + tx, MathF.Ceiling((hint.Bottom - ty) / sy) * sy + ty);
                sourceLeft = Math.Floor(hint.Left); sourceTop = Math.Floor(hint.Top);
                sourceRight = Math.Ceiling(hint.Right); sourceBottom = Math.Ceiling(hint.Bottom);
                workUnits += hintWork.Used;
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
            SvgCpuBitmapOrigin? imageShaderOrigin = null;
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
                SvgPaintBounds painted = new((float)(visual.Left * horizontalScale + horizontalOffset), (float)(visual.Top * verticalScale + verticalOffset),
                    (float)(visual.Right * horizontalScale + horizontalOffset), (float)(visual.Bottom * verticalScale + verticalOffset));
                SvgPaintBounds hint = SvgFilterPlayback.SourceLayerBounds(resource, primitiveMap, sourceContext, painted, budget, sourceRequestWork, cancellationToken);
                workUnits += sourceRequestWork.Used;
                // Floods and later graph results can paint without source ink in the
                // requested region. Cull only after propagating the filter bounds.
                if (hint.Right + paddingX < requestedLeft || hint.Bottom + paddingY < requestedTop
                    || hint.Left - paddingX > requestedRight || hint.Top - paddingY > requestedBottom) return end + 1;
                double nativeLeft = Math.Floor(MathF.Max(hint.Left, needed.Left)) - 1, nativeTop = Math.Floor(MathF.Max(hint.Top, needed.Top)) - 1;
                if (!double.IsFinite(nativeLeft) || !double.IsFinite(nativeTop) || nativeLeft < int.MinValue || nativeLeft > int.MaxValue || nativeTop < int.MinValue || nativeTop > int.MaxValue)
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Filter source origin exceeds the coordinate range.");
                imageShaderOrigin = new((int)nativeLeft, (int)nativeTop);
                double captureLeft = Math.Max(left, needed.Left), captureTop = Math.Max(top, needed.Top);
                double captureRight = Math.Min(right, needed.Right), captureBottom = Math.Min(bottom, needed.Bottom);
                if (captureRight <= captureLeft || captureBottom <= captureTop)
                {
                    // The graph requests no drawable input here. Keep a transparent
                    // source grid near its output instead of retaining remote ink.
                    (left, top, right, bottom) = (requestedLeft, requestedTop, requestedRight, requestedBottom);
                }
                else if (boundedSource || nestedSourceBounds is not null || hasSourceImages)
                {
                    (left, top, right, bottom) = (captureLeft, captureTop, captureRight, captureBottom);
                }
            }
            // Shifted and chained results may feed visible pixels from outside the
            // final output window. A single blur needs only the complete source;
            // graphs retain conservative intermediate halo/shift allowances.
            if (frame is { FilterBinding: {} sourceBinding, NativeSourceBounds: {} sourceBounds } && sourceBinding.Index == command.MaskContentIndex)
            {
                // Clip construction and ordered shader arithmetic use the native
                // padded bitmap, even when the final filter result is cropped.
                left = sourceBounds.Left * horizontalScale + horizontalOffset;
                top = sourceBounds.Top * verticalScale + verticalOffset;
                right = sourceBounds.Right * horizontalScale + horizontalOffset;
                bottom = sourceBounds.Bottom * verticalScale + verticalOffset;
            }
            // Empty source scopes still run their filter graph over transparent input.
            if (right <= left || bottom <= top)
                (left, top, right, bottom) = (requestedLeft, requestedTop, requestedRight, requestedBottom);
            double replayPaddingX = resource.Primitives.Items.Length > 1 ? paddingX : offsetPaddingX;
            double replayPaddingY = resource.Primitives.Items.Length > 1 ? paddingY : offsetPaddingY;
            double frameLeft = Math.Floor(Math.Min(left, requestedLeft - replayPaddingX)), frameTop = Math.Floor(Math.Min(top, requestedTop - replayPaddingY));
            double frameWidth = Math.Ceiling(Math.Max(right, requestedRight + replayPaddingX)) - frameLeft;
            double frameHeight = Math.Ceiling(Math.Max(bottom, requestedBottom + replayPaddingY)) - frameTop;
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
                { PictureOutput = surfaceMode == RasterSurfaceMode.PremultipliedPattern, RasterPlacement = new(new((int)frameLeft, (int)frameTop), rasterGrid, layerBounds), SourceBounds = new((float)(left - frameLeft), (float)(top - frameTop), (float)(right - frameLeft), (float)(bottom - frameTop)), IntermediateOutput = surfaceMode == RasterSurfaceMode.PremultipliedFilter && (frame?.FilterBinding is not {} outerBinding || outerBinding.Index != command.MaskContentIndex), DeferFinalOffset = frame?.FilterBinding is {} deferredBinding && deferredBinding.Index == command.MaskContentIndex && frame?.FilterSourceMap is not null } };
            long copyWork = (long)sw * sh * 4 + end - first;
            if (copyWork >= options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Filter source capture exceeds the work limit.");
            workUnits += copyWork;
            using RenderBudget.BudgetReservation? commandsOwner = end > first
                ? budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(end - first), SvgMemoryCategory.Scene) : null;
            SvgDrawCommand[] sourceCommands = new SvgDrawCommand[end - first];
            for (int index = first; index < end; index++) { cancellationToken.ThrowIfCancellationRequested(); sourceCommands[index - first] = root.Commands[index]; }
            SvgRenderLimits limits = options.Limits;
            SvgRenderLimits sourceLimits = new(limits.MaximumAccountedBytes, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels, limits.MaximumEncodedPngBytes,
                limits.MaximumNodes, limits.MaximumPathCommands, checked((int)(limits.MaximumWorkUnits - workUnits)), limits.MaximumXmlDepth, limits.MaximumAttributesPerElement);
            SvgRenderOptions sourceOptions = new(options.Viewport, options.OutputScale, SvgColor.Transparent, sourceLimits, options.FontSnapshot);
            SvgRoot borrowed = root with { Commands = sourceCommands, ParserWorkUnits = 0 };
            RasterFrame captureFrame = new(sw + 2, sh + 2, horizontalScale, horizontalOffset - left + 1, verticalScale, verticalOffset - top + 1)
            { SingleBitmap = true, ContentClip = new(1, 1, sw + 1, sh + 1), ParentOrigin = new(0, 0), CaptureOrigin = new((int)left - 1, (int)top - 1), ImageShaderOrigin = imageShaderOrigin, SourcePlacement = sourcePlacement, NativeSourceBounds = frame?.NativeSourceBounds, FilterSourceMap = frame?.FilterSourceMap, NativeCapturePlacement = PlaceNativeSource(new((int)left - 1, (int)top - 1)) };
            // Object-box distances already use the placed bounding coordinates.
            // Only user-space primitives conjugate the authored source transform.
            if (!objectPrimitiveUnits && captureFrame.NativeCapturePlacement is {} nativePlacement && primitiveMap.Regions is {} nativeRegions)
            {
                SvgAffineTransform parameters = nativePlacement.ComposeNative(arena.BindingAt(command.MaskContentIndex).Transform);
                primitiveMap = primitiveMap with { Regions = nativeRegions with { NativeMapping = new(new((float)parameters.A, (float)parameters.C, (float)parameters.E, (float)parameters.B, (float)parameters.D, (float)parameters.F), new((int)(left - 1 - frameLeft), (int)(top - 1 - frameTop))) } };
            }
            using RasterizedSurface captured = Rasterize(borrowed, sourceOptions, budget, RasterSurfaceMode.PremultipliedFilter, captureFrame, cancellationToken);
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
            if (filtered.DeferredImage is {} deferred)
            {
                ParserWorkCounter deferredWork = new(options.Limits.MaximumWorkUnits - workUnits);
                SvgAffineTransform move = SvgAffineTransform.FromTranslation(frameLeft, frameTop);
                deferredImage = deferred with { SourceBounds = deferred.SourceBounds.Map(move, deferredWork, cancellationToken), Clip = deferred.Clip.Map(move, deferredWork, cancellationToken) };
                workUnits += deferredWork.Used;
                long copy = (long)outputWidth * outputHeight;
                if (copy > limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Deferred filter capture exceeds its work limit.");
                workUnits += copy;
                for (int y = 0; y < outputHeight; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int x = 0; x < outputWidth; x++)
                    {
                        int sx = x - (int)frameLeft, sy = y - (int)frameTop;
                        if ((uint)sx < (uint)fw && (uint)sy < (uint)fh)
                            filtered.Pixels.AsSpan((sy * fw + sx) * 4, 4).CopyTo(destination.AsSpan((y * outputWidth + x) * 4, 4));
                    }
                }
                return end + 1;
            }
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
                    // Native nonopaque restoration materializes the converted image
                    // before packed paint-alpha modulation. Opaque shader results
                    // retain their unquantized transfer route.
                    if (filtered.Samples is {} shaderSamples && opacity == 1)
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

            // Filter-layer mapping adapts pinned SkCanvas.cpp and SkImageFilterTypes.cpp.
            // Copyright 2008 The Android Open Source Project; 2019 Google LLC.
            // See THIRD_PARTY_NOTICES.txt for the BSD license and source revision.
            int RenderAffineFilter()
            {
                // Filter kernels require scale/translate coordinates. Keep their
                // layer raster separate from the residual rotation/skew, just as
                // native source construction is separate from final image sampling.
                if (!binding.Transform.TryInvert(out SvgAffineTransform inverseSource)) return end + 1;
                SvgAffineTransform ctm = SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset)
                    .ComposeNative(binding.Transform);
                SvgShaderMatrix native = new((float)ctm.A, (float)ctm.C, (float)ctm.E, (float)ctm.B, (float)ctm.D, (float)ctm.F);
                (float scaleX, float scaleY) = native.GetPictureScale();
                SvgShaderMatrix remainder = new(native.ScaleX * (1f / scaleX), native.SkewX * (1f / scaleY), native.TranslateX,
                    native.SkewY * (1f / scaleX), native.ScaleY * (1f / scaleY), native.TranslateY);
                if (!remainder.TryInvert(out SvgShaderMatrix inverse)) return end + 1;
                SvgAffineTransform layerMap = SvgAffineTransform.FromScale(scaleX, scaleY);
                SvgAffineTransform parameterInverse = sourcePlacement == SvgAffineTransform.Identity ? inverseSource : inverseSource.ComposeChecked(sourcePlacement);
                SvgAffineTransform placement = layerMap.ComposeChecked(parameterInverse);
                ParserWorkCounter preparation = new(options.Limits.MaximumWorkUnits - workUnits);
                int count = end - first;
                using RenderBudget.BudgetReservation replayOwner = budget.Reserve(
                    checked(ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(count + 2)
                        + ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(count)), SvgMemoryCategory.Scene);
                SvgDrawCommand[] commands = new SvgDrawCommand[count + 2];
                SvgGeometry[] geometries = new SvgGeometry[count];
                int geometryCount = 0;
                bool preparationCharged = false;
                SvgPaintBounds? objectBounds = null, visualBounds = null;
                try
                {
                    commands[0] = command with { SourceOpacity = SvgPaintOpacity.Opaque };
                    for (int index = first; index < end; index++)
                    {
                        preparation.Consume(cancellationToken);
                        SvgDrawCommand item = root.Commands[index];
                        if (item.Kind == SvgDrawCommandKind.Geometry)
                        {
                            SvgGeometry geometry = root.Geometries[item.GeometryIndex];
                            if (GetRecordedPaintValue(geometry.RecordedPaint).IsEffectAnchor)
                            {
                                SvgRecordedPaintValue paint = GetRecordedPaintValue(geometry.RecordedPaint);
                                if (paint.ObjectBounds is {} anchorBounds)
                                {
                                    if (!paint.IsMarkerContent) objectBounds = Union(objectBounds, anchorBounds.Map(placement, preparation, cancellationToken));
                                    visualBounds = Union(visualBounds, (paint.VisualBounds ?? anchorBounds).Map(placement, preparation, cancellationToken));
                                }
                                geometries[geometryCount] = geometry;
                                commands[index - first + 1] = item with { GeometryIndex = geometryCount++ };
                                continue;
                            }
                            if (geometry.Kind == SvgGeometryKind.GlyphContours && geometry.Glyph.Source is not null)
                            {
                                geometries[geometryCount] = geometry;
                                item = item with { GeometryIndex = geometryCount++ };
                                SvgPaintBounds layout = geometry.Glyph.Source.LayoutBounds(parameterInverse, preparation, cancellationToken);
                                objectBounds = Union(objectBounds, layout.Map(layerMap, preparation, cancellationToken));
                                // Native text recording encloses the font layout before its
                                // filter layer maps the resulting integer visual rectangle.
                                SvgPaintBounds glyphVisual = new SvgPaintBounds(MathF.Floor(layout.Left), MathF.Floor(layout.Top), MathF.Ceiling(layout.Right), MathF.Ceiling(layout.Bottom))
                                    .Map(layerMap, preparation, cancellationToken);
                                float inflation = (float)geometry.Glyph.StrokeBoundsInflation;
                                visualBounds = Union(visualBounds, new(glyphVisual.Left - inflation, glyphVisual.Top - inflation, glyphVisual.Right + inflation, glyphVisual.Bottom + inflation));
                                commands[index - first + 1] = item;
                                continue;
                            }
                            if (geometry.Kind is not (SvgGeometryKind.PolygonFill or SvgGeometryKind.GradientPolygon or SvgGeometryKind.RadialPolygon or SvgGeometryKind.PatternPolygon) || geometry.AuthoredPath is not {} source)
                                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Affine filter sources require retained geometry or supplied glyphs.");
                            geometries[geometryCount] = geometry;
                            item = item with { GeometryIndex = geometryCount++ };
                            SvgRectangle box = SvgPathBounds.Compute(source.Commands, preparation, cancellationToken);
                            SvgPaintBounds bounds = new SvgPaintBounds((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height))
                                .Map(placement.ComposeChecked(source.SceneMap), preparation, cancellationToken);
                            if (!GetRecordedPaintValue(geometry.RecordedPaint).IsMarkerContent)
                                objectBounds = Union(objectBounds, bounds);
                            SvgAuthoredPath outline = source.StrokeOutline ?? source;
                            box = SvgPathBounds.Compute(outline.Commands, preparation, cancellationToken);
                            visualBounds = Union(visualBounds, new SvgPaintBounds((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height))
                                .Map(placement.ComposeChecked(outline.SceneMap), preparation, cancellationToken));
                        }
                        commands[index - first + 1] = item;
                    }
                    commands[^1] = root.Commands[end];
                    SvgPaintBounds objects = objectBounds ?? default, visual = visualBounds ?? default;
                    SvgFilterBinding local = binding with { Transform = layerMap, ObjectBounds = objects, VisualBounds = visual };
                    SvgPaintBounds layerRegion = FilterRegion(local, resource, preparation, SvgAffineTransform.Identity);
                    bool objectUnits = resource.PrimitiveUnits == SvgFilterUnits.ObjectBoundingBox;
                    SvgFilterDeviceMap distances = objectUnits ? new(objects.Right - objects.Left, objects.Bottom - objects.Top) : new(scaleX, scaleY);
                    SvgFilterRegionContext context = new(objectUnits
                        ? SvgAffineTransform.FromScaleOffset(distances.ScaleX, distances.ScaleY, objects.Left, objects.Top) : layerMap,
                        objectUnits ? new(1, 1) : new(binding.LengthBases.Width ?? width, binding.LengthBases.Height ?? height), layerRegion);
                    SvgPaintBounds hint = SvgFilterPlayback.SourceLayerBounds(resource, distances, context, visual, budget, preparation, cancellationToken);
                    SvgPaintBounds parameterHint = SvgFilterPlayback.RoundLayerBounds(new(hint.Left / scaleX, hint.Top / scaleY, hint.Right / scaleX, hint.Bottom / scaleY));
                    SvgPaintBounds nativeHint = new(parameterHint.Left * scaleX, parameterHint.Top * scaleY, parameterHint.Right * scaleX, parameterHint.Bottom * scaleY);
                    SvgPaintBounds nativeSource = SvgFilterPlayback.RoundLayerBounds(nativeHint);
                    SvgPaintBounds nativeImage = SvgFilterPlayback.SourceLayerBounds(resource, distances, context, nativeSource,
                        SvgFilterBoundsKind.StoredImage, budget, preparation, cancellationToken);
                    if (nativeImage.Right <= nativeImage.Left || nativeImage.Bottom <= nativeImage.Top) return end + 1;
                    SvgPaintBounds desired = new(layerBounds.Left, layerBounds.Top, layerBounds.Right, layerBounds.Bottom);
                    if (activeViewportBounds is {} clip)
                        desired = new(MathF.Max(desired.Left, clip.Left), MathF.Max(desired.Top, clip.Top), MathF.Min(desired.Right, clip.Right), MathF.Min(desired.Bottom, clip.Bottom));
                    desired = desired.Map(new(inverse.ScaleX, inverse.SkewY, inverse.SkewX, inverse.ScaleY, inverse.TranslateX, inverse.TranslateY), preparation, cancellationToken);
                    bool deferredSource = resource.Primitives.Items.Length > 0 && resource.Primitives.Items[^1].Kind == SvgFilterPrimitiveKind.Offset
                        && resource.Primitives.Items[^1].Input.Index < 0;
                    if (deferredSource)
                    {
                        SvgPaintBounds needed = SvgFilterPlayback.RequiredSourceBounds(resource, distances, context, desired, budget, preparation, cancellationToken);
                        nativeSource = new(MathF.Max(nativeSource.Left, needed.Left), MathF.Max(nativeSource.Top, needed.Top),
                            MathF.Min(nativeSource.Right, needed.Right), MathF.Min(nativeSource.Bottom, needed.Bottom));
                        if (nativeSource.Right <= nativeSource.Left || nativeSource.Bottom <= nativeSource.Top) return end + 1;
                        nativeImage = SvgFilterPlayback.SourceLayerBounds(resource, distances, context, nativeSource,
                            SvgFilterBoundsKind.StoredImage, budget, preparation, cancellationToken);
                    }
                    double left = Math.Floor(MathF.Max(MathF.Max(layerRegion.Left, hint.Left), desired.Left)), top = Math.Floor(MathF.Max(MathF.Max(layerRegion.Top, hint.Top), desired.Top));
                    double right = Math.Ceiling(MathF.Min(MathF.Min(layerRegion.Right, hint.Right), desired.Right)), bottom = Math.Ceiling(MathF.Min(MathF.Min(layerRegion.Bottom, hint.Bottom), desired.Bottom));
                    if (right <= left || bottom <= top) return end + 1;
                    if (deferredSource)
                    {
                        left = Math.Min(left, nativeSource.Left); top = Math.Min(top, nativeSource.Top);
                        right = Math.Max(right, nativeSource.Right); bottom = Math.Max(bottom, nativeSource.Bottom);
                    }
                    double frameWidth = right - left, frameHeight = bottom - top;
                    SvgRenderLimits limits = options.Limits;
                    if (!double.IsFinite(frameWidth) || !double.IsFinite(frameHeight) || left < int.MinValue || top < int.MinValue || right > int.MaxValue || bottom > int.MaxValue
                        || frameWidth > limits.MaximumDimension || frameHeight > limits.MaximumDimension || frameWidth * frameHeight > limits.MaximumPixels || frameWidth * frameHeight > int.MaxValue / 4)
                        throw new SvgException(SvgErrorCode.LimitExceeded, "Affine filter layer exceeds the configured surface limits.");
                    SvgPaintBounds restoreBounds = new SvgPaintBounds((float)left - 1, (float)top - 1, (float)right + 1, (float)bottom + 1)
                        .Map(new(remainder.ScaleX, remainder.SkewY, remainder.SkewX, remainder.ScaleY, remainder.TranslateX, remainder.TranslateY), preparation, cancellationToken);
                    int restoreLeft = Math.Max(layerBounds.Left, ClampFloor(restoreBounds.Left, outputWidth));
                    int restoreTop = Math.Max(layerBounds.Top, ClampFloor(restoreBounds.Top, outputHeight));
                    int restoreRight = Math.Min(layerBounds.Right, ClampCeiling(restoreBounds.Right, outputWidth));
                    int restoreBottom = Math.Min(layerBounds.Bottom, ClampCeiling(restoreBounds.Bottom, outputHeight));
                    workUnits += preparation.Used;
                    preparationCharged = true;
                    SvgRenderLimits layerLimits = new(limits.MaximumAccountedBytes, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels, limits.MaximumEncodedPngBytes,
                        limits.MaximumNodes, limits.MaximumPathCommands, checked((int)(limits.MaximumWorkUnits - workUnits)), limits.MaximumXmlDepth, limits.MaximumAttributesPerElement);
                    SvgRoot borrowed = root with { Geometries = geometries, Commands = commands, ParserWorkUnits = 0 };
                    using RasterizedSurface image = Rasterize(borrowed, new(options.Viewport, options.OutputScale, SvgColor.Transparent, layerLimits, options.FontSnapshot),
                        budget, RasterSurfaceMode.PremultipliedFilter,
                        new RasterFrame((int)frameWidth, (int)frameHeight, 1, -left, 1, -top)
                        { SingleBitmap = true, FilterBinding = new(command.MaskContentIndex, local), SourcePlacement = placement, NativeSourceBounds = nativeSource, FilterSourceMap = new(arena.BindingAt(command.MaskContentIndex).Transform, layerMap, new SvgAffineTransform(inverse.ScaleX, inverse.SkewY, inverse.SkewX, inverse.ScaleY, inverse.TranslateX, inverse.TranslateY).ComposeNative(SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset))) }, cancellationToken);
                    workUnits += image.WorkUnits;
                    // Native drawSpecial composes the stored image origin and
                    // transparent one-pixel outset before inverting its matrix.
                    // Subtracting that origin after inverse mapping changes phase.
                    SvgAffineTransform sourceDevice = new SvgAffineTransform(remainder.ScaleX, remainder.SkewY, remainder.SkewX, remainder.ScaleY, remainder.TranslateX, remainder.TranslateY)
                        .ComposeNative(SvgAffineTransform.FromTranslation(nativeSource.Left - 1, nativeSource.Top - 1));
                    if (image.DeferredImage is {} deferred)
                    {
                        RestoreDeferredImage(image, deferred, sourceDevice, nativeSource, new((int)left, (int)top), new(restoreLeft, restoreTop, restoreRight, restoreBottom));
                        return end + 1;
                    }
                    SvgShaderMatrix imageToDevice = SvgShaderMatrix.Concat(
                        new((float)sourceDevice.A, (float)sourceDevice.C, (float)sourceDevice.E, (float)sourceDevice.B, (float)sourceDevice.D, (float)sourceDevice.F),
                        new(1, 0, nativeImage.Left - (nativeSource.Left - 1), 0, 1, nativeImage.Top - (nativeSource.Top - 1)));
                    imageToDevice = imageToDevice with
                    {
                        TranslateX = imageToDevice.TranslateX + (-imageToDevice.ScaleX - imageToDevice.SkewX),
                        TranslateY = imageToDevice.TranslateY + (-imageToDevice.SkewY - imageToDevice.ScaleY)
                    };
                    if (!imageToDevice.TryInvert(out SvgShaderMatrix imageInverse)) return end + 1;
                    bool floatingOffset = resource.Primitives.Items.Length > 0
                        && resource.Primitives.Items[^1] is { Kind: SvgFilterPrimitiveKind.Offset, Input: { Index: < 0 } } finalPrimitive
                        && (finalPrimitive.ColorSpace ?? resource.ColorSpace) == SvgFilterColorSpace.LinearRgb;
                    long restoreWork = (long)Math.Max(0, restoreRight - restoreLeft) * Math.Max(0, restoreBottom - restoreTop) * (floatingOffset ? 8 : 4);
                    if (restoreWork > limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Affine filter restoration exceeds the configured work limit.");
                    workUnits += restoreWork;
                    int opacity = Math.Clamp((int)MathF.Floor(command.SourceOpacity.Value * foldedOpacity * 255 + .5f), 0, 255);
                    for (int y = restoreTop; y < restoreBottom; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        for (int x = restoreLeft; x < restoreRight; x++)
                        {
                            if ((x & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                            float px = x + .5f, py = y + .5f;
                            float u = px * imageInverse.ScaleX + (py * imageInverse.SkewX + imageInverse.TranslateX);
                            float v = px * imageInverse.SkewY + (py * imageInverse.ScaleY + imageInverse.TranslateY);
                            if (floatingOffset)
                            {
                                (u, v) = imageInverse.Map(px, py);
                                SvgShaderPaint floating = SvgShaderPaint.SampleAffine(image.Pixels, image.Width, image.Height, u, v,
                                    new((int)nativeImage.Left - 1 - (int)left, (int)nativeImage.Top - 1 - (int)top), opacity / 255f);
                                // A materialized offset source preserves its operating
                                // color-space roundtrip through the floating store.
                                floating = SvgFilterColorTransfer.Convert(SvgFilterColorTransfer.Convert(floating, SvgFilterColorSpace.LinearRgb), SvgFilterColorSpace.Srgb);
                                floating.Blend(destination.AsSpan((y * outputWidth + x) * 4, 4), activeViewportClip.Length == 0 ? (byte)255 : activeViewportClip[(y * outputWidth + x) * 4 + 3]);
                                continue;
                            }
                            SvgConstantShaderPaint sample = SvgConstantShaderPaint.SampleAffine(image.Pixels, image.Width, image.Height, u, v, true,
                                new((int)nativeImage.Left - 1 - (int)left, (int)nativeImage.Top - 1 - (int)top));
                            sample = new((byte)((sample.Red * opacity + 255) >> 8), (byte)((sample.Green * opacity + 255) >> 8),
                                (byte)((sample.Blue * opacity + 255) >> 8), (byte)((sample.Alpha * opacity + 255) >> 8));
                            int target = (y * outputWidth + x) * 4;
                            sample.Blend(destination.AsSpan(target, 4), activeViewportClip.Length == 0 ? (byte)255 : activeViewportClip[target + 3]);
                        }
                    }
                    return end + 1;
                }
                finally
                {
                    if (!preparationCharged) workUnits += preparation.Used;
                    // Geometry and paint owners remain borrowed from the parent scene.
                }

                // Restores one retained source image; storage stays with its captured
                // surface. Native image/crop coverage share the existing bounded walker.
                void RestoreDeferredImage(RasterizedSurface image, SvgFilterDeferredImage deferred, SvgAffineTransform sourceDevice,
                    SvgPaintBounds nativeSource, SvgCpuBitmapOrigin frameOrigin, SvgAnalyticClipBounds area)
                {
                    SvgRenderLimits limits = options.Limits;
                    SvgShaderMatrix device = new((float)sourceDevice.A, (float)sourceDevice.C, (float)sourceDevice.E,
                        (float)sourceDevice.B, (float)sourceDevice.D, (float)sourceDevice.F);
                    SvgShaderMatrix forward = SvgShaderMatrix.Concat(device, deferred.ImageToSource);
                    bool linear = deferred.Sampling == SvgFilterImageSampling.Linear;
                    bool floatingSamples = linear && (resource.Primitives.Items[^1].ColorSpace ?? resource.ColorSpace) == SvgFilterColorSpace.LinearRgb;
                    SvgShaderMatrix sampling = linear ? forward with
                    {
                        TranslateX = forward.TranslateX + (-forward.ScaleX - forward.SkewX),
                        TranslateY = forward.TranslateY + (-forward.SkewY - forward.ScaleY)
                    } : forward;
                    if (!sampling.TryInvert(out SvgShaderMatrix inverse)) return;
                    float sourceWidth = deferred.SourceBounds.Right - deferred.SourceBounds.Left;
                    float sourceHeight = deferred.SourceBounds.Bottom - deferred.SourceBounds.Top;
                    if (sourceWidth <= 0 || sourceHeight <= 0 || area.Right <= area.Left || area.Bottom <= area.Top) return;
                    Span<SvgDevicePoint> points = stackalloc SvgDevicePoint[4];
                    points[0] = MapPoint(forward, 0, 0); points[1] = MapPoint(forward, sourceWidth, 0);
                    points[2] = MapPoint(forward, sourceWidth, sourceHeight); points[3] = MapPoint(forward, 0, sourceHeight);
                    float clipLeft = MathF.Floor(deferred.Clip.Left) + frameOrigin.X - (nativeSource.Left - 1);
                    float clipTop = MathF.Floor(deferred.Clip.Top) + frameOrigin.Y - (nativeSource.Top - 1);
                    float clipRight = MathF.Ceiling(deferred.Clip.Right) + frameOrigin.X - (nativeSource.Left - 1);
                    float clipBottom = MathF.Ceiling(deferred.Clip.Bottom) + frameOrigin.Y - (nativeSource.Top - 1);
                    bool cropped = false;
                    for (int vertex = 0; vertex < 4; vertex++)
                    {
                        (float x, float y) = deferred.ImageToSource.Map(vertex is 1 or 2 ? sourceWidth : 0, vertex >= 2 ? sourceHeight : 0);
                        cropped |= x < clipLeft || x > clipRight || y < clipTop || y > clipBottom;
                    }
                    Span<SvgDevicePoint> cropPoints = stackalloc SvgDevicePoint[4];
                    SvgAnalyticClipBounds imageClip = area;
                    if (cropped)
                    {
                        cropPoints[0] = MapPoint(device, clipLeft, clipTop); cropPoints[1] = MapPoint(device, clipRight, clipTop);
                        cropPoints[2] = MapPoint(device, clipRight, clipBottom); cropPoints[3] = MapPoint(device, clipLeft, clipBottom);
                        float leftEdge = float.PositiveInfinity, topEdge = float.PositiveInfinity;
                        float rightEdge = float.NegativeInfinity, bottomEdge = float.NegativeInfinity;
                        foreach (SvgDevicePoint corner in cropPoints)
                        {
                            leftEdge = MathF.Min(leftEdge, (float)corner.X); topEdge = MathF.Min(topEdge, (float)corner.Y);
                            rightEdge = MathF.Max(rightEdge, (float)corner.X); bottomEdge = MathF.Max(bottomEdge, (float)corner.Y);
                        }
                        // Native AA clipping first limits image-edge construction to its tight binary bounds.
                        imageClip = new(Math.Max(area.Left, (int)MathF.Floor(leftEdge)), Math.Max(area.Top, (int)MathF.Floor(topEdge)),
                            Math.Min(area.Right, (int)MathF.Ceiling(rightEdge)), Math.Min(area.Bottom, (int)MathF.Ceiling(bottomEdge)));
                    }
                    if (imageClip.Right <= imageClip.Left || imageClip.Bottom <= imageClip.Top) return;
                    Charge(64);
                    SvgAnalyticContourPreparation imagePreparation = new(points, [4], [], false, null, SvgPolygonMapping.NativeFillEdges,
                        [], [], area, outputWidth, outputHeight);
                    scoped SvgAnalyticCoverage walker = default, cropWalker = default;
                    RenderBudget.BudgetReservation? cropOwner = null;
                    byte[] cropRow = [];
                    try
                    {
                        if (!linear)
                        {
                            walker = SvgAnalyticCoverage.Prepare(ref imagePreparation, area.Left, area.Right, area.Top, area.Bottom,
                                new(area.Left, area.Right), new SvgAnalyticClipContext(imageClip), SvgAnalyticPaintClip.Antialiased,
                                limits.MaximumWorkUnits - workUnits, budget, out long setupWork, out bool eligible, cancellationToken);
                            workUnits += setupWork;
                            if (!eligible) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Deferred affine image edges are outside the retained boundary.");
                        }
                        if (cropped)
                        {
                            SvgAnalyticContourPreparation cropPreparation = new(cropPoints, [4], [], false, null, SvgPolygonMapping.NativeFillEdges,
                                [], [], area, outputWidth, outputHeight);
                            cropWalker = SvgAnalyticCoverage.Prepare(ref cropPreparation, area.Left, area.Right, area.Top, area.Bottom,
                                new(area.Left, area.Right), new SvgAnalyticClipContext(area), SvgAnalyticPaintClip.Antialiased,
                                limits.MaximumWorkUnits - workUnits, budget, out long cropWork, out bool eligible, cancellationToken);
                            workUnits += cropWork;
                            if (!eligible) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Deferred affine crop edges are outside the retained boundary.");
                            int rowBytes = checked((area.Right - area.Left) * 4);
                            cropOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<byte>(rowBytes), SvgMemoryCategory.Surface);
                            cropRow = new byte[rowBytes];
                        }
                        Charge((long)(area.Right - area.Left) * (area.Bottom - area.Top) * 4);
                        int opacity = Math.Clamp((int)MathF.Floor(command.SourceOpacity.Value * foldedOpacity * 255 + .5f), 0, 255);
                        for (int y = area.Top; y < area.Bottom; y++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!linear)
                            {
                                walker.PrepareRow(y, limits.MaximumWorkUnits - workUnits, out long rowWork, cancellationToken);
                                workUnits += rowWork;
                            }
                            if (cropped)
                            {
                                Charge((long)(area.Right - area.Left) * 2);
                                cropRow.AsSpan().Clear();
                                cropWalker.PrepareRow(y, limits.MaximumWorkUnits - workUnits, out long cropWork, cancellationToken);
                                workUnits += cropWork;
                                for (int x = area.Left; x < area.Right; x++)
                                    cropWalker.PaintPixel(x - area.Left, cropRow.AsSpan((x - area.Left) * 4, 4),
                                        SvgAnalyticPaint.Solid(new(0, 0, 0, 255)), SvgAnalyticClipCoverage.Full);
                            }
                            for (int x = area.Left; x < area.Right; x++)
                            {
                                if ((x & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                                float px = x + .5f, py = y + .5f;
                                float u = px * inverse.ScaleX + (py * inverse.SkewX + inverse.TranslateX);
                                float v = px * inverse.SkewY + (py * inverse.ScaleY + inverse.TranslateY);
                                if (floatingSamples)
                                {
                                    // The linear-RGB filter boundary retains floating
                                    // samples through interpolation and the final store.
                                    (u, v) = inverse.Map(px, py);
                                    byte clipCoverage = cropped ? cropRow[(x - area.Left) * 4 + 3] : (byte)255;
                                    if (activeViewportClip.Length != 0) clipCoverage = (byte)((clipCoverage * activeViewportClip[(y * outputWidth + x) * 4 + 3] + 127) / 255);
                                    SvgShaderPaint.SampleAffine(image.Pixels, image.Width, image.Height, u, v,
                                        new((int)deferred.SourceBounds.Left - 1, (int)deferred.SourceBounds.Top - 1), opacity / 255f)
                                        .Blend(destination.AsSpan((y * outputWidth + x) * 4, 4), clipCoverage);
                                    continue;
                                }
                                SvgConstantShaderPaint sample;
                                if (linear)
                                    sample = SvgConstantShaderPaint.SampleAffine(image.Pixels, image.Width, image.Height, u, v, true,
                                        new((int)deferred.SourceBounds.Left - 1, (int)deferred.SourceBounds.Top - 1));
                                else
                                {
                                    int sx = (int)Math.Clamp(MathF.Floor(u), 0, sourceWidth - 1) + (int)deferred.SourceBounds.Left;
                                    int sy = (int)Math.Clamp(MathF.Floor(v), 0, sourceHeight - 1) + (int)deferred.SourceBounds.Top;
                                    int at = (sy * image.Width + sx) * 4;
                                    sample = (uint)sx < (uint)image.Width && (uint)sy < (uint)image.Height
                                        ? new(image.Pixels[at], image.Pixels[at + 1], image.Pixels[at + 2], image.Pixels[at + 3]) : default;
                                }
                                sample = new((byte)((sample.Red * opacity + 255) >> 8), (byte)((sample.Green * opacity + 255) >> 8),
                                    (byte)((sample.Blue * opacity + 255) >> 8), (byte)((sample.Alpha * opacity + 255) >> 8));
                                byte coverage = cropped ? cropRow[(x - area.Left) * 4 + 3] : (byte)255;
                                if (activeViewportClip.Length != 0) coverage = (byte)((coverage * activeViewportClip[(y * outputWidth + x) * 4 + 3] + 127) / 255);
                                Span<byte> target = destination.AsSpan((y * outputWidth + x) * 4, 4);
                                if (linear) sample.Blend(target, coverage);
                                else walker.PaintPixel(x - area.Left, target, SvgAnalyticPaint.Constant(sample), SvgAnalyticClipCoverage.FromMask(coverage));
                            }
                        }
                    }
                    finally { cropOwner?.Dispose(); cropWalker.Dispose(); walker.Dispose(); }

                    static SvgDevicePoint MapPoint(SvgShaderMatrix matrix, float x, float y)
                        => SvgAuthoredPath.MapPoint(new(matrix.ScaleX, matrix.SkewY, matrix.SkewX, matrix.ScaleY, matrix.TranslateX, matrix.TranslateY), new(x, y), new(0, 0));
                    void Charge(long amount)
                    {
                        if (amount > limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Deferred affine image restoration exceeds its work limit.");
                        workUnits += amount;
                    }
                }

                static SvgPaintBounds Union(SvgPaintBounds? prior, SvgPaintBounds next)
                    => prior is {} box ? new(MathF.Min(box.Left, next.Left), MathF.Min(box.Top, next.Top), MathF.Max(box.Right, next.Right), MathF.Max(box.Bottom, next.Bottom)) : next;
            }

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
                if (placements.Count == 0 || end == first) return true;
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
                        { SingleBitmap = true, ContentClip = new(1, 1, (int)sourceWidth - 1, (int)sourceHeight - 1), ParentOrigin = tile.Origin, CaptureOrigin = new((int)left, (int)top), SourcePlacement = sourcePlacement, NativeSourceBounds = frame?.NativeSourceBounds, FilterSourceMap = frame?.FilterSourceMap, NativeCapturePlacement = PlaceNativeSource(new((int)left, (int)top)) }, cancellationToken);
                    Charge(captured.WorkUnits);
                    int tw = tile.BitmapClip.Right - tile.BitmapClip.Left, th = tile.BitmapClip.Bottom - tile.BitmapClip.Top;
                    using RenderBudget.BudgetReservation tileOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<byte>(checked(tw * th * 4)), SvgMemoryCategory.Surface);
                    byte[] pixels = new byte[tw * th * 4];
                    bool linear = placements.Count > 1 && (final.ColorSpace ?? resource.ColorSpace) == SvgFilterColorSpace.LinearRgb;
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
            if (frame?.NativeSourceBounds is {} native)
                return new(checked((int)((native.Left - 1) * horizontalScale + horizontalOffset)), checked((int)((native.Top - 1) * verticalScale + verticalOffset)));
            SvgCpuBitmapOrigin tile = rasterGrid.GetBitmapOrigin(x, y);
            int left = Math.Max(tile.X, layerBounds.Left), top = Math.Max(tile.Y, layerBounds.Top);
            if (layerClipCoverage is {} coverage && coverage.TryGetBounds(tile, out SvgAnalyticClipBounds bitmapBounds))
            {
                left = Math.Max(left, bitmapBounds.Left);
                top = Math.Max(top, bitmapBounds.Top);
            }
            return new(left, top);
        }

        SvgDevicePoint SceneShaderPoint(double x, double y)
            => sourcePlacement == SvgAffineTransform.Identity ? new(x, y) : sourceInverse.Apply(x, y);

        int GradientDither(int x, int y)
        {
            SvgCpuBitmapOrigin origin = ShaderBitmapOrigin(x, y);
            // The compact capture may omit the requested image's halo. Dithering
            // still belongs to the native padded source bitmap used by image paints.
            if (surfaceMode == RasterSurfaceMode.PremultipliedFilter
                && frame is { NativeSourceBounds: null, ImageShaderOrigin: {} sourceOrigin } sourceFrame)
                origin = new(sourceOrigin.X - sourceFrame.CaptureOrigin.X, sourceOrigin.Y - sourceFrame.CaptureOrigin.Y);
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
            if (sourcePlacement != SvgAffineTransform.Identity) outputMap = outputMap.ComposeChecked(sourcePlacement);
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
                    if (value.RegionKind == SvgLayerRegionKind.Filter && root.FilterResources is {} filters)
                    {
                        SvgFilterBinding binding = frame?.FilterBinding is {} replay && replay.Index == value.MaskContentIndex
                            ? replay.Binding : filters.BindingAt(value.MaskContentIndex);
                        SvgPaintBounds filtered = FilterRegion(binding, filters.ResourceAt(binding.ResourceIndex), boundsWork,
                            SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset));
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
                SvgRecordedPaintValue paint = GetRecordedPaintValue(geometry.RecordedPaint);
                if (paint.IsEffectAnchor) continue;
                SvgPaintBounds? visualBounds = paint.VisualBounds;
                if (visualBounds is {} visual && (sourcePlacement == SvgAffineTransform.Identity || geometry.AuthoredPath is null))
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
                            // Device fitting can extend filled ink beyond its source outline.
                            // Keep that fringe inside the bounded layer, as filtered glyphs do.
                            double inflation = Math.Max(2, geometry.Glyph.StrokeBoundsInflation);
                            IncludeBox(new(segment.Start.X, segment.Start.Y, 0, 0, default, false, null), outputMap, inflation);
                            IncludeBox(new(segment.Control.X, segment.Control.Y, 0, 0, default, false, null), outputMap, inflation);
                            IncludeBox(new(segment.End.X, segment.End.Y, 0, 0, default, false, null), outputMap, inflation);
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
                    SvgClipBox? placedRegion = command.MaskRegion;
                    if (placedRegion is {} retained && sourcePlacement != SvgAffineTransform.Identity)
                    {
                        ParserWorkCounter regionWork = new(options.Limits.MaximumWorkUnits - workUnits);
                        SvgPaintBounds placed = new SvgPaintBounds((float)retained.X, (float)retained.Y, (float)retained.Right, (float)retained.Bottom)
                            .Map(sourcePlacement, regionWork, cancellationToken);
                        workUnits += regionWork.Used;
                        placedRegion = retained with { X = placed.Left, Y = placed.Top, Width = Math.Max(0, placed.Right - placed.Left), Height = Math.Max(0, placed.Bottom - placed.Top) };
                    }
                    (double regionLeft, double regionTop, double regionRight, double regionBottom) = placedRegion is SvgClipBox maskRegion ? MapClipBounds(maskRegion, horizontalScale, horizontalOffset, verticalScale, verticalOffset) : (0, 0, outputWidth, outputHeight);
                    bool affineMaskTarget = command.MaskContentIndex >= 0 && root.SceneStorage is {} maskStorage
                        && (uint)command.MaskContentIndex < (uint)maskStorage.MaskContents.Length && maskStorage.MaskContents[command.MaskContentIndex].UsesAffineTarget;
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
                        using SvgCpuClipCoverage? clipCoverage = RasterizeMaskContent(clipContent, clipPixels, true, clipContent.ClipKind is SvgClipContentKind.AffineViewport or SvgClipContentKind.BinaryViewport or SvgClipContentKind.BinaryExclusion ? layer : previousLayer);
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
                        activeViewportIsBinary = (clipContent.ClipKind is SvgClipContentKind.BinaryViewport or SvgClipContentKind.BinaryExclusion) && previousBinary;
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
                    if (command.RegionKind == SvgLayerRegionKind.Mask && placedRegion is {} paintRegion)
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
                        // An affine mask target keeps the native AA paint consumer even under a fully white mask.
                        maskClipAntialiased = affineMaskTarget || SvgAnalyticRectangleClip.Classify(first.X, first.Y, last.X, last.Y, previousLayer, SvgClipAntialiasing.All) == SvgAnalyticPaintClip.Antialiased;
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
                        if (surfaceMode == RasterSurfaceMode.PremultipliedPattern)
                        {
                            // Masks modify the isolated surface before the picture-backed
                            // tile applies outer opacity through packed shader coverage.
                            SvgConstantShaderPaint sample = new(MultiplyAlpha(groupPixels[offset], maskAlpha), MultiplyAlpha(groupPixels[offset + 1], maskAlpha),
                                MultiplyAlpha(groupPixels[offset + 2], maskAlpha), MultiplyAlpha(groupPixels[offset + 3], maskAlpha));
                            sample.Blend(destination.AsSpan(offset, 4), MultiplyAlpha(extra, clipAlpha));
                        }
                        else if (shaderRestore || parentViewportClip.Length != 0 && !activeViewportIsBinary) BlendClippedLayerPixel(destination.AsSpan(offset, 4), groupPixels.AsSpan(offset, 4), extra, maskAlpha, clipAlpha);
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
            bool previousLuminanceLayer = maskLuminanceLayer;
            maskLuminanceLayer = content.UsesAffineTarget && !content.IsAlpha;
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
                else if (content.ClipKind is SvgClipContentKind.BinaryViewport or SvgClipContentKind.BinaryExclusion)
                {
                    if (content.Geometries.Length != 1 || content.Geometries[0].AuthoredPath is not {} viewportPath || coverage is null)
                        throw new SvgException(SvgErrorCode.MalformedInput, "A binary viewport requires retained rectangle geometry.");
                    SvgAffineTransform clipMap = SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset);
                    if (sourcePlacement != SvgAffineTransform.Identity) clipMap = clipMap.ComposeChecked(sourcePlacement);
                    workUnits += SvgBinaryViewportCoverage.Rasterize(viewportPath,
                        clipMap, rasterGrid,
                        contentBounds, destination, outputWidth, coverage, content.Geometries[0].Polygon.IsEvenOdd, content.ClipKind == SvgClipContentKind.BinaryExclusion, budget, options.Limits.MaximumWorkUnits - workUnits, cancellationToken);
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
                maskLuminanceLayer = previousLuminanceLayer;
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
                    SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset).ComposeChecked(sourcePlacement), ResolveSolidColor(stroke.Color), surfaceMode == RasterSurfaceMode.PremultipliedPattern ? PatternSolidPaint(stroke.Color) : null,
                    stroke.Clip is SvgClipBox clip ? MapClipBounds(clip, horizontalScale, horizontalOffset, verticalScale, verticalOffset) : null,
                    stroke.Clip?.Antialiasing ?? SvgClipAntialiasing.None, activeViewportClip, activeViewportIsBinary, destination, outputWidth, outputHeight, hairlineWork, cancellationToken);
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
            // The transparent source border clips painting without moving the native
            // bitmap's shader seed. Retain that bitmap origin even in a compact capture.
            SvgCpuBitmapOrigin? capturedImageOrigin = frame is { ImageShaderOrigin: {} nativeOrigin, ContentClip: {} content } sourceFrame && content == layerBounds
                ? new(nativeOrigin.X - sourceFrame.CaptureOrigin.X, nativeOrigin.Y - sourceFrame.CaptureOrigin.Y) : null;
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
                    SvgCpuBitmapOrigin origin = capturedImageOrigin ?? ShaderBitmapOrigin(x, y);
                    if (authoredPath is not null || surfaceMode == RasterSurfaceMode.PremultipliedPattern)
                    {
                        if (!coordinates.TryGetMatrix(origin, out SvgShaderMatrix affineInverse))
                            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Embedded image coordinates are not finite and invertible.");
                        float pixelX = x - origin.X + .5f, pixelY = y - origin.Y + .5f;
                        // Low-precision pipeline matrix stages round products separately.
                        float sampleX = pixelX * affineInverse.ScaleX + (pixelY * affineInverse.SkewX + affineInverse.TranslateX);
                        float sampleY = pixelX * affineInverse.SkewY + (pixelY * affineInverse.ScaleY + affineInverse.TranslateY);
                        if (surfaceMode == RasterSurfaceMode.PremultipliedPattern)
                        {
                            (sampleX, sampleY) = affineInverse.Map(pixelX, pixelY);
                            SamplePicture(sampleX, sampleY).Blend(destination.AsSpan((y * outputWidth + x) * 4, 4), coverage);
                        }
                        else SampleAffine(sampleX, sampleY).Blend(destination.AsSpan((y * outputWidth + x) * 4, 4), coverage);
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
                    sample.BlendBitmap(destination.AsSpan((y * outputWidth + x) * 4, 4), coverage);
                    fixedX += stepX;
                }
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

            // Picture-backed image draws retain interpolated premultiplied lanes and
            // paint alpha until the existing floating shader store. Packed document
            // bitmap and affine low-precision consumers keep their native routes.
            SvgShaderPaint SamplePicture(float x, float y)
            {
                float alpha = ShaderAlpha(image.Opacity);
                if (image.UniformSample is {} uniform)
                    return SvgShaderPaint.Pattern(uniform.Red, uniform.Green, uniform.Blue, uniform.Alpha, alpha);
                x = Math.Clamp(x, -1, image.Width) - .5f;
                y = Math.Clamp(y, -1, image.Height) - .5f;
                int left = (int)MathF.Floor(x), top = (int)MathF.Floor(y);
                float fx = x - left, fy = y - top;
                int a = (Math.Clamp(top, 0, image.Height - 1) * image.Width + Math.Clamp(left, 0, image.Width - 1)) * 4;
                int b = (Math.Clamp(top, 0, image.Height - 1) * image.Width + Math.Clamp(left + 1, 0, image.Width - 1)) * 4;
                int c = (Math.Clamp(top + 1, 0, image.Height - 1) * image.Width + Math.Clamp(left, 0, image.Width - 1)) * 4;
                int d = (Math.Clamp(top + 1, 0, image.Height - 1) * image.Width + Math.Clamp(left + 1, 0, image.Width - 1)) * 4;
                return SvgShaderPaint.InterpolatePremultiplied(
                    new(source[a], source[a + 1], source[a + 2], source[a + 3]),
                    new(source[b], source[b + 1], source[b + 2], source[b + 3]),
                    new(source[c], source[c + 1], source[c + 2], source[c + 3]),
                    new(source[d], source[d + 1], source[d + 2], source[d + 3]), fx, fy, alpha);
            }

            // Pinned raster-pipeline bilerp_clamp_8888 retains Q15 intermediate
            // fractions; the separate axis-aligned bitmap route retains four bits.
            // Copyright 2016 Google Inc.; 2018, 2022 Google LLC.
            // See THIRD_PARTY_NOTICES.txt for the BSD license and source pin.
            SvgConstantShaderPaint SampleAffine(float x, float y)
            {
                if (image.UniformSample is {} uniform) return ApplyOpacity(uniform, true);
                return ApplyOpacity(SvgConstantShaderPaint.SampleAffine(source, image.Width, image.Height, x, y, false), true);
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
                if (coordinateSpace == PathCoordinateSpace.User && sourcePlacement != SvgAffineTransform.Identity)
                    outputMap = outputMap.ComposeChecked(sourcePlacement);
                authoredMap = frame?.NativeCapturePlacement is {} nativeCapture
                    ? nativeCapture.ComposeNative(replay.SceneMap) : outputMap.ComposeChecked(replay.SceneMap);
                if (authoredPath?.StrokeStyle?.NonScalingMap is {} nonScaling)
                {
                    preparationWork.Consume(cancellationToken);
                    if (!nonScaling.StrokeTransform.TryInvert(out SvgAffineTransform inverse))
                        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Non-scaling stroke transform must be invertible.");
                    authoredMap = outputMap.ComposeNative(nonScaling.SourceMap).ComposeNative(inverse);
                }
                if (frame is { ParentOrigin: {} parent, NativeCapturePlacement: null } sourceFrame)
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
                return new([], 0, 0, 0, 0, 0, 0, 0, 0, null, SvgClipAntialiasing.None, true, default, [], [], null, [], null, null, SvgAffineTransform.Identity, mapping);
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
                    return new MappedPolygon(mapped, startX, startY, endX, endY, pathStartX, pathStartY, pathEndX, pathEndY, mappedClip, clip?.Antialiasing ?? SvgClipAntialiasing.None, clipContainsPath, completeBounds, mappedSpans, mappedCubics, mappedReservation, contourEnds, signs, authored, authoredMap, mapping);
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
            if (surfaceMode == RasterSurfaceMode.PremultipliedFilter && fontPreblend.IsEmpty
                && authoredPath is { IsKnownConvex: true, StrokeStyle: null } rectangleSource
                && rectangleSource.Commands.Length == 5 && raster.AuthoredMap.IsAxisAligned
                && raster.QuadSpans.Length == 0 && raster.CubicSpans.Length == 0)
            {
                // A native rectangle can become axis-aligned again in a filter's
                // source bitmap. Preserve its direct rectangle coverage there.
                long rectangleWork = 4L + (long)(raster.EndX - raster.StartX) * (raster.EndY - raster.StartY);
                if (rectangleWork > options.Limits.MaximumWorkUnits - workUnits)
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Retained rectangle raster work exceeds the configured limit.");
                workUnits += rectangleWork;
                SvgRectangleCoverage coverage = SvgRectangleCoverage.Create(raster.CompleteBounds.Left, raster.CompleteBounds.Top,
                    raster.CompleteBounds.Right, raster.CompleteBounds.Bottom, outputWidth, outputHeight);
                SvgClipBox? clip = raster.Clip is {} edges ? new SvgClipBox(edges.Left, edges.Top, edges.Right - edges.Left, edges.Bottom - edges.Top) { Antialiasing = raster.ClipAntialiasing } : null;
                RasterizePreparedRectangle(new(raster.StartX, raster.StartY, raster.EndX, raster.EndY, coverage, clip), glyphPaint, destination);
                return;
            }
            SvgAnalyticPaint analyticPaint = fontPreblend.Length == 0 ? glyphPaint : SvgAnalyticPaint.Solid(new SvgColor(0, 0, 0, 255));
            // Luminance save-layers preserve coverage-pair arithmetic through the binary source-region clip.
            if (activeViewportClip.Length != 0 && activeViewportIsBinary && !maskLuminanceLayer) analyticPaint = analyticPaint.ThroughBinaryRegion();
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
                SvgAnalyticClipBounds paintArea = collectingClipCoverage is null ? context.Interior : context.BitmapClip;
                using PolygonRaster traversal = new(ref preparation, raster, context, ActiveClipBounds(context),
                    PolygonPaintClip(raster, context), paintArea, collectingClipCoverage is not null, budget,
                    ref workUnits, options.Limits.MaximumWorkUnits, cancellationToken);
                bool useAnalytic = traversal.IsAnalytic;
                int startY = traversal.StartY, endY = traversal.EndY;
                int clipLeft = context.BitmapClip.Right, clipTop = context.BitmapClip.Bottom, clipRight = context.BitmapClip.Left, clipBottom = context.BitmapClip.Top;
                for (int y = startY; y < endY; y++)
                {
                    traversal.PrepareRow(y, ref workUnits, options.Limits.MaximumWorkUnits, cancellationToken);
                    SvgAnalyticScanWindow paintWindow = traversal.GetPaintWindow(ref workUnits, options.Limits.MaximumWorkUnits);
                    int rowStartX = paintWindow.Left, rowEndX = paintWindow.Right;
                    for (int x = rowStartX; x < rowEndX; x++)
                    {
                        if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                        double pixelCoverage = !useAnalytic && traversal.MightCover(x) ? PixelCoverage(raster, contourEnds, nestingSigns, polygon.IsEvenOdd, raster.Signs, x, y, cancellationToken) : 0.0;
                        int offset = (y * outputWidth + x) * 4;
                        if (fontPreblend.Length != 0)
                        {
                            clipPixel.Clear();
                            if (useAnalytic) traversal.PaintPixel(x, clipPixel, analyticPaint, SvgAnalyticClipCoverage.Full);
                            else clipPixel[3] = CoverageAlpha(255, pixelCoverage);
                            byte alpha = PolygonClipCoverage(raster, x, y).Apply(fontPreblend[clipPixel[3]]);
                            glyphPaint.Blend(destination.AsSpan(offset, 4), alpha, SvgAnalyticPaintKind.Mask);
                        }
                        else if (useAnalytic && collectingClipCoverage is not null)
                        {
                            clipPixel.Clear();
                            traversal.PaintPixel(x, clipPixel, analyticPaint, PolygonClipCoverage(raster, x, y));
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
                        else if (useAnalytic) traversal.PaintPixel(x, destination.AsSpan(offset, 4), analyticPaint, PolygonClipCoverage(raster, x, y));
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
            SvgShaderCoordinates nativeCoordinates = SvgShaderCoordinates.CreateLinear(definition, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset, null, sourcePlacement);
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
                SvgAnalyticClipBounds paintArea = context.Interior;
                using PolygonRaster traversal = new(ref preparation, raster, context, ActiveClipBounds(context),
                    PolygonPaintClip(raster, context), paintArea, false, budget,
                    ref workUnits, options.Limits.MaximumWorkUnits, cancellationToken);
                bool useAnalytic = traversal.IsAnalytic;
                int startY = traversal.StartY, endY = traversal.EndY;
                if (denominator != 0 || definition.Stops.Length == 1)
                for (int y = startY; y < endY; y++)
                {
                traversal.PrepareRow(y, ref workUnits, options.Limits.MaximumWorkUnits, cancellationToken);
                double deviceY = (y + 0.5 - verticalOffset) / verticalScale;
                SvgAnalyticScanWindow paintWindow = traversal.GetPaintWindow(ref workUnits, options.Limits.MaximumWorkUnits);
                int rowStartX = paintWindow.Left, rowEndX = paintWindow.Right;
                for (int x = rowStartX; x < rowEndX; x++)
                {
                    if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                    double pixelCoverage = !useAnalytic && traversal.MightCover(x) ? PixelCoverage(raster, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, x, y, cancellationToken) : 0.0;
                    double deviceX = (x + 0.5 - horizontalOffset) / horizontalScale;
                    SvgDevicePoint sourcePoint = SceneShaderPoint(deviceX, deviceY);
                    deviceX = sourcePoint.X;
                    double sourceY = sourcePoint.Y;
                    double position;
                    if (!nativeCoordinates.TryLinear(x, y, ShaderBitmapOrigin(x, y), out position))
                    {
                        if (definition.SourcePaint is { Data.IsMapped: true } sourcePaint) position = sourcePaint.Position(deviceX, sourceY);
                        else if (definition.IsUserSpace && Math.Abs(definition.ReferenceScaleX) != Math.Abs(definition.ReferenceScaleY))
                        {
                            double relativeX = (deviceX - definition.X1) / definition.ReferenceScaleX;
                            double relativeY = (sourceY - definition.Y1) / definition.ReferenceScaleY;
                            double axisX = directionX / definition.ReferenceScaleX;
                            double axisY = directionY / definition.ReferenceScaleY;
                            position = (relativeX * axisX + relativeY * axisY) / (axisX * axisX + axisY * axisY);
                        }
                        else if (definition.IsUserSpace) position = ((deviceX - definition.X1) * directionX + (sourceY - definition.Y1) * directionY) / denominator;
                        else
                        {
                            double normalizedX = (deviceX - box.X) / box.Width;
                            double normalizedY = (sourceY - box.Y) / box.Height;
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
                    if (useAnalytic) traversal.PaintPixel(x, destination.AsSpan(offset, 4), SvgAnalyticPaint.Shader(sample), PolygonClipCoverage(raster, x, y));
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
            SvgShaderCoordinates nativeCoordinates = SvgShaderCoordinates.CreateRadial(definition, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset, sourcePlacement);
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
                SvgAnalyticClipBounds paintArea = context.Interior;
                using PolygonRaster traversal = new(ref preparation, raster, context, ActiveClipBounds(context),
                    PolygonPaintClip(raster, context), paintArea, false, budget,
                    ref workUnits, options.Limits.MaximumWorkUnits, cancellationToken);
                bool useAnalytic = traversal.IsAnalytic;
                int startY = traversal.StartY, endY = traversal.EndY;
                for (int y = startY; y < endY; y++)
                {
                traversal.PrepareRow(y, ref workUnits, options.Limits.MaximumWorkUnits, cancellationToken);
                double deviceY = (y + 0.5 - verticalOffset) / verticalScale;
                SvgAnalyticScanWindow paintWindow = traversal.GetPaintWindow(ref workUnits, options.Limits.MaximumWorkUnits);
                int rowStartX = paintWindow.Left, rowEndX = paintWindow.Right;
                for (int x = rowStartX; x < rowEndX; x++)
                {
                    if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                    double pixelCoverage = !useAnalytic && traversal.MightCover(x) ? PixelCoverage(raster, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, x, y, cancellationToken) : 0.0;
                    double deviceX = (x + 0.5 - horizontalOffset) / horizontalScale;
                    SvgDevicePoint sourcePoint = SceneShaderPoint(deviceX, deviceY);
                    deviceX = sourcePoint.X;
                    double sourceY = sourcePoint.Y;
                    double sampleX;
                    double sampleY;
                    double focalX;
                    double focalY;
                    double circleX;
                    double circleY;
                    if (definition.IsUserSpace)
                    {
                        sampleX = deviceX;
                        sampleY = sourceY;
                        if (definition.HasGeneralTransform) { (sampleX, sampleY) = definition.Inverse.Apply(sampleX, sampleY); }
                        focalX = definition.FocalX;
                        focalY = definition.FocalY;
                        circleX = definition.CenterX;
                        circleY = definition.CenterY;
                    }
                    else
                    {
                        sampleX = (deviceX - box.X) / box.Width;
                        sampleY = (sourceY - box.Y) / box.Height;
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
                    else position = SvgShaderCoordinates.EvaluateRadial(new(rayX, rayY), new(offsetX, offsetY), radius, focalRadius);
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
                    if (useAnalytic) traversal.PaintPixel(x, destination.AsSpan(offset, 4), SvgAnalyticPaint.Shader(sample), PolygonClipCoverage(raster, x, y));
                    else sample.Blend(destination.AsSpan(offset, 4), ViewportAlpha(CoverageAlpha(255, pixelCoverage), x, y));
                }
                }
                // Structural fallback belongs to the original fill and runs once.
                if (!useAnalytic) break;
            }
        }

        void RasterizePatternPolygon(SvgPatternPolygon outline, SvgAuthoredPath? authoredPath, byte[] destination)
        {
            if (!outline.HasFill || outline.Points.Length < 3 || outline.Coordinates?.IsEmpty == true) return;
            using MappedPolygon raster = MapPolygon(outline.Points, outline.ContourEnds, outline.Signs, outline.IsEvenOdd, outline.QuadSpans, outline.CubicSpans, outline.Mapping, outline.Clip, authoredPath, PathCoordinateSpace.User);
            if (raster.EndY <= raster.StartY || raster.EndX <= raster.StartX) return;
            using RenderBudget.BudgetReservation sourcePatternOwner = budget.Reserve(512, SvgMemoryCategory.Geometry);
            SvgPatternGeometry source = new(outline.Content, outline.OriginX, outline.OriginY, outline.TileWidth, outline.TileHeight, default, outline.Opacity, null) { ContentPaint = outline.ContentPaint, VectorContent = outline.VectorContent, ContentViewBox = outline.ContentViewBox, Coordinates = outline.Coordinates, PaintMatrices = outline.PaintMatrices, ShaderPlacement = outline.ShaderPlacement };
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
                SvgAnalyticClipBounds paintArea = context.Interior;
                using PolygonRaster traversal = new(ref preparation, raster, context, ActiveClipBounds(context),
                    PolygonPaintClip(raster, context), paintArea, false, budget,
                    ref workUnits, options.Limits.MaximumWorkUnits, cancellationToken);
                bool useAnalytic = traversal.IsAnalytic;
                int startY = traversal.StartY, endY = traversal.EndY;
                for (int y = startY; y < endY; y++)
                {
                traversal.PrepareRow(y, ref workUnits, options.Limits.MaximumWorkUnits, cancellationToken);
                SvgAnalyticScanWindow paintWindow = traversal.GetPaintWindow(ref workUnits, options.Limits.MaximumWorkUnits);
                int rowStartX = paintWindow.Left, rowEndX = paintWindow.Right;
                for (int x = rowStartX; x < rowEndX; x++)
                {
                    if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                    double pixelCoverage = !useAnalytic && traversal.MightCover(x) ? PixelCoverage(raster, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, x, y, cancellationToken) : 0.0;
                    int offset = (y * outputWidth + x) * 4;
                    SvgShaderPaint sample = SamplePattern(tile, blitted, x, y, ref imageCoordinates);
                    if (useAnalytic) traversal.PaintPixel(x, destination.AsSpan(offset, 4), SvgAnalyticPaint.Shader(sample), PolygonClipCoverage(raster, x, y));
                    else sample.Blend(destination.AsSpan(offset, 4), ViewportAlpha(CoverageAlpha(255, pixelCoverage), x, y));
                }
                }
                // Structural fallback belongs to the original fill and runs once.
                if (!useAnalytic) break;
            }
        }

        bool NeedsPatternMapping(SvgPatternGeometry pattern)
            => pattern.Coordinates is not null || pattern.VectorContent is not null || horizontalScale != 1 || verticalScale != 1 || horizontalOffset != 0 || verticalOffset != 0 || pattern.ShaderPlacement != SvgAffineTransform.Identity;

        bool TryMapPatternTile(SvgPatternGeometry pattern, out SvgPatternGeometry? mapped)
        {
            mapped = null;
            if (pattern.Coordinates?.IsEmpty == true) return true;
            SvgPatternMappedCoordinates coordinates = pattern.Coordinates is {} units
                ? units.Resolve(pattern.OriginX, pattern.OriginY, pattern.TileWidth, pattern.TileHeight, pattern.ShaderPlacement, pattern.ContentViewBox)
                : new(pattern.OriginX, pattern.OriginY, pattern.TileWidth, pattern.TileHeight, pattern.ShaderPlacement, pattern.ContentViewBox);
            SvgAffineTransform shader = coordinates.Placement;
            if (horizontalScale == 1 && verticalScale == 1 && horizontalOffset == 0 && verticalOffset == 0 && shader == SvgAffineTransform.Identity && pattern.VectorContent is null) return true;
            bool positiveAxis = shader.IsAxisAligned && shader.A > 0 && shader.D > 0;
            if (pattern.VectorContent is null && !positiveAxis) return false;
            double scaleX = shader.A * horizontalScale, scaleY = shader.D * verticalScale;
            if (pattern.Content.Stroke is not null && (scaleX != 1 || scaleY != 1)) return false;
            double originX = (coordinates.OriginX * shader.A + shader.E) * horizontalScale + horizontalOffset;
            double originY = (coordinates.OriginY * shader.D + shader.F) * verticalScale + verticalOffset;
            double tileWidth = coordinates.Width * scaleX, tileHeight = coordinates.Height * scaleY;
            if (pattern.VectorContent is not null)
            {
                float width = (float)coordinates.Width, height = (float)coordinates.Height;
                SvgShaderMatrix outputMap = new((float)horizontalScale, 0, (float)horizontalOffset, 0, (float)verticalScale, (float)verticalOffset);
                SvgShaderMatrix localMap = new((float)shader.A, (float)shader.C, (float)shader.E, (float)shader.B, (float)shader.D, (float)shader.F);
                if (sourcePlacement != SvgAffineTransform.Identity)
                    outputMap = SvgShaderMatrix.Concat(outputMap, new((float)sourcePlacement.A, (float)sourcePlacement.C, (float)sourcePlacement.E, (float)sourcePlacement.B, (float)sourcePlacement.D, (float)sourcePlacement.F));
                SvgShaderMatrix drawMap = outputMap;
                if (pattern.PaintMatrices is {} stages)
                {
                    SvgAffineTransform target = stages.Target, local = stages.Pattern;
                    drawMap = SvgShaderMatrix.Concat(outputMap, new((float)target.A, (float)target.C, (float)target.E, (float)target.B, (float)target.D, (float)target.F));
                    localMap = new((float)local.A, (float)local.C, (float)local.E, (float)local.B, (float)local.D, (float)local.F);
                }
                SvgShaderMatrix map = SvgShaderMatrix.Concat(drawMap, localMap);
                // Picture tiles retain their source axes. Rotation/reflection change the
                // sampling matrix, while column lengths choose the bounded texture scale.
                (float rasterScaleX, float rasterScaleY) = positiveAxis
                    ? (map.ScaleX, map.ScaleY) : map.GetPictureScale();
                double pixelsX = MathF.Ceiling(width * rasterScaleX), pixelsY = MathF.Ceiling(height * rasterScaleY);
                if (pixelsX <= 0 || pixelsY <= 0 || pixelsX > options.Limits.MaximumDimension || pixelsY > options.Limits.MaximumDimension)
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern tile dimensions exceed the configured limit.");
                float tileScaleX = (float)pixelsX / width, tileScaleY = (float)pixelsY / height;
                SvgShaderMatrix imageLocal = SvgShaderMatrix.Concat(localMap, new(1f / tileScaleX, 0, (float)coordinates.OriginX, 0, 1f / tileScaleY, (float)coordinates.OriginY));
                SvgShaderMatrix imageMap = SvgShaderMatrix.Concat(drawMap, imageLocal);
                if (!imageMap.TryInvert(out _)) return false;
                mapped = new(default, 0, 0, pixelsX, pixelsY, pattern.Fill, pattern.Opacity, pattern.Stroke)
                {
                    VectorContent = pattern.VectorContent, ContentViewBox = coordinates.ContentViewBox,
                    ShaderPlacement = SvgAffineTransform.FromScaleOffset(1, 1, originX, originY),
                    RasterSourceBox = new(0, 0, width, height, SvgViewBoxAlignment.None, SvgViewBoxAlignment.None, SvgViewBoxFit.None),
                    NativeImageMap = positiveAxis && IsDeviceInteger(tileWidth) && IsDeviceInteger(tileHeight) ? null : drawMap,
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
            if ((!pattern.Fill.HasFill && pattern.Stroke is null) || pattern.Coordinates?.IsEmpty == true) return;
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
            => SamplePatternWithOpacity(pattern, tile, x, y, ref imageCoordinates, 255);

        SvgShaderPaint SamplePatternWithOpacity(SvgPatternGeometry pattern, byte[] tile, int x, int y, ref SvgShaderCoordinates imageCoordinates, byte opacity)
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
            float alpha = ShaderAlpha(MultiplyAlpha(pattern.Opacity, opacity));
            if (fx == 0 && fy == 0)
            {
                int offset = (tileY * pattern.TextureWidth + tileX) * 4;
                return SvgShaderPaint.Pattern(tile[offset], tile[offset + 1], tile[offset + 2], tile[offset + 3], alpha);
            }
            if (4 > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern filter work exceeds the configured limit.");
            workUnits += 4;
            int nextX = tileX + 1 == pattern.TextureWidth ? 0 : tileX + 1;
            int nextY = tileY + 1 == pattern.TextureHeight ? 0 : tileY + 1;
            int a = (tileY * pattern.TextureWidth + tileX) * 4, b = (tileY * pattern.TextureWidth + nextX) * 4;
            int c = (nextY * pattern.TextureWidth + tileX) * 4, d = (nextY * pattern.TextureWidth + nextX) * 4;
            return SvgShaderPaint.InterpolatePremultiplied(
                new(tile[a], tile[a + 1], tile[a + 2], tile[a + 3]),
                new(tile[b], tile[b + 1], tile[b + 2], tile[b + 3]),
                new(tile[c], tile[c + 1], tile[c + 2], tile[c + 3]),
                new(tile[d], tile[d + 1], tile[d + 2], tile[d + 3]), fx, fy, alpha);
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
            SvgShaderCoordinates nativeCoordinates = SvgShaderCoordinates.CreateLinear(definition, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset, null, sourcePlacement);
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
                SvgDevicePoint sourcePoint = SceneShaderPoint(deviceX, deviceY);
                deviceX = sourcePoint.X;
                double sourceY = sourcePoint.Y;
                double position;
                if (!nativeCoordinates.TryLinear(x, y, ShaderBitmapOrigin(x, y), out position))
                {
                    if (definition.SourcePaint is { Data.IsMapped: true } sourcePaint) position = sourcePaint.Position(deviceX, sourceY);
                    else if (definition.IsUserSpace && Math.Abs(definition.ReferenceScaleX) != Math.Abs(definition.ReferenceScaleY))
                    {
                        double relativeX = (deviceX - definition.X1) / definition.ReferenceScaleX;
                        double relativeY = (sourceY - definition.Y1) / definition.ReferenceScaleY;
                        double axisX = directionX / definition.ReferenceScaleX;
                        double axisY = directionY / definition.ReferenceScaleY;
                        position = (relativeX * axisX + relativeY * axisY) / (axisX * axisX + axisY * axisY);
                    }
                    else if (definition.IsUserSpace) position = ((deviceX - definition.X1) * directionX + (sourceY - definition.Y1) * directionY) / denominator;
                    else
                    {
                        double normalizedX = (deviceX - box.X) / box.Width;
                        double normalizedY = (sourceY - box.Y) / box.Height;
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
            SvgShaderCoordinates nativeCoordinates = SvgShaderCoordinates.CreateRadial(definition, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset, sourcePlacement);
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
                SvgDevicePoint sourcePoint = SceneShaderPoint(deviceX, deviceY);
                deviceX = sourcePoint.X;
                double sourceY = sourcePoint.Y;
                double sampleX;
                double sampleY;
                double focalX;
                double focalY;
                double circleX;
                double circleY;
                if (definition.IsUserSpace)
                {
                    sampleX = deviceX;
                    sampleY = sourceY;
                    if (definition.HasGeneralTransform) { (sampleX, sampleY) = definition.Inverse.Apply(sampleX, sampleY); }
                    focalX = definition.FocalX;
                    focalY = definition.FocalY;
                    circleX = definition.CenterX;
                    circleY = definition.CenterY;
                }
                else
                {
                    sampleX = (deviceX - box.X) / box.Width;
                    sampleY = (sourceY - box.Y) / box.Height;
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
                if (offsetX != 0 || offsetY != 0 || !nativeCoordinates.TryRadial(x, y, ShaderBitmapOrigin(x, y), out position))
                    position = SvgShaderCoordinates.EvaluateRadial(new(rayX, rayY), new(offsetX, offsetY), radius, focalRadius);
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
            using SvgGlyphRasterSource? placedSource = sourcePlacement == SvgAffineTransform.Identity ? null : glyph.Source?.Place(sourcePlacement, budget);
            SvgGlyphRasterSource? glyphSource = placedSource ?? glyph.Source;
            SvgLinearGradient? glyphLinear = glyph.Shader is SvgTextLinearShader linearPaint ? linearPaint.Gradient : null;
            SvgRadialGradient? glyphRadial = glyph.Shader is SvgTextRadialShader radialPaint ? radialPaint.Gradient : null;
            if (glyphLinear?.IsEmpty == true || glyphRadial?.IsEmpty == true) return;
            SvgPatternGeometry? glyphPattern = null;
            SvgPatternGeometry? originalPattern = glyph.Shader is SvgTextPatternShader patternPaint ? patternPaint.Pattern : null;
            if (originalPattern is not null)
            {
                if (!TryMapPatternTile(originalPattern, out SvgPatternGeometry? mapped))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text pattern coordinates are not finite and invertible.");
                if (originalPattern.Coordinates?.IsEmpty == true) return;
                glyphPattern = mapped ?? originalPattern;
            }
            using SvgPatternTileLease glyphTile = originalPattern is null || glyphPattern is null ? default : BorrowPatternTile(originalPattern, glyphPattern, true);
            using SvgGradientRamp glyphRamp = glyphLinear is {} linearRamp
                ? PrepareGradientRamp(linearRamp.Stops, linearRamp.Start, linearRamp.End, linearRamp.Offset1, linearRamp.Offset2)
                : glyphRadial is {} radialRamp ? PrepareGradientRamp(radialRamp.Stops, radialRamp.Start, radialRamp.End, radialRamp.Offset1, radialRamp.Offset2) : default;
            SvgGradientPaintMatrices? textMatrices = null;
            if (glyphLinear?.SourcePaint?.Data is {} textSource && glyph.Source is { GlyphMap: var glyphMap } && glyphMap != SvgAffineTransform.Identity)
            {
                if (!glyphMap.TryInvert(out SvgAffineTransform inverse)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text shader placement must be invertible.");
                SvgAffineTransform local = inverse.ComposeChecked(textSource.Referencing).ComposeChecked(new(textSource.Local.A, textSource.Local.B, textSource.Local.C, textSource.Local.D, textSource.Local.E, textSource.Local.F));
                textMatrices = new(new((float)local.A, (float)local.C, (float)local.E, (float)local.B, (float)local.D, (float)local.F), glyphMap);
            }
            if (glyphRadial is { NativePaintMatrices: {} radialMatrices } radial && glyph.Source is { GlyphMap: var radialGlyphMap } && radialGlyphMap != SvgAffineTransform.Identity)
            {
                if (!radialGlyphMap.TryInvert(out SvgAffineTransform inverse)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text shader placement must be invertible.");
                SvgShaderMatrix matrix = radialMatrices.Local;
                SvgAffineTransform local = inverse.ComposeChecked(radialMatrices.Referencing).ComposeChecked(new(matrix.ScaleX, matrix.SkewY, matrix.SkewX, matrix.ScaleY, matrix.TranslateX, matrix.TranslateY));
                glyphRadial = radial with { NativePaintMatrices = new(new((float)local.A, (float)local.C, (float)local.E, (float)local.B, (float)local.D, (float)local.F), radialGlyphMap) };
            }
            SvgShaderCoordinates glyphCoordinates = glyphLinear is {} linearCoordinates
                ? SvgShaderCoordinates.CreateLinear(linearCoordinates, default, horizontalScale, verticalScale, horizontalOffset, verticalOffset, textMatrices, sourcePlacement)
                : glyphRadial is {} radialCoordinates ? SvgShaderCoordinates.CreateRadial(radialCoordinates, default, horizontalScale, verticalScale, horizontalOffset, verticalOffset, sourcePlacement)
                : glyphPattern?.NativeImageMap is {} imageMap ? SvgShaderCoordinates.CreateImage(imageMap, glyphPattern.NativeImageLocal ?? throw new InvalidOperationException("Text image sampling requires its local matrix.")) : default;
            if (glyphSource is SvgGlyphRasterSource source && (OperatingSystem.IsLinux() || OperatingSystem.IsWindows()))
            {
                ParserWorkCounter preparationWork = new(options.Limits.MaximumWorkUnits - workUnits);
                if (glyph.StrokePaint is {} strokePaint)
                {
                    using SvgAuthoredPath path = source.CreateStrokePath(strokePaint, horizontalScale, horizontalOffset, verticalScale, verticalOffset, budget, preparationWork, cancellationToken);
                    Span<byte> preblend = stackalloc byte[256];
                    SvgColor color = GlyphLuminance(preparationWork);
                    if (OperatingSystem.IsWindows()) SvgFontMaskPreblend.BuildWindowsGrayTable(preblend, color, preparationWork, cancellationToken);
                    else SvgFontMaskPreblend.BuildLinuxGrayTable(preblend, color, preparationWork, cancellationToken);
                    RasterizeMappedBitmap(path, preblend, preparationWork);
                    return;
                }
                if (source.HasGlyphMap && OperatingSystem.IsLinux())
                {
                    using SvgAuthoredPath path = source.CreateMappedPath(horizontalScale, horizontalOffset, verticalScale, verticalOffset, budget, preparationWork, cancellationToken);
                    Span<byte> preblend = stackalloc byte[256];
                    SvgColor color = GlyphLuminance(preparationWork);
                    SvgFontMaskPreblend.BuildLinuxGrayTable(preblend, color, preparationWork, cancellationToken);
                    RasterizeMappedBitmap(path, preblend, preparationWork);
                    return;
                }
                if (source.HasGlyphMap || source.UsesDeviceFitting(budget, preparationWork, cancellationToken))
                {
                    if (OperatingSystem.IsWindows() && (source.HasGlyphMap || source.HasCompleteTrueTypeProfile || source.HasTrueTypePrograms(budget, preparationWork, cancellationToken)))
                    {
                        using OpenTypeGlyphContourStorage contours = source.CreateWindowsContours(horizontalScale, horizontalOffset, verticalScale, verticalOffset, budget, preparationWork, cancellationToken);
                        int crossingCount = checked(contours.Segments.Length * 2);
                        using RenderBudget.BudgetReservation? crossingReservation = crossingCount == 0 ? null : budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGlyphLcdCoverage.Crossing>(crossingCount), SvgMemoryCategory.Temporary);
                        SvgGlyphLcdCoverage.Crossing[] crossings = crossingCount == 0 ? [] : new SvgGlyphLcdCoverage.Crossing[crossingCount];
                        Span<byte> preblend = stackalloc byte[256];
                        SvgColor color = GlyphLuminance(preparationWork);
                        SvgFontMaskPreblend.BuildWindowsGrayTable(preblend, color, preparationWork, cancellationToken);
                        double lcdMinimumX = double.PositiveInfinity, lcdMinimumY = double.PositiveInfinity, lcdMaximumX = double.NegativeInfinity, lcdMaximumY = double.NegativeInfinity;
                        foreach (OpenTypeQuadraticSegment segment in contours.Segments)
                        {
                            preparationWork.Consume(cancellationToken);
                            IncludeLcd(segment.Start); IncludeLcd(segment.Control); IncludeLcd(segment.End);
                        }
                        if (contours.Segments.Length != 0)
                        {
                            int lcdStartX = ClampFloor(lcdMinimumX - 1d / 3, outputWidth), lcdEndX = ClampCeiling(lcdMaximumX + 1d / 3 + (contours.ExtendWindowsLcdSpans ? 1d / 6 : 0), outputWidth);
                            int lcdStartY = ClampFloor(lcdMinimumY, outputHeight), lcdEndY = ClampCeiling(lcdMaximumY, outputHeight);
                            SvgGlyphLcdCoverage.DropoutBounds dropoutBounds = SvgGlyphLcdCoverage.PrepareBounds(contours.Segments, contours.SmartDropouts, contours.SimpleDropouts, contours.ExcludeDropoutStubs, contours.WindowsLcdMode, preparationWork, cancellationToken) with { ExtendRight = contours.ExtendWindowsLcdSpans };
                            // Directed horizontal scans are reused across one row. This
                            // optional preparation yields to the existing low-memory path.
                            using SvgGlyphLcdCoverage.ScanStorage? rowStorage = lcdStartX < lcdEndX && lcdStartY < lcdEndY ? SvgGlyphLcdCoverage.ScanStorage.TryCreate(contours.Segments, contours.WindowsLcdMode, budget, preparationWork, cancellationToken) : null;
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
                                    if (glyph.Shader is not null)
                                        for (int step = 0; step < 16; step++) preparationWork.Consume(cancellationToken);
                                    if (alpha != 0) GlyphPaintAt(x, y).Blend(destination.AsSpan((y * outputWidth + x) * 4, 4), alpha, SvgAnalyticPaintKind.Mask);
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
                        // Native text recording uses the font layout bounds. Keep
                        // an extra device pixel around them before avoiding fitting
                        // glyphs that cannot reach this target surface.
                        SvgPaintBounds layout = source.LayoutBounds(SvgAffineTransform.Identity, preparationWork, cancellationToken)
                            .Map(SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset), preparationWork, cancellationToken);
                        double fittingSize = source.DeviceFontSize(verticalScale);
                        if (fittingSize is > 0 and <= 32767
                            && (layout.Right < -1 || layout.Bottom < -1 || layout.Left > outputWidth + 1 || layout.Top > outputHeight + 1))
                        { workUnits += preparationWork.Used; return; }
                        using SvgAuthoredPath path = source.CreateLatinLightPath(horizontalScale, horizontalOffset, verticalScale, verticalOffset, budget, preparationWork, cancellationToken);
                        if (path.Commands.IsEmpty) { workUnits += preparationWork.Used; return; }
                        Span<byte> preblend = stackalloc byte[256];
                        SvgFontMaskPreblend.BuildLinuxGrayTable(preblend, GlyphLuminance(preparationWork), preparationWork, cancellationToken);
                        RasterizeMappedBitmap(path, preblend, preparationWork);
                        return;
                    }
                }
                // Glyphs without a platform-specific fitting route use the generic
                // contour coverage below.
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
                coverageWork = checked(coverage * checked(sampleWork * samplesPerPixel + 1 + (glyph.Shader is null ? 0 : 16)));
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
                if (glyph.Shader is null) BlendSourceOver(destination.AsSpan((y * outputWidth + x) * 4, 4), new SvgColor(glyph.Color.Red, glyph.Color.Green, glyph.Color.Blue, MultiplyAlpha(glyph.Color.Alpha, alpha)));
                else GlyphPaintAt(x, y).Blend(destination.AsSpan((y * outputWidth + x) * 4, 4), alpha, SvgAnalyticPaintKind.Mask);
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
                    long paintWork = (long)Math.Max(0, endX - startX) * Math.Max(0, endY - startY) * ((horizontalStrokeFilter ? 37 : 1) + (glyph.Shader is null ? 0 : 16));
                    if (paintWork > limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph mask painting exceeds the configured work limit.");
                    workUnits += paintWork;
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
                            GlyphPaintAt(x, y).Blend(destination.AsSpan((y * outputWidth + x) * 4, 4), alpha, SvgAnalyticPaintKind.Mask);

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

            SvgAnalyticPaint GlyphPaintAt(int x, int y)
            {
                if (glyphLinear is {} ink)
                {
                    if (ink.Stops.Length > 1 && ink.X1 == ink.X2 && ink.Y1 == ink.Y2)
                        return SvgAnalyticPaint.Constant(SvgShaderPaint.ConstantGradient(ink.Stops[^1], ShaderAlpha(glyph.Color.Alpha)));
                    double position;
                    if (ink.X1 == ink.X2 && ink.Y1 == ink.Y2) position = 1;
                    else if (!glyphCoordinates.TryLinear(x, y, ShaderBitmapOrigin(x, y), out position))
                        position = ink.SourcePaint?.Position((x + .5 - horizontalOffset) / horizontalScale, (y + .5 - verticalOffset) / verticalScale) ?? 1;
                    double span = ink.Offset2 - ink.Offset1;
                    double local = span == 0 ? 1 : (position - ink.Offset1) / span;
                    return SvgAnalyticPaint.Shader(glyphRamp.SampleLinear(Spread(local, ink.Spread), ShaderAlpha(glyph.Color.Alpha), GradientDither(x, y)));
                }
                if (glyphRadial is {} radial)
                {
                    double position;
                    if (radial.Stops.Length == 1) position = 0;
                    else if (radial.Radius == 0) position = 1;
                    else if (!glyphCoordinates.TryRadial(x, y, ShaderBitmapOrigin(x, y), out position))
                    {
                        double sampleX = (x + .5 - horizontalOffset) / horizontalScale;
                        double sampleY = (y + .5 - verticalOffset) / verticalScale;
                        if (radial.HasGeneralTransform) (sampleX, sampleY) = radial.Inverse.Apply(sampleX, sampleY);
                        double rayX = sampleX - radial.FocalX, rayY = sampleY - radial.FocalY;
                        double offsetX = radial.CenterX - radial.FocalX, offsetY = radial.CenterY - radial.FocalY;
                        double radius = radial.Radius, focalRadius = radial.FocalRadius;
                        if (Math.Abs(radial.ReferenceScaleX) != Math.Abs(radial.ReferenceScaleY))
                        {
                            rayX /= radial.ReferenceScaleX; rayY /= radial.ReferenceScaleY;
                            offsetX /= radial.ReferenceScaleX; offsetY /= radial.ReferenceScaleY;
                            radius /= Math.Abs(radial.ReferenceScaleX); focalRadius /= Math.Abs(radial.ReferenceScaleX);
                        }
                        position = SvgShaderCoordinates.EvaluateRadial(new(rayX, rayY), new(offsetX, offsetY), radius, focalRadius);
                    }
                    return SvgAnalyticPaint.Shader(glyphRamp.SampleRadial(Spread(position, radial.Spread), ShaderAlpha(glyph.Color.Alpha), GradientDither(x, y)));
                }
                return glyphPattern is null ? ResolveSolidPaint(glyph.Color)
                    : SvgAnalyticPaint.Shader(SamplePatternWithOpacity(glyphPattern, glyphTile.Surface.Pixels, x, y, ref glyphCoordinates, glyph.Color.Alpha));

                static double Spread(double value, SvgGradientSpread spread)
                {
                    if (spread == SvgGradientSpread.Repeat) return value - Math.Floor(value);
                    if (spread == SvgGradientSpread.Reflect)
                    {
                        double cycle = value - 2 * Math.Floor(value / 2);
                        return cycle > 1 ? 2 - cycle : cycle;
                    }
                    return Math.Clamp(value, 0, 1);
                }
            }

            SvgColor GlyphLuminance(ParserWorkCounter work)
            {
                if (glyphLinear is {} linear)
                {
                    if (linear.X1 == linear.X2 && linear.Y1 == linear.Y2) return linear.End;
                    if (linear.Stops.Length == 1) return linear.Stops[0].Color;
                    return GradientLuminance(linear.Stops, linear.Start, linear.End, work);
                }
                if (glyphRadial is {} radial)
                {
                    if (radial.Stops.Length == 1) return radial.Stops[0].Color;
                    if (radial.Radius == 0) return radial.End;
                    return GradientLuminance(radial.Stops, radial.Start, radial.End, work);
                }
                // SkPaintPriv uses neutral mid-gray when an image/picture shader
                // supplies no representative luminance color. See existing notices.
                return glyphPattern is null ? ResolveSolidColor(glyph.Color) : new(128, 128, 128, 255);
            }

            // SkGradientBaseShader uses an unweighted source-color mean to select
            // font-mask preblend, independently of stop alpha and pixel position.
            SvgColor GradientLuminance(ReadOnlySpan<SvgGradientStop> stops, SvgColor start, SvgColor end, ParserWorkCounter work)
            {
                float red = 0, green = 0, blue = 0;
                int count = 0;
                if (stops.Length != 0 && stops[0].Offset > 0) Include(stops[0].Color);
                foreach (SvgGradientStop stop in stops)
                {
                    work.Consume(cancellationToken);
                    Include(stop.Color);
                }
                if (count == 0) { Include(start); Include(end); }
                else
                {
                    if (stops[^1].Offset < 1) Include(stops[^1].Color);
                }
                float scale = 255f / count;
                return new((byte)MathF.Floor(red * scale + .5f), (byte)MathF.Floor(green * scale + .5f), (byte)MathF.Floor(blue * scale + .5f), 255);

                void Include(SvgColor color)
                {
                    red += color.Red * (1f / 255f); green += color.Green * (1f / 255f); blue += color.Blue * (1f / 255f); count++;
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
            => RasterizePreparedRectangle(PrepareRectangle(rectangle), ResolveSolidPaint(rectangle.Color), destination);

        void RasterizePreparedRectangle(SvgRectangleRaster raster, SvgAnalyticPaint rectanglePaint, byte[] destination)
        {
            int startX = raster.Left, endX = raster.Right, startY = raster.Top, endY = raster.Bottom;
            if (activeViewportClip.Length != 0 && activeViewportIsBinary)
            {
                ParserWorkCounter clippingWork = new(options.Limits.MaximumWorkUnits - workUnits);
                SvgBinaryClipRectangles regions = new(activeViewportClip, outputWidth, outputHeight, new(0, 0, outputWidth, outputHeight),
                    new(startX, startY, endX, endY), clippingWork, cancellationToken);
                while (regions.MoveNext())
                {
                    SvgAnalyticClipBounds region = regions.Current;
                    // Native binary regions clip rectangle geometry before AA:
                    // a surviving one-pixel strip has its own fringe coverage.
                    SvgRectangleCoverage coverage = raster.Coverage;
                    coverage = new(Math.Max(coverage.Left, (long)region.Left * 256), Math.Max(coverage.Top, (long)region.Top * 256),
                        Math.Min(coverage.Right, (long)region.Right * 256), Math.Min(coverage.Bottom, (long)region.Bottom * 256));
                    SvgRectangleRaster clipped = raster with { Coverage = coverage };
                    for (int y = region.Top; y < region.Bottom; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        for (int x = region.Left; x < region.Right; x++)
                            rectanglePaint.Blend(destination.AsSpan((y * outputWidth + x) * 4, 4), clipped.GetAlpha(x, y), SvgAnalyticPaintKind.Direct);
                    }
                }
                workUnits += clippingWork.Used;
                return;
            }
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
            double x1 = MapRectangleEdge(rectangle.X, horizontalScale, horizontalOffset);
            double y1 = MapRectangleEdge(rectangle.Y, verticalScale, verticalOffset);
            double x2 = MapRectangleEdge(rectangle.X + rectangle.Width, horizontalScale, horizontalOffset);
            double y2 = MapRectangleEdge(rectangle.Y + rectangle.Height, verticalScale, verticalOffset);
            double left = Math.Min(x1, x2), top = Math.Min(y1, y2);
            double right = Math.Max(x1, x2), bottom = Math.Max(y1, y2);
            SvgRectangleCoverage coverage = SvgRectangleCoverage.Create(left, top, right, bottom, outputWidth, outputHeight);
            SvgClipBox? clip = null;
            if (rectangle.Clip is {} sourceClip)
            {
                double cx1 = MapEdge(sourceClip.X, horizontalScale, horizontalOffset), cy1 = MapEdge(sourceClip.Y, verticalScale, verticalOffset);
                double cx2 = MapEdge(sourceClip.Right, horizontalScale, horizontalOffset), cy2 = MapEdge(sourceClip.Bottom, verticalScale, verticalOffset);
                double clipLeft = Math.Min(cx1, cx2), clipTop = Math.Min(cy1, cy2);
                double clipRight = Math.Max(cx1, cx2), clipBottom = Math.Max(cy1, cy2);
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
    {
        SvgViewport resolved = SvgDocumentSizing.Resolve(viewport, rootWidth, rootHeight, viewBox).LogicalViewport;
        return (resolved.Width, resolved.Height);
    }

    private static (double HorizontalScale, double HorizontalOffset, double VerticalScale, double VerticalOffset) GetViewBoxTransform(SvgViewBox? viewBox, double width, double height, double outputScale)
    {
        if (viewBox is not SvgViewBox box) return (outputScale, 0, outputScale, 0);
        (double horizontalScale, double verticalScale, double horizontalOffset, double verticalOffset) = SvgGeometrySyntax.MapViewBox(box, width, height);
        return (horizontalScale * outputScale, horizontalOffset * outputScale, verticalScale * outputScale, verticalOffset * outputScale);
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
        internal SvgFilterDeferredImage? DeferredImage { get; init; }
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
