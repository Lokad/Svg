using System.Globalization;
using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Reads the bounded SVG root without materializing a DOM or input string.</summary>
internal static class SvgRootParser
{
    private const int GroupScopeStorageBytes = 64;
    private const int CssRuleStorageBytes = 96;
    private const int DefinitionStorageBytes = 160;
    private const int PendingUseStorageBytes = 64;
    private const int PointStorageBytes = 8;
    private const int IntegerStorageBytes = 4;
    private const int RectangleStorageBytes = 56;
    private const int MinimumXmlReaderWorkspaceBytes = 16 * 1024;

    private static readonly Dictionary<string, SvgColor> CssNamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aliceblue"] = new(240, 248, 255, 255),
        ["antiquewhite"] = new(250, 235, 215, 255),
        ["aqua"] = new(0, 255, 255, 255),
        ["aquamarine"] = new(127, 255, 212, 255),
        ["azure"] = new(240, 255, 255, 255),
        ["beige"] = new(245, 245, 220, 255),
        ["bisque"] = new(255, 228, 196, 255),
        ["black"] = new(0, 0, 0, 255),
        ["blanchedalmond"] = new(255, 235, 205, 255),
        ["blue"] = new(0, 0, 255, 255),
        ["blueviolet"] = new(138, 43, 226, 255),
        ["brown"] = new(165, 42, 42, 255),
        ["burlywood"] = new(222, 184, 135, 255),
        ["cadetblue"] = new(95, 158, 160, 255),
        ["chartreuse"] = new(127, 255, 0, 255),
        ["chocolate"] = new(210, 105, 30, 255),
        ["coral"] = new(255, 127, 80, 255),
        ["cornflowerblue"] = new(100, 149, 237, 255),
        ["cornsilk"] = new(255, 248, 220, 255),
        ["crimson"] = new(220, 20, 60, 255),
        ["cyan"] = new(0, 255, 255, 255),
        ["darkblue"] = new(0, 0, 139, 255),
        ["darkcyan"] = new(0, 139, 139, 255),
        ["darkgoldenrod"] = new(184, 134, 11, 255),
        ["darkgray"] = new(169, 169, 169, 255),
        ["darkgreen"] = new(0, 100, 0, 255),
        ["darkgrey"] = new(169, 169, 169, 255),
        ["darkkhaki"] = new(189, 183, 107, 255),
        ["darkmagenta"] = new(139, 0, 139, 255),
        ["darkolivegreen"] = new(85, 107, 47, 255),
        ["darkorange"] = new(255, 140, 0, 255),
        ["darkorchid"] = new(153, 50, 204, 255),
        ["darkred"] = new(139, 0, 0, 255),
        ["darksalmon"] = new(233, 150, 122, 255),
        ["darkseagreen"] = new(143, 188, 143, 255),
        ["darkslateblue"] = new(72, 61, 139, 255),
        ["darkslategray"] = new(47, 79, 79, 255),
        ["darkslategrey"] = new(47, 79, 79, 255),
        ["darkturquoise"] = new(0, 206, 209, 255),
        ["darkviolet"] = new(148, 0, 211, 255),
        ["deeppink"] = new(255, 20, 147, 255),
        ["deepskyblue"] = new(0, 191, 255, 255),
        ["dimgray"] = new(105, 105, 105, 255),
        ["dimgrey"] = new(105, 105, 105, 255),
        ["dodgerblue"] = new(30, 144, 255, 255),
        ["firebrick"] = new(178, 34, 34, 255),
        ["floralwhite"] = new(255, 250, 240, 255),
        ["forestgreen"] = new(34, 139, 34, 255),
        ["fuchsia"] = new(255, 0, 255, 255),
        ["gainsboro"] = new(220, 220, 220, 255),
        ["ghostwhite"] = new(248, 248, 255, 255),
        ["gold"] = new(255, 215, 0, 255),
        ["goldenrod"] = new(218, 165, 32, 255),
        ["gray"] = new(128, 128, 128, 255),
        ["green"] = new(0, 128, 0, 255),
        ["greenyellow"] = new(173, 255, 47, 255),
        ["grey"] = new(128, 128, 128, 255),
        ["honeydew"] = new(240, 255, 240, 255),
        ["hotpink"] = new(255, 105, 180, 255),
        ["indianred"] = new(205, 92, 92, 255),
        ["indigo"] = new(75, 0, 130, 255),
        ["ivory"] = new(255, 255, 240, 255),
        ["khaki"] = new(240, 230, 140, 255),
        ["lavender"] = new(230, 230, 250, 255),
        ["lavenderblush"] = new(255, 240, 245, 255),
        ["lawngreen"] = new(124, 252, 0, 255),
        ["lemonchiffon"] = new(255, 250, 205, 255),
        ["lightblue"] = new(173, 216, 230, 255),
        ["lightcoral"] = new(240, 128, 128, 255),
        ["lightcyan"] = new(224, 255, 255, 255),
        ["lightgoldenrodyellow"] = new(250, 250, 210, 255),
        ["lightgray"] = new(211, 211, 211, 255),
        ["lightgreen"] = new(144, 238, 144, 255),
        ["lightgrey"] = new(211, 211, 211, 255),
        ["lightpink"] = new(255, 182, 193, 255),
        ["lightsalmon"] = new(255, 160, 122, 255),
        ["lightseagreen"] = new(32, 178, 170, 255),
        ["lightskyblue"] = new(135, 206, 250, 255),
        ["lightslategray"] = new(119, 136, 153, 255),
        ["lightslategrey"] = new(119, 136, 153, 255),
        ["lightsteelblue"] = new(176, 196, 222, 255),
        ["lightyellow"] = new(255, 255, 224, 255),
        ["lime"] = new(0, 255, 0, 255),
        ["limegreen"] = new(50, 205, 50, 255),
        ["linen"] = new(250, 240, 230, 255),
        ["magenta"] = new(255, 0, 255, 255),
        ["maroon"] = new(128, 0, 0, 255),
        ["mediumaquamarine"] = new(102, 205, 170, 255),
        ["mediumblue"] = new(0, 0, 205, 255),
        ["mediumorchid"] = new(186, 85, 211, 255),
        ["mediumpurple"] = new(147, 112, 219, 255),
        ["mediumseagreen"] = new(60, 179, 113, 255),
        ["mediumslateblue"] = new(123, 104, 238, 255),
        ["mediumspringgreen"] = new(0, 250, 154, 255),
        ["mediumturquoise"] = new(72, 209, 204, 255),
        ["mediumvioletred"] = new(199, 21, 133, 255),
        ["midnightblue"] = new(25, 25, 112, 255),
        ["mintcream"] = new(245, 255, 250, 255),
        ["mistyrose"] = new(255, 228, 225, 255),
        ["moccasin"] = new(255, 228, 181, 255),
        ["navajowhite"] = new(255, 222, 173, 255),
        ["navy"] = new(0, 0, 128, 255),
        ["oldlace"] = new(253, 245, 230, 255),
        ["olive"] = new(128, 128, 0, 255),
        ["olivedrab"] = new(107, 142, 35, 255),
        ["orange"] = new(255, 165, 0, 255),
        ["orangered"] = new(255, 69, 0, 255),
        ["orchid"] = new(218, 112, 214, 255),
        ["palegoldenrod"] = new(238, 232, 170, 255),
        ["palegreen"] = new(152, 251, 152, 255),
        ["paleturquoise"] = new(175, 238, 238, 255),
        ["palevioletred"] = new(219, 112, 147, 255),
        ["papayawhip"] = new(255, 239, 213, 255),
        ["peachpuff"] = new(255, 218, 185, 255),
        ["peru"] = new(205, 133, 63, 255),
        ["pink"] = new(255, 192, 203, 255),
        ["plum"] = new(221, 160, 221, 255),
        ["powderblue"] = new(176, 224, 230, 255),
        ["purple"] = new(128, 0, 128, 255),
        ["red"] = new(255, 0, 0, 255),
        ["rosybrown"] = new(188, 143, 143, 255),
        ["royalblue"] = new(65, 105, 225, 255),
        ["saddlebrown"] = new(139, 69, 19, 255),
        ["salmon"] = new(250, 128, 114, 255),
        ["sandybrown"] = new(244, 164, 96, 255),
        ["seagreen"] = new(46, 139, 87, 255),
        ["seashell"] = new(255, 245, 238, 255),
        ["sienna"] = new(160, 82, 45, 255),
        ["silver"] = new(192, 192, 192, 255),
        ["skyblue"] = new(135, 206, 235, 255),
        ["slateblue"] = new(106, 90, 205, 255),
        ["slategray"] = new(112, 128, 144, 255),
        ["slategrey"] = new(112, 128, 144, 255),
        ["snow"] = new(255, 250, 250, 255),
        ["springgreen"] = new(0, 255, 127, 255),
        ["steelblue"] = new(70, 130, 180, 255),
        ["tan"] = new(210, 180, 140, 255),
        ["teal"] = new(0, 128, 128, 255),
        ["thistle"] = new(216, 191, 216, 255),
        ["tomato"] = new(255, 99, 71, 255),
        ["turquoise"] = new(64, 224, 208, 255),
        ["violet"] = new(238, 130, 238, 255),
        ["wheat"] = new(245, 222, 179, 255),
        ["white"] = new(255, 255, 255, 255),
        ["whitesmoke"] = new(245, 245, 245, 255),
        ["yellow"] = new(255, 255, 0, 255),
        ["yellowgreen"] = new(154, 205, 50, 255),
        ["rebeccapurple"] = new(102, 51, 153, 255),
    };
    internal static SvgRoot Parse(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, CancellationToken cancellationToken)
        => Parse(svg, limits, new RenderBudget(long.MaxValue), cancellationToken);

    internal static SvgRoot Parse(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, RenderBudget budget, CancellationToken cancellationToken)
    {
        try { return ParseCore(svg, limits, budget, cancellationToken); }
        catch (XmlException exception) { throw new SvgException(SvgErrorCode.MalformedInput, "SVG XML is malformed: " + exception.Message); }
    }

    private static SvgRoot ParseCore(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, RenderBudget budget, CancellationToken cancellationToken)
    {
        if (svg.Length > limits.MaximumInputBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG input exceeds the configured input limit.");
        using RenderBudget.BudgetReservation xmlReaderWorkspace = budget.Reserve(GetXmlReaderWorkspaceBytes(svg.Length), SvgMemoryCategory.Parser);
        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = limits.MaximumInputBytes, IgnoreComments = true, IgnoreProcessingInstructions = true };
        using ReadOnlyMemoryStream stream = new(svg);
        using XmlReader reader = XmlReader.Create(stream, settings);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element) continue;
            ValidateElementStructure(reader, limits);
            if (reader.LocalName != "svg" || reader.NamespaceURI != "http://www.w3.org/2000/svg") throw new SvgException(SvgErrorCode.MalformedInput, "The document root must be an SVG element in the SVG namespace.");
            InlineStyle rootStyle = ParseInlineStyle(reader.GetAttribute("style"));
            SvgPaint rootPaint = ParseRootPaint(reader, rootStyle);
            byte rootOpacity = ParseOpacity(GetProperty(reader, rootStyle.Opacity, "opacity"));
            bool rootVisible = ParseDisplay(GetProperty(reader, rootStyle.Display, "display"));
            bool rootVisibility = ParseVisibility(GetProperty(reader, rootStyle.Visibility, "visibility"), true);
            double? width = ParseLength(reader.GetAttribute("width"));
            double? height = ParseLength(reader.GetAttribute("height"));
            SvgViewBox? viewBox = ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio"));
            if (reader.IsEmptyElement) return CompleteRoot(new SvgRoot(width, height, viewBox, [], [], false, 0, null));
            int rootDepth = reader.Depth;
            using BudgetedSceneList<SvgRectangle> rectangles = new(budget, SvgSceneArrayKind.Rectangle);
            using BudgetedSceneList<SvgDrawCommand> commands = new(budget, SvgSceneArrayKind.DrawCommand);
            if (rootOpacity != 255) commands.Add(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, default, rootOpacity));
            using BudgetedParserList<GroupScope> groupDepths = new(budget, SvgMemoryCategory.Parser, GroupScopeStorageBytes);
            int nodeCount = 0;
            bool unsupported = false;
            bool sawGraphicNode = false;
            using BudgetedParserList<CssRule> rectangleStyleRules = new(budget, SvgMemoryCategory.Parser, CssRuleStorageBytes);
            bool hasRectangleStyleSheet = false;
            using BudgetedParserDictionary<string, SvgRectangle> definitions = new(budget, SvgMemoryCategory.Parser, DefinitionStorageBytes);
            using BudgetedParserList<PendingUse> pendingUses = new(budget, SvgMemoryCategory.Parser, PendingUseStorageBytes);
            int geometryElementCount = 0;
            ParserWorkCounter workCounter = new(limits.MaximumWorkUnits);
            int definitionsDepth = -1;
            int symbolDepth = -1;
            string? symbolId = null;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.Element)
                {
                    ValidateElementStructure(reader, limits);
                    if (nodeCount >= limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                    nodeCount++;
                    bool directChild = reader.Depth == rootDepth + 1;
                    bool nestedInGroup = groupDepths.Count > 0;
                    if (definitionsDepth >= 0 && (reader.Depth == definitionsDepth + 1 || reader.Depth == symbolDepth + 1) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "rect")
                    {
                        string? id = symbolDepth >= 0 ? symbolId : reader.GetAttribute("id");
                        SvgRectangle rectangle = ParseRectangle(reader, rootPaint, rootVisible, rootVisibility, AxisTransform.Identity, GetStyleSheet(reader, rectangleStyleRules, directChild, groupDepths, workCounter, cancellationToken));
                        if (id is not null && !definitions.TryAdd(id, rectangle)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
                    }
                    else if (definitionsDepth >= 0 && reader.Depth == definitionsDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "symbol")
                    {
                        if (reader.HasAttributes) { while (reader.MoveToNextAttribute()) { if (reader.NamespaceURI.Length == 0 && reader.LocalName == "id") continue; throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol attribute '" + reader.Name + "' is not implemented."); } reader.MoveToElement(); }
                        symbolId = reader.GetAttribute("id") ?? throw new SvgException(SvgErrorCode.MalformedInput, "A symbol requires an id.");
                        if (!reader.IsEmptyElement) symbolDepth = reader.Depth;
                    }
                    else if (definitionsDepth < 0 && directChild && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "defs")
                    {
                        if (!reader.IsEmptyElement) definitionsDepth = reader.Depth;
                    }
                    else if (definitionsDepth < 0 && directChild && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "use")
                    {
                        UseReference reference = ParseUse(reader);
                        if (definitions.TryGetValue(reference.Id, out SvgRectangle definition))
                        {
                            SvgRectangle rectangle = ApplyUseOffset(definition, reference);
                            rectangles.Add(rectangle); commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Rectangle, rectangle, 255));
                        }
                        else
                        {
                            int rectangleIndex = rectangles.Count; int commandIndex = commands.Count;
                            rectangles.Add(default); commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Rectangle, default, 255));
                            pendingUses.Add(new PendingUse(rectangleIndex, commandIndex, reference));
                        }
                        unsupported |= !reader.IsEmptyElement;
                        sawGraphicNode = true;
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "rect")
                    {
                        AxisTransform transform = groupDepths.Count == 0 ? AxisTransform.Identity : groupDepths[^1].Transform;
                        SvgRectangle rectangle = ParseRectangle(reader, groupDepths.Count == 0 ? rootPaint : groupDepths[^1].Paint, groupDepths.Count == 0 ? rootVisible : groupDepths[^1].IsDisplayed, groupDepths.Count == 0 ? rootVisibility : groupDepths[^1].IsVisible, transform, GetStyleSheet(reader, rectangleStyleRules, directChild, groupDepths, workCounter, cancellationToken));
                        rectangles.Add(rectangle); commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Rectangle, rectangle, 255));
                        unsupported |= !reader.IsEmptyElement;
                        sawGraphicNode = true;
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "line")
                    {
                        AxisTransform transform = groupDepths.Count == 0 ? AxisTransform.Identity : groupDepths[^1].Transform;
                        SvgRectangle line = ParseLine(reader, groupDepths.Count == 0 ? rootPaint : groupDepths[^1].Paint, groupDepths.Count == 0 ? rootVisible : groupDepths[^1].IsDisplayed, groupDepths.Count == 0 ? rootVisibility : groupDepths[^1].IsVisible, transform);
                        rectangles.Add(line); commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Rectangle, line, 255));
                        unsupported |= !reader.IsEmptyElement;
                        sawGraphicNode = true;
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "path")
                    {
                        AxisTransform transform = groupDepths.Count == 0 ? AxisTransform.Identity : groupDepths[^1].Transform;
                        using (ParsedGeometry path = ParseOrthogonalPath(reader, groupDepths.Count == 0 ? rootPaint : groupDepths[^1].Paint, groupDepths.Count == 0 ? rootVisible : groupDepths[^1].IsDisplayed, groupDepths.Count == 0 ? rootVisibility : groupDepths[^1].IsVisible, transform, GetStyleSheet(reader, rectangleStyleRules, directChild, groupDepths, workCounter, cancellationToken), limits.MaximumPathCommands - geometryElementCount, budget, workCounter, cancellationToken))
                        {
                            geometryElementCount += path.ElementCount;
                            rectangles.AddRange(path.Rectangles); foreach (SvgRectangle rectangle in path.Rectangles) commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Rectangle, rectangle, 255));
                        }
                        unsupported |= !reader.IsEmptyElement;
                        sawGraphicNode = true;
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName is "polygon" or "polyline")
                    {
                        AxisTransform transform = groupDepths.Count == 0 ? AxisTransform.Identity : groupDepths[^1].Transform;
                        using (ParsedGeometry pointList = ParseOrthogonalPointList(reader, reader.LocalName, groupDepths.Count == 0 ? rootPaint : groupDepths[^1].Paint, groupDepths.Count == 0 ? rootVisible : groupDepths[^1].IsDisplayed, groupDepths.Count == 0 ? rootVisibility : groupDepths[^1].IsVisible, transform, GetStyleSheet(reader, rectangleStyleRules, directChild, groupDepths, workCounter, cancellationToken), limits.MaximumPathCommands - geometryElementCount, budget, workCounter, cancellationToken))
                        {
                            geometryElementCount += pointList.ElementCount;
                            rectangles.AddRange(pointList.Rectangles); foreach (SvgRectangle rectangle in pointList.Rectangles) commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Rectangle, rectangle, 255));
                        }
                        unsupported |= !reader.IsEmptyElement;
                        sawGraphicNode = true;
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && (reader.LocalName is "g" or "a"))
                    {
                        InlineStyle style = ParseInlineStyle(reader.GetAttribute("style"));
                        InlineStyle styleSheet = GetStyleSheet(reader, rectangleStyleRules, directChild, groupDepths, workCounter, cancellationToken);
                        SvgPaint paint = ParseGroupPaint(reader, groupDepths.Count == 0 ? rootPaint : groupDepths[^1].Paint, style, styleSheet);
                        bool displayed = (groupDepths.Count == 0 ? rootVisible : groupDepths[^1].IsDisplayed) && ParseDisplay(GetProperty(reader, style.Display, styleSheet.Display, "display"));
                        bool visibility = ParseVisibility(GetProperty(reader, style.Visibility, styleSheet.Visibility, "visibility"), groupDepths.Count == 0 ? rootVisibility : groupDepths[^1].IsVisible);
                        AxisTransform transform = groupDepths.Count == 0 ? ParseTransform(reader.GetAttribute("transform")) : groupDepths[^1].Transform.Compose(ParseTransform(reader.GetAttribute("transform")));
                        byte opacity = ParseOpacity(GetProperty(reader, style.Opacity, styleSheet.Opacity, "opacity"));
                        bool isolatesOpacity = opacity != 255;
                        if (isolatesOpacity && (rootOpacity != 255 || groupDepths.Any(static group => group.IsolatesOpacity))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Nested group opacity requires separate compositing calibration.");
                        if (!reader.IsEmptyElement) { if (isolatesOpacity) commands.Add(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, default, opacity)); groupDepths.Add(new GroupScope(reader.Depth, reader.LocalName, paint, displayed, visibility, transform, isolatesOpacity)); }
                    }
                    else if (directChild && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "style")
                    {
                        if (sawGraphicNode || hasRectangleStyleSheet) throw new SvgException(SvgErrorCode.UnsupportedFeature, "A single root-level rectangle stylesheet rule must precede rendered content.");
                        if (reader.HasAttributes) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet attributes are not implemented.");
                        ParseRectangleStyleSheet(ReadStyleElementText(reader), limits.MaximumNodes, rectangleStyleRules, workCounter, cancellationToken);
                        hasRectangleStyleSheet = true;
                    }
                    else if (directChild && (reader.LocalName is "title" or "desc" or "metadata" or "defs")) { }
                    else if (directChild || nestedInGroup) unsupported = true;
                }
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.Depth == definitionsDepth) definitionsDepth = -1;
                    if (reader.Depth == symbolDepth) { symbolDepth = -1; symbolId = null; }
                    if (groupDepths.Count > 0 && groupDepths[^1].Depth == reader.Depth) { GroupScope group = groupDepths[^1]; groupDepths.RemoveLast(); if (group.IsolatesOpacity) commands.Add(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, default, 255)); }
                    if (reader.Depth == rootDepth)
                    {
                        for (int pendingUseIndex = 0; pendingUseIndex < pendingUses.Count; pendingUseIndex++)
                        {
                            PendingUse pendingUse = pendingUses[pendingUseIndex];
                            if (!definitions.TryGetValue(pendingUse.Reference.Id, out SvgRectangle definition)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local rectangle use references are implemented.");
                            SvgRectangle rectangle = ApplyUseOffset(definition, pendingUse.Reference);
                            rectangles[pendingUse.RectangleIndex] = rectangle; commands[pendingUse.CommandIndex] = new SvgDrawCommand(SvgDrawCommandKind.Rectangle, rectangle, 255);
                        }
                        if (rootOpacity != 255) commands.Add(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, default, 255));
                        SvgSceneStorage scene = SvgSceneStorage.Create(rectangles, commands);
                        try { return CompleteRoot(new SvgRoot(width, height, viewBox, scene.Rectangles, scene.Commands, unsupported, workCounter.Used, scene)); }
                        catch { scene.Dispose(); throw; }
                    }
                }
            }
            throw new SvgException(SvgErrorCode.MalformedInput, "SVG root element is not closed.");
        }
        throw new SvgException(SvgErrorCode.MalformedInput, "SVG input has no root element.");

        SvgRoot CompleteRoot(SvgRoot root)
        {
            try
            {
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (reader.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace) continue;
                    throw new SvgException(SvgErrorCode.MalformedInput, "SVG input has content after its root element.");
                }
                return root;
            }
            catch { root.Dispose(); throw; }
        }
    }

    private static void ValidateElementStructure(XmlReader reader, SvgRenderLimits limits)
    {
        if (reader.Depth >= limits.MaximumXmlDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG XML nesting exceeds the configured limit.");
        if (reader.AttributeCount > limits.MaximumAttributesPerElement) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG element attributes exceed the configured limit.");
    }

    private static long GetXmlReaderWorkspaceBytes(int inputBytes) => Math.Max(MinimumXmlReaderWorkspaceBytes, checked((long)inputBytes * 2));

    private static SvgPaint ParseGroupPaint(XmlReader reader, SvgPaint inheritedPaint, InlineStyle style, InlineStyle styleSheet)
    {
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns" || reader.Name == "xmlns") continue;
                if ((reader.NamespaceURI.Length == 0 && reader.LocalName is "id" or "class" or "href" or "color" or "fill" or "fill-opacity" or "stroke" or "opacity" or "display" or "visibility" or "style" or "transform") || (reader.NamespaceURI == "http://www.w3.org/1999/xlink" && reader.LocalName == "href")) continue;
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Group attribute '" + reader.Name + "' is not implemented.");
            }
            reader.MoveToElement();
        }
        if (style.FillRule is not null || styleSheet.FillRule is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill rules are only implemented on shapes.");
        ValidateStroke(GetProperty(reader, style.Stroke, styleSheet.Stroke, "stroke"));
        return ParsePaint(GetProperty(reader, style.Fill, styleSheet.Fill, "fill"), GetProperty(reader, style.FillOpacity, styleSheet.FillOpacity, "fill-opacity"), GetProperty(reader, style.Color, styleSheet.Color, "color"), inheritedPaint);
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
                if (reader.NamespaceURI.Length == 0 && reader.LocalName is "id" or "class" or "x" or "y" or "width" or "height" or "rx" or "ry" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "stroke-width" or "opacity" or "display" or "visibility" or "style" or "transform") continue;
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
        byte opacity = ParseOpacity(GetProperty(reader, style.Opacity, styleSheet.Opacity, "opacity"));
        SvgColor color = ResolvePaintColor(paint, opacity);
        SvgStroke? stroke = ParseRectangleStroke(GetProperty(reader, style.Stroke, styleSheet.Stroke, "stroke"), reader.GetAttribute("stroke-width"), GetProperty(reader, style.Color, styleSheet.Color, "color"), inheritedPaint, transform, x, y, width, height, opacity, paint.HasFill);
        bool isPainted = inheritedDisplayed && ParseDisplay(GetProperty(reader, style.Display, styleSheet.Display, "display")) && ParseVisibility(GetProperty(reader, style.Visibility, styleSheet.Visibility, "visibility"), inheritedVisibility);
        return new SvgRectangle(x, y, width, height, color, paint.HasFill && isPainted, isPainted ? stroke : null);
    }

    private static SvgStroke? ParseRectangleStroke(string? value, string? widthValue, string? currentColor, SvgPaint inheritedPaint, AxisTransform transform, double x, double y, double width, double height, byte opacity, bool hasFill)
    {
        if (value is null || value.Equals("none", StringComparison.OrdinalIgnoreCase)) { if (widthValue is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stroke width without a stroke is not implemented."); return null; }
        if (transform != AxisTransform.Identity || x != Math.Truncate(x) || y != Math.Truncate(y) || width != Math.Truncate(width) || height != Math.Truncate(height) || (hasFill && opacity != 255)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only untransformed integer rectangle strokes, with opacity only when fill-less, are implemented.");
        SvgColor color = ParseStrokeColor(value, currentColor, inheritedPaint);
        int strokeWidth = ParseInteger(widthValue ?? "1");
        if (color.Alpha != 255 || strokeWidth <= 0 || strokeWidth % 2 != 0 || strokeWidth > width || strokeWidth > height) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only opaque even-width rectangle strokes no larger than the rectangle dimensions are implemented.");
        return new SvgStroke(new SvgColor(color.Red, color.Green, color.Blue, MultiplyAlpha(color.Alpha, opacity)), strokeWidth);
    }

    private static SvgRectangle ParseLine(XmlReader reader, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisibility, AxisTransform inheritedTransform)
    {
        InlineStyle style = ParseInlineStyle(reader.GetAttribute("style"));
        if (reader.HasAttributes) { while (reader.MoveToNextAttribute()) { if (reader.NamespaceURI.Length == 0 && reader.LocalName is "id" or "class" or "x1" or "y1" or "x2" or "y2" or "color" or "stroke" or "stroke-width" or "opacity" or "display" or "visibility" or "style" or "transform") continue; throw new SvgException(SvgErrorCode.UnsupportedFeature, "Line attribute '" + reader.Name + "' is not implemented."); } reader.MoveToElement(); }
        AxisTransform transform = inheritedTransform.Compose(ParseTransform(reader.GetAttribute("transform")));
        int x1 = ParseInteger(reader.GetAttribute("x1") ?? "0"); int y1 = ParseInteger(reader.GetAttribute("y1") ?? "0"); int x2 = ParseInteger(reader.GetAttribute("x2") ?? "0"); int y2 = ParseInteger(reader.GetAttribute("y2") ?? "0");
        string? stroke = GetProperty(reader, style.Stroke, "stroke");
        byte opacity = ParseOpacity(GetProperty(reader, style.Opacity, "opacity"));
        if (transform != AxisTransform.Identity || stroke is null || stroke.Equals("none", StringComparison.OrdinalIgnoreCase) || opacity != 255 || (x1 != x2 && y1 != y2) || (x1 == x2 && y1 == y2)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only opaque untransformed horizontal or vertical lines are implemented.");
        SvgColor strokeColor = ParseStrokeColor(stroke, GetProperty(reader, style.Color, "color"), inheritedPaint);
        int strokeWidth = ParseInteger(reader.GetAttribute("stroke-width") ?? "1");
        if (strokeColor.Alpha != 255 || strokeWidth <= 0 || strokeWidth % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only opaque even-width line strokes are implemented.");
        double halfWidth = strokeWidth / 2d;
        bool painted = inheritedDisplayed && ParseDisplay(GetProperty(reader, style.Display, "display")) && ParseVisibility(GetProperty(reader, style.Visibility, "visibility"), inheritedVisibility);
        SvgColor color = new(strokeColor.Red, strokeColor.Green, strokeColor.Blue, MultiplyAlpha(strokeColor.Alpha, opacity));
        return x1 != x2 ? new SvgRectangle(Math.Min(x1, x2), y1 - halfWidth, Math.Abs((long)x2 - x1), strokeWidth, color, painted, null) : new SvgRectangle(x1 - halfWidth, Math.Min(y1, y2), strokeWidth, Math.Abs((long)y2 - y1), color, painted, null);
    }

    private static UseReference ParseUse(XmlReader reader)
    {
        InlineStyle style = ParseInlineStyle(reader.GetAttribute("style"));
        if (style.Color is not null || style.Fill is not null || style.FillOpacity is not null || style.Stroke is not null || style.FillRule is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Paint styles on use elements are not implemented.");
        if (reader.HasAttributes) { while (reader.MoveToNextAttribute()) { if ((reader.NamespaceURI.Length == 0 && reader.LocalName is "href" or "x" or "y" or "transform" or "opacity" or "display" or "visibility" or "style") || (reader.NamespaceURI == "http://www.w3.org/1999/xlink" && reader.LocalName == "href")) continue; throw new SvgException(SvgErrorCode.UnsupportedFeature, "Use attribute '" + reader.Name + "' is not implemented."); } reader.MoveToElement(); }
        string href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink") ?? throw new SvgException(SvgErrorCode.MalformedInput, "A use element requires an href.");
        if (href.Length < 2 || href[0] != '#') throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local rectangle use references are implemented.");
        double x = ParseRectangleCoordinate(reader.GetAttribute("x") ?? "0"); double y = ParseRectangleCoordinate(reader.GetAttribute("y") ?? "0");
        return new UseReference(href[1..], x, y, ParseTransform(reader.GetAttribute("transform")), ParseOpacity(GetProperty(reader, style.Opacity, "opacity")), ParseDisplay(GetProperty(reader, style.Display, "display")) && ParseVisibility(GetProperty(reader, style.Visibility, "visibility"), true));
    }

    private static SvgRectangle ApplyUseOffset(SvgRectangle rectangle, UseReference reference)
    {
        if (rectangle.Stroke is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle use references with strokes are not implemented.");
        return rectangle with { X = reference.Transform.ScaleX * (rectangle.X + reference.X) + reference.Transform.OffsetX, Y = reference.Transform.ScaleY * (rectangle.Y + reference.Y) + reference.Transform.OffsetY, Width = reference.Transform.ScaleX * rectangle.Width, Height = reference.Transform.ScaleY * rectangle.Height, Color = new SvgColor(rectangle.Color.Red, rectangle.Color.Green, rectangle.Color.Blue, MultiplyAlpha(rectangle.Color.Alpha, reference.Opacity)), HasFill = rectangle.HasFill && reference.IsPainted };
    }

    private static ParsedGeometry ParseOrthogonalPath(XmlReader reader, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisibility, AxisTransform inheritedTransform, InlineStyle styleSheet, int maximumGeometryElements, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (reader.HasAttributes) { while (reader.MoveToNextAttribute()) { if (reader.NamespaceURI.Length == 0 && reader.LocalName is "id" or "d" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "opacity" or "display" or "visibility" or "style" or "transform") continue; throw new SvgException(SvgErrorCode.UnsupportedFeature, "Path attribute '" + reader.Name + "' is not implemented."); } reader.MoveToElement(); }
        string d = reader.GetAttribute("d") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Path data is required.");
        ValidatePathCommandCount(d, maximumGeometryElements, cancellationToken);
        PathTokenReader tokens = new(d);
        if (tokens.End || tokens.ReadCommand() != 'M') throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented.");
        using BudgetedParserList<SvgPoint> points = new(budget, SvgMemoryCategory.Geometry, PointStorageBytes);
        points.Add(new SvgPoint(tokens.ReadInteger(), tokens.ReadInteger()));
        bool closed = false;
        int commandIndex = 0;
        while (!tokens.End)
        {
            if ((commandIndex++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            char command = tokens.ReadCommand();
            SvgPoint current = points[^1];
            if (command == 'H') points.Add(new SvgPoint(tokens.ReadInteger(), current.Y));
            else if (command == 'V') points.Add(new SvgPoint(current.X, tokens.ReadInteger()));
            else if (command == 'L') points.Add(new SvgPoint(tokens.ReadInteger(), tokens.ReadInteger()));
            else if (command is 'Z' or 'z') { closed = tokens.End; break; }
            else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented.");
        }
        if (!closed || points.Count < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented.");
        if (points.Count > maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, "Path command count exceeds the configured limit.");
        for (int pointIndex = 0; pointIndex < points.Count; pointIndex++) { SvgPoint start = points[pointIndex]; SvgPoint end = points[(pointIndex + 1) % points.Count]; if ((start.X == end.X) == (start.Y == end.Y)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented."); }
        ValidateSimpleOrthogonalContour(points, "path", workCounter, cancellationToken);
        InlineStyle style = ParseInlineStyle(reader.GetAttribute("style")); AxisTransform transform = inheritedTransform.Compose(ParseTransform(reader.GetAttribute("transform")));
        SvgPaint paint = ParsePaint(GetProperty(reader, style.Fill, styleSheet.Fill, "fill"), GetProperty(reader, style.FillOpacity, styleSheet.FillOpacity, "fill-opacity"), GetProperty(reader, style.Color, styleSheet.Color, "color"), inheritedPaint); ValidateFillRule(GetProperty(reader, style.FillRule, styleSheet.FillRule, "fill-rule")); ValidateStroke(GetProperty(reader, style.Stroke, styleSheet.Stroke, "stroke"));
        byte opacity = ParseOpacity(GetProperty(reader, style.Opacity, "opacity")); SvgColor color = ResolvePaintColor(paint, opacity);
        bool hasFill = paint.HasFill && inheritedDisplayed && ParseDisplay(GetProperty(reader, style.Display, styleSheet.Display, "display")) && ParseVisibility(GetProperty(reader, style.Visibility, styleSheet.Visibility, "visibility"), inheritedVisibility);
        int geometryElementCount = points.Count;
        using BudgetedParserList<int> boundaries = new(budget, SvgMemoryCategory.Geometry, IntegerStorageBytes);
        for (int pointIndex = 0; pointIndex < points.Count; pointIndex++) boundaries.Add(points[pointIndex].Y);
        boundaries.Sort();
        using BudgetedParserList<int> intersections = new(budget, SvgMemoryCategory.Geometry, IntegerStorageBytes);
        using BudgetedParserList<SvgRectangle> rectangles = new(budget, SvgMemoryCategory.Geometry, RectangleStorageBytes);
        for (int boundaryIndex = 0; boundaryIndex < boundaries.Count;)
        {
            int top = boundaries[boundaryIndex];
            while (boundaryIndex < boundaries.Count && boundaries[boundaryIndex] == top) boundaryIndex++;
            if (boundaryIndex == boundaries.Count) break;
            int bottom = boundaries[boundaryIndex];
            intersections.Clear();
            for (int pointIndex = 0; pointIndex < points.Count; pointIndex++) { workCounter.Consume(cancellationToken); SvgPoint start = points[pointIndex]; SvgPoint end = points[(pointIndex + 1) % points.Count]; if (start.X == end.X && Math.Min(start.Y, end.Y) <= top && Math.Max(start.Y, end.Y) >= bottom) intersections.Add(start.X); }
            intersections.Sort();
            if (intersections.Count % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only simple closed absolute axis-aligned integer paths are implemented.");
            for (int intersectionIndex = 0; intersectionIndex < intersections.Count; intersectionIndex += 2) { int left = intersections[intersectionIndex]; int right = intersections[intersectionIndex + 1]; if (right <= left) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only simple closed absolute axis-aligned integer paths are implemented."); if (geometryElementCount >= maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, "Path rectangle-band count exceeds the configured path-command limit."); rectangles.Add(new SvgRectangle(ApplyTransform(transform.ScaleX, left, transform.OffsetX), ApplyTransform(transform.ScaleY, top, transform.OffsetY), ApplyTransformScale(transform.ScaleX, (long)right - left), ApplyTransformScale(transform.ScaleY, (long)bottom - top), color, hasFill, null)); geometryElementCount++; }
        }
        return new ParsedGeometry(rectangles.Detach(), geometryElementCount);
    }

    private static ParsedGeometry ParseOrthogonalPointList(XmlReader reader, string elementName, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisibility, AxisTransform inheritedTransform, InlineStyle styleSheet, int maximumGeometryElements, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (reader.HasAttributes) { while (reader.MoveToNextAttribute()) { if (reader.NamespaceURI.Length == 0 && reader.LocalName is "id" or "points" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "opacity" or "display" or "visibility" or "style" or "transform") continue; throw new SvgException(SvgErrorCode.UnsupportedFeature, elementName + " attribute '" + reader.Name + "' is not implemented."); } reader.MoveToElement(); }
        string pointsValue = reader.GetAttribute("points") ?? throw new SvgException(SvgErrorCode.MalformedInput, elementName + " points are required.");
        ValidatePointListVertexCount(pointsValue, maximumGeometryElements, cancellationToken);
        using BudgetedParserList<SvgPoint> points = new(budget, SvgMemoryCategory.Geometry, PointStorageBytes);
        DelimitedTokenReader values = new(pointsValue);
        int vertexCount = 0;
        while (values.TryReadValue(out ReadOnlySpan<char> x))
        {
            if ((vertexCount & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (!values.TryReadValue(out ReadOnlySpan<char> y)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer " + elementName + " elements are implemented.");
            if (vertexCount >= maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, elementName + " vertex count exceeds the configured path-command limit.");
            points.Add(new SvgPoint(ParseInteger(x), ParseInteger(y)));
            vertexCount++;
        }
        if (points.Count < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer " + elementName + " elements are implemented.");
        for (int index = 0; index < points.Count; index++)
        {
            SvgPoint start = points[index]; SvgPoint end = points[(index + 1) % points.Count];
            if ((start.X == end.X) == (start.Y == end.Y)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed axis-aligned integer " + elementName + " elements are implemented.");
        }
        ValidateSimpleOrthogonalContour(points, elementName, workCounter, cancellationToken);
        int geometryElementCount = points.Count;
        using BudgetedParserList<int> boundaries = new(budget, SvgMemoryCategory.Geometry, IntegerStorageBytes);
        for (int pointIndex = 0; pointIndex < points.Count; pointIndex++) boundaries.Add(points[pointIndex].Y);
        boundaries.Sort();
        using BudgetedParserList<int> intersections = new(budget, SvgMemoryCategory.Geometry, IntegerStorageBytes);
        using BudgetedParserList<SvgRectangle> rectangles = new(budget, SvgMemoryCategory.Geometry, RectangleStorageBytes);
        InlineStyle style = ParseInlineStyle(reader.GetAttribute("style")); AxisTransform transform = inheritedTransform.Compose(ParseTransform(reader.GetAttribute("transform")));
        SvgPaint paint = ParsePaint(GetProperty(reader, style.Fill, styleSheet.Fill, "fill"), GetProperty(reader, style.FillOpacity, styleSheet.FillOpacity, "fill-opacity"), GetProperty(reader, style.Color, styleSheet.Color, "color"), inheritedPaint); ValidateFillRule(GetProperty(reader, style.FillRule, styleSheet.FillRule, "fill-rule")); ValidateStroke(GetProperty(reader, style.Stroke, styleSheet.Stroke, "stroke"));
        byte opacity = ParseOpacity(GetProperty(reader, style.Opacity, styleSheet.Opacity, "opacity")); SvgColor color = ResolvePaintColor(paint, opacity); bool hasFill = paint.HasFill && inheritedDisplayed && ParseDisplay(GetProperty(reader, style.Display, styleSheet.Display, "display")) && ParseVisibility(GetProperty(reader, style.Visibility, styleSheet.Visibility, "visibility"), inheritedVisibility);
        for (int boundaryIndex = 0; boundaryIndex < boundaries.Count;)
        {
            int top = boundaries[boundaryIndex];
            while (boundaryIndex < boundaries.Count && boundaries[boundaryIndex] == top) boundaryIndex++;
            if (boundaryIndex == boundaries.Count) break;
            int bottom = boundaries[boundaryIndex];
            intersections.Clear();
            for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
            {
                workCounter.Consume(cancellationToken);
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
                if (geometryElementCount >= maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, elementName + " rectangle-band count exceeds the configured path-command limit.");
                rectangles.Add(new SvgRectangle(ApplyTransform(transform.ScaleX, left, transform.OffsetX), ApplyTransform(transform.ScaleY, top, transform.OffsetY), ApplyTransformScale(transform.ScaleX, (long)right - left), ApplyTransformScale(transform.ScaleY, (long)bottom - top), color, hasFill, null));
                geometryElementCount++;
            }
        }
        return new ParsedGeometry(rectangles.Detach(), geometryElementCount);
    }

    private static void ValidateSimpleOrthogonalContour(BudgetedParserList<SvgPoint> points, string elementName, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        for (int firstIndex = 0; firstIndex < points.Count; firstIndex++)
        {
            SvgPoint firstStart = points[firstIndex]; SvgPoint firstEnd = points[(firstIndex + 1) % points.Count];
            bool firstHorizontal = firstStart.Y == firstEnd.Y;
            for (int secondIndex = firstIndex + 1; secondIndex < points.Count; secondIndex++)
            {
                workCounter.Consume(cancellationToken);
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

    private static void ValidatePathCommandCount(string value, int maximumGeometryElements, CancellationToken cancellationToken)
    {
        int commandCount = 0;
        for (int index = 0; index < value.Length; index++)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (value[index] is not ('M' or 'L' or 'H' or 'V' or 'Z' or 'z')) continue;
            if (commandCount >= maximumGeometryElements) throw new SvgException(SvgErrorCode.LimitExceeded, "Path command count exceeds the configured limit.");
            commandCount++;
        }
    }

    private static void ValidatePointListVertexCount(string value, int maximumGeometryElements, CancellationToken cancellationToken)
    {
        long maximumValues = (long)maximumGeometryElements * 2;
        int valueCount = 0;
        bool withinValue = false;
        for (int index = 0; index < value.Length; index++)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (value[index] is ',' or ' ' or '\t' or '\r' or '\n') { withinValue = false; continue; }
            if (withinValue) continue;
            if (valueCount >= maximumValues) throw new SvgException(SvgErrorCode.LimitExceeded, "Point-list vertex count exceeds the configured limit.");
            valueCount++;
            withinValue = true;
        }
    }

    private static bool IsPathInteger(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return false;
        int start = value[0] == '-' ? 1 : 0;
        if (start == value.Length) return false;
        for (int index = start; index < value.Length; index++) if (!char.IsAsciiDigit(value[index])) return false;
        return true;
    }

    private ref struct PathTokenReader
    {
        private ReadOnlySpan<char> _value;
        private int _position;

        internal PathTokenReader(ReadOnlySpan<char> value) => _value = value;

        internal bool End
        {
            get
            {
                SkipSeparators();
                return _position == _value.Length;
            }
        }

        internal char ReadCommand()
        {
            SkipSeparators();
            if (_position == _value.Length) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented.");
            return _value[_position++];
        }

        internal ReadOnlySpan<char> ReadValue()
        {
            SkipSeparators();
            int start = _position;
            while (_position < _value.Length && !IsPathSeparator(_value[_position]) && !IsPathCommand(_value[_position])) _position++;
            if (start == _position) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented.");
            return _value[start.._position];
        }

        internal int ReadInteger()
        {
            ReadOnlySpan<char> value = ReadValue();
            if (!IsPathInteger(value)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only closed absolute axis-aligned integer paths are implemented.");
            return ParseInteger(value);
        }

        private void SkipSeparators()
        {
            while (_position < _value.Length && IsPathSeparator(_value[_position])) _position++;
        }
    }

    private ref struct DelimitedTokenReader
    {
        private ReadOnlySpan<char> _value;
        private int _position;

        internal DelimitedTokenReader(ReadOnlySpan<char> value) => _value = value;

        internal bool TryReadValue(out ReadOnlySpan<char> value)
        {
            while (_position < _value.Length && IsPointSeparator(_value[_position])) _position++;
            if (_position == _value.Length)
            {
                value = default;
                return false;
            }

            int start = _position;
            while (_position < _value.Length && !IsPointSeparator(_value[_position])) _position++;
            value = _value[start.._position];
            return true;
        }
    }

    private static bool IsPathCommand(char value) => value is 'M' or 'L' or 'H' or 'V' or 'Z' or 'z';

    private static bool IsPathSeparator(char value) => value == ',' || char.IsWhiteSpace(value);

    private static bool IsPointSeparator(char value) => value is ',' or ' ' or '\t' or '\r' or '\n';

    private static int ApplyTransform(int scale, int value, int offset)
    {
        try { return checked(scale * value + offset); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported integer transform exceeds the coordinate range."); }
    }

    private static int ApplyTransformScale(int scale, int value)
    {
        try { return checked(scale * value); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported integer transform exceeds the coordinate range."); }
    }

    private static double ApplyTransformScale(int scale, long value)
    {
        try { return checked((long)scale * value); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported integer transform exceeds the coordinate range."); }
    }

    private static string? GetProperty(XmlReader reader, string? inlineValue, string name) => inlineValue ?? reader.GetAttribute(name);

    private static string? GetProperty(XmlReader reader, string? inlineValue, string? styleSheetValue, string name) => inlineValue ?? styleSheetValue ?? reader.GetAttribute(name);

    private static AxisTransform ParseTransform(string? value)
    {
        if (value is null) return AxisTransform.Identity;
        ReadOnlySpan<char> input = value.AsSpan();
        AxisTransform transform = AxisTransform.Identity;
        int position = 0;
        bool hasFunction = false;
        while (true)
        {
            while (position < input.Length && char.IsWhiteSpace(input[position])) position++;
            if (position == input.Length)
            {
                if (!hasFunction) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer translate(), positive scale(), and axis-aligned matrix() transforms are implemented.");
                return transform;
            }

            int nameStart = position;
            while (position < input.Length && char.IsAsciiLetter(input[position])) position++;
            if (nameStart == position) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer translate(), positive scale(), and axis-aligned matrix() transforms are implemented.");
            ReadOnlySpan<char> name = input[nameStart..position];
            while (position < input.Length && char.IsWhiteSpace(input[position])) position++;
            if (position == input.Length || input[position] != '(') throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer translate(), positive scale(), and axis-aligned matrix() transforms are implemented.");
            position++;
            int argumentsStart = position;
            while (position < input.Length && input[position] != ')') position++;
            if (position == input.Length) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer translate(), positive scale(), and axis-aligned matrix() transforms are implemented.");
            transform = transform.Compose(ParseTransformFunction(name, input[argumentsStart..position]));
            position++;
            hasFunction = true;
        }
    }

    private static AxisTransform ParseTransformFunction(ReadOnlySpan<char> name, ReadOnlySpan<char> arguments)
    {
        bool isMatrix = name.Equals("matrix", StringComparison.OrdinalIgnoreCase);
        bool isTranslate = name.Equals("translate", StringComparison.OrdinalIgnoreCase);
        if (!isMatrix && !isTranslate && !name.Equals("scale", StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer translate(), positive scale(), and axis-aligned matrix() transforms are implemented.");
        Span<int> values = stackalloc int[6];
        int valueCount = 0;
        DelimitedTokenReader tokens = new(arguments);
        while (tokens.TryReadValue(out ReadOnlySpan<char> value))
        {
            if (valueCount == values.Length) throw new SvgException(SvgErrorCode.MalformedInput, isMatrix ? "matrix() requires six coordinates." : "Transforms require one or two coordinates.");
            values[valueCount++] = ParseInteger(value);
        }
        if (isMatrix)
        {
            if (valueCount != 6) throw new SvgException(SvgErrorCode.MalformedInput, "matrix() requires six coordinates.");
            int scaleX = values[0]; int skewY = values[1]; int skewX = values[2]; int scaleY = values[3]; int offsetX = values[4]; int offsetY = values[5];
            if (skewX != 0 || skewY != 0 || scaleX <= 0 || scaleY <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive axis-aligned integer matrix() transforms are implemented.");
            return new AxisTransform(scaleX, scaleY, offsetX, offsetY);
        }
        if (valueCount is < 1 or > 2) throw new SvgException(SvgErrorCode.MalformedInput, "Transforms require one or two coordinates.");
        int first = values[0]; int second = valueCount == 2 ? values[1] : first;
        if (isTranslate) return new AxisTransform(1, 1, first, valueCount == 2 ? second : 0);
        if (first <= 0 || second <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive integer scale() values are implemented.");
        return new AxisTransform(first, second, 0, 0);
    }

    private static void ValidateNonGroupStyle(InlineStyle style)
    {
        if (style.Opacity is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Root and group opacity require isolated compositing and are not implemented.");
        if (style.FillRule is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill rules are only implemented on rectangles.");
    }

    private ref struct StyleDeclarationReader
    {
        private readonly ReadOnlySpan<char> _value;
        private int _position;

        internal StyleDeclarationReader(ReadOnlySpan<char> value) => _value = value;

        internal bool TryRead(out ReadOnlySpan<char> declaration)
        {
            while (_position < _value.Length)
            {
                int start = _position;
                while (_position < _value.Length && _value[_position] != ';') _position++;
                declaration = _value[start.._position].Trim();
                if (_position < _value.Length) _position++;
                if (!declaration.IsEmpty) return true;
            }
            declaration = default;
            return false;
        }
    }

    private static InlineStyle ParseInlineStyle(string? value) => value is null ? default : ParseInlineStyle(value.AsSpan());

    private static InlineStyle ParseInlineStyle(ReadOnlySpan<char> value)
    {
        string? color = null, fill = null, fillOpacity = null, stroke = null, display = null, visibility = null, opacity = null, fillRule = null;
        StyleDeclarationReader declarations = new(value);
        while (declarations.TryRead(out ReadOnlySpan<char> declaration))
        {
            int separator = declaration.IndexOf(':');
            if (separator <= 0 || declaration[(separator + 1)..].Contains(':')) throw new SvgException(SvgErrorCode.MalformedInput, "Inline style declarations must use property:value syntax.");
            ReadOnlySpan<char> property = declaration[..separator].Trim();
            ReadOnlySpan<char> propertyValue = declaration[(separator + 1)..].Trim();
            if (propertyValue.Length == 0 || propertyValue.Contains("!important".AsSpan(), StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Empty and !important inline style declarations are not implemented.");
            if (property.Equals("color", StringComparison.OrdinalIgnoreCase)) color = propertyValue.ToString();
            else if (property.Equals("fill", StringComparison.OrdinalIgnoreCase)) fill = propertyValue.ToString();
            else if (property.Equals("fill-opacity", StringComparison.OrdinalIgnoreCase)) fillOpacity = propertyValue.ToString();
            else if (property.Equals("stroke", StringComparison.OrdinalIgnoreCase)) stroke = propertyValue.ToString();
            else if (property.Equals("display", StringComparison.OrdinalIgnoreCase)) display = propertyValue.ToString();
            else if (property.Equals("visibility", StringComparison.OrdinalIgnoreCase)) visibility = propertyValue.ToString();
            else if (property.Equals("opacity", StringComparison.OrdinalIgnoreCase)) opacity = propertyValue.ToString();
            else if (property.Equals("fill-rule", StringComparison.OrdinalIgnoreCase)) fillRule = propertyValue.ToString();
            else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Inline style property '" + property.ToString() + "' is not implemented.");
        }
        return new InlineStyle(color, fill, fillOpacity, stroke, display, visibility, opacity, fillRule);
    }

    private static void ParseRectangleStyleSheet(string value, int maximumRules, BudgetedParserList<CssRule> rules, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        for (int index = 0; index < value.Length; index += 4096) workCounter.Consume(cancellationToken);
        value = StripCssComments(value);
        int position = 0;
        while (position < value.Length)
        {
            while (position < value.Length && char.IsWhiteSpace(value[position])) position++;
            if (position == value.Length) break;
            int openingBrace = value.IndexOf('{', position);
            if (openingBrace < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Stylesheet rules must contain a declaration block.");
            int closingBrace = value.IndexOf('}', openingBrace + 1);
            if (closingBrace < 0 || value.AsSpan(openingBrace + 1, closingBrace - openingBrace - 1).Contains('{')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Nested stylesheet rules are not implemented.");
            InlineStyle style = ParseInlineStyle(value.AsSpan(openingBrace + 1, closingBrace - openingBrace - 1));
            CssSelectorListReader selectors = new(value.AsSpan(position, openingBrace - position));
            while (selectors.TryRead(out ReadOnlySpan<char> selector))
            {
                if (rules.Count >= maximumRules) throw new SvgException(SvgErrorCode.LimitExceeded, "Stylesheet rule count exceeds the configured structural limit.");
                rules.Add(ParseRectangleSelector(selector, style));
            }
            position = closingBrace + 1;
        }
        if (rules.Count == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "A stylesheet must contain a rect, ID, or class selector rule.");
    }

    private static string StripCssComments(string value)
    {
        int sourceIndex = 0;
        int removedCharacters = 0;
        while (true)
        {
            int start = value.IndexOf("/*", sourceIndex, StringComparison.Ordinal);
            if (start < 0) break;
            int end = value.IndexOf("*/", start + 2, StringComparison.Ordinal);
            if (end < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Stylesheet comments must be terminated.");
            removedCharacters += end + 2 - start;
            sourceIndex = end + 2;
        }
        if (removedCharacters == 0) return value;
        return string.Create(value.Length - removedCharacters, value, static (destination, source) =>
        {
            int sourceIndex = 0;
            int destinationIndex = 0;
            while (sourceIndex < source.Length)
            {
                int commentStart = source.IndexOf("/*", sourceIndex, StringComparison.Ordinal);
                if (commentStart < 0)
                {
                    source.AsSpan(sourceIndex).CopyTo(destination[destinationIndex..]);
                    return;
                }
                source.AsSpan(sourceIndex, commentStart - sourceIndex).CopyTo(destination[destinationIndex..]);
                destinationIndex += commentStart - sourceIndex;
                int commentEnd = source.IndexOf("*/", commentStart + 2, StringComparison.Ordinal);
                sourceIndex = commentEnd + 2;
            }
        });
    }

    private static CssRule ParseRectangleSelector(ReadOnlySpan<char> selector, InlineStyle style)
    {
        CssSelectorTokenReader tokens = new(selector);
        if (!tokens.TryRead(out ReadOnlySpan<char> first)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only rect type, ID, class, id/class attribute, g descendant, and g child stylesheet selectors are implemented.");
        if (tokens.TryRead(out ReadOnlySpan<char> second))
        {
            if (first.Equals("g", StringComparison.OrdinalIgnoreCase) && second.Equals("rect", StringComparison.OrdinalIgnoreCase) && !tokens.TryRead(out _)) return new CssRule(CssSelectorKind.DescendantGroup, string.Empty, null, 2, style);
            if (first.Equals("g", StringComparison.OrdinalIgnoreCase) && second.SequenceEqual(">".AsSpan()) && tokens.TryRead(out ReadOnlySpan<char> third) && third.Equals("rect", StringComparison.OrdinalIgnoreCase) && !tokens.TryRead(out _)) return new CssRule(CssSelectorKind.ChildGroup, string.Empty, null, 2, style);
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only rect type, ID, class, id/class attribute, g descendant, and g child stylesheet selectors are implemented.");
        }
        if (TryParseCompoundSelector(first, "rect", CssSelectorKind.RectangleClass, CssSelectorKind.RectangleId, style, out CssRule compoundRule)) return compoundRule;
        if (TryParseCompoundSelector(first, "g", CssSelectorKind.GroupClass, CssSelectorKind.GroupId, style, out compoundRule)) return compoundRule;
        if (first.Equals("rect", StringComparison.OrdinalIgnoreCase)) return new CssRule(CssSelectorKind.Rectangle, string.Empty, null, 1, style);
        if (first.Equals("g", StringComparison.OrdinalIgnoreCase)) return new CssRule(CssSelectorKind.Group, string.Empty, null, 1, style);
        if (first.Length > 1 && first[0] == '#' && IsCssIdentifier(first[1..])) return new CssRule(CssSelectorKind.Id, first[1..].ToString(), null, 100, style);
        if (first.Length > 1 && first[0] == '.' && IsCssIdentifier(first[1..])) return new CssRule(CssSelectorKind.Class, first[1..].ToString(), null, 10, style);
        if (first.Length > 2 && first[0] == '[' && first[^1] == ']') return ParseAttributeSelector(first[1..^1], style);
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only rect type, ID, class, id/class attribute, g descendant, and g child stylesheet selectors are implemented.");
    }

    private static CssRule ParseAttributeSelector(ReadOnlySpan<char> selector, InlineStyle style)
    {
        int separator = selector.IndexOf('=');
        ReadOnlySpan<char> property = (separator < 0 ? selector : selector[..separator]).Trim();
        bool isId = property.SequenceEqual("id".AsSpan());
        bool isClass = property.SequenceEqual("class".AsSpan());
        if (!isId && !isClass) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only id and class presence/equality stylesheet attribute selectors are implemented.");
        if (separator < 0) return new CssRule(CssSelectorKind.AttributePresent, isId ? "id" : "class", null, 10, style);
        ReadOnlySpan<char> expected = TrimCssQuotes(selector[(separator + 1)..].Trim());
        if (expected.IsEmpty) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only id and class presence/equality stylesheet attribute selectors are implemented.");
        if (!IsCssIdentifier(expected)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only simple CSS identifier attribute selector values are implemented.");
        return new CssRule(CssSelectorKind.AttributeEquals, isId ? "id" : "class", expected.ToString(), 10, style);
    }

    private static bool TryParseCompoundSelector(ReadOnlySpan<char> selector, string elementName, CssSelectorKind classKind, CssSelectorKind idKind, InlineStyle style, out CssRule rule)
    {
        if (selector.StartsWith(elementName + ".", StringComparison.OrdinalIgnoreCase) && IsCssIdentifier(selector[(elementName.Length + 1)..])) { rule = new CssRule(classKind, selector[(elementName.Length + 1)..].ToString(), null, 11, style); return true; }
        if (selector.StartsWith(elementName + "#", StringComparison.OrdinalIgnoreCase) && IsCssIdentifier(selector[(elementName.Length + 1)..])) { rule = new CssRule(idKind, selector[(elementName.Length + 1)..].ToString(), null, 101, style); return true; }
        rule = default;
        return false;
    }

    private static ReadOnlySpan<char> TrimCssQuotes(ReadOnlySpan<char> value)
    {
        while (!value.IsEmpty && value[0] is '\'' or '"') value = value[1..];
        while (!value.IsEmpty && value[^1] is '\'' or '"') value = value[..^1];
        return value;
    }

    private ref struct CssSelectorTokenReader
    {
        private readonly ReadOnlySpan<char> _value;
        private int _position;

        internal CssSelectorTokenReader(ReadOnlySpan<char> value) => _value = value;

        internal bool TryRead(out ReadOnlySpan<char> token)
        {
            while (_position < _value.Length && char.IsWhiteSpace(_value[_position])) _position++;
            if (_position == _value.Length)
            {
                token = default;
                return false;
            }
            if (_value[_position] == '>')
            {
                token = _value.Slice(_position, 1);
                _position++;
                return true;
            }
            if (_value[_position] == '[')
            {
                int attributeStart = _position++;
                while (_position < _value.Length && _value[_position] != ']') _position++;
                if (_position < _value.Length) _position++;
                token = _value[attributeStart.._position];
                return true;
            }
            int start = _position;
            while (_position < _value.Length && !char.IsWhiteSpace(_value[_position]) && _value[_position] != '>') _position++;
            token = _value[start.._position];
            return true;
        }
    }

    private ref struct CssSelectorListReader
    {
        private readonly ReadOnlySpan<char> _value;
        private int _position;

        internal CssSelectorListReader(ReadOnlySpan<char> value) => _value = value;

        internal bool TryRead(out ReadOnlySpan<char> selector)
        {
            while (_position < _value.Length)
            {
                int start = _position;
                while (_position < _value.Length && _value[_position] != ',') _position++;
                selector = _value[start.._position].Trim();
                if (_position < _value.Length) _position++;
                if (!selector.IsEmpty) return true;
            }
            selector = default;
            return false;
        }
    }

    private static bool IsCssIdentifier(ReadOnlySpan<char> value)
    {
        foreach (char character in value) if (!(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')) return false;
        return true;
    }

    private static InlineStyle GetStyleSheet(XmlReader reader, BudgetedParserList<CssRule> rules, bool directChild, BudgetedParserList<GroupScope> groupDepths, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        string? color = null, fill = null, fillOpacity = null, stroke = null, display = null, visibility = null, opacity = null, fillRule = null;
        int colorSpecificity = -1, fillSpecificity = -1, fillOpacitySpecificity = -1, strokeSpecificity = -1, displaySpecificity = -1, visibilitySpecificity = -1, opacitySpecificity = -1, fillRuleSpecificity = -1;
        for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            CssRule rule = rules[ruleIndex];
            workCounter.Consume(cancellationToken);
            if (!rule.Matches(reader, directChild, groupDepths)) continue;
            if (rule.Style.Color is not null && rule.Specificity >= colorSpecificity) { color = rule.Style.Color; colorSpecificity = rule.Specificity; }
            if (rule.Style.Fill is not null && rule.Specificity >= fillSpecificity) { fill = rule.Style.Fill; fillSpecificity = rule.Specificity; }
            if (rule.Style.FillOpacity is not null && rule.Specificity >= fillOpacitySpecificity) { fillOpacity = rule.Style.FillOpacity; fillOpacitySpecificity = rule.Specificity; }
            if (rule.Style.Stroke is not null && rule.Specificity >= strokeSpecificity) { stroke = rule.Style.Stroke; strokeSpecificity = rule.Specificity; }
            if (rule.Style.Display is not null && rule.Specificity >= displaySpecificity) { display = rule.Style.Display; displaySpecificity = rule.Specificity; }
            if (rule.Style.Visibility is not null && rule.Specificity >= visibilitySpecificity) { visibility = rule.Style.Visibility; visibilitySpecificity = rule.Specificity; }
            if (rule.Style.Opacity is not null && rule.Specificity >= opacitySpecificity) { opacity = rule.Style.Opacity; opacitySpecificity = rule.Specificity; }
            if (rule.Style.FillRule is not null && rule.Specificity >= fillRuleSpecificity) { fillRule = rule.Style.FillRule; fillRuleSpecificity = rule.Specificity; }
        }
        return new InlineStyle(color, fill, fillOpacity, stroke, display, visibility, opacity, fillRule);
    }

    private static string ReadStyleElementText(XmlReader reader)
    {
        using XmlReader subtree = reader.ReadSubtree();
        subtree.Read();
        return subtree.ReadElementContentAsString();
    }

    private static SvgPaint ParsePaint(string? fill, string? opacity, string? currentColor, SvgPaint inheritedPaint)
    {
        SvgColor resolvedCurrentColor = currentColor is null ? inheritedPaint.CurrentColor : ParseColor(currentColor);
        bool hasFill = fill is null ? inheritedPaint.HasFill : !fill.Equals("none", StringComparison.OrdinalIgnoreCase) && !fill.Equals("transparent", StringComparison.OrdinalIgnoreCase);
        bool fillUsesCurrentColor = fill is null ? inheritedPaint.FillUsesCurrentColor : fill.Equals("currentColor", StringComparison.OrdinalIgnoreCase);
        SvgColor fillColor = fill is null ? inheritedPaint.FillColor : fillUsesCurrentColor ? SvgColor.Transparent : hasFill ? ParseColor(fill) : SvgColor.Transparent;
        byte fillOpacity = opacity is null ? inheritedPaint.FillOpacity : ParseOpacity(opacity);
        return new SvgPaint(fillColor, hasFill, fillOpacity, resolvedCurrentColor, fillUsesCurrentColor);
    }

    private static SvgColor ResolvePaintColor(SvgPaint paint, byte opacity)
    {
        SvgColor color = paint.FillUsesCurrentColor ? paint.CurrentColor : paint.FillColor;
        return new SvgColor(color.Red, color.Green, color.Blue, MultiplyAlpha(MultiplyAlpha(color.Alpha, paint.FillOpacity), opacity));
    }

    private static SvgColor ParseStrokeColor(string value, string? currentColor, SvgPaint inheritedPaint)
    {
        SvgColor resolvedCurrentColor = currentColor is null ? inheritedPaint.CurrentColor : ParseColor(currentColor);
        return value.Equals("currentColor", StringComparison.OrdinalIgnoreCase) ? resolvedCurrentColor : ParseColor(value);
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

    private static int ParseInteger(string value) => ParseInteger(value.AsSpan());

    private static int ParseInteger(ReadOnlySpan<char> value)
    {
        ReadOnlySpan<char> token = value.Trim();
        if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase)) token = token[..^2].TrimEnd();
        else if (token.Length > 0 && (char.IsLetter(token[^1]) || token[^1] == '%')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bare-number and px integer rectangle coordinates are implemented.");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result) || result != Math.Truncate(result) || result < int.MinValue || result > int.MaxValue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer rectangle coordinates are implemented.");
        return checked((int)result);
    }

    private static byte ParseOpacity(string? value)
    {
        if (value is null) return 255;
        return ParseOpacity(value.AsSpan());
    }

    private static byte ParseOpacity(ReadOnlySpan<char> value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double opacity) || !double.IsFinite(opacity) || opacity < 0 || opacity > 1) throw new SvgException(SvgErrorCode.MalformedInput, "Fill opacity must be a finite value from zero through one.");
        return checked((byte)Math.Round(opacity * 255, MidpointRounding.AwayFromZero));
    }

    private static byte MultiplyAlpha(byte left, byte right) => (byte)((left * right + 127) / 255);

    private static byte ParseAlpha(ReadOnlySpan<char> value)
    {
        if (!value.EndsWith('%')) return ParseOpacity(value);
        if (!double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double percentage) || !double.IsFinite(percentage) || percentage < 0 || percentage > 100) throw new SvgException(SvgErrorCode.MalformedInput, "Alpha percentages must be finite values from zero through one hundred.");
        return checked((byte)Math.Round(percentage * 255 / 100, MidpointRounding.AwayFromZero));
    }

    private static SvgColor ParseColor(string fill)
    {
        if (fill.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return SvgColor.Transparent;
        if (CssNamedColors.TryGetValue(fill, out SvgColor namedColor)) return namedColor;
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
        FunctionalColorTokenReader tokens = new(fill.AsSpan((hasAlpha ? 5 : 4)..^1), fill.Contains(','));
        if (!tokens.TryRead(out ReadOnlySpan<char> redToken) || !tokens.TryRead(out ReadOnlySpan<char> greenToken) || !tokens.TryRead(out ReadOnlySpan<char> blueToken) || !TryParseRgbChannel(redToken, out byte red) || !TryParseRgbChannel(greenToken, out byte green) || !TryParseRgbChannel(blueToken, out byte blue)) throw new SvgException(SvgErrorCode.MalformedInput, "RGB color channels must be decimal bytes.");
        byte alpha = 255;
        if (tokens.IsCommaSeparated)
        {
            if (hasAlpha)
            {
                if (!tokens.TryRead(out ReadOnlySpan<char> alphaToken) || tokens.TryRead(out _)) throw new SvgException(SvgErrorCode.MalformedInput, "RGB color channels must be decimal bytes.");
                alpha = ParseAlpha(alphaToken);
            }
            else if (tokens.TryRead(out _)) throw new SvgException(SvgErrorCode.MalformedInput, "RGB color channels must be decimal bytes.");
        }
        else if (tokens.TryRead(out ReadOnlySpan<char> separator))
        {
            if (!separator.SequenceEqual("/".AsSpan()) || !tokens.TryRead(out ReadOnlySpan<char> alphaToken) || tokens.TryRead(out _)) throw new SvgException(SvgErrorCode.MalformedInput, "RGB color channels must be decimal bytes.");
            alpha = ParseAlpha(alphaToken);
        }
        color = new SvgColor(red, green, blue, alpha);
        return true;
    }

    private static bool TryParseRgbChannel(ReadOnlySpan<char> token, out byte value)
    {
        if (token.EndsWith('%'))
        {
            bool valid = double.TryParse(token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double percentage) && double.IsFinite(percentage) && percentage >= 0 && percentage <= 100;
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
        FunctionalColorTokenReader tokens = new(fill.AsSpan((hasAlpha ? 5 : 4)..^1), fill.Contains(','));
        if (!tokens.TryRead(out ReadOnlySpan<char> hueToken) || !tokens.TryRead(out ReadOnlySpan<char> saturationToken) || !tokens.TryRead(out ReadOnlySpan<char> lightnessToken) || !double.TryParse(hueToken, NumberStyles.Float, CultureInfo.InvariantCulture, out double hue) || !double.IsFinite(hue) || !TryParsePercentage(saturationToken, out double saturation) || !TryParsePercentage(lightnessToken, out double lightness)) throw new SvgException(SvgErrorCode.MalformedInput, "HSL colors require a hue and percentage saturation/lightness channels.");
        byte alpha = 255;
        bool hasSpecifiedAlpha = false;
        if (tokens.IsCommaSeparated)
        {
            if (hasAlpha)
            {
                if (!tokens.TryRead(out ReadOnlySpan<char> alphaToken) || tokens.TryRead(out _)) throw new SvgException(SvgErrorCode.MalformedInput, "HSL colors require a hue and percentage saturation/lightness channels.");
                alpha = ParseAlpha(alphaToken);
                hasSpecifiedAlpha = true;
            }
            else if (tokens.TryRead(out _)) throw new SvgException(SvgErrorCode.MalformedInput, "HSL colors require a hue and percentage saturation/lightness channels.");
        }
        else if (tokens.TryRead(out ReadOnlySpan<char> separator))
        {
            if (!separator.SequenceEqual("/".AsSpan()) || !tokens.TryRead(out ReadOnlySpan<char> alphaToken) || tokens.TryRead(out _)) throw new SvgException(SvgErrorCode.MalformedInput, "HSL colors require a hue and percentage saturation/lightness channels.");
            alpha = ParseAlpha(alphaToken);
            hasSpecifiedAlpha = true;
        }
        if (hasAlpha && !hasSpecifiedAlpha) throw new SvgException(SvgErrorCode.MalformedInput, "HSL colors require a hue and percentage saturation/lightness channels.");
        hue = ((hue % 360) + 360) % 360;
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double secondary = chroma * (1 - Math.Abs((hue / 60 % 2) - 1));
        (double red, double green, double blue) = hue < 60 ? (chroma, secondary, 0d) : hue < 120 ? (secondary, chroma, 0d) : hue < 180 ? (0d, chroma, secondary) : hue < 240 ? (0d, secondary, chroma) : hue < 300 ? (secondary, 0d, chroma) : (chroma, 0d, secondary);
        double match = lightness - chroma / 2;
        color = new SvgColor((byte)Math.Round((red + match) * 255, MidpointRounding.AwayFromZero), (byte)Math.Round((green + match) * 255, MidpointRounding.AwayFromZero), (byte)Math.Round((blue + match) * 255, MidpointRounding.AwayFromZero), alpha);
        return true;
    }

    private static bool TryParsePercentage(ReadOnlySpan<char> token, out double value)
    {
        value = 0;
        bool valid = token.EndsWith('%') && double.TryParse(token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && value >= 0 && value <= 100;
        value /= valid ? 100 : 1;
        return valid;
    }

    private ref struct FunctionalColorTokenReader
    {
        private readonly ReadOnlySpan<char> _value;
        private int _position;
        private bool _finished;

        internal FunctionalColorTokenReader(ReadOnlySpan<char> value, bool isCommaSeparated)
        {
            _value = value;
            IsCommaSeparated = isCommaSeparated;
        }

        internal bool IsCommaSeparated { get; }

        internal bool TryRead(out ReadOnlySpan<char> token)
        {
            if (IsCommaSeparated)
            {
                if (_finished)
                {
                    token = default;
                    return false;
                }
                int start = _position;
                while (_position < _value.Length && _value[_position] != ',') _position++;
                token = _value[start.._position].Trim();
                if (_position == _value.Length) _finished = true;
                else _position++;
                return true;
            }

            while (_position < _value.Length && char.IsWhiteSpace(_value[_position])) _position++;
            if (_position == _value.Length)
            {
                token = default;
                return false;
            }
            if (_value[_position] == '/')
            {
                token = _value.Slice(_position, 1);
                _position++;
                return true;
            }
            int tokenStart = _position;
            while (_position < _value.Length && !char.IsWhiteSpace(_value[_position]) && _value[_position] != '/') _position++;
            token = _value[tokenStart.._position];
            return true;
        }
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
        Span<double> values = stackalloc double[4];
        int valueCount = 0;
        DelimitedTokenReader tokens = new(value);
        while (tokens.TryReadValue(out ReadOnlySpan<char> token))
        {
            if (valueCount == values.Length || !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out values[valueCount])) throw new SvgException(SvgErrorCode.MalformedInput, "viewBox must contain four finite numbers with positive width and height.");
            valueCount++;
        }
        if (valueCount != 4 || !double.IsFinite(values[0]) || !double.IsFinite(values[1]) || !double.IsFinite(values[2]) || !double.IsFinite(values[3]) || values[2] <= 0 || values[3] <= 0) throw new SvgException(SvgErrorCode.MalformedInput, "viewBox must contain four finite numbers with positive width and height.");
        (SvgViewBoxAlignment horizontalAlignment, SvgViewBoxAlignment verticalAlignment, SvgViewBoxFit fit) = ParsePreserveAspectRatio(preserveAspectRatio);
        return new SvgViewBox(values[0], values[1], values[2], values[3], horizontalAlignment, verticalAlignment, fit);
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

    private readonly record struct GroupScope(int Depth, string Name, SvgPaint Paint, bool IsDisplayed, bool IsVisible, AxisTransform Transform, bool IsolatesOpacity);

    private readonly record struct AxisTransform(int ScaleX, int ScaleY, int OffsetX, int OffsetY)
    {
        internal static AxisTransform Identity { get; } = new(1, 1, 0, 0);
        internal AxisTransform Compose(AxisTransform child) => new(ApplyTransformScale(ScaleX, child.ScaleX), ApplyTransformScale(ScaleY, child.ScaleY), ApplyTransform(ScaleX, child.OffsetX, OffsetX), ApplyTransform(ScaleY, child.OffsetY, OffsetY));
    }

    private readonly record struct SvgPaint(SvgColor FillColor, bool HasFill, byte FillOpacity, SvgColor CurrentColor, bool FillUsesCurrentColor)
    {
        internal static SvgPaint Default { get; } = new(new SvgColor(0, 0, 0, 255), true, 255, new SvgColor(0, 0, 0, 255), false);
    }

    private readonly record struct InlineStyle(string? Color, string? Fill, string? FillOpacity, string? Stroke, string? Display, string? Visibility, string? Opacity, string? FillRule);

    private readonly record struct UseReference(string Id, double X, double Y, AxisTransform Transform, byte Opacity, bool IsPainted);

    private readonly record struct PendingUse(int RectangleIndex, int CommandIndex, UseReference Reference);

    private readonly record struct ParsedGeometry(OwnedParserArray<SvgRectangle> Storage, int ElementCount) : IDisposable
    {
        internal ReadOnlySpan<SvgRectangle> Rectangles => Storage.Items;

        public void Dispose() => Storage.Dispose();
    }

    private sealed class ParserWorkCounter
    {
        private readonly long _maximumWorkUnits;
        private long _used;

        internal ParserWorkCounter(long maximumWorkUnits) => _maximumWorkUnits = maximumWorkUnits;

        internal long Used => _used;

        internal void Consume(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_used >= _maximumWorkUnits) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG parsing work exceeds the configured limit.");
            _used++;
        }
    }

    private enum CssSelectorKind { Rectangle, Group, Id, Class, RectangleClass, RectangleId, GroupClass, GroupId, AttributePresent, AttributeEquals, DescendantGroup, ChildGroup }

    private static bool HasCssClass(string? value, string expected)
    {
        if (value is null) return false;
        ReadOnlySpan<char> classes = value.AsSpan();
        int position = 0;
        while (position < classes.Length)
        {
            while (position < classes.Length && char.IsWhiteSpace(classes[position])) position++;
            int start = position;
            while (position < classes.Length && !char.IsWhiteSpace(classes[position])) position++;
            if (classes[start..position].Equals(expected, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private readonly record struct CssRule(CssSelectorKind Kind, string Value, string? ExpectedValue, int Specificity, InlineStyle Style)
    {
        internal bool Matches(XmlReader reader, bool directChild, BudgetedParserList<GroupScope> groupDepths) => Kind switch
        {
            CssSelectorKind.Rectangle => reader.LocalName == "rect",
            CssSelectorKind.Group => reader.LocalName == "g",
            CssSelectorKind.Id => string.Equals(Value, reader.GetAttribute("id"), StringComparison.Ordinal),
            CssSelectorKind.Class => HasCssClass(reader.GetAttribute("class"), Value),
            CssSelectorKind.RectangleClass => reader.LocalName == "rect" && HasCssClass(reader.GetAttribute("class"), Value),
            CssSelectorKind.RectangleId => reader.LocalName == "rect" && string.Equals(Value, reader.GetAttribute("id"), StringComparison.Ordinal),
            CssSelectorKind.GroupClass => reader.LocalName == "g" && HasCssClass(reader.GetAttribute("class"), Value),
            CssSelectorKind.GroupId => reader.LocalName == "g" && string.Equals(Value, reader.GetAttribute("id"), StringComparison.Ordinal),
            CssSelectorKind.AttributePresent => reader.GetAttribute(Value) is not null,
            CssSelectorKind.AttributeEquals => string.Equals(reader.GetAttribute(Value), ExpectedValue, StringComparison.Ordinal),
            CssSelectorKind.DescendantGroup => groupDepths.Any(static group => group.Name == "g"),
            CssSelectorKind.ChildGroup => !directChild && groupDepths.Count > 0 && groupDepths[^1].Name == "g",
            _ => false
        };
    }
}
