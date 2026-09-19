using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Reads the bounded SVG root without materializing a DOM or input string.</summary>
internal static class SvgRootParser
{
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
            ParserWorkCounter workCounter = new(limits.MaximumWorkUnits);
            SvgRootAttributes root = SvgRootAttributes.Parse(reader, CssNamedColors, workCounter, cancellationToken);
            if (reader.IsEmptyElement) return CompleteRoot(new SvgRoot(root.Width, root.Height, root.ViewBox, [], [], false, 0, null));
            int rootDepth = reader.Depth;
            using SvgDocumentState state = new(budget, workCounter, cancellationToken);
            if (root.Opacity != 255) state.Scene.BeginOpacityScope(root.Opacity);
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.Element)
                {
                    ValidateElementStructure(reader, limits);
                    if (state.NodeCount >= limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                    state.NodeCount++;
                    bool directChild = reader.Depth == rootDepth + 1;
                    bool nestedInGroup = state.Groups.Count > 0;
                    if (state.DefinitionsDepth >= 0 && (reader.Depth == state.DefinitionsDepth + 1 || reader.Depth == state.SymbolDepth + 1) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "rect")
                    {
                        string? id = state.SymbolDepth >= 0 ? state.SymbolId : reader.GetAttribute("id");
                        SvgRectangle rectangle = SvgLeafParser.ParseRectangle(reader, root.Paint, root.IsDisplayed, root.IsVisible, AxisTransform.Identity, GetStyleSheet(reader, state.StyleRules, directChild, state.Groups, workCounter, cancellationToken), CssNamedColors, workCounter, cancellationToken);
                        if (id is not null)
                        {
                            workCounter.Consume(cancellationToken);
                            if (!state.Definitions.TryAdd(id, rectangle)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
                        }
                        state.HasUnsupportedContent |= !reader.IsEmptyElement;
                        state.SawStyleableNode = true;
                    }
                    else if (state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "symbol")
                    {
                        SvgElementAttributePolicy.ValidateSymbol(reader);
                        if (GetStyleSheet(reader, state.StyleRules, directChild, state.Groups, workCounter, cancellationToken) != default) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet rules on symbol definitions are not implemented.");
                        state.SymbolId = reader.GetAttribute("id") ?? throw new SvgException(SvgErrorCode.MalformedInput, "A symbol requires an id.");
                        if (!reader.IsEmptyElement) state.SymbolDepth = reader.Depth;
                    }
                    else if (state.DefinitionsDepth < 0 && directChild && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "defs")
                    {
                        SvgElementAttributePolicy.ValidateDefinitionContainer(reader);
                        if (!reader.IsEmptyElement) state.DefinitionsDepth = reader.Depth;
                    }
                    else if (state.DefinitionsDepth < 0 && directChild && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "use")
                    {
                        SvgUseReference reference = SvgLeafParser.ParseUse(reader, GetStyleSheet(reader, state.StyleRules, directChild, state.Groups, workCounter, cancellationToken), workCounter, cancellationToken);
                        workCounter.Consume(cancellationToken);
                        if (state.Definitions.TryGetValue(reference.Id, out SvgRectangle definition))
                        {
                            SvgRectangle rectangle = SvgReferenceResolver.Apply(definition, reference);
                            state.Scene.AddRectangle(rectangle);
                        }
                        else
                        {
                            state.PendingUses.Add(new PendingUse(state.Scene.AddReferencePlaceholder(), reference));
                        }
                        state.HasUnsupportedContent |= !reader.IsEmptyElement;
                        state.SawStyleableNode = true;
                    }
                    else if (state.SymbolDepth >= 0 && reader.Depth > state.SymbolDepth) state.HasUnsupportedContent = true;
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "rect")
                    {
                        AxisTransform transform = state.Groups.Count == 0 ? AxisTransform.Identity : state.Groups[^1].Transform;
                        SvgRectangle rectangle = SvgLeafParser.ParseRectangle(reader, state.Groups.Count == 0 ? root.Paint : state.Groups[^1].Paint, state.Groups.Count == 0 ? root.IsDisplayed : state.Groups[^1].IsDisplayed, state.Groups.Count == 0 ? root.IsVisible : state.Groups[^1].IsVisible, transform, GetStyleSheet(reader, state.StyleRules, directChild, state.Groups, workCounter, cancellationToken), CssNamedColors, workCounter, cancellationToken);
                        state.Scene.AddRectangle(rectangle);
                        state.HasUnsupportedContent |= !reader.IsEmptyElement;
                        state.SawStyleableNode = true;
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "line")
                    {
                        AxisTransform transform = state.Groups.Count == 0 ? AxisTransform.Identity : state.Groups[^1].Transform;
                        SvgRectangle line = SvgLeafParser.ParseLine(reader, state.Groups.Count == 0 ? root.Paint : state.Groups[^1].Paint, state.Groups.Count == 0 ? root.IsDisplayed : state.Groups[^1].IsDisplayed, state.Groups.Count == 0 ? root.IsVisible : state.Groups[^1].IsVisible, transform, GetStyleSheet(reader, state.StyleRules, directChild, state.Groups, workCounter, cancellationToken), CssNamedColors, workCounter, cancellationToken);
                        state.Scene.AddRectangle(line);
                        state.HasUnsupportedContent |= !reader.IsEmptyElement;
                        state.SawStyleableNode = true;
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "path")
                    {
                        AxisTransform transform = state.Groups.Count == 0 ? AxisTransform.Identity : state.Groups[^1].Transform;
                        using (SvgParsedGeometry path = SvgLeafParser.ParseOrthogonalPath(reader, state.Groups.Count == 0 ? root.Paint : state.Groups[^1].Paint, state.Groups.Count == 0 ? root.IsDisplayed : state.Groups[^1].IsDisplayed, state.Groups.Count == 0 ? root.IsVisible : state.Groups[^1].IsVisible, transform, GetStyleSheet(reader, state.StyleRules, directChild, state.Groups, workCounter, cancellationToken), limits.MaximumPathCommands - state.GeometryElementCount, budget, workCounter, cancellationToken, CssNamedColors))
                        {
                            state.GeometryElementCount += path.ElementCount;
                            state.Scene.AddRectangles(path.Rectangles);
                        }
                        state.HasUnsupportedContent |= !reader.IsEmptyElement;
                        state.SawStyleableNode = true;
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName is "polygon" or "polyline")
                    {
                        AxisTransform transform = state.Groups.Count == 0 ? AxisTransform.Identity : state.Groups[^1].Transform;
                        using (SvgParsedGeometry pointList = SvgLeafParser.ParseOrthogonalPointList(reader, reader.LocalName, state.Groups.Count == 0 ? root.Paint : state.Groups[^1].Paint, state.Groups.Count == 0 ? root.IsDisplayed : state.Groups[^1].IsDisplayed, state.Groups.Count == 0 ? root.IsVisible : state.Groups[^1].IsVisible, transform, GetStyleSheet(reader, state.StyleRules, directChild, state.Groups, workCounter, cancellationToken), limits.MaximumPathCommands - state.GeometryElementCount, budget, workCounter, cancellationToken, CssNamedColors))
                        {
                            state.GeometryElementCount += pointList.ElementCount;
                            state.Scene.AddRectangles(pointList.Rectangles);
                        }
                        state.HasUnsupportedContent |= !reader.IsEmptyElement;
                        state.SawStyleableNode = true;
                    }
                    else if ((directChild || nestedInGroup) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && (reader.LocalName is "g" or "a"))
                    {
                        InlineStyle styleSheet = GetStyleSheet(reader, state.StyleRules, directChild, state.Groups, workCounter, cancellationToken);
                        SvgParsedGroup parsedGroup = SvgGroupParser.Parse(reader, state.Groups.Count == 0 ? root.Paint : state.Groups[^1].Paint, state.Groups.Count == 0 ? root.IsDisplayed : state.Groups[^1].IsDisplayed, state.Groups.Count == 0 ? root.IsVisible : state.Groups[^1].IsVisible, state.Groups.Count == 0 ? AxisTransform.Identity : state.Groups[^1].Transform, styleSheet, CssNamedColors, workCounter, cancellationToken);
                        if (parsedGroup.Scope.IsolatesOpacity && (root.Opacity != 255 || state.Groups.Any(static current => current.IsolatesOpacity))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Nested group opacity requires separate compositing calibration.");
                        if (!reader.IsEmptyElement) { if (parsedGroup.Scope.IsolatesOpacity) state.Scene.BeginOpacityScope(parsedGroup.Opacity); state.Groups.Add(parsedGroup.Scope); }
                    }
                    else if (directChild && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "style")
                    {
                        if (state.SawStyleableNode || state.HasStyleSheet) throw new SvgException(SvgErrorCode.UnsupportedFeature, "A single root-level rectangle stylesheet rule must precede supported definitions and rendered content.");
                        if (reader.HasAttributes) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet attributes are not implemented.");
                        ParseRectangleStyleSheet(ReadStyleElementText(reader), limits.MaximumNodes, state.StyleRules, workCounter, cancellationToken);
                        if (SvgStyleSheet.TargetsRootId(root.Id, state.StyleRules, workCounter, cancellationToken)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet rules targeting the SVG root are not implemented.");
                        state.HasStyleSheet = true;
                    }
                    else if (reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName is "style" or "script" or "animate" or "animateMotion" or "animateTransform" or "set") state.HasUnsupportedContent = true;
                    else if (directChild && (reader.LocalName is "title" or "desc" or "metadata" or "defs")) { }
                    else if (directChild || nestedInGroup) state.HasUnsupportedContent = true;
                }
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.Depth == state.DefinitionsDepth) state.DefinitionsDepth = -1;
                    if (reader.Depth == state.SymbolDepth) { state.SymbolDepth = -1; state.SymbolId = null; }
                    if (state.Groups.Count > 0 && state.Groups[^1].Depth == reader.Depth) { SvgGroupScope group = state.Groups[^1]; state.Groups.RemoveLast(); if (group.IsolatesOpacity) state.Scene.EndOpacityScope(); }
                    if (reader.Depth == rootDepth)
                    {
                        SvgReferenceResolver.ResolvePendingUses(state.Definitions, state.PendingUses, state.Scene, workCounter, cancellationToken);
                        if (root.Opacity != 255) state.Scene.EndOpacityScope();
                        SvgSceneStorage sceneStorage = state.Scene.Build();
                        try { return CompleteRoot(new SvgRoot(root.Width, root.Height, root.ViewBox, sceneStorage.Rectangles, sceneStorage.Commands, state.HasUnsupportedContent, workCounter.Used, sceneStorage)); }
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

    private static void ParseRectangleStyleSheet(string value, int maximumRules, BudgetedParserList<CssRule> rules, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => SvgStyleSheet.Parse(value, maximumRules, rules, workCounter, cancellationToken);

    private static InlineStyle GetStyleSheet(XmlReader reader, BudgetedParserList<CssRule> rules, bool directChild, BudgetedParserList<SvgGroupScope> groupDepths, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => SvgStyleSheet.Resolve(reader, rules, directChild, groupDepths.Any(static group => group.Name == "g"), groupDepths.Count > 0 && groupDepths[^1].Name == "g", workCounter, cancellationToken);

    private static string ReadStyleElementText(XmlReader reader)
        => SvgStyleSheet.ReadElementText(reader);

}
