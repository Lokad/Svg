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
        (double width, double height) = ResolveOutputDimensions(options.Viewport, root.Width, root.Height, root.ViewBox);
        double scaledWidth = Math.Round(width * options.OutputScale, MidpointRounding.AwayFromZero);
        double scaledHeight = Math.Round(height * options.OutputScale, MidpointRounding.AwayFromZero);
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
                case SvgGeometryKind.RadialGradient:
                    RasterizeRadialFill(geometry.Radial, destination);
                    return;
                case SvgGeometryKind.PolygonFill:
                    RasterizePolygonFill(geometry.Polygon, destination);
                    return;
                case SvgGeometryKind.GradientPolygon:
                    RasterizeGradientPolygon(geometry.GradientPolygon, destination);
                    return;
                case SvgGeometryKind.RadialPolygon:
                    RasterizeRadialPolygon(geometry.RadialPolygon, destination);
                    return;
                case SvgGeometryKind.PatternPolygon:
                    RasterizePatternPolygon(geometry.PatternPolygon, destination);
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

        void RasterizePolygonFill(SvgPolygonFill polygon, byte[] destination)
        {
            if (!polygon.HasFill || polygon.Points.Length < 3) return;
            long mappingWork = polygon.Points.Length;
            if (mappingWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon vertex mapping exceeds the configured limit.");
            workUnits += mappingWork;
            using RenderBudget.BudgetReservation mappedReservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(polygon.Points.Length), SvgMemoryCategory.Surface);
            SvgDevicePoint[] mapped = new SvgDevicePoint[polygon.Points.Length];
            bool hasFractionalEdge = false;
            double left = double.PositiveInfinity;
            double top = double.PositiveInfinity;
            double right = double.NegativeInfinity;
            double bottom = double.NegativeInfinity;
            for (int index = 0; index < polygon.Points.Length; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                double edgeX = polygon.Mapping == SvgPolygonMapping.QuarterEdges ? MapStraightPolygonEdge(polygon.Points[index].X, horizontalScale, horizontalOffset) : MapEdge(polygon.Points[index].X, horizontalScale, horizontalOffset);
                double edgeY = polygon.Mapping == SvgPolygonMapping.QuarterEdges ? MapStraightPolygonEdge(polygon.Points[index].Y, verticalScale, verticalOffset) : MapEdge(polygon.Points[index].Y, verticalScale, verticalOffset);
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
            if (polygon.Clip is SvgClipBox clip)
            {
                (startX, startY, endX, endY) = IntersectMappedClip(clip, horizontalScale, horizontalOffset, verticalScale, verticalOffset, outputWidth, outputHeight, startX, startY, endX, endY);
            }

            if (endY <= startY || endX <= startX) return;
            long coverage = (long)(endY - startY) * (endX - startX) * polygon.Points.Length;
            if (coverage > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill raster work exceeds the configured limit.");
            workUnits += coverage;
            ReadOnlySpan<int> contourEnds = polygon.ContourEnds;
            ReadOnlySpan<int> nestingSigns = polygon.Signs is int[] signs ? signs : Array.Empty<int>();
            for (int y = startY; y < endY; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = startX; x < endX; x++)
                {
                    double pixelCoverage = polygon.IsEvenOdd
                        ? SvgPolygonCoverage.GetEvenOddCoverage(mapped, contourEnds, nestingSigns, x, y, cancellationToken)
                        : polygon.Signs is int[] roles
                            ? SvgPolygonCoverage.GetWindingCoverage(mapped, contourEnds, roles, x, y, cancellationToken)
                            : Math.Clamp(Math.Abs(SvgPolygonCoverage.GetWindingCoverage(mapped, contourEnds, x, y, cancellationToken)), 0, 1);
                    SvgColor color = WithCoverage(polygon.Color, pixelCoverage);
                    int offset = (y * outputWidth + x) * 4;
                    BlendSourceOver(destination.AsSpan(offset, 4), color, hasFractionalEdge);
                }
            }
        }
        void RasterizeGradientPolygon(SvgGradientPolygon outline, byte[] destination)
        {
            if (!outline.HasFill || outline.Points.Length < 3) return;
            long mappingWork = outline.Points.Length;
            if (mappingWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon vertex mapping exceeds the configured limit.");
            workUnits += mappingWork;
            using RenderBudget.BudgetReservation mappedReservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(outline.Points.Length), SvgMemoryCategory.Surface);
            SvgDevicePoint[] mapped = new SvgDevicePoint[outline.Points.Length];
            bool hasFractionalEdge = false;
            double left = double.PositiveInfinity;
            double top = double.PositiveInfinity;
            double right = double.NegativeInfinity;
            double bottom = double.NegativeInfinity;
            for (int index = 0; index < outline.Points.Length; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                double edgeX = outline.Mapping == SvgPolygonMapping.QuarterEdges ? MapStraightPolygonEdge(outline.Points[index].X, horizontalScale, horizontalOffset) : MapEdge(outline.Points[index].X, horizontalScale, horizontalOffset);
                double edgeY = outline.Mapping == SvgPolygonMapping.QuarterEdges ? MapStraightPolygonEdge(outline.Points[index].Y, verticalScale, verticalOffset) : MapEdge(outline.Points[index].Y, verticalScale, verticalOffset);
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
            if (outline.Clip is SvgClipBox clip)
            {
                (startX, startY, endX, endY) = IntersectMappedClip(clip, horizontalScale, horizontalOffset, verticalScale, verticalOffset, outputWidth, outputHeight, startX, startY, endX, endY);
            }
            if (endY <= startY || endX <= startX) return;
            long coverage = (long)(endY - startY) * (endX - startX) * outline.Points.Length;
            if (coverage > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill raster work exceeds the configured limit.");
            workUnits += coverage;
            SvgLinearGradient definition = outline.Gradient;
            SvgRectangle box = outline.Box;
            double directionX = definition.X2 - definition.X1;
            double directionY = definition.Y2 - definition.Y1;
            double denominator = directionX * directionX + directionY * directionY;
            double offsetSpan = definition.Offset2 - definition.Offset1;
            if (offsetSpan <= 0 && definition.Stops.Length != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only two-stop increasing-offset linear gradients are implemented.");
            bool transparentStops = false;
            foreach (SvgGradientStop stop in definition.Stops)
            {
                if (stop.Color.Alpha != 255) { transparentStops = true; break; }
            }
            ReadOnlySpan<int> contourEnds = outline.ContourEnds;
            ReadOnlySpan<int> nestingSigns = outline.Signs is int[] signs ? signs : Array.Empty<int>();
            if (denominator != 0 || definition.Stops.Length == 1)
            for (int y = startY; y < endY; y++)
            {
            cancellationToken.ThrowIfCancellationRequested();
            double deviceY = (y + 0.5 - verticalOffset) / verticalScale;
            for (int x = startX; x < endX; x++)
            {
                double pixelCoverage = outline.IsEvenOdd
                    ? SvgPolygonCoverage.GetEvenOddCoverage(mapped, contourEnds, nestingSigns, x, y, cancellationToken)
                    : outline.Signs is int[] roles
                        ? SvgPolygonCoverage.GetWindingCoverage(mapped, contourEnds, roles, x, y, cancellationToken)
                        : Math.Clamp(Math.Abs(SvgPolygonCoverage.GetWindingCoverage(mapped, contourEnds, x, y, cancellationToken)), 0, 1);
                double deviceX = (x + 0.5 - horizontalOffset) / horizontalScale;
                double position;
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
                double dither = GradientDither[x & 7, y & 7] / 64d;
                SvgColor tileColor = InterpolateGradientRamp(definition.Stops, definition.Start, definition.End, definition.Offset1, definition.Offset2, position, transparentStops, dither, outline.Opacity);
                SvgColor color = WithCoverage(tileColor, pixelCoverage);
                int offset = (y * outputWidth + x) * 4;
                BlendSourceOver(destination.AsSpan(offset, 4), color, hasFractionalEdge);
            }
            }
        }
        void RasterizeRadialPolygon(SvgRadialPolygon outline, byte[] destination)
        {
            if (!outline.HasFill || outline.Points.Length < 3) return;
            long mappingWork = outline.Points.Length;
            if (mappingWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon vertex mapping exceeds the configured limit.");
            workUnits += mappingWork;
            using RenderBudget.BudgetReservation mappedReservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(outline.Points.Length), SvgMemoryCategory.Surface);
            SvgDevicePoint[] mapped = new SvgDevicePoint[outline.Points.Length];
            bool hasFractionalEdge = false;
            double left = double.PositiveInfinity;
            double top = double.PositiveInfinity;
            double right = double.NegativeInfinity;
            double bottom = double.NegativeInfinity;
            for (int index = 0; index < outline.Points.Length; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                double edgeX = outline.Mapping == SvgPolygonMapping.QuarterEdges ? MapStraightPolygonEdge(outline.Points[index].X, horizontalScale, horizontalOffset) : MapEdge(outline.Points[index].X, horizontalScale, horizontalOffset);
                double edgeY = outline.Mapping == SvgPolygonMapping.QuarterEdges ? MapStraightPolygonEdge(outline.Points[index].Y, verticalScale, verticalOffset) : MapEdge(outline.Points[index].Y, verticalScale, verticalOffset);
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
            if (outline.Clip is SvgClipBox clip)
            {
                (startX, startY, endX, endY) = IntersectMappedClip(clip, horizontalScale, horizontalOffset, verticalScale, verticalOffset, outputWidth, outputHeight, startX, startY, endX, endY);
            }
            if (endY <= startY || endX <= startX) return;
            long coverage = (long)(endY - startY) * (endX - startX) * outline.Points.Length;
            if (coverage > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill raster work exceeds the configured limit.");
            workUnits += coverage;
            SvgRadialGradient definition = outline.Gradient;
            SvgRectangle box = outline.Box;
            if (definition.Radius <= 0 && definition.Stops.Length != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive-radius radial gradients are implemented.");
            bool transparentStops = false;
            foreach (SvgGradientStop stop in definition.Stops)
            {
                if (stop.Color.Alpha != 255) { transparentStops = true; break; }
            }
            ReadOnlySpan<int> contourEnds = outline.ContourEnds;
            ReadOnlySpan<int> nestingSigns = outline.Signs is int[] signs ? signs : Array.Empty<int>();
            for (int y = startY; y < endY; y++)
            {
            cancellationToken.ThrowIfCancellationRequested();
            double deviceY = (y + 0.5 - verticalOffset) / verticalScale;
            for (int x = startX; x < endX; x++)
            {
                double pixelCoverage = outline.IsEvenOdd
                    ? SvgPolygonCoverage.GetEvenOddCoverage(mapped, contourEnds, nestingSigns, x, y, cancellationToken)
                    : outline.Signs is int[] roles
                        ? SvgPolygonCoverage.GetWindingCoverage(mapped, contourEnds, roles, x, y, cancellationToken)
                        : Math.Clamp(Math.Abs(SvgPolygonCoverage.GetWindingCoverage(mapped, contourEnds, x, y, cancellationToken)), 0, 1);
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
                    focalX = definition.FocalX;
                    focalY = definition.FocalY;
                    circleX = definition.CenterX;
                    circleY = definition.CenterY;
                }
                else
                {
                    sampleX = (deviceX - box.X) / box.Width;
                    sampleY = (deviceY - box.Y) / box.Height;
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
                double dither = GradientDither[x & 7, y & 7] / 64d;
                SvgColor tileColor = InterpolateGradientRamp(definition.Stops, definition.Start, definition.End, definition.Offset1, definition.Offset2, position, transparentStops, dither, outline.Opacity);
                SvgColor color = WithCoverage(tileColor, pixelCoverage);
                int offset = (y * outputWidth + x) * 4;
                BlendSourceOver(destination.AsSpan(offset, 4), color, hasFractionalEdge);
            }
            }
        }

        void RasterizePatternPolygon(SvgPatternPolygon outline, byte[] destination)
        {
            if (!outline.HasFill || outline.Points.Length < 3) return;
            long mappingWork = outline.Points.Length;
            if (mappingWork > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon vertex mapping exceeds the configured limit.");
            workUnits += mappingWork;
            using RenderBudget.BudgetReservation mappedReservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(outline.Points.Length), SvgMemoryCategory.Surface);
            SvgDevicePoint[] mapped = new SvgDevicePoint[outline.Points.Length];
            bool hasFractionalEdge = false;
            double left = double.PositiveInfinity;
            double top = double.PositiveInfinity;
            double right = double.NegativeInfinity;
            double bottom = double.NegativeInfinity;
            for (int index = 0; index < outline.Points.Length; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                double edgeX = outline.Mapping == SvgPolygonMapping.QuarterEdges ? MapStraightPolygonEdge(outline.Points[index].X, horizontalScale, horizontalOffset) : MapEdge(outline.Points[index].X, horizontalScale, horizontalOffset);
                double edgeY = outline.Mapping == SvgPolygonMapping.QuarterEdges ? MapStraightPolygonEdge(outline.Points[index].Y, verticalScale, verticalOffset) : MapEdge(outline.Points[index].Y, verticalScale, verticalOffset);
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
            if (outline.Clip is SvgClipBox clip)
            {
                (startX, startY, endX, endY) = IntersectMappedClip(clip, horizontalScale, horizontalOffset, verticalScale, verticalOffset, outputWidth, outputHeight, startX, startY, endX, endY);
            }
            if (endY <= startY || endX <= startX) return;
            long coverage = (long)(endY - startY) * (endX - startX) * outline.Points.Length;
            if (coverage > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill raster work exceeds the configured limit.");
            workUnits += coverage;
            SvgPatternGeometry source = new(outline.Content, outline.OriginX, outline.OriginY, outline.TileWidth, outline.TileHeight, default, outline.Opacity, null);
            if (!TryMapPatternTile(source, out SvgPatternGeometry? mappedTile)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills under viewBox or output scaling are not implemented.");
            SvgPatternGeometry tile = mappedTile ?? source;
            long tilePixels = (long)tile.TileWidth * tile.TileHeight;
            if (tilePixels <= 0 || tilePixels > options.Limits.MaximumPixels) throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern tile surface exceeds the configured pixel limit.");
            using RenderBudget.BudgetReservation tileReservation = budget.Reserve(checked(tilePixels * 4), SvgMemoryCategory.Surface);
            byte[] blitted = new byte[(int)tilePixels * 4];
            BlitPatternTile(tile, blitted);
            ReadOnlySpan<int> contourEnds = outline.ContourEnds;
            ReadOnlySpan<int> nestingSigns = outline.Signs is int[] signs ? signs : Array.Empty<int>();
            for (int y = startY; y < endY; y++)
            {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = startX; x < endX; x++)
            {
                double pixelCoverage = outline.IsEvenOdd
                    ? SvgPolygonCoverage.GetEvenOddCoverage(mapped, contourEnds, nestingSigns, x, y, cancellationToken)
                    : outline.Signs is int[] roles
                        ? SvgPolygonCoverage.GetWindingCoverage(mapped, contourEnds, roles, x, y, cancellationToken)
                        : Math.Clamp(Math.Abs(SvgPolygonCoverage.GetWindingCoverage(mapped, contourEnds, x, y, cancellationToken)), 0, 1);
                int tileX = PositiveModulo(x - tile.OriginX, tile.TileWidth);
                int tileY = PositiveModulo(y - tile.OriginY, tile.TileHeight);
                int tileOffset = (tileY * tile.TileWidth + tileX) * 4;
                SvgColor tileColor = new(blitted[tileOffset], blitted[tileOffset + 1], blitted[tileOffset + 2], MultiplyAlpha(blitted[tileOffset + 3], tile.Opacity));
                SvgColor color = WithCoverage(tileColor, pixelCoverage);
                int offset = (y * outputWidth + x) * 4;
                BlendSourceOver(destination.AsSpan(offset, 4), color, hasFractionalEdge);
            }
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
            mapped = new SvgPatternGeometry(new SvgRectangle(contentX, contentY, contentWidth, contentHeight, pattern.Content.Color, pattern.Content.HasFill, null), (int)originX, (int)originY, (int)tileWidth, (int)tileHeight, pattern.Fill, pattern.Opacity, pattern.Stroke);
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
            long blitted = (long)(endY - startY) * (endX - startX);
            if (blitted > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Pattern tile raster work exceeds the configured limit.");
            workUnits += blitted;
            for (int y = startY; y < endY; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = startX; x < endX; x++)
                {
                    if ((x & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
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

        static SvgColor TransparentTileColor(SvgColor first, SvgColor second, double blend, double dither, byte opacity)
        {
            double alpha = ((double)first.Alpha + ((double)second.Alpha - first.Alpha) * blend) * opacity / 255;
            byte mask = (byte)Math.Floor(alpha + 0.5);
            if (mask == 0) return new SvgColor(0, 0, 0, 0);
            double red = ((double)first.Red + ((double)second.Red - first.Red) * blend) * alpha / 255 + dither;
            double green = ((double)first.Green + ((double)second.Green - first.Green) * blend) * alpha / 255 + dither;
            double blue = ((double)first.Blue + ((double)second.Blue - first.Blue) * blend) * alpha / 255 + dither;
            byte premultRed = (byte)Math.Floor(red + 1e-9);
            byte premultGreen = (byte)Math.Floor(green + 1e-9);
            byte premultBlue = (byte)Math.Floor(blue + 1e-9);
            double inverse = 255.0 / mask;
            int r = (int)Math.Floor(premultRed * inverse + 0.5);
            if (r > 255) r = 255;
            int g = (int)Math.Floor(premultGreen * inverse + 0.5);
            if (g > 255) g = 255;
            int b = (int)Math.Floor(premultBlue * inverse + 0.5);
            if (b > 255) b = 255;
            return new SvgColor((byte)r, (byte)g, (byte)b, mask);
        }


        static SvgColor InterpolateGradientRamp(SvgGradientStop[] stops, SvgColor start, SvgColor end, double offset1, double offset2, double position, bool transparentStops, double dither, byte opacity)
        {
            if (stops.Length > 2)
            {
                double offsetSpan = offset2 - offset1;
                double absolute = offset1 + position * offsetSpan;
                int segment = 0;
                while (segment < stops.Length - 2 && absolute >= stops[segment + 1].Offset) segment++;
                SvgGradientStop first = stops[segment];
                SvgGradientStop second = stops[segment + 1];
                // A zero-width segment is a hard edge: the later stop wins at the shared offset, matching the pinned oracle.
                double blend = second.Offset <= first.Offset ? 1 : (absolute - first.Offset) / (second.Offset - first.Offset);
                if (transparentStops)
                    return TransparentTileColor(first.Color, second.Color, blend, dither, opacity);
                return new(InterpolateChannel(first.Color.Red, second.Color.Red, blend, dither), InterpolateChannel(first.Color.Green, second.Color.Green, blend, dither), InterpolateChannel(first.Color.Blue, second.Color.Blue, blend, dither), opacity);
            }
            if (transparentStops)
                return TransparentTileColor(start, end, position, dither, opacity);
            return new(InterpolateChannel(start.Red, end.Red, position, dither), InterpolateChannel(start.Green, end.Green, position, dither), InterpolateChannel(start.Blue, end.Blue, position, dither), opacity);
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
            if (paint && offsetSpan <= 0 && definition.Stops.Length != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only two-stop increasing-offset linear gradients are implemented.");
            bool transparentStops = false;
            foreach (SvgGradientStop stop in definition.Stops)
            {
                if (stop.Color.Alpha != 255) { transparentStops = true; break; }
            }
            if (paint && (denominator != 0 || definition.Stops.Length == 1))
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
                double dither = GradientDither[x & 7, y & 7] / 64d;
                SvgColor tileColor = InterpolateGradientRamp(definition.Stops, definition.Start, definition.End, definition.Offset1, definition.Offset2, position, transparentStops, dither, opacity);
                SvgColor color = WithCoverage(tileColor, horizontalCoverage * verticalCoverage);
                int offset = (y * outputWidth + x) * 4;
                BlendSourceOver(destination.AsSpan(offset, 4), color, hasFractionalEdge);
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
                RasterizeSolidRectangle(new SvgRectangle(radial.Fill.X - halfWidth, radial.Fill.Y - halfWidth, radial.Fill.Width + stroke.Width, stroke.Width, stroke.Color, true, null), destination);
                RasterizeSolidRectangle(new SvgRectangle(radial.Fill.X - halfWidth, radial.Fill.Y + radial.Fill.Height - halfWidth, radial.Fill.Width + stroke.Width, stroke.Width, stroke.Color, true, null), destination);
                RasterizeSolidRectangle(new SvgRectangle(radial.Fill.X - halfWidth, radial.Fill.Y + halfWidth, stroke.Width, radial.Fill.Height - stroke.Width, stroke.Color, true, null), destination);
                RasterizeSolidRectangle(new SvgRectangle(radial.Fill.X + radial.Fill.Width - halfWidth, radial.Fill.Y + halfWidth, stroke.Width, radial.Fill.Height - stroke.Width, stroke.Color, true, null), destination);
            }
        }

        void RasterizeRadialArea(SvgRadialGradient definition, SvgRectangle box, SvgRectangle area, byte opacity, bool paint, byte[] destination)
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
            if (coverage > options.Limits.MaximumWorkUnits - workUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "Radial fill raster work exceeds the configured limit.");
            workUnits += coverage;
            if (paint && (definition.Radius <= 0 && definition.Stops.Length != 1)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive-radius radial gradients are implemented.");
            bool transparentStops = false;
            foreach (SvgGradientStop stop in definition.Stops)
            {
                if (stop.Color.Alpha != 255) { transparentStops = true; break; }
            }
            if (paint)
            for (int y = startY; y < endY; y++)
            {
            cancellationToken.ThrowIfCancellationRequested();
            double deviceY = (y + 0.5 - verticalOffset) / verticalScale;
            for (int x = startX; x < endX; x++)
            {
                double verticalCoverage = Math.Clamp(Math.Min(areaBottom, y + 1) - Math.Max(areaTop, y), 0, 1);
                double horizontalCoverage = Math.Clamp(Math.Min(areaRight, x + 1) - Math.Max(areaLeft, x), 0, 1);
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
                    focalX = definition.FocalX;
                    focalY = definition.FocalY;
                    circleX = definition.CenterX;
                    circleY = definition.CenterY;
                }
                else
                {
                    sampleX = (deviceX - box.X) / box.Width;
                    sampleY = (deviceY - box.Y) / box.Height;
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
                double dither = GradientDither[x & 7, y & 7] / 64d;
                SvgColor tileColor = InterpolateGradientRamp(definition.Stops, definition.Start, definition.End, definition.Offset1, definition.Offset2, position, transparentStops, dither, opacity);
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

        static double MapStraightPolygonEdge(double value, double scale, double offset)
        {
            double edge = MapEdge(value, scale, offset);
            if (Math.Abs(edge * 4 - Math.Round(edge * 4, MidpointRounding.AwayFromZero)) > 0.000000001) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Polygon viewBox mapping must preserve quarter-pixel output edges.");
            return edge;
        }

        static int ClampFloor(double value, int maximum) => value <= 0 ? 0 : value >= maximum ? maximum : checked((int)Math.Floor(value));

        static (int StartX, int StartY, int EndX, int EndY) IntersectMappedClip(SvgClipBox clip, double horizontalScale, double horizontalOffset, double verticalScale, double verticalOffset, int outputWidth, int outputHeight, int startX, int startY, int endX, int endY)
        {
            double left = MapEdge(clip.X, horizontalScale, horizontalOffset);
            double top = MapEdge(clip.Y, verticalScale, verticalOffset);
            double right = MapEdge(clip.Right, horizontalScale, horizontalOffset);
            double bottom = MapEdge(clip.Bottom, verticalScale, verticalOffset);
            return (
                Math.Max(startX, ClampFloor(left, outputWidth)),
                Math.Max(startY, ClampFloor(top, outputHeight)),
                Math.Min(endX, ClampCeiling(right, outputWidth)),
                Math.Min(endY, ClampCeiling(bottom, outputHeight)));
        }

        static int ClampCeiling(double value, int maximum) => value <= 0 ? 0 : value >= maximum ? maximum : checked((int)Math.Ceiling(value));

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

    /// <summary>Resolves output dimensions from an explicit viewport or root dimensions with viewBox aspect handling.</summary>
    private static (double Width, double Height) ResolveOutputDimensions(SvgViewport? viewport, double? rootWidth, double? rootHeight, SvgViewBox? viewBox)
        => viewport is SvgViewport explicitViewport
            ? (explicitViewport.Width, explicitViewport.Height)
            : (rootWidth, rootHeight) switch
            {
                (double explicitWidth, double explicitHeight) => (explicitWidth, explicitHeight),
                (null, null) => (300, 150),
                (double explicitWidth, null) when viewBox is SvgViewBox widthBox => (explicitWidth, explicitWidth * widthBox.Height / widthBox.Width),
                (null, double explicitHeight) when viewBox is SvgViewBox heightBox => (explicitHeight * heightBox.Width / heightBox.Height, explicitHeight),
                _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Both root dimensions or neither root dimension are required currently.")
            };

    private static (double HorizontalScale, double HorizontalOffset, double VerticalScale, double VerticalOffset) GetViewBoxTransform(SvgViewBox? viewBox, double width, double height, double outputScale)
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

    /// <summary>Probes root dimensions and the output transform without lowering, falling back to unit scales when the root cannot be resolved.</summary>
    internal static (double HorizontalScale, double VerticalScale) TryResolveOutputScales(ReadOnlyMemory<byte> svg, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
    {
        try
        {
            using RenderBudget.BudgetReservation workspace = budget.Reserve(SvgRootParser.GetXmlReaderWorkspaceBytes(svg.Length, options.Limits.MaximumAttributesPerElement), SvgMemoryCategory.Parser);
            XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = options.Limits.MaximumInputBytes, IgnoreComments = true, IgnoreProcessingInstructions = true };
            using ReadOnlyMemoryStream stream = new(svg);
            using XmlReader reader = XmlReader.Create(stream, settings);
            ParserWorkCounter probe = new(options.Limits.MaximumWorkUnits);
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
        try { return SvgRootParser.Parse(svg, options.Limits, budget, options.FontSnapshot, counter, cancellationToken, outputScaleX, outputScaleY); }
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
        return (byte)Math.Min(sourceContribution + retainedContribution, 255);
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
