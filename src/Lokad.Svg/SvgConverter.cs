namespace Lokad.Svg;

using Lokad.Svg.Parsing;
using Lokad.Svg.Encoding;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;
using System.Xml;
using System.Numerics;

/// <summary>Converts static SVG bytes to PNG bytes.</summary>
public static partial class SvgConverter
{
    /// <summary>Inspects root sizing using the conversion CSS/unit rules without constructing a scene or accessing fonts.</summary>
    /// <remarks>Uses the logical viewport and parsing limits from options. Output scale, background, font snapshot and raster limits do not affect the result. Unsupported drawing content is not a renderability check.</remarks>
    public static SvgDocumentDimensions InspectDimensions(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ValidateInvocation(svg, options, cancellationToken);
        using IMemoryScope? scope = options.MemoryBudget?.CreateScope();
        RenderBudget budget = scope is null ? new() : new(scope);
        try { return SvgDocumentSizing.Inspect(svg, options, budget, cancellationToken); }
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
        ValidateInvocation(svg, options, cancellationToken);
        if (options.MemoryBudget is not null) throw new InvalidOperationException("Governed output requires ConvertToOwnedPng, caller memory or a stream.");
        using RasterizedConversion conversion = PrepareRasterizationCore(svg, options, cancellationToken);
        return PngEncoder.Encode(conversion.Surface.Width, conversion.Surface.Height, conversion.Surface.Pixels, conversion.Budget, options.Limits.MaximumEncodedPngBytes, cancellationToken);
    }

    /// <summary>Converts SVG bytes to PNG storage owned by the disposable result.</summary>
    /// <remarks>The optional host budget is borrowed. A governed result owns a sibling scope created before preparation; its charge survives operation cleanup until result disposal. Finish readers before disposing the result.</remarks>
    public static SvgPngResult ConvertToOwnedPng(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ValidateInvocation(svg, options, cancellationToken);
        IMemoryScope? owner = options.MemoryBudget?.CreateScope();
        try
        {
            using RasterizedConversion conversion = PrepareRasterizationCore(svg, options, cancellationToken);
            byte[] png = PngEncoder.Encode(conversion.Surface.Width, conversion.Surface.Height, conversion.Surface.Pixels,
                conversion.Budget, owner, options.Limits.MaximumEncodedPngBytes, cancellationToken);
            return new(png, owner);
        }
        catch { owner?.Dispose(); throw; }
    }

    /// <summary>Schedules one owned-result conversion on the thread pool; its scope stays alive through result disposal.</summary>
    public static Task<SvgPngResult> ConvertToOwnedPngAsync(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
        => Task.Run(() => ConvertToOwnedPng(svg, options, cancellationToken), cancellationToken);

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
        return PngEncoder.EncodeInto(conversion.Surface.Width, conversion.Surface.Height, conversion.Surface.Pixels, destination, conversion.Budget, options.Limits.MaximumEncodedPngBytes, cancellationToken);
    }

    /// <summary>Converts SVG bytes to caller-owned writable stream output.</summary>
    public static int ConvertToPng(ReadOnlyMemory<byte> svg, Stream destination, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("PNG destination must be writable.", nameof(destination));
        using RasterizedConversion conversion = PrepareRasterization(svg, options, cancellationToken);
        return PngEncoder.EncodeTo(conversion.Surface.Width, conversion.Surface.Height, conversion.Surface.Pixels, destination, conversion.Budget, options.Limits.MaximumEncodedPngBytes, cancellationToken);
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
            return await PngEncoder.EncodeToAsync(conversion.Surface.Width, conversion.Surface.Height, conversion.Surface.Pixels, destination, conversion.Budget, options.Limits.MaximumEncodedPngBytes, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>Converts one retained prepared scene to an independently owned PNG result.</summary>
    internal static ReadOnlyMemory<byte> ConvertToPng(SvgRoot root, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
    {
        ValidatePreparedInvocation(options, budget, cancellationToken);
        using RasterizedSurface surface = Rasterize(root, options, budget, cancellationToken);
        return PngEncoder.Encode(surface.Width, surface.Height, surface.Pixels, budget, options.Limits.MaximumEncodedPngBytes, cancellationToken);
    }

    /// <summary>Encodes a retained scene while the caller's sibling scope owns the returned PNG storage and charge.</summary>
    internal static byte[] ConvertToOwnedPng(SvgRoot root, SvgRenderOptions options, RenderBudget budget, IMemoryScope? outputScope, CancellationToken cancellationToken)
    {
        ValidatePreparedInvocation(options, budget, cancellationToken);
        using RasterizedSurface surface = Rasterize(root, options, budget, cancellationToken);
        return PngEncoder.Encode(surface.Width, surface.Height, surface.Pixels, budget, outputScope,
            options.Limits.MaximumEncodedPngBytes, cancellationToken);
    }

    /// <summary>Converts one retained prepared scene into caller-owned destination memory.</summary>
    internal static int ConvertToPng(SvgRoot root, Memory<byte> destination, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
    {
        ValidatePreparedInvocation(options, budget, cancellationToken);
        using RasterizedSurface surface = Rasterize(root, options, budget, cancellationToken);
        return PngEncoder.EncodeInto(surface.Width, surface.Height, surface.Pixels, destination, budget, options.Limits.MaximumEncodedPngBytes, cancellationToken);
    }

    /// <summary>Converts one retained prepared scene to a caller-owned writable stream.</summary>
    internal static int ConvertToPng(SvgRoot root, Stream destination, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("PNG destination must be writable.", nameof(destination));
        ValidatePreparedInvocation(options, budget, cancellationToken);
        using RasterizedSurface surface = Rasterize(root, options, budget, cancellationToken);
        return PngEncoder.EncodeTo(surface.Width, surface.Height, surface.Pixels, destination, budget, options.Limits.MaximumEncodedPngBytes, cancellationToken);
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
            return await PngEncoder.EncodeToAsync(surface.Width, surface.Height, surface.Pixels, destination, budget, options.Limits.MaximumEncodedPngBytes, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Creates the budgeted parse-and-raster lifetime shared by output APIs and internal pixel validation.</summary>
    /// <remarks>The caller owns the result and finishes all pixel borrowers before disposal. Capture pixels are tightly packed sRGB straight-alpha RGBA8; RGB at zero alpha is unspecified until PNG encoding clears it. No PNG encoding or encoded-size validation occurs here.</remarks>
    internal static RasterizedConversion PrepareRasterization(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ValidateInvocation(svg, options, cancellationToken);
        return PrepareRasterizationCore(svg, options, cancellationToken);
    }

    private static RasterizedConversion PrepareRasterizationCore(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        IMemoryScope? scope = options.MemoryBudget?.CreateScope();
        RenderBudget budget = scope is null ? new() : new(scope);
        SvgRoot root = default;
        try
        {
            try
            {
                using SvgDocumentPreparation preparation = SvgDocumentPreparation.Create(svg, options.Limits, budget, cancellationToken);
                options = preparation.ResolveRasterOptions(options);
                SvgAffineTransform outputMap = preparation.GetOutputMap(options);
                root = SvgRootParser.ParsePrepared(svg, options.Limits, budget, options.FontSnapshot, cancellationToken,
                    outputMap, options.Viewport, options.OutputScale, preparation);
            }
            catch (XmlException exception) { throw new SvgException(SvgErrorCode.MalformedInput, "SVG XML is malformed: " + exception.Message); }
            if (root.HasUnsupportedChild) throw new SvgException(SvgErrorCode.UnsupportedFeature, "This SVG child element is not implemented yet.", null, root.UnsupportedDiagnostic);
            return new RasterizedConversion(budget, root, Rasterize(root, options, budget, cancellationToken), scope);
        }
        catch
        {
            root.Dispose();
            scope?.Dispose();
            throw;
        }
    }

    /// <summary>Renders one parsed scene while retaining its surface reservation until the returned surface is disposed.</summary>
    internal static RasterizedSurface Rasterize(SvgRoot root, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
        => Rasterize(root, options, budget, RasterSurfaceMode.Capture, cancellationToken);

    /// <summary>Renders a borrowed scene with shared budget/cancellation; pattern surfaces remain premultiplied for image-shader sampling.</summary>
    private static RasterizedSurface Rasterize(SvgRoot root, SvgRenderOptions options, RenderBudget budget, RasterSurfaceMode surfaceMode, CancellationToken cancellationToken)
        => Rasterize(root, options, budget, surfaceMode, null, cancellationToken);

    /// <summary>Restores one borrowed clip state and owns its replacement coverage until scope exit.
    /// A default frame marks an equivalent clip and owns nothing. The command walker charges
    /// metadata growth and visits, checks cancellation, and releases frames without cancellation.</summary>
    private readonly record struct ViewportClipFrame(ViewportClipState Previous,
        RenderBudget.BudgetReservation? Reservation, SvgCpuClipCoverage? Coverage);

    /// <summary>Borrows one replay scope's coverage, enclosure and drawing map; its frame owns replacement storage.</summary>
    /// <remarks>Conversion-thread state only. An isolated group can clear the pixel tape while retaining
    /// the allocation enclosure. Finish every borrower before releasing the frame's coverage and reservation.</remarks>
    private record struct ViewportClipState(byte[] Pixels, bool IsBinary, bool IsRectangle,
        SvgAnalyticClipBounds? Bounds, SvgCpuClipCoverage? Coverage, SvgCanvasDrawMap? DrawMap, SvgClipBox? Rectangle)
    {
        internal bool HasMask => Pixels.Length != 0 || Rectangle is not null;
        internal SvgRasterClipMask Mask => Rectangle is {} rectangle ? new(rectangle) : new(Pixels);
    }

    private enum SourceImageBoundsKind { Recorded, Element }

    private readonly record struct RasterFrame(int Width, int Height, double ScaleX, double OffsetX, double ScaleY, double OffsetY)
    {
        // Native filter sources use one padded device, with its content clip
        // separate from the physical bitmap origin.
        internal bool SingleBitmap { get; init; }
        /// <summary>Retains source content/request bounds; RasterClip can preserve additional numerical paint coverage.</summary>
        internal SvgAnalyticClipBounds? ContentClip { get; init; }
        /// <summary>Retains the admitted geometry and clip-resource raster window independently of source layout and filter requests.</summary>
        internal SvgAnalyticClipBounds? RasterClip { get; init; }
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
        // A vector pattern owns one raster image, independent of outer compositor tiles.
        SvgCpuRasterGrid rasterGrid = new(outputWidth, outputHeight,
            frame?.SingleBitmap == true || surfaceMode == RasterSurfaceMode.PremultipliedPattern);
        SvgAffineTransform sourcePlacement = frame?.SourcePlacement ?? SvgAffineTransform.Identity;
        if (!sourcePlacement.TryInvert(out SvgAffineTransform sourceInverse))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filter source placement must be invertible.");
        long pixelCountLong = (long)outputWidth * outputHeight;
        if (pixelCountLong > options.Limits.MaximumPixels || pixelCountLong > int.MaxValue / 4) throw new SvgException(SvgErrorCode.LimitExceeded, "Output pixel count exceeds the configured contiguous RGBA surface limit.");
        int pixelCount = (int)pixelCountLong;
        int surfaceByteCount = pixelCount * 4;
        SvgRootImageMap rootImage = frame is null ? SvgRootImageMap.Resolve(root.ViewBox, new(width, height), options.OutputScale) : default;
        SvgDrawingMap drawingMap = frame is {} filterFrame
            ? new(SvgAffineTransform.FromScaleOffset(filterFrame.ScaleX, filterFrame.ScaleY, filterFrame.OffsetX, filterFrame.OffsetY), null)
            : rootImage.Drawing;
        SvgAffineTransform outputMap = drawingMap.Parameter;
        (double horizontalScale, double horizontalOffset, double verticalScale, double verticalOffset) = (outputMap.A, outputMap.E, outputMap.D, outputMap.F);
        SvgAffineTransform authoredRootMap = frame is null ? rootImage.Layout : SvgAffineTransform.Identity;
        if (!double.IsFinite(horizontalScale) || !double.IsFinite(horizontalOffset) || !double.IsFinite(verticalScale) || !double.IsFinite(verticalOffset) || horizontalScale <= 0 || verticalScale <= 0)
        {
            throw new SvgException(SvgErrorCode.LimitExceeded, "Output scaling and viewBox mapping exceed the admitted numeric range.");
        }
        if (horizontalScale != 1 || verticalScale != 1)
        {
            foreach (SvgGeometry geometry in root.Geometries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (geometry.Kind == SvgGeometryKind.PatternFill && !TryMapPatternTile(geometry.Pattern, GetRecordedPaintValue(geometry.RecordedPaint).RecordingTransform, SvgAffineTransform.Identity, out _)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills under viewBox or output scaling are not implemented.");
                bool isCurveFill = (geometry.Kind == SvgGeometryKind.PolygonFill && geometry.Polygon.HasFill && geometry.Polygon.Mapping == SvgPolygonMapping.FlattenedCurves)
                    || (geometry.Kind == SvgGeometryKind.GradientPolygon && geometry.GradientPolygon.Mapping == SvgPolygonMapping.FlattenedCurves)
                    || (geometry.Kind == SvgGeometryKind.RadialPolygon && geometry.RadialPolygon.Mapping == SvgPolygonMapping.FlattenedCurves);
                if (horizontalScale != verticalScale && isCurveFill && geometry.AuthoredPath is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Curve fills under non-uniform viewBox or output scaling require retained source commands.");
            }
        }
        SvgCpuClipCoverage? layerClipCoverage = null;
        float foldedOpacity = 1;
        SvgAnalyticClipBounds layerBounds = frame?.ContentClip ?? new(0, 0, outputWidth, outputHeight);
        // Fit mode can retain a one-pixel canvas after the logical viewport
        // rounds empty. This gate is separate from the drawn image boundary.
        bool emptyRootViewport = frame is null && options.RasterBounds is not null
            && (MathF.Floor((float)(width * options.OutputScale) + .5f) == 0
                || MathF.Floor((float)(height * options.OutputScale) + .5f) == 0);
        bool maskClipAntialiased = false;
        bool maskLuminanceLayer = false;
        bool collectingViewportClip = false;
        ViewportClipState activeViewport = new([], true, false, null, null, null, null);
        if (frame is null && surfaceMode == RasterSurfaceMode.Capture && !emptyRootViewport
            && rootImage.Boundary is {} rootBoundary
            && (rootBoundary.Right < outputWidth || rootBoundary.Bottom < outputHeight))
        {
            SvgRasterClipMask mask = new(rootBoundary);
            int right = ClampCeiling(rootBoundary.Right, outputWidth), bottom = ClampCeiling(rootBoundary.Bottom, outputHeight);
            int middleX = Math.Max(0, (right - 1) / 2), middleY = Math.Max(0, (bottom - 1) / 2);
            while (right > 0 && mask.GetAlpha(outputWidth, right - 1, middleY) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                right--;
            }
            while (bottom > 0 && mask.GetAlpha(outputWidth, middleX, bottom - 1) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bottom--;
            }
            bool binary = true, opaque = right > 0 && bottom > 0;
            if (right > 0 && bottom > 0)
            {
                for (int row = 0; row < 3; row++)
                    for (int column = 0; column < 3; column++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int x = column * (right - 1) / 2, y = row * (bottom - 1) / 2;
                        byte alpha = mask.GetAlpha(outputWidth, x, y);
                        if (alpha is not (0 or 255)) binary = false;
                        opaque &= alpha == 255;
                    }
            }
            layerBounds = right > 0 && bottom > 0 ? new(0, 0, right, bottom) : new(0, 0, 0, 0);
            if (!opaque || right != outputWidth || bottom != outputHeight)
                activeViewport = new([], binary, binary, layerBounds, null, null, rootBoundary);
        }
        SvgCpuClipCoverage? collectingClipCoverage = null, activeConstructionCoverage = null;
        byte[] activeConstructionClip = [];
        SvgAnalyticClipBounds? activeConstructionBounds = null;
        SvgCpuClipCoverage? clipWindowCoverage = null;
        SvgAnalyticClipBounds? clipWindowBounds = null;
        SvgRecordedPaintValue activePaint = default;
        SvgRecordedPaintValue activeStrokePaint = default;
        SvgPatternTileCache? patternTileCache = null;
        SvgPatternGeometry? nextPatternTile = null;
        SvgFilterDeferredImage? deferredImage = null;
        SvgFilterColorSpace filterOutputColorSpace = SvgFilterColorSpace.Srgb;
        SvgFilterDeferredColorFilter filterSamplingColorFilter = SvgFilterDeferredColorFilter.None;
        RenderBudget.BudgetReservation surface = budget.Reserve(surfaceByteCount, SvgMemoryCategory.Surface);
        try
        {
            byte[] pixels = new byte[surfaceByteCount];
            if (options.BackgroundColor != SvgColor.Transparent)
            {
                BackgroundInitializationProbe.Started();
                byte backgroundRed = MultiplyAlpha(options.BackgroundColor.Red, options.BackgroundColor.Alpha);
                byte backgroundGreen = MultiplyAlpha(options.BackgroundColor.Green, options.BackgroundColor.Alpha);
                byte backgroundBlue = MultiplyAlpha(options.BackgroundColor.Blue, options.BackgroundColor.Alpha);
                for (int pixel = 0; pixel < pixelCount; pixel++)
                {
                    if ((pixel & 4_095) == 0 && cancellationToken.IsCancellationRequested)
                    {
                        BackgroundInitializationProbe.CancellationObserved();
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    int offset = pixel * 4;
                    pixels[offset] = backgroundRed;
                    pixels[offset + 1] = backgroundGreen;
                    pixels[offset + 2] = backgroundBlue;
                    pixels[offset + 3] = options.BackgroundColor.Alpha;
                }
            }
            if (!emptyRootViewport && !activeViewport.HasMask && surfaceMode == RasterSurfaceMode.Capture && TryResolveSolidCanvas(out SvgColor solidColor))
            {
                int red = (solidColor.Red * solidColor.Alpha + 127) / 255;
                int green = (solidColor.Green * solidColor.Alpha + 127) / 255;
                int blue = (solidColor.Blue * solidColor.Alpha + 127) / 255;
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
            return new RasterizedSurface(outputWidth, outputHeight, pixels, surface) { DeferredImage = deferredImage, FilterColorSpace = filterOutputColorSpace, SamplingColorFilter = filterSamplingColorFilter };
        }
        catch
        {
            surface.Dispose();
            throw;
        }
        finally { patternTileCache?.Dispose(); }

        SvgPaintBounds FilterRegion(SvgFilterBinding binding, SvgFilterResource resource, SvgAffineTransform output)
        {
            if (!resource.IsAvailable) return default;
            SvgFilterRegion region = resource.Region;
            bool objectUnits = region.Units == SvgFilterUnits.ObjectBoundingBox;
            SvgPaintBounds box = objectUnits ? binding.ParameterObjectBounds ?? binding.ObjectBounds : binding.ObjectBounds;
            double basisX = objectUnits ? box.Right - box.Left : binding.LengthBases.Width ?? width;
            double basisY = objectUnits ? box.Bottom - box.Top : binding.LengthBases.Height ?? height;
            double x = Length(region.X, basisX), y = Length(region.Y, basisY);
            double w = Length(region.Width, basisX), h = Length(region.Height, basisY);
            if (w <= 0 || h <= 0) return default;
            SvgAffineTransform map = output;
            if (objectUnits) { x += box.Left; y += box.Top; }
            if (!objectUnits || binding.ParameterObjectBounds is not null) map = map.ComposeChecked(binding.Transform);
            SvgPaintBounds bounds = new SvgPaintBounds((float)x, (float)y, (float)(x + w), (float)(y + h)).Map(map, cancellationToken);
            return bounds;

            double Length(SvgFilterLength length, double basis) => length.Percentage ? length.Value * basis / 100 : objectUnits ? length.Value * basis : length.Value;
        }

        // Borrow source geometry when rebasing a nested binding. Effects remain
        // separate from raw object/paint bounds; each traversal observes cancellation.
        SvgFilterBinding PlaceFilterBinding(SvgFilterBinding binding, int first)
        {
            if (sourcePlacement == SvgAffineTransform.Identity) return binding;
            SvgAffineTransform transform = frame?.FilterSourceMap is {} relation && relation.Source == binding.Transform
                ? relation.Layer : sourcePlacement.ComposeChecked(binding.Transform);
            SvgPaintBounds? objects = null, visuals = null;
            int depth = 0;
            for (int index = first; index < root.Commands.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                    if (paint.IsEffectAnchor)
                    {
                        if (!paint.IsMarkerContent && paint.ObjectBounds is {} anchor)
                            objects = Union(objects, anchor.Map(sourcePlacement, cancellationToken));
                        continue;
                    }
                    SvgPaintBounds box, visual;
                    if (geometry.AuthoredPath is {} path)
                    {
                        box = PathBounds(path);
                        visual = PathBounds(path.StrokeOutline ?? path);
                    }
                    else if (geometry.Kind == SvgGeometryKind.GlyphContours && geometry.Glyph.Source is {} glyph)
                    {
                        box = glyph.LayoutBounds(sourcePlacement, cancellationToken);
                        if (!binding.Transform.TryInvert(out SvgAffineTransform inverse)) return binding with { Transform = transform, ObjectBounds = default, VisualBounds = default };
                        SvgPaintBounds layout = glyph.LayoutBounds(inverse, cancellationToken);
                        visual = new SvgPaintBounds(MathF.Floor(layout.Left), MathF.Floor(layout.Top), MathF.Ceiling(layout.Right), MathF.Ceiling(layout.Bottom)).Map(transform, cancellationToken);
                        float inflation = (float)geometry.Glyph.StrokeBoundsInflation;
                        visual = new(visual.Left - inflation, visual.Top - inflation, visual.Right + inflation, visual.Bottom + inflation);
                    }
                    else
                    {
                        box = (paint.ObjectBounds ?? default).Map(sourcePlacement, cancellationToken);
                        visual = (paint.VisualBounds ?? paint.ObjectBounds ?? default).Map(sourcePlacement, cancellationToken);
                    }
                    if (!paint.IsMarkerContent) objects = Union(objects, box);
                    visuals = Union(visuals, visual);
                }
            }
            throw new SvgException(SvgErrorCode.MalformedInput, "Nested filter source is unbalanced.");

            SvgPaintBounds PathBounds(SvgAuthoredPath path)
            {
                SvgRectangle box = SvgPathBounds.Compute(path.Commands, cancellationToken);
                return new SvgPaintBounds((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height))
                    .Map(sourcePlacement.ComposeChecked(path.SceneMap), cancellationToken);
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

        // Each bordered destination bitmap determines its required source capture;
        // only its interior owns restored output. A non-null bitmap bounds the replay
        // to one scope, with invocation clip state restored on every exit.
        int RenderFilter(int first, SvgDrawCommand command, byte[] destination, SvgCpuRasterContext? destinationBitmap)
        {
            SvgFilterArena arena = root.FilterResources ?? throw new SvgException(SvgErrorCode.MalformedInput, "Filter resources are missing.");
            SvgFilterBinding binding = frame?.FilterBinding is {} replay && replay.Index == command.MaskContentIndex
                ? replay.Binding : arena.BindingAt(command.MaskContentIndex);
            SvgFilterResource resource = arena.ResourceAt(binding.ResourceIndex);
            int end = first, depth = 0, nestedDepth = -1;
            SvgPaintBounds? nestedSourceBounds = null, imageSourceBounds = null, elementSourceBounds = null;
            bool missingImageBounds = false, missingElementBounds = false;
            for (; end < root.Commands.Length; end++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgDrawCommand item = root.Commands[end];
                // Nested filters remain part of the recorded source picture.
                // Their original paint bounds seed its image extent and dither
                // origin; filtered output bounds are collected separately below.
                if (item.Kind == SvgDrawCommandKind.Geometry)
                {
                    SvgRecordedPaintValue sourcePaint = GetRecordedPaintValue(root.Geometries[item.GeometryIndex].RecordedPaint);
                    if (!sourcePaint.IsEffectAnchor)
                    {
                        if ((sourcePaint.SourceVisualBounds ?? sourcePaint.VisualBounds ?? sourcePaint.ObjectBounds) is {} sourceBox)
                            imageSourceBounds = imageSourceBounds is {} priorBox
                                ? new(MathF.Min(priorBox.Left, sourceBox.Left), MathF.Min(priorBox.Top, sourceBox.Top), MathF.Max(priorBox.Right, sourceBox.Right), MathF.Max(priorBox.Bottom, sourceBox.Bottom))
                                : sourceBox;
                        else missingImageBounds = true;
                        if ((sourcePaint.ElementVisualBounds ?? sourcePaint.VisualBounds ?? sourcePaint.ObjectBounds) is {} elementBox)
                            elementSourceBounds = elementSourceBounds is {} priorElement
                                ? new(MathF.Min(priorElement.Left, elementBox.Left), MathF.Min(priorElement.Top, elementBox.Top), MathF.Max(priorElement.Right, elementBox.Right), MathF.Max(priorElement.Bottom, elementBox.Bottom))
                                : elementBox;
                        else missingElementBounds = true;
                    }
                }
                if (item.Kind == SvgDrawCommandKind.EndGroup)
                {
                    if (depth-- == 0) break;
                    if (nestedDepth == depth) nestedDepth = -1;
                }
                if (item.Kind == SvgDrawCommandKind.BeginGroup)
                {
                    if (item.RegionKind == SvgLayerRegionKind.Filter && nestedDepth < 0)
                    {
                        // The child's bounded filtered output is this source's input.
                        // Its declared region can be larger; object bounds remain
                        // independent of effects.
                        SvgFilterBinding child = arena.BindingAt(item.MaskContentIndex);
                        child = PlaceFilterBinding(child, end + 1);
                        SvgPaintBounds childOutput = FilterResultBounds(child, arena.ResourceAt(child.ResourceIndex), SvgFilterBoundsKind.Output);
                        if (childOutput.Right > childOutput.Left && childOutput.Bottom > childOutput.Top)
                            nestedSourceBounds = nestedSourceBounds is {} previous
                                ? new(MathF.Min(previous.Left, childOutput.Left), MathF.Min(previous.Top, childOutput.Top), MathF.Max(previous.Right, childOutput.Right), MathF.Max(previous.Bottom, childOutput.Bottom))
                                : childOutput;
                        nestedDepth = depth;
                    }
                    depth++;
                }
            }
            if (end == root.Commands.Length) throw new SvgException(SvgErrorCode.MalformedInput, "Filter source scope is unbalanced.");
            if (!resource.IsAvailable) return end + 1;
            if (frame?.FilterBinding is not {} placed || placed.Index != command.MaskContentIndex)
            {
                binding = PlaceFilterBinding(binding, first);
            }
            if (binding.Transform.B != 0 || binding.Transform.C != 0)
                return RenderAffineFilter();
            SvgPaintBounds region = FilterRegion(binding, resource, SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset));
            if (region.Right <= region.Left || region.Bottom <= region.Top) return end + 1;
            double primitiveScaleX = resource.PrimitiveUnits == SvgFilterUnits.ObjectBoundingBox ? binding.ObjectBounds.Right - binding.ObjectBounds.Left : binding.Transform.A;
            double primitiveScaleY = resource.PrimitiveUnits == SvgFilterUnits.ObjectBoundingBox ? binding.ObjectBounds.Bottom - binding.ObjectBounds.Top : binding.Transform.D;
            SvgFilterDeviceMap primitiveMap = new(primitiveScaleX * horizontalScale, primitiveScaleY * verticalScale);
            SvgPaintBounds visual = binding.VisualBounds;
            if (nestedSourceBounds is {} nestedOutput)
            {
                SvgPaintBounds localOutput = nestedOutput.Map(SvgAffineTransform.FromScaleOffset(1 / horizontalScale, 1 / verticalScale,
                    -horizontalOffset / horizontalScale, -verticalOffset / verticalScale), cancellationToken);
                visual = new(MathF.Min(visual.Left, localOutput.Left), MathF.Min(visual.Top, localOutput.Top),
                    MathF.Max(visual.Right, localOutput.Right), MathF.Max(visual.Bottom, localOutput.Bottom));
                binding = binding with { VisualBounds = visual };
            }
            SvgPaintBounds imageVisual = PlaceImageHintBounds(imageSourceBounds, missingImageBounds, SourceImageBoundsKind.Recorded);
            SvgPaintBounds elementVisual = PlaceImageHintBounds(elementSourceBounds, missingElementBounds, SourceImageBoundsKind.Element);
            double sourceLeft = Math.Floor(visual.Left * horizontalScale + horizontalOffset), sourceTop = Math.Floor(visual.Top * verticalScale + verticalOffset);
            double sourceRight = Math.Ceiling(visual.Right * horizontalScale + horizontalOffset), sourceBottom = Math.Ceiling(visual.Bottom * verticalScale + verticalOffset);
            if (nestedSourceBounds is {} nested)
            {
                sourceLeft = Math.Min(sourceLeft, Math.Floor(nested.Left)); sourceTop = Math.Min(sourceTop, Math.Floor(nested.Top));
                sourceRight = Math.Max(sourceRight, Math.Ceiling(nested.Right)); sourceBottom = Math.Max(sourceBottom, Math.Ceiling(nested.Bottom));
            }
            SvgPaintBounds sourceCaptureBounds = new((float)sourceLeft, (float)sourceTop, (float)sourceRight, (float)sourceBottom);
            // Offset replay captures source ink; its native image hint and output
            // clip are computed separately. Enlarging this ink box changes curves.
            if (destinationBitmap is null)
            {
                if (TryRenderOffsetSources()) return end + 1;
                if (TryRenderFractionalOffsetBitmaps()) return end + 1;
                if (rasterGrid.ContextCount > 1)
                {
                    SvgAnalyticClipBounds previousBounds = layerBounds;
                    try
                    {
                        foreach (SvgCpuRasterContext bitmap in rasterGrid.GetContexts(previousBounds))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            layerBounds = new(Math.Max(previousBounds.Left, bitmap.BitmapClip.Left), Math.Max(previousBounds.Top, bitmap.BitmapClip.Top),
                                Math.Min(previousBounds.Right, bitmap.BitmapClip.Right), Math.Min(previousBounds.Bottom, bitmap.BitmapClip.Bottom));
                            RenderFilter(first, command, destination, bitmap);
                        }
                    }
                    finally { layerBounds = previousBounds; }
                    return end + 1;
                }
            }
            // A complex clip can cover only a narrow part of this bitmap.
            // Its local nonzero bounds govern filter requests, independently of
            // the full-canvas enclosure used to enumerate destination bitmaps.
            SvgAnalyticClipBounds? filterClipBounds = destinationBitmap is {} filterBitmap
                ? ActiveClipBounds(filterBitmap) : activeViewport.Bounds;
            if (frame?.NativeSourceBounds is null)
            {
                bool objectSourceUnits = resource.PrimitiveUnits == SvgFilterUnits.ObjectBoundingBox;
                SvgAffineTransform coordinates = objectSourceUnits
                    ? SvgAffineTransform.FromScaleOffset(primitiveMap.ScaleX, primitiveMap.ScaleY,
                        binding.ObjectBounds.Left * horizontalScale + horizontalOffset, binding.ObjectBounds.Top * verticalScale + verticalOffset)
                    : MapFilterParameterTransform(binding.Transform);
                SvgFilterRegionContext context = new(coordinates,
                    objectSourceUnits ? new(1, 1) : new(binding.LengthBases.Width ?? width, binding.LengthBases.Height ?? height), region);
                SvgPaintBounds hint = SvgFilterPlayback.SourceLayerBounds(resource, primitiveMap, context,
                    new((float)(visual.Left * horizontalScale + horizontalOffset), (float)(visual.Top * verticalScale + verticalOffset),
                        (float)(visual.Right * horizontalScale + horizontalOffset), (float)(visual.Bottom * verticalScale + verticalOffset)), budget, cancellationToken);
                SvgAffineTransform transform = MapFilterParameterTransform(binding.Transform);
                float sx = (float)transform.A, sy = (float)transform.D;
                float tx = (float)transform.E, ty = (float)transform.F;
                if (sx > 0 && sy > 0)
                    hint = new(MathF.Floor((hint.Left - tx) / sx) * sx + tx, MathF.Floor((hint.Top - ty) / sy) * sy + ty,
                        MathF.Ceiling((hint.Right - tx) / sx) * sx + tx, MathF.Ceiling((hint.Bottom - ty) / sy) * sy + ty);
                sourceLeft = Math.Floor(hint.Left); sourceTop = Math.Floor(hint.Top);
                sourceRight = Math.Ceiling(hint.Right); sourceBottom = Math.Ceiling(hint.Bottom);
            }
            double paddingX = 0, paddingY = 0;
            double offsetPaddingX = 0, offsetPaddingY = 0;
            foreach (SvgFilterPrimitive primitive in resource.Primitives.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                paddingX += Math.Ceiling(Math.Abs(primitive.SigmaX * primitiveMap.ScaleX) * 4) + Math.Ceiling(Math.Abs(primitive.OffsetX * primitiveMap.ScaleX));
                paddingY += Math.Ceiling(Math.Abs(primitive.SigmaY * primitiveMap.ScaleY) * 4) + Math.Ceiling(Math.Abs(primitive.OffsetY * primitiveMap.ScaleY));
                offsetPaddingX += Math.Ceiling(Math.Abs(primitive.OffsetX * primitiveMap.ScaleX));
                offsetPaddingY += Math.Ceiling(Math.Abs(primitive.OffsetY * primitiveMap.ScaleY));
            }
            double left = sourceLeft, top = sourceTop, right = sourceRight, bottom = sourceBottom;
            // The source grid only needs the captured image and requested output.
            // Kernel samples beyond this union are transparent; allocating the
            // entire halo makes large blurs needlessly consume unnecessary storage and traversal.
            double requestedLeft = Math.Max(region.Left, layerBounds.Left), requestedTop = Math.Max(region.Top, layerBounds.Top);
            double requestedRight = Math.Min(region.Right, layerBounds.Right), requestedBottom = Math.Min(region.Bottom, layerBounds.Bottom);
            if (filterClipBounds is {} filterClip)
            {
                requestedLeft = Math.Max(requestedLeft, filterClip.Left); requestedTop = Math.Max(requestedTop, filterClip.Top);
                requestedRight = Math.Min(requestedRight, filterClip.Right); requestedBottom = Math.Min(requestedBottom, filterClip.Bottom);
            }
            if (requestedRight <= requestedLeft || requestedBottom <= requestedTop) return end + 1;
            SvgCpuBitmapOrigin? imageShaderOrigin = null;
            SvgPaintBounds requestedSourceImage = new((float)left, (float)top, (float)right, (float)bottom);
            {
                bool objectSourceUnits = resource.PrimitiveUnits == SvgFilterUnits.ObjectBoundingBox;
                SvgAffineTransform sourceCoordinates = objectSourceUnits
                    ? SvgAffineTransform.FromScaleOffset(primitiveMap.ScaleX, primitiveMap.ScaleY,
                        binding.ObjectBounds.Left * horizontalScale + horizontalOffset, binding.ObjectBounds.Top * verticalScale + verticalOffset)
                    : MapFilterParameterTransform(binding.Transform);
                SvgFilterRegionContext sourceContext = new(sourceCoordinates,
                    objectSourceUnits ? new(1, 1) : new(binding.LengthBases.Width ?? width, binding.LengthBases.Height ?? height), region);
                SvgPaintBounds needed = SvgFilterPlayback.RequiredSourceBounds(resource, primitiveMap, sourceContext,
                    new((float)requestedLeft, (float)requestedTop, (float)requestedRight, (float)requestedBottom), budget, cancellationToken);
                SvgPaintBounds painted = new((float)(imageVisual.Left * horizontalScale + horizontalOffset), (float)(imageVisual.Top * verticalScale + verticalOffset),
                    (float)(imageVisual.Right * horizontalScale + horizontalOffset), (float)(imageVisual.Bottom * verticalScale + verticalOffset));
                SvgPaintBounds graphical = new((float)(elementVisual.Left * horizontalScale + horizontalOffset), (float)(elementVisual.Top * verticalScale + verticalOffset),
                    (float)(elementVisual.Right * horizontalScale + horizontalOffset), (float)(elementVisual.Bottom * verticalScale + verticalOffset));
                SvgPaintBounds outputHint = SvgFilterPlayback.SourceLayerBounds(resource, primitiveMap, sourceContext, graphical, SvgFilterBoundsKind.Output, budget, cancellationToken);
                SvgPaintBounds hint = ImageHint(painted, RoundOutputHint(outputHint));
                // Floods and later graph results can paint without source ink in the
                // requested region. Cull only after propagating the filter bounds.
                if (hint.Right + paddingX < requestedLeft || hint.Bottom + paddingY < requestedTop
                    || hint.Left - paddingX > requestedRight || hint.Top - paddingY > requestedBottom) return end + 1;
                requestedSourceImage = SvgFilterPlayback.RequestedSourceImageBounds(hint, needed);
                imageShaderOrigin = SourceImageOrigin(hint, needed);
                double captureLeft = Math.Max(left, needed.Left), captureTop = Math.Max(top, needed.Top);
                double captureRight = Math.Min(right, needed.Right), captureBottom = Math.Min(bottom, needed.Bottom);
                if (captureRight <= captureLeft || captureBottom <= captureTop)
                {
                    // The graph requests no drawable input here. Keep a transparent
                    // source grid near its output instead of retaining remote ink.
                    (left, top, right, bottom) = (requestedLeft, requestedTop, requestedRight, requestedBottom);
                }
                // Every source picture uses the propagated sampling request.
                // Capturing unrelated ink changes curve construction as well as
                // storage; image hints and transparent padding remain separate.
                else
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
                sourceCaptureBounds = new((float)left, (float)top, (float)right, (float)bottom);
            }
            // Empty source scopes still run their filter graph over transparent input.
            if (right <= left || bottom <= top)
                (left, top, right, bottom) = (requestedLeft, requestedTop, requestedRight, requestedBottom);
            double replayPaddingX = resource.Primitives.Items.Length > 1 ? paddingX : offsetPaddingX;
            double replayPaddingY = resource.Primitives.Items.Length > 1 ? paddingY : offsetPaddingY;
            double frameLeft = Math.Floor(Math.Min(left, requestedLeft - replayPaddingX)), frameTop = Math.Floor(Math.Min(top, requestedTop - replayPaddingY));
            double frameWidth = Math.Ceiling(Math.Max(right, requestedRight + replayPaddingX)) - frameLeft;
            double frameHeight = Math.Ceiling(Math.Max(bottom, requestedBottom + replayPaddingY)) - frameTop;
            frameLeft -= 1; frameTop -= 1;
            frameWidth += 2; frameHeight += 2;
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
            SvgPaintBounds sourceImageExtent = new((float)Math.Max(left, requestedSourceImage.Left), (float)Math.Max(top, requestedSourceImage.Top),
                (float)Math.Min(right, requestedSourceImage.Right), (float)Math.Min(bottom, requestedSourceImage.Bottom));
            primitiveMap = primitiveMap with { Regions = new(primitiveCoordinates,
                objectPrimitiveUnits ? new(1, 1) : new(binding.LengthBases.Width ?? width, binding.LengthBases.Height ?? height), gridRegion)
                { DeferFinalColorTransfer = frame?.FilterSourceMap is not null && frame?.FilterBinding is {} colorBinding && colorBinding.Index == command.MaskContentIndex, PictureOutput = surfaceMode == RasterSurfaceMode.PremultipliedPattern, RasterPlacement = new(new((int)frameLeft, (int)frameTop), rasterGrid, layerBounds), SourceBounds = new((float)(sourceImageExtent.Left - frameLeft), (float)(sourceImageExtent.Top - frameTop), (float)(sourceImageExtent.Right - frameLeft), (float)(sourceImageExtent.Bottom - frameTop)), DeferFinalOffset = frame?.FilterBinding is {} deferredBinding && deferredBinding.Index == command.MaskContentIndex && frame?.FilterSourceMap is not null } };
            using RenderBudget.BudgetReservation? commandsOwner = end > first
                ? budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(end - first), SvgMemoryCategory.Scene) : null;
            SvgDrawCommand[] sourceCommands = new SvgDrawCommand[end - first];
            for (int index = first; index < end; index++) { cancellationToken.ThrowIfCancellationRequested(); sourceCommands[index - first] = root.Commands[index]; }
            SvgRenderLimits limits = options.Limits;
            SvgRenderLimits sourceLimits = limits;
            SvgRenderOptions sourceOptions = new(options.Viewport, options.OutputScale, SvgColor.Transparent, sourceLimits, options.FontSnapshot);
            SvgRoot borrowed = root with { Commands = sourceCommands};
            // Captured content governs image extents and nested filter requests;
            // construction separately retains the source image hint's padding.
            SvgAnalyticClipBounds sourceContentClip = SourceContentClip(sourceCaptureBounds, new((int)left - 1, (int)top - 1), sw + 2, sh + 2);
            if (primitiveMap.Regions is {} capturedRegion)
                primitiveMap = primitiveMap with { Regions = capturedRegion with { CapturedSourceBounds = new(
                    (float)(left - frameLeft + sourceContentClip.Left - 1), (float)(top - frameTop + sourceContentClip.Top - 1),
                    (float)(left - frameLeft + sourceContentClip.Right - 1), (float)(top - frameTop + sourceContentClip.Bottom - 1)) } };
            RasterFrame captureFrame = new(sw + 2, sh + 2, horizontalScale, horizontalOffset - left + 1, verticalScale, verticalOffset - top + 1)
            { SingleBitmap = true, ContentClip = sourceContentClip, RasterClip = new(1, 1, sw + 1, sh + 1), ParentOrigin = destinationBitmap?.Origin ?? new(0, 0), CaptureOrigin = new((int)left - 1, (int)top - 1), ImageShaderOrigin = imageShaderOrigin, SourcePlacement = sourcePlacement, NativeSourceBounds = frame?.NativeSourceBounds, FilterSourceMap = frame?.FilterSourceMap, NativeCapturePlacement = PlaceNativeSource(new((int)left - 1, (int)top - 1)) };
            // Region coordinates belong to the padded filter bitmap; sampled
            // source-image transforms retain their separate capture domain.
            if (primitiveMap.Regions is {} nativeRegions)
            {
                // Primitive parameters belong to the filter bitmap. Source draws
                // retain their own recording placement and canvas stages.
                SvgAffineTransform nativePlacement = captureFrame.NativeCapturePlacement
                    ?? SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset - left + 1, verticalOffset - top + 1);
                SvgAffineTransform parameters = nativePlacement.ComposeNative(arena.BindingAt(command.MaskContentIndex).Transform);
                SvgCpuBitmapOrigin nativeOrigin = new((int)(left - 1 - frameLeft), (int)(top - 1 - frameTop));
                SvgPaintBounds parameterObjects = binding.ParameterObjectBounds ?? default;
                cancellationToken.ThrowIfCancellationRequested();
                if (binding.Transform.TryInvert(out SvgAffineTransform inverseRegion))
                {
                    SvgFilterBinding parameterBinding = binding with { Transform = SvgAffineTransform.Identity,
                        ObjectBounds = binding.ObjectBounds.Map(inverseRegion, cancellationToken) };
                    parameterObjects = parameterBinding.ParameterObjectBounds ?? parameterBinding.ObjectBounds;
                    gridRegion = FilterRegion(parameterBinding, resource, parameters)
                        .Map(SvgAffineTransform.FromTranslation(nativeOrigin.X, nativeOrigin.Y), cancellationToken);
                    region = new((float)(gridRegion.Left + frameLeft), (float)(gridRegion.Top + frameTop),
                        (float)(gridRegion.Right + frameLeft), (float)(gridRegion.Bottom + frameTop));
                    requestedLeft = Math.Max(layerBounds.Left, gridRegion.Left + frameLeft);
                    requestedTop = Math.Max(layerBounds.Top, gridRegion.Top + frameTop);
                    requestedRight = Math.Min(layerBounds.Right, gridRegion.Right + frameLeft);
                    requestedBottom = Math.Min(layerBounds.Bottom, gridRegion.Bottom + frameTop);
                    if (filterClipBounds is {} nativeClip)
                    {
                        requestedLeft = Math.Max(requestedLeft, nativeClip.Left); requestedTop = Math.Max(requestedTop, nativeClip.Top);
                        requestedRight = Math.Min(requestedRight, nativeClip.Right); requestedBottom = Math.Min(requestedBottom, nativeClip.Bottom);
                    }
                }
                SvgAffineTransform primitiveParameters = objectPrimitiveUnits
                    ? parameters.ComposeNative(SvgAffineTransform.FromScaleOffset(parameterObjects.Right - parameterObjects.Left,
                        parameterObjects.Bottom - parameterObjects.Top, parameterObjects.Left, parameterObjects.Top)) : parameters;
                SvgFilterNativeMapping regionMapping = new(new((float)primitiveParameters.A, (float)primitiveParameters.C, (float)primitiveParameters.E,
                    (float)primitiveParameters.B, (float)primitiveParameters.D, (float)primitiveParameters.F), nativeOrigin);
                primitiveMap = primitiveMap with { Regions = nativeRegions with { FilterRegion = gridRegion, NativeRegionMapping = regionMapping,
                    NativeMapping = !objectPrimitiveUnits && captureFrame.NativeCapturePlacement is not null ? regionMapping : nativeRegions.NativeMapping } };
            }
            using RasterizedSurface captured = Rasterize(borrowed, sourceOptions, budget, RasterSurfaceMode.PremultipliedFilter, captureFrame, cancellationToken);
            int frameBytes = fw * fh * 4;
            using RenderBudget.BudgetReservation frameOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<byte>(frameBytes), SvgMemoryCategory.Surface);
            byte[] source = new byte[frameBytes];
            int px = (int)(left - frameLeft), py = (int)(top - frameTop);
            for (int row = 0; row < sh; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                captured.Pixels.AsSpan(((row + 1) * (sw + 2) + 1) * 4, sw * 4).CopyTo(source.AsSpan(((row + py) * fw + px) * 4, sw * 4));
            }
            using SvgFilterResult filtered = SvgFilterPlayback.Apply(source, fw, fh, resource, primitiveMap,
                new((float)(requestedLeft - frameLeft), (float)(requestedTop - frameTop),
                    (float)(requestedRight - frameLeft), (float)(requestedBottom - frameTop)),
                budget, cancellationToken);
            if (frame?.FilterBinding is {} colorOutput && colorOutput.Index == command.MaskContentIndex)
            {
                filterOutputColorSpace = filtered.ColorSpace;
                filterSamplingColorFilter = filtered.SamplingColorFilter;
            }
            if (filtered.DeferredImage is {} deferred)
            {
                SvgAffineTransform move = SvgAffineTransform.FromTranslation(frameLeft, frameTop);
                deferredImage = deferred with { SourceBounds = deferred.SourceBounds.Map(move, cancellationToken), Clip = deferred.Clip.Map(move, cancellationToken) };
                SvgAnalyticClipBounds deferredOutputArea = destinationBitmap?.Interior ?? new(0, 0, outputWidth, outputHeight);
                for (int y = deferredOutputArea.Top; y < deferredOutputArea.Bottom; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int x = deferredOutputArea.Left; x < deferredOutputArea.Right; x++)
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
            if (destinationBitmap is {} outputBitmap)
            {
                restoreLeft = Math.Max(restoreLeft, outputBitmap.Interior.Left); restoreTop = Math.Max(restoreTop, outputBitmap.Interior.Top);
                restoreRight = Math.Min(restoreRight, outputBitmap.Interior.Right); restoreBottom = Math.Min(restoreBottom, outputBitmap.Interior.Bottom);
            }
            // Nonopaque filters restore through an enclosing opacity layer.
            // Its parameter-space painted extent is separate from the filtered
            // image's stored texels and sampling bounds.
            if (command.SourceOpacity.Value * foldedOpacity < 1)
            {
                SvgPaintBounds opacityExtent = FilterResultBounds(binding, resource, SvgFilterBoundsKind.Output);
                restoreLeft = Math.Max(restoreLeft, ClampFloor(opacityExtent.Left, outputWidth));
                restoreTop = Math.Max(restoreTop, ClampFloor(opacityExtent.Top, outputHeight));
                restoreRight = Math.Min(restoreRight, ClampCeiling(opacityExtent.Right, outputWidth));
                restoreBottom = Math.Min(restoreBottom, ClampCeiling(opacityExtent.Bottom, outputHeight));
            }
            SvgFilterUniformPaint? uniformPaint = filtered.ColorSpace == SvgFilterColorSpace.Srgb && command.SourceOpacity.Value * foldedOpacity == 1 ? filtered.UniformPaint : null;
            // Aliases keep source color filters instead of converted texels.
            // Consume them at final restoration; captured affine sources still
            // defer the filter to their later image sampler.
            bool finalSourceColorFilter = (frame?.FilterBinding is not {} retainedFilter || retainedFilter.Index != command.MaskContentIndex)
                && filtered.SamplingColorFilter is SvgFilterDeferredColorFilter.SourceAlpha or SvgFilterDeferredColorFilter.LinearRgbRoundTrip;
            Span<byte> aliasPixel = stackalloc byte[4];
            for (int y = restoreTop; y < restoreBottom; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = restoreLeft; x < restoreRight; x++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int target = (y * outputWidth + x) * 4, origin = ((int)(y - frameTop) * fw + (int)(x - frameLeft)) * 4;
                    if (uniformPaint is {} uniform)
                    {
                        int sx = origin / 4 % fw, sy = origin / 4 / fw;
                        SvgPaintBounds bounds = uniform.Bounds;
                        if (sx >= MathF.Floor(bounds.Left) && sx < MathF.Ceiling(bounds.Right) && sy >= MathF.Floor(bounds.Top) && sy < MathF.Ceiling(bounds.Bottom))
                        {
                            byte coverage = uniform.Coverage is {} edge ? edge.GetAlpha(sx, sy) : (byte)255;
                            if (activeViewport.HasMask) coverage = (byte)((coverage * activeViewport.Mask.GetAlphaAtOffset(outputWidth, target) + 127) / 255);
                            if (coverage == 0) continue;
                            if (uniform.Coverage is not null) uniform.Paint.AsConstant(1f).BlendBitmap(destination.AsSpan(target, 4), coverage);
                            else if (uniform.HighPrecision) uniform.Paint.Blend(destination.AsSpan(target, 4), coverage);
                            else uniform.Paint.AsConstant(1f).Blend(destination.AsSpan(target, 4), coverage);
                        }
                        continue;
                    }
                    float opacity = command.SourceOpacity.Value * foldedOpacity;
                    scoped ReadOnlySpan<byte> restoredPixel = filtered.Pixels.AsSpan(origin, 4);
                    SvgShaderPaint? terminalPaint = filtered.Samples?[origin / 4];
                    if (finalSourceColorFilter)
                    {
                        const float byteScale = 1f / 255f;
                        SvgShaderPaint sourcePaint = filtered.SamplingColorFilter == SvgFilterDeferredColorFilter.SourceAlpha
                            ? new(0, 0, 0, restoredPixel[3] * byteScale)
                            : new(restoredPixel[0] * byteScale, restoredPixel[1] * byteScale, restoredPixel[2] * byteScale, restoredPixel[3] * byteScale);
                        terminalPaint = filtered.SamplingColorFilter == SvgFilterDeferredColorFilter.LinearRgbRoundTrip
                            ? SvgFilterColorTransfer.Convert(SvgFilterColorTransfer.Convert(sourcePaint, SvgFilterColorSpace.LinearRgb), SvgFilterColorSpace.Srgb)
                            : sourcePaint;
                    }
                    // Native nonopaque restoration materializes the converted image
                    // before packed paint-alpha modulation. Opaque shader results
                    // retain their unquantized transfer route.
                    if (filtered.ColorSpace == SvgFilterColorSpace.Srgb && terminalPaint is {} sample && opacity == 1)
                    {
                        sample.Blend(destination.AsSpan(target, 4), !activeViewport.HasMask ? (byte)255 : activeViewport.Mask.GetAlphaAtOffset(outputWidth, target));
                        continue;
                    }
                    if (finalSourceColorFilter && terminalPaint is {} aliasPaint)
                    {
                        aliasPixel.Clear();
                        aliasPaint.Blend(aliasPixel, 255);
                        restoredPixel = aliasPixel;
                    }
                    // A stored filter image uses native packed-lane alpha modulation;
                    // unquantized shader results above retain their separate float route.
                    int opacityScale = Math.Clamp((int)MathF.Floor(opacity * 255 + .5f), 0, 255) + 1;
                    if (activeViewport.HasMask)
                    {
                        byte clip = activeViewport.Mask.GetAlphaAtOffset(outputWidth, target);
                        if (clip != 0) BlendClippedLayerPixel(destination.AsSpan(target, 4), restoredPixel, (byte)(opacityScale - 1), 255, clip);
                        continue;
                    }
                    // Global bitmap alpha keeps source and backdrop products
                    // together until the final packed shift.
                    RestoreOpacityLayerPixel(destination.AsSpan(target, 4), restoredPixel, (byte)(opacityScale - 1));
                }
            }
            return end + 1;

            // Filter-layer mapping adapts pinned SkCanvas.cpp and SkImageFilterTypes.cpp.
            // Copyright 2008 The Android Open Source Project; 2019 Google LLC.
            // See THIRD_PARTY_NOTICES.txt for the BSD license and source revision.
            int RenderAffineFilter()
            {
                foreach (SvgCpuRasterContext bitmap in rasterGrid.GetContexts(layerBounds))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RenderAffineBitmap(bitmap);
                }
                return end + 1;
            }

            void RenderAffineBitmap(SvgCpuRasterContext bitmap)
            {
                // Filter kernels require scale/translate coordinates. Keep their
                // layer raster separate from the residual rotation/skew, just as
                // native source construction is separate from final image sampling.
                if (!binding.Transform.TryInvert(out SvgAffineTransform inverseSource)) return;
                SvgAffineTransform ctm = MapFilterDrawingTransform(binding.Transform);
                SvgShaderMatrix native = new((float)ctm.A, (float)ctm.C, (float)ctm.E, (float)ctm.B, (float)ctm.D, (float)ctm.F);
                (float scaleX, float scaleY) = native.GetPictureScale();
                SvgShaderMatrix remainder = new(native.ScaleX * (1f / scaleX), native.SkewX * (1f / scaleY), native.TranslateX,
                    native.SkewY * (1f / scaleX), native.ScaleY * (1f / scaleY), native.TranslateY);
                if (!remainder.TryInvert(out SvgShaderMatrix inverse)) return;
                SvgAffineTransform layerMap = SvgAffineTransform.FromScale(scaleX, scaleY);
                SvgAffineTransform parameterInverse = sourcePlacement == SvgAffineTransform.Identity ? inverseSource : inverseSource.ComposeChecked(sourcePlacement);
                SvgAffineTransform placement = layerMap.ComposeChecked(parameterInverse);
                int count = end - first;
                using RenderBudget.BudgetReservation replayOwner = budget.Reserve(
                    checked(ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(count + 2)
                        + ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(count)), SvgMemoryCategory.Scene);
                SvgDrawCommand[] commands = new SvgDrawCommand[count + 2];
                SvgGeometry[] geometries = new SvgGeometry[count];
                int geometryCount = 0;
                SvgPaintBounds? objectBounds = null, visualBounds = null;
                SvgPaintBounds? parameterObjectBounds = null, parameterVisualBounds = null;
                {
                    commands[0] = command with { SourceOpacity = SvgPaintOpacity.Opaque };
                    for (int index = first; index < end; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        SvgDrawCommand item = root.Commands[index];
                        if (item.Kind == SvgDrawCommandKind.Geometry)
                        {
                            SvgGeometry geometry = root.Geometries[item.GeometryIndex];
                            if (GetRecordedPaintValue(geometry.RecordedPaint).IsEffectAnchor)
                            {
                                SvgRecordedPaintValue paint = GetRecordedPaintValue(geometry.RecordedPaint);
                                if (paint.ObjectBounds is {} anchorBounds)
                                {
                                    if (!paint.IsMarkerContent)
                                    {
                                        objectBounds = Union(objectBounds, anchorBounds.Map(placement, cancellationToken));
                                        parameterObjectBounds = Union(parameterObjectBounds, anchorBounds.Map(parameterInverse, cancellationToken));
                                    }
                                }
                                geometries[geometryCount] = geometry;
                                commands[index - first + 1] = item with { GeometryIndex = geometryCount++ };
                                continue;
                            }
                            if (geometry.Kind == SvgGeometryKind.GlyphContours && geometry.Glyph.Source is not null)
                            {
                                geometries[geometryCount] = geometry;
                                item = item with { GeometryIndex = geometryCount++ };
                                SvgPaintBounds layout = geometry.Glyph.Source.LayoutBounds(parameterInverse, cancellationToken);
                                objectBounds = Union(objectBounds, layout.Map(layerMap, cancellationToken));
                                parameterObjectBounds = Union(parameterObjectBounds, layout);
                                // Native text recording encloses the font layout before its
                                // filter layer maps the resulting integer visual rectangle.
                                SvgPaintBounds glyphVisual = new SvgPaintBounds(MathF.Floor(layout.Left), MathF.Floor(layout.Top), MathF.Ceiling(layout.Right), MathF.Ceiling(layout.Bottom))
                                    .Map(layerMap, cancellationToken);
                                float inflation = (float)geometry.Glyph.StrokeBoundsInflation;
                                visualBounds = Union(visualBounds, new(glyphVisual.Left - inflation, glyphVisual.Top - inflation, glyphVisual.Right + inflation, glyphVisual.Bottom + inflation));
                                parameterVisualBounds = Union(parameterVisualBounds, new(MathF.Floor(layout.Left) - inflation / scaleX,
                                    MathF.Floor(layout.Top) - inflation / scaleY, MathF.Ceiling(layout.Right) + inflation / scaleX, MathF.Ceiling(layout.Bottom) + inflation / scaleY));
                                commands[index - first + 1] = item;
                                continue;
                            }
                            if (geometry.Kind is not (SvgGeometryKind.PolygonFill or SvgGeometryKind.GradientPolygon or SvgGeometryKind.RadialPolygon or SvgGeometryKind.PatternPolygon) || geometry.AuthoredPath is not {} source)
                                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Affine filter sources require retained geometry or supplied glyphs.");
                            geometries[geometryCount] = geometry;
                            item = item with { GeometryIndex = geometryCount++ };
                            SvgRectangle box = SvgPathBounds.Compute(source.Commands, cancellationToken);
                            SvgPaintBounds bounds = new SvgPaintBounds((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height))
                                .Map(placement.ComposeChecked(source.SceneMap), cancellationToken);
                            if (!GetRecordedPaintValue(geometry.RecordedPaint).IsMarkerContent)
                            {
                                objectBounds = Union(objectBounds, bounds);
                                parameterObjectBounds = Union(parameterObjectBounds, new SvgPaintBounds((float)box.X, (float)box.Y,
                                    (float)(box.X + box.Width), (float)(box.Y + box.Height)).Map(parameterInverse.ComposeChecked(source.SceneMap), cancellationToken));
                            }
                            SvgAuthoredPath outline = source.StrokeOutline ?? source;
                            box = SvgPathBounds.Compute(outline.Commands, cancellationToken);
                            visualBounds = Union(visualBounds, new SvgPaintBounds((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height))
                                .Map(placement.ComposeChecked(outline.SceneMap), cancellationToken));
                            parameterVisualBounds = Union(parameterVisualBounds, new SvgPaintBounds((float)box.X, (float)box.Y,
                                (float)(box.X + box.Width), (float)(box.Y + box.Height)).Map(parameterInverse.ComposeChecked(outline.SceneMap), cancellationToken));
                            if (GetRecordedPaintValue(geometry.RecordedPaint).VisualBounds is {} drawingHint)
                            {
                                visualBounds = Union(visualBounds, drawingHint.Map(placement, cancellationToken));
                                parameterVisualBounds = Union(parameterVisualBounds, drawingHint.Map(parameterInverse, cancellationToken));
                            }
                        }
                        commands[index - first + 1] = item;
                    }
                    commands[^1] = root.Commands[end];
                    SvgPaintBounds objects = objectBounds ?? default, visual = visualBounds ?? default;
                    SvgFilterBinding local = binding with { Transform = layerMap, ObjectBounds = objects, VisualBounds = visual };
                    SvgPaintBounds layerRegion = FilterRegion(local, resource, SvgAffineTransform.Identity);
                    bool objectUnits = resource.PrimitiveUnits == SvgFilterUnits.ObjectBoundingBox;
                    SvgFilterDeviceMap distances = objectUnits ? new(objects.Right - objects.Left, objects.Bottom - objects.Top) : new(scaleX, scaleY);
                    SvgFilterRegionContext context = new(objectUnits
                        ? SvgAffineTransform.FromScaleOffset(distances.ScaleX, distances.ScaleY, objects.Left, objects.Top) : layerMap,
                        objectUnits ? new(1, 1) : new(binding.LengthBases.Width ?? width, binding.LengthBases.Height ?? height), layerRegion);
                    SvgPaintBounds hint = SvgFilterPlayback.SourceLayerBounds(resource, distances, context, visual, budget, cancellationToken);
                    // Capture hints are rounded before layer scaling. Inverting a
                    // layer-space hint loses authored fringes and perturbs integers.
                    SvgPaintBounds parameterObjects = parameterObjectBounds ?? default, parameterVisual = parameterVisualBounds ?? default;
                    SvgFilterBinding parameterBinding = binding with { Transform = SvgAffineTransform.Identity, ObjectBounds = parameterObjects, VisualBounds = parameterVisual };
                    SvgFilterDeviceMap parameterDistances = objectUnits
                        ? new(parameterObjects.Right - parameterObjects.Left, parameterObjects.Bottom - parameterObjects.Top) : new(1, 1);
                    SvgFilterRegionContext parameterContext = new(objectUnits
                        ? SvgAffineTransform.FromScaleOffset(parameterDistances.ScaleX, parameterDistances.ScaleY, parameterObjects.Left, parameterObjects.Top)
                        : SvgAffineTransform.Identity, context.LengthBases, FilterRegion(parameterBinding, resource, SvgAffineTransform.Identity));
                    SvgPaintBounds parameterHint = SvgFilterPlayback.ParameterCaptureBounds(resource, parameterDistances,
                        parameterContext, parameterVisual, budget, cancellationToken);
                    SvgPaintBounds nativeHint = new(parameterHint.Left * scaleX, parameterHint.Top * scaleY, parameterHint.Right * scaleX, parameterHint.Bottom * scaleY);
                    SvgPaintBounds nativeSource = SvgFilterPlayback.RoundLayerBounds(nativeHint);
                    SvgFilterImageBounds storedImage = SvgFilterPlayback.SourceImageBounds(resource, distances, context, nativeSource, budget, cancellationToken);
                    SvgPaintBounds nativeImage = storedImage.Bounds;
                    if (nativeImage.Right <= nativeImage.Left || nativeImage.Bottom <= nativeImage.Top) return;
                    SvgPaintBounds desired = new(Math.Max(layerBounds.Left, bitmap.BitmapClip.Left), Math.Max(layerBounds.Top, bitmap.BitmapClip.Top),
                        Math.Min(layerBounds.Right, bitmap.BitmapClip.Right), Math.Min(layerBounds.Bottom, bitmap.BitmapClip.Bottom));
                    if (ActiveClipBounds(bitmap) is {} clip)
                        desired = new(MathF.Max(desired.Left, clip.Left), MathF.Max(desired.Top, clip.Top), MathF.Min(desired.Right, clip.Right), MathF.Min(desired.Bottom, clip.Bottom));
                    desired = desired.Map(new(inverse.ScaleX, inverse.SkewY, inverse.SkewX, inverse.ScaleY, inverse.TranslateX, inverse.TranslateY), cancellationToken);
                    bool deferredSource = resource.Primitives.Items.Length > 0 && resource.Primitives.Items[^1].Kind == SvgFilterPrimitiveKind.Offset
                        && resource.Primitives.Items[^1].Input.Index < 0;
                    {
                        SvgPaintBounds needed = SvgFilterPlayback.RequiredSourceBounds(resource, distances, context, desired, budget, cancellationToken);
                        if (deferredSource || needed.Right > needed.Left && needed.Bottom > needed.Top)
                        {
                            nativeSource = new(MathF.Max(nativeSource.Left, needed.Left), MathF.Max(nativeSource.Top, needed.Top),
                                MathF.Min(nativeSource.Right, needed.Right), MathF.Min(nativeSource.Bottom, needed.Bottom));
                            if (nativeSource.Right <= nativeSource.Left || nativeSource.Bottom <= nativeSource.Top) return;
                            storedImage = SvgFilterPlayback.SourceImageBounds(resource, distances, context, nativeSource, budget, cancellationToken);
                            nativeImage = storedImage.Bounds;
                        }
                    }
                    double left = Math.Floor(MathF.Max(MathF.Max(layerRegion.Left, hint.Left), desired.Left)), top = Math.Floor(MathF.Max(MathF.Max(layerRegion.Top, hint.Top), desired.Top));
                    double right = Math.Ceiling(MathF.Min(MathF.Min(layerRegion.Right, hint.Right), desired.Right)), bottom = Math.Ceiling(MathF.Min(MathF.Min(layerRegion.Bottom, hint.Bottom), desired.Bottom));
                    // A stored graph result may extend outside the visual source
                    // hint. Retain both extents without shrinking the prior grid.
                    left = Math.Min(left, Math.Floor(MathF.Max(MathF.Max(layerRegion.Left, nativeImage.Left), desired.Left)));
                    top = Math.Min(top, Math.Floor(MathF.Max(MathF.Max(layerRegion.Top, nativeImage.Top), desired.Top)));
                    right = Math.Max(right, Math.Ceiling(MathF.Min(MathF.Min(layerRegion.Right, nativeImage.Right), desired.Right)));
                    bottom = Math.Max(bottom, Math.Ceiling(MathF.Min(MathF.Min(layerRegion.Bottom, nativeImage.Bottom), desired.Bottom)));
                    if (right <= left || bottom <= top) return;
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
                        .Map(new(remainder.ScaleX, remainder.SkewY, remainder.SkewX, remainder.ScaleY, remainder.TranslateX, remainder.TranslateY), cancellationToken);
                    int restoreLeft = Math.Max(bitmap.Interior.Left, Math.Max(layerBounds.Left, ClampFloor(restoreBounds.Left, outputWidth)));
                    int restoreTop = Math.Max(bitmap.Interior.Top, Math.Max(layerBounds.Top, ClampFloor(restoreBounds.Top, outputHeight)));
                    int restoreRight = Math.Min(bitmap.Interior.Right, Math.Min(layerBounds.Right, ClampCeiling(restoreBounds.Right, outputWidth)));
                    int restoreBottom = Math.Min(bitmap.Interior.Bottom, Math.Min(layerBounds.Bottom, ClampCeiling(restoreBounds.Bottom, outputHeight)));
                    if (command.SourceOpacity.Value * foldedOpacity < 1)
                    {
                        SvgPaintBounds paintBounds = FilterResultBounds(binding, resource, SvgFilterBoundsKind.StoredImage);
                        restoreLeft = Math.Max(restoreLeft, ClampFloor(paintBounds.Left, outputWidth));
                        restoreTop = Math.Max(restoreTop, ClampFloor(paintBounds.Top, outputHeight));
                        restoreRight = Math.Max(restoreLeft, Math.Min(restoreRight, ClampCeiling(paintBounds.Right, outputWidth)));
                        restoreBottom = Math.Max(restoreTop, Math.Min(restoreBottom, ClampCeiling(paintBounds.Bottom, outputHeight)));
                    }
                    // An inverse tile enclosure can overlap the source even when
                    // its restored image misses this tile entirely.
                    if (restoreRight <= restoreLeft || restoreBottom <= restoreTop) return;
                    SvgRenderLimits layerLimits = limits;
                    SvgRoot borrowed = root with { Geometries = geometries, Commands = commands};
                    using RasterizedSurface image = Rasterize(borrowed, new(options.Viewport, options.OutputScale, SvgColor.Transparent, layerLimits, options.FontSnapshot),
                        budget, RasterSurfaceMode.PremultipliedFilter,
                        new RasterFrame((int)frameWidth, (int)frameHeight, 1, -left, 1, -top)
                        { SingleBitmap = true, FilterBinding = new(command.MaskContentIndex, local), SourcePlacement = placement, NativeSourceBounds = nativeSource, FilterSourceMap = new(arena.BindingAt(command.MaskContentIndex).Transform, layerMap, new SvgAffineTransform(inverse.ScaleX, inverse.SkewY, inverse.SkewX, inverse.ScaleY, inverse.TranslateX, inverse.TranslateY).ComposeNative(SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset))) }, cancellationToken);
                    // Native drawSpecial composes the stored image origin and
                    // transparent one-pixel outset before inverting its matrix.
                    // Subtracting that origin after inverse mapping changes phase.
                    if (image.DeferredImage is {} deferred)
                    {
                        SvgPaintBounds requested = SvgFilterPlayback.RoundLayerBounds(desired);
                        SvgPaintBounds logical = new(MathF.Max(nativeImage.Left, requested.Left), MathF.Max(nativeImage.Top, requested.Top),
                            MathF.Min(nativeImage.Right, requested.Right), MathF.Min(nativeImage.Bottom, requested.Bottom));
                        SvgPaintBounds existing = deferred.Clip;
                        deferred = deferred with { Clip = new(MathF.Max(existing.Left, logical.Left - (float)left), MathF.Max(existing.Top, logical.Top - (float)top),
                            MathF.Min(existing.Right, logical.Right - (float)left), MathF.Min(existing.Bottom, logical.Bottom - (float)top)) };
                        RestoreDeferredImage(image, deferred, remainder, nativeSource, new((int)left, (int)top), bitmap.Origin,
                            new(restoreLeft, restoreTop, restoreRight, restoreBottom));
                        return;
                    }
                    if (resource.Primitives.Items.Length > 0 && resource.Primitives.Items[^1].Kind == SvgFilterPrimitiveKind.Flood)
                    {
                        RestoreFlood(image, nativeImage, remainder, new((int)left, (int)top), new(restoreLeft, restoreTop, restoreRight, restoreBottom));
                        return;
                    }
                    SvgShaderMatrix subset = new(1, 0, nativeImage.Left - (nativeSource.Left - 1), 0, 1, nativeImage.Top - (nativeSource.Top - 1));
                    SvgShaderMatrix edgeDevice = SvgShaderMatrix.Concat(SourceDeviceMap(remainder, nativeSource, new(0, 0)), subset);
                    SvgShaderMatrix imageToDevice = SvgShaderMatrix.Concat(SourceDeviceMap(remainder, nativeSource, bitmap.Origin), subset);

                    // A crop discards padding knowledge. Only preserved padding can
                    // supply transparent-border sampling instead of an AA image edge.
                    bool imageEdges = storedImage.Storage == SvgFilterImageStorage.Cropped || RequiresImageEdges(remainder);
                    if (!imageEdges) imageToDevice = imageToDevice with
                    {
                        TranslateX = imageToDevice.TranslateX + (-imageToDevice.ScaleX - imageToDevice.SkewX),
                        TranslateY = imageToDevice.TranslateY + (-imageToDevice.SkewY - imageToDevice.ScaleY)
                    };
                    if (!imageToDevice.TryInvert(out SvgShaderMatrix imageInverse)) return;
                    bool floatingSamples = image.FilterColorSpace == SvgFilterColorSpace.LinearRgb
                        || image.SamplingColorFilter is SvgFilterDeferredColorFilter.SourceAlpha or SvgFilterDeferredColorFilter.LinearRgbRoundTrip || imageEdges
                        && (resource.Primitives.Items[^1].ColorSpace ?? resource.ColorSpace) == SvgFilterColorSpace.LinearRgb;
                    int opacity = Math.Clamp((int)MathF.Floor(command.SourceOpacity.Value * foldedOpacity * 255 + .5f), 0, 255);
                    bool opacityLayer = command.SourceOpacity.Value * foldedOpacity < 1;
                    Span<SvgDevicePoint> edgePoints = stackalloc SvgDevicePoint[4];
                    float imageWidth = nativeImage.Right - nativeImage.Left, imageHeight = nativeImage.Bottom - nativeImage.Top;
                    SvgAffineTransform edgeMap = new(edgeDevice.ScaleX, edgeDevice.SkewY, edgeDevice.SkewX, edgeDevice.ScaleY, edgeDevice.TranslateX, edgeDevice.TranslateY);
                    edgePoints[0] = SvgAuthoredPath.MapPoint(edgeMap, new(0, 0), new(0, 0));
                    edgePoints[1] = SvgAuthoredPath.MapPoint(edgeMap, new(imageWidth, 0), new(0, 0));
                    edgePoints[2] = SvgAuthoredPath.MapPoint(edgeMap, new(imageWidth, imageHeight), new(0, 0));
                    edgePoints[3] = SvgAuthoredPath.MapPoint(edgeMap, new(0, imageHeight), new(0, 0));
                    SvgAnalyticClipBounds edgeArea = new(restoreLeft, restoreTop, restoreRight, restoreBottom);
                    if (ActiveClipBounds(bitmap) is {} imageClip)
                        edgeArea = new(Math.Max(edgeArea.Left, imageClip.Left), Math.Max(edgeArea.Top, imageClip.Top),
                            Math.Min(edgeArea.Right, imageClip.Right), Math.Min(edgeArea.Bottom, imageClip.Bottom));
                    SvgAnalyticClipBounds dispatch = new(
                        (int)Math.Clamp(Math.Floor(Math.Min(Math.Min(edgePoints[0].X, edgePoints[1].X), Math.Min(edgePoints[2].X, edgePoints[3].X))), int.MinValue, int.MaxValue),
                        (int)Math.Clamp(Math.Floor(Math.Min(Math.Min(edgePoints[0].Y, edgePoints[1].Y), Math.Min(edgePoints[2].Y, edgePoints[3].Y))), int.MinValue, int.MaxValue),
                        (int)Math.Clamp(Math.Ceiling(Math.Max(Math.Max(edgePoints[0].X, edgePoints[1].X), Math.Max(edgePoints[2].X, edgePoints[3].X))), int.MinValue, int.MaxValue),
                        (int)Math.Clamp(Math.Ceiling(Math.Max(Math.Max(edgePoints[0].Y, edgePoints[1].Y), Math.Max(edgePoints[2].Y, edgePoints[3].Y))), int.MinValue, int.MaxValue));
                    Span<int> imageEnds = stackalloc int[1] { 4 };
                    SvgAnalyticContourPreparation edgePreparation = new(edgePoints, imageEnds, [], false, null, SvgPolygonMapping.NativeFillEdges,
                        [], [], dispatch, outputWidth, outputHeight);
                    bool edgeEligible = true;
                    // Image geometry is antialiased independently of clip type;
                    // only an actual AA clip selects its RLE accumulation path.
                    SvgAnalyticPaintClip edgePaintClip = activeViewport.HasMask && !activeViewport.IsBinary
                        ? SvgAnalyticPaintClip.Antialiased : SvgAnalyticPaintClip.Binary;
                    using SvgAnalyticCoverage edgeWalker = imageEdges ? SvgAnalyticCoverage.Prepare(ref edgePreparation, restoreLeft, restoreRight, restoreTop, restoreBottom,
                        new(restoreLeft, restoreRight), new SvgAnalyticClipContext(edgeArea), edgePaintClip,
                        budget, out edgeEligible, cancellationToken) : default;
                    if (!edgeEligible) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Affine filter image edges are outside the retained boundary.");
                    Span<byte> opacityPixel = stackalloc byte[4];
                    for (int y = restoreTop; y < restoreBottom; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (imageEdges)
                        {
                            edgeWalker.PrepareRow(y, cancellationToken);
                        }
                        for (int x = restoreLeft; x < restoreRight; x++)
                        {
                            if ((x & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                            int target = (y * outputWidth + x) * 4;
                            byte clipCoverage = !activeViewport.HasMask ? (byte)255 : activeViewport.Mask.GetAlphaAtOffset(outputWidth, target);
                            Span<byte> destinationPixel = destination.AsSpan(target, 4);
                            Span<byte> paintTarget = opacityLayer ? opacityPixel : destinationPixel;
                            if (opacityLayer) opacityPixel.Clear();
                            byte paintCoverage = opacityLayer ? (byte)255 : clipCoverage;
                            float px = x + .5f - bitmap.Origin.X, py = y + .5f - bitmap.Origin.Y;
                            // Packed sampling retains its map arithmetic; floating
                            // shader transfers use the fused map below.
                            float u = px * imageInverse.ScaleX + (py * imageInverse.SkewX + imageInverse.TranslateX);
                            float v = px * imageInverse.SkewY + (py * imageInverse.ScaleY + imageInverse.TranslateY);
                            if (floatingSamples)
                            {
                                (u, v) = imageInverse.Map(px, py);
                                SvgCpuBitmapOrigin sampleOrigin = new((int)nativeImage.Left - 1 - (int)left, (int)nativeImage.Top - 1 - (int)top);
                                if (imageEdges)
                                {
                                    u = Math.Clamp(u, .5f, imageWidth - .5f);
                                    v = Math.Clamp(v, .5f, imageHeight - .5f);
                                    sampleOrigin = new((int)nativeImage.Left - (int)left, (int)nativeImage.Top - (int)top);
                                }
                                SvgShaderPaint floating = SvgShaderPaint.SampleAffine(image.Pixels, image.Width, image.Height, u, v,
                                    sampleOrigin, 1f);
                                // Operating-space transfers precede the store. A retained
                                // alpha shader only requires floating sampling.
                                if (image.FilterColorSpace == SvgFilterColorSpace.LinearRgb)
                                    floating = SvgFilterColorTransfer.Convert(floating, SvgFilterColorSpace.Srgb);
                                else if (image.SamplingColorFilter != SvgFilterDeferredColorFilter.SourceAlpha)
                                    floating = SvgFilterColorTransfer.Convert(SvgFilterColorTransfer.Convert(floating, SvgFilterColorSpace.LinearRgb), SvgFilterColorSpace.Srgb);
                                if (imageEdges) edgeWalker.PaintPixel(x - restoreLeft, paintTarget, SvgAnalyticPaint.Shader(floating), SvgAnalyticClipCoverage.FromMask(paintCoverage));
                                else floating.Blend(paintTarget, paintCoverage);
                            }
                            else
                            {
                                // Clamp at the native subset; missing compact-frame
                                // texels stay transparent, even when its border is colored.
                                SvgConstantShaderPaint sample = imageEdges
                                    ? SvgConstantShaderPaint.SampleAffine(image.Pixels, image.Width, image.Height,
                                        Math.Clamp(u, .5f, imageWidth - .5f), Math.Clamp(v, .5f, imageHeight - .5f), true,
                                        new((int)nativeImage.Left - (int)left, (int)nativeImage.Top - (int)top))
                                    : SvgConstantShaderPaint.SampleAffine(image.Pixels, image.Width, image.Height, u, v, true,
                                        new((int)nativeImage.Left - 1 - (int)left, (int)nativeImage.Top - 1 - (int)top));
                                if (imageEdges) edgeWalker.PaintPixel(x - restoreLeft, paintTarget, SvgAnalyticPaint.Constant(sample), SvgAnalyticClipCoverage.FromMask(paintCoverage));
                                else sample.Blend(paintTarget, paintCoverage);
                            }
                            if (opacityLayer)
                            {
                                // The enclosing layer stores converted/covered pixels
                                // before packed paint-alpha modulation.
                                if (!activeViewport.HasMask) RestoreOpacityLayerPixel(destinationPixel, opacityPixel, (byte)opacity);
                                else BlendClippedLayerPixel(destinationPixel, opacityPixel, (byte)opacity, 255, clipCoverage);
                            }
                        }
                    }
                    return;
                }

                // Image origins concatenate after the destination bitmap's origin.
                // This pure setup owns no storage; the caller admits its finite map.
                static SvgShaderMatrix SourceDeviceMap(SvgShaderMatrix transform, SvgPaintBounds source, SvgCpuBitmapOrigin origin)
                {
                    SvgAffineTransform placed = new SvgAffineTransform(transform.ScaleX, transform.SkewY, transform.SkewX, transform.ScaleY,
                        transform.TranslateX - origin.X, transform.TranslateY - origin.Y)
                        .ComposeNative(SvgAffineTransform.FromTranslation(source.Left - 1, source.Top - 1));
                    return new((float)placed.A, (float)placed.C, (float)placed.E, (float)placed.B, (float)placed.D, (float)placed.F);
                }

                // Native min/max affine singular scales (SkMatrix.cpp) and the
                // decal boundary criterion (SkImageFilterTypes.cpp), at the source pin.
                // Pure bounded setup; no storage or cancellation ownership transfers.
                static bool RequiresImageEdges(SvgShaderMatrix map)
                {
                    float a = map.ScaleX * map.ScaleX + map.SkewY * map.SkewY;
                    float b = map.ScaleX * map.SkewX + map.ScaleY * map.SkewY;
                    float c = map.SkewX * map.SkewX + map.ScaleY * map.ScaleY;
                    float square = b * b;
                    float minimum, maximum;
                    if (square <= 1f / (4096f * 4096f)) { minimum = MathF.Min(a, c); maximum = MathF.Max(a, c); }
                    else
                    {
                        float difference = a - c, middle = (a + c) / 2;
                        float radius = MathF.Sqrt(difference * difference + 4 * square) / 2;
                        minimum = middle - radius; maximum = middle + radius;
                    }
                    return !float.IsFinite(minimum) || !float.IsFinite(maximum)
                        || !(MathF.Abs(MathF.Sqrt(MathF.Max(0, minimum)) - 1) <= .2f)
                        || !(MathF.Abs(MathF.Sqrt(MathF.Max(0, maximum)) - 1) <= .2f);
                }

                // Native floods fill with a uniform shader through the region's
                // AA clip. Accumulate clip coverage before applying colored alpha.
                void RestoreFlood(RasterizedSurface image, SvgPaintBounds bounds, SvgShaderMatrix transform,
                    SvgCpuBitmapOrigin origin, SvgAnalyticClipBounds area)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int sx = (int)Math.Clamp((double)bounds.Left - origin.X, 0, image.Width - 1);
                    int sy = (int)Math.Clamp((double)bounds.Top - origin.Y, 0, image.Height - 1);
                    int at = (sy * image.Width + sx) * 4;
                    int opacity = Math.Clamp((int)MathF.Floor(command.SourceOpacity.Value * foldedOpacity * 255 + .5f), 0, 255);
                    SvgConstantShaderPaint paint = new(image.Pixels[at], image.Pixels[at + 1], image.Pixels[at + 2], image.Pixels[at + 3]);
                    bool opacityLayer = command.SourceOpacity.Value * foldedOpacity < 1;
                    Span<SvgDevicePoint> points = stackalloc SvgDevicePoint[4];
                    points[0] = Map(bounds.Left, bounds.Top); points[1] = Map(bounds.Right, bounds.Top);
                    points[2] = Map(bounds.Right, bounds.Bottom); points[3] = Map(bounds.Left, bounds.Bottom);
                    bool clipped = !ContainsOutput(points, area);
                    SvgAnalyticContourPreparation preparation = new(points, [4], [], false, null, SvgPolygonMapping.NativeFillEdges,
                        [], [], area, outputWidth, outputHeight);
                    scoped SvgAnalyticCoverage walker = default;
                    try
                    {
                        if (clipped)
                        {
                            walker = SvgAnalyticCoverage.Prepare(ref preparation, area.Left, area.Right, area.Top, area.Bottom,
                                new(area.Left, area.Right), new SvgAnalyticClipContext(area), SvgAnalyticPaintClip.Antialiased,
                                budget, out bool eligible, cancellationToken);
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!eligible) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Affine flood clip edges are outside the retained boundary.");
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        Span<byte> clipPixel = stackalloc byte[4];
                        for (int y = area.Top; y < area.Bottom; y++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (clipped)
                            {
                                walker.PrepareRow(y, cancellationToken);
                                cancellationToken.ThrowIfCancellationRequested();
                            }
                            for (int x = area.Left; x < area.Right; x++)
                            {
                                if ((x & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                                byte coverage = 255;
                                if (clipped)
                                {
                                    clipPixel.Clear();
                                    walker.PaintPixel(x - area.Left, clipPixel, SvgAnalyticPaint.Solid(new(0, 0, 0, 255)), SvgAnalyticClipCoverage.Full);
                                    coverage = clipPixel[3];
                                }
                                if (activeViewport.HasMask) coverage = (byte)((coverage * activeViewport.Mask.GetAlphaAtOffset(outputWidth, (y * outputWidth + x) * 4) + 127) / 255);
                                Span<byte> target = destination.AsSpan((y * outputWidth + x) * 4, 4);
                                if (opacityLayer)
                                {
                                    clipPixel.Clear();
                                    paint.Blend(clipPixel, coverage);
                                    RestoreOpacityLayerPixel(target, clipPixel, (byte)opacity);
                                }
                                else paint.Blend(target, coverage);
                            }
                        }
                    }
                    finally { walker.Dispose(); }

                    SvgDevicePoint Map(float x, float y) => SvgAuthoredPath.MapPoint(
                        new(transform.ScaleX, transform.SkewY, transform.SkewX, transform.ScaleY, transform.TranslateX, transform.TranslateY), new(x, y), new(0, 0));
                    // Scalar affine form of SkRectPriv::QuadContainsRectMask.
                    // The inset matches native coordinate rounding, not pixel tolerance.
                    static bool ContainsOutput(ReadOnlySpan<SvgDevicePoint> corners, SvgAnalyticClipBounds output)
                    {
                        const float inset = 1e-3f;
                        float a0 = (float)corners[0].Y - (float)corners[1].Y;
                        float b0 = (float)corners[1].X - (float)corners[0].X;
                        float a1 = (float)corners[1].Y - (float)corners[2].Y;
                        float b1 = (float)corners[2].X - (float)corners[1].X;
                        float sign = a0 * b1 - b0 * a1 < 0 ? -1 : 1;
                        for (int edge = 0; edge < 4; edge++)
                        {
                            SvgDevicePoint p = corners[edge], q = corners[(edge + 1) & 3];
                            float a = (float)p.Y - (float)q.Y, b = (float)q.X - (float)p.X;
                            float c = (float)p.X * (float)q.Y - (float)p.Y * (float)q.X;
                            for (int corner = 0; corner < 4; corner++)
                            {
                                float x = corner is 1 or 2 ? output.Right - inset : output.Left + inset;
                                float y = corner >= 2 ? output.Bottom - inset : output.Top + inset;
                                if (!(sign * (a * x + b * y + c) >= 0)) return false;
                            }
                        }
                        return true;
                    }
                }

                // Restores one retained source image; storage stays with its captured
                // surface. Native image/crop coverage share the existing bounded walker.
                void RestoreDeferredImage(RasterizedSurface image, SvgFilterDeferredImage deferred, SvgShaderMatrix transform,
                    SvgPaintBounds nativeSource, SvgCpuBitmapOrigin frameOrigin, SvgCpuBitmapOrigin deviceOrigin, SvgAnalyticClipBounds area)
                {
                    SvgShaderMatrix device = SourceDeviceMap(transform, nativeSource, new(0, 0));
                    SvgShaderMatrix forward = SvgShaderMatrix.Concat(device, deferred.ImageToSource);
                    SvgShaderMatrix samplingForward = SvgShaderMatrix.Concat(SourceDeviceMap(transform, nativeSource, deviceOrigin), deferred.ImageToSource);
                    bool linear = deferred.Sampling == SvgFilterImageSampling.Linear;
                    bool floatingSamples = linear && (resource.Primitives.Items[^1].ColorSpace ?? resource.ColorSpace) == SvgFilterColorSpace.LinearRgb;
                    SvgShaderMatrix sampling = linear ? samplingForward with
                    {
                        TranslateX = samplingForward.TranslateX + (-samplingForward.ScaleX - samplingForward.SkewX),
                        TranslateY = samplingForward.TranslateY + (-samplingForward.SkewY - samplingForward.ScaleY)
                    } : samplingForward;
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
                    // A pending source color fills the layer crop. Draw that
                    // crop once; finite materialized images retain separate AA.
                    if (deferred.ColorFilter == SvgFilterDeferredColorFilter.UniformFill)
                    {
                        points[0] = MapPoint(device, clipLeft, clipTop); points[1] = MapPoint(device, clipRight, clipTop);
                        points[2] = MapPoint(device, clipRight, clipBottom); points[3] = MapPoint(device, clipLeft, clipBottom);
                        cropped = false;
                    }
                    Span<SvgDevicePoint> cropPoints = stackalloc SvgDevicePoint[4];
                    SvgAnalyticClipBounds construction = new(Math.Max(layerBounds.Left, bitmap.BitmapClip.Left), Math.Max(layerBounds.Top, bitmap.BitmapClip.Top),
                        Math.Min(layerBounds.Right, bitmap.BitmapClip.Right), Math.Min(layerBounds.Bottom, bitmap.BitmapClip.Bottom));
                    // An enclosing opacity layer owns a bounded destination;
                    // construct its filter/image clips within that destination.
                    if (command.SourceOpacity.Value * foldedOpacity < 1)
                        construction = new(Math.Max(construction.Left, area.Left), Math.Max(construction.Top, area.Top),
                            Math.Min(construction.Right, area.Right), Math.Min(construction.Bottom, area.Bottom));
                    SvgAnalyticClipBounds imageClip = construction;
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
                        imageClip = new(Math.Max(construction.Left, (int)MathF.Floor(leftEdge)), Math.Max(construction.Top, (int)MathF.Floor(topEdge)),
                            Math.Min(construction.Right, (int)MathF.Ceiling(rightEdge)), Math.Min(construction.Bottom, (int)MathF.Ceiling(bottomEdge)));
                    }
                    if (imageClip.Right <= imageClip.Left || imageClip.Bottom <= imageClip.Top) return;
                    SvgAnalyticClipBounds cropConstruction = imageClip;
                    cancellationToken.ThrowIfCancellationRequested();
                    SvgAnalyticContourPreparation imagePreparation = new(points, [4], [], false, null, SvgPolygonMapping.NativeFillEdges,
                        [], [], area, outputWidth, outputHeight);
                    scoped SvgAnalyticCoverage walker = default, cropWalker = default;
                    RenderBudget.BudgetReservation? cropOwner = null;
                    byte[] cropRow = [];
                    Span<byte> edgePixel = stackalloc byte[4];
                    try
                    {
                        if (cropped)
                        {
                            // Native AA clips trim zero-coverage rows and columns before
                            // their binary bounds constrain the later image edge builder.
                            // Measure through the existing walker without retaining a mask.
                            SvgAnalyticContourPreparation boundsPreparation = new(cropPoints, [4], [], false, null, SvgPolygonMapping.NativeFillEdges,
                                [], [], area, outputWidth, outputHeight);
                            scoped SvgAnalyticCoverage boundsWalker = default;
                            try
                            {
                                boundsWalker = SvgAnalyticCoverage.Prepare(ref boundsPreparation, cropConstruction.Left, cropConstruction.Right,
                                    cropConstruction.Top, cropConstruction.Bottom, new(cropConstruction.Left, cropConstruction.Right),
                                    new SvgAnalyticClipContext(cropConstruction, bitmap.Origin), SvgAnalyticPaintClip.Antialiased,
                                    budget, out bool eligible, cancellationToken);
                                cancellationToken.ThrowIfCancellationRequested();
                                if (!eligible) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Deferred affine crop bounds are outside the retained boundary.");
                                int leftEdge = cropConstruction.Right, topEdge = cropConstruction.Bottom;
                                int rightEdge = cropConstruction.Left, bottomEdge = cropConstruction.Top;
                                for (int y = cropConstruction.Top; y < cropConstruction.Bottom; y++)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    boundsWalker.PrepareRow(y, cancellationToken);
                                    cancellationToken.ThrowIfCancellationRequested();
                                    for (int x = cropConstruction.Left; x < cropConstruction.Right; x++)
                                    {
                                        edgePixel.Clear();
                                        boundsWalker.PaintPixel(x - cropConstruction.Left, edgePixel,
                                            SvgAnalyticPaint.Solid(new(0, 0, 0, 255)), SvgAnalyticClipCoverage.Full);
                                        if (edgePixel[3] == 0) continue;
                                        leftEdge = Math.Min(leftEdge, x); topEdge = Math.Min(topEdge, y);
                                        rightEdge = Math.Max(rightEdge, x + 1); bottomEdge = Math.Max(bottomEdge, y + 1);
                                    }
                                }
                                if (rightEdge <= leftEdge || bottomEdge <= topEdge) return;
                                imageClip = new(leftEdge, topEdge, rightEdge, bottomEdge);
                            }
                            finally { boundsWalker.Dispose(); }
                        }
                        if (!linear)
                        {
                            SvgAnalyticPaintClip imagePaintClip = deferred.Constant is not null || cropped || activeViewport.HasMask && !activeViewport.IsBinary
                                ? SvgAnalyticPaintClip.Antialiased : SvgAnalyticPaintClip.Binary;
                            walker = SvgAnalyticCoverage.Prepare(ref imagePreparation, area.Left, area.Right, area.Top, area.Bottom,
                                new(area.Left, area.Right), new SvgAnalyticClipContext(imageClip, bitmap.Origin), imagePaintClip,
                                budget, out bool eligible, cancellationToken);
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!eligible) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Deferred affine image edges are outside the retained boundary.");
                        }
                        if (cropped)
                        {
                            SvgAnalyticContourPreparation cropPreparation = new(cropPoints, [4], [], false, null, SvgPolygonMapping.NativeFillEdges,
                                [], [], area, outputWidth, outputHeight);
                            cropWalker = SvgAnalyticCoverage.Prepare(ref cropPreparation, area.Left, area.Right, area.Top, area.Bottom,
                                new(area.Left, area.Right), new SvgAnalyticClipContext(cropConstruction, bitmap.Origin), SvgAnalyticPaintClip.Antialiased,
                                budget, out bool eligible, cancellationToken);
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!eligible) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Deferred affine crop edges are outside the retained boundary.");
                            int rowBytes = checked((area.Right - area.Left) * 4);
                            cropOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<byte>(rowBytes), SvgMemoryCategory.Surface);
                            cropRow = new byte[rowBytes];
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        int opacity = Math.Clamp((int)MathF.Floor(command.SourceOpacity.Value * foldedOpacity * 255 + .5f), 0, 255);
                        for (int y = area.Top; y < area.Bottom; y++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!linear)
                            {
                                walker.PrepareRow(y, cancellationToken);
                                cancellationToken.ThrowIfCancellationRequested();
                            }
                            if (cropped)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                cropRow.AsSpan().Clear();
                                cropWalker.PrepareRow(y, cancellationToken);
                                cancellationToken.ThrowIfCancellationRequested();
                                for (int x = area.Left; x < area.Right; x++)
                                    cropWalker.PaintPixel(x - area.Left, cropRow.AsSpan((x - area.Left) * 4, 4),
                                        SvgAnalyticPaint.Solid(new(0, 0, 0, 255)), SvgAnalyticClipCoverage.Full);
                            }
                            for (int x = area.Left; x < area.Right; x++)
                            {
                                if ((x & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                                float px = x + .5f - deviceOrigin.X, py = y + .5f - deviceOrigin.Y;
                                float u = px * inverse.ScaleX + (py * inverse.SkewX + inverse.TranslateX);
                                float v = px * inverse.SkewY + (py * inverse.ScaleY + inverse.TranslateY);
                                if (image.FilterColorSpace == SvgFilterColorSpace.LinearRgb
                                    || deferred.ColorFilter is SvgFilterDeferredColorFilter.SourceAlpha or SvgFilterDeferredColorFilter.LinearRgbRoundTrip)
                                {
                                    (u, v) = inverse.Map(px, py);
                                    SvgShaderPaint sampled;
                                    if (deferred.Constant is {} texel)
                                        sampled = texel;
                                    else if (linear)
                                        sampled = SvgShaderPaint.SampleAffine(image.Pixels, image.Width, image.Height, u, v,
                                            new((int)deferred.SourceBounds.Left - 1, (int)deferred.SourceBounds.Top - 1), 1f);
                                    else
                                    {
                                        int sx = (int)Math.Clamp(MathF.Floor(u), 0, sourceWidth - 1) + (int)deferred.SourceBounds.Left;
                                        int sy = (int)Math.Clamp(MathF.Floor(v), 0, sourceHeight - 1) + (int)deferred.SourceBounds.Top;
                                        int at = (sy * image.Width + sx) * 4;
                                        sampled = (uint)sx < (uint)image.Width && (uint)sy < (uint)image.Height
                                            ? new(image.Pixels[at] / 255f, image.Pixels[at + 1] / 255f, image.Pixels[at + 2] / 255f, image.Pixels[at + 3] / 255f) : default;
                                    }
                                    if (image.FilterColorSpace == SvgFilterColorSpace.LinearRgb)
                                        sampled = SvgFilterColorTransfer.Convert(sampled, SvgFilterColorSpace.Srgb);
                                    else if (deferred.ColorFilter == SvgFilterDeferredColorFilter.LinearRgbRoundTrip)
                                        sampled = SvgFilterColorTransfer.Convert(SvgFilterColorTransfer.Convert(sampled, SvgFilterColorSpace.LinearRgb), SvgFilterColorSpace.Srgb);
                                    byte sampledCoverage = cropped ? cropRow[(x - area.Left) * 4 + 3] : (byte)255;
                                    if (!linear)
                                    {
                                        edgePixel.Clear();
                                        walker.PaintPixel(x - area.Left, edgePixel, SvgAnalyticPaint.Solid(new(0, 0, 0, 255)), SvgAnalyticClipCoverage.Full);
                                        sampledCoverage = (byte)((sampledCoverage * edgePixel[3] + 127) / 255);
                                    }
                                    if (activeViewport.HasMask) sampledCoverage = (byte)((sampledCoverage * activeViewport.Mask.GetAlphaAtOffset(outputWidth, (y * outputWidth + x) * 4) + 127) / 255);
                                    Span<byte> opacityTarget = destination.AsSpan((y * outputWidth + x) * 4, 4);
                                    if (command.SourceOpacity.Value * foldedOpacity < 1)
                                    {
                                        edgePixel.Clear();
                                        sampled.Blend(edgePixel, sampledCoverage);
                                        RestoreOpacityLayerPixel(opacityTarget, edgePixel, (byte)opacity);
                                    }
                                    else sampled.Blend(opacityTarget, sampledCoverage);
                                    continue;
                                }
                                if (floatingSamples)
                                {
                                    // The linear-RGB filter boundary retains floating
                                    // samples through interpolation and the final store.
                                    (u, v) = inverse.Map(px, py);
                                    byte clipCoverage = cropped ? cropRow[(x - area.Left) * 4 + 3] : (byte)255;
                                    if (activeViewport.HasMask) clipCoverage = (byte)((clipCoverage * activeViewport.Mask.GetAlphaAtOffset(outputWidth, (y * outputWidth + x) * 4) + 127) / 255);
                                    SvgShaderPaint sampled = SvgShaderPaint.SampleAffine(image.Pixels, image.Width, image.Height, u, v,
                                        new((int)deferred.SourceBounds.Left - 1, (int)deferred.SourceBounds.Top - 1), 1f);
                                    Span<byte> opacityTarget = destination.AsSpan((y * outputWidth + x) * 4, 4);
                                    if (command.SourceOpacity.Value * foldedOpacity < 1)
                                    {
                                        edgePixel.Clear();
                                        sampled.Blend(edgePixel, clipCoverage);
                                        RestoreOpacityLayerPixel(opacityTarget, edgePixel, (byte)opacity);
                                    }
                                    else sampled.Blend(opacityTarget, clipCoverage);
                                    continue;
                                }
                                SvgConstantShaderPaint sample;
                                if (deferred.Constant is {} constant) sample = constant.AsConstant(1f);
                                else if (linear)
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
                                byte coverage = cropped ? cropRow[(x - area.Left) * 4 + 3] : (byte)255;
                                if (activeViewport.HasMask) coverage = (byte)((coverage * activeViewport.Mask.GetAlphaAtOffset(outputWidth, (y * outputWidth + x) * 4) + 127) / 255);
                                Span<byte> target = destination.AsSpan((y * outputWidth + x) * 4, 4);
                                if (command.SourceOpacity.Value * foldedOpacity < 1)
                                {
                                    edgePixel.Clear();
                                    if (linear) sample.Blend(edgePixel, coverage);
                                    else walker.PaintPixel(x - area.Left, edgePixel, SvgAnalyticPaint.Constant(sample), SvgAnalyticClipCoverage.FromMask(coverage));
                                    RestoreOpacityLayerPixel(target, edgePixel, (byte)opacity);
                                }
                                else if (linear) sample.Blend(target, coverage);
                                else walker.PaintPixel(x - area.Left, target, SvgAnalyticPaint.Constant(sample), SvgAnalyticClipCoverage.FromMask(coverage));
                            }
                        }
                    }
                    finally { cropOwner?.Dispose(); cropWalker.Dispose(); walker.Dispose(); }

                    static SvgDevicePoint MapPoint(SvgShaderMatrix matrix, float x, float y)
                        => SvgAuthoredPath.MapPoint(new(matrix.ScaleX, matrix.SkewY, matrix.SkewX, matrix.ScaleY, matrix.TranslateX, matrix.TranslateY), new(x, y), new(0, 0));
                }

                static SvgPaintBounds Union(SvgPaintBounds? prior, SvgPaintBounds next)
                    => prior is {} box ? new(MathF.Min(box.Left, next.Left), MathF.Min(box.Top, next.Top), MathF.Max(box.Right, next.Right), MathF.Max(box.Bottom, next.Bottom)) : next;
            }

            SvgPaintBounds PlaceImageHintBounds(SvgPaintBounds? bounds, bool missing, SourceImageBoundsKind kind)
            {
                if (bounds is not {} box) return visual;
                if (sourcePlacement != SvgAffineTransform.Identity)
                {
                    box = kind == SourceImageBoundsKind.Element ? box.MapLayout(sourcePlacement, cancellationToken)
                        : box.Map(sourcePlacement, cancellationToken);
                }
                return missing ? ImageHint(visual, box) : box;
            }

            // Native saveLayer encloses the output hint in the source parameter
            // grid before mapping it into device pixels. Ink capture stays separate.
            SvgPaintBounds RoundOutputHint(SvgPaintBounds output)
            {
                SvgAffineTransform transform = MapFilterParameterTransform(binding.Transform);
                float sx = (float)transform.A, sy = (float)transform.D;
                float tx = (float)transform.E, ty = (float)transform.F;
                if (sx == 0 || sy == 0) return output;
                float x1 = (output.Left - tx) / sx, x2 = (output.Right - tx) / sx;
                float y1 = (output.Top - ty) / sy, y2 = (output.Bottom - ty) / sy;
                float left = MathF.Floor(MathF.Min(x1, x2)) * sx + tx, right = MathF.Ceiling(MathF.Max(x1, x2)) * sx + tx;
                float top = MathF.Floor(MathF.Min(y1, y2)) * sy + ty, bottom = MathF.Ceiling(MathF.Max(y1, y2)) * sy + ty;
                return new(MathF.Min(left, right), MathF.Min(top, bottom), MathF.Max(left, right), MathF.Max(top, bottom));
            }

            static SvgPaintBounds ImageHint(SvgPaintBounds recorded, SvgPaintBounds output)
            {
                if (recorded.Right <= recorded.Left || recorded.Bottom <= recorded.Top) return output;
                if (output.Right <= output.Left || output.Bottom <= output.Top) return recorded;
                return new(MathF.Min(recorded.Left, output.Left), MathF.Min(recorded.Top, output.Top),
                    MathF.Max(recorded.Right, output.Right), MathF.Max(recorded.Bottom, output.Bottom));
            }

            // Shader origins belong to the requested native source image, while
            // compact ink captures retain their independent allocation bounds.
            static SvgCpuBitmapOrigin SourceImageOrigin(SvgPaintBounds hint, SvgPaintBounds needed)
            {
                SvgPaintBounds image = SvgFilterPlayback.RequestedSourceImageBounds(hint, needed);
                double left = (double)image.Left - 1, top = (double)image.Top - 1;
                if (left < int.MinValue || top < int.MinValue)
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Filter source origin exceeds the coordinate range.");
                return new((int)left, (int)top);
            }

            // Native source devices belong to individual compositor bitmaps.
            // Reuse ordinary filter playback so graph stages, regions and limits
            // retain one implementation; the child binding prevents recursive tiling.
            bool TryRenderFractionalOffsetBitmaps()
            {
                if (rasterGrid.ContextCount <= 1 || frame?.FilterSourceMap is not null
                    || frame?.FilterBinding is {} bitmapBinding && bitmapBinding.Index == command.MaskContentIndex)
                    return false;
                bool fractional = false;
                foreach (SvgFilterPrimitive item in resource.Primitives.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item.Kind != SvgFilterPrimitiveKind.Offset) continue;
                    double dx = item.OffsetX * primitiveMap.ScaleX, dy = item.OffsetY * primitiveMap.ScaleY;
                    fractional |= dx != Math.Truncate(dx) || dy != Math.Truncate(dy);
                }
                if (!fractional) return false;
                int commandCount = end - first + 2;
                cancellationToken.ThrowIfCancellationRequested();
                using RenderBudget.BudgetReservation commandsOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(commandCount), SvgMemoryCategory.Scene);
                SvgDrawCommand[] commands = new SvgDrawCommand[commandCount];
                commands[0] = command;
                for (int index = first; index <= end; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    commands[index - first + 1] = root.Commands[index];
                }
                SvgRoot scene = root with { Commands = commands};
                SvgAnalyticClipBounds requested = new(Math.Max(layerBounds.Left, ClampFloor(region.Left, outputWidth)), Math.Max(layerBounds.Top, ClampFloor(region.Top, outputHeight)),
                    Math.Min(layerBounds.Right, ClampCeiling(region.Right, outputWidth)), Math.Min(layerBounds.Bottom, ClampCeiling(region.Bottom, outputHeight)));
                if (activeViewport.Bounds is {} viewport)
                    requested = new(Math.Max(requested.Left, viewport.Left), Math.Max(requested.Top, viewport.Top), Math.Min(requested.Right, viewport.Right), Math.Min(requested.Bottom, viewport.Bottom));
                if (requested.Right <= requested.Left || requested.Bottom <= requested.Top) return true;
                foreach (SvgCpuRasterContext bitmap in rasterGrid.GetContexts(requested))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SvgAnalyticClipBounds clip = new(Math.Max(requested.Left, bitmap.BitmapClip.Left), Math.Max(requested.Top, bitmap.BitmapClip.Top),
                        Math.Min(requested.Right, bitmap.BitmapClip.Right), Math.Min(requested.Bottom, bitmap.BitmapClip.Bottom));
                    if (activeViewport.Coverage is {} coverage && coverage.TryGetBounds(bitmap.BitmapClip, out SvgAnalyticClipBounds bounded))
                        clip = new(Math.Max(clip.Left, bounded.Left), Math.Max(clip.Top, bounded.Top), Math.Min(clip.Right, bounded.Right), Math.Min(clip.Bottom, bounded.Bottom));
                    if (clip.Right <= clip.Left || clip.Bottom <= clip.Top) continue;
                    SvgRenderLimits original = options.Limits;
                    SvgRenderLimits replayLimits = original;
                    SvgRenderOptions replayOptions = new(options.Viewport, options.OutputScale, SvgColor.Transparent, replayLimits, options.FontSnapshot);
                    int ox = bitmap.BitmapClip.Left, oy = bitmap.BitmapClip.Top;
                    using RasterizedSurface result = Rasterize(scene, replayOptions, budget, RasterSurfaceMode.PremultipliedFilter,
                        new RasterFrame(bitmap.BitmapClip.Right - ox, bitmap.BitmapClip.Bottom - oy,
                            horizontalScale, horizontalOffset - ox, verticalScale, verticalOffset - oy)
                        { SingleBitmap = true, ContentClip = new(clip.Left - ox, clip.Top - oy, clip.Right - ox, clip.Bottom - oy),
                            ParentOrigin = bitmap.Origin, CaptureOrigin = new(ox, oy), FilterBinding = new(command.MaskContentIndex, binding),
                            SourcePlacement = sourcePlacement, NativeCapturePlacement = PlaceNativeSource(new(ox, oy)) }, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    int x1 = Math.Max(clip.Left, bitmap.Interior.Left), y1 = Math.Max(clip.Top, bitmap.Interior.Top);
                    int x2 = Math.Min(clip.Right, bitmap.Interior.Right), y2 = Math.Min(clip.Bottom, bitmap.Interior.Bottom);
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int y = y1; y < y2; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        for (int x = x1; x < x2; x++)
                        {
                            int at = ((y - oy) * result.Width + x - ox) * 4;
                            int destinationIndex = (y * outputWidth + x) * 4;
                            byte alpha = !activeViewport.HasMask ? (byte)255 : activeViewport.Mask.GetAlphaAtOffset(outputWidth, destinationIndex);
                            if (alpha != 0) BlendClippedLayerPixel(destination.AsSpan(destinationIndex, 4), result.Pixels.AsSpan(at, 4), 255, 255, alpha);
                        }
                    }
                }
                return true;
            }

            // The image content is independent of its scratch padding and origin.
            // Preserve one transparent guard pixel on each owned capture surface.
            static SvgAnalyticClipBounds SourceContentClip(SvgPaintBounds content, SvgCpuBitmapOrigin origin, int width, int height)
            {
                int left = Math.Max(1, ClampFloor((double)content.Left - origin.X, width - 1));
                int top = Math.Max(1, ClampFloor((double)content.Top - origin.Y, height - 1));
                int right = Math.Max(left, Math.Min(width - 1, ClampCeiling((double)content.Right - origin.X, width - 1)));
                int bottom = Math.Max(top, Math.Min(height - 1, ClampCeiling((double)content.Bottom - origin.Y, height - 1)));
                return new(left, top, right, bottom);
            }

            bool TryRenderOffsetSources()
            {
                ReadOnlySpan<SvgFilterPrimitive> primitives = resource.Primitives.Items;
                if (primitives.Length == 0) return false;
                for (int index = 0; index < primitives.Length; index++)
                {
                    SvgFilterPrimitive primitive = primitives[index];
                    cancellationToken.ThrowIfCancellationRequested();
                    if (primitive.Kind == SvgFilterPrimitiveKind.Offset)
                    {
                        double x = primitive.OffsetX * primitiveMap.ScaleX, y = primitive.OffsetY * primitiveMap.ScaleY;
                        bool integer = x == Math.Truncate(x) && y == Math.Truncate(y);
                        if (!integer && (frame?.FilterSourceMap is not null || !SvgFilterPlayback.IsNearestSpriteTranslation((float)x) || !SvgFilterPlayback.IsNearestSpriteTranslation((float)y))) return false;
                    }
                    if (primitive.Region is not null || (primitive.Kind != SvgFilterPrimitiveKind.Offset
                        && (index != primitives.Length - 1 || primitive.Kind != SvgFilterPrimitiveKind.Merge))) return false;
                }
                using BudgetedParserList<SvgOffsetFilterPlacement> placements = new(budget, SvgMemoryCategory.Temporary, cancellationToken);
                SvgFilterPrimitive final = primitives[^1];
                // A single-input merge retains its own operating space; only
                // reachable offset branches may add another transfer requirement.
                bool hasLinearTransfer = (final.ColorSpace ?? resource.ColorSpace) == SvgFilterColorSpace.LinearRgb;
                if (final.Kind == SvgFilterPrimitiveKind.Merge)
                    foreach (SvgFilterInput input in resource.MergeInputs.Items.AsSpan(final.MergeFirst, final.MergeCount)) Add(input);
                else Add(new(primitives.Length - 1));
                if (placements.Count == 0 || end == first) return true;
                if (frame?.FilterBinding is {} alphaOutput && alphaOutput.Index == command.MaskContentIndex)
                    filterSamplingColorFilter = placements.Count != 1 ? SvgFilterDeferredColorFilter.None
                        : placements[0].AlphaOnly ? SvgFilterDeferredColorFilter.SourceAlpha
                        : hasLinearTransfer ? SvgFilterDeferredColorFilter.LinearRgbRoundTrip : SvgFilterDeferredColorFilter.None;
                using RenderBudget.BudgetReservation commandOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(end - first), SvgMemoryCategory.Scene);
                SvgDrawCommand[] draws = new SvgDrawCommand[end - first];
                cancellationToken.ThrowIfCancellationRequested();
                for (int index = first; index < end; index++) draws[index - first] = root.Commands[index];
                SvgRoot scene = root with { Commands = draws};
                // The native saveLayer hint includes drawable source and filtered
                // output bounds. A branch without source ink may still enlarge the
                // shared capture; pruning it changes clipped curve construction.
                double captureLeft = sourceLeft, captureTop = sourceTop, captureRight = sourceRight, captureBottom = sourceBottom;
                for (int placementIndex = 0; placementIndex < placements.Count; placementIndex++)
                {
                    SvgOffsetFilterPlacement placement = placements[placementIndex];
                    cancellationToken.ThrowIfCancellationRequested();
                    double x1 = Math.Max(sourceLeft + placement.X, placement.Clip.Left), y1 = Math.Max(sourceTop + placement.Y, placement.Clip.Top);
                    double x2 = Math.Min(sourceRight + placement.X, placement.Clip.Right), y2 = Math.Min(sourceBottom + placement.Y, placement.Clip.Bottom);
                    if (x2 <= x1 || y2 <= y1) continue;
                    captureLeft = Math.Min(captureLeft, x1); captureTop = Math.Min(captureTop, y1);
                    captureRight = Math.Max(captureRight, x2); captureBottom = Math.Max(captureBottom, y2);
                }
                SvgAnalyticClipBounds requested = new(Math.Max(layerBounds.Left, ClampFloor(region.Left, outputWidth)), Math.Max(layerBounds.Top, ClampFloor(region.Top, outputHeight)),
                    Math.Min(layerBounds.Right, ClampCeiling(region.Right, outputWidth)), Math.Min(layerBounds.Bottom, ClampCeiling(region.Bottom, outputHeight)));
                if (activeViewport.Bounds is {} outputClip)
                    requested = new(Math.Max(requested.Left, outputClip.Left), Math.Max(requested.Top, outputClip.Top),
                        Math.Min(requested.Right, outputClip.Right), Math.Min(requested.Bottom, outputClip.Bottom));
                if (requested.Right <= requested.Left || requested.Bottom <= requested.Top) return true;
                Span<byte> materialized = stackalloc byte[4];
                foreach (SvgCpuRasterContext tile in rasterGrid.GetContexts(requested))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double left = double.PositiveInfinity, top = double.PositiveInfinity, right = double.NegativeInfinity, bottom = double.NegativeInfinity;
                    int active = 0;
                    for (int placementIndex = 0; placementIndex < placements.Count; placementIndex++)
                    {
                        SvgOffsetFilterPlacement placement = placements[placementIndex];
                        cancellationToken.ThrowIfCancellationRequested();
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
                    if (frame is { FilterBinding: {} nativeCaptureOwner, NativeSourceBounds: {} captureExtent } && nativeCaptureOwner.Index == command.MaskContentIndex)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        left = Math.Floor(captureExtent.Left * horizontalScale + horizontalOffset) - 1;
                        top = Math.Floor(captureExtent.Top * verticalScale + verticalOffset) - 1;
                        right = Math.Ceiling(captureExtent.Right * horizontalScale + horizontalOffset) + 1;
                        bottom = Math.Ceiling(captureExtent.Bottom * verticalScale + verticalOffset) + 1;
                    }
                    SvgRenderLimits limits = options.Limits;
                    double sourceWidth = right - left, sourceHeight = bottom - top;
                    if (sourceWidth > limits.MaximumDimension || sourceHeight > limits.MaximumDimension
                        || sourceWidth * sourceHeight > limits.MaximumPixels || sourceWidth * sourceHeight > int.MaxValue / 4
                        || left < int.MinValue || left > int.MaxValue || top < int.MinValue || top > int.MaxValue)
                        throw new SvgException(SvgErrorCode.LimitExceeded, "Offset source capture exceeds the configured surface limits.");
                    SvgRenderLimits captureLimits = limits;
                    SvgRenderOptions captureOptions = new(options.Viewport, options.OutputScale, SvgColor.Transparent, captureLimits, options.FontSnapshot);
                    bool objectSourceUnits = resource.PrimitiveUnits == SvgFilterUnits.ObjectBoundingBox;
                    SvgAffineTransform coordinates = objectSourceUnits
                        ? SvgAffineTransform.FromScaleOffset(primitiveMap.ScaleX, primitiveMap.ScaleY,
                            binding.ObjectBounds.Left * horizontalScale + horizontalOffset, binding.ObjectBounds.Top * verticalScale + verticalOffset)
                        : MapFilterParameterTransform(binding.Transform);
                    SvgFilterRegionContext sourceContext = new(coordinates,
                        objectSourceUnits ? new(1, 1) : new(binding.LengthBases.Width ?? width, binding.LengthBases.Height ?? height), region);
                    SvgPaintBounds requestedSource = new(MathF.Max(requested.Left, tile.BitmapClip.Left), MathF.Max(requested.Top, tile.BitmapClip.Top),
                        MathF.Min(requested.Right, tile.BitmapClip.Right), MathF.Min(requested.Bottom, tile.BitmapClip.Bottom));
                    // Each compositor bitmap owns its clipped request and image origin.
                    cancellationToken.ThrowIfCancellationRequested();
                    if (activeViewport.Coverage is {} clipCoverage && clipCoverage.TryGetBounds(tile.BitmapClip, out SvgAnalyticClipBounds bitmapClip))
                        requestedSource = new(MathF.Max(requestedSource.Left, bitmapClip.Left), MathF.Max(requestedSource.Top, bitmapClip.Top),
                            MathF.Min(requestedSource.Right, bitmapClip.Right), MathF.Min(requestedSource.Bottom, bitmapClip.Bottom));
                    if (requestedSource.Right <= requestedSource.Left || requestedSource.Bottom <= requestedSource.Top) continue;
                    SvgPaintBounds needed = SvgFilterPlayback.RequiredSourceBounds(resource, primitiveMap, sourceContext, requestedSource, budget, cancellationToken);
                    SvgPaintBounds painted = new((float)(imageVisual.Left * horizontalScale + horizontalOffset), (float)(imageVisual.Top * verticalScale + verticalOffset),
                        (float)(imageVisual.Right * horizontalScale + horizontalOffset), (float)(imageVisual.Bottom * verticalScale + verticalOffset));
                    SvgPaintBounds graphical = new((float)(elementVisual.Left * horizontalScale + horizontalOffset), (float)(elementVisual.Top * verticalScale + verticalOffset),
                        (float)(elementVisual.Right * horizontalScale + horizontalOffset), (float)(elementVisual.Bottom * verticalScale + verticalOffset));
                    SvgPaintBounds outputHint = SvgFilterPlayback.SourceLayerBounds(resource, primitiveMap, sourceContext, graphical, SvgFilterBoundsKind.Output, budget, cancellationToken);
                    SvgPaintBounds hint = ImageHint(painted, RoundOutputHint(outputHint));
                    SvgCpuBitmapOrigin sourceImageOrigin = SourceImageOrigin(hint, needed);
                    SvgAnalyticClipBounds sourceContentClip = new(1, 1, (int)sourceWidth - 1, (int)sourceHeight - 1);
                    if (frame is { FilterBinding: {} sourceOwner, NativeSourceBounds: {} ownedSource } && sourceOwner.Index == command.MaskContentIndex)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        SvgPaintBounds content = new((float)(ownedSource.Left * horizontalScale + horizontalOffset),
                            (float)(ownedSource.Top * verticalScale + verticalOffset),
                            (float)(ownedSource.Right * horizontalScale + horizontalOffset),
                            (float)(ownedSource.Bottom * verticalScale + verticalOffset));
                        sourceContentClip = SourceContentClip(content, new((int)left, (int)top), (int)sourceWidth, (int)sourceHeight);
                    }
                    using RasterizedSurface captured = Rasterize(scene, captureOptions, budget, RasterSurfaceMode.PremultipliedFilter,
                        new RasterFrame((int)sourceWidth, (int)sourceHeight, horizontalScale, horizontalOffset - left, verticalScale, verticalOffset - top)
                        { SingleBitmap = true, ContentClip = sourceContentClip, ParentOrigin = tile.Origin, CaptureOrigin = new((int)left, (int)top), ImageShaderOrigin = sourceImageOrigin, SourcePlacement = sourcePlacement, NativeSourceBounds = frame?.NativeSourceBounds, FilterSourceMap = frame?.FilterSourceMap, NativeCapturePlacement = PlaceNativeSource(new((int)left, (int)top)) }, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    int tw = tile.BitmapClip.Right - tile.BitmapClip.Left, th = tile.BitmapClip.Bottom - tile.BitmapClip.Top;
                    using RenderBudget.BudgetReservation tileOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<byte>(checked(tw * th * 4)), SvgMemoryCategory.Surface);
                    byte[] pixels = new byte[tw * th * 4];
                    SvgFilterColorSpace outputColorSpace = final.ColorSpace ?? resource.ColorSpace;
                    bool directSource = placements.Count == 1 && !placements[0].AlphaOnly;
                    bool colorFiltered = placements.Count == 1 && (placements[0].AlphaOnly || hasLinearTransfer);
                    bool linear = placements.Count > 1 && outputColorSpace == SvgFilterColorSpace.LinearRgb;
                    // Match graph playback: affine restoration samples the working-
                    // space image before applying its final color transfer.
                    bool retainedLinearOutput = linear && frame?.FilterSourceMap is not null
                        && frame?.FilterBinding is {} offsetTransferBinding && offsetTransferBinding.Index == command.MaskContentIndex;
                    if (retainedLinearOutput) filterOutputColorSpace = SvgFilterColorSpace.LinearRgb;
                    for (int placementIndex = 0; placementIndex < placements.Count; placementIndex++)
                    {
                        SvgOffsetFilterPlacement placement = placements[placementIndex];
                        int x1 = Math.Max(tile.BitmapClip.Left, (int)Math.Max(left + placement.X, Math.Floor(placement.Clip.Left)));
                        int y1 = Math.Max(tile.BitmapClip.Top, (int)Math.Max(top + placement.Y, Math.Floor(placement.Clip.Top)));
                        int x2 = Math.Min(tile.BitmapClip.Right, (int)Math.Min(right + placement.X, Math.Ceiling(placement.Clip.Right)));
                        int y2 = Math.Min(tile.BitmapClip.Bottom, (int)Math.Min(bottom + placement.Y, Math.Ceiling(placement.Clip.Bottom)));
                        cancellationToken.ThrowIfCancellationRequested();
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
                                Span<byte> target = pixels.AsSpan(((y - tile.BitmapClip.Top) * tw + x - tile.BitmapClip.Left) * 4, 4);
                                // Transfers and SourceAlpha retain a color-filter shader;
                                // stored same-space image inputs use packed source-over.
                                if (linear) SvgFilterColorTransfer.Convert(paint, SvgFilterColorSpace.LinearRgb).Blend(target, 255);
                                else if (placement.AlphaOnly) paint.Blend(target, 255);
                                else BlendPremultipliedOver(target, captured.Pixels[sourceIndex], captured.Pixels[sourceIndex + 1],
                                    captured.Pixels[sourceIndex + 2], captured.Pixels[sourceIndex + 3]);
                            }
                        }
                    }
                    int restoreLeft = Math.Max(requested.Left, tile.Interior.Left), restoreTop = Math.Max(requested.Top, tile.Interior.Top);
                    int restoreRight = Math.Min(requested.Right, tile.Interior.Right), restoreBottom = Math.Min(requested.Bottom, tile.Interior.Bottom);
                    cancellationToken.ThrowIfCancellationRequested();
                    float opacity = command.SourceOpacity.Value * foldedOpacity;
                    byte paintAlpha = (byte)Math.Clamp((int)MathF.Floor(opacity * 255 + .5f), 0, 255);
                    for (int y = restoreTop; y < restoreBottom; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        for (int x = restoreLeft; x < restoreRight; x++)
                        {
                            byte coverage = !activeViewport.HasMask ? (byte)255 : activeViewport.Mask.GetAlphaAtOffset(outputWidth, (y * outputWidth + x) * 4);
                            // The packed blitter consumes nonzero AA runs. A zero
                            // run must leave an already-painted backdrop untouched.
                            if (coverage == 0) continue;
                            int p = ((y - tile.BitmapClip.Top) * tw + x - tile.BitmapClip.Left) * 4;
                            const float byteScale = 1f / 255f;
                            SvgShaderPaint paint = new(pixels[p] * byteScale, pixels[p + 1] * byteScale, pixels[p + 2] * byteScale, pixels[p + 3] * byteScale);
                            if (linear && !retainedLinearOutput) paint = SvgFilterColorTransfer.Convert(paint, SvgFilterColorSpace.Srgb);
                            else if (directSource && hasLinearTransfer)
                                paint = SvgFilterColorTransfer.Convert(SvgFilterColorTransfer.Convert(paint, SvgFilterColorSpace.LinearRgb), SvgFilterColorSpace.Srgb);
                            Span<byte> target = destination.AsSpan((y * outputWidth + x) * 4, 4);
                            // Non-unit paint alpha resolves transfer samples first.
                            // Same-space images retain their stored lane arithmetic.
                            if (opacity != 1 || !colorFiltered && outputColorSpace == SvgFilterColorSpace.Srgb)
                            {
                                scoped ReadOnlySpan<byte> stored = pixels.AsSpan(p, 4);
                                if (linear || directSource && hasLinearTransfer)
                                {
                                    materialized.Clear();
                                    paint.Blend(materialized, 255);
                                    stored = materialized;
                                }
                                if (surfaceMode == RasterSurfaceMode.PremultipliedPattern)
                                {
                                    SvgConstantShaderPaint sample = new((byte)((stored[0] * paintAlpha + 255) >> 8),
                                        (byte)((stored[1] * paintAlpha + 255) >> 8), (byte)((stored[2] * paintAlpha + 255) >> 8),
                                        (byte)((stored[3] * paintAlpha + 255) >> 8));
                                    sample.Blend(target, coverage);
                                }
                                else BlendClippedLayerPixel(target, stored, paintAlpha, 255, coverage);
                                continue;
                            }
                            new SvgShaderPaint(paint.Red * opacity, paint.Green * opacity, paint.Blue * opacity, paint.Alpha * opacity)
                                .Blend(target, coverage);
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
                        cancellationToken.ThrowIfCancellationRequested();
                        if ((uint)input.Index >= (uint)resource.Primitives.Items.Length) throw new SvgException(SvgErrorCode.MalformedInput, "Offset input lies outside its graph.");
                        SvgFilterPrimitive primitive = resource.Primitives.Items[input.Index];
                        clip = new((float)Math.Max(clip.Left, region.Left + dx), (float)Math.Max(clip.Top, region.Top + dy),
                            (float)Math.Min(clip.Right, region.Right + dx), (float)Math.Min(clip.Bottom, region.Bottom + dy));
                        hasLinearTransfer |= (primitive.ColorSpace ?? resource.ColorSpace) == SvgFilterColorSpace.LinearRgb;
                        dx += PixelOffset(primitive.OffsetX * primitiveMap.ScaleX); dy += PixelOffset(primitive.OffsetY * primitiveMap.ScaleY);
                        if (!double.IsFinite(dx) || !double.IsFinite(dy) || Math.Abs(dx) > int.MaxValue || Math.Abs(dy) > int.MaxValue)
                            throw new SvgException(SvgErrorCode.LimitExceeded, "Offset chain exceeds the supported coordinate range.");
                        if (dx != Math.Truncate(dx) || dy != Math.Truncate(dy)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fractional filter offsets are not implemented.");
                        input = primitive.Input;
                    }
                    placements.Add(new(dx, dy, input == SvgFilterInput.SourceAlpha, clip));
                }

                static double PixelOffset(double value)
                    => value == Math.Truncate(value) ? value : MathF.Floor((float)value + .5f);
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

        SvgShaderSamplingContext ShaderSamplingContext()
        {
            SvgCpuBitmapOrigin? dither = surfaceMode == RasterSurfaceMode.PremultipliedFilter
                && frame is { NativeSourceBounds: null, ImageShaderOrigin: {} sourceOrigin } sourceFrame
                ? new(sourceOrigin.X - sourceFrame.CaptureOrigin.X, sourceOrigin.Y - sourceFrame.CaptureOrigin.Y) : null;
            SvgAffineTransform device = drawingMap.Device;
            return new(rasterGrid, layerBounds, layerClipCoverage, frame?.NativeSourceBounds, dither,
                sourceInverse, device.A, device.D, device.E, device.F);
        }

        bool RasterizeShaderHairline(SvgAuthoredPath? source, SvgClipBox? clip, ref SvgShaderSampler shader, byte[] destination)
        {
            if (source?.StrokeStyle is not {} style
                || style.Shape == SvgAuthoredStrokeShape.Rectangle && source.SceneMap.PreservesAxisAlignedRectangles) return false;
            bool painted = SvgHairlineStroke.TryRasterize(source,
                drawingMap.Compose(sourcePlacement),
                ref shader, clip is {} bounds ? MapClipBounds(bounds, horizontalScale, horizontalOffset, verticalScale, verticalOffset) : null,
                clip?.Antialiasing ?? SvgClipAntialiasing.None, activeViewport.Mask, activeViewport.IsBinary,
                destination, outputWidth, outputHeight, cancellationToken);
            return painted;
        }

        SvgCpuRasterContext LayerRasterContext(SvgCpuRasterContext tile)
        {
            SvgAnalyticClipBounds rasterBounds = LayerRasterBounds(layerBounds, frame);
            SvgAnalyticClipBounds bitmap = new(Math.Max(tile.BitmapClip.Left, rasterBounds.Left), Math.Max(tile.BitmapClip.Top, rasterBounds.Top),
                Math.Min(tile.BitmapClip.Right, rasterBounds.Right), Math.Min(tile.BitmapClip.Bottom, rasterBounds.Bottom));
            SvgAnalyticClipBounds interior = new(Math.Max(tile.Interior.Left, rasterBounds.Left), Math.Max(tile.Interior.Top, rasterBounds.Top),
                Math.Min(tile.Interior.Right, rasterBounds.Right), Math.Min(tile.Interior.Bottom, rasterBounds.Bottom));
            return new(bitmap, interior) { ConstructionOrigin = frame?.ContentClip is not null ? tile.Origin : null };
        }

        // Source layout/request bounds can be tighter than numerical curve coverage.
        // The admitted raster window governs geometry and clip resources.
        static SvgAnalyticClipBounds LayerRasterBounds(SvgAnalyticClipBounds bounds, RasterFrame? sourceFrame)
            => sourceFrame is { ContentClip: {} content, RasterClip: {} raster } && content == bounds
                ? raster : bounds;

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

        // Painted output and stored image bounds share graph propagation, with distinct rounding domains.
        SvgPaintBounds FilterResultBounds(SvgFilterBinding binding, SvgFilterResource filter, SvgFilterBoundsKind kind)
        {
            SvgPaintBounds filtered = FilterRegion(binding, filter, SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset));
            if (binding.Transform.TryInvert(out SvgAffineTransform inverseBinding))
            {
                SvgFilterBinding local = binding with { Transform = SvgAffineTransform.Identity,
                    ObjectBounds = binding.ObjectBounds.Map(inverseBinding, cancellationToken),
                    VisualBounds = binding.VisualBounds.Map(inverseBinding, cancellationToken) };
                SvgPaintBounds localRegion = FilterRegion(local, filter, SvgAffineTransform.Identity);
                bool objectUnits = filter.PrimitiveUnits == SvgFilterUnits.ObjectBoundingBox;
                SvgFilterDeviceMap distances = objectUnits
                    ? new(local.ObjectBounds.Right - local.ObjectBounds.Left, local.ObjectBounds.Bottom - local.ObjectBounds.Top) : new(1, 1);
                SvgFilterRegionContext context = new(objectUnits
                    ? SvgAffineTransform.FromScaleOffset(distances.ScaleX, distances.ScaleY, local.ObjectBounds.Left, local.ObjectBounds.Top)
                    : SvgAffineTransform.Identity,
                    objectUnits ? new(1, 1) : new(binding.LengthBases.Width ?? width, binding.LengthBases.Height ?? height), localRegion);
                SvgPaintBounds sourceHint = new(MathF.Min(local.VisualBounds.Left, local.ObjectBounds.Left), MathF.Min(local.VisualBounds.Top, local.ObjectBounds.Top),
                    MathF.Max(local.VisualBounds.Right, local.ObjectBounds.Right), MathF.Max(local.VisualBounds.Bottom, local.ObjectBounds.Bottom));
                bool dependsOnSource = DependsOnSource(filter, budget, cancellationToken);
                if (kind != SvgFilterBoundsKind.Output && dependsOnSource)
                {
                    SvgAffineTransform ctm = MapFilterDrawingTransform(binding.Transform);
                    SvgShaderMatrix native = new((float)ctm.A, (float)ctm.C, (float)ctm.E, (float)ctm.B, (float)ctm.D, (float)ctm.F);
                    (float scaleX, float scaleY) = native.GetPictureScale();
                    SvgAffineTransform layerMap = SvgAffineTransform.FromScale(scaleX, scaleY);
                    SvgAffineTransform remainder = new(native.ScaleX * (1f / scaleX), native.SkewY * (1f / scaleX),
                        native.SkewX * (1f / scaleY), native.ScaleY * (1f / scaleY), native.TranslateX, native.TranslateY);
                    SvgFilterBinding nativeBinding = local with { Transform = layerMap,
                        ObjectBounds = local.ObjectBounds.Map(layerMap, cancellationToken),
                        VisualBounds = sourceHint.Map(layerMap, cancellationToken) };
                    SvgFilterDeviceMap nativeDistances = objectUnits
                        ? new(nativeBinding.ObjectBounds.Right - nativeBinding.ObjectBounds.Left, nativeBinding.ObjectBounds.Bottom - nativeBinding.ObjectBounds.Top)
                        : new(scaleX, scaleY);
                    SvgFilterRegionContext nativeContext = new(objectUnits
                        ? SvgAffineTransform.FromScaleOffset(nativeDistances.ScaleX, nativeDistances.ScaleY, nativeBinding.ObjectBounds.Left, nativeBinding.ObjectBounds.Top) : layerMap,
                        context.LengthBases, FilterRegion(nativeBinding, filter, SvgAffineTransform.Identity));
                    SvgPaintBounds parameterHint = SvgFilterPlayback.ParameterCaptureBounds(filter, distances,
                        context, sourceHint, budget, cancellationToken);
                    SvgPaintBounds nativeSource = SvgFilterPlayback.RoundLayerBounds(new(parameterHint.Left * scaleX, parameterHint.Top * scaleY,
                        parameterHint.Right * scaleX, parameterHint.Bottom * scaleY));
                    return SvgFilterPlayback.SourceLayerBounds(filter, nativeDistances, nativeContext, nativeSource,
                        SvgFilterBoundsKind.StoredImage, budget, cancellationToken).Map(remainder, cancellationToken);
                }
                // Dynamic source images enclose their parameter-space capture
                // before the graph maps them. Source-free paint keeps its logical
                // extent until the enclosing opacity layer is prepared.
                if (dependsOnSource) sourceHint = SvgFilterPlayback.RoundParameterBounds(sourceHint);
                SvgPaintBounds outputBounds = SvgFilterPlayback.SourceLayerBounds(filter, distances, context,
                    sourceHint, SvgFilterBoundsKind.Output, budget, cancellationToken).Map(binding.Transform, cancellationToken);
                outputBounds = new(MathF.Floor(outputBounds.Left), MathF.Floor(outputBounds.Top), MathF.Ceiling(outputBounds.Right), MathF.Ceiling(outputBounds.Bottom));
                filtered = outputBounds.Map(SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset), cancellationToken);
            }
            return filtered;

            static bool DependsOnSource(SvgFilterResource filter, RenderBudget budget, CancellationToken token)
            {
                int count = filter.Primitives.Items.Length;
                if (count == 0) return false;
                using RenderBudget.BudgetReservation owner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<bool>(count), SvgMemoryCategory.Temporary);
                bool[] sources = new bool[count];
                for (int index = 0; index < count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    SvgFilterPrimitive primitive = filter.Primitives.Items[index];
                    bool usesSource = primitive.Kind != SvgFilterPrimitiveKind.Flood
                        && (primitive.Input.Index < 0 || sources[primitive.Input.Index]);
                    if (primitive.Kind == SvgFilterPrimitiveKind.Merge)
                    {
                        usesSource = false;
                        foreach (SvgFilterInput input in filter.MergeInputs.Items.AsSpan(primitive.MergeFirst, primitive.MergeCount))
                        {
                            token.ThrowIfCancellationRequested();
                            usesSource |= input.Index < 0 || sources[input.Index];
                        }
                    }
                    else if (primitive.Kind is SvgFilterPrimitiveKind.Composite or SvgFilterPrimitiveKind.Blend)
                        usesSource |= primitive.SecondInput.Index < 0 || sources[primitive.SecondInput.Index];
                    sources[index] = usesSource;
                }
                return count > 0 && sources[^1];
            }

        }

        SvgAnalyticClipBounds LayerPaintBounds(int first, SvgAnalyticClipBounds parent, SvgClipBox? retainedRegion, SvgLayerRegionKind regionKind)
        {
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
                cancellationToken.ThrowIfCancellationRequested();
                SvgDrawCommand value = root.Commands[cursor];
                if (value.Kind == SvgDrawCommandKind.BeginGroup)
                {
                    if (value.RegionKind == SvgLayerRegionKind.Filter && root.FilterResources is {} filters)
                    {
                        SvgFilterBinding binding = frame?.FilterBinding is {} replay && replay.Index == value.MaskContentIndex
                            ? replay.Binding : filters.BindingAt(value.MaskContentIndex);
                        SvgPaintBounds filtered = FilterResultBounds(binding, filters.ResourceAt(binding.ResourceIndex), SvgFilterBoundsKind.StoredImage);
                        left = Math.Min(left, filtered.Left); top = Math.Min(top, filtered.Top);
                        right = Math.Max(right, filtered.Right); bottom = Math.Max(bottom, filtered.Bottom);
                        int sourceDepth = 1;
                        while (++cursor < root.Commands.Length)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            SvgDrawCommand nestedSource = root.Commands[cursor];
                            if (nestedSource.Kind == SvgDrawCommandKind.BeginGroup) sourceDepth++;
                            else if (nestedSource.Kind == SvgDrawCommandKind.EndGroup && --sourceDepth == 0) break;
                        }
                        continue;
                    }
                    // Eliding constant mask content must preserve its spatial contribution.
                    if (value.RegionKind is (SvgLayerRegionKind.Mask or SvgLayerRegionKind.ClipMask) && value.MaskRegion is {} spatialRegion)
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
                if (IncludeRectangleDrawingBounds(geometry.AuthoredPath)) continue;
                SvgPaintBounds? visualBounds = paint.VisualBounds;
                if (visualBounds is {} visual && (sourcePlacement == SvgAffineTransform.Identity || geometry.AuthoredPath is null))
                {
                    IncludeBox(new(visual.Left, visual.Top, visual.Right - visual.Left, visual.Bottom - visual.Top, default, false, null), outputMap, 0);
                    // A coverage surface must also contain the primitive's ink.
                    if (regionKind is not (SvgLayerRegionKind.Viewport or SvgLayerRegionKind.FilterOutputClip)) continue;
                }
                SvgAuthoredPath? source = geometry.AuthoredPath?.StrokeOutline ?? geometry.AuthoredPath;
                if (source is not null)
                {
                    SvgRectangle box = SvgPathBounds.Compute(source.Commands, cancellationToken);
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
                            IncludeBox(new(Math.Min(line.X1, line.X2), Math.Min(line.Y1, line.Y2), Math.Abs(line.X2-line.X1), Math.Abs(line.Y2-line.Y1), default, true, null), outputMap, line.StrokeWidth / 2d);
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
            if (double.IsPositiveInfinity(left)) return parent;
            return new((int)Math.Clamp(Math.Floor(left), parent.Left, parent.Right),
                (int)Math.Clamp(Math.Floor(top), parent.Top, parent.Bottom),
                (int)Math.Clamp(Math.Ceiling(right), parent.Left, parent.Right),
                (int)Math.Clamp(Math.Ceiling(bottom), parent.Top, parent.Bottom));

            bool IncludeRectangleDrawingBounds(SvgAuthoredPath? source)
            {
                if (source is null) return false;
                float inflation = 0;
                if (source.StrokeStyle is {} stroke)
                {
                    if (stroke.Shape is not (SvgAuthoredStrokeShape.Rectangle or SvgAuthoredStrokeShape.RectanglePath) || stroke.NonScalingMap is not null
                        || stroke.WidthMap != source.SceneMap) return false;
                    inflation = (float)stroke.Width / 2f;
                }
                else if (!source.HasRectangleDrawingBounds) return false;
                SvgRectanglePathRecognizer recognizer = new();
                foreach (SvgAuthoredPathCommand command in source.Commands)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!recognizer.Add(command.Verb, command.First)) return false;
                }
                if (recognizer.Finish() is not SvgRecognizedRectangle { Closed: true } rectangle) return false;
                // Drawing records enclose source-space rectangle bounds before mapping.
                // Filter visual bounds retain their independent floating-point contract.
                SvgAffineTransform map = outputMap.ComposeChecked(source.SceneMap);
                IncludeBox(new(MathF.Floor(rectangle.Left - inflation), MathF.Floor(rectangle.Top - inflation),
                    MathF.Ceiling(rectangle.Right + inflation) - MathF.Floor(rectangle.Left - inflation),
                    MathF.Ceiling(rectangle.Bottom + inflation) - MathF.Floor(rectangle.Top - inflation), default, false, null), map, 0);
                return true;
            }

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
                cancellationToken.ThrowIfCancellationRequested();
                SvgDevicePoint mapped = SvgAuthoredPath.MapPoint(map, point, new(0, 0));
                if (!double.IsFinite(mapped.X) || !double.IsFinite(mapped.Y)) throw new SvgException(SvgErrorCode.LimitExceeded, "Layer bounds exceed the admitted numeric range.");
                left = Math.Min(left, mapped.X); top = Math.Min(top, mapped.Y);
                right = Math.Max(right, mapped.X); bottom = Math.Max(bottom, mapped.Y);
            }
        }

        bool TrySinglePaint(int first, SvgRecordingScope scope)
        {
            SvgPaintOperationIdentity identity = default;
            int depth = 0;
            for (int end = first; end < root.Commands.Length; end++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgDrawCommand candidate = root.Commands[end];
                if (candidate.Kind == SvgDrawCommandKind.EndGroup)
                {
                    if (depth == 0) return identity.Value != 0;
                    depth--;
                    continue;
                }
                if (candidate.Kind == SvgDrawCommandKind.BeginGroup)
                {
                    if (candidate.RegionKind is not (SvgLayerRegionKind.Viewport or SvgLayerRegionKind.FilterOutputClip or SvgLayerRegionKind.AffineViewport or SvgLayerRegionKind.Bounds)
                        || candidate.SourceOpacity.Value != 1 || candidate.MaskAlpha != 255) return false;
                    depth++;
                    continue;
                }
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
            BudgetedParserList<ViewportClipFrame>? clipFrames = null;
            try
            {
                while (index < root.Commands.Length)
                {
                    SvgDrawCommand command = root.Commands[index++];
                    if (command.Kind != SvgDrawCommandKind.Geometry) ReleasePatternTile();
                    if (command.Kind == SvgDrawCommandKind.EndGroup)
                    {
                        if (clipFrames is { Count: > 0 })
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            ViewportClipFrame saved = clipFrames[^1];
                            clipFrames.RemoveLast();
                            RestoreViewportClip(saved);
                            continue;
                        }
                        if (!stopsAtGroupEnd) throw new SvgException(SvgErrorCode.MalformedInput, "SVG group structure is unbalanced.");
                        return index;
                    }
                    if (command.Kind == SvgDrawCommandKind.BeginGroup)
                    {
                        if (command.RegionKind == SvgLayerRegionKind.Filter)
                        {
                            index = RenderFilter(index, command, destination, null);
                            continue;
                        }
                        if (command.MaskRegion is null && command.MaskContentIndex < 0 && command.MaskAlpha == 255
                            && (command.SourceOpacity.Value == 1 || TrySinglePaint(index, command.RecordingScope)))
                        {
                            float previousOpacity = foldedOpacity;
                            foldedOpacity *= command.SourceOpacity.Value;
                            // Identity groups replay their paints directly. Single-paint
                            // groups can also fold opacity while retaining active clips.
                            try { index = RenderCommands(index, destination, true); }
                            finally { foldedOpacity = previousOpacity; }
                            continue;
                        }
                        SvgAnalyticClipBounds previousLayer = layerBounds;
                        bool previousMaskClip = maskClipAntialiased;
                        bool viewportClip = command.RegionKind is (SvgLayerRegionKind.Viewport or SvgLayerRegionKind.FilterOutputClip) && command.MaskRegion is not null;
                        bool affineViewportClip = command.RegionKind == SvgLayerRegionKind.AffineViewport && command.MaskContentIndex >= 0;
                        SvgClipBox? placedRegion = command.MaskRegion;
                        if (placedRegion is {} retained && sourcePlacement != SvgAffineTransform.Identity)
                        {
                            SvgPaintBounds placed = new SvgPaintBounds((float)retained.X, (float)retained.Y, (float)retained.Right, (float)retained.Bottom)
                                .Map(sourcePlacement, cancellationToken);
                            placedRegion = retained with { X = placed.Left, Y = placed.Top, Width = Math.Max(0, placed.Right - placed.Left), Height = Math.Max(0, placed.Bottom - placed.Top) };
                        }
                        (double regionLeft, double regionTop, double regionRight, double regionBottom) = placedRegion is SvgClipBox maskRegion ? MapClipBounds(maskRegion, horizontalScale, horizontalOffset, verticalScale, verticalOffset) : (0, 0, outputWidth, outputHeight);
                        if (viewportClip && command.SourceOpacity.Value == 1 && command.MaskAlpha == 255 && command.MaskContentIndex < 0
                            && sourcePlacement.PreservesAxisAlignedRectangles && regionLeft <= previousLayer.Left && regionTop <= previousLayer.Top
                            && regionRight >= previousLayer.Right && regionBottom >= previousLayer.Bottom)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            index = RenderCommands(index, destination, true);
                            continue;
                        }
                        bool affineMaskTarget = command.MaskContentIndex >= 0 && root.SceneStorage is {} maskStorage
                            && (uint)command.MaskContentIndex < (uint)maskStorage.MaskContents.Length && maskStorage.MaskContents[command.MaskContentIndex].UsesAffineTarget;
                        bool paintedClipMask = command.RegionKind == SvgLayerRegionKind.ClipMask;
                        SvgAnalyticClipBounds layer = affineViewportClip ? previousLayer : LayerPaintBounds(index, previousLayer, viewportClip ? null : command.MaskRegion, command.RegionKind);
                        if (activeViewport.Bounds is {} inheritedBounds)
                        {
                            int left = Math.Max(layer.Left, inheritedBounds.Left), top = Math.Max(layer.Top, inheritedBounds.Top);
                            layer = new(left, top, Math.Max(left, Math.Min(layer.Right, inheritedBounds.Right)), Math.Max(top, Math.Min(layer.Bottom, inheritedBounds.Bottom)));
                        }
                        bool shaderRestore = false;
                        if (viewportClip || affineViewportClip)
                        {
                            // A retained affine clip owns source coordinates and its map.
                            // Scene-space bounds rounded before output placement cannot safely
                            // trim its device bitmap: native concatenation can move an edge.
                            if (!affineViewportClip)
                            {
                                int left = Math.Max(layer.Left, ClampFloor(regionLeft, outputWidth));
                                int top = Math.Max(layer.Top, ClampFloor(regionTop, outputHeight));
                                int right = Math.Min(layer.Right, ClampCeiling(regionRight, outputWidth));
                                int bottom = Math.Min(layer.Bottom, ClampCeiling(regionBottom, outputHeight));
                                layer = new(left, top, Math.Max(left, right), Math.Max(top, bottom));
                            }
                            var bounds = (Left: regionLeft, Top: regionTop, Right: regionRight, Bottom: regionBottom);
                            SvgClipAntialiasing antialiasing = command.MaskRegion.GetValueOrDefault().Antialiasing;
                            // SkDraw::clipHandlesSprite permits the direct image blitter for a BW
                            // clip or an image wholly contained in opaque AA runs. Otherwise the
                            // image shader shades opacity first, then applies clip coverage.
                            shaderRestore = affineViewportClip || SvgAnalyticRectangleClip.Classify(regionLeft, regionTop, regionRight, regionBottom, previousLayer, antialiasing) == SvgAnalyticPaintClip.Antialiased
                                && (SvgAnalyticRectangleClip.GetCoverage(bounds, antialiasing, layer.Left, layer.Top).Apply(255) != 255
                                    || SvgAnalyticRectangleClip.GetCoverage(bounds, antialiasing, layer.Right - 1, layer.Bottom - 1).Apply(255) != 255);
                        }
                        if ((affineViewportClip || viewportClip && command.MaskContentIndex < 0)
                            && command.SourceOpacity.Value == 1 && command.MaskAlpha == 255)
                        {
                            SvgMaskContent? viewportContent = null;
                            if (affineViewportClip)
                            {
                                if (root.SceneStorage is null || (uint)command.MaskContentIndex >= (uint)root.SceneStorage.MaskContents.Length)
                                    throw new SvgException(SvgErrorCode.MalformedInput, "SVG viewport references missing clip content.");
                                viewportContent = root.SceneStorage.MaskContents[command.MaskContentIndex];
                            }
                            RenderBudget.BudgetReservation clipReservation = budget.Reserve(surfaceByteCount, SvgMemoryCategory.Surface);
                            SvgCpuClipCoverage? clipCoverage = null;
                            bool transferred = false;
                            try
                            {
                                byte[] clipPixels = new byte[surfaceByteCount];
                                ViewportClipState previousViewport = activeViewport;
                                bool binaryClip;
                                if (viewportContent is {} clipContent)
                                {
                                    SvgAnalyticClipBounds resourceConstruction = clipContent.ClipKind is SvgClipContentKind.AffineViewport or SvgClipContentKind.BinaryViewport or SvgClipContentKind.BinaryExclusion
                                        ? layer : LayerRasterBounds(previousLayer, frame);
                                    clipCoverage = RasterizeMaskContent(clipContent, clipPixels, true, resourceConstruction,
                                        SvgAnalyticPaintClip.Antialiased, [], out binaryClip);
                                }
                                else
                                {
                                    binaryClip = true;
                                    int clipLeft = Math.Max(previousLayer.Left, ClampFloor(regionLeft, outputWidth));
                                    int clipTop = Math.Max(previousLayer.Top, ClampFloor(regionTop, outputHeight));
                                    int clipRight = Math.Min(previousLayer.Right, ClampCeiling(regionRight, outputWidth));
                                    int clipBottom = Math.Min(previousLayer.Bottom, ClampCeiling(regionBottom, outputHeight));
                                    var rectangleBounds = (Left: regionLeft, Top: regionTop, Right: regionRight, Bottom: regionBottom);
                                    SvgClipAntialiasing clipAntialiasing = command.MaskRegion.GetValueOrDefault().Antialiasing;
                                    for (int y = clipTop; y < clipBottom; y++)
                                    {
                                        cancellationToken.ThrowIfCancellationRequested();
                                        for (int x = clipLeft; x < clipRight; x++)
                                        {
                                            byte alpha = SvgAnalyticRectangleClip.GetCoverage(rectangleBounds, clipAntialiasing, x, y).Apply(255);
                                            clipPixels[(y * outputWidth + x) * 4 + 3] = alpha;
                                            if (alpha is not (0 or 255)) binaryClip = false;
                                        }
                                    }
                                }
                                if (previousViewport.HasMask && (viewportContent is not {} boundedClip || boundedClip.BoundsClipContentIndex < 0))
                                {
                                    for (int pixel = 0; pixel < pixelCount; pixel++)
                                    {
                                        cancellationToken.ThrowIfCancellationRequested();
                                        int offset = pixel * 4 + 3;
                                        clipPixels[offset] = (byte)((clipPixels[offset] * previousViewport.Mask.GetAlphaAtOffset(outputWidth, offset - 3) + 127) / 255);
                                    }
                                    if (clipCoverage is not null)
                                    {
                                        clipCoverage.IntersectAndTrim(previousViewport.Coverage, previousViewport.Mask, clipPixels, outputWidth,
                                            cancellationToken);
                                    }
                                }
                                layer = CoverageBounds(clipPixels, layer, out bool rectangleClip);
                                bool equivalent = binaryClip && previousViewport.IsBinary && rectangleClip && previousViewport.IsRectangle && previousViewport.Bounds == layer
                                    && (viewportContent?.DrawMap is null || viewportContent?.DrawMap == activeViewport.DrawMap);
                                clipFrames ??= new(budget, SvgMemoryCategory.Surface, cancellationToken);
                                cancellationToken.ThrowIfCancellationRequested();
                                int beforeCount = clipFrames.Count;
                                try
                                {
                                    clipFrames.Add(equivalent ? default : new(previousViewport, clipReservation, clipCoverage));
                                }
                                finally
                                {
                                    transferred = !equivalent && clipFrames.Count > beforeCount;
                                }
                                if (equivalent) continue;
                                activeViewport = new(clipPixels, binaryClip && previousViewport.IsBinary, rectangleClip,
                                    layer, clipCoverage, viewportContent?.DrawMap ?? previousViewport.DrawMap, null);
                                continue;
                            }
                            finally
                            {
                                if (!transferred) { clipCoverage?.Dispose(); clipReservation.Dispose(); }
                            }
                        }
                        using RenderBudget.BudgetReservation groupReservation = budget.Reserve(checked((long)pixelCount * 4), SvgMemoryCategory.Surface);
                        byte[] groupPixels = new byte[checked(pixelCount * 4)];
                        ViewportClipState parentViewport = activeViewport;
                        SvgRasterClipMask parentViewportClip = parentViewport.Mask;
                        // A native layer can restore as a sprite when its entire
                        // bitmap lies inside opaque runs of an otherwise AA clip.
                        bool parentRequiresShader = !paintedClipMask && !parentViewportClip.IsEmpty && !activeViewport.IsBinary
                            && !IsOpaqueClipRectangle(parentViewportClip, layer);
                        byte[] sourcePaintClipPixels = [];
                        RenderBudget.BudgetReservation? sourcePaintClipOwner = null;
                        activeViewport = paintedClipMask ? parentViewport : parentViewport with { Pixels = [], Rectangle = null };
                        layerBounds = layer;
                        // An inner isolated surface paints before its parent's clip;
                        // folded single paints keep the existing consumer instead.
                        maskClipAntialiased = false;
                        SvgAnalyticPaintClip sourcePaintClip = SvgAnalyticPaintClip.Binary;
                        if (command.RegionKind == SvgLayerRegionKind.Mask && placedRegion is {} paintRegion)
                        {
                            // Enclose in source space before placing the clip. Rounding a
                            // fractional scene pose would change the source's AA consumer.
                            bool retainedPaintRegion = command.MaskContentIndex >= 0 && root.SceneStorage is {} clipStorage
                                && (uint)command.MaskContentIndex < (uint)clipStorage.MaskContents.Length
                                && clipStorage.MaskContents[command.MaskContentIndex].SourcePaintRegion is not null;
                            if (retainedPaintRegion && root.SceneStorage is {} paintStorage
                                && paintStorage.MaskContents[command.MaskContentIndex].SourcePaintRegion is {} sourceRegion)
                                paintRegion = sourcePlacement.IsAxisAligned ? sourcePlacement.Apply(sourceRegion) : sourceRegion;
                            SvgAffineTransform outputMap = SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset);
                            SvgDevicePoint first = SvgAuthoredPath.MapPoint(outputMap,
                                new(retainedPaintRegion ? (float)paintRegion.X : MathF.Floor((float)paintRegion.X), retainedPaintRegion ? (float)paintRegion.Y : MathF.Floor((float)paintRegion.Y)), new(0, 0));
                            SvgDevicePoint last = SvgAuthoredPath.MapPoint(outputMap,
                                new(retainedPaintRegion ? (float)paintRegion.Right : MathF.Ceiling((float)paintRegion.Right), retainedPaintRegion ? (float)paintRegion.Bottom : MathF.Ceiling((float)paintRegion.Bottom)), new(0, 0));
                            // An affine mask target keeps the native AA paint consumer even under a fully white mask.
                            maskClipAntialiased = affineMaskTarget || SvgAnalyticRectangleClip.Classify(first.X, first.Y, last.X, last.Y, previousLayer, SvgClipAntialiasing.All) == SvgAnalyticPaintClip.Antialiased;
                            sourcePaintClip = maskClipAntialiased ? SvgAnalyticPaintClip.Antialiased : SvgAnalyticPaintClip.Binary;
                            if (retainedPaintRegion && sourcePlacement.IsAxisAligned && maskClipAntialiased)
                            {
                                RenderBudget.BudgetReservation owner = budget.Reserve(surfaceByteCount, SvgMemoryCategory.Surface);
                                try
                                {
                                    byte[] regionPixels = new byte[surfaceByteCount];
                                    SvgRectangleCoverage regionCoverage = SvgRectangleCoverage.Create(Math.Min(first.X, last.X), Math.Min(first.Y, last.Y),
                                        Math.Max(first.X, last.X), Math.Max(first.Y, last.Y), outputWidth, outputHeight);
                                    for (int y = 0; y < outputHeight; y++)
                                    {
                                        cancellationToken.ThrowIfCancellationRequested();
                                        for (int x = 0; x < outputWidth; x++) regionPixels[(y*outputWidth+x)*4+3] = regionCoverage.GetAlpha(x, y);
                                    }
                                    sourcePaintClipPixels = regionPixels;
                                    sourcePaintClipOwner = owner;
                                }
                                catch { owner.Dispose(); throw; }
                            }
                        }
                        using RenderBudget.BudgetReservation? sourcePaintClipLease = sourcePaintClipOwner;
                        if (sourcePaintClipPixels.Length != 0)
                        {
                            activeViewport.Pixels = sourcePaintClipPixels; activeViewport.Rectangle = null;
                            activeViewport.IsBinary = false; activeViewport.IsRectangle = true;
                            activeViewport.Bounds = layer; activeViewport.Coverage = null;
                        }
                        try { index = RenderCommands(index, groupPixels, true); }
                        finally
                        {
                            layerBounds = previousLayer; activeViewport = parentViewport; maskClipAntialiased = previousMaskClip;
                        }
                        // Skia hard-rectangle clipping rounds each mapped edge to the nearest integer
                        // (Copyright 2006 The Android Open Source Project; see THIRD_PARTY_NOTICES.txt);
                        // half-pixel ties move toward positive infinity on both sides.
                        // Allocation bounds enclose ink; physical mask/viewport clips keep nearest-edge rounding.
                        SvgAnalyticClipBounds maskBounds = command.RegionKind is SvgLayerRegionKind.Bounds or SvgLayerRegionKind.ClipMask
                            ? new(ClampFloor(regionLeft, outputWidth), ClampFloor(regionTop, outputHeight), ClampCeiling(regionRight, outputWidth), ClampCeiling(regionBottom, outputHeight))
                            : new(ClampFloor(regionLeft + 0.5, outputWidth), ClampFloor(regionTop + 0.5, outputHeight),
                                ClampFloor(regionRight + 0.5, outputWidth), ClampFloor(regionBottom + 0.5, outputHeight));
                        byte[] maskSurface = [];
                        byte maskLuminance = 255;
                        bool maskIsAlpha = false;
                        bool variableLuminance = false;
                        bool hasMaskContent = command.MaskContentIndex >= 0;
                        byte constantMask = 255;
                        if (hasMaskContent && root.SceneStorage is {} constantStorage
                            && (uint)command.MaskContentIndex < (uint)constantStorage.MaskContents.Length
                            && constantStorage.MaskContents[command.MaskContentIndex].ConstantAlpha is byte constant)
                        { constantMask = constant; hasMaskContent = false; }

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
                                using SvgCpuClipCoverage? maskCoverage = RasterizeMaskContent(maskContent, maskSurface, affineViewportClip, contentBounds, sourcePaintClip, sourcePaintClipPixels, out _);
                            }
                            catch
                            {
                                maskReservation.Dispose();
                                throw;
                            }
                        }
                        using RenderBudget.BudgetReservation? maskLease = maskReservation;
                        byte groupMaskAlpha = MultiplyAlpha(command.MaskAlpha, constantMask);
                        for (int pixel = 0; pixel < pixelCount; pixel++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            int offset = pixel * 4;
                            byte extra = command.Opacity;
                            byte maskAlpha = groupMaskAlpha;
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
                            if (!parentViewportClip.IsEmpty && !paintedClipMask)
                                clipAlpha = (byte)((clipAlpha * parentViewportClip.GetAlphaAtOffset(outputWidth, offset) + 127) / 255);
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
                            else if (shaderRestore || parentRequiresShader) BlendClippedLayerPixel(destination.AsSpan(offset, 4), groupPixels.AsSpan(offset, 4), extra, maskAlpha, clipAlpha);
                            else BlendLayerPixel(destination.AsSpan(offset, 4), groupPixels.AsSpan(offset, 4), extra, maskAlpha);
                        }
                        continue;
                    }
                    if ((uint)command.GeometryIndex >= (uint)root.Geometries.Length) throw new SvgException(SvgErrorCode.MalformedInput, "SVG draw command references missing geometry.");
                    nextPatternTile = NextPatternTile(index, root.Commands.Length);
                    RasterizeGeometry(root.Geometries[command.GeometryIndex], destination);
                }
                if (stopsAtGroupEnd || clipFrames is { Count: > 0 }) throw new SvgException(SvgErrorCode.MalformedInput, "SVG group structure is unbalanced.");
                return index;
            }
            finally
            {
                if (clipFrames is not null)
                {
                    while (clipFrames.Count > 0)
                    {
                        ViewportClipFrame saved = clipFrames[^1];
                        clipFrames.RemoveLast();
                        RestoreViewportClip(saved);
                    }
                    clipFrames.Dispose();
                }
            }

            void RestoreViewportClip(ViewportClipFrame saved)
            {
                if (saved.Reservation is not {} reservation) return;
                activeViewport = saved.Previous;
                saved.Coverage?.Dispose();
                reservation.Dispose();
            }

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

        // Restores a quantized layer using the native packed row's paint alpha.
        // Storage remains borrowed; the surrounding raster loop observes cancellation.
        static void RestoreOpacityLayerPixel(Span<byte> destination, ReadOnlySpan<byte> source, byte opacity)
            => new SvgConstantShaderPaint(source[0], source[1], source[2], source[3]).BlendBitmap(destination, opacity);

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
        // Geometry traversal retains cancellation and temporary-owner contracts.
        SvgCpuClipCoverage? RasterizeMaskContent(SvgMaskContent content, byte[] destination, bool viewport, SvgAnalyticClipBounds contentBounds, SvgAnalyticPaintClip paintClip, byte[] sourcePaintClipPixels, out bool binaryViewport)
        {
            binaryViewport = content.ClipKind is SvgClipContentKind.BinaryViewport or SvgClipContentKind.BinaryExclusion;
            ReleasePatternTile();
            nextPatternTile = null;
            ViewportClipState previousViewport = activeViewport;
            byte[] previousConstruction = activeConstructionClip;
            SvgAnalyticClipBounds? previousConstructionBounds = activeConstructionBounds;
            SvgCpuClipCoverage? previousCollected = collectingClipCoverage, previousConstructionCoverage = activeConstructionCoverage;
            SvgCpuClipCoverage? previousWindowCoverage = clipWindowCoverage;
            SvgAnalyticClipBounds? previousWindowBounds = clipWindowBounds;
            // A native clip builds within the parent bitmap's nonzero bounds.
            // Coverage intersects afterward; the bitmap origin stays unchanged.
            clipWindowCoverage = viewport ? previousViewport.Coverage ?? clipWindowCoverage : null;
            clipWindowBounds = viewport ? previousViewport.Bounds ?? clipWindowBounds : null;
            SvgCpuClipCoverage? previousLayerClipCoverage = layerClipCoverage;
            if (!viewport) layerClipCoverage = previousViewport.Coverage ?? layerClipCoverage;
            float previousOpacity = foldedOpacity;
            bool previousCollector = collectingViewportClip;

            bool previousMaskClip = maskClipAntialiased;
            // Alpha-only mask replay retains the source's native paint-clip consumer.
            // Luminance masks keep their independent color-picture dispatch.
            maskClipAntialiased = !viewport && content.IsAlpha && paintClip == SvgAnalyticPaintClip.Antialiased;
            bool previousLuminanceLayer = maskLuminanceLayer;
            maskLuminanceLayer = content.UsesAffineTarget && !content.IsAlpha;
            SvgAnalyticClipBounds previousLayer = layerBounds;
            SvgRoot previousRoot = root;
            // A path clip establishes a concatenation boundary for subsequent
            // paints. Construct its own mask using the incoming canvas instead.
            activeViewport.DrawMap = !viewport ? null
                : content.ClipKind == SvgClipContentKind.ClipPath ? previousViewport.DrawMap
                : content.DrawMap ?? previousViewport.DrawMap;
            double previousScaleX = horizontalScale, previousScaleY = verticalScale, previousOffsetX = horizontalOffset, previousOffsetY = verticalOffset;
            SvgAffineTransform maskMap = content.CoordinateMap;
            if (!maskMap.IsAxisAligned) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned mask replay maps are implemented.");
            SvgDrawingMap previousDrawing = drawingMap;
            drawingMap = drawingMap.Compose(maskMap);
            SvgAffineTransform maskOutput = drawingMap.Parameter;
            (horizontalScale, horizontalOffset, verticalScale, verticalOffset) = (maskOutput.A, maskOutput.E, maskOutput.D, maskOutput.F);
            activeViewport.Pixels = []; activeViewport.Rectangle = null; activeViewport.Bounds = null; activeViewport.Coverage = null; foldedOpacity = 1;
            if (!viewport && content.IsAlpha && sourcePaintClipPixels.Length != 0)
            {
                activeViewport.Pixels = sourcePaintClipPixels; activeViewport.Rectangle = null; activeViewport.Bounds = contentBounds;
                activeViewport.IsBinary = false; activeViewport.IsRectangle = true;
            }
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
                    coverage = new(rasterGrid, budget);
                    collectingClipCoverage = coverage;
                }
                if (viewport && activeViewport.IsBinary && content.ClipKind == SvgClipContentKind.AffineViewport
                    && content.Geometries.Length == 1 && content.Geometries[0].AuthoredPath is { IsKnownConvex: true } rectangle
                    && rectangle.Commands.Length == 5)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SvgAffineTransform clipMap = drawingMap.Recording;
                    if (sourcePlacement != SvgAffineTransform.Identity) clipMap = clipMap.Compose(sourcePlacement);
                    binaryViewport = RoundsToBinaryRectangle(rectangle, MapSourceTransform(clipMap, rectangle.SceneMap, PathCoordinateSpace.User), contentBounds);
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
                    boundsCoverage = RasterizeMaskContent(storage.MaskContents[content.BoundsClipContentIndex], boundsPixels, true, contentBounds, SvgAnalyticPaintClip.Antialiased, [], out _);
                    if (viewport && previousViewport.HasMask)
                    {
                        for (int pixel = 0; pixel < pixelCount; pixel++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            int offset = pixel * 4 + 3;
                            boundsPixels[offset] = (byte)((boundsPixels[offset] * previousViewport.Mask.GetAlphaAtOffset(outputWidth, offset - 3) + 127) / 255);
                        }
                        if (boundsCoverage is not null)
                        {
                            boundsCoverage.IntersectAndTrim(previousViewport.Coverage, previousViewport.Mask, boundsPixels, outputWidth,
                                cancellationToken);
                        }
                    }
                    activeConstructionCoverage = boundsCoverage;
                    activeConstructionClip = boundsPixels;
                    activeConstructionBounds = CoverageBounds(boundsPixels, contentBounds, out _);
                }
                if (content.VectorContent is {} vector)
                {
                    SvgSceneStorage scene = vector.Scene;
                    root = root with { Geometries = scene.Geometries, Commands = scene.Commands, SceneStorage = scene };
                    RenderCommands(0, destination, false);
                }
                else if (binaryViewport)
                {
                    if (content.Geometries.Length != 1 || content.Geometries[0].AuthoredPath is not {} viewportPath || coverage is null)
                        throw new SvgException(SvgErrorCode.MalformedInput, "A binary viewport requires retained rectangle geometry.");
                    SvgAffineTransform clipMap = drawingMap.Recording;
                    if (sourcePlacement != SvgAffineTransform.Identity) clipMap = clipMap.ComposeChecked(sourcePlacement);
                    SvgBinaryViewportCoverage.Rasterize(viewportPath,
                        MapSourceTransform(clipMap, viewportPath.SceneMap, PathCoordinateSpace.User), rasterGrid,
                        contentBounds, destination, outputWidth, coverage, content.Geometries[0].Polygon.IsEvenOdd, content.ClipKind == SvgClipContentKind.BinaryExclusion, budget, cancellationToken);
                }
                else foreach (SvgGeometry geometry in content.Geometries) RasterizeGeometry(geometry, destination);
                if (boundsPixels.Length != 0)
                {
                    for (int pixel = 0; pixel < pixelCount; pixel++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int offset = pixel * 4 + 3;
                        destination[offset] = (byte)((destination[offset] * boundsPixels[offset] + 127) / 255);
                    }
                }
                if (!viewport && !content.IsAlpha && sourcePaintClipPixels.Length != 0)
                {
                    for (int pixel = 0; pixel < pixelCount; pixel++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int offset = pixel*4;
                        byte alpha = sourcePaintClipPixels[offset+3];
                        for (int lane = 0; lane < 4; lane++) destination[offset+lane] = MultiplyAlpha(destination[offset+lane], alpha);
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
                activeViewport = previousViewport;
                foldedOpacity = previousOpacity; collectingViewportClip = previousCollector; root = previousRoot;
                horizontalScale = previousScaleX; verticalScale = previousScaleY; horizontalOffset = previousOffsetX; verticalOffset = previousOffsetY;
                drawingMap = previousDrawing;
                layerBounds = previousLayer;
                maskClipAntialiased = previousMaskClip;
                maskLuminanceLayer = previousLuminanceLayer;
                ReleasePatternTile();
            }

            static bool RoundsToBinaryRectangle(SvgAuthoredPath rectangle, SvgAffineTransform map, SvgAnalyticClipBounds bitmap)
            {
                if (!map.PreservesAxisAlignedRectangles) return false;
                float left = float.PositiveInfinity, top = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.NegativeInfinity;
                for (int index = 0; index < 4; index++)
                {
                    SvgDevicePoint point = rectangle.Commands[index].First;
                    float x = map.IsAxisAligned ? (float)map.A * (float)point.X + (float)map.E
                        : ((float)map.A * (float)point.X + (float)map.C * (float)point.Y) + (float)map.E;
                    float y = map.IsAxisAligned ? (float)map.D * (float)point.Y + (float)map.F
                        : ((float)map.B * (float)point.X + (float)map.D * (float)point.Y) + (float)map.F;
                    if (!float.IsFinite(x) || !float.IsFinite(y)) return false;
                    left = MathF.Min(left, x); top = MathF.Min(top, y);
                    right = MathF.Max(right, x); bottom = MathF.Max(bottom, y);
                }
                // Native clipRect can keep a nearly integral rectangle binary
                // when its parent is binary. Reuse its existing boundary rule.
                return SvgAnalyticRectangleClip.Classify(left, top, right, bottom, bitmap, SvgClipAntialiasing.All) == SvgAnalyticPaintClip.Binary;
            }
        }

        SvgAffineTransform MapFilterParameterTransform(SvgAffineTransform target)
            => drawingMap.Parameter.ComposeChecked(target);

        SvgAffineTransform MapFilterDrawingTransform(SvgAffineTransform target)
            => frame is null ? drawingMap.MapScene(target) : drawingMap.Recording.ComposeNative(target);

        SvgAffineTransform MapSourceTransform(SvgAffineTransform output, SvgAffineTransform target, PathCoordinateSpace space)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SvgAffineTransform recorded = activeViewport.DrawMap is {} viewport
                ? viewport.Prepare(output, target) : output.ComposeChecked(target);
            // Fitted glyph outlines already contain complete destination placement.
            return space == PathCoordinateSpace.Device ? recorded : drawingMap.PlaceRecorded(recorded);
        }

        SvgShaderMatrix? NativeViewportReference(SvgAffineTransform target)
        {
            if (activeViewport.DrawMap is null && frame is not null) return null;
            SvgAffineTransform output = drawingMap.Recording;
            if (sourcePlacement != SvgAffineTransform.Identity) output = output.ComposeChecked(sourcePlacement);
            return SvgShaderMatrix.FromAffine(MapSourceTransform(output, target, PathCoordinateSpace.User));
        }

        SvgShaderMatrix? NativeRadialViewportReference(SvgRadialGradient gradient, SvgRectangle box)
        {
            if (activeViewport.DrawMap is null && frame is not null || gradient.NativePaintMatrices is not {} matrices) return null;
            SvgAffineTransform target = matrices.Referencing;
            // Object-box matrices reference normalized paint coordinates. Convert
            // those to scene units before shared viewport draw preparation.
            if (!gradient.IsUserSpace)
                target = SvgAffineTransform.FromScaleOffset(box.Width, box.Height, box.X, box.Y).Compose(target);
            return NativeViewportReference(target);
        }

        void RasterizeGeometry(SvgGeometry geometry, byte[] destination)
        {
            bool reusablePattern = geometry.Kind == SvgGeometryKind.PatternFill && geometry.Pattern.Stroke is null && geometry.LeafMask is null;
            if (!reusablePattern || patternTileCache is {} retained && !retained.MatchesSource(geometry.Pattern)) ReleasePatternTile();
            SvgRecordedPaintValue previousPaint = activePaint;
            SvgRecordedPaintValue previousStroke = activeStrokePaint;
            SvgCanvasDrawMap? previousDrawMap = activeViewport.DrawMap;
            activePaint = GetRecordedPaintValue(geometry.RecordedPaint);
            activeStrokePaint = GetRecordedPaintValue(geometry.RecordedStrokePaint);
            activeViewport.DrawMap = activePaint.DrawMap?.Map ?? activeViewport.DrawMap;
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
            finally { activePaint = previousPaint; activeStrokePaint = previousStroke; activeViewport.DrawMap = previousDrawMap; }
        }

        void RasterizeGeometryCore(SvgGeometry geometry, byte[] destination, SvgPaintPass pass)
        {
            if (geometry.Kind == SvgGeometryKind.PolygonFill && geometry.AuthoredPath is SvgAuthoredPath centerline
                && centerline.StrokeStyle is SvgAuthoredStrokeStyle strokeStyle
                && (strokeStyle.Shape != SvgAuthoredStrokeShape.Rectangle || !centerline.SceneMap.PreservesAxisAlignedRectangles))
            {
                SvgPolygonFill stroke = geometry.Polygon;
                bool rendered = stroke.HasFill && SvgHairlineStroke.TryRasterize(centerline,
                    drawingMap.Compose(sourcePlacement), ResolveSolidColor(stroke.Color), surfaceMode == RasterSurfaceMode.PremultipliedPattern ? PatternSolidPaint(stroke.Color) : null,
                    stroke.Clip is SvgClipBox clip ? MapClipBounds(clip, horizontalScale, horizontalOffset, verticalScale, verticalOffset) : null,
                    stroke.Clip?.Antialiasing ?? SvgClipAntialiasing.None, activeViewport.Mask, activeViewport.IsBinary, destination, outputWidth, outputHeight, cancellationToken);
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
            if (!image.Box.HasFill || opacity == 0 || image.Box.Width == 0 || image.Box.Height == 0 || image.SourceRect.IsEmpty) return;
            SvgAnalyticClipBounds sampleBounds = image.SourceRect.PixelBounds;
            int sampleWidth = sampleBounds.Right - sampleBounds.Left, sampleHeight = sampleBounds.Bottom - sampleBounds.Top;
            bool sourceIsSubset = image.SourceRect != new SvgImageSourceRect(0, 0, image.Width, image.Height);
            SvgRectangleRaster raster = PrepareRectangle(image.Box);
            raster = raster with { Left = Math.Max(raster.Left, layerBounds.Left), Top = Math.Max(raster.Top, layerBounds.Top),
                Right = Math.Min(raster.Right, layerBounds.Right), Bottom = Math.Min(raster.Bottom, layerBounds.Bottom) };
            if (raster.Left >= raster.Right || raster.Top >= raster.Bottom) return;
            SvgShaderMatrix recordedImage = SvgShaderMatrix.Concat(SvgShaderMatrix.FromAffine(drawingMap.Recording), image.ReferenceMap);
            SvgShaderMatrix imageDrawing = SvgShaderMatrix.FromAffine(drawingMap.PlaceRecorded(new(recordedImage.ScaleX,
                recordedImage.SkewY, recordedImage.SkewX, recordedImage.ScaleY, recordedImage.TranslateX, recordedImage.TranslateY)));
            SvgShaderMatrix samplingMap = image.LocalMap;
            if (sampleBounds.Left != 0 || sampleBounds.Top != 0)
                samplingMap = SvgShaderMatrix.Concat(samplingMap, new(1, 0, sampleBounds.Left, 0, 1, sampleBounds.Top));
            SvgShaderCoordinates coordinates = SvgShaderCoordinates.CreateImage(imageDrawing, samplingMap);
            // The transparent source border clips painting without moving the native
            // bitmap's shader seed. Retain that bitmap origin even in a compact capture.
            SvgCpuBitmapOrigin? capturedImageOrigin = frame is { ImageShaderOrigin: {} nativeOrigin, ContentClip: {} content } sourceFrame && content == layerBounds
                ? new(nativeOrigin.X - sourceFrame.CaptureOrigin.X, nativeOrigin.Y - sourceFrame.CaptureOrigin.Y) : null;
            (int Width, int Height) decodeSize = surfaceMode == RasterSurfaceMode.PremultipliedPattern
                ? (sampleWidth, sampleHeight)
                : SvgImageMipMap.SelectDecodeSize(SvgShaderMatrix.Concat(imageDrawing, samplingMap), sampleWidth, sampleHeight);
            if (decodeSize.Width == 0 || decodeSize.Height == 0) return;
            (int Upper, float LowerWeight) pictureLevels = image.UniformSample is null && surfaceMode == RasterSurfaceMode.PremultipliedPattern && !sourceIsSubset
                && coordinates.TryGetMatrix(new(0, 0), out SvgShaderMatrix pictureInverse)
                ? SvgImageMipMap.SelectLinearLevels(pictureInverse, sampleWidth, sampleHeight) : (0, 0);
            int pictureMip = pictureLevels.Upper;
            bool pictureReduction = pictureMip != 0 || pictureLevels.LowerWeight != 0;
            if (pictureReduction) decodeSize = (Math.Max(1, sampleWidth >> pictureMip), Math.Max(1, sampleHeight >> pictureMip));
            using RasterizedSurface? mask = authoredPath is null ? null : PrepareImageMask(authoredPath, image.Box.Clip, raster);
            (RasterizedSurface? mipValue, RasterizedSurface? lowerValue) = PrepareImageLevels();
            using RasterizedSurface? mip = mipValue;
            using RasterizedSurface? lowerMip = lowerValue;
            int sourceWidth = image.Width, sourceHeight = image.Height;
            byte[] source = image.UniformSample is null ? image.Pixels : [];
            if (mip is {} level)
            {
                samplingMap = SvgShaderMatrix.Concat(samplingMap, new((float)sampleWidth / level.Width, 0, 0, 0, (float)sampleHeight / level.Height, 0));
                coordinates = SvgShaderCoordinates.CreateImage(imageDrawing, samplingMap);
                source = level.Pixels;
                sourceWidth = sampleWidth = level.Width;
                sourceHeight = sampleHeight = level.Height;
                sampleBounds = new(0, 0, level.Width, level.Height);
            }
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
                            if (sourceIsSubset) SampleAffine(sampleX, sampleY).Blend(destination.AsSpan((y * outputWidth + x) * 4, 4), coverage);
                            else SamplePicture(sampleX, sampleY).Blend(destination.AsSpan((y * outputWidth + x) * 4, 4), coverage);
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
                        filtering = sampleWidth < 16384 && sampleHeight < 16384
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

            // Retain only the selected decoded view or adjacent picture mip levels.
            // Every scratch level is reserved before allocation and released on all exits.
            (RasterizedSurface? Upper, RasterizedSurface? Lower) PrepareImageLevels()
            {
                if (image.UniformSample is not null || !pictureReduction && decodeSize == (sampleWidth, sampleHeight)) return (null, null);
                SvgShaderMatrix resizeInverse = new(1f / ((float)decodeSize.Width / sampleWidth), 0, 0, 0, 1f / ((float)decodeSize.Height / sampleHeight), 0);
                int count = pictureReduction ? pictureMip + (pictureLevels.LowerWeight != 0 ? 1 : 0) : SvgImageMipMap.SelectLevel(resizeInverse, sampleWidth, sampleHeight);
                RasterizedSurface? previous = null, retainedUpper = null;
                try
                {
                    byte[] pixels = image.Pixels;
                    int stride = image.Width;
                    SvgAnalyticClipBounds bounds = sampleBounds;
                    for (int index = 0; index < count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (pictureLevels.LowerWeight != 0 && index == count - 1)
                        {
                            retainedUpper = previous;
                            previous = null;
                        }
                        int width = Math.Max(1, (bounds.Right - bounds.Left) / 2);
                        int height = Math.Max(1, (bounds.Bottom - bounds.Top) / 2);
                        RenderBudget.BudgetReservation reservation = budget.Reserve(checked((long)width * height * 4 + 64), SvgMemoryCategory.Surface);
                        RasterizedSurface next;
                        try
                        {
                            byte[] reduced = new byte[checked(width * height * 4)];
                            SvgImageMipMap.Reduce(pixels, stride, bounds, reduced, cancellationToken);
                            next = new(width, height, reduced, reservation);
                        }
                        catch { reservation.Dispose(); throw; }
                        previous?.Dispose();
                        previous = next;
                        pixels = next.Pixels;
                        stride = width;
                        bounds = new(0, 0, width, height);
                    }
                    if (!pictureReduction && (previous is not {} last || last.Width != decodeSize.Width || last.Height != decodeSize.Height))
                    {
                        RenderBudget.BudgetReservation reservation = budget.Reserve(checked((long)decodeSize.Width * decodeSize.Height * 4 + 64), SvgMemoryCategory.Surface);
                        RasterizedSurface resized;
                        try
                        {
                            byte[] reduced = new byte[checked(decodeSize.Width * decodeSize.Height * 4)];
                            SvgImageMipMap.Resize(pixels, stride, bounds, reduced, decodeSize.Width, decodeSize.Height, cancellationToken);
                            resized = new(decodeSize.Width, decodeSize.Height, reduced, reservation);
                        }
                        catch { reservation.Dispose(); throw; }
                        previous?.Dispose();
                        previous = resized;
                    }
                    (RasterizedSurface? Upper, RasterizedSurface? Lower) result = pictureLevels.LowerWeight != 0
                        ? (retainedUpper, previous) : (previous, null);
                    previous = null;
                    retainedUpper = null;
                    return result;
                }
                finally { previous?.Dispose(); retainedUpper?.Dispose(); }
            }

            // Native bitmap interpolation retains four-bit fractions and truncates
            // the bilerp before independent packed paint alpha is applied.
            SvgConstantShaderPaint Sample(long x, long y, bool filtering)
            {
                if (image.UniformSample is {} uniform) return ApplyOpacity(uniform, false);
                long bias = filtering ? 2147483648L : 65536L;
                x -= bias; y -= bias;
                int left = (int)Math.Clamp(x >> 32, -1, sampleWidth), top = (int)Math.Clamp(y >> 32, -1, sampleHeight);
                int fx = filtering ? (int)(x >> 28 & 15) : 0, fy = filtering ? (int)(y >> 28 & 15) : 0;
                int x0 = Math.Clamp(left, 0, sampleWidth - 1) + sampleBounds.Left, x1 = Math.Clamp(left + 1, 0, sampleWidth - 1) + sampleBounds.Left;
                int y0 = Math.Clamp(top, 0, sampleHeight - 1) + sampleBounds.Top, y1 = Math.Clamp(top + 1, 0, sampleHeight - 1) + sampleBounds.Top;
                int a = (y0 * sourceWidth + x0) * 4, b = (y0 * sourceWidth + x1) * 4;
                int c = (y1 * sourceWidth + x0) * 4, d = (y1 * sourceWidth + x1) * 4;
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
                SvgShaderPaint upper = SvgShaderPaint.SampleView(source, sourceWidth, sampleBounds, x, y, pictureLevels.LowerWeight == 0 ? alpha : 1);
                if (lowerMip is not {} lower || pictureLevels.LowerWeight == 0) return upper;
                SvgShaderPaint smaller = SvgShaderPaint.SampleView(lower.Pixels, lower.Width, new(0, 0, lower.Width, lower.Height),
                    x * ((float)lower.Width / sampleWidth), y * ((float)lower.Height / sampleHeight), 1);
                float weight = pictureLevels.LowerWeight;
                return new(MathF.FusedMultiplyAdd(smaller.Red - upper.Red, weight, upper.Red) * alpha,
                    MathF.FusedMultiplyAdd(smaller.Green - upper.Green, weight, upper.Green) * alpha,
                    MathF.FusedMultiplyAdd(smaller.Blue - upper.Blue, weight, upper.Blue) * alpha,
                    MathF.FusedMultiplyAdd(smaller.Alpha - upper.Alpha, weight, upper.Alpha) * alpha);
            }

            // Pinned raster-pipeline bilerp_clamp_8888 retains Q15 intermediate
            // fractions; the separate axis-aligned bitmap route retains four bits.
            // Copyright 2016 Google Inc.; 2018, 2022 Google LLC.
            // See THIRD_PARTY_NOTICES.txt for the BSD license and source pin.
            SvgConstantShaderPaint SampleAffine(float x, float y)
            {
                if (image.UniformSample is {} uniform) return ApplyOpacity(uniform, true);
                return ApplyOpacity(SvgConstantShaderPaint.SampleAffine(source, sourceWidth, sourceHeight, x, y, false, new(0, 0), sampleBounds), true);
            }

            // Borrow the retained rectangle into the current bitmap's path traversal.
            // Its parent clip selects native mask/run coverage; a separate scene loses
            // that classification. Apply viewport coverage once, during image painting.
            RasterizedSurface PrepareImageMask(SvgAuthoredPath sourcePath, SvgClipBox? clip, SvgRectangleRaster bounds)
            {
                using MappedPolygon raster = MapPolygon([], [], null, false, [], [], SvgPolygonMapping.NativeFillEdges,
                    clip, sourcePath, PathCoordinateSpace.User);
                SvgAnalyticContourPreparation preparation = new(raster.Points, raster.ContourEnds, [], false,
                    raster.Signs, raster.Mapping, raster.QuadSpans, raster.CubicSpans, raster.CompleteBounds.RoundOutForDispatch(),
                    outputWidth, outputHeight, raster.Authored, raster.AuthoredMap);
                int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
                RenderBudget.BudgetReservation reservation = budget.Reserve(checked(width * height * 4), SvgMemoryCategory.Surface);
                try
                {
                    byte[] pixels = new byte[checked(width * height * 4)];
                    SvgAnalyticPaint coveragePaint = SvgAnalyticPaint.Solid(new(0, 0, 0, 255));
                    Span<byte> sample = stackalloc byte[4];
                    foreach (SvgCpuRasterContext cpuTile in rasterGrid.GetContexts(new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom)))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        SvgCpuRasterContext context = LayerRasterContext(cpuTile);
                        SvgAnalyticClipBounds area = new(Math.Max(context.Interior.Left, bounds.Left), Math.Max(context.Interior.Top, bounds.Top),
                            Math.Min(context.Interior.Right, bounds.Right), Math.Min(context.Interior.Bottom, bounds.Bottom));
                        if (area.Left >= area.Right || area.Top >= area.Bottom) continue;
                        using PolygonRaster traversal = new(ref preparation, raster, context, ActiveClipBounds(context),
                            PolygonPaintClip(raster, context), area, budget, cancellationToken);
                        for (int y = traversal.StartY; y < traversal.EndY; y++)
                        {
                            traversal.PrepareRow(y, cancellationToken);
                            SvgAnalyticScanWindow window = traversal.GetPaintWindow();
                            for (int x = window.Left; x < window.Right; x++)
                            {
                                if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                                sample.Clear();
                                if (traversal.IsAnalytic)
                                {
                                    SvgAnalyticClipCoverage sourceClip = raster.Clip is {} rectangle
                                        ? SvgAnalyticRectangleClip.GetCoverage(rectangle, raster.ClipAntialiasing, x, y) : SvgAnalyticClipCoverage.Full;
                                    traversal.PaintPixel(x, sample, coveragePaint, sourceClip);
                                }
                                else if (traversal.MightCover(x)) sample[3] = CoverageAlpha(255, PixelCoverage(raster, raster.ContourEnds, [], false, raster.Signs, x, y, cancellationToken));
                                pixels[((y - bounds.Top) * width + x - bounds.Left) * 4 + 3] = sample[3];
                            }
                        }
                        if (!traversal.IsAnalytic) break;
                    }
                    return new(width, height, pixels, reservation);
                }
                catch { reservation.Dispose(); throw; }
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
                bool binaryViewport = clip is {} binaryClip && (binaryClip.Antialiasing & SvgClipAntialiasing.Binary) != 0
                    || activeViewport.HasMask && activeViewport.IsBinary;
                SvgAffineTransform outputMap = coordinateSpace == PathCoordinateSpace.Device ? SvgAffineTransform.Identity : binaryViewport
                    ? SvgAffineTransform.FromScaleOffset((float)drawingMap.Recording.A, (float)drawingMap.Recording.D, (float)drawingMap.Recording.E, (float)drawingMap.Recording.F)
                    : drawingMap.Recording;
                if (coordinateSpace == PathCoordinateSpace.User && sourcePlacement != SvgAffineTransform.Identity)
                    outputMap = outputMap.ComposeChecked(sourcePlacement);
                authoredMap = frame?.NativeCapturePlacement is {} nativeCapture
                    ? nativeCapture.ComposeNative(replay.SceneMap) : MapSourceTransform(outputMap, replay.SceneMap, coordinateSpace);
                // Recorded source drawings already belong to the filter layer.
                // Clip geometry keeps its own native concatenation; fitted glyphs
                // already use device coordinates rather than this recording map.
                if (!collectingViewportClip && coordinateSpace == PathCoordinateSpace.User
                    && frame is { NativeCapturePlacement: not null, FilterSourceMap: {} sourceRelation }
                    && sourceRelation.Source == replay.SceneMap)
                    authoredMap = SvgAffineTransform.FromTranslation(horizontalOffset, verticalOffset).ComposeNative(sourceRelation.Layer);
                if (authoredPath?.StrokeStyle?.NonScalingMap is {} nonScaling)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!nonScaling.StrokeTransform.TryInvert(out SvgAffineTransform inverse))
                        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Non-scaling stroke transform must be invertible.");
                    authoredMap = SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset)
                        .ComposeNative(nonScaling.SourceMap).ComposeNative(inverse);
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
                SvgAffineTransform preparationMap = coordinateSpace == PathCoordinateSpace.Device
                    ? SvgAffineTransform.Identity : drawingMap.Parameter.ComposeChecked(sourcePlacement);
                authored = replay.PrepareSource(preparationMap, isEvenOdd, budget, cancellationToken);
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
            => !activeViewport.HasMask ? alpha : (byte)((alpha * activeViewport.Mask.GetAlphaAtOffset(outputWidth, (y * outputWidth + x) * 4) + 127) / 255);

        bool IsOpaqueClipRectangle(SvgRasterClipMask pixels, SvgAnalyticClipBounds bounds)
        {
            if (pixels.Rectangle is not null)
            {
                if (bounds.Left >= bounds.Right || bounds.Top >= bounds.Bottom) return true;
                cancellationToken.ThrowIfCancellationRequested();
                return pixels.GetAlpha(outputWidth, bounds.Left, bounds.Top) == 255
                    && pixels.GetAlpha(outputWidth, bounds.Right - 1, bounds.Top) == 255
                    && pixels.GetAlpha(outputWidth, bounds.Left, bounds.Bottom - 1) == 255
                    && pixels.GetAlpha(outputWidth, bounds.Right - 1, bounds.Bottom - 1) == 255;
            }
            for (int y = bounds.Top; y < bounds.Bottom; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = bounds.Left; x < bounds.Right; x++)
                {
                    if (pixels.GetAlpha(outputWidth, x, y) != 255) return false;
                }
            }
            return true;
        }

        SvgAnalyticClipCoverage PolygonClipCoverage(MappedPolygon raster, int x, int y)
        {
            SvgAnalyticClipCoverage coverage = raster.Clip is null ? SvgAnalyticClipCoverage.Full
                : SvgAnalyticRectangleClip.GetCoverage(raster.Clip.Value, raster.ClipAntialiasing, x, y);
            return !activeViewport.HasMask ? coverage
                : SvgAnalyticClipCoverage.FromMask(coverage.Apply(activeViewport.Mask.GetAlphaAtOffset(outputWidth, (y * outputWidth + x) * 4)));
        }

        SvgAnalyticClipBounds CoverageBounds(byte[] pixels, SvgAnalyticClipBounds area, out bool rectangle)
        {
            int left = area.Right, top = area.Bottom, right = area.Left, bottom = area.Top;
            long nonzero = 0;
            bool binary = true;
            for (int y = area.Top; y < area.Bottom; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = area.Left; x < area.Right; x++)
                {
                    if ((x & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    byte alpha = pixels[(y * outputWidth + x) * 4 + 3];
                    if (alpha == 0) continue;
                    nonzero++;
                    binary &= alpha == 255;
                    left = Math.Min(left, x); top = Math.Min(top, y);
                    right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1);
                }
            }
            rectangle = binary && (nonzero == 0 || nonzero == (long)(right - left) * (bottom - top));
            return left < right && top < bottom ? new(left, top, right, bottom) : new(area.Left, area.Top, area.Left, area.Top);
        }

        SvgAnalyticClipBounds? ActiveClipBounds(SvgCpuRasterContext context)
        {
            SvgCpuClipCoverage? coverage = activeConstructionClip.Length != 0 ? activeConstructionCoverage : activeViewport.Coverage;
            SvgAnalyticClipBounds? current = activeConstructionClip.Length != 0 ? activeConstructionBounds : activeViewport.Bounds;
            if (coverage is not null && coverage.TryGetBounds(context.BitmapClip, out SvgAnalyticClipBounds local)) current = local;
            else
            {
                byte[] tape = activeConstructionClip.Length != 0 ? activeConstructionClip : activeViewport.Pixels;
                if (activeConstructionClip.Length == 0 && activeViewport.Rectangle is not null && current is {} rectangleBounds)
                    current = new(Math.Max(rectangleBounds.Left, context.BitmapClip.Left), Math.Max(rectangleBounds.Top, context.BitmapClip.Top),
                        Math.Min(rectangleBounds.Right, context.BitmapClip.Right), Math.Min(rectangleBounds.Bottom, context.BitmapClip.Bottom));
                if (tape.Length != 0 && current is {} bounds)
                {
                    int left = Math.Max(bounds.Left, context.BitmapClip.Left), top = Math.Max(bounds.Top, context.BitmapClip.Top);
                    int right = Math.Max(left, Math.Min(bounds.Right, context.BitmapClip.Right)), bottom = Math.Max(top, Math.Min(bounds.Bottom, context.BitmapClip.Bottom));
                    current = CoverageBounds(tape, new(left, top, right, bottom), out _);
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
            if (!activeViewport.HasMask || activeViewport.IsBinary) return paintClip;
            SvgAnalyticClipBounds clip = context.BitmapClip;
            // Coverage bounds belong to the bitmap's actual clip tape. The
            // allocation/request enclosure can omit its antialiased fringe.
            if (ActiveClipBounds(context) is {} bounds)
                clip = new(Math.Max(clip.Left, bounds.Left), Math.Max(clip.Top, bounds.Top), Math.Min(clip.Right, bounds.Right), Math.Min(clip.Bottom, bounds.Bottom));
            // Native raster clips collapse a wholly opaque rectangular bitmap
            // intersection to BW. Preserve its ordinary mask/run material routing.
            return IsOpaqueClipRectangle(activeViewport.Mask, clip) ? paintClip : SvgAnalyticPaintClip.Antialiased;
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
            if (surfaceMode == RasterSurfaceMode.PremultipliedFilter && fontPreblend.IsEmpty && !collectingViewportClip
                && authoredPath is { IsRectangleFill: true, StrokeStyle: null } rectangleSource
                && rectangleSource.Commands.Length == 5 && raster.AuthoredMap.IsAxisAligned
                && raster.QuadSpans.Length == 0 && raster.CubicSpans.Length == 0)
            {
                SvgRectangleCoverage coverage = SvgRectangleCoverage.Create(raster.CompleteBounds.Left, raster.CompleteBounds.Top,
                    raster.CompleteBounds.Right, raster.CompleteBounds.Bottom, outputWidth, outputHeight);
                SvgClipBox? clip = raster.Clip is {} edges ? new SvgClipBox(edges.Left, edges.Top, edges.Right - edges.Left, edges.Bottom - edges.Top) { Antialiasing = raster.ClipAntialiasing } : null;
                RasterizePreparedRectangle(new(raster.StartX, raster.StartY, raster.EndX, raster.EndY, coverage, clip), glyphPaint, destination);
                return;
            }
            SvgAnalyticPaint analyticPaint = fontPreblend.Length == 0 ? glyphPaint : SvgAnalyticPaint.Solid(new SvgColor(0, 0, 0, 255));
            // Rectangle construction already handles its own clip wrapper. Only
            // complex binary regions forward fringe pairs as ordinary alpha runs.
            if (activeViewport.HasMask && activeViewport.IsBinary && !activeViewport.IsRectangle && !maskLuminanceLayer) analyticPaint = analyticPaint.ThroughBinaryRegion();
            ReadOnlySpan<int> contourEnds = raster.ContourEnds;
            ReadOnlySpan<int> nestingSigns = raster.Signs is int[] signs ? signs : Array.Empty<int>();
            SvgAnalyticContourPreparation preparation = new(raster.Points, contourEnds, nestingSigns, polygon.IsEvenOdd, raster.Signs, raster.Mapping, raster.QuadSpans, raster.CubicSpans, raster.CompleteBounds.RoundOutForDispatch(), outputWidth, outputHeight, raster.Authored, raster.AuthoredMap);
            preparation.RectangleFrame = PrepareRectangleFrame(authoredPath);
            preparation.RectangleFill = PrepareRectangleFill(authoredPath, raster.AuthoredMap);
            Span<byte> clipPixel = stackalloc byte[4];
            foreach (SvgCpuRasterContext cpuTile in rasterGrid.GetContexts(new(raster.StartX, raster.StartY, raster.EndX, raster.EndY)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgCpuRasterContext context = LayerRasterContext(cpuTile);
                if (context.Interior.Right <= context.Interior.Left || context.Interior.Bottom <= context.Interior.Top) continue;
                SvgAnalyticClipBounds paintArea = collectingClipCoverage is null ? context.Interior : context.BitmapClip;
                using PolygonRaster traversal = new(ref preparation, raster, context, ActiveClipBounds(context),
                    PolygonPaintClip(raster, context), paintArea, budget,
                    cancellationToken);
                bool useAnalytic = traversal.IsAnalytic;
                int startY = traversal.StartY, endY = traversal.EndY;
                int clipLeft = context.BitmapClip.Right, clipTop = context.BitmapClip.Bottom, clipRight = context.BitmapClip.Left, clipBottom = context.BitmapClip.Top;
                for (int y = startY; y < endY; y++)
                {
                    traversal.PrepareRow(y, cancellationToken);
                    SvgAnalyticScanWindow paintWindow = traversal.GetPaintWindow();
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
                                    : activeConstructionCoverage.GetAlpha(context.BitmapClip, new(activeConstructionClip), outputWidth, x, y);
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
                        else BlendSourceOver(destination.AsSpan(offset, 4), WithCoverage(polygon.Color, pixelCoverage * (!activeViewport.HasMask ? 1 : activeViewport.Mask.GetAlphaAtOffset(outputWidth, offset) / 255.0)));
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
        // Native interval integral from SkGradientBaseShader.cpp at the pinned revision.
        // Copyright 2016 Google Inc.; see THIRD_PARTY_NOTICES.txt. Inputs are borrowed;
        // preparation allocates no storage and charges/checks each bounded interval.
        SvgShaderPaint PrepareGradientFactory(ReadOnlySpan<SvgGradientStop> stops, SvgGradientSpread spread, out SvgColor luminance)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stops.Length > 256) throw new SvgException(SvgErrorCode.LimitExceeded, "Gradient factory preparation exceeds the supported stop limit.");
            luminance = default;
            if (stops.Length == 0) return default;
            if (stops.Length == 1 || spread == SvgGradientSpread.Pad)
            {
                SvgGradientStop last = stops[^1];
                luminance = last.Color;
                return new(last.Color.Red / 255f * last.Alpha, last.Color.Green / 255f * last.Alpha, last.Color.Blue / 255f * last.Alpha, last.Alpha);
            }
            Vector4 mean = default;
            for (int index = 0; index < stops.Length - 1; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                float first = Math.Clamp((float)stops[index].Offset, 0, 1);
                float second = Math.Clamp((float)stops[index + 1].Offset, first, 1);
                Vector4 a = Color(stops[index]), b = Color(stops[index + 1]);
                if (index == 0 && first > 0) mean += first * a;
                if (index == stops.Length - 2 && second < 1) mean += (1 - second) * b;
                mean += .5f * (second - first) * (b + a);
            }
            float alpha = Math.Clamp(mean.W, 0, 1);
            luminance = new(Quantize(mean.X), Quantize(mean.Y), Quantize(mean.Z), 255);
            return new(mean.X * alpha, mean.Y * alpha, mean.Z * alpha, alpha);

            static Vector4 Color(SvgGradientStop stop) => new(stop.Color.Red / 255f, stop.Color.Green / 255f, stop.Color.Blue / 255f, stop.Alpha);
            static byte Quantize(float value) => (byte)Math.Clamp((int)(value * 255f + .5f), 0, 255);
        }

        SvgGradientRamp PrepareGradientRamp(ReadOnlySpan<SvgGradientStop> stops, SvgGradientSpread spread)
        {
            SvgGradientRamp ramp = SvgGradientRamp.Create(stops, spread, budget, cancellationToken);
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
            bool constantFactory = SvgShaderCoordinates.IsConstantFactory(definition);
            SvgShaderCoordinates nativeCoordinates = constantFactory ? default : SvgShaderCoordinates.CreateLinear(definition, box, drawingMap.Device.A, drawingMap.Device.D, drawingMap.Device.E, drawingMap.Device.F, null, sourcePlacement,
                definition.SourcePaint?.Data is { IsMapped: true } viewportSourcePaint ? NativeViewportReference(viewportSourcePaint.Referencing) : null);
            using SvgGradientRamp ramp = constantFactory ? default : PrepareGradientRamp(definition.Stops, definition.Spread);
            SvgShaderSampler shader = constantFactory
                ? SvgShaderSampler.Constant(PrepareGradientFactory(definition.Stops, definition.Spread, out _), ShaderAlpha(outline.Opacity))
                : SvgShaderSampler.Linear(definition, box, ramp, nativeCoordinates, ShaderSamplingContext(), ShaderAlpha(outline.Opacity));
            if (RasterizeShaderHairline(authoredPath, outline.Clip, ref shader, destination)) return;

            ReadOnlySpan<int> contourEnds = raster.ContourEnds;
            ReadOnlySpan<int> nestingSigns = raster.Signs is int[] signs ? signs : Array.Empty<int>();
            SvgAnalyticContourPreparation preparation = new(raster.Points, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, raster.Mapping, raster.QuadSpans, raster.CubicSpans, raster.CompleteBounds.RoundOutForDispatch(), outputWidth, outputHeight, raster.Authored, raster.AuthoredMap);
            preparation.RectangleFrame = PrepareRectangleFrame(authoredPath);
            preparation.RectangleFill = PrepareRectangleFill(authoredPath, raster.AuthoredMap);
            foreach (SvgCpuRasterContext cpuTile in rasterGrid.GetContexts(new(raster.StartX, raster.StartY, raster.EndX, raster.EndY)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgCpuRasterContext context = LayerRasterContext(cpuTile);
                if (context.Interior.Right <= context.Interior.Left || context.Interior.Bottom <= context.Interior.Top) continue;
                SvgAnalyticClipBounds paintArea = context.Interior;
                using PolygonRaster traversal = new(ref preparation, raster, context, ActiveClipBounds(context),
                    PolygonPaintClip(raster, context), paintArea, budget,
                    cancellationToken);
                bool useAnalytic = traversal.IsAnalytic;
                int startY = traversal.StartY, endY = traversal.EndY;
                for (int y = startY; y < endY; y++)
                {
                traversal.PrepareRow(y, cancellationToken);
                SvgAnalyticScanWindow paintWindow = traversal.GetPaintWindow();
                int rowStartX = paintWindow.Left, rowEndX = paintWindow.Right;
                for (int x = rowStartX; x < rowEndX; x++)
                {
                    if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                    double pixelCoverage = !useAnalytic && traversal.MightCover(x) ? PixelCoverage(raster, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, x, y, cancellationToken) : 0.0;
                    int offset = (y * outputWidth + x) * 4;
                    SvgAnalyticPaint sample = shader.Sample(x, y, cancellationToken);
                    if (useAnalytic) traversal.PaintPixel(x, destination.AsSpan(offset, 4), sample, PolygonClipCoverage(raster, x, y));
                    else sample.Blend(destination.AsSpan(offset, 4), ViewportAlpha(CoverageAlpha(255, pixelCoverage), x, y), SvgAnalyticPaintKind.Direct);
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
            bool constantFactory = SvgShaderCoordinates.IsConstantFactory(definition);
            SvgShaderCoordinates nativeCoordinates = constantFactory ? default : SvgShaderCoordinates.CreateRadial(definition, box, drawingMap.Device.A, drawingMap.Device.D, drawingMap.Device.E, drawingMap.Device.F, sourcePlacement,
                NativeRadialViewportReference(definition, box));
            if (definition.Radius < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Radial gradient radii must not be negative.");
            using SvgGradientRamp ramp = constantFactory ? default : PrepareGradientRamp(definition.Stops, definition.Spread);

            SvgShaderSampler shader = constantFactory
                ? SvgShaderSampler.Constant(PrepareGradientFactory(definition.Stops, definition.Spread, out _), ShaderAlpha(outline.Opacity))
                : SvgShaderSampler.Radial(definition, box, ramp, nativeCoordinates, ShaderSamplingContext(), ShaderAlpha(outline.Opacity));
            if (RasterizeShaderHairline(authoredPath, outline.Clip, ref shader, destination)) return;

            ReadOnlySpan<int> contourEnds = raster.ContourEnds;
            ReadOnlySpan<int> nestingSigns = raster.Signs is int[] signs ? signs : Array.Empty<int>();
            SvgAnalyticContourPreparation preparation = new(raster.Points, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, raster.Mapping, raster.QuadSpans, raster.CubicSpans, raster.CompleteBounds.RoundOutForDispatch(), outputWidth, outputHeight, raster.Authored, raster.AuthoredMap);
            preparation.RectangleFrame = PrepareRectangleFrame(authoredPath);
            preparation.RectangleFill = PrepareRectangleFill(authoredPath, raster.AuthoredMap);
            foreach (SvgCpuRasterContext cpuTile in rasterGrid.GetContexts(new(raster.StartX, raster.StartY, raster.EndX, raster.EndY)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgCpuRasterContext context = LayerRasterContext(cpuTile);
                if (context.Interior.Right <= context.Interior.Left || context.Interior.Bottom <= context.Interior.Top) continue;
                SvgAnalyticClipBounds paintArea = context.Interior;
                using PolygonRaster traversal = new(ref preparation, raster, context, ActiveClipBounds(context),
                    PolygonPaintClip(raster, context), paintArea, budget,
                    cancellationToken);
                bool useAnalytic = traversal.IsAnalytic;
                int startY = traversal.StartY, endY = traversal.EndY;
                for (int y = startY; y < endY; y++)
                {
                traversal.PrepareRow(y, cancellationToken);
                SvgAnalyticScanWindow paintWindow = traversal.GetPaintWindow();
                int rowStartX = paintWindow.Left, rowEndX = paintWindow.Right;
                for (int x = rowStartX; x < rowEndX; x++)
                {
                    if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                    double pixelCoverage = !useAnalytic && traversal.MightCover(x) ? PixelCoverage(raster, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, x, y, cancellationToken) : 0.0;
                    int offset = (y * outputWidth + x) * 4;
                    SvgAnalyticPaint sample = shader.Sample(x, y, cancellationToken);
                    if (useAnalytic) traversal.PaintPixel(x, destination.AsSpan(offset, 4), sample, PolygonClipCoverage(raster, x, y));
                    else sample.Blend(destination.AsSpan(offset, 4), ViewportAlpha(CoverageAlpha(255, pixelCoverage), x, y), SvgAnalyticPaintKind.Direct);
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
            if (!TryMapPatternTile(source, activePaint.RecordingTransform, SvgAffineTransform.Identity, out SvgPatternGeometry? mappedTile)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills under viewBox or output scaling are not implemented.");
            SvgPatternGeometry tile = mappedTile ?? source;
            using SvgPatternTileLease texture = BorrowPatternTile(source, tile, false);
            byte[] blitted = texture.Surface.Pixels;
            SvgShaderCoordinates imageCoordinates = tile.NativeImageMap is {} imageMap ? SvgShaderCoordinates.CreateImage(imageMap, tile.NativeImageLocal ?? throw new InvalidOperationException("Image sampling requires its local matrix.")) : default;
            SvgShaderSampler shader = SvgShaderSampler.Pattern(tile, blitted, imageCoordinates, ShaderSamplingContext(), ShaderAlpha(tile.Opacity));
            if (RasterizeShaderHairline(authoredPath, outline.Clip, ref shader, destination)) return;
            ReadOnlySpan<int> contourEnds = raster.ContourEnds;
            ReadOnlySpan<int> nestingSigns = raster.Signs is int[] signs ? signs : Array.Empty<int>();
            SvgAnalyticContourPreparation preparation = new(raster.Points, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, raster.Mapping, raster.QuadSpans, raster.CubicSpans, raster.CompleteBounds.RoundOutForDispatch(), outputWidth, outputHeight, raster.Authored, raster.AuthoredMap);
            preparation.RectangleFrame = PrepareRectangleFrame(authoredPath);
            preparation.RectangleFill = PrepareRectangleFill(authoredPath, raster.AuthoredMap);
            foreach (SvgCpuRasterContext cpuTile in rasterGrid.GetContexts(new(raster.StartX, raster.StartY, raster.EndX, raster.EndY)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgCpuRasterContext context = LayerRasterContext(cpuTile);
                if (context.Interior.Right <= context.Interior.Left || context.Interior.Bottom <= context.Interior.Top) continue;
                SvgAnalyticClipBounds paintArea = context.Interior;
                using PolygonRaster traversal = new(ref preparation, raster, context, ActiveClipBounds(context),
                    PolygonPaintClip(raster, context), paintArea, budget,
                    cancellationToken);
                bool useAnalytic = traversal.IsAnalytic;
                int startY = traversal.StartY, endY = traversal.EndY;
                for (int y = startY; y < endY; y++)
                {
                traversal.PrepareRow(y, cancellationToken);
                SvgAnalyticScanWindow paintWindow = traversal.GetPaintWindow();
                int rowStartX = paintWindow.Left, rowEndX = paintWindow.Right;
                for (int x = rowStartX; x < rowEndX; x++)
                {
                    if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                    double pixelCoverage = !useAnalytic && traversal.MightCover(x) ? PixelCoverage(raster, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, x, y, cancellationToken) : 0.0;
                    int offset = (y * outputWidth + x) * 4;
                    SvgAnalyticPaint sample = shader.Sample(x, y, cancellationToken);
                    if (useAnalytic) traversal.PaintPixel(x, destination.AsSpan(offset, 4), sample, PolygonClipCoverage(raster, x, y));
                    else sample.Blend(destination.AsSpan(offset, 4), ViewportAlpha(CoverageAlpha(255, pixelCoverage), x, y), SvgAnalyticPaintKind.Direct);
                }
                }
                // Structural fallback belongs to the original fill and runs once.
                if (!useAnalytic) break;
            }
        }

        bool NeedsPatternMapping(SvgPatternGeometry pattern)
            => pattern.Coordinates is not null || pattern.VectorContent is not null || horizontalScale != 1 || verticalScale != 1 || horizontalOffset != 0 || verticalOffset != 0 || pattern.ShaderPlacement != SvgAffineTransform.Identity;

        // Picture and glyph shaders share the native drawing stages. Local paint
        // matrices stay separate until sampling; no source or layout bounds change.
        bool TryMapShaderDraw(SvgAffineTransform target, SvgRecordedTransform? recording, out SvgShaderMatrix drawing)
        {
            SvgShaderMatrix output = SvgShaderMatrix.FromAffine(drawingMap.Recording);
            if (sourcePlacement != SvgAffineTransform.Identity)
                output = SvgShaderMatrix.Concat(output, SvgShaderMatrix.FromAffine(sourcePlacement));
            drawing = SvgShaderMatrix.Concat(output, SvgShaderMatrix.FromAffine(target));
            SvgShaderMatrix external = frame is null
                ? SvgShaderMatrix.FromAffine(rootImage.Projection) : output;
            if (recording is not null)
            {
                if (!recording.TryMap(external, authoredRootMap, target, budget, cancellationToken, out drawing)) return false;
            }
            else if (authoredRootMap != SvgAffineTransform.Identity)
                drawing = SvgShaderMatrix.Concat(external, SvgShaderMatrix.FromAffine(authoredRootMap.Compose(target)));
            drawing = SvgShaderMatrix.FromAffine(drawingMap.PlaceRecorded(new(drawing.ScaleX, drawing.SkewY,
                drawing.SkewX, drawing.ScaleY, drawing.TranslateX, drawing.TranslateY)));
            return drawing.IsFinite;
        }

        bool TryMapPatternTile(SvgPatternGeometry pattern, SvgRecordedTransform? recording, SvgAffineTransform glyphMap, out SvgPatternGeometry? mapped)
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
                SvgShaderMatrix localMap = new((float)shader.A, (float)shader.C, (float)shader.E, (float)shader.B, (float)shader.D, (float)shader.F);
                SvgShaderMatrix drawMap;
                if (pattern.PaintMatrices is {} stages)
                {
                    SvgAffineTransform target = stages.Target, local = stages.Pattern;
                    if (glyphMap != SvgAffineTransform.Identity)
                    {
                        // Glyph drawing and local paint narrow separately. Flattening
                        // their mathematical cancellation changes picture texture sizes.
                        local = SvgTextShader.RebaseLocal(glyphMap, target, local);
                        target = glyphMap;
                    }
                    if (!TryMapShaderDraw(target, recording, out drawMap)) return false;
                    localMap = new((float)local.A, (float)local.C, (float)local.E, (float)local.B, (float)local.D, (float)local.F);
                }
                else
                {
                    drawMap = new((float)horizontalScale, 0, (float)horizontalOffset, 0, (float)verticalScale, (float)verticalOffset);
                    if (sourcePlacement != SvgAffineTransform.Identity)
                        drawMap = SvgShaderMatrix.Concat(drawMap, SvgShaderMatrix.FromAffine(sourcePlacement));
                }
                SvgShaderMatrix map = SvgShaderMatrix.Concat(drawMap, localMap);
                // Picture tiles retain their source axes. Rotation/reflection change the
                // sampling matrix, while column lengths choose the bounded texture scale.
                (float rasterScaleX, float rasterScaleY) = map.GetPictureScale();
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
                    NativeImageMap = drawMap,
                    NativeImageLocal = imageLocal,
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
            if (!TryMapPatternTile(pattern, activePaint.RecordingTransform, SvgAffineTransform.Identity, out SvgPatternGeometry? mapped)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills under viewBox or output scaling are not implemented.");
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
                patternTileCache = retain ? SvgPatternTileCache.Retain(original, mapped, prepared, budget) : null;
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
                using RenderBudget.BudgetReservation setup = budget.Reserve(256, SvgMemoryCategory.Geometry);
                SvgRenderLimits limits = options.Limits;
                SvgRenderLimits tileLimits = limits;
                SvgRenderOptions tileOptions = new(new SvgViewport(pattern.TextureWidth, pattern.TextureHeight), 1, SvgColor.Transparent, tileLimits);
                SvgSceneStorage scene = content.Scene;
                SvgRoot tileRoot = new(null, null, pattern.RasterSourceBox ?? throw new InvalidOperationException("Vector tile replay requires a mapped source grid."), scene.Geometries, scene.Commands, false, scene);
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
                return rendered;
            }
            RenderBudget.BudgetReservation reservation = budget.Reserve(checked(tilePixels * 4), SvgMemoryCategory.Surface);
            try
            {
                byte[] pixels = new byte[(int)tilePixels * 4];
                BlitPatternTile(pattern, pixels);
                return new RasterizedSurface(pattern.TextureWidth, pattern.TextureHeight, pixels, reservation);
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
            cancellationToken.ThrowIfCancellationRequested();
            double left = box.X - pattern.OriginX, top = box.Y - pattern.OriginY;
            double right = left + box.Width, bottom = top + box.Height;
            SvgRectangleCoverage coverage = SvgRectangleCoverage.Create(left, top, right, bottom, pattern.TextureWidth, pattern.TextureHeight);
            SvgClipBox? clip = box.Clip is {} bounds ? new SvgClipBox(bounds.X - pattern.OriginX, bounds.Y - pattern.OriginY, bounds.Width, bounds.Height) { Antialiasing = bounds.Antialiasing } : null;
            int startX = ClampFloor(clip is {} cx ? Math.Max(left, cx.X) : left, pattern.TextureWidth);
            int startY = ClampFloor(clip is {} cy ? Math.Max(top, cy.Y) : top, pattern.TextureHeight);
            int endX = Math.Max(startX, ClampCeiling(clip is {} ex ? Math.Min(right, ex.Right) : right, pattern.TextureWidth));
            int endY = Math.Max(startY, ClampCeiling(clip is {} ey ? Math.Min(bottom, ey.Bottom) : bottom, pattern.TextureHeight));
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
            SvgPatternSample sample = SvgPatternSample.Prepare(pattern, x, y,
                pattern.NativeImageMap is null ? default : ShaderBitmapOrigin(x, y), ref imageCoordinates);
            return sample.Sample(tile, ShaderAlpha(MultiplyAlpha(pattern.Opacity, opacity)));
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
            bool constantFactory = SvgShaderCoordinates.IsConstantFactory(definition);
            SvgShaderCoordinates nativeCoordinates = constantFactory ? default : SvgShaderCoordinates.CreateLinear(definition, box, drawingMap.Device.A, drawingMap.Device.D, drawingMap.Device.E, drawingMap.Device.F, null, sourcePlacement,
                definition.SourcePaint?.Data is { IsMapped: true } viewportSourcePaint ? NativeViewportReference(viewportSourcePaint.Referencing) : null);
            if (definition.Stops.Length > 0 && constantFactory)
            {
                RasterizeConstantGradient(definition.Stops, definition.Spread, area, opacity, paint, destination);
                return;
            }
            SvgRectangleRaster raster = PrepareRectangle(area);
            int startX = raster.Left, endX = raster.Right, startY = raster.Top, endY = raster.Bottom;
            double directionX = definition.X2 - definition.X1;
            double directionY = definition.Y2 - definition.Y1;
            double denominator = directionX * directionX + directionY * directionY;
            if (!paint || endX <= startX || endY <= startY || (denominator == 0 && definition.Stops.Length != 1)) return;
            using SvgGradientRamp ramp = constantFactory ? default : PrepareGradientRamp(definition.Stops, definition.Spread);

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
                position = SvgGradientRamp.Spread(position, definition.Spread);
                int offset = (y * outputWidth + x) * 4;
                ramp.SampleAbsolute(position, ShaderAlpha(opacity), GradientDither(x, y)).Blend(destination.AsSpan(offset, 4), ViewportAlpha(raster.GetAlpha(x, y), x, y));
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
            bool constantFactory = SvgShaderCoordinates.IsConstantFactory(definition);
            SvgShaderCoordinates nativeCoordinates = constantFactory ? default : SvgShaderCoordinates.CreateRadial(definition, box, drawingMap.Device.A, drawingMap.Device.D, drawingMap.Device.E, drawingMap.Device.F, sourcePlacement,
                NativeRadialViewportReference(definition, box));
            if (definition.Stops.Length > 0 && constantFactory)
            {
                RasterizeConstantGradient(definition.Stops, definition.Spread, area, opacity, paint, destination);
                return;
            }
            SvgRectangleRaster raster = PrepareRectangle(area);
            int startX = raster.Left, endX = raster.Right, startY = raster.Top, endY = raster.Bottom;
            if (paint && (definition.Radius <= 0 && definition.Stops.Length != 1)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive-radius radial gradients are implemented.");
            if (!paint || endX <= startX || endY <= startY) return;
            using SvgGradientRamp ramp = constantFactory ? default : PrepareGradientRamp(definition.Stops, definition.Spread);

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
                if (definition.Stops.Length == 1 && !nativeCoordinates.MasksRadialSamples) position = 0;
                else
                if (!nativeCoordinates.TryRadial(x, y, ShaderBitmapOrigin(x, y), out SvgRadialShaderSample sample))
                    position = SvgShaderCoordinates.EvaluateRadial(new(rayX, rayY), new(offsetX, offsetY), radius, focalRadius);
                else
                {
                    if (!sample.IsVisible) continue;
                    position = sample.Position;
                }
                position = SvgGradientRamp.Spread(position, definition.Spread);
                int offset = (y * outputWidth + x) * 4;
                ramp.SampleAbsolute(position, ShaderAlpha(opacity), GradientDither(x, y)).Blend(destination.AsSpan(offset, 4), ViewportAlpha(raster.GetAlpha(x, y), x, y));
            }
            }
        }

        void RasterizeConstantGradient(ReadOnlySpan<SvgGradientStop> stops, SvgGradientSpread spread, SvgRectangle area, byte opacity, bool paint, byte[] destination)
        {
            SvgRectangleRaster raster = PrepareRectangle(area);
            if (!paint) return;
            SvgConstantShaderPaint color = PrepareGradientFactory(stops, spread, out _).AsConstant(ShaderAlpha(opacity));
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
            SvgAffineTransform device = drawingMap.Device;
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
                if (!TryMapPatternTile(originalPattern, activePaint.RecordingTransform, glyph.Source?.GlyphMap ?? SvgAffineTransform.Identity, out SvgPatternGeometry? mapped))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text pattern coordinates are not finite and invertible.");
                if (originalPattern.Coordinates?.IsEmpty == true) return;
                glyphPattern = mapped ?? originalPattern;
            }
            using SvgPatternTileLease glyphTile = originalPattern is null || glyphPattern is null ? default : BorrowPatternTile(originalPattern, glyphPattern, true);
            SvgGradientPaintMatrices? textMatrices = null;
            if (glyphLinear?.SourcePaint?.Data is {} textSource && glyph.Source is { GlyphMap: var glyphMap } && glyphMap != SvgAffineTransform.Identity)
            {
                SvgAffineTransform local = SvgTextShader.RebaseLocal(glyphMap, textSource.Referencing, new(textSource.Local.A, textSource.Local.B, textSource.Local.C, textSource.Local.D, textSource.Local.E, textSource.Local.F));
                textMatrices = new(new((float)local.A, (float)local.C, (float)local.E, (float)local.B, (float)local.D, (float)local.F), glyphMap);
            }
            if (glyphRadial is { NativePaintMatrices: {} radialMatrices } radial && glyph.Source is { GlyphMap: var radialGlyphMap } && radialGlyphMap != SvgAffineTransform.Identity)
            {
                SvgShaderMatrix matrix = radialMatrices.Local;
                SvgAffineTransform local = SvgTextShader.RebaseLocal(radialGlyphMap, radialMatrices.Referencing, new(matrix.ScaleX, matrix.SkewY, matrix.SkewX, matrix.ScaleY, matrix.TranslateX, matrix.TranslateY));
                glyphRadial = radial with { NativePaintMatrices = new(new((float)local.A, (float)local.C, (float)local.E, (float)local.B, (float)local.D, (float)local.F), radialGlyphMap) };
            }
            bool constantGlyphFactory = glyphLinear is {} factoryLinear ? SvgShaderCoordinates.IsConstantFactory(factoryLinear)
                : glyphRadial is {} factoryRadial && SvgShaderCoordinates.IsConstantFactory(factoryRadial);
            SvgShaderMatrix? glyphReference = null;
            if (!constantGlyphFactory && (glyphLinear is not null || glyphRadial is not null)
                && glyph.Source is { GlyphMap: var paintGlyphMap } && paintGlyphMap != SvgAffineTransform.Identity)
            {
                if (!TryMapShaderDraw(paintGlyphMap, activePaint.RecordingTransform, out SvgShaderMatrix drawing))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text shader placement must be finite and invertible.");
                glyphReference = drawing;
            }
            SvgShaderCoordinates glyphCoordinates = constantGlyphFactory ? default : glyphLinear is {} linearCoordinates
                ? SvgShaderCoordinates.CreateLinear(linearCoordinates, default, device.A, device.D, device.E, device.F, textMatrices, sourcePlacement, glyphReference)
                : glyphRadial is {} radialCoordinates ? SvgShaderCoordinates.CreateRadial(radialCoordinates, default, device.A, device.D, device.E, device.F, sourcePlacement, glyphReference)
                : glyphPattern?.NativeImageMap is {} imageMap ? SvgShaderCoordinates.CreateImage(imageMap, glyphPattern.NativeImageLocal ?? throw new InvalidOperationException("Text image sampling requires its local matrix.")) : default;
            SvgColor glyphFactoryLuminance = default;
            SvgShaderPaint glyphFactoryColor = default;
            if (constantGlyphFactory)
            {
                if (glyphLinear is {} constantLinear) glyphFactoryColor = PrepareGradientFactory(constantLinear.Stops, constantLinear.Spread, out glyphFactoryLuminance);
                else if (glyphRadial is {} constantRadial) glyphFactoryColor = PrepareGradientFactory(constantRadial.Stops, constantRadial.Spread, out glyphFactoryLuminance);
            }
            using SvgGradientRamp glyphRamp = constantGlyphFactory ? default : glyphLinear is {} linearRamp
                ? PrepareGradientRamp(linearRamp.Stops, linearRamp.Spread)
                : glyphRadial is {} radialRamp ? PrepareGradientRamp(radialRamp.Stops, radialRamp.Spread) : default;
            if (glyphSource is SvgGlyphRasterSource source && (OperatingSystem.IsLinux() || OperatingSystem.IsWindows()))
            {
                if (glyph.StrokePaint is {} strokePaint)
                {
                    using SvgAuthoredPath path = source.CreateStrokePath(strokePaint, device.A, device.E, device.D, device.F, budget, cancellationToken);
                    Span<byte> preblend = stackalloc byte[256];
                    SvgColor color = GlyphLuminance();
                    if (OperatingSystem.IsWindows()) SvgFontMaskPreblend.BuildWindowsGrayTable(preblend, color, cancellationToken);
                    else SvgFontMaskPreblend.BuildLinuxGrayTable(preblend, color, cancellationToken);
                    RasterizeMappedBitmap(path, preblend);
                    return;
                }
                if (source.HasGlyphMap && OperatingSystem.IsLinux())
                {
                    using SvgAuthoredPath path = source.CreateMappedPath(device.A, device.E, device.D, device.F, budget, cancellationToken);
                    Span<byte> preblend = stackalloc byte[256];
                    SvgColor color = GlyphLuminance();
                    SvgFontMaskPreblend.BuildLinuxGrayTable(preblend, color, cancellationToken);
                    RasterizeMappedBitmap(path, preblend);
                    return;
                }
                if (source.HasGlyphMap || source.UsesDeviceFitting(budget, cancellationToken))
                {
                    if (OperatingSystem.IsWindows() && (source.HasGlyphMap || source.HasCompleteTrueTypeProfile || source.HasTrueTypePrograms(budget, cancellationToken)))
                    {
                        using OpenTypeGlyphContourStorage contours = source.CreateWindowsContours(device.A, device.E, device.D, device.F, budget, cancellationToken);
                        int crossingCount = checked(contours.Segments.Length * 2);
                        using RenderBudget.BudgetReservation? crossingReservation = crossingCount == 0 ? null : budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGlyphLcdCoverage.Crossing>(crossingCount), SvgMemoryCategory.Temporary);
                        SvgGlyphLcdCoverage.Crossing[] crossings = crossingCount == 0 ? [] : new SvgGlyphLcdCoverage.Crossing[crossingCount];
                        Span<byte> preblend = stackalloc byte[256];
                        SvgColor color = GlyphLuminance();
                        SvgFontMaskPreblend.BuildWindowsGrayTable(preblend, color, cancellationToken);
                        double lcdMinimumX = double.PositiveInfinity, lcdMinimumY = double.PositiveInfinity, lcdMaximumX = double.NegativeInfinity, lcdMaximumY = double.NegativeInfinity;
                        foreach (OpenTypeQuadraticSegment segment in contours.Segments)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            IncludeLcd(segment.Start); IncludeLcd(segment.Control); IncludeLcd(segment.End);
                        }
                        if (contours.Segments.Length != 0)
                        {
                            int lcdStartX = ClampFloor(lcdMinimumX - 1d / 3, outputWidth), lcdEndX = ClampCeiling(lcdMaximumX + 1d / 3 + (contours.ExtendWindowsLcdSpans ? 1d / 6 : 0), outputWidth);
                            int lcdStartY = ClampFloor(lcdMinimumY, outputHeight), lcdEndY = ClampCeiling(lcdMaximumY, outputHeight);
                            SvgGlyphLcdCoverage.DropoutBounds dropoutBounds = SvgGlyphLcdCoverage.PrepareBounds(contours.Segments, contours.SmartDropouts, contours.SimpleDropouts, contours.ExcludeDropoutStubs, contours.WindowsLcdMode, cancellationToken) with { ExtendRight = contours.ExtendWindowsLcdSpans };
                            // Directed horizontal scans are reused across one row. This
                            // optional preparation yields to the existing low-memory path.
                            using SvgGlyphLcdCoverage.ScanStorage? rowStorage = lcdStartX < lcdEndX && lcdStartY < lcdEndY ? SvgGlyphLcdCoverage.ScanStorage.TryCreate(contours.Segments, contours.WindowsLcdMode, budget, cancellationToken) : null;
                            Span<int> rowCounts = stackalloc int[10];
                            for (int y = lcdStartY; y < lcdEndY; y++)
                            {
                                SvgGlyphLcdCoverage.ScanRow scans = rowStorage is null ? default : rowStorage.PrepareRow(contours.Segments, y, rowCounts, cancellationToken);
                                for (int x = lcdStartX; x < lcdEndX; x++)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    SvgGlyphRgbCoverage lcdCoverage = scans.IsPrepared
                                        ? SvgGlyphLcdCoverage.GetCoverage(scans, x, crossings, dropoutBounds, cancellationToken)
                                        : SvgGlyphLcdCoverage.GetCoverage(contours.Segments, x, y, crossings, dropoutBounds, cancellationToken);
                                    byte alpha = ViewportAlpha(preblend[lcdCoverage.Mean], x, y);
                                    if (glyph.Shader is not null)
                                        cancellationToken.ThrowIfCancellationRequested();
                                    if (alpha != 0) GlyphPaintAt(x, y).Blend(destination.AsSpan((y * outputWidth + x) * 4, 4), alpha, SvgAnalyticPaintKind.Mask);
                                }
                            }
                        }
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
                        SvgPaintBounds layout = source.LayoutBounds(SvgAffineTransform.Identity, cancellationToken)
                            .Map(SvgAffineTransform.FromScaleOffset(device.A, device.D, device.E, device.F), cancellationToken);
                        double fittingSize = source.DeviceFontSize(device.D);
                        if (fittingSize is > 0 and <= 32767
                            && (layout.Right < -1 || layout.Bottom < -1 || layout.Left > outputWidth + 1 || layout.Top > outputHeight + 1))
                        { return; }
                        using SvgAuthoredPath path = source.CreateLatinLightPath(device.A, device.E, device.D, device.F, budget, cancellationToken);
                        if (path.Commands.IsEmpty) { return; }
                        Span<byte> preblend = stackalloc byte[256];
                        SvgFontMaskPreblend.BuildLinuxGrayTable(preblend, GlyphLuminance(), cancellationToken);
                        RasterizeMappedBitmap(path, preblend);
                        return;
                    }
                }
            }
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
            int startX = ClampFloor(MapEdge(minimumX, device.A, device.E), outputWidth);
            int endX = ClampCeiling(MapEdge(maximumX, device.A, device.E), outputWidth);
            int startY = ClampFloor(MapEdge(minimumY, device.D, device.F), outputHeight);
            int endY = ClampCeiling(MapEdge(maximumY, device.D, device.F), outputHeight);
            if (startX == endX || startY == endY) return;
            double sourceLeft = (startX - device.E) / device.A;
            double sourceRight = (endX - device.E) / device.A;
            double sourceTop = (startY - device.F) / device.D;
            double sourceBottom = (endY - device.F) / device.D;
            double sourcePixelWidth = 1 / device.A;
            double sourcePixelHeight = 1 / device.D;
            if (!double.IsFinite(sourceLeft) || !double.IsFinite(sourceRight) || !double.IsFinite(sourceTop) || !double.IsFinite(sourceBottom) || !double.IsFinite(sourcePixelWidth) || !double.IsFinite(sourcePixelHeight) || sourcePixelWidth <= 0 || sourcePixelHeight <= 0)
            {
                throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph coverage coordinates exceed the admitted numeric range.");
            }
            using SvgGlyphCoverage.PreparedContours? prepared = SvgGlyphCoverage.PreparedContours.TryCreate(glyph.Contours, budget, cancellationToken);
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
            // cancellation token. Only final mask painting belongs
            // to the parent SVG viewport/opacity scope. No font ownership transfers.
            void RasterizeMappedBitmap(SvgAuthoredPath sourcePath, ReadOnlySpan<byte> preblend)
            {
                bool horizontalStrokeFilter = glyph.StrokePaint is not null && OperatingSystem.IsWindows();
                int sampleScale = horizontalStrokeFilter ? 4 : 1;
                // Native mask bounds include filter guards, but the four-times
                // sample image excludes them. Keep its map, limits and filter
                // indices in that same interior domain.
                int sampleInset = horizontalStrokeFilter ? 1 : 0;
                SvgAffineTransform map = sourcePath.SceneMap;
                double originX = Math.Floor(map.E), originY = Math.Floor(map.F);
                float phaseX = (float)(map.E - originX), phaseY = (float)(map.F - originY);
                double left = double.PositiveInfinity, top = double.PositiveInfinity;
                double right = double.NegativeInfinity, bottom = double.NegativeInfinity;
                using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Geometry, cancellationToken);
                foreach (SvgAuthoredPathCommand command in sourcePath.Commands)
                {
                    cancellationToken.ThrowIfCancellationRequested();
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
                if (commands.Count == 0) return;
                left = Math.Floor(left); top = Math.Floor(top);
                right = Math.Ceiling(right); bottom = Math.Ceiling(bottom);
                if (horizontalStrokeFilter) { left--; right++; }
                double bitmapWidth = right - left, bitmapHeight = bottom - top;
                if (!double.IsFinite(bitmapWidth) || !double.IsFinite(bitmapHeight))
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph bitmap bounds exceed the device range.");
                double samplingWidth = (bitmapWidth - 2 * sampleInset) * sampleScale;
                if (samplingWidth <= 0 || bitmapHeight <= 0 || originX + right <= 0 || originY + bottom <= 0
                    || originX + left >= outputWidth || originY + top >= outputHeight) return;
                SvgRenderLimits limits = options.Limits;
                if (samplingWidth > limits.MaximumDimension || bitmapHeight > limits.MaximumDimension
                    || samplingWidth * bitmapHeight > limits.MaximumPixels || samplingWidth * bitmapHeight > int.MaxValue / 4)
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph bitmap exceeds the configured surface limits.");
                int width = (int)samplingWidth, height = (int)bitmapHeight;
                OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
                SvgAuthoredPath localPath;
                try { localPath = SvgAuthoredPath.Create(owned, SvgAffineTransform.FromScaleOffset(sampleScale, 1, -(left + sampleInset) * sampleScale, -top), budget); }
                catch { owned.Dispose(); throw; }
                using (localPath)
                using (SvgPolygonFill fill = SvgPolygonFill.Create([], [], null, new SvgColor(0, 0, 0, 255), true, false, SvgPolygonMapping.NativeFillEdges, null, null, budget))
                using (RenderBudget.BudgetReservation scene = budget.Reserve(256 + ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(1) + ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(1), SvgMemoryCategory.Geometry))
                {
                    SvgGeometry[] geometries = [SvgGeometry.FromPolygonFill(fill) with { AuthoredPath = localPath }];
                    SvgDrawCommand[] draws = [new(SvgDrawCommandKind.Geometry, 0, 255, null, -1, 255)];
                    SvgRoot bitmapRoot = new(null, null, null, geometries, draws, false, null);
                    SvgRenderLimits bitmapLimits = limits;
                    SvgRenderOptions bitmapOptions = new(new SvgViewport(width, height), 1, SvgColor.Transparent, bitmapLimits);
                    using RasterizedSurface bitmap = Rasterize(bitmapRoot, bitmapOptions, budget, RasterSurfaceMode.PremultipliedGlyph, cancellationToken);
                    double deviceLeft = originX + left, deviceTop = originY + top;
                    int startX = Math.Max(layerBounds.Left, ClampFloor(deviceLeft, outputWidth));
                    int startY = Math.Max(layerBounds.Top, ClampFloor(deviceTop, outputHeight));
                    int endX = Math.Min(layerBounds.Right, ClampCeiling(deviceLeft + bitmapWidth, outputWidth));
                    int endY = Math.Min(layerBounds.Bottom, ClampCeiling(deviceTop + height, outputHeight));
                    for (int y = startY; y < endY; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        for (int x = startX; x < endX; x++)
                        {
                            if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                            int row = (int)(y - deviceTop) * width * 4;
                            int sampleX = ((int)(x - deviceLeft) - sampleInset) * sampleScale;
                            byte coverage = horizontalStrokeFilter ? FilterStroke(bitmap.Pixels.AsSpan(row, width * 4), sampleX) : bitmap.Pixels[row + sampleX * 4 + 3];
                            byte alpha = ViewportAlpha(preblend[coverage], x, y);
                            GlyphPaintAt(x, y).Blend(destination.AsSpan((y * outputWidth + x) * 4, 4), alpha, SvgAnalyticPaintKind.Mask);

                        }
                    }
                }

                SvgDevicePoint MapLocal(SvgDevicePoint point)
                {
                    cancellationToken.ThrowIfCancellationRequested();
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
                if (constantGlyphFactory) return SvgAnalyticPaint.Constant(glyphFactoryColor.AsConstant(ShaderAlpha(glyph.Color.Alpha)));
                if (glyphLinear is {} ink)
                {
                    double position;
                    if (ink.X1 == ink.X2 && ink.Y1 == ink.Y2) position = 1;
                    else if (!glyphCoordinates.TryLinear(x, y, ShaderBitmapOrigin(x, y), out position))
                        position = ink.SourcePaint?.Position((x + .5 - device.E) / device.A, (y + .5 - device.F) / device.D) ?? 1;
                    return SvgAnalyticPaint.Shader(glyphRamp.SampleAbsolute(SvgGradientRamp.Spread(position, ink.Spread), ShaderAlpha(glyph.Color.Alpha), GradientDither(x, y)));
                }
                if (glyphRadial is {} radial)
                {
                    double position;
                    if (radial.Stops.Length == 1 && !glyphCoordinates.MasksRadialSamples) position = 0;
                    else if (radial.Radius == 0) position = 1;
                    else if (!glyphCoordinates.TryRadial(x, y, ShaderBitmapOrigin(x, y), out SvgRadialShaderSample sample))
                    {
                        double sampleX = (x + .5 - device.E) / device.A;
                        double sampleY = (y + .5 - device.F) / device.D;
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
                    else
                    {
                        if (!sample.IsVisible) return SvgAnalyticPaint.Shader(default);
                        position = sample.Position;
                    }
                    return SvgAnalyticPaint.Shader(glyphRamp.SampleAbsolute(SvgGradientRamp.Spread(position, radial.Spread), ShaderAlpha(glyph.Color.Alpha), GradientDither(x, y)));
                }
                return glyphPattern is null ? ResolveSolidPaint(glyph.Color)
                    : SvgAnalyticPaint.Shader(SamplePatternWithOpacity(glyphPattern, glyphTile.Surface.Pixels, x, y, ref glyphCoordinates, glyph.Color.Alpha));

            }

            SvgColor GlyphLuminance()
            {
                if (constantGlyphFactory) return glyphFactoryLuminance;
                if (glyphLinear is {} linear)
                {
                    if (linear.X1 == linear.X2 && linear.Y1 == linear.Y2) return linear.End;
                    if (linear.Stops.Length == 1) return linear.Stops[0].Color;
                    return GradientLuminance(linear.Stops, linear.Spread);
                }
                if (glyphRadial is {} radial)
                {
                    if (radial.Stops.Length == 1) return radial.Stops[0].Color;
                    if (radial.Radius == 0) return radial.End;
                    return GradientLuminance(radial.Stops, radial.Spread);
                }
                // SkPaintPriv uses neutral mid-gray when an image/picture shader
                // supplies no representative luminance color. See existing notices.
                return glyphPattern is null ? ResolveSolidColor(glyph.Color) : new(128, 128, 128, 255);
            }

            // SkGradientBaseShader uses an unweighted source-color mean to select
            // font-mask preblend, independently of stop alpha and pixel position.
            SvgColor GradientLuminance(ReadOnlySpan<SvgGradientStop> stops, SvgGradientSpread spread)
            {
                if (stops.Length == 0) return default;
                if (stops.Length == 1) return stops[0].Color;
                SvgGradientStops canonical = SvgGradientStops.Create(stops, spread, cancellationToken);
                SvgGradientStops.Enumerator iterator = canonical.GetEnumerator(cancellationToken);
                float red = 0, green = 0, blue = 0;
                while (iterator.MoveNext())
                {
                    SvgColor color = iterator.Current.Color;
                    red += color.Red / 255f; green += color.Green / 255f; blue += color.Blue / 255f;
                }
                float scale = 1f / canonical.Count;
                return new(Quantize(red * scale), Quantize(green * scale), Quantize(blue * scale), 255);

                static byte Quantize(float value) => (byte)Math.Clamp((int)(value * 255f + .5f), 0, 255);
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

        SvgRectangleCoverage? PrepareRectangleFill(SvgAuthoredPath? source, SvgAffineTransform map)
        {
            if (source is not { IsRectangleFill: true }) return null;
            // Retained canvas stages already prepared this map; rebuilding it loses their rounding.
            if (sourcePlacement == SvgAffineTransform.Identity && activeViewport.DrawMap is null)
                map = MapSourceTransform(drawingMap.Recording, source.SceneMap, PathCoordinateSpace.User);
            if (!map.PreservesAxisAlignedRectangles) return null;
            SvgRectanglePathRecognizer recognizer = new();
            foreach (SvgAuthoredPathCommand command in source.Commands)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!recognizer.Add(command.Verb, command.First)) return null;
            }
            if (recognizer.Finish() is not SvgRecognizedRectangle { Closed: true } rectangle) return null;
            bool exchangeAxes = !map.IsAxisAligned;
            double x1 = MapRectangleEdge(exchangeAxes ? rectangle.Top : rectangle.Left, exchangeAxes ? map.C : map.A, map.E);
            double x2 = MapRectangleEdge(exchangeAxes ? rectangle.Bottom : rectangle.Right, exchangeAxes ? map.C : map.A, map.E);
            double y1 = MapRectangleEdge(exchangeAxes ? rectangle.Left : rectangle.Top, exchangeAxes ? map.B : map.D, map.F);
            double y2 = MapRectangleEdge(exchangeAxes ? rectangle.Right : rectangle.Bottom, exchangeAxes ? map.B : map.D, map.F);
            return SvgRectangleCoverage.Create(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2), outputWidth, outputHeight);
        }

        SvgRectangleFrameCoverage? PrepareRectangleFrame(SvgAuthoredPath? source)
        {
            if (source?.StrokeStyle is not { Shape: SvgAuthoredStrokeShape.Rectangle } style) return null;
            SvgAffineTransform map = MapSourceTransform(drawingMap.Recording, source.SceneMap, PathCoordinateSpace.User);
            SvgAffineTransform widthMap = MapSourceTransform(drawingMap.Recording, style.WidthMap, PathCoordinateSpace.User);
            if (!map.PreservesAxisAlignedRectangles || !widthMap.PreservesAxisAlignedRectangles) return null;
            SvgRectanglePathRecognizer recognizer = new();
            foreach (SvgAuthoredPathCommand command in source.Commands)
            {
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
            SvgAffineTransform device = drawingMap.Device;
            cancellationToken.ThrowIfCancellationRequested();
            SvgRectangleFrameCoverage frame = SvgRectangleFrameCoverage.Create(
                MapRectangleEdge(rectangle.X, device.A, device.E), MapRectangleEdge(rectangle.Y, device.D, device.F),
                MapRectangleEdge(rectangle.X + rectangle.Width, device.A, device.E), MapRectangleEdge(rectangle.Y + rectangle.Height, device.D, device.F),
                MapEdge(stroke.Width, device.A, 0), MapEdge(stroke.Width, device.D, 0));
            SvgClipBox? clip = null;
            if (rectangle.Clip is {} sourceClip)
            {
                (double l, double t, double r, double b) = MapClipBounds(sourceClip, device.A, device.E, device.D, device.F);
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
                    local = SvgRectangleFrameCoverage.Create(
                        MapRectangleEdge(rectangle.X, device.A, (float)device.E - origin.X),
                        MapRectangleEdge(rectangle.Y, device.D, (float)device.F - origin.Y),
                        MapRectangleEdge(rectangle.X + rectangle.Width, device.A, (float)device.E - origin.X),
                        MapRectangleEdge(rectangle.Y + rectangle.Height, device.D, (float)device.F - origin.Y),
                        MapEdge(stroke.Width, device.A, 0), MapEdge(stroke.Width, device.D, 0));
                }
                int x1 = Math.Max(left, context.Interior.Left), x2 = Math.Min(right, context.Interior.Right);
                int y1 = Math.Max(top, context.Interior.Top), y2 = Math.Min(bottom, context.Interior.Bottom);
                if (x2 <= x1 || y2 <= y1) continue;
                long pixels = 0;
                for (int y = y1; y < y2; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    (int end, int start) = local.GetRowSpans(y - origin.Y, x1 - origin.X, x2 - origin.X);
                    pixels += end - (x1 - origin.X) + (x2 - origin.X) - Math.Max(end, start);
                }
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
            int startX = Math.Max(raster.Left, layerBounds.Left), endX = Math.Min(raster.Right, layerBounds.Right);
            int startY = Math.Max(raster.Top, layerBounds.Top), endY = Math.Min(raster.Bottom, layerBounds.Bottom);
            if (startX >= endX || startY >= endY) return;
            if (activeViewport.HasMask && activeViewport.IsBinary)
            {
                SvgBinaryClipRectangles regions = new(activeViewport.Mask, outputWidth, outputHeight, new(0, 0, outputWidth, outputHeight),
                    new(startX, startY, endX, endY), cancellationToken);
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
            SvgAffineTransform device = drawingMap.Device;
            cancellationToken.ThrowIfCancellationRequested();
            double x1 = MapRectangleEdge(rectangle.X, device.A, device.E);
            double y1 = MapRectangleEdge(rectangle.Y, device.D, device.F);
            double x2 = MapRectangleEdge(rectangle.X + rectangle.Width, device.A, device.E);
            double y2 = MapRectangleEdge(rectangle.Y + rectangle.Height, device.D, device.F);
            double left = Math.Min(x1, x2), top = Math.Min(y1, y2);
            double right = Math.Max(x1, x2), bottom = Math.Max(y1, y2);
            SvgRectangleCoverage coverage = SvgRectangleCoverage.Create(left, top, right, bottom, outputWidth, outputHeight);
            SvgClipBox? clip = null;
            if (rectangle.Clip is {} sourceClip)
            {
                double cx1 = MapEdge(sourceClip.X, device.A, device.E), cy1 = MapEdge(sourceClip.Y, device.D, device.F);
                double cx2 = MapEdge(sourceClip.Right, device.A, device.E), cy2 = MapEdge(sourceClip.Bottom, device.D, device.F);
                double clipLeft = Math.Min(cx1, cx2), clipTop = Math.Min(cy1, cy2);
                double clipRight = Math.Max(cx1, cx2), clipBottom = Math.Max(cy1, cy2);
                clip = new SvgClipBox(clipLeft, clipTop, Math.Max(0, clipRight - clipLeft), Math.Max(0, clipBottom - clipTop)) { Antialiasing = sourceClip.Antialiasing };
                left = Math.Max(left, clipLeft); top = Math.Max(top, clipTop);
                right = Math.Min(right, clipRight); bottom = Math.Min(bottom, clipBottom);
            }
            int startX = ClampFloor(left, outputWidth), startY = ClampFloor(top, outputHeight);
            int endX = Math.Max(startX, ClampCeiling(right, outputWidth)), endY = Math.Max(startY, ClampCeiling(bottom, outputHeight));
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
    internal readonly record struct RasterizedSurface(int Width, int Height, byte[] Pixels, RenderBudget.BudgetReservation Reservation) : IDisposable
    {
        internal SvgFilterDeferredImage? DeferredImage { get; init; }
        /// <summary>Identifies borrowed affine filter pixels awaiting their final transfer; ordinary raster surfaces are sRGB.</summary>
        internal SvgFilterColorSpace FilterColorSpace { get; init; } = SvgFilterColorSpace.Srgb;
        /// <summary>Retains an image-free source color-filter stage until affine sampling; owns no extra storage.</summary>
        internal SvgFilterDeferredColorFilter SamplingColorFilter { get; init; }
        /// <summary>Tracks sampling retained by an unevaluated filter transform; materialized images have no pending color filter.</summary>
        internal SvgFilterImageSampling FilterImageSampling { get; init; }
        public void Dispose() => Reservation.Dispose();
    }

    /// <summary>Owns the parsed scene, surface and invocation scope until their synchronous pixel borrower or output path finishes.</summary>
    /// <remarks>Surface pixels are borrowed read-only until disposal; callers must not retain or mutate them, or race disposal with access. The supplied host budget remains borrowed.</remarks>
    internal sealed class RasterizedConversion : IDisposable
    {
        private readonly SvgRoot _root;
        private readonly IMemoryScope? _scope;

        internal RasterizedConversion(RenderBudget budget, SvgRoot root, RasterizedSurface surface, IMemoryScope? scope)
        {
            Budget = budget;
            _root = root;
            Surface = surface;
            _scope = scope;
        }

        internal RenderBudget Budget { get; }

        internal RasterizedSurface Surface { get; }

        public void Dispose()
        {
            Surface.Dispose();
            _root.Dispose();
            _scope?.Dispose();
        }
    }
}
