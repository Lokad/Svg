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
        ValidateInvocation(svg, options, cancellationToken);
        RenderBudget budget = new(options.Limits.MaximumAccountedBytes);
        using SvgRoot root = ParseRoot(svg, options, budget, cancellationToken);
        if (root.HasUnsupportedChild) throw new SvgException(SvgErrorCode.UnsupportedFeature, "This SVG child element is not implemented yet.");
        using RasterizedSurface surface = Rasterize(root, options, budget, cancellationToken);
        return PngEncoder.Encode(surface.Width, surface.Height, surface.Pixels, budget, options.Limits.MaximumEncodedPngBytes, surface.WorkUnits, options.Limits.MaximumWorkUnits, cancellationToken);
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
        RenderBudget budget = new(options.Limits.MaximumAccountedBytes);
        using SvgRoot root = ParseRoot(svg, options, budget, cancellationToken);
        if (root.HasUnsupportedChild) throw new SvgException(SvgErrorCode.UnsupportedFeature, "This SVG child element is not implemented yet.");
        using RasterizedSurface surface = Rasterize(root, options, budget, cancellationToken);
        return PngEncoder.EncodeInto(surface.Width, surface.Height, surface.Pixels, destination, budget, options.Limits.MaximumEncodedPngBytes, surface.WorkUnits, options.Limits.MaximumWorkUnits, cancellationToken);
    }

    /// <summary>Converts SVG bytes to caller-owned writable stream output.</summary>
    public static int ConvertToPng(ReadOnlyMemory<byte> svg, Stream destination, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("PNG destination must be writable.", nameof(destination));
        ValidateInvocation(svg, options, cancellationToken);
        RenderBudget budget = new(options.Limits.MaximumAccountedBytes);
        using SvgRoot root = ParseRoot(svg, options, budget, cancellationToken);
        if (root.HasUnsupportedChild) throw new SvgException(SvgErrorCode.UnsupportedFeature, "This SVG child element is not implemented yet.");
        using RasterizedSurface surface = Rasterize(root, options, budget, cancellationToken);
        return PngEncoder.EncodeTo(surface.Width, surface.Height, surface.Pixels, destination, budget, options.Limits.MaximumEncodedPngBytes, surface.WorkUnits, options.Limits.MaximumWorkUnits, cancellationToken);
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
            ValidateInvocation(svg, options, cancellationToken);
            RenderBudget budget = new(options.Limits.MaximumAccountedBytes);
            using SvgRoot root = ParseRoot(svg, options, budget, cancellationToken);
            if (root.HasUnsupportedChild) throw new SvgException(SvgErrorCode.UnsupportedFeature, "This SVG child element is not implemented yet.");
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
                    RasterizeGlyphContours(geometry.Glyph ?? throw new SvgException(SvgErrorCode.MalformedInput, "SVG glyph geometry is missing its contours."), destination);
                    return;
                default:
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "SVG scene geometry is not implemented.");
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
            for (int y = startY; y < endY; y++)
            for (int x = startX; x < endX; x++)
            {
                byte alpha = SvgGlyphCoverage.GetCoverage(glyph.Contours, (x - horizontalOffset) / horizontalScale, (y - verticalOffset) / verticalScale, 1 / horizontalScale, 1 / verticalScale, GlyphSamplesPerAxis, cancellationToken);
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
            if (!double.IsFinite(scaled)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Output scaling and viewBox mapping must preserve finite edges.");
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

    private static SvgRoot ParseRoot(ReadOnlyMemory<byte> svg, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
    {
        try { return SvgRootParser.Parse(svg, options.Limits, budget, options.FontSnapshot, cancellationToken); }
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
            destination[0] = (byte)((source.Red * sourceAlpha + ((destination[0] * destinationAlpha * inverseSourceAlpha + 127) / 255)) / outputAlpha);
            destination[1] = (byte)((source.Green * sourceAlpha + ((destination[1] * destinationAlpha * inverseSourceAlpha + 127) / 255)) / outputAlpha);
            destination[2] = (byte)((source.Blue * sourceAlpha + ((destination[2] * destinationAlpha * inverseSourceAlpha + 127) / 255)) / outputAlpha);
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
}
