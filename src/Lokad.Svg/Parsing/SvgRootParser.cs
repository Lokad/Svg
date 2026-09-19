using System.Globalization;
using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Reads the bounded SVG root without materializing a DOM or input string.</summary>
internal static class SvgRootParser
{
    internal static SvgRoot Parse(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, CancellationToken cancellationToken)
    {
        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = limits.MaximumInputBytes, IgnoreComments = true, IgnoreProcessingInstructions = true };
        using ReadOnlyMemoryStream stream = new(svg);
        using XmlReader reader = XmlReader.Create(stream, settings);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (reader.LocalName != "svg" || reader.NamespaceURI != "http://www.w3.org/2000/svg") throw new SvgException(SvgErrorCode.MalformedInput, "The document root must be an SVG element in the SVG namespace.");
            double? width = ParseLength(reader.GetAttribute("width"));
            double? height = ParseLength(reader.GetAttribute("height"));
            if (reader.IsEmptyElement) return new SvgRoot(width, height, null, false);
            int rootDepth = reader.Depth;
            SvgRectangle? rectangle = null;
            bool unsupported = false;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.Element && reader.Depth == rootDepth + 1)
                {
                    if (reader.LocalName == "rect" && rectangle is null) rectangle = ParseRectangle(reader);
                    else unsupported = true;
                }
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == rootDepth) return new SvgRoot(width, height, rectangle, unsupported);
            }
            throw new SvgException(SvgErrorCode.MalformedInput, "SVG root element is not closed.");
        }
        throw new SvgException(SvgErrorCode.MalformedInput, "SVG input has no root element.");
    }

    private static SvgRectangle ParseRectangle(XmlReader reader)
    {
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.NamespaceURI.Length == 0 && reader.LocalName is "x" or "y" or "width" or "height" or "fill") continue;
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle attribute '" + reader.Name + "' is not implemented.");
            }
            reader.MoveToElement();
        }
        int x = ParseInteger(reader.GetAttribute("x") ?? "0");
        int y = ParseInteger(reader.GetAttribute("y") ?? "0");
        int width = ParseInteger(reader.GetAttribute("width") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Rectangle width is required."));
        int height = ParseInteger(reader.GetAttribute("height") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Rectangle height is required."));
        string fill = reader.GetAttribute("fill") ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle fill is required currently.");
        if (width < 0 || height < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Rectangle dimensions cannot be negative.");
        if (fill.Length != 7 || fill[0] != '#' || !byte.TryParse(fill.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red) || !byte.TryParse(fill.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green) || !byte.TryParse(fill.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only #RRGGBB rectangle fills are implemented.");
        return new SvgRectangle(x, y, width, height, new SvgColor(red, green, blue, 255));
    }

    private static int ParseInteger(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer rectangle coordinates are implemented.");
        return result;
    }

    private static double? ParseLength(string? value)
    {
        if (value is null) return null;
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase)) token = token[..^2].TrimEnd();
        else if (token.IndexOfAny("%emivc".AsSpan()) >= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number and px root dimensions are implemented.");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result) || result <= 0) throw new SvgException(SvgErrorCode.MalformedInput, "Root dimensions must be finite positive CSS pixel lengths.");
        return result;
    }
}
