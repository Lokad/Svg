using System.Globalization;
using System.Xml;
using System.Text.RegularExpressions;

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
            InlineStyle rootStyle = ParseInlineStyle(reader.GetAttribute("style"));
            SvgPaint rootPaint = ParseRootPaint(reader, rootStyle);
            byte rootOpacity = ParseOpacity(GetProperty(reader, rootStyle.Opacity, "opacity"));
            bool rootVisible = ParseDisplay(GetProperty(reader, rootStyle.Display, "display"));
            bool rootVisibility = ParseVisibility(GetProperty(reader, rootStyle.Visibility, "visibility"), true);
            double? width = ParseLength(reader.GetAttribute("width"));
            double? height = ParseLength(reader.GetAttribute("height"));
            SvgViewBox? viewBox = ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio"));
            if (reader.IsEmptyElement) return new SvgRoot(width, height, viewBox, [], [], false);
            int rootDepth = reader.Depth;
            List<SvgRectangle> rectangles = [];
            List<SvgDrawCommand> commands = [];
            if (rootOpacity != 255) commands.Add(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, default, rootOpacity));
            List<GroupScope> groupDepths = [];
            int nodeCount = 0;
            bool unsupported = false;
            bool sawGraphicNode = false;
            InlineStyle rectangleStyleSheet = default;
            bool hasRectangleStyleSheet = false;
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
                        AxisTransform transform = groupDepths.Count == 0 ? AxisTransform.Identity : groupDepths[^1].Transform;
                        SvgRectangle rectangle = ParseRectangle(reader, groupDepths.Count == 0 ? rootPaint : groupDepths[^1].Paint, groupDepths.Count == 0 ? rootVisible : groupDepths[^1].IsDisplayed, groupDepths.Count == 0 ? rootVisibility : groupDepths[^1].IsVisible, transform, rectangleStyleSheet);
                        rectangles.Add(rectangle); commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Rectangle, rectangle, 255));
                        sawGraphicNode = true;
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "path")
                    {
                        AxisTransform transform = groupDepths.Count == 0 ? AxisTransform.Identity : groupDepths[^1].Transform;
                        SvgRectangle[] pathRectangles = ParseOrthogonalPath(reader, groupDepths.Count == 0 ? rootPaint : groupDepths[^1].Paint, groupDepths.Count == 0 ? rootVisible : groupDepths[^1].IsDisplayed, groupDepths.Count == 0 ? rootVisibility : groupDepths[^1].IsVisible, transform, limits.MaximumPathCommands);
                        rectangles.AddRange(pathRectangles); foreach (SvgRectangle rectangle in pathRectangles) commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Rectangle, rectangle, 255));
                        sawGraphicNode = true;
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName is "polygon" or "polyline")
                    {
                        AxisTransform transform = groupDepths.Count == 0 ? AxisTransform.Identity : groupDepths[^1].Transform;
                        SvgRectangle[] pointRectangles = ParseOrthogonalPointList(reader, reader.LocalName, groupDepths.Count == 0 ? rootPaint : groupDepths[^1].Paint, groupDepths.Count == 0 ? rootVisible : groupDepths[^1].IsDisplayed, groupDepths.Count == 0 ? rootVisibility : groupDepths[^1].IsVisible, transform, limits.MaximumPathCommands);
                        rectangles.AddRange(pointRectangles); foreach (SvgRectangle rectangle in pointRectangles) commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Rectangle, rectangle, 255));
                        sawGraphicNode = true;
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && (reader.LocalName is "g" or "a"))
                    {
                        InlineStyle style = ParseInlineStyle(reader.GetAttribute("style"));
                        SvgPaint paint = ParseGroupPaint(reader, groupDepths.Count == 0 ? rootPaint : groupDepths[^1].Paint, style);
                        bool displayed = (groupDepths.Count == 0 ? rootVisible : groupDepths[^1].IsDisplayed) && ParseDisplay(GetProperty(reader, style.Display, "display"));
                        bool visibility = ParseVisibility(GetProperty(reader, style.Visibility, "visibility"), groupDepths.Count == 0 ? rootVisibility : groupDepths[^1].IsVisible);
                        AxisTransform transform = groupDepths.Count == 0 ? ParseTransform(reader.GetAttribute("transform")) : groupDepths[^1].Transform.Compose(ParseTransform(reader.GetAttribute("transform")));
                        byte opacity = ParseOpacity(GetProperty(reader, style.Opacity, "opacity"));
                        bool isolatesOpacity = opacity != 255;
                        if (isolatesOpacity && (rootOpacity != 255 || groupDepths.Any(static group => group.IsolatesOpacity))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Nested group opacity requires separate compositing calibration.");
                        if (!reader.IsEmptyElement) { if (isolatesOpacity) commands.Add(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, default, opacity)); groupDepths.Add(new GroupScope(reader.Depth, paint, displayed, visibility, transform, isolatesOpacity)); }
                    }
                    else if (directChild && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "style")
                    {
                        if (sawGraphicNode || hasRectangleStyleSheet) throw new SvgException(SvgErrorCode.UnsupportedFeature, "A single root-level rectangle stylesheet rule must precede rendered content.");
                        if (reader.HasAttributes) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet attributes are not implemented.");
                        rectangleStyleSheet = ParseRectangleStyleSheet(ReadStyleElementText(reader));
                        hasRectangleStyleSheet = true;
                    }
                    else if (directChild && (reader.LocalName is "title" or "desc" or "metadata" or "defs")) { }
                    else if (directChild || nestedInGroup) unsupported = true;
                }
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (groupDepths.Count > 0 && groupDepths[^1].Depth == reader.Depth) { GroupScope group = groupDepths[^1]; groupDepths.RemoveAt(groupDepths.Count - 1); if (group.IsolatesOpacity) commands.Add(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, default, 255)); }
                    if (reader.Depth == rootDepth) { if (rootOpacity != 255) commands.Add(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, default, 255)); return new SvgRoot(width, height, viewBox, rectangles.ToArray(), commands.ToArray(), unsupported); }
                }
            }
            throw new SvgException(SvgErrorCode.MalformedInput, "SVG root element is not closed.");
        }
        throw new SvgException(SvgErrorCode.MalformedInput, "SVG input has no root element.");
    }

    private static SvgPaint ParseGroupPaint(XmlReader reader, SvgPaint inheritedPaint, InlineStyle style)
    {
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns" || reader.Name == "xmlns") continue;
                if ((reader.NamespaceURI.Length == 0 && reader.LocalName is "id" or "href" or "color" or "fill" or "fill-opacity" or "stroke" or "opacity" or "display" or "visibility" or "style" or "transform") || (reader.NamespaceURI == "http://www.w3.org/1999/xlink" && reader.LocalName == "href")) continue;
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Group attribute '" + reader.Name + "' is not implemented.");
            }
            reader.MoveToElement();
        }
        if (style.FillRule is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill rules are only implemented on shapes.");
        ValidateStroke(GetProperty(reader, style.Stroke, "stroke"));
        return ParsePaint(GetProperty(reader, style.Fill, "fill"), GetProperty(reader, style.FillOpacity, "fill-opacity"), GetProperty(reader, style.Color, "color"), inheritedPaint);
    }

    private static SvgPaint ParseRootPaint(XmlReader reader, InlineStyle style)
    {
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns" || reader.Name == "xmlns") continue;
                if (reader.NamespaceURI.Length == 0 && reader.LocalName is "id" or "width" or "height" or "viewBox" or "preserveAspectRatio" or "color" or "fill" or "fill-opacity" or "stroke" or "opacity" or "display" or "visibility" or "style") continue;
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "SVG root attribute '" + reader.Name + "' is not implemented.");
            }
            reader.MoveToElement();
        }
        if (style.FillRule is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill rules are only implemented on shapes.");
        ValidateStroke(GetProperty(reader, style.Stroke, "stroke"));
        return ParsePaint(GetProperty(reader, style.Fill, "fill"), GetProperty(reader, style.FillOpacity, "fill-opacity"), GetProperty(reader, style.Color, "color"), SvgPaint.Default);
    }

    private static SvgRectangle ParseRectangle(XmlReader reader, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisibility, AxisTransform inheritedTransform, InlineStyle styleSheet)
    {
        InlineStyle style = ParseInlineStyle(reader.GetAttribute("style"));
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.NamespaceURI.Length == 0 && reader.LocalName is "id" or "x" or "y" or "width" or "height" or "rx" or "ry" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "opacity" or "display" or "visibility" or "style" or "transform") continue;
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle attribute '" + reader.Name + "' is not implemented.");
            }
            reader.MoveToElement();
        }
        double x = ParseRectangleCoordinate(reader.GetAttribute("x") ?? "0");
        double y = ParseRectangleCoordinate(reader.GetAttribute("y") ?? "0");
        double width = ParseRectangleCoordinate(reader.GetAttribute("width") ?? "0");
        double height = ParseRectangleCoordinate(reader.GetAttribute("height") ?? "0");
        AxisTransform transform = inheritedTransform.Compose(ParseTransform(reader.GetAttribute("transform")));
        x = transform.ScaleX * x + transform.OffsetX; y = transform.ScaleY * y + transform.OffsetY;
        width = transform.ScaleX * width; height = transform.ScaleY * height;
        ValidateSquareCorners(reader.GetAttribute("rx"));
        ValidateSquareCorners(reader.GetAttribute("ry"));
        if (width < 0 || height < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Rectangle dimensions cannot be negative.");
        SvgPaint paint = ParsePaint(GetProperty(reader, style.Fill, styleSheet.Fill, "fill"), GetProperty(reader, style.FillOpacity, styleSheet.FillOpacity, "fill-opacity"), GetProperty(reader, style.Color, styleSheet.Color, "color"), inheritedPaint);
        ValidateFillRule(GetProperty(reader, style.FillRule, styleSheet.FillRule, "fill-rule"));
        ValidateStroke(GetProperty(reader, style.Stroke, styleSheet.Stroke, "stroke"));
        byte opacity = ParseOpacity(GetProperty(reader, style.Opacity, styleSheet.Opacity, "opacity"));
        SvgColor color = new(paint.Color.Red, paint.Color.Green, paint.Color.Blue, MultiplyAlpha(paint.Color.Alpha, opacity));
        return new SvgRectangle(x, y, width, height, color, paint.HasFill && inheritedDisplayed && ParseDisplay(GetProperty(reader, style.Display, styleSheet.Display, "display")) && ParseVisibility(GetProperty(reader, style.Visibility, styleSheet.Visibility, "visibility"), inheritedVisibility));
    }

    private static SvgRectangle[] ParseOrthogonalPath(XmlReader reader, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisibility, AxisTransform inheritedTransform, int maximumGeometryElements)
    {
        if (reader.HasAttributes) { while (reader.MoveToNextAttribute()) { if (reader.NamespaceURI.Length == 0 && reader.LocalName is "id" or "d" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "opacity" or "display" or "visibility" or "style" or "transform") continue; throw new SvgException(SvgErrorCode.UnsupportedFeature, "Path attribute '" + reader.Name + "' is not implemented."); } reader.MoveToElement(); }
        string d = reader.GetAttribute("d") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Path data is required.");
        string[] tokens = Regex.Replace(d, @"([MLHVZz])", " $1 ").Replace(',', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 7 || tokens[0] != "M") throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented.");
        int tokenIndex = 1;
        List<SvgPoint> points = [new SvgPoint(ParsePathInteger(), ParsePathInteger())];
        bool closed = false;
        while (tokenIndex < tokens.Length)
        {
            string command = tokens[tokenIndex++];
            SvgPoint current = points[^1];
            if (command == "H") points.Add(new SvgPoint(ParsePathInteger(), current.Y));
            else if (command == "V") points.Add(new SvgPoint(current.X, ParsePathInteger()));
            else if (command == "L") points.Add(new SvgPoint(ParsePathInteger(), ParsePathInteger()));
            else if (command is "Z" or "z") { closed = tokenIndex == tokens.Length; break; }
            else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented.");
        }
        if (!closed || points.Count < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented.");
        if (points.Count > maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, "Path command count exceeds the configured limit.");
        for (int pointIndex = 0; pointIndex < points.Count; pointIndex++) { SvgPoint start = points[pointIndex]; SvgPoint end = points[(pointIndex + 1) % points.Count]; if ((start.X == end.X) == (start.Y == end.Y)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented."); }
        ValidateSimpleOrthogonalContour(points, "path");
        InlineStyle style = ParseInlineStyle(reader.GetAttribute("style")); AxisTransform transform = inheritedTransform.Compose(ParseTransform(reader.GetAttribute("transform")));
        SvgPaint paint = ParsePaint(GetProperty(reader, style.Fill, "fill"), GetProperty(reader, style.FillOpacity, "fill-opacity"), GetProperty(reader, style.Color, "color"), inheritedPaint); ValidateFillRule(GetProperty(reader, style.FillRule, "fill-rule")); ValidateStroke(GetProperty(reader, style.Stroke, "stroke"));
        byte opacity = ParseOpacity(GetProperty(reader, style.Opacity, "opacity")); SvgColor color = new(paint.Color.Red, paint.Color.Green, paint.Color.Blue, MultiplyAlpha(paint.Color.Alpha, opacity));
        bool hasFill = paint.HasFill && inheritedDisplayed && ParseDisplay(GetProperty(reader, style.Display, "display")) && ParseVisibility(GetProperty(reader, style.Visibility, "visibility"), inheritedVisibility);
        List<int> boundaries = points.Select(static point => point.Y).Distinct().Order().ToList(); List<SvgRectangle> rectangles = [];
        for (int boundaryIndex = 0; boundaryIndex < boundaries.Count - 1; boundaryIndex++)
        {
            int top = boundaries[boundaryIndex]; int bottom = boundaries[boundaryIndex + 1]; List<int> intersections = [];
            for (int pointIndex = 0; pointIndex < points.Count; pointIndex++) { SvgPoint start = points[pointIndex]; SvgPoint end = points[(pointIndex + 1) % points.Count]; if (start.X == end.X && Math.Min(start.Y, end.Y) <= top && Math.Max(start.Y, end.Y) >= bottom) intersections.Add(start.X); }
            intersections.Sort();
            if (intersections.Count % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only simple closed absolute axis-aligned integer paths are implemented.");
            for (int intersectionIndex = 0; intersectionIndex < intersections.Count; intersectionIndex += 2) { int left = intersections[intersectionIndex]; int right = intersections[intersectionIndex + 1]; if (right <= left) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only simple closed absolute axis-aligned integer paths are implemented."); if (rectangles.Count >= maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, "Path rectangle-band count exceeds the configured path-command limit."); rectangles.Add(new SvgRectangle(checked(transform.ScaleX * left + transform.OffsetX), checked(transform.ScaleY * top + transform.OffsetY), checked(transform.ScaleX * (right - left)), checked(transform.ScaleY * (bottom - top)), color, hasFill)); }
        }
        return rectangles.ToArray();

        int ParsePathInteger()
        {
            if (tokenIndex >= tokens.Length || !Regex.IsMatch(tokens[tokenIndex], @"^-?\d+$")) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented.");
            return ParseInteger(tokens[tokenIndex++]);
        }
    }

    private static SvgRectangle[] ParseOrthogonalPointList(XmlReader reader, string elementName, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisibility, AxisTransform inheritedTransform, int maximumGeometryElements)
    {
        if (reader.HasAttributes) { while (reader.MoveToNextAttribute()) { if (reader.NamespaceURI.Length == 0 && reader.LocalName is "id" or "points" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "opacity" or "display" or "visibility" or "style" or "transform") continue; throw new SvgException(SvgErrorCode.UnsupportedFeature, elementName + " attribute '" + reader.Name + "' is not implemented."); } reader.MoveToElement(); }
        string pointsValue = reader.GetAttribute("points") ?? throw new SvgException(SvgErrorCode.MalformedInput, elementName + " points are required.");
        string[] values = pointsValue.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length < 6 || values.Length % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer " + elementName + " elements are implemented.");
        if (values.Length / 2 > maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, elementName + " vertex count exceeds the configured path-command limit.");
        List<SvgPoint> points = new(values.Length / 2);
        for (int index = 0; index < values.Length; index += 2) points.Add(new SvgPoint(ParseInteger(values[index]), ParseInteger(values[index + 1])));
        for (int index = 0; index < points.Count; index++)
        {
            SvgPoint start = points[index]; SvgPoint end = points[(index + 1) % points.Count];
            if ((start.X == end.X) == (start.Y == end.Y)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer " + elementName + " elements are implemented.");
        }
        ValidateSimpleOrthogonalContour(points, elementName);
        List<int> boundaries = points.Select(static point => point.Y).Distinct().Order().ToList();
        List<SvgRectangle> rectangles = [];
        InlineStyle style = ParseInlineStyle(reader.GetAttribute("style")); AxisTransform transform = inheritedTransform.Compose(ParseTransform(reader.GetAttribute("transform")));
        SvgPaint paint = ParsePaint(GetProperty(reader, style.Fill, "fill"), GetProperty(reader, style.FillOpacity, "fill-opacity"), GetProperty(reader, style.Color, "color"), inheritedPaint); ValidateFillRule(GetProperty(reader, style.FillRule, "fill-rule")); ValidateStroke(GetProperty(reader, style.Stroke, "stroke"));
        byte opacity = ParseOpacity(GetProperty(reader, style.Opacity, "opacity")); SvgColor color = new(paint.Color.Red, paint.Color.Green, paint.Color.Blue, MultiplyAlpha(paint.Color.Alpha, opacity)); bool hasFill = paint.HasFill && inheritedDisplayed && ParseDisplay(GetProperty(reader, style.Display, "display")) && ParseVisibility(GetProperty(reader, style.Visibility, "visibility"), inheritedVisibility);
        for (int boundaryIndex = 0; boundaryIndex < boundaries.Count - 1; boundaryIndex++)
        {
            int top = boundaries[boundaryIndex]; int bottom = boundaries[boundaryIndex + 1];
            List<int> intersections = [];
            for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
            {
                SvgPoint start = points[pointIndex]; SvgPoint end = points[(pointIndex + 1) % points.Count];
                if (start.X == end.X && Math.Min(start.Y, end.Y) <= top && Math.Max(start.Y, end.Y) >= bottom) intersections.Add(start.X);
            }
            intersections.Sort();
            if (intersections.Count == 0) continue;
            if (intersections.Count % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only simple closed axis-aligned integer " + elementName + " elements are implemented.");
            for (int intersectionIndex = 0; intersectionIndex < intersections.Count; intersectionIndex += 2)
            {
                int left = intersections[intersectionIndex]; int right = intersections[intersectionIndex + 1];
                if (right <= left) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only simple closed axis-aligned integer " + elementName + " elements are implemented.");
                if (rectangles.Count >= maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, elementName + " rectangle-band count exceeds the configured path-command limit.");
                rectangles.Add(new SvgRectangle(checked(transform.ScaleX * left + transform.OffsetX), checked(transform.ScaleY * top + transform.OffsetY), checked(transform.ScaleX * (right - left)), checked(transform.ScaleY * (bottom - top)), color, hasFill));
            }
        }
        return rectangles.ToArray();
    }

    private static void ValidateSimpleOrthogonalContour(List<SvgPoint> points, string elementName)
    {
        for (int firstIndex = 0; firstIndex < points.Count; firstIndex++)
        {
            SvgPoint firstStart = points[firstIndex]; SvgPoint firstEnd = points[(firstIndex + 1) % points.Count];
            bool firstHorizontal = firstStart.Y == firstEnd.Y;
            for (int secondIndex = firstIndex + 1; secondIndex < points.Count; secondIndex++)
            {
                SvgPoint secondStart = points[secondIndex]; SvgPoint secondEnd = points[(secondIndex + 1) % points.Count];
                bool secondHorizontal = secondStart.Y == secondEnd.Y;
                bool intersects = firstHorizontal == secondHorizontal
                    ? firstHorizontal
                        ? firstStart.Y == secondStart.Y && Math.Max(Math.Min(firstStart.X, firstEnd.X), Math.Min(secondStart.X, secondEnd.X)) <= Math.Min(Math.Max(firstStart.X, firstEnd.X), Math.Max(secondStart.X, secondEnd.X))
                        : firstStart.X == secondStart.X && Math.Max(Math.Min(firstStart.Y, firstEnd.Y), Math.Min(secondStart.Y, secondEnd.Y)) <= Math.Min(Math.Max(firstStart.Y, firstEnd.Y), Math.Max(secondStart.Y, secondEnd.Y))
                    : firstHorizontal
                        ? Math.Min(firstStart.X, firstEnd.X) <= secondStart.X && secondStart.X <= Math.Max(firstStart.X, firstEnd.X) && Math.Min(secondStart.Y, secondEnd.Y) <= firstStart.Y && firstStart.Y <= Math.Max(secondStart.Y, secondEnd.Y)
                        : Math.Min(secondStart.X, secondEnd.X) <= firstStart.X && firstStart.X <= Math.Max(secondStart.X, secondEnd.X) && Math.Min(firstStart.Y, firstEnd.Y) <= secondStart.Y && firstStart.Y <= Math.Max(firstStart.Y, firstEnd.Y);
                if (!intersects) continue;
                bool adjacent = secondIndex == firstIndex + 1 || (firstIndex == 0 && secondIndex == points.Count - 1);
                SvgPoint shared = secondIndex == firstIndex + 1 ? firstEnd : firstStart;
                if (!adjacent || firstHorizontal == secondHorizontal || (firstHorizontal ? new SvgPoint(secondStart.X, firstStart.Y) : new SvgPoint(firstStart.X, secondStart.Y)) != shared) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only simple closed axis-aligned integer " + elementName + " contours are implemented.");
            }
        }
    }

    private static string? GetProperty(XmlReader reader, string? inlineValue, string name) => inlineValue ?? reader.GetAttribute(name);

    private static string? GetProperty(XmlReader reader, string? inlineValue, string? styleSheetValue, string name) => inlineValue ?? styleSheetValue ?? reader.GetAttribute(name);

    private static AxisTransform ParseTransform(string? value)
    {
        if (value is null) return AxisTransform.Identity;
        MatchCollection matches = Regex.Matches(value, @"(?<name>translate|scale|matrix)\s*\((?<arguments>[^)]*)\)", RegexOptions.IgnoreCase);
        AxisTransform transform = AxisTransform.Identity;
        int consumed = 0;
        foreach (Match match in matches)
        {
            if (!string.IsNullOrWhiteSpace(value.Substring(consumed, match.Index - consumed))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer translate(), positive scale(), and axis-aligned matrix() transforms are implemented.");
            transform = transform.Compose(ParseTransformFunction(match.Groups["name"].Value, match.Groups["arguments"].Value));
            consumed = match.Index + match.Length;
        }
        if (matches.Count == 0 || !string.IsNullOrWhiteSpace(value[consumed..])) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer translate(), positive scale(), and axis-aligned matrix() transforms are implemented.");
        return transform;
    }

    private static AxisTransform ParseTransformFunction(string name, string arguments)
    {
        string[] values = arguments.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (name.Equals("matrix", StringComparison.OrdinalIgnoreCase))
        {
            if (values.Length != 6) throw new SvgException(SvgErrorCode.MalformedInput, "matrix() requires six coordinates.");
            int scaleX = ParseInteger(values[0]); int skewY = ParseInteger(values[1]); int skewX = ParseInteger(values[2]); int scaleY = ParseInteger(values[3]); int offsetX = ParseInteger(values[4]); int offsetY = ParseInteger(values[5]);
            if (skewX != 0 || skewY != 0 || scaleX <= 0 || scaleY <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive axis-aligned integer matrix() transforms are implemented.");
            return new AxisTransform(scaleX, scaleY, offsetX, offsetY);
        }
        if (values.Length is < 1 or > 2) throw new SvgException(SvgErrorCode.MalformedInput, "Transforms require one or two coordinates.");
        int first = ParseInteger(values[0]); int second = values.Length == 2 ? ParseInteger(values[1]) : first;
        if (name.Equals("translate", StringComparison.OrdinalIgnoreCase)) return new AxisTransform(1, 1, first, values.Length == 2 ? second : 0);
        if (first <= 0 || second <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive integer scale() values are implemented.");
        return new AxisTransform(first, second, 0, 0);
    }

    private static void ValidateNonGroupStyle(InlineStyle style)
    {
        if (style.Opacity is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Root and group opacity require isolated compositing and are not implemented.");
        if (style.FillRule is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill rules are only implemented on rectangles.");
    }

    private static InlineStyle ParseInlineStyle(string? value)
    {
        if (value is null) return default;
        string? color = null, fill = null, fillOpacity = null, stroke = null, display = null, visibility = null, opacity = null, fillRule = null;
        foreach (string declaration in value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = declaration.IndexOf(':');
            if (separator <= 0 || declaration.IndexOf(':', separator + 1) >= 0) throw new SvgException(SvgErrorCode.MalformedInput, "Inline style declarations must use property:value syntax.");
            string property = declaration[..separator].Trim().ToLowerInvariant();
            string propertyValue = declaration[(separator + 1)..].Trim();
            if (propertyValue.Length == 0 || propertyValue.Contains("!important", StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Empty and !important inline style declarations are not implemented.");
            switch (property)
            {
                case "color": color = propertyValue; break;
                case "fill": fill = propertyValue; break;
                case "fill-opacity": fillOpacity = propertyValue; break;
                case "stroke": stroke = propertyValue; break;
                case "display": display = propertyValue; break;
                case "visibility": visibility = propertyValue; break;
                case "opacity": opacity = propertyValue; break;
                case "fill-rule": fillRule = propertyValue; break;
                default: throw new SvgException(SvgErrorCode.UnsupportedFeature, "Inline style property '" + property + "' is not implemented.");
            }
        }
        return new InlineStyle(color, fill, fillOpacity, stroke, display, visibility, opacity, fillRule);
    }

    private static InlineStyle ParseRectangleStyleSheet(string value)
    {
        Match match = Regex.Match(value, @"^\s*rect\s*\{(?<declarations>[^{}]*)\}\s*$", RegexOptions.IgnoreCase);
        if (!match.Success) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only one rect type-selector stylesheet rule is implemented.");
        return ParseInlineStyle(match.Groups["declarations"].Value);
    }

    private static string ReadStyleElementText(XmlReader reader)
    {
        using XmlReader subtree = reader.ReadSubtree();
        subtree.Read();
        return subtree.ReadElementContentAsString();
    }

    private static SvgPaint ParsePaint(string? fill, string? opacity, string? currentColor, SvgPaint inheritedPaint)
    {
        SvgColor inheritedCurrentColor = currentColor is null ? inheritedPaint.CurrentColor : ParseColor(currentColor);
        bool hasFill = fill is null ? inheritedPaint.HasFill : !fill.Equals("none", StringComparison.OrdinalIgnoreCase) && !fill.Equals("transparent", StringComparison.OrdinalIgnoreCase);
        SvgColor color = fill is null ? inheritedPaint.Color : fill.Equals("currentColor", StringComparison.OrdinalIgnoreCase) ? inheritedCurrentColor : hasFill ? ParseColor(fill) : SvgColor.Transparent;
        byte alpha = opacity is null ? color.Alpha : MultiplyAlpha(color.Alpha, ParseOpacity(opacity));
        return new SvgPaint(new SvgColor(color.Red, color.Green, color.Blue, alpha), hasFill, inheritedCurrentColor);
    }

    private static bool ParseDisplay(string? value)
    {
        if (value is null || value.Equals("inline", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) return false;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only inline and none display values are implemented.");
    }

    private static void ValidateFillRule(string? value)
    {
        if (value is null || value.Equals("nonzero", StringComparison.OrdinalIgnoreCase) || value.Equals("evenodd", StringComparison.OrdinalIgnoreCase)) return;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only nonzero and evenodd fill rules are implemented.");
    }

    private static void ValidateStroke(string? value)
    {
        if (value is null || value.Equals("none", StringComparison.OrdinalIgnoreCase)) return;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only non-painting stroke='none' is implemented.");
    }

    private static void ValidateSquareCorners(string? value)
    {
        if (value is null || ParseRectangleCoordinate(value) == 0) return;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only zero rectangle corner radii are implemented.");
    }

    private static double ParseRectangleCoordinate(string value)
    {
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase)) token = token[..^2].TrimEnd();
        else if (token.Length > 0 && (char.IsLetter(token[^1]) || token[^1] == '%')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number and px rectangle coordinates are implemented.");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result) || result < int.MinValue || result > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle coordinates must be finite bounded numbers.");
        if (Math.Abs(result * 4 - Math.Round(result * 4, MidpointRounding.AwayFromZero)) > 0.000000001) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer and quarter-pixel rectangle coordinates are implemented.");
        return result;
    }

    private static bool ParseVisibility(string? value, bool inheritedVisibility)
    {
        if (value is null) return inheritedVisibility;
        if (value.Equals("visible", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("hidden", StringComparison.OrdinalIgnoreCase)) return false;
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

    private static byte ParseAlpha(string value)
    {
        if (!value.EndsWith('%')) return ParseOpacity(value);
        if (!double.TryParse(value.AsSpan()[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double percentage) || !double.IsFinite(percentage) || percentage < 0 || percentage > 100) throw new SvgException(SvgErrorCode.MalformedInput, "Alpha percentages must be finite values from zero through one hundred.");
        return checked((byte)Math.Round(percentage * 255 / 100, MidpointRounding.AwayFromZero));
    }

    private static SvgColor ParseColor(string fill)
    {
        if (fill.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return SvgColor.Transparent;
        if (fill.Equals("black", StringComparison.OrdinalIgnoreCase)) return new SvgColor(0, 0, 0, 255);
        if (fill.Equals("red", StringComparison.OrdinalIgnoreCase)) return new SvgColor(255, 0, 0, 255);
        if (fill.Equals("blue", StringComparison.OrdinalIgnoreCase)) return new SvgColor(0, 0, 255, 255);
        if (fill.Equals("lime", StringComparison.OrdinalIgnoreCase)) return new SvgColor(0, 255, 0, 255);
        if (fill.Equals("white", StringComparison.OrdinalIgnoreCase)) return new SvgColor(255, 255, 255, 255);
        if (fill.Equals("yellow", StringComparison.OrdinalIgnoreCase)) return new SvgColor(255, 255, 0, 255);
        if (fill.Equals("cyan", StringComparison.OrdinalIgnoreCase) || fill.Equals("aqua", StringComparison.OrdinalIgnoreCase)) return new SvgColor(0, 255, 255, 255);
        if (fill.Equals("magenta", StringComparison.OrdinalIgnoreCase) || fill.Equals("fuchsia", StringComparison.OrdinalIgnoreCase)) return new SvgColor(255, 0, 255, 255);
        if (fill.Equals("gray", StringComparison.OrdinalIgnoreCase) || fill.Equals("grey", StringComparison.OrdinalIgnoreCase)) return new SvgColor(128, 128, 128, 255);
        if (fill.Equals("silver", StringComparison.OrdinalIgnoreCase)) return new SvgColor(192, 192, 192, 255);
        if (fill.Equals("maroon", StringComparison.OrdinalIgnoreCase)) return new SvgColor(128, 0, 0, 255);
        if (fill.Equals("olive", StringComparison.OrdinalIgnoreCase)) return new SvgColor(128, 128, 0, 255);
        if (fill.Equals("green", StringComparison.OrdinalIgnoreCase)) return new SvgColor(0, 128, 0, 255);
        if (fill.Equals("teal", StringComparison.OrdinalIgnoreCase)) return new SvgColor(0, 128, 128, 255);
        if (fill.Equals("navy", StringComparison.OrdinalIgnoreCase)) return new SvgColor(0, 0, 128, 255);
        if (fill.Equals("purple", StringComparison.OrdinalIgnoreCase)) return new SvgColor(128, 0, 128, 255);
        if (TryParseFunctionalRgb(fill, out SvgColor functionalColor)) return functionalColor;
        if (TryParseFunctionalHsl(fill, out functionalColor)) return functionalColor;
        if (fill.Length == 9 && fill[0] == '#' && byte.TryParse(fill.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red8) && byte.TryParse(fill.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green8) && byte.TryParse(fill.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue8) && byte.TryParse(fill.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte alpha8)) return new SvgColor(red8, green8, blue8, alpha8);
        if (fill.Length == 7 && fill[0] == '#' && byte.TryParse(fill.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red) && byte.TryParse(fill.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green) && byte.TryParse(fill.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue)) return new SvgColor(red, green, blue, 255);
        if (fill.Length == 5 && fill[0] == '#' && byte.TryParse(fill.AsSpan(1, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red4) && byte.TryParse(fill.AsSpan(2, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green4) && byte.TryParse(fill.AsSpan(3, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue4) && byte.TryParse(fill.AsSpan(4, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte alpha4)) return new SvgColor((byte)(red4 * 17), (byte)(green4 * 17), (byte)(blue4 * 17), (byte)(alpha4 * 17));
        if (fill.Length == 4 && fill[0] == '#' && byte.TryParse(fill.AsSpan(1, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red) && byte.TryParse(fill.AsSpan(2, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green) && byte.TryParse(fill.AsSpan(3, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue)) return new SvgColor((byte)(red * 17), (byte)(green * 17), (byte)(blue * 17), 255);
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only basic CSS named colors, comma-form rgb()/rgba(), #RGB, #RGBA, #RRGGBB, and #RRGGBBAA rectangle fills are implemented.");
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
        color = new SvgColor(red, green, blue, hasAlpha ? ParseAlpha(channels[3]) : (byte)255);
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
        color = new SvgColor((byte)Math.Round((red + match) * 255, MidpointRounding.AwayFromZero), (byte)Math.Round((green + match) * 255, MidpointRounding.AwayFromZero), (byte)Math.Round((blue + match) * 255, MidpointRounding.AwayFromZero), hasAlpha ? ParseAlpha(channels[3]) : (byte)255);
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

    private static SvgViewBox? ParseViewBox(string? value, string? preserveAspectRatio)
    {
        if (value is null)
        {
            if (preserveAspectRatio is not null) ParsePreserveAspectRatio(preserveAspectRatio);
            return null;
        }
        string[] values = value.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length != 4 || !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) || !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) || !double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double width) || !double.TryParse(values[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double height) || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) throw new SvgException(SvgErrorCode.MalformedInput, "viewBox must contain four finite numbers with positive width and height.");
        (SvgViewBoxAlignment horizontalAlignment, SvgViewBoxAlignment verticalAlignment, SvgViewBoxFit fit) = ParsePreserveAspectRatio(preserveAspectRatio);
        return new SvgViewBox(x, y, width, height, horizontalAlignment, verticalAlignment, fit);
    }

    private static (SvgViewBoxAlignment HorizontalAlignment, SvgViewBoxAlignment VerticalAlignment, SvgViewBoxFit Fit) ParsePreserveAspectRatio(string? value)
    {
        if (value is null || value.Equals("xMidYMid meet", StringComparison.OrdinalIgnoreCase)) return (SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Mid, SvgViewBoxFit.Meet);
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) return (SvgViewBoxAlignment.None, SvgViewBoxAlignment.None, SvgViewBoxFit.None);
        if (value is not null)
        {
            int separator = value.LastIndexOf(' ');
            if (separator <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only meet, slice, and none preserveAspectRatio values are implemented.");
            SvgViewBoxFit fit = value[(separator + 1)..] switch { "meet" => SvgViewBoxFit.Meet, "slice" => SvgViewBoxFit.Slice, _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only meet, slice, and none preserveAspectRatio values are implemented.") };
            return value[..separator] switch
            {
                "xMinYMin" => (SvgViewBoxAlignment.Min, SvgViewBoxAlignment.Min, fit), "xMidYMin" => (SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Min, fit), "xMaxYMin" => (SvgViewBoxAlignment.Max, SvgViewBoxAlignment.Min, fit), "xMinYMid" => (SvgViewBoxAlignment.Min, SvgViewBoxAlignment.Mid, fit), "xMidYMid" => (SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Mid, fit), "xMaxYMid" => (SvgViewBoxAlignment.Max, SvgViewBoxAlignment.Mid, fit), "xMinYMax" => (SvgViewBoxAlignment.Min, SvgViewBoxAlignment.Max, fit), "xMidYMax" => (SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Max, fit), "xMaxYMax" => (SvgViewBoxAlignment.Max, SvgViewBoxAlignment.Max, fit),
                _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only meet, slice, and none preserveAspectRatio values are implemented.")
            };
        }
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only meet, slice, and none preserveAspectRatio values are implemented.");
    }

    private readonly record struct GroupScope(int Depth, SvgPaint Paint, bool IsDisplayed, bool IsVisible, AxisTransform Transform, bool IsolatesOpacity);

    private readonly record struct AxisTransform(int ScaleX, int ScaleY, int OffsetX, int OffsetY)
    {
        internal static AxisTransform Identity { get; } = new(1, 1, 0, 0);
        internal AxisTransform Compose(AxisTransform child) => new(checked(ScaleX * child.ScaleX), checked(ScaleY * child.ScaleY), checked(ScaleX * child.OffsetX + OffsetX), checked(ScaleY * child.OffsetY + OffsetY));
    }

    private readonly record struct SvgPaint(SvgColor Color, bool HasFill, SvgColor CurrentColor)
    {
        internal static SvgPaint Default { get; } = new(new SvgColor(0, 0, 0, 255), true, new SvgColor(0, 0, 0, 255));
    }

    private readonly record struct InlineStyle(string? Color, string? Fill, string? FillOpacity, string? Stroke, string? Display, string? Visibility, string? Opacity, string? FillRule);
}
