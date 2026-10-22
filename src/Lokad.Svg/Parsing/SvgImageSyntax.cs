using System.Xml;
using Lokad.Svg.Encoding;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Parses the bounded embedded-image reference syntax before PNG decoding.</summary>
internal readonly record struct SvgImageSyntax(string Href, double X, double Y, double? Width, double? Height)
{
    internal static SvgImageSyntax Parse(XmlReader reader, double? baseWidth, double? baseHeight)
    {
        string href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink") ?? string.Empty;
        double x = ParsePosition(reader.GetAttribute("x"), baseWidth);
        double y = ParsePosition(reader.GetAttribute("y"), baseHeight);
        double? width = ParseDimension(reader.GetAttribute("width"), baseWidth);
        double? height = ParseDimension(reader.GetAttribute("height"), baseHeight);
        return new SvgImageSyntax(href, x, y, width, height);
    }

    private static double? ParseDimension(string? value, double? baseValue)
    {
        if (value is null) return null;
        double parsed = SvgGeometrySyntax.ParseViewportDimension(value, baseValue);
        if (parsed < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Image dimensions cannot be negative.");
        return parsed;
    }

    private static double ParsePosition(string? value, double? baseValue)
    {
        if (value is null) return 0;
        return SvgGeometrySyntax.ParseViewportDimension(value, baseValue);
    }

    /// <summary>Parses the admitted image placement aspect policy, defaulting to centered meet.</summary>
    internal static (SvgViewBoxAlignment HorizontalAlignment, SvgViewBoxAlignment VerticalAlignment, SvgViewBoxFit Fit) ParsePlacementAspectRatio(string? value)
    {
        if (value is not null && value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "preserveAspectRatio values exceed the configured syntax limit.");
        return SvgGeometrySyntax.ParsePreserveAspectRatio(value);
    }

    /// <summary>Maps one solid image into its placement box under the effective aspect policy.</summary>
    internal static SvgRectangle MapPlacement(string? preserveAspectRatio, SvgImageSyntax syntax, DecodedPngImage image, double width, double height)
    {
        if (image.Width * height == image.Height * width) return new(syntax.X, syntax.Y, width, height, default, true, null);
        (SvgViewBoxAlignment horizontalAlignment, SvgViewBoxAlignment verticalAlignment, SvgViewBoxFit fit) = ParsePlacementAspectRatio(preserveAspectRatio);
        if (fit == SvgViewBoxFit.None) return new(syntax.X, syntax.Y, width, height, default, true, null);
        (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(new SvgViewBox(0, 0, image.Width, image.Height, horizontalAlignment, verticalAlignment, fit), width, height);
        double left = syntax.X + offsetX;
        double top = syntax.Y + offsetY;
        double right = left + image.Width * scaleX;
        double bottom = top + image.Height * scaleY;
        if (fit == SvgViewBoxFit.Slice)
        {
            left = Math.Max(left, syntax.X);
            top = Math.Max(top, syntax.Y);
            right = Math.Min(right, syntax.X + width);
            bottom = Math.Min(bottom, syntax.Y + height);
        }
        return new(left, top, right - left, bottom - top, default, true, null);
    }

    /// <summary>Retains the full pixel map separately from the meet/slice drawable box.</summary>
    internal static (SvgRectangle Box, SvgShaderMatrix Local) MapRasterPlacement(string? preserveAspectRatio,
        SvgImageSyntax syntax, int pixelWidth, int pixelHeight, double width, double height)
    {
        (SvgViewBoxAlignment horizontal, SvgViewBoxAlignment vertical, SvgViewBoxFit fit) = ParsePlacementAspectRatio(preserveAspectRatio);
        // Image painting maps a float source rectangle into a float destination.
        // Slice crops the source before matrix construction; an extended destination
        // is mathematically equivalent but changes bitmap sample-boundary rounding.
        float left = (float)syntax.X, top = (float)syntax.Y;
        float destinationWidth = (float)width, destinationHeight = (float)height;
        float sourceWidth = pixelWidth, sourceHeight = pixelHeight;
        float sourceLeft = 0, sourceTop = 0;
        float ratio = (float)pixelHeight / pixelWidth;
        if (fit == SvgViewBoxFit.Meet)
        {
            if (destinationHeight > destinationWidth * ratio)
            {
                destinationHeight = destinationWidth * ratio;
                top = Align(top, (float)height, destinationHeight, vertical);
            }
            if (destinationWidth > (float)height / ratio)
            {
                destinationWidth = (float)height / ratio;
                left = Align(left, (float)width, destinationWidth, horizontal);
            }
        }
        else if (fit == SvgViewBoxFit.Slice)
        {
            if (destinationHeight < destinationWidth * ratio)
            {
                sourceHeight = destinationHeight * ((float)pixelWidth / destinationWidth);
                sourceTop = Align(0, pixelHeight, sourceHeight, vertical);
            }
            if (destinationWidth < destinationHeight / ratio)
            {
                sourceWidth = destinationWidth * ((float)pixelHeight / destinationHeight);
                sourceLeft = Align(0, pixelWidth, sourceWidth, horizontal);
            }
        }
        float sx = (left + destinationWidth - left) / (sourceLeft + sourceWidth - sourceLeft);
        float sy = (top + destinationHeight - top) / (sourceTop + sourceHeight - sourceTop);
        SvgShaderMatrix local = new(sx, 0, left - sourceLeft * sx, 0, sy, top - sourceTop * sy);
        return (new(left, top, Math.Max(0, destinationWidth), Math.Max(0, destinationHeight), default, true, null), local);

        static float Align(float origin, float available, float used, SvgViewBoxAlignment alignment) => alignment switch
        {
            SvgViewBoxAlignment.Mid => origin + available / 2 - used / 2,
            SvgViewBoxAlignment.Max => origin + available - used,
            _ => origin,
        };
    }
}
