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
            SvgComputedStyle rootStyle = SvgComputedStyle.Resolve(reader, ParseInlineStyle(reader.GetAttribute("style")));
            SvgPaint rootPaint = ParseRootPaint(reader, rootStyle);
            string? rootId = reader.GetAttribute("id");
            byte rootOpacity = ParseOpacity(rootStyle.Opacity);
            bool rootVisible = ParseDisplay(rootStyle.Display);
            bool rootVisibility = ParseVisibility(rootStyle.Visibility, true);
            double? width = ParseLength(reader.GetAttribute("width"));
            double? height = ParseLength(reader.GetAttribute("height"));
            SvgViewBox? viewBox = ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio"));
            if (reader.IsEmptyElement) return CompleteRoot(new SvgRoot(width, height, viewBox, [], [], false, 0, null));
            int rootDepth = reader.Depth;
            using SvgSceneBuilder scene = new(budget);
            if (rootOpacity != 255) scene.BeginOpacityScope(rootOpacity);
            using BudgetedParserList<GroupScope> groupDepths = new(budget, SvgMemoryCategory.Parser, GroupScopeStorageBytes);
            int nodeCount = 0;
            bool unsupported = false;
            bool sawStyleableNode = false;
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
                        unsupported |= !reader.IsEmptyElement;
                        sawStyleableNode = true;
                    }
                    else if (definitionsDepth >= 0 && reader.Depth == definitionsDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "symbol")
                    {
                        SvgElementAttributePolicy.ValidateSymbol(reader);
                        if (GetStyleSheet(reader, rectangleStyleRules, directChild, groupDepths, workCounter, cancellationToken) != default) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet rules on symbol definitions are not implemented.");
                        symbolId = reader.GetAttribute("id") ?? throw new SvgException(SvgErrorCode.MalformedInput, "A symbol requires an id.");
                        if (!reader.IsEmptyElement) symbolDepth = reader.Depth;
                    }
                    else if (definitionsDepth < 0 && directChild && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "defs")
                    {
                        SvgElementAttributePolicy.ValidateDefinitionContainer(reader);
                        if (!reader.IsEmptyElement) definitionsDepth = reader.Depth;
                    }
                    else if (definitionsDepth < 0 && directChild && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "use")
                    {
                        SvgUseReference reference = ParseUse(reader, GetStyleSheet(reader, rectangleStyleRules, directChild, groupDepths, workCounter, cancellationToken));
                        if (definitions.TryGetValue(reference.Id, out SvgRectangle definition))
                        {
                            SvgRectangle rectangle = SvgReferenceResolver.Apply(definition, reference);
                            scene.AddRectangle(rectangle);
                        }
                        else
                        {
                            pendingUses.Add(new PendingUse(scene.AddReferencePlaceholder(), reference));
                        }
                        unsupported |= !reader.IsEmptyElement;
                        sawStyleableNode = true;
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "rect")
                    {
                        AxisTransform transform = groupDepths.Count == 0 ? AxisTransform.Identity : groupDepths[^1].Transform;
                        SvgRectangle rectangle = ParseRectangle(reader, groupDepths.Count == 0 ? rootPaint : groupDepths[^1].Paint, groupDepths.Count == 0 ? rootVisible : groupDepths[^1].IsDisplayed, groupDepths.Count == 0 ? rootVisibility : groupDepths[^1].IsVisible, transform, GetStyleSheet(reader, rectangleStyleRules, directChild, groupDepths, workCounter, cancellationToken));
                        scene.AddRectangle(rectangle);
                        unsupported |= !reader.IsEmptyElement;
                        sawStyleableNode = true;
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "line")
                    {
                        AxisTransform transform = groupDepths.Count == 0 ? AxisTransform.Identity : groupDepths[^1].Transform;
                        SvgRectangle line = ParseLine(reader, groupDepths.Count == 0 ? rootPaint : groupDepths[^1].Paint, groupDepths.Count == 0 ? rootVisible : groupDepths[^1].IsDisplayed, groupDepths.Count == 0 ? rootVisibility : groupDepths[^1].IsVisible, transform, GetStyleSheet(reader, rectangleStyleRules, directChild, groupDepths, workCounter, cancellationToken));
                        scene.AddRectangle(line);
                        unsupported |= !reader.IsEmptyElement;
                        sawStyleableNode = true;
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "path")
                    {
                        AxisTransform transform = groupDepths.Count == 0 ? AxisTransform.Identity : groupDepths[^1].Transform;
                        using (SvgParsedGeometry path = ParseOrthogonalPath(reader, groupDepths.Count == 0 ? rootPaint : groupDepths[^1].Paint, groupDepths.Count == 0 ? rootVisible : groupDepths[^1].IsDisplayed, groupDepths.Count == 0 ? rootVisibility : groupDepths[^1].IsVisible, transform, GetStyleSheet(reader, rectangleStyleRules, directChild, groupDepths, workCounter, cancellationToken), limits.MaximumPathCommands - geometryElementCount, budget, workCounter, cancellationToken))
                        {
                            geometryElementCount += path.ElementCount;
                            scene.AddRectangles(path.Rectangles);
                        }
                        unsupported |= !reader.IsEmptyElement;
                        sawStyleableNode = true;
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName is "polygon" or "polyline")
                    {
                        AxisTransform transform = groupDepths.Count == 0 ? AxisTransform.Identity : groupDepths[^1].Transform;
                        using (SvgParsedGeometry pointList = ParseOrthogonalPointList(reader, reader.LocalName, groupDepths.Count == 0 ? rootPaint : groupDepths[^1].Paint, groupDepths.Count == 0 ? rootVisible : groupDepths[^1].IsDisplayed, groupDepths.Count == 0 ? rootVisibility : groupDepths[^1].IsVisible, transform, GetStyleSheet(reader, rectangleStyleRules, directChild, groupDepths, workCounter, cancellationToken), limits.MaximumPathCommands - geometryElementCount, budget, workCounter, cancellationToken))
                        {
                            geometryElementCount += pointList.ElementCount;
                            scene.AddRectangles(pointList.Rectangles);
                        }
                        unsupported |= !reader.IsEmptyElement;
                        sawStyleableNode = true;
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && (reader.LocalName is "g" or "a"))
                    {
                        InlineStyle inlineStyle = ParseInlineStyle(reader.GetAttribute("style"));
                        InlineStyle styleSheet = GetStyleSheet(reader, rectangleStyleRules, directChild, groupDepths, workCounter, cancellationToken);
                        SvgComputedStyle style = SvgComputedStyle.Resolve(reader, inlineStyle, styleSheet);
                        SvgPaint paint = ParseGroupPaint(reader, groupDepths.Count == 0 ? rootPaint : groupDepths[^1].Paint, style);
                        bool displayed = (groupDepths.Count == 0 ? rootVisible : groupDepths[^1].IsDisplayed) && ParseDisplay(style.Display);
                        bool visibility = ParseVisibility(style.Visibility, groupDepths.Count == 0 ? rootVisibility : groupDepths[^1].IsVisible);
                        AxisTransform transform = groupDepths.Count == 0 ? ParseTransform(reader.GetAttribute("transform")) : groupDepths[^1].Transform.Compose(ParseTransform(reader.GetAttribute("transform")));
                        byte opacity = ParseOpacity(style.Opacity);
                        bool isolatesOpacity = opacity != 255;
                        if (isolatesOpacity && (rootOpacity != 255 || groupDepths.Any(static group => group.IsolatesOpacity))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Nested group opacity requires separate compositing calibration.");
                        if (!reader.IsEmptyElement) { if (isolatesOpacity) scene.BeginOpacityScope(opacity); groupDepths.Add(new GroupScope(reader.Depth, reader.LocalName, paint, displayed, visibility, transform, isolatesOpacity)); }
                    }
                    else if (directChild && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "style")
                    {
                        if (sawStyleableNode || hasRectangleStyleSheet) throw new SvgException(SvgErrorCode.UnsupportedFeature, "A single root-level rectangle stylesheet rule must precede supported definitions and rendered content.");
                        if (reader.HasAttributes) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet attributes are not implemented.");
                        ParseRectangleStyleSheet(ReadStyleElementText(reader), limits.MaximumNodes, rectangleStyleRules, workCounter, cancellationToken);
                        if (SvgStyleSheet.TargetsRootId(rootId, rectangleStyleRules, workCounter, cancellationToken)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet rules targeting the SVG root are not implemented.");
                        hasRectangleStyleSheet = true;
                    }
                    else if (directChild && (reader.LocalName is "title" or "desc" or "metadata" or "defs")) { }
                    else if (directChild || nestedInGroup) unsupported = true;
                }
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.Depth == definitionsDepth) definitionsDepth = -1;
                    if (reader.Depth == symbolDepth) { symbolDepth = -1; symbolId = null; }
                    if (groupDepths.Count > 0 && groupDepths[^1].Depth == reader.Depth) { GroupScope group = groupDepths[^1]; groupDepths.RemoveLast(); if (group.IsolatesOpacity) scene.EndOpacityScope(); }
                    if (reader.Depth == rootDepth)
                    {
                        SvgReferenceResolver.ResolvePendingUses(definitions, pendingUses, scene);
                        if (rootOpacity != 255) scene.EndOpacityScope();
                        SvgSceneStorage sceneStorage = scene.Build();
                        try { return CompleteRoot(new SvgRoot(width, height, viewBox, sceneStorage.Rectangles, sceneStorage.Commands, unsupported, workCounter.Used, sceneStorage)); }
                        catch { sceneStorage.Dispose(); throw; }
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

    private static SvgPaint ParseGroupPaint(XmlReader reader, SvgPaint inheritedPaint, SvgComputedStyle style)
    {
        SvgElementAttributePolicy.ValidateGroup(reader);
        if (style.FillRule is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill rules are only implemented on shapes.");
        ValidateStroke(style.Stroke);
        return SvgPaintResolver.Resolve(style.Fill, style.FillOpacity, style.Color, inheritedPaint, CssNamedColors);
    }

    private static SvgPaint ParseRootPaint(XmlReader reader, SvgComputedStyle style)
    {
        SvgElementAttributePolicy.ValidateRoot(reader);
        if (style.FillRule is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill rules are only implemented on shapes.");
        ValidateStroke(style.Stroke);
        return SvgPaintResolver.Resolve(style.Fill, style.FillOpacity, style.Color, SvgPaint.Default, CssNamedColors);
    }

    private static SvgRectangle ParseRectangle(XmlReader reader, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisibility, AxisTransform inheritedTransform, InlineStyle styleSheet)
    {
        SvgComputedStyle style = SvgComputedStyle.Resolve(reader, ParseInlineStyle(reader.GetAttribute("style")), styleSheet);
        SvgElementAttributePolicy.ValidateRectangle(reader);
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
        SvgResolvedShapePresentation presentation = SvgShapePresentationResolver.Resolve(style, inheritedPaint, inheritedDisplayed, inheritedVisibility, true, CssNamedColors);
        SvgStroke? stroke = ParseRectangleStroke(presentation.Style.Stroke, reader.GetAttribute("stroke-width"), presentation.Style.Color, inheritedPaint, transform, x, y, width, height, presentation.Opacity, presentation.Paint.HasFill);
        return new SvgRectangle(x, y, width, height, presentation.Color, presentation.HasFill, presentation.IsPainted ? stroke : null);
    }

    private static SvgStroke? ParseRectangleStroke(string? value, string? widthValue, string? currentColor, SvgPaint inheritedPaint, AxisTransform transform, double x, double y, double width, double height, byte opacity, bool hasFill)
    {
        if (value is null || value.Equals("none", StringComparison.OrdinalIgnoreCase)) { if (widthValue is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stroke width without a stroke is not implemented."); return null; }
        if (transform != AxisTransform.Identity || x != Math.Truncate(x) || y != Math.Truncate(y) || width != Math.Truncate(width) || height != Math.Truncate(height) || (hasFill && opacity != 255)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only untransformed integer rectangle strokes, with opacity only when fill-less, are implemented.");
        SvgColor color = SvgPaintResolver.ResolveStrokeColor(value, currentColor, inheritedPaint, CssNamedColors);
        int strokeWidth = ParseInteger(widthValue ?? "1");
        if (color.Alpha != 255 || strokeWidth <= 0 || strokeWidth % 2 != 0 || strokeWidth > width || strokeWidth > height) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only opaque even-width rectangle strokes no larger than the rectangle dimensions are implemented.");
        return new SvgStroke(SvgPaintResolver.ApplyOpacity(color, opacity), strokeWidth);
    }

    private static SvgRectangle ParseLine(XmlReader reader, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisibility, AxisTransform inheritedTransform, InlineStyle styleSheet)
    {
        SvgComputedStyle style = SvgComputedStyle.Resolve(reader, ParseInlineStyle(reader.GetAttribute("style")), styleSheet);
        SvgElementAttributePolicy.ValidateLine(reader);
        AxisTransform transform = inheritedTransform.Compose(ParseTransform(reader.GetAttribute("transform")));
        int x1 = ParseInteger(reader.GetAttribute("x1") ?? "0"); int y1 = ParseInteger(reader.GetAttribute("y1") ?? "0"); int x2 = ParseInteger(reader.GetAttribute("x2") ?? "0"); int y2 = ParseInteger(reader.GetAttribute("y2") ?? "0");
        string? stroke = style.Stroke;
        byte opacity = ParseOpacity(style.Opacity);
        if (transform != AxisTransform.Identity || stroke is null || stroke.Equals("none", StringComparison.OrdinalIgnoreCase) || opacity != 255 || (x1 != x2 && y1 != y2) || (x1 == x2 && y1 == y2)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only opaque untransformed horizontal or vertical lines are implemented.");
        SvgColor strokeColor = SvgPaintResolver.ResolveStrokeColor(stroke, style.Color, inheritedPaint, CssNamedColors);
        int strokeWidth = ParseInteger(reader.GetAttribute("stroke-width") ?? "1");
        if (strokeColor.Alpha != 255 || strokeWidth <= 0 || strokeWidth % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only opaque even-width line strokes are implemented.");
        double halfWidth = strokeWidth / 2d;
        bool painted = inheritedDisplayed && ParseDisplay(style.Display) && ParseVisibility(style.Visibility, inheritedVisibility);
        SvgColor color = SvgPaintResolver.ApplyOpacity(strokeColor, opacity);
        return x1 != x2 ? new SvgRectangle(Math.Min(x1, x2), y1 - halfWidth, Math.Abs((long)x2 - x1), strokeWidth, color, painted, null) : new SvgRectangle(x1 - halfWidth, Math.Min(y1, y2), strokeWidth, Math.Abs((long)y2 - y1), color, painted, null);
    }

    private static SvgUseReference ParseUse(XmlReader reader, InlineStyle styleSheet)
    {
        SvgComputedStyle style = SvgComputedStyle.Resolve(reader, ParseInlineStyle(reader.GetAttribute("style")), styleSheet);
        if (style.Color is not null || style.Fill is not null || style.FillOpacity is not null || style.Stroke is not null || style.FillRule is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Paint styles on use elements are not implemented.");
        SvgElementAttributePolicy.ValidateUse(reader);
        string href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink") ?? throw new SvgException(SvgErrorCode.MalformedInput, "A use element requires an href.");
        if (href.Length < 2 || href[0] != '#') throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local rectangle use references are implemented.");
        double x = ParseRectangleCoordinate(reader.GetAttribute("x") ?? "0"); double y = ParseRectangleCoordinate(reader.GetAttribute("y") ?? "0");
        return new SvgUseReference(href[1..], x, y, ParseTransform(reader.GetAttribute("transform")), ParseOpacity(style.Opacity), ParseDisplay(style.Display) && ParseVisibility(style.Visibility, true));
    }

    private static SvgParsedGeometry ParseOrthogonalPath(XmlReader reader, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisibility, AxisTransform inheritedTransform, InlineStyle styleSheet, int maximumGeometryElements, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgElementAttributePolicy.ValidatePath(reader);
        string d = reader.GetAttribute("d") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Path data is required.");
        using BudgetedParserList<SvgPoint> points = SvgOrthogonalGeometryTokenizer.ParsePath(d, maximumGeometryElements, budget, cancellationToken);
        SvgOrthogonalGeometryLowering.ValidateSimpleContour(points, "path", workCounter, cancellationToken);
        SvgComputedStyle style = SvgComputedStyle.Resolve(reader, ParseInlineStyle(reader.GetAttribute("style")), styleSheet); AxisTransform transform = inheritedTransform.Compose(ParseTransform(reader.GetAttribute("transform")));
        SvgResolvedShapePresentation presentation = SvgShapePresentationResolver.Resolve(style, inheritedPaint, inheritedDisplayed, inheritedVisibility, false, CssNamedColors);
        using SvgOrthogonalGeometryLowering lowering = new(points, "path", true, false, maximumGeometryElements, budget, workCounter, cancellationToken);
        return lowering.Lower(presentation.Color, presentation.HasFill, transform);
    }

    private static SvgParsedGeometry ParseOrthogonalPointList(XmlReader reader, string elementName, SvgPaint inheritedPaint, bool inheritedDisplayed, bool inheritedVisibility, AxisTransform inheritedTransform, InlineStyle styleSheet, int maximumGeometryElements, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgElementAttributePolicy.ValidatePointList(reader, elementName);
        string pointsValue = reader.GetAttribute("points") ?? throw new SvgException(SvgErrorCode.MalformedInput, elementName + " points are required.");
        using BudgetedParserList<SvgPoint> points = SvgOrthogonalGeometryTokenizer.ParsePointList(pointsValue, elementName, maximumGeometryElements, budget, cancellationToken);
        using SvgOrthogonalGeometryLowering lowering = new(points, elementName, false, true, maximumGeometryElements, budget, workCounter, cancellationToken);
        SvgComputedStyle style = SvgComputedStyle.Resolve(reader, ParseInlineStyle(reader.GetAttribute("style")), styleSheet); AxisTransform transform = inheritedTransform.Compose(ParseTransform(reader.GetAttribute("transform")));
        SvgResolvedShapePresentation presentation = SvgShapePresentationResolver.Resolve(style, inheritedPaint, inheritedDisplayed, inheritedVisibility, false, CssNamedColors);
        return lowering.Lower(presentation.Color, presentation.HasFill, transform);
    }

    private static AxisTransform ParseTransform(string? value) => SvgGeometrySyntax.ParseTransform(value);

    private static InlineStyle ParseInlineStyle(string? value) => SvgInlineStyleParser.Parse(value);

    private static void ParseRectangleStyleSheet(string value, int maximumRules, BudgetedParserList<CssRule> rules, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => SvgStyleSheet.Parse(value, maximumRules, rules, workCounter, cancellationToken);

    private static InlineStyle GetStyleSheet(XmlReader reader, BudgetedParserList<CssRule> rules, bool directChild, BudgetedParserList<GroupScope> groupDepths, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => SvgStyleSheet.Resolve(reader, rules, directChild, groupDepths.Any(static group => group.Name == "g"), groupDepths.Count > 0 && groupDepths[^1].Name == "g", workCounter, cancellationToken);

    private static string ReadStyleElementText(XmlReader reader)
        => SvgStyleSheet.ReadElementText(reader);

    private static bool ParseDisplay(string? value) => SvgShapePresentationResolver.ParseDisplay(value);

    private static void ValidateFillRule(string? value) => SvgShapePresentationResolver.ValidateFillRule(value);

    private static void ValidateStroke(string? value) => SvgShapePresentationResolver.ValidateStroke(value);

    private static void ValidateSquareCorners(string? value)
    {
        if (value is null || ParseRectangleCoordinate(value) == 0) return;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only zero rectangle corner radii are implemented.");
    }

    private static double ParseRectangleCoordinate(string value) => SvgGeometrySyntax.ParseRectangleCoordinate(value);

    private static bool ParseVisibility(string? value, bool inheritedVisibility) => SvgShapePresentationResolver.ParseVisibility(value, inheritedVisibility);

    private static int ParseInteger(string value) => SvgGeometrySyntax.ParseInteger(value);

    private static byte ParseOpacity(string? value) => SvgColorParser.ParseOpacity(value);

    private static double? ParseLength(string? value) => SvgGeometrySyntax.ParseLength(value);

    private static SvgViewBox? ParseViewBox(string? value, string? preserveAspectRatio) => SvgGeometrySyntax.ParseViewBox(value, preserveAspectRatio);

    private readonly record struct GroupScope(int Depth, string Name, SvgPaint Paint, bool IsDisplayed, bool IsVisible, AxisTransform Transform, bool IsolatesOpacity);

}
