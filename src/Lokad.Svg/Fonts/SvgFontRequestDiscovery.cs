namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Xml;

/// <summary>Discovers the explicit single-family text requests admitted by the initial preparation subset.</summary>
internal static class SvgFontRequestDiscovery
{
    private const int MaximumRequests = 256;
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    internal static SvgFontFaceRequest[] Discover(ReadOnlyMemory<byte> svg, CancellationToken cancellationToken)
        => Discover(svg, SvgRenderLimits.Default, cancellationToken);

    /// <summary>Discovers admitted text requests under the same caller-selected structural and work limits as rendering.</summary>
    internal static SvgFontFaceRequest[] Discover(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return Discover(svg, limits, new ParserWorkCounter(limits.MaximumWorkUnits), cancellationToken);
    }

    /// <summary>Discovers admitted text requests using one caller-owned preparation work counter.</summary>
    internal static SvgFontFaceRequest[] Discover(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(workCounter);
        if (svg.Length > limits.MaximumInputBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG input exceeds the configured input limit.");
        var requests = new List<SvgFontFaceRequest>();
        var seen = new HashSet<TextRequestKey>(TextRequestKeyComparer.Instance);
        var inherited = new Stack<TextStyle>();
        var directText = new Stack<TextRequestCandidate?>();
        var directTextWhitespaceModes = new Stack<SvgTextWhitespaceMode>();
        var textEligibleChildren = new Stack<bool>();
        var elementGroups = new Stack<bool>();
        RenderBudget styleBudget = new(limits.MaximumAccountedBytes);
        using var styleRules = new BudgetedParserList<CssRule>(styleBudget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        using var styleStrings = new BudgetedParserStringStore(styleBudget);
        char[]? textChunk = null;
        RenderBudget.BudgetReservation? textChunkReservation = null;
        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = limits.MaximumInputBytes, IgnoreComments = true, IgnoreProcessingInstructions = true };
        try
        {
            using ReadOnlyMemoryStream stream = new(svg);
            using XmlReader reader = XmlReader.Create(stream, settings);
            bool hasRoot = false;
            int rootDepth = -1;
            string? rootId = null;
            bool hasStyleSheet = false;
            bool sawStyleableNode = false;
            bool hasUnsupportedContent = false;
            int nodeCount = 0;
            int inertDepth = -1;
            int groupDepth = 0;
            TextRequestCandidate? activeDirectTextCandidate = null;
            while (reader.Read())
            {
                workCounter.Consume(cancellationToken);
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (!hasRoot)
                    {
                        if (reader.LocalName != "svg" || reader.NamespaceURI != SvgNamespace) throw new SvgException(SvgErrorCode.MalformedInput, "The document root must be an SVG element in the SVG namespace.");
                        hasRoot = true;
                        rootDepth = reader.Depth;
                        rootId = reader.GetAttribute("id");
                    }
                    if (reader.Depth >= limits.MaximumXmlDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG XML nesting exceeds the configured limit.");
                    if (reader.AttributeCount > limits.MaximumAttributesPerElement) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG element attributes exceed the configured limit.");
                    if (nodeCount >= limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                    nodeCount++;
                    bool isSvgElement = reader.NamespaceURI == SvgNamespace;
                    bool insideDirectText = activeDirectTextCandidate is not null;
                    SvgTextWhitespaceMode inheritedWhitespaceMode = directTextWhitespaceModes.Count == 0 ? SvgTextWhitespaceMode.Default : directTextWhitespaceModes.Peek();
                    SvgTextWhitespaceMode childWhitespaceMode = inheritedWhitespaceMode;
                    bool admittedTextSpan = insideDirectText && isSvgElement && reader.LocalName == "tspan" && SvgTextSyntax.TryParseTspanWhitespaceMode(reader, inheritedWhitespaceMode, workCounter, cancellationToken, out childWhitespaceMode);
                    if (!admittedTextSpan && activeDirectTextCandidate is not null) activeDirectTextCandidate.HasChildElement = true;
                    bool parentAllowsText = textEligibleChildren.Count == 0 || textEligibleChildren.Peek();
                    bool rootElement = reader.Depth == rootDepth;
                    TextStyle style;
                    if (inherited.Count == 0)
                    {
                        SvgRootAttributes root = SvgRootAttributes.Parse(reader, SvgRootParser.CssNamedColors, workCounter, cancellationToken);
                        style = new TextStyle(root.InheritedTextStyle, root.InheritedPresentation.Paint, root.InheritedPresentation.IsDisplayed, root.InheritedPresentation.IsVisible, root.InheritedPresentation.Transform, null, root.Opacity != 255);
                    }
                    else style = inherited.Peek();
                    bool directChild = reader.Depth == rootDepth + 1;
                    bool hasGroupAncestor = groupDepth != 0;
                    bool parentIsGroup = elementGroups.Count > 0 && elementGroups.Peek();
                    bool rootStyleElement = isSvgElement && reader.LocalName == "style" && directChild;
                    if (rootStyleElement)
                    {
                        if (sawStyleableNode || hasStyleSheet) throw new SvgException(SvgErrorCode.UnsupportedFeature, "A single root-level rectangle stylesheet rule must precede supported definitions and rendered content.");
                        if (reader.HasAttributes) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet attributes are not implemented.");
                        using OwnedStyleSheetText styleText = SvgStyleSheet.ReadElementText(reader, styleBudget, workCounter, cancellationToken);
                        SvgStyleSheet.Parse(styleText.Value, limits.MaximumNodes, styleRules, styleStrings, styleBudget, workCounter, cancellationToken);
                        if (SvgStyleSheet.TargetsRootId(rootId, styleRules, workCounter, cancellationToken)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet rules targeting the SVG root are not implemented.");
                        hasStyleSheet = true;
                    }
                    if (rootStyleElement) continue;
                    bool textElement = isSvgElement && reader.LocalName == "text";
                    bool isTextEligible = parentAllowsText && textElement;
                    bool childAllowsText = parentAllowsText && isSvgElement && (reader.Depth == rootDepth || reader.LocalName is "g" or "a");
                    bool isKnownContent = isSvgElement && reader.LocalName is ("svg" or "g" or "a" or "text" or "rect" or "line" or "use" or "path" or "polygon" or "polyline" or "style" or "defs" or "title" or "desc" or "metadata" or "symbol");
                    bool unsupportedContainer = (!rootElement && reader.LocalName is ("svg" or "style" or "symbol")) || (!directChild && reader.LocalName is ("defs" or "title" or "desc" or "metadata"));
                    bool nonemptyRenderedLeaf = !reader.IsEmptyElement && reader.LocalName is ("rect" or "line" or "use" or "path" or "polygon" or "polyline");
                    bool rendererRejectsKnownContent = isSvgElement && (unsupportedContainer || nonemptyRenderedLeaf);
                    if (!hasUnsupportedContent && inertDepth < 0 && !insideDirectText && parentAllowsText && (!isKnownContent || rendererRejectsKnownContent))
                    {
                        hasUnsupportedContent = true;
                        requests.Clear();
                        seen.Clear();
                    }
                    if (inertDepth < 0 && !insideDirectText && parentAllowsText && isSvgElement)
                    {
                        if (!rootElement && reader.LocalName is "g" or "a") SvgElementAttributePolicy.ValidateGroup(reader, workCounter, cancellationToken);
                        else if (directChild && reader.LocalName == "defs") SvgElementAttributePolicy.ValidateDefinitionContainer(reader);
                    }
                    if (inertDepth >= 0 && reader.Depth == inertDepth + 1 && isSvgElement)
                    {
                        if (reader.LocalName == "rect") SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken);
                        else if (reader.LocalName == "line") SvgElementAttributePolicy.ValidateLine(reader, workCounter, cancellationToken);
                        else if (reader.LocalName == "symbol") SvgElementAttributePolicy.ValidateSymbol(reader, workCounter, cancellationToken);
                    }
                    InlineStyle styleSheet = isSvgElement && reader.LocalName is ("g" or "a" or "text") ? SvgStyleSheet.Resolve(reader, styleRules, directChild, hasGroupAncestor, parentIsGroup, workCounter, cancellationToken) : default;
                    SvgTextWhitespaceMode inheritedTextWhitespaceMode = style.Text.WhitespaceMode;
                    string? specifiedWhiteSpace = null;
                    if (isSvgElement && !rootElement && !insideDirectText && parentAllowsText && reader.LocalName is ("g" or "a" or "text")) style = ApplyAttributes(reader, style, styleSheet, workCounter, cancellationToken, out specifiedWhiteSpace);
                    if (inertDepth < 0 && isSvgElement && reader.LocalName is ("defs" or "title" or "desc" or "metadata") && !reader.IsEmptyElement) inertDepth = reader.Depth;
                    SvgTextWhitespaceMode whitespaceMode = SvgTextWhitespaceMode.Default;
                    if (inertDepth < 0 && !insideDirectText && isTextEligible && reader.GetAttribute("transform") is null)
                    {
                        SvgElementAttributePolicy.ValidateText(reader, workCounter, cancellationToken);
                        whitespaceMode = SvgTextSyntax.ParseWhitespaceMode(reader, specifiedWhiteSpace, inheritedTextWhitespaceMode);
                    }
                    if (inertDepth < 0 && !insideDirectText && isTextEligible && style.FillRule is not null && !style.FillRule.Equals("nonzero", StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only the default nonzero fill rule is implemented for direct text.");
                    TextRequestCandidate? candidate = !hasUnsupportedContent && inertDepth < 0 && !insideDirectText && isTextEligible && reader.GetAttribute("transform") is null && style.Transform == AxisTransform.Identity && style.IsDisplayed && style.IsVisible && style.HasFill && style.Text.Family is string family
                        ? new TextRequestCandidate(new SvgFontFaceRequest(family, style.Text.Weight, style.Text.Italic), whitespaceMode)
                        : null;
                    if (candidate is not null) childWhitespaceMode = candidate.WhitespaceMode;
                    if (reader.IsEmptyElement)
                    {
                        if (candidate is not null) AddCandidate(candidate, requests, seen);
                    }
                    else
                    {
                        inherited.Push(style);
                        directText.Push(candidate);
                        directTextWhitespaceModes.Push(childWhitespaceMode);
                        textEligibleChildren.Push(childAllowsText);
                        bool group = isSvgElement && reader.LocalName == "g";
                        elementGroups.Push(group);
                        if (group) groupDepth++;
                        if (candidate is not null) activeDirectTextCandidate = candidate;
                    }
                    if (isSvgElement && parentAllowsText && reader.LocalName is ("rect" or "line" or "use" or "path" or "polygon" or "polyline" or "text")) sawStyleableNode = true;
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.Depth == inertDepth) inertDepth = -1;
                    if (inherited.Count != 0)
                    {
                        inherited.Pop();
                        TextRequestCandidate? candidate = directText.Pop();
                        directTextWhitespaceModes.Pop();
                        if (candidate is not null)
                        {
                            AddCandidate(candidate, requests, seen);
                            activeDirectTextCandidate = null;
                        }
                        textEligibleChildren.Pop();
                        if (elementGroups.Pop()) groupDepth--;
                    }
                }
                else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                {
                    if (activeDirectTextCandidate is not null)
                    {
                        activeDirectTextCandidate.HasRenderableTextContent |= HasRenderableText(reader, GetTextChunk(), directTextWhitespaceModes.Peek(), workCounter, cancellationToken);
                    }
                }
            }
            if (!hasRoot) throw new SvgException(SvgErrorCode.MalformedInput, "SVG input has no root element.");
        }
        catch (XmlException exception)
        {
            throw new SvgException(SvgErrorCode.MalformedInput, "SVG XML is malformed: " + exception.Message);
        }
        finally
        {
            textChunkReservation?.Dispose();
        }

        char[] GetTextChunk()
        {
            if (textChunk is not null) return textChunk;
            RenderBudget.BudgetReservation reservation = styleBudget.Reserve(ParserCollectionMemory.GetArrayByteCount(ParserWorkCounter.ByteCheckpointSize, sizeof(char)), SvgMemoryCategory.Parser);
            try { textChunk = new char[ParserWorkCounter.ByteCheckpointSize]; }
            catch
            {
                reservation.Dispose();
                throw;
            }
            textChunkReservation = reservation;
            return textChunk;
        }

        return requests.ToArray();
    }

    private static void AddCandidate(TextRequestCandidate? candidate, List<SvgFontFaceRequest> requests, HashSet<TextRequestKey> seen)
    {
        if (candidate is null || candidate.HasChildElement || !candidate.HasRenderableTextContent) return;
        SvgFontFaceRequest request = candidate.Request;
        if (!seen.Add(new TextRequestKey(request.Family, request.Weight, request.Italic))) return;
        if (requests.Count == MaximumRequests) throw new SvgException(SvgErrorCode.LimitExceeded, "The SVG text font request count exceeds the preparation limit.");
        requests.Add(request);
    }

    private static bool HasRenderableText(XmlReader reader, char[] chunk, SvgTextWhitespaceMode whitespaceMode, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        bool hasRenderableText = false;
        int read;
        do
        {
            read = reader.ReadValueChunk(chunk, 0, chunk.Length);
            if (read != 0) hasRenderableText |= whitespaceMode == SvgTextWhitespaceMode.Preserve
                ? SvgTextWhitespaceNormalizer.HasText(chunk.AsSpan(0, read), workCounter, cancellationToken)
                : SvgTextWhitespaceNormalizer.HasNonWhitespaceText(chunk.AsSpan(0, read), workCounter, cancellationToken);
        } while (read != 0);
        return hasRenderableText;
    }

    private static TextStyle ApplyAttributes(XmlReader reader, TextStyle inherited, InlineStyle styleSheet, ParserWorkCounter workCounter, CancellationToken cancellationToken, out string? specifiedWhiteSpace)
    {
        InlineStyle inlineStyle = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
        SvgComputedStyle computedStyle = SvgComputedStyle.Resolve(reader, inlineStyle, styleSheet);
        specifiedWhiteSpace = inlineStyle.WhiteSpace ?? styleSheet.WhiteSpace;
        SvgInheritedTextStyle text = SvgTextInheritance.Resolve(reader, inherited.Text, inlineStyle.TextFont.Overlay(styleSheet.TextFont), specifiedWhiteSpace, inlineStyle.LetterSpacing ?? styleSheet.LetterSpacing);
        AxisTransform transform = inherited.Transform.Compose(SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform")));
        if (reader.LocalName is "g" or "a")
        {
            SvgResolvedPresentation presentation = SvgContainerPresentationResolver.Resolve(computedStyle, inherited.Paint, inherited.IsDisplayed, inherited.IsVisible, SvgRootParser.CssNamedColors);
            if (presentation.Opacity != 255 && inherited.HasOpacityScope) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Nested group opacity requires separate compositing calibration.");
            return new TextStyle(text, presentation.Paint, presentation.IsDisplayed, presentation.IsVisible, transform, computedStyle.FillRule, inherited.HasOpacityScope || presentation.Opacity != 255);
        }
        SvgResolvedShapePresentation textPresentation = SvgShapePresentationResolver.Resolve(computedStyle, inherited.Paint, inherited.IsDisplayed, inherited.IsVisible, false, SvgRootParser.CssNamedColors);
        bool isDisplayed = inherited.IsDisplayed && SvgPresentationResolver.ParseDisplay(computedStyle.Display);
        bool isVisible = SvgPresentationResolver.ParseVisibility(computedStyle.Visibility, inherited.IsVisible);
        return new TextStyle(text, textPresentation.Paint, isDisplayed, isVisible, transform, computedStyle.FillRule, inherited.HasOpacityScope);
    }

    private readonly record struct TextStyle(SvgInheritedTextStyle Text, SvgPaint Paint, bool IsDisplayed, bool IsVisible, AxisTransform Transform, string? FillRule, bool HasOpacityScope)
    {
        internal bool HasFill => Paint.HasFill;
    }

    private readonly record struct TextRequestKey(string Family, int Weight, bool Italic);

    private sealed class TextRequestCandidate(SvgFontFaceRequest request, SvgTextWhitespaceMode whitespaceMode)
    {
        internal SvgFontFaceRequest Request { get; } = request;

        internal SvgTextWhitespaceMode WhitespaceMode { get; } = whitespaceMode;

        internal bool HasChildElement { get; set; }

        internal bool HasRenderableTextContent { get; set; }
    }

    private sealed class TextRequestKeyComparer : IEqualityComparer<TextRequestKey>
    {
        internal static TextRequestKeyComparer Instance { get; } = new();

        public bool Equals(TextRequestKey left, TextRequestKey right)
            => left.Weight == right.Weight && left.Italic == right.Italic && left.Family.Equals(right.Family, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(TextRequestKey value)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Family), value.Weight, value.Italic);
    }
}
