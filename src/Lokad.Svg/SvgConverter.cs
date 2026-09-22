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
        (double width, double height) = options.Viewport is SvgViewport viewport ? (viewport.Width, viewport.Height) : (root.Width, root.Height) switch
        {
            (double explicitWidth, double explicitHeight) => (explicitWidth, explicitHeight),
            (null, null) => (300, 150),
            (double explicitWidth, null) when root.ViewBox is SvgViewBox box => (explicitWidth, explicitWidth * box.Height / box.Width),
            (null, double explicitHeight) when root.ViewBox is SvgViewBox box => (explicitHeight * box.Width / box.Height, explicitHeight),
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Both root dimensions or neither root dimension are required currently.")
        };
        double scaledWidth = width * options.OutputScale;
        double scaledHeight = height * options.OutputScale;
        if (!double.IsFinite(scaledWidth) || !double.IsFinite(scaledHeight) || scaledWidth <= 0 || scaledHeight <= 0 || scaledWidth != Math.Truncate(scaledWidth) || scaledHeight != Math.Truncate(scaledHeight)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive finite integral output dimensions are implemented currently.");
        if (scaledWidth > options.Limits.MaximumDimension || scaledHeight > options.Limits.MaximumDimension) throw new SvgException(SvgErrorCode.LimitExceeded, "Output dimensions exceed the configured dimension limit.");
        int outputWidth = (int)scaledWidth;
        int outputHeight = (int)scaledHeight;
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
            int commandIndex = RenderCommands(0, pixels, false);
            if (commandIndex != root.Commands.Length) throw new SvgException(SvgErrorCode.MalformedInput, "SVG group structure is unbalanced.");
            return new RasterizedSurface(outputWidth, outputHeight, pixels, surface, workUnits);
        }
        catch
        {
            surface.Dispose();
            throw;
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
                    for (int pixel = 0; pixel < pixelCount; pixel++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int offset = pixel * 4;
                        SvgColor source = new(groupPixels[offset], groupPixels[offset + 1], groupPixels[offset + 2], MultiplyAlpha(groupPixels[offset + 3], command.Opacity));
                        BlendSourceOver(destination.AsSpan(offset, 4), source, false);
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

        void RasterizePatternFill(SvgPatternGeometry pattern, byte[] destination)
        {
            if (!pattern.Fill.HasFill && pattern.Stroke is null) return;
            long tilePixels = (long)pattern.TileWidth * pattern.TileHeight;
            if (tilePixels <= 0 || tilePixels > options.Limits.MaximumPixels) throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern tile surface exceeds the configured pixel limit.");
            using RenderBudget.BudgetReservation tileReservation = budget.Reserve(checked(tilePixels * 4), SvgMemoryCategory.Surface);
            byte[] tile = new byte[(int)tilePixels * 4];
            BlitPatternTile(pattern, tile);
            if (pattern.Fill.HasFill) StampPatternFill(pattern, tile, destination);
            if (pattern.Stroke is SvgStroke stroke) RasterizePatternStroke(pattern, stroke, destination);
        }

        void BlitPatternTile(SvgPatternGeometry pattern, byte[] tile)
        {
            BlitPatternBox(pattern.Content, tile, pattern);
            if (pattern.Content.Stroke is SvgStroke stroke)
            {
                double halfWidth = stroke.Width / 2d;
                BlitPatternBox(new SvgRectangle(pattern.Content.X - halfWidth, pattern.Content.Y - halfWidth, pattern.Content.Width + stroke.Width, stroke.Width, stroke.Color, true, null), tile, pattern);
                BlitPatternBox(new SvgRectangle(pattern.Content.X - halfWidth, pattern.Content.Y + pattern.Content.Height - halfWidth, pattern.Content.Width + stroke.Width, stroke.Width, stroke.Color, true, null), tile, pattern);
                BlitPatternBox(new SvgRectangle(pattern.Content.X - halfWidth, pattern.Content.Y + halfWidth, stroke.Width, pattern.Content.Height - stroke.Width, stroke.Color, true, null), tile, pattern);
                BlitPatternBox(new SvgRectangle(pattern.Content.X + pattern.Content.Width - halfWidth, pattern.Content.Y + halfWidth, stroke.Width, pattern.Content.Height - stroke.Width, stroke.Color, true, null), tile, pattern);
            }
        }

        void BlitPatternBox(SvgRectangle box, byte[] tile, SvgPatternGeometry pattern)
        {
            int startX = (int)(box.X - pattern.OriginX);
            int startY = (int)(box.Y - pattern.OriginY);
            int endX = startX + (int)box.Width;
            int endY = startY + (int)box.Height;
            for (int y = startY; y < endY; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = startX; x < endX; x++)
                {
                    int offset = (y * pattern.TileWidth + x) * 4;
                    BlendSourceOver(tile.AsSpan(offset, 4), box.Color, false);
                }
            }
        }

        void StampPatternFill(SvgPatternGeometry pattern, byte[] tile, byte[] destination)
        {
            double fillTop = MapRectangleEdge(pattern.Fill.Y, verticalScale, verticalOffset);
            double fillBottom = MapRectangleEdge(pattern.Fill.Y + pattern.Fill.Height, verticalScale, verticalOffset);
            double fillLeft = MapRectangleEdge(pattern.Fill.X, horizontalScale, horizontalOffset);
            double fillRight = MapRectangleEdge(pattern.Fill.X + pattern.Fill.Width, horizontalScale, horizontalOffset);
            bool hasFractionalEdge = fillTop != Math.Truncate(fillTop) || fillBottom != Math.Truncate(fillBottom) || fillLeft != Math.Truncate(fillLeft) || fillRight != Math.Truncate(fillRight);
            int startY = ClampFloor(fillTop, outputHeight);
            int endY = ClampCeiling(fillBottom, outputHeight);
            int startX = ClampFloor(fillLeft, outputWidth);
            int endX = ClampCeiling(fillRight, outputWidth);
            long coverage = (long)(endY - startY) * (endX - startX);
            if (coverage > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern fill raster work exceeds the configured limit.");
            workUnits += coverage;
            for (int y = startY; y < endY; y++)
            {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = startX; x < endX; x++)
            {
                double verticalCoverage = Math.Clamp(Math.Min(fillBottom, y + 1) - Math.Max(fillTop, y), 0, 1);
                double horizontalCoverage = Math.Clamp(Math.Min(fillRight, x + 1) - Math.Max(fillLeft, x), 0, 1);
                int tileX = PositiveModulo(x - pattern.OriginX, pattern.TileWidth);
                int tileY = PositiveModulo(y - pattern.OriginY, pattern.TileHeight);
                int tileOffset = (tileY * pattern.TileWidth + tileX) * 4;
                SvgColor tileColor = new(tile[tileOffset], tile[tileOffset + 1], tile[tileOffset + 2], MultiplyAlpha(tile[tileOffset + 3], pattern.Opacity));
                SvgColor color = WithCoverage(tileColor, horizontalCoverage * verticalCoverage);
                int offset = (y * outputWidth + x) * 4;
                BlendSourceOver(destination.AsSpan(offset, 4), color, hasFractionalEdge);
            }
            }
        }

        void RasterizePatternStroke(SvgPatternGeometry pattern, SvgStroke stroke, byte[] destination)
        {
            double halfWidth = stroke.Width / 2d;
            RasterizeSolidRectangle(new SvgRectangle(pattern.Fill.X - halfWidth, pattern.Fill.Y - halfWidth, pattern.Fill.Width + stroke.Width, stroke.Width, stroke.Color, true, null), destination);
            RasterizeSolidRectangle(new SvgRectangle(pattern.Fill.X - halfWidth, pattern.Fill.Y + pattern.Fill.Height - halfWidth, pattern.Fill.Width + stroke.Width, stroke.Width, stroke.Color, true, null), destination);
            RasterizeSolidRectangle(new SvgRectangle(pattern.Fill.X - halfWidth, pattern.Fill.Y + halfWidth, stroke.Width, pattern.Fill.Height - stroke.Width, stroke.Color, true, null), destination);
            RasterizeSolidRectangle(new SvgRectangle(pattern.Fill.X + pattern.Fill.Width - halfWidth, pattern.Fill.Y + halfWidth, stroke.Width, pattern.Fill.Height - stroke.Width, stroke.Color, true, null), destination);
        }

        static int PositiveModulo(int value, int modulus) => ((value % modulus) + modulus) % modulus;

        static byte InterpolateChannel(byte start, byte end, double position, double dither)
        {
            double value = start + (end - start) * position + dither;
            if (value <= 0) return 0;
            if (value >= 255) return 255;
            return (byte)Math.Floor(value + 1e-9);
        }



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
            double areaTop = MapRectangleEdge(area.Y, verticalScale, verticalOffset);
            double areaBottom = MapRectangleEdge(area.Y + area.Height, verticalScale, verticalOffset);
            double areaLeft = MapRectangleEdge(area.X, horizontalScale, horizontalOffset);
            double areaRight = MapRectangleEdge(area.X + area.Width, horizontalScale, horizontalOffset);
            bool hasFractionalEdge = areaTop != Math.Truncate(areaTop) || areaBottom != Math.Truncate(areaBottom) || areaLeft != Math.Truncate(areaLeft) || areaRight != Math.Truncate(areaRight);
            int startY = ClampFloor(areaTop, outputHeight);
            int endY = ClampCeiling(areaBottom, outputHeight);
            int startX = ClampFloor(areaLeft, outputWidth);
            int endX = ClampCeiling(areaRight, outputWidth);
            long coverage = (long)(endY - startY) * (endX - startX);
            if (coverage > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Gradient fill raster work exceeds the configured limit.");
            workUnits += coverage;
            double directionX = definition.X2 - definition.X1;
            double directionY = definition.Y2 - definition.Y1;
            double denominator = directionX * directionX + directionY * directionY;
            double offsetSpan = definition.Offset2 - definition.Offset1;
            if (paint && offsetSpan <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only two-stop increasing-offset linear gradients are implemented.");
            if (paint && denominator != 0)
            for (int y = startY; y < endY; y++)
            {
            cancellationToken.ThrowIfCancellationRequested();
            double deviceY = (y + 0.5 - verticalOffset) / verticalScale;
            for (int x = startX; x < endX; x++)
            {
                double verticalCoverage = Math.Clamp(Math.Min(areaBottom, y + 1) - Math.Max(areaTop, y), 0, 1);
                double horizontalCoverage = Math.Clamp(Math.Min(areaRight, x + 1) - Math.Max(areaLeft, x), 0, 1);
                double deviceX = (x + 0.5 - horizontalOffset) / horizontalScale;
                double position;
                if (definition.IsUserSpace) position = ((deviceX - definition.X1) * directionX + (deviceY - definition.Y1) * directionY) / denominator;
                else
                {
                    double normalizedX = (deviceX - box.X) / box.Width;
                    double normalizedY = (deviceY - box.Y) / box.Height;
                    position = ((normalizedX - definition.X1) * directionX + (normalizedY - definition.Y1) * directionY) / denominator;
                }
                double local = (position - definition.Offset1) / offsetSpan;
                if (local < 0) local = 0;
                if (local > 1) local = 1;
                position = local;
                double dither = GradientDither[x & 7, y & 7] / 64d;
                SvgColor tileColor = new(InterpolateChannel(definition.Start.Red, definition.End.Red, position, dither), InterpolateChannel(definition.Start.Green, definition.End.Green, position, dither), InterpolateChannel(definition.Start.Blue, definition.End.Blue, position, dither), opacity);
                SvgColor color = WithCoverage(tileColor, horizontalCoverage * verticalCoverage);
                int offset = (y * outputWidth + x) * 4;
                BlendSourceOver(destination.AsSpan(offset, 4), color, hasFractionalEdge);
            }
            }
        }
        void RasterizeGradientStroke(SvgGradientGeometry gradient, SvgStroke stroke, byte[] destination)
        {
            double halfWidth = stroke.Width / 2d;
            RasterizeSolidRectangle(new SvgRectangle(gradient.Fill.X - halfWidth, gradient.Fill.Y - halfWidth, gradient.Fill.Width + stroke.Width, stroke.Width, stroke.Color, true, null), destination);
            RasterizeSolidRectangle(new SvgRectangle(gradient.Fill.X - halfWidth, gradient.Fill.Y + gradient.Fill.Height - halfWidth, gradient.Fill.Width + stroke.Width, stroke.Width, stroke.Color, true, null), destination);
            RasterizeSolidRectangle(new SvgRectangle(gradient.Fill.X - halfWidth, gradient.Fill.Y + halfWidth, stroke.Width, gradient.Fill.Height - stroke.Width, stroke.Color, true, null), destination);
            RasterizeSolidRectangle(new SvgRectangle(gradient.Fill.X + gradient.Fill.Width - halfWidth, gradient.Fill.Y + halfWidth, stroke.Width, gradient.Fill.Height - stroke.Width, stroke.Color, true, null), destination);
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
                BlendSourceOver(destination.AsSpan((y * outputWidth + x) * 4, 4), new SvgColor(glyph.Color.Red, glyph.Color.Green, glyph.Color.Blue, MultiplyAlpha(glyph.Color.Alpha, alpha)), true);
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
                RasterizeSolidRectangle(new SvgRectangle(rectangle.X - halfWidth, rectangle.Y - halfWidth, rectangle.Width + stroke.Width, stroke.Width, stroke.Color, true, null), destination);
                RasterizeSolidRectangle(new SvgRectangle(rectangle.X - halfWidth, rectangle.Y + rectangle.Height - halfWidth, rectangle.Width + stroke.Width, stroke.Width, stroke.Color, true, null), destination);
                RasterizeSolidRectangle(new SvgRectangle(rectangle.X - halfWidth, rectangle.Y + halfWidth, stroke.Width, rectangle.Height - stroke.Width, stroke.Color, true, null), destination);
                RasterizeSolidRectangle(new SvgRectangle(rectangle.X + rectangle.Width - halfWidth, rectangle.Y + halfWidth, stroke.Width, rectangle.Height - stroke.Width, stroke.Color, true, null), destination);
            }
        }

        void RasterizeSolidRectangle(SvgRectangle rectangle, byte[] destination)
        {
            double rectangleY = MapRectangleEdge(rectangle.Y, verticalScale, verticalOffset);
            double rectangleBottom = MapRectangleEdge(rectangle.Y + rectangle.Height, verticalScale, verticalOffset);
            double rectangleX = MapRectangleEdge(rectangle.X, horizontalScale, horizontalOffset);
            double rectangleRight = MapRectangleEdge(rectangle.X + rectangle.Width, horizontalScale, horizontalOffset);
            bool hasFractionalEdge = rectangleY != Math.Truncate(rectangleY) || rectangleBottom != Math.Truncate(rectangleBottom) || rectangleX != Math.Truncate(rectangleX) || rectangleRight != Math.Truncate(rectangleRight);
            int startY = ClampFloor(rectangleY, outputHeight);
            int endY = ClampCeiling(rectangleBottom, outputHeight);
            int startX = ClampFloor(rectangleX, outputWidth);
            int endX = ClampCeiling(rectangleRight, outputWidth);
            long coverage = (long)(endY - startY) * (endX - startX);
            if (coverage > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle raster work exceeds the configured limit.");
            workUnits += coverage;
            for (int y = startY; y < endY; y++)
            {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = startX; x < endX; x++)
            {
                double verticalCoverage = Math.Clamp(Math.Min(rectangleBottom, y + 1) - Math.Max(rectangleY, y), 0, 1);
                double horizontalCoverage = Math.Clamp(Math.Min(rectangleRight, x + 1) - Math.Max(rectangleX, x), 0, 1);
                SvgColor color = WithCoverage(rectangle.Color, horizontalCoverage * verticalCoverage);
                int offset = (y * outputWidth + x) * 4;
                BlendSourceOver(destination.AsSpan(offset, 4), color, hasFractionalEdge);
            }
            }
        }

        static double MapEdge(double value, double scale, double offset)
        {
            double scaled = value * scale + offset;
            if (!double.IsFinite(scaled)) throw new SvgException(SvgErrorCode.LimitExceeded, "Output scaling and viewBox mapping exceed the admitted numeric range.");
            return scaled;
        }

        static double MapRectangleEdge(double value, double scale, double offset)
        {
            double edge = MapEdge(value, scale, offset);
            if (Math.Abs(edge * 4 - Math.Round(edge * 4, MidpointRounding.AwayFromZero)) > 0.000000001) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle viewBox mapping must preserve quarter-pixel output edges.");
            return edge;
        }

        static int ClampFloor(double value, int maximum) => value <= 0 ? 0 : value >= maximum ? maximum : checked((int)Math.Floor(value));

        static int ClampCeiling(double value, int maximum) => value <= 0 ? 0 : value >= maximum ? maximum : checked((int)Math.Ceiling(value));

        static (double HorizontalScale, double HorizontalOffset, double VerticalScale, double VerticalOffset) GetViewBoxTransform(SvgViewBox? viewBox, double width, double height, double outputScale)
        {
            if (viewBox is not SvgViewBox box) return (outputScale, 0, outputScale, 0);
            double horizontalScale = width / box.Width;
            double verticalScale = height / box.Height;
            if (box.HorizontalAlignment != SvgViewBoxAlignment.None)
            {
                double scale = box.Fit == SvgViewBoxFit.Slice ? Math.Max(horizontalScale, verticalScale) : Math.Min(horizontalScale, verticalScale);
                horizontalScale = scale;
                verticalScale = scale;
            }
            return (horizontalScale * outputScale, (GetAlignmentOffset(width - box.Width * horizontalScale, box.HorizontalAlignment) - box.X * horizontalScale) * outputScale, verticalScale * outputScale, (GetAlignmentOffset(height - box.Height * verticalScale, box.VerticalAlignment) - box.Y * verticalScale) * outputScale);

            static double GetAlignmentOffset(double remaining, SvgViewBoxAlignment alignment) => alignment switch
            {
                SvgViewBoxAlignment.Min or SvgViewBoxAlignment.None => 0,
                SvgViewBoxAlignment.Mid => remaining / 2,
                SvgViewBoxAlignment.Max => remaining,
                _ => throw new InvalidOperationException("Unknown viewBox alignment.")
            };
        }
    }

        private static readonly int[,] GradientDither =
        {
            { 0, 32, 8, 40, 2, 34, 10, 42 },
            { 48, 16, 56, 24, 50, 18, 58, 26 },
            { 12, 44, 4, 36, 14, 46, 6, 38 },
            { 60, 28, 52, 20, 62, 30, 54, 22 },
            { 3, 35, 11, 43, 1, 33, 9, 41 },
            { 51, 19, 59, 27, 49, 17, 57, 25 },
            { 15, 47, 7, 39, 13, 45, 5, 37 },
            { 63, 31, 55, 23, 61, 29, 53, 21 },
        };

    private static SvgRoot ParseRoot(ReadOnlyMemory<byte> svg, SvgRenderOptions options, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        try { return workCounter is null ? SvgRootParser.Parse(svg, options.Limits, budget, options.FontSnapshot, cancellationToken) : SvgRootParser.Parse(svg, options.Limits, budget, options.FontSnapshot, workCounter, cancellationToken); }
        catch (XmlException exception) { throw new SvgException(SvgErrorCode.MalformedInput, "SVG XML is malformed: " + exception.Message); }
    }

    private static void BlendSourceOver(Span<byte> destination, SvgColor source, bool roundRetainedDestination)
    {
        if (source.Alpha == 255) { destination[0] = source.Red; destination[1] = source.Green; destination[2] = source.Blue; destination[3] = 255; return; }
        if (source.Alpha == 0) return;
        int sourceAlpha = source.Alpha;
        int destinationAlpha = destination[3];
        int inverseSourceAlpha = 255 - sourceAlpha;
        int outputAlpha = sourceAlpha + ((destinationAlpha * inverseSourceAlpha + (roundRetainedDestination ? 127 : 0)) / 255);
        if (roundRetainedDestination)
        {
            destination[0] = ComposeFractionalChannel(destination[0], source.Red, destinationAlpha, sourceAlpha, inverseSourceAlpha, outputAlpha);
            destination[1] = ComposeFractionalChannel(destination[1], source.Green, destinationAlpha, sourceAlpha, inverseSourceAlpha, outputAlpha);
            destination[2] = ComposeFractionalChannel(destination[2], source.Blue, destinationAlpha, sourceAlpha, inverseSourceAlpha, outputAlpha);
        }
        else
        {
            destination[0] = (byte)Math.Min((source.Red * sourceAlpha + ((destination[0] * destinationAlpha * inverseSourceAlpha + 127) / 255)) / outputAlpha, 255);
            destination[1] = (byte)Math.Min((source.Green * sourceAlpha + ((destination[1] * destinationAlpha * inverseSourceAlpha + 127) / 255)) / outputAlpha, 255);
            destination[2] = (byte)Math.Min((source.Blue * sourceAlpha + ((destination[2] * destinationAlpha * inverseSourceAlpha + 127) / 255)) / outputAlpha, 255);
        }
        destination[3] = (byte)outputAlpha;
    }

    private static SvgColor WithCoverage(SvgColor color, double coverage) => coverage == 1 ? color : new SvgColor(color.Red, color.Green, color.Blue, checked((byte)Math.Round(color.Alpha * coverage, MidpointRounding.AwayFromZero)));

    private static byte MultiplyAlpha(byte left, byte right) => (byte)((left * right + 127) / 255);

    private static byte ComposeFractionalChannel(byte destination, byte source, int destinationAlpha, int sourceAlpha, int inverseSourceAlpha, int outputAlpha)
    {
        int sourceContribution = (source * sourceAlpha + outputAlpha / 2) / outputAlpha;
        int retainedContribution = (destination * destinationAlpha * inverseSourceAlpha + 255 * outputAlpha - 1) / (255 * outputAlpha);
        return checked((byte)(sourceContribution + retainedContribution));
    }

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
