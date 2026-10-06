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
    private readonly record struct MappedPolygon(SvgDevicePoint[] Points, int StartX, int StartY, int EndX, int EndY, int PathStartX, int PathStartY, int PathEndX, int PathEndY, (double Left, double Top, double Right, double Bottom)? Clip, SvgClipAntialiasing ClipAntialiasing, bool ClipContainsPath, SvgDeviceBounds CompleteBounds, bool HasFractionalEdge, SvgQuadSpan[] QuadSpans, SvgCubicSpan[] CubicSpans, RenderBudget.BudgetReservation? Reservation, int[] ContourEnds, int[]? Signs, SvgMappedAuthoredPath? Authored, SvgPolygonMapping Mapping) : IDisposable
    {
        public void Dispose() { Reservation?.Dispose(); Authored?.Dispose(); }

        /// <summary>Gets bordered construction when complete mapped path/control bounds escape this bitmap or the SVG clip.</summary>
        internal SvgAnalyticClipContext? GetConstructionClip(SvgCpuRasterContext context)
            => ClipContainsPath && CompleteBounds.IsContainedBy(context.BitmapClip) ? null : new(GetBitmapClip(context), new(context.BitmapClip.Left, context.BitmapClip.Top));

        /// <summary>Keeps bitmap walking distinct from whole-path storage and exclusively owned painting.</summary>
        internal SvgAnalyticScanWindow GetScanWindow(SvgCpuRasterContext context)
        {
            SvgAnalyticClipBounds clip = GetBitmapClip(context);
            return new(clip.Left, clip.Right);
        }

        /// <summary>Retains AA paint routing even when the complete path needs no construction clipping.</summary>
        internal SvgAnalyticPaintClip GetPaintClip(SvgCpuRasterContext context)
            => Clip is {} bounds ? SvgAnalyticRectangleClip.Classify(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, context.BitmapClip, ClipAntialiasing) : SvgAnalyticPaintClip.Binary;

        private SvgAnalyticClipBounds GetBitmapClip(SvgCpuRasterContext context)
        {
            SvgAnalyticClipBounds bitmap = context.BitmapClip;
            return Clip is {} bounds
                ? new((int)Math.Clamp(Math.Floor(bounds.Left), bitmap.Left, bitmap.Right), (int)Math.Clamp(Math.Floor(bounds.Top), bitmap.Top, bitmap.Bottom), (int)Math.Clamp(Math.Ceiling(bounds.Right), bitmap.Left, bitmap.Right), (int)Math.Clamp(Math.Ceiling(bounds.Bottom), bitmap.Top, bitmap.Bottom))
                : bitmap;
        }
    }

    /// <summary>Complete mapped vertex/control bounds used only for native raw-versus-clipped construction.</summary>
    private readonly record struct SvgDeviceBounds(double Left, double Top, double Right, double Bottom)
    {
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
                if (horizontalScale != verticalScale && isCurveFill) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Curve fills under non-uniform viewBox or output scaling are not implemented.");
            }
        }
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
            if (TryResolveSolidCanvas(out SvgColor solidColor))
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
            ApplyCaptureUnpremultiply(pixels, pixelCount, cancellationToken);
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
                    using RenderBudget.BudgetReservation groupReservation = budget.Reserve(checked((long)pixelCount * 4), SvgMemoryCategory.Surface);
                    byte[] groupPixels = new byte[checked(pixelCount * 4)];
                    index = RenderCommands(index, groupPixels, true);
                    long compositionWork = pixelCount;
                    if (compositionWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Group opacity composition exceeds the configured limit.");
                    workUnits += compositionWork;
                    (double regionLeft, double regionTop, double regionRight, double regionBottom) = command.MaskRegion is SvgClipBox maskRegion ? MapClipBounds(maskRegion, horizontalScale, horizontalOffset, verticalScale, verticalOffset) : (0, 0, outputWidth, outputHeight);
                    byte[] maskSurface = [];
                    byte maskLuminance = 255;
                    bool maskIsAlpha = false;
                    bool hasMaskContent = command.MaskContentIndex >= 0;
                    RenderBudget.BudgetReservation? maskReservation = null;
                    if (hasMaskContent)
                    {
                        if (root.SceneStorage is null || (uint)command.MaskContentIndex >= (uint)root.SceneStorage.MaskContents.Length) throw new SvgException(SvgErrorCode.MalformedInput, "SVG group references missing mask content.");
                        SvgMaskContent maskContent = root.SceneStorage.MaskContents[command.MaskContentIndex];
                        maskLuminance = maskContent.Luminance;
                        maskIsAlpha = maskContent.IsAlpha;
                        maskReservation = budget.Reserve(checked((long)pixelCount * 4), SvgMemoryCategory.Surface);
                        try
                        {
                            maskSurface = new byte[checked(pixelCount * 4)];
                            foreach (SvgGeometry contentGeometry in maskContent.Geometries) RasterizeGeometry(contentGeometry, maskSurface);
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
                        if (hasMaskContent)
                        {
                            byte contentAlpha = maskSurface[offset + 3];
                            byte maskValue = maskIsAlpha ? contentAlpha : (byte)(contentAlpha * maskLuminance / 255);
                            extra = MultiplyAlpha(extra, maskValue);
                        }
                        if (command.MaskRegion is not null)
                        {
                            int x = pixel % outputWidth;
                            int y = pixel / outputWidth;
                            extra = (byte)(extra * ClipBoxCoverage((regionLeft, regionTop, regionRight, regionBottom), x, y));
                        }
                        if (extra == 0 || groupPixels[offset + 3] == 0) continue;
                        // Round-half-up modulation mirrors the inline stamped-mask alpha chain
                        // (SvgMaskSyntax stamp sites use MultiplyAlpha), keeping single-geometry group
                        // and direct spellings identical through one shared backend.
                        BlendPremultipliedOver(destination.AsSpan(offset, 4), (groupPixels[offset] * extra + 127) / 255, (groupPixels[offset + 1] * extra + 127) / 255, (groupPixels[offset + 2] * extra + 127) / 255, MultiplyAlpha(groupPixels[offset + 3], extra));
                    }
                    continue;
                }
                if ((uint)command.GeometryIndex >= (uint)root.Geometries.Length) throw new SvgException(SvgErrorCode.MalformedInput, "SVG draw command references missing geometry.");
                RasterizeGeometry(root.Geometries[command.GeometryIndex], destination);
            }
            if (stopsAtGroupEnd) throw new SvgException(SvgErrorCode.MalformedInput, "SVG group structure is unbalanced.");
            return index;
        }

        void RasterizeGeometry(SvgGeometry geometry, byte[] destination)
        {
            if (geometry.Kind == SvgGeometryKind.PolygonFill && geometry.AuthoredPath is SvgAuthoredPath centerline && centerline.StrokeStyle is not null)
            {
                ParserWorkCounter hairlineWork = new(options.Limits.MaximumWorkUnits - workUnits);
                SvgPolygonFill stroke = geometry.Polygon;
                bool rendered = stroke.HasFill && SvgHairlineStroke.TryRasterize(centerline,
                    SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset), stroke.Color,
                    stroke.Clip is SvgClipBox clip ? MapClipBounds(clip, horizontalScale, horizontalOffset, verticalScale, verticalOffset) : null,
                    stroke.Clip?.Antialiasing ?? SvgClipAntialiasing.None, destination, outputWidth, outputHeight, hairlineWork, cancellationToken);
                workUnits += hairlineWork.Used;
                if (rendered) return;
            }
            switch (geometry.Kind)
            {
                case SvgGeometryKind.Rectangle:
                    RasterizeRectangle(geometry.Rectangle, destination);
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
                    RasterizePatternFill(geometry.Pattern, destination);
                    return;
                case SvgGeometryKind.GradientFill:
                    RasterizeGradientFill(geometry.Gradient, destination);
                    return;
                case SvgGeometryKind.GradientStroke:
                    RasterizeGradientStrokeFill(geometry.GradientStroke, destination);
                    return;
                case SvgGeometryKind.RadialGradient:
                    RasterizeRadialFill(geometry.Radial, destination);
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
            SvgMappedAuthoredPath? authored = null;
            if (authoredPath is not null && authoredPath.StrokeStyle is null)
            {
                ParserWorkCounter preparationWork = new(options.Limits.MaximumWorkUnits - workUnits);
                authored = authoredPath.Prepare(SvgAffineTransform.FromScaleOffset(horizontalScale, verticalScale, horizontalOffset, verticalOffset), isEvenOdd, budget, preparationWork, cancellationToken);
                workUnits += preparationWork.Used;
                points = authored.Points; contourEnds = authored.ContourEnds; signs = authored.Signs;
                mapping = SvgPolygonMapping.NativeFillEdges;
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
                    SvgQuadSpan[] mappedSpans = MapQuadSpanControls(quadSpans, pointScaleX, pointOffsetX, pointScaleY, pointOffsetY, outputWidth, outputHeight, mapping is SvgPolygonMapping.NativeStrokeEdges or SvgPolygonMapping.NativeFillEdges or SvgPolygonMapping.FlattenedCurves, out bool canvasContainsQuads, cancellationToken);
                    SvgCubicSpan[] mappedCubics = MapCubicSpanControls(cubicSpans, pointScaleX, pointOffsetX, pointScaleY, pointOffsetY, outputWidth, outputHeight, out bool canvasContainsCubics, cancellationToken);
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
                    return new MappedPolygon(mapped, startX, startY, endX, endY, pathStartX, pathStartY, pathEndX, pathEndY, mappedClip, clip?.Antialiasing ?? SvgClipAntialiasing.None, clipContainsPath, completeBounds, hasFractionalEdge, mappedSpans, mappedCubics, mappedReservation, contourEnds, signs, authored, mapping);
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

        void RasterizePolygonFill(SvgPolygonFill polygon, SvgAuthoredPath? authoredPath, byte[] destination)
        {
            if (!polygon.HasFill || polygon.Points.Length < 3) return;
            using MappedPolygon raster = MapPolygon(polygon.Points, polygon.ContourEnds, polygon.Signs, polygon.IsEvenOdd, polygon.QuadSpans, polygon.CubicSpans, polygon.Mapping, polygon.Clip, authoredPath);
            if (raster.EndY <= raster.StartY || raster.EndX <= raster.StartX) return;
            SvgAnalyticPaint analyticPaint = SvgAnalyticPaint.Solid(polygon.Color);
            ReadOnlySpan<int> contourEnds = raster.ContourEnds;
            ReadOnlySpan<int> nestingSigns = raster.Signs is int[] signs ? signs : Array.Empty<int>();
            SvgAnalyticContourPreparation preparation = new(raster.Points, contourEnds, nestingSigns, polygon.IsEvenOdd, raster.Signs, raster.Mapping, raster.QuadSpans, raster.CubicSpans, new(raster.PathStartX, raster.PathStartY, raster.PathEndX, raster.PathEndY), outputWidth, outputHeight);
            foreach (SvgCpuRasterContext context in rasterGrid.GetContexts(new(raster.StartX, raster.StartY, raster.EndX, raster.EndY)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (workUnits >= options.Limits.MaximumWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "CPU raster context traversal exceeds the configured limit.");
                workUnits++;
                if (raster.Clip is not null)
                {
                    if (4 > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle paint-clip classification exceeds the configured limit.");
                    workUnits += 4;
                }
                int storageStartX = Math.Max(raster.PathStartX, context.BitmapClip.Left);
                int storageEndX = Math.Min(raster.PathEndX, context.BitmapClip.Right);
                using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, storageStartX, storageEndX, raster.PathStartY, raster.PathEndY, raster.GetScanWindow(context), raster.GetConstructionClip(context), raster.GetPaintClip(context), options.Limits.MaximumWorkUnits - workUnits, budget, out long analyticSetup, out bool useAnalytic, cancellationToken);
                if (analyticSetup > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Scanline coverage setup exceeds the configured limit.");
                workUnits += analyticSetup;
                // Charge the taken traversal: analytic rows cost pixels while the fan evaluates every contour per pixel.
                int startX = useAnalytic ? Math.Max(raster.StartX, context.Interior.Left) : raster.StartX;
                int endX = useAnalytic ? Math.Min(raster.EndX, context.Interior.Right) : raster.EndX;
                int startY = useAnalytic ? Math.Max(raster.StartY, context.Interior.Top) : raster.StartY;
                int endY = useAnalytic ? Math.Min(raster.EndY, context.Interior.Bottom) : raster.EndY;
                long coverage = useAnalytic ? (long)(endY - startY) * (endX - startX) : (long)(endY - startY) * (endX - startX) * raster.Points.Length;
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
                    for (int x = startX; x < endX; x++)
                    {
                        if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                        double pixelCoverage = !useAnalytic && scan.MightCover(x) ? PixelCoverage(raster, contourEnds, nestingSigns, polygon.IsEvenOdd, raster.Signs, x, y, cancellationToken) : 0.0;
                        int offset = (y * outputWidth + x) * 4;
                        if (useAnalytic) walker.PaintPixel(x - storageStartX, destination.AsSpan(offset, 4), analyticPaint, raster.Clip is null ? SvgAnalyticClipCoverage.Full : SvgAnalyticRectangleClip.GetCoverage(raster.Clip.Value, raster.ClipAntialiasing, x, y));
                        else BlendSourceOver(destination.AsSpan(offset, 4), WithCoverage(polygon.Color, pixelCoverage));
                    }
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
            SvgGradientCoordinates nativeCoordinates = SvgGradientCoordinates.CreateLinear(definition, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset);
            if (offsetSpan <= 0 && definition.Stops.Length != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only two-stop increasing-offset linear gradients are implemented.");

            ReadOnlySpan<int> contourEnds = raster.ContourEnds;
            ReadOnlySpan<int> nestingSigns = raster.Signs is int[] signs ? signs : Array.Empty<int>();
            SvgAnalyticContourPreparation preparation = new(raster.Points, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, raster.Mapping, raster.QuadSpans, raster.CubicSpans, new(raster.PathStartX, raster.PathStartY, raster.PathEndX, raster.PathEndY), outputWidth, outputHeight);
            foreach (SvgCpuRasterContext context in rasterGrid.GetContexts(new(raster.StartX, raster.StartY, raster.EndX, raster.EndY)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (workUnits >= options.Limits.MaximumWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "CPU raster context traversal exceeds the configured limit.");
                workUnits++;
                if (raster.Clip is not null)
                {
                    if (4 > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle paint-clip classification exceeds the configured limit.");
                    workUnits += 4;
                }
                int storageStartX = Math.Max(raster.PathStartX, context.BitmapClip.Left);
                int storageEndX = Math.Min(raster.PathEndX, context.BitmapClip.Right);
                using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, storageStartX, storageEndX, raster.PathStartY, raster.PathEndY, raster.GetScanWindow(context), raster.GetConstructionClip(context), raster.GetPaintClip(context), options.Limits.MaximumWorkUnits - workUnits, budget, out long analyticSetup, out bool useAnalytic, cancellationToken);
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
                for (int x = startX; x < endX; x++)
                {
                    if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                    double pixelCoverage = !useAnalytic && scan.MightCover(x) ? PixelCoverage(raster, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, x, y, cancellationToken) : 0.0;
                    double deviceX = (x + 0.5 - horizontalOffset) / horizontalScale;
                    double position;
                    if (!nativeCoordinates.TryLinear(x, y, rasterGrid, out position))
                    {
                        if (definition.IsUserSpace && Math.Abs(definition.ReferenceScaleX) != Math.Abs(definition.ReferenceScaleY))
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
                    SvgShaderPaint sample = SvgShaderPaint.Ramp(definition.Stops, definition.Start, definition.End, definition.Offset1, definition.Offset2, position, outline.Opacity, rasterGrid.GetGradientDither(x, y));
                    if (useAnalytic) walker.PaintPixel(x - storageStartX, destination.AsSpan(offset, 4), SvgAnalyticPaint.Shader(sample), raster.Clip is null ? SvgAnalyticClipCoverage.Full : SvgAnalyticRectangleClip.GetCoverage(raster.Clip.Value, raster.ClipAntialiasing, x, y));
                    else sample.Blend(destination.AsSpan(offset, 4), CoverageAlpha(255, pixelCoverage));
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
            SvgGradientCoordinates nativeCoordinates = SvgGradientCoordinates.CreateRadial(definition, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset);
            if (definition.Radius <= 0 && definition.Stops.Length != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive-radius radial gradients are implemented.");

            ReadOnlySpan<int> contourEnds = raster.ContourEnds;
            ReadOnlySpan<int> nestingSigns = raster.Signs is int[] signs ? signs : Array.Empty<int>();
            SvgAnalyticContourPreparation preparation = new(raster.Points, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, raster.Mapping, raster.QuadSpans, raster.CubicSpans, new(raster.PathStartX, raster.PathStartY, raster.PathEndX, raster.PathEndY), outputWidth, outputHeight);
            foreach (SvgCpuRasterContext context in rasterGrid.GetContexts(new(raster.StartX, raster.StartY, raster.EndX, raster.EndY)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (workUnits >= options.Limits.MaximumWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "CPU raster context traversal exceeds the configured limit.");
                workUnits++;
                if (raster.Clip is not null)
                {
                    if (4 > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle paint-clip classification exceeds the configured limit.");
                    workUnits += 4;
                }
                int storageStartX = Math.Max(raster.PathStartX, context.BitmapClip.Left);
                int storageEndX = Math.Min(raster.PathEndX, context.BitmapClip.Right);
                using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, storageStartX, storageEndX, raster.PathStartY, raster.PathEndY, raster.GetScanWindow(context), raster.GetConstructionClip(context), raster.GetPaintClip(context), options.Limits.MaximumWorkUnits - workUnits, budget, out long analyticSetup, out bool useAnalytic, cancellationToken);
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
                for (int x = startX; x < endX; x++)
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
                    if (offsetX == 0 && offsetY == 0)
                    {
                        if (!nativeCoordinates.TryRadial(x, y, rasterGrid, out position))
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
                    SvgShaderPaint sample = SvgShaderPaint.Ramp(definition.Stops, definition.Start, definition.End, definition.Offset1, definition.Offset2, position, outline.Opacity, rasterGrid.GetGradientDither(x, y));
                    if (useAnalytic) walker.PaintPixel(x - storageStartX, destination.AsSpan(offset, 4), SvgAnalyticPaint.Shader(sample), raster.Clip is null ? SvgAnalyticClipCoverage.Full : SvgAnalyticRectangleClip.GetCoverage(raster.Clip.Value, raster.ClipAntialiasing, x, y));
                    else sample.Blend(destination.AsSpan(offset, 4), CoverageAlpha(255, pixelCoverage));
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
            SvgPatternGeometry source = new(outline.Content, outline.OriginX, outline.OriginY, outline.TileWidth, outline.TileHeight, default, outline.Opacity, null);
            if (!TryMapPatternTile(source, out SvgPatternGeometry? mappedTile)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills under viewBox or output scaling are not implemented.");
            SvgPatternGeometry tile = mappedTile ?? source;
            long tilePixels = (long)tile.TileWidth * tile.TileHeight;
            if (tilePixels <= 0 || tilePixels > options.Limits.MaximumPixels) throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern tile surface exceeds the configured pixel limit.");
            using RenderBudget.BudgetReservation tileReservation = budget.Reserve(checked(tilePixels * 4), SvgMemoryCategory.Surface);
            byte[] blitted = new byte[(int)tilePixels * 4];
            BlitPatternTile(tile, blitted);
            ReadOnlySpan<int> contourEnds = raster.ContourEnds;
            ReadOnlySpan<int> nestingSigns = raster.Signs is int[] signs ? signs : Array.Empty<int>();
            SvgAnalyticContourPreparation preparation = new(raster.Points, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, raster.Mapping, raster.QuadSpans, raster.CubicSpans, new(raster.PathStartX, raster.PathStartY, raster.PathEndX, raster.PathEndY), outputWidth, outputHeight);
            foreach (SvgCpuRasterContext context in rasterGrid.GetContexts(new(raster.StartX, raster.StartY, raster.EndX, raster.EndY)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (workUnits >= options.Limits.MaximumWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "CPU raster context traversal exceeds the configured limit.");
                workUnits++;
                if (raster.Clip is not null)
                {
                    if (4 > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle paint-clip classification exceeds the configured limit.");
                    workUnits += 4;
                }
                int storageStartX = Math.Max(raster.PathStartX, context.BitmapClip.Left);
                int storageEndX = Math.Min(raster.PathEndX, context.BitmapClip.Right);
                using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, storageStartX, storageEndX, raster.PathStartY, raster.PathEndY, raster.GetScanWindow(context), raster.GetConstructionClip(context), raster.GetPaintClip(context), options.Limits.MaximumWorkUnits - workUnits, budget, out long analyticSetup, out bool useAnalytic, cancellationToken);
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
                for (int x = startX; x < endX; x++)
                {
                    if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                    double pixelCoverage = !useAnalytic && scan.MightCover(x) ? PixelCoverage(raster, contourEnds, nestingSigns, outline.IsEvenOdd, raster.Signs, x, y, cancellationToken) : 0.0;
                    int tileX = PositiveModulo(x - tile.OriginX, tile.TileWidth);
                    int tileY = PositiveModulo(y - tile.OriginY, tile.TileHeight);
                    int tileOffset = (tileY * tile.TileWidth + tileX) * 4;
                    int offset = (y * outputWidth + x) * 4;
                    SvgShaderPaint sample = SvgShaderPaint.Pattern(blitted[tileOffset], blitted[tileOffset + 1], blitted[tileOffset + 2], blitted[tileOffset + 3], tile.Opacity);
                    if (useAnalytic) walker.PaintPixel(x - storageStartX, destination.AsSpan(offset, 4), SvgAnalyticPaint.Shader(sample), raster.Clip is null ? SvgAnalyticClipCoverage.Full : SvgAnalyticRectangleClip.GetCoverage(raster.Clip.Value, raster.ClipAntialiasing, x, y));
                    else sample.Blend(destination.AsSpan(offset, 4), CoverageAlpha(255, pixelCoverage));
                }
                }
                // Structural fallback belongs to the original fill and runs once.
                if (!useAnalytic) break;
            }
        }

        bool TryMapPatternTile(SvgPatternGeometry pattern, out SvgPatternGeometry? mapped)
        {
            mapped = null;
            if (horizontalScale == 1 && verticalScale == 1 && horizontalOffset == 0 && verticalOffset == 0) return true;
            if (pattern.Content.Stroke is not null) return false;
            double originX = pattern.OriginX * horizontalScale + horizontalOffset;
            double originY = pattern.OriginY * verticalScale + verticalOffset;
            double tileWidth = pattern.TileWidth * horizontalScale;
            double tileHeight = pattern.TileHeight * verticalScale;
            double contentX = pattern.Content.X * horizontalScale + horizontalOffset;
            double contentY = pattern.Content.Y * verticalScale + verticalOffset;
            double contentWidth = pattern.Content.Width * horizontalScale;
            double contentHeight = pattern.Content.Height * verticalScale;
            if (!IsDeviceInteger(originX) || !IsDeviceInteger(originY) || !IsDeviceInteger(tileWidth) || !IsDeviceInteger(tileHeight) || !IsDeviceInteger(contentX) || !IsDeviceInteger(contentY) || !IsDeviceInteger(contentWidth) || !IsDeviceInteger(contentHeight)) return false;
            SvgClipBox? contentClip = pattern.Content.Clip is {} clip ? new SvgClipBox(clip.X * horizontalScale + horizontalOffset, clip.Y * verticalScale + verticalOffset, clip.Width * horizontalScale, clip.Height * verticalScale) { Antialiasing = clip.Antialiasing } : null;
            mapped = new SvgPatternGeometry(new SvgRectangle(contentX, contentY, contentWidth, contentHeight, pattern.Content.Color, pattern.Content.HasFill, null) { Clip = contentClip }, (int)originX, (int)originY, (int)tileWidth, (int)tileHeight, pattern.Fill, pattern.Opacity, pattern.Stroke);
            return true;

            static bool IsDeviceInteger(double value)
                => double.IsFinite(value) && value == Math.Truncate(value) && value >= int.MinValue && value <= int.MaxValue;
        }

        void RasterizePatternFill(SvgPatternGeometry pattern, byte[] destination)
        {
            if (!pattern.Fill.HasFill && pattern.Stroke is null) return;
            if (!TryMapPatternTile(pattern, out SvgPatternGeometry? mapped)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills under viewBox or output scaling are not implemented.");
            SvgPatternGeometry source = mapped ?? pattern;
            long tilePixels = (long)source.TileWidth * source.TileHeight;
            if (tilePixels <= 0 || tilePixels > options.Limits.MaximumPixels) throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern tile surface exceeds the configured pixel limit.");
            using RenderBudget.BudgetReservation tileReservation = budget.Reserve(checked(tilePixels * 4), SvgMemoryCategory.Surface);
            byte[] tile = new byte[(int)tilePixels * 4];
            BlitPatternTile(source, tile);
            if (source.Fill.HasFill) StampPatternFill(source, tile, destination);
            if (source.Stroke is SvgStroke stroke) RasterizePatternStroke(source, stroke, destination);
        }

        void BlitPatternTile(SvgPatternGeometry pattern, byte[] tile)
        {
            BlitPatternBox(pattern.Content, tile, pattern);
            if (pattern.Content.Stroke is SvgStroke stroke)
            {
                double halfWidth = stroke.Width / 2d;
                BlitPatternBox(new SvgRectangle(pattern.Content.X - halfWidth, pattern.Content.Y - halfWidth, pattern.Content.Width + stroke.Width, stroke.Width, stroke.Color, true, null) with { Clip = pattern.Content.Clip }, tile, pattern);
                BlitPatternBox(new SvgRectangle(pattern.Content.X - halfWidth, pattern.Content.Y + pattern.Content.Height - halfWidth, pattern.Content.Width + stroke.Width, stroke.Width, stroke.Color, true, null) with { Clip = pattern.Content.Clip }, tile, pattern);
                BlitPatternBox(new SvgRectangle(pattern.Content.X - halfWidth, pattern.Content.Y + halfWidth, stroke.Width, pattern.Content.Height - stroke.Width, stroke.Color, true, null) with { Clip = pattern.Content.Clip }, tile, pattern);
                BlitPatternBox(new SvgRectangle(pattern.Content.X + pattern.Content.Width - halfWidth, pattern.Content.Y + halfWidth, stroke.Width, pattern.Content.Height - stroke.Width, stroke.Color, true, null) with { Clip = pattern.Content.Clip }, tile, pattern);
            }
        }

        void BlitPatternBox(SvgRectangle box, byte[] tile, SvgPatternGeometry pattern)
        {
            int setupVisits = box.Clip is null ? 4 : 8;
            if (setupVisits > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern rectangle preparation exceeds the configured work limit.");
            workUnits += setupVisits;
            cancellationToken.ThrowIfCancellationRequested();
            double left = box.X - pattern.OriginX, top = box.Y - pattern.OriginY;
            double right = left + box.Width, bottom = top + box.Height;
            SvgRectangleCoverage coverage = SvgRectangleCoverage.Create(left, top, right, bottom, pattern.TileWidth, pattern.TileHeight);
            SvgClipBox? clip = box.Clip is {} bounds ? new SvgClipBox(bounds.X - pattern.OriginX, bounds.Y - pattern.OriginY, bounds.Width, bounds.Height) { Antialiasing = bounds.Antialiasing } : null;
            int startX = ClampFloor(clip is {} cx ? Math.Max(left, cx.X) : left, pattern.TileWidth);
            int startY = ClampFloor(clip is {} cy ? Math.Max(top, cy.Y) : top, pattern.TileHeight);
            int endX = Math.Max(startX, ClampCeiling(clip is {} ex ? Math.Min(right, ex.Right) : right, pattern.TileWidth));
            int endY = Math.Max(startY, ClampCeiling(clip is {} ey ? Math.Min(bottom, ey.Bottom) : bottom, pattern.TileHeight));
            long blitted = (long)(endY - startY) * (endX - startX);
            if (blitted > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern tile raster work exceeds the configured limit.");
            workUnits += blitted;
            SvgAnalyticPaint paint = SvgAnalyticPaint.Solid(box.Color);
            for (int y = startY; y < endY; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = startX; x < endX; x++)
                {
                    if ((x & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                    int offset = (y * pattern.TileWidth + x) * 4;
                    byte alpha = coverage.GetAlpha(x, y);
                    if (clip is {} paintClip) alpha = SvgAnalyticRectangleClip.GetCoverage((paintClip.X, paintClip.Y, paintClip.Right, paintClip.Bottom), paintClip.Antialiasing, x, y).Apply(alpha);
                    paint.Blend(tile.AsSpan(offset, 4), alpha, SvgAnalyticPaintKind.Direct);
                }
            }
        }

        void StampPatternFill(SvgPatternGeometry pattern, byte[] tile, byte[] destination)
        {
            SvgRectangleRaster raster = PrepareRectangle(pattern.Fill);
            int startX = raster.Left, endX = raster.Right, startY = raster.Top, endY = raster.Bottom;
            for (int y = startY; y < endY; y++)
            {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = startX; x < endX; x++)
            {
                int tileX = PositiveModulo(x - pattern.OriginX, pattern.TileWidth);
                int tileY = PositiveModulo(y - pattern.OriginY, pattern.TileHeight);
                int tileOffset = (tileY * pattern.TileWidth + tileX) * 4;
                int offset = (y * outputWidth + x) * 4;
                SvgShaderPaint.Pattern(tile[tileOffset], tile[tileOffset + 1], tile[tileOffset + 2], tile[tileOffset + 3], pattern.Opacity).Blend(destination.AsSpan(offset, 4), raster.GetAlpha(x, y));
            }
            }
        }

        void RasterizePatternStroke(SvgPatternGeometry pattern, SvgStroke stroke, byte[] destination)
        {
            double halfWidth = stroke.Width / 2d;
            RasterizeSolidRectangle(new SvgRectangle(pattern.Fill.X - halfWidth, pattern.Fill.Y - halfWidth, pattern.Fill.Width + stroke.Width, stroke.Width, stroke.Color, true, null) with { Clip = pattern.Fill.Clip }, destination);
            RasterizeSolidRectangle(new SvgRectangle(pattern.Fill.X - halfWidth, pattern.Fill.Y + pattern.Fill.Height - halfWidth, pattern.Fill.Width + stroke.Width, stroke.Width, stroke.Color, true, null) with { Clip = pattern.Fill.Clip }, destination);
            RasterizeSolidRectangle(new SvgRectangle(pattern.Fill.X - halfWidth, pattern.Fill.Y + halfWidth, stroke.Width, pattern.Fill.Height - stroke.Width, stroke.Color, true, null) with { Clip = pattern.Fill.Clip }, destination);
            RasterizeSolidRectangle(new SvgRectangle(pattern.Fill.X + pattern.Fill.Width - halfWidth, pattern.Fill.Y + halfWidth, stroke.Width, pattern.Fill.Height - stroke.Width, stroke.Color, true, null) with { Clip = pattern.Fill.Clip }, destination);
        }

        static int PositiveModulo(int value, int modulus) => ((value % modulus) + modulus) % modulus;

        void RasterizeGradientFill(SvgGradientGeometry gradient, byte[] destination)
        {
            if (!gradient.Fill.HasFill && gradient.Stroke is null) return;
            RasterizeGradientArea(gradient.Gradient, gradient.Box, gradient.Fill, gradient.Opacity, gradient.Fill.HasFill, destination);
            if (gradient.Stroke is SvgStroke stroke) RasterizeGradientStroke(gradient, stroke, destination);
        }

        void RasterizeGradientStrokeFill(SvgGradientStrokeGeometry stroke, byte[] destination)
        {
            if (stroke.Fill.HasFill) RasterizeSolidRectangle(stroke.Fill, destination);
            if (!stroke.HasStroke) return;
            RasterizeGradientArea(stroke.Gradient, stroke.Box, stroke.Top, stroke.Opacity, true, destination);
            RasterizeGradientArea(stroke.Gradient, stroke.Box, stroke.Bottom, stroke.Opacity, true, destination);
            RasterizeGradientArea(stroke.Gradient, stroke.Box, stroke.Left, stroke.Opacity, true, destination);
            RasterizeGradientArea(stroke.Gradient, stroke.Box, stroke.Right, stroke.Opacity, true, destination);
        }

        void RasterizeGradientArea(SvgLinearGradient definition, SvgRectangle box, SvgRectangle area, byte opacity, bool paint, byte[] destination)
        {
            SvgRectangleRaster raster = PrepareRectangle(area);
            int startX = raster.Left, endX = raster.Right, startY = raster.Top, endY = raster.Bottom;
            double directionX = definition.X2 - definition.X1;
            double directionY = definition.Y2 - definition.Y1;
            double denominator = directionX * directionX + directionY * directionY;
            double offsetSpan = definition.Offset2 - definition.Offset1;
            SvgGradientCoordinates nativeCoordinates = SvgGradientCoordinates.CreateLinear(definition, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset);
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
                if (!nativeCoordinates.TryLinear(x, y, rasterGrid, out position))
                {
                    if (definition.IsUserSpace && Math.Abs(definition.ReferenceScaleX) != Math.Abs(definition.ReferenceScaleY))
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
                SvgShaderPaint.Ramp(definition.Stops, definition.Start, definition.End, definition.Offset1, definition.Offset2, position, opacity, rasterGrid.GetGradientDither(x, y)).Blend(destination.AsSpan(offset, 4), raster.GetAlpha(x, y));
            }
            }

        }
        void RasterizeRadialFill(SvgRadialGeometry radial, byte[] destination)
        {
            if (!radial.Fill.HasFill && radial.Stroke is null) return;
            RasterizeRadialArea(radial.Gradient, radial.Box, radial.Fill, radial.Opacity, radial.Fill.HasFill, destination);
            if (radial.Stroke is SvgStroke stroke)
            {
                double halfWidth = stroke.Width / 2d;
                RasterizeSolidRectangle(new SvgRectangle(radial.Fill.X - halfWidth, radial.Fill.Y - halfWidth, radial.Fill.Width + stroke.Width, stroke.Width, stroke.Color, true, null) with { Clip = radial.Fill.Clip }, destination);
                RasterizeSolidRectangle(new SvgRectangle(radial.Fill.X - halfWidth, radial.Fill.Y + radial.Fill.Height - halfWidth, radial.Fill.Width + stroke.Width, stroke.Width, stroke.Color, true, null) with { Clip = radial.Fill.Clip }, destination);
                RasterizeSolidRectangle(new SvgRectangle(radial.Fill.X - halfWidth, radial.Fill.Y + halfWidth, stroke.Width, radial.Fill.Height - stroke.Width, stroke.Color, true, null) with { Clip = radial.Fill.Clip }, destination);
                RasterizeSolidRectangle(new SvgRectangle(radial.Fill.X + radial.Fill.Width - halfWidth, radial.Fill.Y + halfWidth, stroke.Width, radial.Fill.Height - stroke.Width, stroke.Color, true, null) with { Clip = radial.Fill.Clip }, destination);
            }
        }

        void RasterizeRadialArea(SvgRadialGradient definition, SvgRectangle box, SvgRectangle area, byte opacity, bool paint, byte[] destination)
        {
            SvgRectangleRaster raster = PrepareRectangle(area);
            SvgGradientCoordinates nativeCoordinates = SvgGradientCoordinates.CreateRadial(definition, box, horizontalScale, verticalScale, horizontalOffset, verticalOffset);
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
                    if (!nativeCoordinates.TryRadial(x, y, rasterGrid, out position))
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
                SvgShaderPaint.Ramp(definition.Stops, definition.Start, definition.End, definition.Offset1, definition.Offset2, position, opacity, rasterGrid.GetGradientDither(x, y)).Blend(destination.AsSpan(offset, 4), raster.GetAlpha(x, y));
            }
            }
        }

        void RasterizeGradientStroke(SvgGradientGeometry gradient, SvgStroke stroke, byte[] destination)
        {
            double halfWidth = stroke.Width / 2d;
            RasterizeSolidRectangle(new SvgRectangle(gradient.Fill.X - halfWidth, gradient.Fill.Y - halfWidth, gradient.Fill.Width + stroke.Width, stroke.Width, stroke.Color, true, null) with { Clip = gradient.Fill.Clip }, destination);
            RasterizeSolidRectangle(new SvgRectangle(gradient.Fill.X - halfWidth, gradient.Fill.Y + gradient.Fill.Height - halfWidth, gradient.Fill.Width + stroke.Width, stroke.Width, stroke.Color, true, null) with { Clip = gradient.Fill.Clip }, destination);
            RasterizeSolidRectangle(new SvgRectangle(gradient.Fill.X - halfWidth, gradient.Fill.Y + halfWidth, stroke.Width, gradient.Fill.Height - stroke.Width, stroke.Color, true, null) with { Clip = gradient.Fill.Clip }, destination);
            RasterizeSolidRectangle(new SvgRectangle(gradient.Fill.X + gradient.Fill.Width - halfWidth, gradient.Fill.Y + halfWidth, stroke.Width, gradient.Fill.Height - stroke.Width, stroke.Color, true, null) with { Clip = gradient.Fill.Clip }, destination);
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
                byte alpha = SvgGlyphCoverage.GetCoverage(glyph.Contours, sourceLeft + (x - startX) * sourcePixelWidth, sourceTop + (y - startY) * sourcePixelHeight, sourcePixelWidth, sourcePixelHeight, GlyphSamplesPerAxis, cancellationToken);
                if (alpha == 0) continue;
                BlendSourceOver(destination.AsSpan((y * outputWidth + x) * 4, 4), new SvgColor(glyph.Color.Red, glyph.Color.Green, glyph.Color.Blue, MultiplyAlpha(glyph.Color.Alpha, alpha)));
            }

            void Include(SvgGlyphOutlinePoint point)
            {
                minimumX = Math.Min(minimumX, point.X); minimumY = Math.Min(minimumY, point.Y);
                maximumX = Math.Max(maximumX, point.X); maximumY = Math.Max(maximumY, point.Y);
            }
        }

        void RasterizeRectangle(SvgRectangle rectangle, byte[] destination)
        {
            if (rectangle.HasFill) RasterizeSolidRectangle(rectangle, destination);
            if (rectangle.Stroke is SvgStroke stroke)
            {
                double halfWidth = stroke.Width / 2d;
                RasterizeSolidRectangle(new SvgRectangle(rectangle.X - halfWidth, rectangle.Y - halfWidth, rectangle.Width + stroke.Width, stroke.Width, stroke.Color, true, null) with { Clip = rectangle.Clip }, destination);
                RasterizeSolidRectangle(new SvgRectangle(rectangle.X - halfWidth, rectangle.Y + rectangle.Height - halfWidth, rectangle.Width + stroke.Width, stroke.Width, stroke.Color, true, null) with { Clip = rectangle.Clip }, destination);
                RasterizeSolidRectangle(new SvgRectangle(rectangle.X - halfWidth, rectangle.Y + halfWidth, stroke.Width, rectangle.Height - stroke.Width, stroke.Color, true, null) with { Clip = rectangle.Clip }, destination);
                RasterizeSolidRectangle(new SvgRectangle(rectangle.X + rectangle.Width - halfWidth, rectangle.Y + halfWidth, stroke.Width, rectangle.Height - stroke.Width, stroke.Color, true, null) with { Clip = rectangle.Clip }, destination);
            }
        }

        void RasterizeSolidRectangle(SvgRectangle rectangle, byte[] destination)
        {
            SvgRectangleRaster raster = PrepareRectangle(rectangle);
            int startX = raster.Left, endX = raster.Right, startY = raster.Top, endY = raster.Bottom;
            SvgAnalyticPaint rectanglePaint = SvgAnalyticPaint.Solid(rectangle.Color);
            for (int y = startY; y < endY; y++)
            {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = startX; x < endX; x++)
            {
                byte rectangleAlpha = raster.GetAlpha(x, y);
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
        static SvgCubicSpan[] MapCubicSpanControls(SvgCubicSpan[] spans, double horizontalScale, double horizontalOffset, double verticalScale, double verticalOffset, int outputWidth, int outputHeight, out bool canvasContainsControls, CancellationToken cancellationToken)
        {
            canvasContainsControls = true;
            if (spans.Length == 0) return Array.Empty<SvgCubicSpan>();
            SvgCubicSpan[] mapped = new SvgCubicSpan[spans.Length];
            for (int index = 0; index < spans.Length; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                SvgDevicePoint first = spans[index].FirstControl;
                SvgDevicePoint second = spans[index].SecondControl;
                SvgDevicePoint mappedFirst = new(MapNativePolygonEdge(first.X, horizontalScale, horizontalOffset), MapNativePolygonEdge(first.Y, verticalScale, verticalOffset));
                SvgDevicePoint mappedSecond = new(MapNativePolygonEdge(second.X, horizontalScale, horizontalOffset), MapNativePolygonEdge(second.Y, verticalScale, verticalOffset));
                canvasContainsControls &= mappedFirst.X >= 0 && mappedFirst.X <= outputWidth && mappedFirst.Y >= 0 && mappedFirst.Y <= outputHeight
                    && mappedSecond.X >= 0 && mappedSecond.X <= outputWidth && mappedSecond.Y >= 0 && mappedSecond.Y <= outputHeight;
                mapped[index] = new SvgCubicSpan(spans[index].Contour, spans[index].StartIndex, spans[index].EndIndex, mappedFirst, mappedSecond);
            }
            return mapped;
        }

                // Maps retained quadratic controls through the output scale without grid snapping; curve data never takes the quarter discipline.
        static SvgQuadSpan[] MapQuadSpanControls(SvgQuadSpan[] spans, double horizontalScale, double horizontalOffset, double verticalScale, double verticalOffset, int outputWidth, int outputHeight, bool nativeMapping, out bool canvasContainsControls, CancellationToken cancellationToken)
        {
            canvasContainsControls = true;
            if (spans.Length == 0) return Array.Empty<SvgQuadSpan>();
            SvgQuadSpan[] mapped = new SvgQuadSpan[spans.Length];
            for (int index = 0; index < spans.Length; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                SvgDevicePoint control = spans[index].Control;
                bool native = nativeMapping || spans[index].ConicWeight != 1f;
                SvgDevicePoint mappedControl = native
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
