using System.Xml;
using Lokad.Svg.Encoding;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Parses the bounded embedded-image reference syntax before PNG decoding.</summary>
internal readonly record struct SvgImageSyntax(string Href, double X, double Y, double? Width, double? Height)
{
    internal static SvgImageSyntax Parse(XmlReader reader, double? baseWidth, double? baseHeight)
    {
        string href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink") ?? throw new SvgException(SvgErrorCode.MalformedInput, "An image element requires an href.");
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
    internal static SvgRectangle MapPlacement(string? preserveAspectRatio, SvgImageSyntax syntax, DecodedSolidImage image, double width, double height)
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
        RequireIntegralEdges(left, top, right, bottom);
        return new(left, top, right - left, bottom - top, default, true, null);
    }

    private static void RequireIntegralEdges(double left, double top, double right, double bottom)
    {
        if (Math.Abs(left - Math.Round(left, MidpointRounding.AwayFromZero)) > 0.000000001) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Image aspect mapping must preserve integral output edges.");
        if (Math.Abs(top - Math.Round(top, MidpointRounding.AwayFromZero)) > 0.000000001) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Image aspect mapping must preserve integral output edges.");
        if (Math.Abs(right - Math.Round(right, MidpointRounding.AwayFromZero)) > 0.000000001) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Image aspect mapping must preserve integral output edges.");
        if (Math.Abs(bottom - Math.Round(bottom, MidpointRounding.AwayFromZero)) > 0.000000001) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Image aspect mapping must preserve integral output edges.");
    }

    /// <summary>Retains the full pixel map separately from the meet/slice drawable box.</summary>
    internal static (SvgRectangle Box, SvgShaderMatrix Local) MapRasterPlacement(string? preserveAspectRatio,
        SvgImageSyntax syntax, int pixelWidth, int pixelHeight, double width, double height)
    {
        (SvgViewBoxAlignment horizontal, SvgViewBoxAlignment vertical, SvgViewBoxFit fit) = ParsePlacementAspectRatio(preserveAspectRatio);
        (double sx, double sy, double tx, double ty) = SvgGeometrySyntax.MapViewBox(
            new SvgViewBox(0, 0, pixelWidth, pixelHeight, horizontal, vertical, fit), width, height);
        double left = syntax.X + tx, top = syntax.Y + ty;
        double right = left + pixelWidth * sx, bottom = top + pixelHeight * sy;
        SvgShaderMatrix local = new((float)sx, 0, (float)left, 0, (float)sy, (float)top);
        if (fit == SvgViewBoxFit.Slice)
        {
            left = Math.Max(left, syntax.X); top = Math.Max(top, syntax.Y);
            right = Math.Min(right, syntax.X + width); bottom = Math.Min(bottom, syntax.Y + height);
        }
        return (new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), default, true, null), local);
    }
}
