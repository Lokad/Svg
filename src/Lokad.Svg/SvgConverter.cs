namespace Lokad.Svg;

using Lokad.Svg.Parsing;
using Lokad.Svg.Encoding;
using Lokad.Svg.Rendering;
using System.Xml;

/// <summary>Converts static SVG bytes to PNG bytes.</summary>
public static class SvgConverter
{
    /// <summary>Converts SVG bytes to an independently owned PNG result.</summary>
    public static ReadOnlyMemory<byte> ConvertToPng(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        ValidateInvocation(svg, options, cancellationToken);
        RenderBudget budget = new(options.Limits.MaximumAccountedBytes);
        SvgRoot root = ParseRoot(svg, options, cancellationToken);
        if (root.HasUnsupportedChild) throw new SvgException(SvgErrorCode.UnsupportedFeature, "This SVG child element is not implemented yet.");
        using RasterizedSurface surface = Rasterize(root, options, budget, cancellationToken);
        return PngEncoder.Encode(surface.Width, surface.Height, surface.Pixels, budget, options.Limits.MaximumEncodedPngBytes, cancellationToken);
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
        RenderBudget budget = new(options.Limits.MaximumAccountedBytes);
        SvgRoot root = ParseRoot(svg, options, cancellationToken);
        if (root.HasUnsupportedChild) throw new SvgException(SvgErrorCode.UnsupportedFeature, "This SVG child element is not implemented yet.");
        using RasterizedSurface surface = Rasterize(root, options, budget, cancellationToken);
        return PngEncoder.EncodeInto(surface.Width, surface.Height, surface.Pixels, destination, budget, options.Limits.MaximumEncodedPngBytes, cancellationToken);
    }

    /// <summary>Schedules one destination conversion on the thread pool.</summary>
    public static Task<int> ConvertToPngAsync(ReadOnlyMemory<byte> svg, Memory<byte> destination, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        return Task.Run(() => ConvertToPng(svg, destination, options, cancellationToken), cancellationToken);
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

    private static RasterizedSurface Rasterize(SvgRoot root, SvgRenderOptions options, RenderBudget budget, CancellationToken cancellationToken)
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
        if (scaledWidth != Math.Truncate(scaledWidth) || scaledHeight != Math.Truncate(scaledHeight) || scaledWidth > options.Limits.MaximumDimension || scaledHeight > options.Limits.MaximumDimension) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integral output dimensions within configured limits are implemented currently.");
        int outputWidth = (int)scaledWidth;
        int outputHeight = (int)scaledHeight;
        long pixelCountLong = (long)outputWidth * outputHeight;
        if (pixelCountLong > options.Limits.MaximumPixels || pixelCountLong > int.MaxValue / 4) throw new SvgException(SvgErrorCode.LimitExceeded, "Output pixel count exceeds the configured contiguous RGBA surface limit.");
        int pixelCount = (int)pixelCountLong;
        int surfaceByteCount = pixelCount * 4;
        RenderBudget.BudgetReservation surface = budget.Reserve(surfaceByteCount, SvgMemoryCategory.Surface);
        byte[] pixels = new byte[surfaceByteCount];
        if (options.BackgroundColor != SvgColor.Transparent)
        {
            for (int pixel = 0; pixel < pixelCount; pixel++)
            {
                int offset = pixel * 4;
                pixels[offset] = options.BackgroundColor.Red;
                pixels[offset + 1] = options.BackgroundColor.Green;
                pixels[offset + 2] = options.BackgroundColor.Blue;
                pixels[offset + 3] = options.BackgroundColor.Alpha;
            }
        }
        (double horizontalScale, double horizontalOffset, double verticalScale, double verticalOffset) = GetViewBoxTransform(root.ViewBox, width, height, options.OutputScale);
        long workUnits = 0;
        int commandIndex = RenderCommands(0, pixels, false);
        if (commandIndex != root.Commands.Length) throw new SvgException(SvgErrorCode.MalformedInput, "SVG group structure is unbalanced.");
        return new RasterizedSurface(outputWidth, outputHeight, pixels, surface);

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
                RasterizeRectangle(command.Rectangle, destination);
            }
            if (stopsAtGroupEnd) throw new SvgException(SvgErrorCode.MalformedInput, "SVG group structure is unbalanced.");
            return index;
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
            double rectangleY = MapEdge(rectangle.Y, verticalScale, verticalOffset);
            double rectangleBottom = MapEdge(rectangle.Y + rectangle.Height, verticalScale, verticalOffset);
            double rectangleX = MapEdge(rectangle.X, horizontalScale, horizontalOffset);
            double rectangleRight = MapEdge(rectangle.X + rectangle.Width, horizontalScale, horizontalOffset);
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
            if (!double.IsFinite(scaled)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Output scaling and viewBox mapping must preserve finite rectangle edges.");
            if (Math.Abs(scaled * 4 - Math.Round(scaled * 4, MidpointRounding.AwayFromZero)) > 0.000000001) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Output scaling and viewBox mapping must preserve quarter-pixel rectangle edges.");
            return scaled;
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

    private static SvgRoot ParseRoot(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        try { return SvgRootParser.Parse(svg, options.Limits, cancellationToken); }
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

    private readonly record struct RasterizedSurface(int Width, int Height, byte[] Pixels, RenderBudget.BudgetReservation Reservation) : IDisposable
    {
        public void Dispose() => Reservation.Dispose();
    }
}
