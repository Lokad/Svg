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
            bool rootVisibility = ParseVisibility(reader.GetAttribute("visibility"), true);
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
                    if (nodeCount >= limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                    nodeCount++;
                    bool directChild = reader.Depth == rootDepth + 1;
                    bool nestedInGroup = groupDepths.Count > 0;
                    if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "rect")
                    {
                        rectangles.Add(ParseRectangle(reader, groupDepths.Count == 0 ? rootPaint : groupDepths[^1].Paint, groupDepths.Count == 0 ? rootVisible : groupDepths[^1].IsDisplayed, groupDepths.Count == 0 ? rootVisibility : groupDepths[^1].IsVisible));
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && (reader.LocalName is "g" or "a"))
                    {
                        SvgPaint paint = ParseGroupPaint(reader, groupDepths.Count == 0 ? rootPaint : groupDepths[^1].Paint);
                        bool displayed = (groupDepths.Count == 0 ? rootVisible : groupDepths[^1].IsDisplayed) && ParseDisplay(reader.GetAttribute("display"));
                        bool visibility = ParseVisibility(reader.GetAttribute("visibility"), groupDepths.Count == 0 ? rootVisibility : groupDepths[^1].IsVisible);
                        if (!reader.IsEmptyElement) groupDepths.Add(new GroupScope(reader.Depth, paint, displayed, visibility));
                    }
                    else if (directChild && (reader.LocalName is "title" or "desc" or "metadata" or "defs")) { }
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
                if ((reader.NamespaceURI.Length == 0 && reader.LocalName is "id" or "href" or "fill" or "fill-opacity" or "display" or "visibility") || (reader.NamespaceURI == "http://www.w3.org/1999/xlink" && reader.LocalName == "href")) continue;
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
                if (reader.NamespaceURI.Length == 0 && reader.LocalName is "id" or "width" or "height" or "fill" or "fill-opacity" or "display" or "visibility") continue;
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "SVG root attribute '" + reader.Name + "' is not implemented.");
            }
            reader.MoveToElement();
        }
        return ParsePaint(reader.GetAttribute("fill"), reader.GetAttribute("fill-opacity"), SvgPaint.Default);
    }

    private static SvgRectangle ParseRectangle(XmlReader reader, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisibility)
    {
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.NamespaceURI.Length == 0 && reader.LocalName is "id" or "x" or "y" or "width" or "height" or "fill" or "fill-opacity" or "opacity" or "display" or "visibility") continue;
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle attribute '" + reader.Name + "' is not implemented.");
            }
            reader.MoveToElement();
        }
        int x = ParseInteger(reader.GetAttribute("x") ?? "0");
        int y = ParseInteger(reader.GetAttribute("y") ?? "0");
        int width = ParseInteger(reader.GetAttribute("width") ?? "0");
        int height = ParseInteger(reader.GetAttribute("height") ?? "0");
        if (width < 0 || height < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Rectangle dimensions cannot be negative.");
        SvgPaint paint = ParsePaint(reader.GetAttribute("fill"), reader.GetAttribute("fill-opacity"), inheritedPaint);
        byte opacity = ParseOpacity(reader.GetAttribute("opacity"));
        SvgColor color = new(paint.Color.Red, paint.Color.Green, paint.Color.Blue, MultiplyAlpha(paint.Color.Alpha, opacity));
        return new SvgRectangle(x, y, width, height, color, paint.HasFill && inheritedDisplayed && ParseDisplay(reader.GetAttribute("display")) && ParseVisibility(reader.GetAttribute("visibility"), inheritedVisibility));
    }

    private static SvgPaint ParsePaint(string? fill, string? opacity, SvgPaint inheritedPaint)
    {
        bool hasFill = fill is null ? inheritedPaint.HasFill : !fill.Equals("none", StringComparison.OrdinalIgnoreCase) && !fill.Equals("transparent", StringComparison.OrdinalIgnoreCase);
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

    private static bool ParseVisibility(string? value, bool inheritedVisibility)
    {
        if (value is null) return inheritedVisibility;
        if (value == "visible") return true;
        if (value == "hidden") return false;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only visible and hidden visibility values are implemented.");
    }

    private static int ParseInteger(string value)
    {
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase)) token = token[..^2].TrimEnd();
        else if (token.Length > 0 && (char.IsLetter(token[^1]) || token[^1] == '%')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number and px integer rectangle coordinates are implemented.");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result) || result != Math.Truncate(result) || result < int.MinValue || result > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer rectangle coordinates are implemented.");
        return checked((int)result);
    }

    private static byte ParseOpacity(string? value)
    {
        if (value is null) return 255;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double opacity) || !double.IsFinite(opacity) || opacity < 0 || opacity > 1) throw new SvgException(SvgErrorCode.MalformedInput, "Fill opacity must be a finite value from zero through one.");
        return checked((byte)Math.Round(opacity * 255, MidpointRounding.AwayFromZero));
    }

    private static byte MultiplyAlpha(byte left, byte right) => (byte)((left * right + 127) / 255);

    private static SvgColor ParseColor(string fill)
    {
        if (fill.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return SvgColor.Transparent;
        if (fill.Equals("black", StringComparison.OrdinalIgnoreCase)) return new SvgColor(0, 0, 0, 255);
        if (fill.Equals("red", StringComparison.OrdinalIgnoreCase)) return new SvgColor(255, 0, 0, 255);
        if (fill.Equals("blue", StringComparison.OrdinalIgnoreCase)) return new SvgColor(0, 0, 255, 255);
        if (TryParseFunctionalRgb(fill, out SvgColor functionalColor)) return functionalColor;
        if (TryParseFunctionalHsl(fill, out functionalColor)) return functionalColor;
        if (fill.Length == 9 && fill[0] == '#' && byte.TryParse(fill.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red8) && byte.TryParse(fill.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green8) && byte.TryParse(fill.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue8) && byte.TryParse(fill.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte alpha8)) return new SvgColor(red8, green8, blue8, alpha8);
        if (fill.Length == 7 && fill[0] == '#' && byte.TryParse(fill.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red) && byte.TryParse(fill.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green) && byte.TryParse(fill.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue)) return new SvgColor(red, green, blue, 255);
        if (fill.Length == 5 && fill[0] == '#' && byte.TryParse(fill.AsSpan(1, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red4) && byte.TryParse(fill.AsSpan(2, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green4) && byte.TryParse(fill.AsSpan(3, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue4) && byte.TryParse(fill.AsSpan(4, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte alpha4)) return new SvgColor((byte)(red4 * 17), (byte)(green4 * 17), (byte)(blue4 * 17), (byte)(alpha4 * 17));
        if (fill.Length == 4 && fill[0] == '#' && byte.TryParse(fill.AsSpan(1, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red) && byte.TryParse(fill.AsSpan(2, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green) && byte.TryParse(fill.AsSpan(3, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue)) return new SvgColor((byte)(red * 17), (byte)(green * 17), (byte)(blue * 17), 255);
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only transparent, black, red, blue, comma-form rgb()/rgba(), #RGB, #RGBA, #RRGGBB, and #RRGGBBAA rectangle fills are implemented.");
    }

    private static bool TryParseFunctionalRgb(string fill, out SvgColor color)
    {
        bool hasAlpha = fill.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) && fill.EndsWith(')');
        bool isRgb = fill.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && fill.EndsWith(')');
        if (!hasAlpha && !isRgb) { color = default; return false; }
        string[] channels = fill[(hasAlpha ? 5 : 4)..^1].Split(',', StringSplitOptions.TrimEntries);
        if (channels.Length != (hasAlpha ? 4 : 3) || !TryParseRgbChannel(channels[0], out byte red) || !TryParseRgbChannel(channels[1], out byte green) || !TryParseRgbChannel(channels[2], out byte blue))
        {
            throw new SvgException(SvgErrorCode.MalformedInput, "RGB color channels must be decimal bytes.");
        }
        color = new SvgColor(red, green, blue, hasAlpha ? ParseOpacity(channels[3]) : (byte)255);
        return true;
    }

    private static bool TryParseRgbChannel(string token, out byte value)
    {
        if (token.EndsWith('%'))
        {
            bool valid = double.TryParse(token.AsSpan()[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double percentage) && double.IsFinite(percentage) && percentage >= 0 && percentage <= 100;
            value = valid ? checked((byte)Math.Round(percentage * 255 / 100, MidpointRounding.AwayFromZero)) : default;
            return valid;
        }
        return byte.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseFunctionalHsl(string fill, out SvgColor color)
    {
        bool hasAlpha = fill.StartsWith("hsla(", StringComparison.OrdinalIgnoreCase) && fill.EndsWith(')');
        bool isHsl = fill.StartsWith("hsl(", StringComparison.OrdinalIgnoreCase) && fill.EndsWith(')');
        if (!hasAlpha && !isHsl) { color = default; return false; }
        string[] channels = fill[(hasAlpha ? 5 : 4)..^1].Split(',', StringSplitOptions.TrimEntries);
        if (channels.Length != (hasAlpha ? 4 : 3) || !double.TryParse(channels[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double hue) || !TryParsePercentage(channels[1], out double saturation) || !TryParsePercentage(channels[2], out double lightness)) throw new SvgException(SvgErrorCode.MalformedInput, "HSL colors require a hue and percentage saturation/lightness channels.");
        hue = ((hue % 360) + 360) % 360;
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double secondary = chroma * (1 - Math.Abs((hue / 60 % 2) - 1));
        (double red, double green, double blue) = hue < 60 ? (chroma, secondary, 0d) : hue < 120 ? (secondary, chroma, 0d) : hue < 180 ? (0d, chroma, secondary) : hue < 240 ? (0d, secondary, chroma) : hue < 300 ? (secondary, 0d, chroma) : (chroma, 0d, secondary);
        double match = lightness - chroma / 2;
        color = new SvgColor((byte)Math.Round((red + match) * 255, MidpointRounding.AwayFromZero), (byte)Math.Round((green + match) * 255, MidpointRounding.AwayFromZero), (byte)Math.Round((blue + match) * 255, MidpointRounding.AwayFromZero), hasAlpha ? ParseOpacity(channels[3]) : (byte)255);
        return true;
    }

    private static bool TryParsePercentage(string token, out double value)
    {
        value = 0;
        bool valid = token.EndsWith('%') && double.TryParse(token.AsSpan()[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && value >= 0 && value <= 100;
        value /= valid ? 100 : 1;
        return valid;
    }

    private static double? ParseLength(string? value)
    {
        if (value is null) return null;
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        double multiplier = 1;
        if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase)) token = token[..^2].TrimEnd();
        else if (token.EndsWith("in", StringComparison.OrdinalIgnoreCase)) { token = token[..^2].TrimEnd(); multiplier = 96; }
        else if (token.EndsWith("cm", StringComparison.OrdinalIgnoreCase)) { token = token[..^2].TrimEnd(); multiplier = 96d / 2.54d; }
        else if (token.EndsWith("mm", StringComparison.OrdinalIgnoreCase)) { token = token[..^2].TrimEnd(); multiplier = 96d / 25.4d; }
        else if (token.EndsWith("pt", StringComparison.OrdinalIgnoreCase)) { token = token[..^2].TrimEnd(); multiplier = 96d / 72d; }
        else if (token.EndsWith("pc", StringComparison.OrdinalIgnoreCase)) { token = token[..^2].TrimEnd(); multiplier = 16; }
        else if (token.IndexOfAny("%emivc".AsSpan()) >= 0 || (token.Length > 0 && char.IsLetter(token[^1]))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number, px, in, cm, mm, pt, and pc root dimensions are implemented.");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result) || result <= 0 || !double.IsFinite(result *= multiplier)) throw new SvgException(SvgErrorCode.MalformedInput, "Root dimensions must be finite positive CSS pixel lengths.");
        return result;
    }

    private readonly record struct GroupScope(int Depth, SvgPaint Paint, bool IsDisplayed, bool IsVisible);

    private readonly record struct SvgPaint(SvgColor Color, bool HasFill)
    {
        internal static SvgPaint Default { get; } = new(new SvgColor(0, 0, 0, 255), true);
    }
}
