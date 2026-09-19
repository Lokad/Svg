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
            for (int y = Math.Max(0, rectangle.Y); y < Math.Min(outputHeight, checked(rectangle.Y + rectangle.Height)); y++)
            {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = Math.Max(0, rectangle.X); x < Math.Min(outputWidth, checked(rectangle.X + rectangle.Width)); x++)
            {
                int offset = (y * outputWidth + x) * 4;
                pixels[offset] = rectangle.Color.Red; pixels[offset + 1] = rectangle.Color.Green; pixels[offset + 2] = rectangle.Color.Blue; pixels[offset + 3] = 255;
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

    private readonly record struct RasterizedSurface(int Width, int Height, byte[] Pixels);
}
