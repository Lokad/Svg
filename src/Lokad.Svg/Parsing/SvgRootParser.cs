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
            SvgPaint rootPaint = ParseRootPaint(reader);
            bool rootVisible = ParseDisplay(reader.GetAttribute("display"));
            double? width = ParseLength(reader.GetAttribute("width"));
            double? height = ParseLength(reader.GetAttribute("height"));
            if (reader.IsEmptyElement) return new SvgRoot(width, height, [], false);
            int rootDepth = reader.Depth;
            List<SvgRectangle> rectangles = [];
            List<GroupScope> groupDepths = [];
            int nodeCount = 0;
            bool unsupported = false;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.Element)
                {
                    bool directChild = reader.Depth == rootDepth + 1;
                    bool nestedInGroup = groupDepths.Count > 0;
                    if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "rect")
                    {
                        if (nodeCount >= limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                        nodeCount++;
                        rectangles.Add(ParseRectangle(reader, groupDepths.Count == 0 ? rootPaint : groupDepths[^1].Paint, groupDepths.Count == 0 ? rootVisible : groupDepths[^1].IsVisible));
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "g")
                    {
                        if (nodeCount >= limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                        nodeCount++;
                        SvgPaint paint = ParseGroupPaint(reader, groupDepths.Count == 0 ? rootPaint : groupDepths[^1].Paint);
                        bool visible = (groupDepths.Count == 0 ? rootVisible : groupDepths[^1].IsVisible) && ParseDisplay(reader.GetAttribute("display"));
                        if (!reader.IsEmptyElement) groupDepths.Add(new GroupScope(reader.Depth, paint, visible));
                    }
                    else if (directChild && (reader.LocalName is "title" or "desc" or "metadata")) reader.Skip();
                    else if (directChild || nestedInGroup) unsupported = true;
                }
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (groupDepths.Count > 0 && groupDepths[^1].Depth == reader.Depth) groupDepths.RemoveAt(groupDepths.Count - 1);
                    if (reader.Depth == rootDepth) return new SvgRoot(width, height, rectangles.ToArray(), unsupported);
                }
            }
            throw new SvgException(SvgErrorCode.MalformedInput, "SVG root element is not closed.");
        }
        throw new SvgException(SvgErrorCode.MalformedInput, "SVG input has no root element.");
    }

    private static SvgPaint ParseGroupPaint(XmlReader reader, SvgPaint inheritedPaint)
    {
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns" || reader.Name == "xmlns") continue;
                if (reader.NamespaceURI.Length == 0 && reader.LocalName is "fill" or "fill-opacity" or "display") continue;
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Group attribute '" + reader.Name + "' is not implemented.");
            }
            reader.MoveToElement();
        }
        return ParsePaint(reader.GetAttribute("fill"), reader.GetAttribute("fill-opacity"), inheritedPaint);
    }

    private static SvgPaint ParseRootPaint(XmlReader reader)
    {
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns" || reader.Name == "xmlns") continue;
                if (reader.NamespaceURI.Length == 0 && reader.LocalName is "width" or "height" or "fill" or "fill-opacity" or "display") continue;
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "SVG root attribute '" + reader.Name + "' is not implemented.");
            }
            reader.MoveToElement();
        }
        return ParsePaint(reader.GetAttribute("fill"), reader.GetAttribute("fill-opacity"), SvgPaint.Default);
    }

    private static SvgRectangle ParseRectangle(XmlReader reader, SvgPaint inheritedPaint, bool inheritedVisible)
    {
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.NamespaceURI.Length == 0 && reader.LocalName is "x" or "y" or "width" or "height" or "fill" or "fill-opacity" or "display") continue;
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle attribute '" + reader.Name + "' is not implemented.");
            }
            reader.MoveToElement();
        }
        int x = ParseInteger(reader.GetAttribute("x") ?? "0");
        int y = ParseInteger(reader.GetAttribute("y") ?? "0");
        int width = ParseInteger(reader.GetAttribute("width") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Rectangle width is required."));
        int height = ParseInteger(reader.GetAttribute("height") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Rectangle height is required."));
        if (width < 0 || height < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Rectangle dimensions cannot be negative.");
        SvgPaint paint = ParsePaint(reader.GetAttribute("fill"), reader.GetAttribute("fill-opacity"), inheritedPaint);
        return new SvgRectangle(x, y, width, height, paint.Color, paint.HasFill && inheritedVisible && ParseDisplay(reader.GetAttribute("display")));
    }

    private static SvgPaint ParsePaint(string? fill, string? opacity, SvgPaint inheritedPaint)
    {
        bool hasFill = fill is null ? inheritedPaint.HasFill : fill is not "none" and not "transparent";
        SvgColor color = fill is null ? inheritedPaint.Color : hasFill ? ParseColor(fill) : SvgColor.Transparent;
        byte alpha = opacity is null ? color.Alpha : ParseOpacity(opacity);
        return new SvgPaint(new SvgColor(color.Red, color.Green, color.Blue, alpha), hasFill);
    }

    private static bool ParseDisplay(string? value)
    {
        if (value is null or "inline") return true;
        if (value == "none") return false;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only inline and none display values are implemented.");
    }

    private static int ParseInteger(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result) || result != Math.Truncate(result) || result < int.MinValue || result > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer rectangle coordinates are implemented.");
        return checked((int)result);
    }

    private static byte ParseOpacity(string? value)
    {
        if (value is null) return 255;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double opacity) || !double.IsFinite(opacity) || opacity < 0 || opacity > 1) throw new SvgException(SvgErrorCode.MalformedInput, "Fill opacity must be a finite value from zero through one.");
        return checked((byte)Math.Round(opacity * 255, MidpointRounding.AwayFromZero));
    }

    private static SvgColor ParseColor(string fill)
    {
        if (fill == "transparent") return SvgColor.Transparent;
        if (fill == "black") return new SvgColor(0, 0, 0, 255);
        if (fill == "red") return new SvgColor(255, 0, 0, 255);
        if (fill == "blue") return new SvgColor(0, 0, 255, 255);
        if (fill.Length == 7 && fill[0] == '#' && byte.TryParse(fill.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red) && byte.TryParse(fill.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green) && byte.TryParse(fill.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue)) return new SvgColor(red, green, blue, 255);
        if (fill.Length == 4 && fill[0] == '#' && byte.TryParse(fill.AsSpan(1, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red) && byte.TryParse(fill.AsSpan(2, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green) && byte.TryParse(fill.AsSpan(3, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue)) return new SvgColor((byte)(red * 17), (byte)(green * 17), (byte)(blue * 17), 255);
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only transparent, black, red, blue, #RGB, and #RRGGBB rectangle fills are implemented.");
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

    private readonly record struct GroupScope(int Depth, SvgPaint Paint, bool IsVisible);

    private readonly record struct SvgPaint(SvgColor Color, bool HasFill)
    {
        internal static SvgPaint Default { get; } = new(new SvgColor(0, 0, 0, 255), true);
    }
}
