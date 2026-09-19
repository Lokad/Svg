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
        SvgRoot root = ParseRoot(svg, options, cancellationToken);
        if (root.HasUnsupportedChild) throw new SvgException(SvgErrorCode.UnsupportedFeature, "This SVG child element is not implemented yet.");
        RasterizedSurface surface = Rasterize(root, options, cancellationToken);
        return PngEncoder.Encode(surface.Width, surface.Height, surface.Pixels, cancellationToken);
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
        SvgRoot root = ParseRoot(svg, options, cancellationToken);
        if (root.HasUnsupportedChild) throw new SvgException(SvgErrorCode.UnsupportedFeature, "This SVG child element is not implemented yet.");
        RasterizedSurface surface = Rasterize(root, options, cancellationToken);
        return PngEncoder.EncodeInto(surface.Width, surface.Height, surface.Pixels, destination, cancellationToken);
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

    private static RasterizedSurface Rasterize(SvgRoot root, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        if (root.Width is not double width || root.Height is not double height) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Explicit root width and height are required currently.");
        double scaledWidth = width * options.OutputScale;
        double scaledHeight = height * options.OutputScale;
        if (scaledWidth != Math.Truncate(scaledWidth) || scaledHeight != Math.Truncate(scaledHeight) || scaledWidth > options.Limits.MaximumDimension || scaledHeight > options.Limits.MaximumDimension) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integral output dimensions within configured limits are implemented currently.");
        int outputWidth = checked((int)scaledWidth);
        int outputHeight = checked((int)scaledHeight);
        int pixelCount = checked(outputWidth * outputHeight);
        if (pixelCount > options.Limits.MaximumPixels) throw new SvgException(SvgErrorCode.LimitExceeded, "Output pixel count exceeds the configured limit.");
        RenderBudget budget = new(options.Limits.MaximumAccountedBytes);
        using RenderBudget.BudgetReservation surface = budget.Reserve(checked((long)pixelCount * 4), SvgMemoryCategory.Surface);
        byte[] pixels = new byte[checked(pixelCount * 4)];
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
        foreach (SvgRectangle rectangle in root.Rectangles)
        {
            if (!rectangle.HasFill) continue;
            int startY = (int)Math.Max(0L, rectangle.Y);
            int endY = (int)Math.Clamp((long)rectangle.Y + rectangle.Height, 0L, outputHeight);
            int startX = (int)Math.Max(0L, rectangle.X);
            int endX = (int)Math.Clamp((long)rectangle.X + rectangle.Width, 0L, outputWidth);
            for (int y = startY; y < endY; y++)
            {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = startX; x < endX; x++)
            {
                int offset = (y * outputWidth + x) * 4;
                BlendSourceOver(pixels.AsSpan(offset, 4), rectangle.Color);
            }
            }
        }
        return new RasterizedSurface(outputWidth, outputHeight, pixels);
    }

    private static SvgRoot ParseRoot(ReadOnlyMemory<byte> svg, SvgRenderOptions options, CancellationToken cancellationToken)
    {
        try { return SvgRootParser.Parse(svg, options.Limits, cancellationToken); }
        catch (XmlException exception) { throw new SvgException(SvgErrorCode.MalformedInput, "SVG XML is malformed: " + exception.Message); }
    }

    private static void BlendSourceOver(Span<byte> destination, SvgColor source)
    {
        if (source.Alpha == 255) { destination[0] = source.Red; destination[1] = source.Green; destination[2] = source.Blue; destination[3] = 255; return; }
        if (source.Alpha == 0) return;
        int sourceAlpha = source.Alpha;
        int destinationAlpha = destination[3];
        int inverseSourceAlpha = 255 - sourceAlpha;
        // Chrome's 8-bit source-over path truncates the retained destination alpha.
        int outputAlpha = sourceAlpha + ((destinationAlpha * inverseSourceAlpha) / 255);
        destination[0] = (byte)((source.Red * sourceAlpha + ((destination[0] * destinationAlpha * inverseSourceAlpha + 127) / 255)) / outputAlpha);
        destination[1] = (byte)((source.Green * sourceAlpha + ((destination[1] * destinationAlpha * inverseSourceAlpha + 127) / 255)) / outputAlpha);
        destination[2] = (byte)((source.Blue * sourceAlpha + ((destination[2] * destinationAlpha * inverseSourceAlpha + 127) / 255)) / outputAlpha);
        destination[3] = (byte)outputAlpha;
    }

    private readonly record struct RasterizedSurface(int Width, int Height, byte[] Pixels);
}
