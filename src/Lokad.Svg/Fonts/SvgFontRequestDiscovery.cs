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
        RenderBudget budget = new(limits.MaximumAccountedBytes);
        return Discover(svg, limits, workCounter, budget, cancellationToken);
    }

    /// <summary>Discovers admitted text requests while reserving all discovery control storage from the supplied preparation budget.</summary>
    internal static SvgFontFaceRequest[] Discover(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, ParserWorkCounter workCounter, RenderBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(workCounter);
        ArgumentNullException.ThrowIfNull(budget);
        if (svg.Length > limits.MaximumInputBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG input exceeds the configured input limit.");
        // Reserve the same conservative reader workspace as root parsing before either XmlReader is created.
        using RenderBudget.BudgetReservation xmlReaderWorkspace = budget.Reserve(SvgRootParser.GetXmlReaderWorkspaceBytes(svg.Length, limits.MaximumAttributesPerElement), SvgMemoryCategory.Parser);
        using var styleRules = new BudgetedParserList<CssRule>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        using var styleStrings = new BudgetedParserStringStore(budget);
        using var requestKeys = new BudgetedParserList<TextRequestKey>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        using var inheritedLevels = new BudgetedParserList<TextStyle>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        using var directTextLevels = new BudgetedParserList<DiscoveryCandidate>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        using var whitespaceLevels = new BudgetedParserList<SvgTextWhitespaceMode>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        using var eligibleLevels = new BudgetedParserList<bool>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        using var groupLevels = new BudgetedParserList<bool>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        using var viewportSizes = new BudgetedParserList<SvgViewportSize>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        using var clipDepths = new BudgetedParserList<int>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        using var switchScopes = new BudgetedParserList<SwitchScope>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
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
            bool sawStyleableNode = false;
            bool hasUnsupportedContent = false;
            int nodeCount = 0;
            int inertDepth = -1;
            int definitionsDepth = -1;
            int symbolDepth = -1;
            int groupDepth = 0;
            int skipDepth = -1;
            int activeCandidateLevel = -1;
            double? rootWidth = null;
            double? rootHeight = null;
            while (reader.Read())
            {
                workCounter.Consume(cancellationToken);
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (!hasRoot)
                    {
                        SvgStructuralAdmission.ThrowIfNotSvgRoot(reader);
                        hasRoot = true;
                        rootDepth = reader.Depth;
                        rootId = reader.GetAttribute("id");
                    }
                    SvgStructuralAdmission.ValidateElementStructure(reader, limits);
                    if (nodeCount >= limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                    nodeCount++;
                    bool isSvgElement = reader.NamespaceURI == SvgNamespace;
                    bool insideDirectText = activeCandidateLevel >= 0;
                    bool directChild = reader.Depth == rootDepth + 1;
                    bool hasGroupAncestor = groupDepth != 0;
                    bool parentIsGroup = groupLevels.Count > 0 && groupLevels[groupLevels.Count - 1];
                    SvgTextWhitespaceMode inheritedWhitespaceMode = whitespaceLevels.Count == 0 ? SvgTextWhitespaceMode.Default : whitespaceLevels[whitespaceLevels.Count - 1];
                    SvgTextWhitespaceMode childWhitespaceMode = inheritedWhitespaceMode;
                    InlineStyle tspanStyleSheet = insideDirectText && isSvgElement && reader.LocalName == "tspan" ? SvgStyleSheet.Resolve(reader, styleRules, directChild, hasGroupAncestor, parentIsGroup, workCounter, cancellationToken) : default;
                    bool admittedTextSpan = insideDirectText && isSvgElement && reader.LocalName == "tspan" && SvgTextSyntax.TryParseTspanWhitespaceMode(reader, inheritedWhitespaceMode, tspanStyleSheet, workCounter, cancellationToken, out childWhitespaceMode);
                    if (!admittedTextSpan && activeCandidateLevel >= 0)
                    {
                        DiscoveryCandidate active = directTextLevels[activeCandidateLevel];
                        if (active.HasRequest)
                        {
                            directTextLevels[activeCandidateLevel] = active with { HasChildElement = true };
                        }
                    }
                    bool parentAllowsText = eligibleLevels.Count == 0 || eligibleLevels[eligibleLevels.Count - 1];
                    bool skipped = skipDepth >= 0 && reader.Depth > skipDepth;
                    bool switchChild = switchScopes.Count > 0 && reader.Depth == switchScopes[switchScopes.Count - 1].Depth + 1;
                    bool sterile = skipped;
                    if (switchChild)
                    {
                        SwitchScope top = switchScopes[switchScopes.Count - 1];
                        if (top.FirstChildConsumed)
                        {
                            if (!reader.IsEmptyElement) skipDepth = reader.Depth;
                            sterile = true;
                        }
                        else switchScopes[switchScopes.Count - 1] = top with { FirstChildConsumed = true };
                    }
                    bool rootElement = reader.Depth == rootDepth;
                    TextStyle style;
                    if (inheritedLevels.Count == 0)
                    {
                        SvgRootAttributes root = SvgRootAttributes.Parse(reader, SvgRootParser.CssNamedColors, workCounter, cancellationToken);
                        rootWidth = root.Width;
                        rootHeight = root.Height;
                        style = new TextStyle(root.InheritedTextStyle, root.InheritedPresentation.Paint, root.InheritedPresentation.IsDisplayed, root.InheritedPresentation.IsVisible, root.InheritedPresentation.Transform, null, root.Opacity != 255);
                    }
                    else style = inheritedLevels[inheritedLevels.Count - 1];
                    bool rootStyleElement = isSvgElement && reader.LocalName == "style" && directChild;
                    if (rootStyleElement)
                    {
                        SvgRootStyleSheetParser.ThrowIfLate(sawStyleableNode);
                        SvgRootStyleSheetParser.ThrowIfAttributed(reader);
                        using OwnedStyleSheetText styleText = SvgStyleSheet.ReadElementText(reader, budget, workCounter, cancellationToken);
                        SvgStyleSheet.Parse(styleText.Value, limits.MaximumNodes, styleRules, styleStrings, budget, workCounter, cancellationToken);
                        if (SvgStyleSheet.TargetsRootId(rootId, styleRules, workCounter, cancellationToken)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet rules targeting the SVG root are not implemented.");
                    }
                    if (rootStyleElement) continue;
                    if (!sterile && isSvgElement && SvgStructuralVocabulary.IsDefinitionsContainer(reader.LocalName) && (directChild || hasGroupAncestor) && definitionsDepth < 0 && !reader.IsEmptyElement) definitionsDepth = reader.Depth;
                    if (!sterile && isSvgElement && reader.LocalName == "symbol" && definitionsDepth >= 0 && reader.Depth == definitionsDepth + 1 && !reader.IsEmptyElement) symbolDepth = reader.Depth;
                    if (!sterile && isSvgElement && definitionsDepth >= 0 && (reader.Depth == definitionsDepth + 1 || reader.Depth == symbolDepth + 1) && SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName)) sawStyleableNode = true;
                    if (!sterile && isSvgElement && SvgStructuralVocabulary.IsStyleScriptOrAnimationElement(reader.LocalName)) hasUnsupportedContent = true;
                    bool textElement = isSvgElement && reader.LocalName == "text";
                    bool isTextEligible = !sterile && parentAllowsText && textElement;
                    bool childAllowsText = !sterile && parentAllowsText && isSvgElement && clipDepths.Count == 0 && (reader.Depth == rootDepth || SvgStructuralVocabulary.IsGroupOrLinkContainer(reader.LocalName));
                    bool plainViewport = false;
                    if (!sterile && inertDepth < 0 && !insideDirectText && parentAllowsText && isSvgElement && !rootElement && (directChild || viewportSizes.Count > 0) && reader.LocalName == "svg")
                    {
                        SvgElementAttributePolicy.ValidateNestedViewport(reader, workCounter, cancellationToken);
                        double? baseWidth = viewportSizes.Count == 0 ? rootWidth : viewportSizes[^1].Width;
                        double? baseHeight = viewportSizes.Count == 0 ? rootHeight : viewportSizes[^1].Height;
                        SvgNestedViewportSyntax viewport = SvgNestedViewportSyntax.Parse(reader, baseWidth, baseHeight);
                        plainViewport = true;
                        sawStyleableNode = true;
                        if (!reader.IsEmptyElement) viewportSizes.Add(new SvgViewportSize(viewport.Width, viewport.Height, reader.Depth));
                    }
                    if (!sterile && isSvgElement && !rootElement && reader.LocalName == "clipPath") sawStyleableNode = true;
                    if (!sterile && isSvgElement && !rootElement && reader.LocalName == "mask") sawStyleableNode = true;
                    if (!sterile && isSvgElement && !rootElement && reader.LocalName == "pattern") sawStyleableNode = true;
                    if (!sterile && isSvgElement && !rootElement && reader.LocalName is ("linearGradient" or "radialGradient")) sawStyleableNode = true;
                    if (!sterile && inertDepth < 0 && !insideDirectText && parentAllowsText && isSvgElement && SvgStructuralVocabulary.IsGroupOrLinkContainer(reader.LocalName) && reader.GetAttribute("clip-path") is not null && !reader.IsEmptyElement) clipDepths.Add(reader.Depth);
                    if (!sterile && !insideDirectText && isSvgElement && reader.LocalName == "switch" && (parentAllowsText || viewportSizes.Count > 0 || clipDepths.Count > 0) && !reader.IsEmptyElement) switchScopes.Add(new SwitchScope(reader.Depth, false));
                    bool isKnownContent = isSvgElement && reader.LocalName is ("svg" or "g" or "a" or "switch" or "text" or "rect" or "line" or "use" or "path" or "polygon" or "polyline" or "style" or "defs" or "title" or "desc" or "metadata" or "symbol" or "clipPath" or "mask" or "image" or "pattern" or "linearGradient" or "radialGradient" or "stop");
                    bool unsupportedContainer = (!rootElement && reader.LocalName is ("style" or "symbol")) || (!rootElement && reader.LocalName == "svg" && !plainViewport) || (!directChild && reader.LocalName is ("title" or "desc" or "metadata")) || (!directChild && !hasGroupAncestor && reader.LocalName == "defs");
                    bool nonemptyRenderedLeaf = !reader.IsEmptyElement && reader.LocalName is ("rect" or "line" or "use" or "path" or "polygon" or "polyline" or "image");
                    bool rendererRejectsKnownContent = isSvgElement && (unsupportedContainer || nonemptyRenderedLeaf);
                    // Retain requests admitted before this point: lowering still paints them before conversion rejects the unsupported sibling, so clearing here would omit a font lowering requires.
                    if (!sterile && !hasUnsupportedContent && inertDepth < 0 && !insideDirectText && parentAllowsText && (!isKnownContent || rendererRejectsKnownContent))
                    {
                        hasUnsupportedContent = true;
                    }
                    if (!sterile && inertDepth < 0 && !insideDirectText && parentAllowsText && isSvgElement)
                    {
                        if (!rootElement && reader.LocalName == "switch") SvgElementAttributePolicy.ValidateSwitch(reader, workCounter, cancellationToken);
                        else if (!rootElement && SvgStructuralVocabulary.IsGroupOrLinkContainer(reader.LocalName)) SvgElementAttributePolicy.ValidateGroup(reader, workCounter, cancellationToken);
                        else if ((directChild || hasGroupAncestor) && SvgStructuralVocabulary.IsDefinitionsContainer(reader.LocalName)) SvgElementAttributePolicy.ValidateDefinitionContainer(reader, workCounter, cancellationToken);
                    }
                    if (!sterile && inertDepth >= 0 && reader.Depth == inertDepth + 1 && isSvgElement)
                    {
                        if (reader.LocalName == "rect") SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken);
                        else if (reader.LocalName == "line") SvgElementAttributePolicy.ValidateLine(reader, workCounter, cancellationToken);
                        else if (reader.LocalName == "symbol") SvgElementAttributePolicy.ValidateSymbol(reader, workCounter, cancellationToken);
                    }
                    InlineStyle styleSheet = isSvgElement && reader.LocalName is ("g" or "a" or "switch" or "text") ? SvgStyleSheet.Resolve(reader, styleRules, directChild, hasGroupAncestor, parentIsGroup, workCounter, cancellationToken) : default;
                    SvgTextWhitespaceMode inheritedTextWhitespaceMode = style.Text.WhitespaceMode;
                    string? specifiedWhiteSpace = null;
                    if (!sterile && isSvgElement && !rootElement && !insideDirectText && parentAllowsText && reader.LocalName is ("g" or "a" or "switch" or "text")) style = ApplyAttributes(reader, style, styleSheet, workCounter, cancellationToken, out specifiedWhiteSpace);
                    if (!sterile && inertDepth < 0 && isSvgElement && SvgStructuralVocabulary.IsInertContainer(reader.LocalName) && !reader.IsEmptyElement) inertDepth = reader.Depth;
                    SvgTextWhitespaceMode whitespaceMode = SvgTextWhitespaceMode.Default;
                    if (inertDepth < 0 && !insideDirectText && isTextEligible && reader.GetAttribute("transform") is null)
                    {
                        SvgElementAttributePolicy.ValidateText(reader, workCounter, cancellationToken);
                        whitespaceMode = SvgTextSyntax.ParseWhitespaceMode(reader, specifiedWhiteSpace, inheritedTextWhitespaceMode);
                    }
                    if (inertDepth < 0 && !insideDirectText && isTextEligible && style.FillRule is not null && !style.FillRule.Equals("nonzero", StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only the default nonzero fill rule is implemented for direct text.");
                    DiscoveryCandidate candidateState;
                    if (!hasUnsupportedContent && inertDepth < 0 && !insideDirectText && isTextEligible && reader.GetAttribute("transform") is null && style.Transform == AxisTransform.Identity && style.IsDisplayed && style.IsVisible && style.HasFill && style.Text.Family is string family)
                    {
                        candidateState = new DiscoveryCandidate(true, family, style.Text.Weight, style.Text.Italic, whitespaceMode, false, false);
                    }
                    else
                    {
                        candidateState = new DiscoveryCandidate(false, null, 400, false, SvgTextWhitespaceMode.Default, false, false);
                    }
                    if (candidateState.HasRequest) childWhitespaceMode = candidateState.WhitespaceMode;
                    if (reader.IsEmptyElement)
                    {
                        if (candidateState.HasRequest) AddCandidate(candidateState, requestKeys, workCounter, cancellationToken);
                    }
                    else
                    {
                        inheritedLevels.Add(style);
                        directTextLevels.Add(candidateState);
                        whitespaceLevels.Add(childWhitespaceMode);
                        eligibleLevels.Add(childAllowsText);
                        bool group = isSvgElement && SvgStructuralVocabulary.IsGroupContainer(reader.LocalName);
                        groupLevels.Add(group);
                        if (group) groupDepth++;
                        if (candidateState.HasRequest) activeCandidateLevel = directTextLevels.Count - 1;
                    }
                    if (!sterile && isSvgElement && parentAllowsText && reader.LocalName is ("rect" or "line" or "use" or "path" or "polygon" or "polyline" or "text" or "image")) sawStyleableNode = true;
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.Depth == skipDepth) skipDepth = -1;
                    if (switchScopes.Count > 0 && switchScopes[switchScopes.Count - 1].Depth == reader.Depth) switchScopes.RemoveLast();
                    if (reader.Depth == inertDepth) inertDepth = -1;
                    if (reader.Depth == definitionsDepth) definitionsDepth = -1;
                    if (reader.Depth == symbolDepth) symbolDepth = -1;
                    if (viewportSizes.Count > 0 && viewportSizes[^1].Depth == reader.Depth) viewportSizes.RemoveLast();
                    if (clipDepths.Count > 0 && clipDepths[^1] == reader.Depth) clipDepths.RemoveLast();
                    if (inheritedLevels.Count != 0)
                    {
                        inheritedLevels.RemoveLast();
                        DiscoveryCandidate candidateState = directTextLevels[directTextLevels.Count - 1];
                        directTextLevels.RemoveLast();
                        whitespaceLevels.RemoveLast();
                        eligibleLevels.RemoveLast();
                        bool wasGroup = groupLevels[groupLevels.Count - 1];
                        groupLevels.RemoveLast();
                        if (wasGroup) groupDepth--;
                        if (candidateState.HasRequest)
                        {
                            AddCandidate(candidateState, requestKeys, workCounter, cancellationToken);
                            activeCandidateLevel = -1;
                        }
                    }
                }
                else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                {
                    if (activeCandidateLevel >= 0)
                    {
                        SvgTextWhitespaceMode activeWhitespace = whitespaceLevels[whitespaceLevels.Count - 1];
                        bool hasRenderable = HasRenderableText(reader, GetTextChunk(), activeWhitespace, workCounter, cancellationToken);
                        if (hasRenderable)
                        {
                            DiscoveryCandidate active = directTextLevels[activeCandidateLevel];
                            if (active.HasRequest && !active.HasRenderableTextContent)
                            {
                                directTextLevels[activeCandidateLevel] = active with { HasRenderableTextContent = true };
                            }
                        }
                    }
                }
            }
            if (!hasRoot) throw new SvgException(SvgErrorCode.MalformedInput, "SVG input has no root element.");
            var result = new SvgFontFaceRequest[requestKeys.Count];
            for (int index = 0; index < result.Length; index++)
            {
                workCounter.Consume(cancellationToken);
                TextRequestKey key = requestKeys[index];
                if (key.Family is not string family)
                {
                    throw new SvgException(SvgErrorCode.MalformedInput, "The discovered font request is missing its family.");
                }
                result[index] = new SvgFontFaceRequest(family, key.Weight, key.Italic);
            }
            return result;
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
            RenderBudget.BudgetReservation reservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount(ParserWorkCounter.ByteCheckpointSize, sizeof(char)), SvgMemoryCategory.Parser);
            try { textChunk = new char[ParserWorkCounter.ByteCheckpointSize]; }
            catch
            {
                reservation.Dispose();
                throw;
            }
            textChunkReservation = reservation;
            return textChunk;
        }
    }

    private static void AddCandidate(DiscoveryCandidate candidate, BudgetedParserList<TextRequestKey> requests, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(workCounter);
        if (!candidate.HasRequest || candidate.HasChildElement || !candidate.HasRenderableTextContent)
        {
            return;
        }
        if (candidate.Family is not string family)
        {
            return;
        }
        for (int index = 0; index < requests.Count; index++)
        {
            workCounter.Consume(cancellationToken);
            TextRequestKey existing = requests[index];
            if (existing.Family is not string existingFamily)
            {
                continue;
            }
            if (existing.Weight == candidate.Weight && existing.Italic == candidate.Italic && existingFamily.Equals(family, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        if (requests.Count == MaximumRequests) throw new SvgException(SvgErrorCode.LimitExceeded, "The SVG text font request count exceeds the preparation limit.");
        requests.Add(new TextRequestKey(family, candidate.Weight, candidate.Italic));
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
        specifiedWhiteSpace = SvgInlineStyleParser.MergeWithImportance(inlineStyle.WhiteSpace, inlineStyle.Important, InlineImportantProperties.WhiteSpace, styleSheet.WhiteSpace, styleSheet.Important);
        if (SvgStructuralVocabulary.IsGroupOrLinkContainer(reader.LocalName))
        {
            SvgInheritedContainer resolved = SvgContainerInheritance.Resolve(reader, inlineStyle, computedStyle, styleSheet, new SvgInheritedPresentation(inherited.Paint, inherited.IsDisplayed, inherited.IsVisible, inherited.Transform), inherited.Text, inherited.HasOpacityScope, SvgRootParser.CssNamedColors);
            return new TextStyle(resolved.TextStyle, resolved.Presentation.Paint, resolved.Presentation.IsDisplayed, resolved.Presentation.IsVisible, resolved.Presentation.Transform, resolved.FillRule, resolved.HasOpacityScope);
        }
        SvgInheritedTextStyle text = SvgTextInheritance.Resolve(reader, inherited.Text, SvgInlineStyleParser.MergeFont(inlineStyle.TextFont, inlineStyle.Important, styleSheet.TextFont, styleSheet.Important), specifiedWhiteSpace, SvgInlineStyleParser.MergeWithImportance(inlineStyle.LetterSpacing, inlineStyle.Important, InlineImportantProperties.LetterSpacing, styleSheet.LetterSpacing, styleSheet.Important));
        AxisTransform transform = inherited.Transform.Compose(SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform")));
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

    private readonly record struct DiscoveryCandidate(bool HasRequest, string? Family, int Weight, bool Italic, SvgTextWhitespaceMode WhitespaceMode, bool HasChildElement, bool HasRenderableTextContent);
}
