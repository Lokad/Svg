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
    /// <summary>Owns mapped polygon points with raster bounds, clip and their surface reservation until the rasterizer releases them.</summary>
    private readonly record struct MappedPolygon(SvgDevicePoint[] Points, int StartX, int StartY, int EndX, int EndY, int PathStartX, int PathStartY, int PathEndX, int PathEndY, (double Left, double Top, double Right, double Bottom)? Clip, SvgClipAntialiasing ClipAntialiasing, bool ClipContainsPath, SvgDeviceBounds CompleteBounds, bool HasFractionalEdge, SvgQuadSpan[] QuadSpans, SvgCubicSpan[] CubicSpans, RenderBudget.BudgetReservation? Reservation, int[] ContourEnds, int[]? Signs, SvgPreparedPathContours? Authored, SvgAffineTransform AuthoredMap, SvgPolygonMapping Mapping) : IDisposable
    {
        public void Dispose() { Reservation?.Dispose(); Authored?.Dispose(); }

        /// <summary>Gets bordered construction when complete mapped path/control bounds escape this bitmap or the SVG clip.</summary>
        internal SvgAnalyticClipContext? GetConstructionClip(SvgCpuRasterContext context, SvgAnalyticClipBounds? contentClip)
            => ClipContainsPath && CompleteBounds.IsContainedBy(GetBitmapClip(context, contentClip)) ? null : new(GetBitmapClip(context, contentClip), new(context.BitmapClip.Left, context.BitmapClip.Top));

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
                ? new((int)Math.Clamp(Math.Floor(bounds.Left), bitmap.Left, bitmap.Right), (int)Math.Clamp(Math.Floor(bounds.Top), bitmap.Top, bitmap.Bottom), (int)Math.Clamp(Math.Ceiling(bounds.Right), bitmap.Left, bitmap.Right), (int)Math.Clamp(Math.Ceiling(bounds.Bottom), bitmap.Top, bitmap.Bottom))
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
    {
        (double width, double height) = ResolveOutputDimensions(options.Viewport, root.Width, root.Height, root.ViewBox);
        double scaledWidth = Math.Round(width * options.OutputScale, MidpointRounding.AwayFromZero);
        double scaledHeight = Math.Round(height * options.OutputScale, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(scaledWidth) || !double.IsFinite(scaledHeight) || scaledWidth <= 0 || scaledHeight <= 0 || scaledWidth != Math.Truncate(scaledWidth) || scaledHeight != Math.Truncate(scaledHeight)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive finite integral output dimensions are implemented currently.");
        if (scaledWidth > options.Limits.MaximumDimension || scaledHeight > options.Limits.MaximumDimension) throw new SvgException(SvgErrorCode.LimitExceeded, "Output dimensions exceed the configured dimension limit.");
        int outputWidth = (int)scaledWidth;
        int outputHeight = (int)scaledHeight;
        SvgCpuRasterGrid rasterGrid = new(outputWidth, outputHeight);
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
                if (horizontalScale != verticalScale && isCurveFill && (surfaceMode != RasterSurfaceMode.PremultipliedPattern || geometry.AuthoredPath is null)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Curve fills under non-uniform viewBox or output scaling are not implemented.");
            }
        }
        SvgCpuClipCoverage? layerClipCoverage = null;
        float foldedOpacity = 1;
        SvgAnalyticClipBounds layerBounds = new(0, 0, outputWidth, outputHeight);
        bool maskClipAntialiased = false;
        bool collectingViewportClip = false;
        byte[] activeViewportClip = [];
        SvgCpuClipCoverage? collectingClipCoverage = null, activeViewportCoverage = null, activeConstructionCoverage = null;
        byte[] activeConstructionClip = [];
        SvgAnalyticClipBounds? activeConstructionBounds = null;
        SvgCpuClipCoverage? clipWindowCoverage = null;
        SvgAnalyticClipBounds? clipWindowBounds = null;
        SvgAnalyticClipBounds? activeViewportBounds = null;
        SvgRecordedPaintValue activePaint = default;
        SvgRecordedPaintValue activeStrokePaint = default;
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
            return new(bitmap, interior);
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
                            Include(new(segment.Start.X, segment.Start.Y), outputMap);
                            Include(new(segment.Control.X, segment.Control.Y), outputMap);
                            Include(new(segment.End.X, segment.End.Y), outputMap);
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
                if (command.Kind == SvgDrawCommandKind.EndGroup)
                {
                    if (!stopsAtGroupEnd) throw new SvgException(SvgErrorCode.MalformedInput, "SVG group structure is unbalanced.");
                    return index;
                }
                if (command.Kind == SvgDrawCommandKind.BeginGroup)
                {
                    if (command.MaskRegion is null && command.MaskContentIndex < 0 && command.MaskAlpha == 255
                        && TrySinglePaint(index, command.RecordingScope, out int paintEnd))
                    {
                        float previousOpacity = foldedOpacity;
                        foldedOpacity *= command.SourceOpacity.Value;
                        try
                        {
                            for (; index < paintEnd; index++) RasterizeGeometry(root.Geometries[root.Commands[index].GeometryIndex], destination);
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
                        SvgMaskContent clipContent = root.SceneStorage.MaskContents[command.MaskContentIndex];
                        using SvgCpuClipCoverage? clipCoverage = RasterizeMaskContent(clipContent, clipPixels, true, clipContent.ClipKind != SvgClipContentKind.AffineViewport ? previousLayer : layer);
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
                        activeViewportBounds = layer;
                        try { index = RenderCommands(index, destination, true); }
                        finally { activeViewportClip = previousClip; activeViewportBounds = previousBounds; activeViewportCoverage = previousCoverage; }
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
                        if (shaderRestore || parentViewportClip.Length != 0) BlendClippedLayerPixel(destination.AsSpan(offset, 4), groupPixels.AsSpan(offset, 4), extra, maskAlpha, clipAlpha);
                        else BlendLayerPixel(destination.AsSpan(offset, 4), groupPixels.AsSpan(offset, 4), extra, maskAlpha);
                    }
                    continue;
                }
                if ((uint)command.GeometryIndex >= (uint)root.Geometries.Length) throw new SvgException(SvgErrorCode.MalformedInput, "SVG draw command references missing geometry.");
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
        }

        // Borrow caller-reserved destination storage. Clip collection forces ordered AA runs;
        // mask/clip content is independent of the surrounding paint opacity and clip stack.
        // Geometry traversal retains the existing work, cancellation and temporary-owner contracts.
        SvgCpuClipCoverage? RasterizeMaskContent(SvgMaskContent content, byte[] destination, bool viewport, SvgAnalyticClipBounds contentBounds)
        {
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
            }
        }

        void RasterizeGeometry(SvgGeometry geometry, byte[] destination)
        {
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
            if (geometry.Kind == SvgGeometryKind.PolygonFill && geometry.AuthoredPath is SvgAuthoredPath centerline && centerline.StrokeStyle is not null)
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
                    RasterizePolygonFill(geometry.Polygon, geometry.AuthoredPath, destination);
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

        MappedPolygon MapPolygon(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, bool isEvenOdd, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, SvgPolygonMapping mapping, SvgClipBox? clip, SvgAuthoredPath? authoredPath)
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
                authored = replay.PrepareSource(SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset), isEvenOdd, budget, preparationWork, cancellationToken);
                workUnits += preparationWork.Used;
                authoredMap = SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset).ComposeChecked(replay.SceneMap);
                points = authored.Points; contourEnds = authored.ContourEnds; signs = authored.Signs;
                mapping = authoredPath?.StrokeStyle is null ? SvgPolygonMapping.NativeFillEdges : SvgPolygonMapping.NativeStrokeEdges;
                quadSpans = authored.QuadSpans; cubicSpans = authored.CubicSpans;
            }
            bool transferred = false;
            try
            {
                double pointScaleX = authored is null ? horizontalScale : 1;
                double pointScaleY = authored is null ? verticalScale : 1;
                double pointOffsetX = authored is null ? horizontalOffset : 0;
                double pointOffsetY = authored is null ? verticalOffset : 0;
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
            if (activeViewportClip.Length == 0) return paintClip;
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

        void RasterizePolygonFill(SvgPolygonFill polygon, SvgAuthoredPath? authoredPath, byte[] destination)
        {
            if (!polygon.HasFill || polygon.Points.Length < 3) return;
            using MappedPolygon raster = MapPolygon(polygon.Points, polygon.ContourEnds, polygon.Signs, polygon.IsEvenOdd, polygon.QuadSpans, polygon.CubicSpans, polygon.Mapping, polygon.Clip, authoredPath);
            if (raster.EndY <= raster.StartY || raster.EndX <= raster.StartX) return;
            SvgAnalyticPaint analyticPaint = ResolveSolidPaint(polygon.Color);
            ReadOnlySpan<int> contourEnds = raster.ContourEnds;
            ReadOnlySpan<int> nestingSigns = raster.Signs is int[] signs ? signs : Array.Empty<int>();
            SvgAnalyticContourPreparation preparation = new(raster.Points, contourEnds, nestingSigns, polygon.IsEvenOdd, raster.Signs, raster.Mapping, raster.QuadSpans, raster.CubicSpans, raster.CompleteBounds.RoundOutForDispatch(), outputWidth, outputHeight, raster.Authored, raster.AuthoredMap);
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
                using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, storageStartX, storageEndX, raster.PathStartY, raster.PathEndY, raster.GetScanWindow(context, bitmapClipBounds), new SvgCpuBitmapOrigin(context.BitmapClip.Left, context.BitmapClip.Top), raster.GetConstructionClip(context, bitmapClipBounds), PolygonPaintClip(raster, context), options.Limits.MaximumWorkUnits - workUnits, budget, out long analyticSetup, out bool useAnalytic, cancellationToken);
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
                        if (useAnalytic && collectingClipCoverage is not null)
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
        void RasterizeGradientPolygon(SvgGradientPolygon outline, SvgAuthoredPath? authoredPath, byte[] destination)
        {
            if (!outline.HasFill || outline.Points.Length < 3) return;
            // An objectBoundingBox paint on a zero-area box covers nothing: the stroke outline keeps its own extents while the paint box stays degenerate.
            if (!outline.Gradient.IsUserSpace && (outline.Box.Width <= 0 || outline.Box.Height <= 0)) return;
            using MappedPolygon raster = MapPolygon(outline.Points, outline.ContourEnds, outline.Signs, outline.IsEvenOdd, outline.QuadSpans, outline.CubicSpans, outline.Mapping, outline.Clip, authoredPath);
            if (raster.EndY <= raster.StartY || raster.EndX <= raster.StartX) return;
            SvgLinearGradient definition = outline.Gradient;
            SvgRectangle box = outline.Box;
            double directionX = definition.X2 - definition.X1;
            double directionY = definition.Y2 - definition.Y1;
            double denominator = directionX * directionX + directionY * directionY;
            double offsetSpan = definition.Offset2 - definition.Offset1;
            SvgShaderCoordinates nativeCoordinates = SvgShaderCoordinates.CreateLinear(definition, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset);
            if (offsetSpan <= 0 && definition.Stops.Length != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only two-stop increasing-offset linear gradients are implemented.");

            ReadOnlySpan<int> contourEnds = raster.ContourEnds;
            ReadOnlySpan<int> nestingSigns = raster.Signs is int[] signs ? signs : Array.Empty<int>();
            SvgAnalyticContourPreparation preparation = new(raster.Points, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, raster.Mapping, raster.QuadSpans, raster.CubicSpans, raster.CompleteBounds.RoundOutForDispatch(), outputWidth, outputHeight, raster.Authored, raster.AuthoredMap);
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
                using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, storageStartX, storageEndX, raster.PathStartY, raster.PathEndY, raster.GetScanWindow(context, bitmapClipBounds), new SvgCpuBitmapOrigin(context.BitmapClip.Left, context.BitmapClip.Top), raster.GetConstructionClip(context, bitmapClipBounds), PolygonPaintClip(raster, context), options.Limits.MaximumWorkUnits - workUnits, budget, out long analyticSetup, out bool useAnalytic, cancellationToken);
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
                    SvgShaderPaint sample = SvgShaderPaint.Ramp(definition.Stops, definition.Start, definition.End, definition.Offset1, definition.Offset2, position, ShaderAlpha(outline.Opacity), GradientDither(x, y));
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
            using MappedPolygon raster = MapPolygon(outline.Points, outline.ContourEnds, outline.Signs, outline.IsEvenOdd, outline.QuadSpans, outline.CubicSpans, outline.Mapping, outline.Clip, authoredPath);
            if (raster.EndY <= raster.StartY || raster.EndX <= raster.StartX) return;
            SvgRadialGradient definition = outline.Gradient;
            SvgRectangle box = outline.Box;
            SvgShaderCoordinates nativeCoordinates = SvgShaderCoordinates.CreateRadial(definition, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset);
            if (definition.Radius <= 0 && definition.Stops.Length != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive-radius radial gradients are implemented.");

            ReadOnlySpan<int> contourEnds = raster.ContourEnds;
            ReadOnlySpan<int> nestingSigns = raster.Signs is int[] signs ? signs : Array.Empty<int>();
            SvgAnalyticContourPreparation preparation = new(raster.Points, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, raster.Mapping, raster.QuadSpans, raster.CubicSpans, raster.CompleteBounds.RoundOutForDispatch(), outputWidth, outputHeight, raster.Authored, raster.AuthoredMap);
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
                using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, storageStartX, storageEndX, raster.PathStartY, raster.PathEndY, raster.GetScanWindow(context, bitmapClipBounds), new SvgCpuBitmapOrigin(context.BitmapClip.Left, context.BitmapClip.Top), raster.GetConstructionClip(context, bitmapClipBounds), PolygonPaintClip(raster, context), options.Limits.MaximumWorkUnits - workUnits, budget, out long analyticSetup, out bool useAnalytic, cancellationToken);
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
                    SvgShaderPaint sample = SvgShaderPaint.Ramp(definition.Stops, definition.Start, definition.End, definition.Offset1, definition.Offset2, position, ShaderAlpha(outline.Opacity), GradientDither(x, y));
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
            using MappedPolygon raster = MapPolygon(outline.Points, outline.ContourEnds, outline.Signs, outline.IsEvenOdd, outline.QuadSpans, outline.CubicSpans, outline.Mapping, outline.Clip, authoredPath);
            if (raster.EndY <= raster.StartY || raster.EndX <= raster.StartX) return;
            using RenderBudget.BudgetReservation sourcePatternOwner = budget.Reserve(512, SvgMemoryCategory.Geometry);
            SvgPatternGeometry source = new(outline.Content, outline.OriginX, outline.OriginY, outline.TileWidth, outline.TileHeight, default, outline.Opacity, null) { ContentPaint = outline.ContentPaint, VectorContent = outline.VectorContent, ShaderPlacement = outline.ShaderPlacement };
            using RenderBudget.BudgetReservation? mappedPatternOwner = NeedsPatternMapping(source) ? budget.Reserve(512, SvgMemoryCategory.Geometry) : null;
            if (!TryMapPatternTile(source, out SvgPatternGeometry? mappedTile)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills under viewBox or output scaling are not implemented.");
            SvgPatternGeometry tile = mappedTile ?? source;
            using RasterizedSurface texture = PreparePatternTile(tile);
            byte[] blitted = texture.Pixels;
            SvgShaderCoordinates imageCoordinates = tile.NativeImageMap is {} imageMap ? SvgShaderCoordinates.CreateImage(imageMap, tile.NativeImageLocal ?? throw new InvalidOperationException("Image sampling requires its local matrix.")) : default;
            ReadOnlySpan<int> contourEnds = raster.ContourEnds;
            ReadOnlySpan<int> nestingSigns = raster.Signs is int[] signs ? signs : Array.Empty<int>();
            SvgAnalyticContourPreparation preparation = new(raster.Points, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, raster.Mapping, raster.QuadSpans, raster.CubicSpans, raster.CompleteBounds.RoundOutForDispatch(), outputWidth, outputHeight, raster.Authored, raster.AuthoredMap);
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
                using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, storageStartX, storageEndX, raster.PathStartY, raster.PathEndY, raster.GetScanWindow(context, bitmapClipBounds), new SvgCpuBitmapOrigin(context.BitmapClip.Left, context.BitmapClip.Top), raster.GetConstructionClip(context, bitmapClipBounds), PolygonPaintClip(raster, context), options.Limits.MaximumWorkUnits - workUnits, budget, out long analyticSetup, out bool useAnalytic, cancellationToken);
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
                    VectorContent = pattern.VectorContent,
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
                { ShaderPlacement = SvgAffineTransform.FromScaleOffset(1, 1, originX, originY), ContentPaint = pattern.ContentPaint, VectorContent = pattern.VectorContent };
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
                using RasterizedSurface texture = PreparePatternTile(source);
                StampPatternFill(source, texture.Pixels, destination);
            }
            if (pass != SvgPaintPass.Fill && source.Stroke is SvgStroke stroke) RasterizePatternStroke(source, stroke, destination);
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
                RasterizedSurface rendered = Rasterize(tileRoot, tileOptions, budget, RasterSurfaceMode.PremultipliedPattern, cancellationToken);
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
                SvgShaderPaint.Ramp(definition.Stops, definition.Start, definition.End, definition.Offset1, definition.Offset2, position, ShaderAlpha(opacity), GradientDither(x, y)).Blend(destination.AsSpan(offset, 4), ViewportAlpha(raster.GetAlpha(x, y), x, y));
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
                SvgShaderPaint.Ramp(definition.Stops, definition.Start, definition.End, definition.Offset1, definition.Offset2, position, ShaderAlpha(opacity), GradientDither(x, y)).Blend(destination.AsSpan(offset, 4), ViewportAlpha(raster.GetAlpha(x, y), x, y));
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
            for (int y = startY; y < endY; y++)
            for (int x = startX; x < endX; x++)
            {
                byte alpha = ViewportAlpha(SvgGlyphCoverage.GetCoverage(glyph.Contours, sourceLeft + (x - startX) * sourcePixelWidth, sourceTop + (y - startY) * sourcePixelHeight, sourcePixelWidth, sourcePixelHeight, GlyphSamplesPerAxis, cancellationToken), x, y);
                if (alpha == 0) continue;
                BlendSourceOver(destination.AsSpan((y * outputWidth + x) * 4, 4), new SvgColor(glyph.Color.Red, glyph.Color.Green, glyph.Color.Blue, MultiplyAlpha(glyph.Color.Alpha, alpha)));
            }

            void Include(SvgGlyphOutlinePoint point)
            {
                minimumX = Math.Min(minimumX, point.X); minimumY = Math.Min(minimumY, point.Y);
                maximumX = Math.Max(maximumX, point.X); maximumY = Math.Max(maximumY, point.Y);
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
            long rows = 2L * (bottom - top);
            if (rows > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle frame rows exceed the configured work limit.");
            workUnits += rows;
            long pixels = 0;
            for (int y = top; y < bottom; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (int end, int start) = frame.GetRowSpans(y, left, right);
                pixels += end - left + right - Math.Max(end, start);
            }
            long rasterWork = 5 * pixels;
            if (rasterWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle frame raster exceeds the configured work limit.");
            workUnits += rasterWork;
            SvgAnalyticPaint paint = ResolveSolidPaint(stroke.Color);
            Span<byte> alphas = stackalloc byte[4];
            for (int y = top; y < bottom; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (int end, int start) = frame.GetRowSpans(y, left, right);
                int second = Math.Max(end, start);
                int visited = end - left + right - second;
                for (int index = 0; index < visited; index++)
                {
                    int x = index < end - left ? left + index : second + index - (end - left);
                    int count = frame.GetOperations(x, y, alphas);
                    SvgAnalyticClipCoverage coverage = clip is {} paintClip ? SvgAnalyticRectangleClip.GetCoverage((paintClip.X, paintClip.Y, paintClip.Right, paintClip.Bottom), paintClip.Antialiasing, x, y) : SvgAnalyticClipCoverage.Full;
                    Span<byte> pixel = destination.AsSpan((y * outputWidth + x) * 4, 4);
                    for (int operation = 0; operation < count; operation++) paint.Blend(pixel, ViewportAlpha(coverage.Apply(alphas[operation]), x, y), SvgAnalyticPaintKind.Direct);
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
            return MapEdge(value, scale, offset);
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
                MapEdge(clip.X, horizontalScale, horizontalOffset),
                MapEdge(clip.Y, verticalScale, verticalOffset),
                MapEdge(clip.Right, horizontalScale, horizontalOffset),
                MapEdge(clip.Bottom, verticalScale, verticalOffset));
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
        try
        {
            using RenderBudget.BudgetReservation workspace = budget.Reserve(SvgRootParser.GetXmlReaderWorkspaceBytes(svg.Length, options.Limits.MaximumAttributesPerElement), SvgMemoryCategory.Parser);
            ParserWorkCounter probe = new(options.Limits.MaximumWorkUnits);
            XmlReaderSettings settings = SvgXmlReaderPolicy.Create(svg, options.Limits, budget, probe, cancellationToken);
            using ReadOnlyMemoryStream stream = new(svg);
            using XmlReader reader = XmlReader.Create(stream, settings);
            while (reader.Read())
            {
                probe.Consume(cancellationToken);
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (!reader.LocalName.Equals("svg", StringComparison.Ordinal)) return (1.0, 1.0);
                SvgRootAttributes root = SvgRootAttributes.Parse(reader, SvgRootParser.CssNamedColors, probe, cancellationToken);
                (double width, double height) = ResolveOutputDimensions(options.Viewport, root.Width, root.Height, root.ViewBox);
                (double horizontalScale, _, double verticalScale, _) = GetViewBoxTransform(root.ViewBox, width, height, options.OutputScale);
                if (!double.IsFinite(horizontalScale) || !double.IsFinite(verticalScale) || horizontalScale <= 0 || verticalScale <= 0) return (1.0, 1.0);
                return (horizontalScale, verticalScale);
            }

            return (1.0, 1.0);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is SvgException or XmlException or InvalidOperationException or OverflowException)
        {
            return (1.0, 1.0);
        }
    }

    private static SvgRoot ParseRoot(ReadOnlyMemory<byte> svg, SvgRenderOptions options, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        (double outputScaleX, double outputScaleY) = TryResolveOutputScales(svg, options, budget, cancellationToken);
        ParserWorkCounter counter = workCounter ?? new ParserWorkCounter(options.Limits.MaximumWorkUnits);
        try { return SvgRootParser.Parse(svg, options.Limits, budget, options.FontSnapshot, counter, cancellationToken, outputScaleX, outputScaleY, options.Viewport); }
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
    private static void BlendPremultipliedOver(Span<byte> destination, int sourceRed, int sourceGreen, int sourceBlue, int sourceAlpha)
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

    private enum RasterSurfaceMode { Capture, PremultipliedPattern }

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
