namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Xml;

/// <summary>Discovers admitted ordered text font requests without acquiring fonts.</summary>
internal static class SvgFontRequestDiscovery
{
    private const int MaximumRequests = 256;
    private const string SvgNamespace = "http://www.w3.org/2000/svg";
    private readonly record struct TextPaintReference(string Id);
    private readonly record struct TextFilterReference(string Id);
    private readonly record struct MarkerFontReference(SvgMarkerFontLengths Lengths);
    private readonly record struct MarkerReference(string Id);

    internal static SvgFontFaceRequest[] Discover(ReadOnlyMemory<byte> svg, CancellationToken cancellationToken)
        => Discover(svg, SvgRenderLimits.Default, cancellationToken);

    /// <summary>Discovers admitted text requests under the same caller-selected structural limits as rendering.</summary>
    internal static SvgFontFaceRequest[] Discover(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        RenderBudget budget = new(limits.MaximumAccountedBytes);
        return Discover(svg, limits, budget, cancellationToken);
    }

    /// <summary>Discovers admitted text requests while reserving all discovery control storage from the supplied preparation budget.</summary>
    internal static SvgFontFaceRequest[] Discover(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, RenderBudget budget, CancellationToken cancellationToken)
    {
        using SvgDocumentPreparation preparation = SvgDocumentPreparation.Create(svg, limits, budget, cancellationToken);
        return Discover(svg, limits, budget, preparation, cancellationToken);
    }

    /// <summary>Borrows the operation's already-collected styles and reader workspace; only discovery control storage is temporary.</summary>
    internal static SvgFontFaceRequest[] Discover(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, RenderBudget budget, SvgDocumentPreparation preparation, CancellationToken cancellationToken)
        => DiscoverCore(svg, limits, budget, preparation, null, cancellationToken);

    /// <summary>Shares request discovery and whitespace normalization while retaining bounded scalar needs for selective acquisition.</summary>
    internal static SvgFontCoverageDiscovery DiscoverWithCoverage(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, CancellationToken cancellationToken)
    {
        RenderBudget budget = new(limits.MaximumAccountedBytes);
        using SvgDocumentPreparation preparation = SvgDocumentPreparation.Create(svg, limits, budget, cancellationToken);
        return DiscoverWithCoverage(svg, limits, budget, preparation, cancellationToken);
    }

    /// <summary>Returns a caller-owned scalar requirement owner, charged through acquisition while borrowing this document preparation.</summary>
    internal static SvgFontCoverageDiscovery DiscoverWithCoverage(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, RenderBudget budget, SvgDocumentPreparation preparation, CancellationToken cancellationToken)
    {
        SvgFontCoverageDiscovery coverage = new(budget, cancellationToken);
        try
        {
            SvgFontFaceRequest[] requests = DiscoverCore(svg, limits, budget, preparation, coverage, cancellationToken);
            coverage.Materialize(requests);
            return coverage;
        }
        catch { coverage.Dispose(); throw; }
    }

    private static SvgFontFaceRequest[] DiscoverCore(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, RenderBudget budget, SvgDocumentPreparation preparation, SvgFontCoverageDiscovery? coverage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(budget);
        if (svg.Length > limits.MaximumInputBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG input exceeds the configured input limit.");
        ArgumentNullException.ThrowIfNull(preparation);
        BudgetedParserList<CssRule> styleRules = preparation.Rules;
        BudgetedParserStringStore styleStrings = preparation.Strings;
        using var requestKeys = new BudgetedParserList<TextRequestKey>(budget, SvgMemoryCategory.Parser, cancellationToken);
        using var inheritedLevels = new BudgetedParserList<TextStyle>(budget, SvgMemoryCategory.Parser, cancellationToken);
        using var directTextLevels = new BudgetedParserList<DiscoveryCandidate>(budget, SvgMemoryCategory.Parser, cancellationToken);
        using var spanCandidates = new BudgetedParserList<DiscoveryCandidate>(budget, SvgMemoryCategory.Parser, cancellationToken);
        using var whitespaceLevels = new BudgetedParserList<SvgTextWhitespaceMode>(budget, SvgMemoryCategory.Parser, cancellationToken);
        using var eligibleLevels = new BudgetedParserList<bool>(budget, SvgMemoryCategory.Parser, cancellationToken);
        using var groupLevels = new BudgetedParserList<bool>(budget, SvgMemoryCategory.Parser, cancellationToken);
        using var viewportSizes = new BudgetedParserList<SvgViewportSize>(budget, SvgMemoryCategory.Parser, cancellationToken);
        using var switchScopes = new BudgetedParserList<SwitchScope>(budget, SvgMemoryCategory.Parser, cancellationToken);
        using var paintServerIds = new BudgetedParserDictionary<string, bool>(budget, SvgMemoryCategory.Parser, 96);
        using var textPaintIds = new BudgetedParserList<TextPaintReference>(budget, SvgMemoryCategory.Parser, cancellationToken);
        using var filterIds = new BudgetedParserDictionary<string, bool>(budget, SvgMemoryCategory.Parser, 96);
        using var textFilterIds = new BudgetedParserList<TextFilterReference>(budget, SvgMemoryCategory.Parser, cancellationToken);
        using var markerFonts = new BudgetedParserDictionary<string, MarkerFontReference>(budget, SvgMemoryCategory.Parser, 128);
        using var markerReferences = new BudgetedParserDictionary<string, MarkerReference>(budget, SvgMemoryCategory.Parser, 96);
        char[]? textChunk = null;
        RenderBudget.BudgetReservation? textChunkReservation = null;
        try
        {
            XmlReaderSettings settings = preparation.Settings;
            SvgInstanceFontDiscovery? instanceFonts = preparation.GetFontInstances();
            SvgPaintResourceSelection? paintResources = preparation.GetPaintResources();
            using SvgCssAncestorContext ancestors = new(styleRules, budget, cancellationToken);
            using ReadOnlyMemoryStream stream = new(svg);
            using XmlReader reader = XmlReader.Create(stream, settings);
            bool hasRoot = false;
            int rootDepth = -1;
            bool hasUnsupportedContent = false;
            int nodeCount = 0;
            int inertDepth = -1;
            int descriptionDepth = -1;
            int definitionsDepth = -1;
            int symbolDepth = -1;
            int patternDepth = -1;
            int patternInheritedInertDepth = -1;
            int groupDepth = 0;
            int skipDepth = -1;
            int activeCandidateLevel = -1;
            bool hasPriorText = false;
            DiscoveryCandidate? pendingWhitespace = null;
            bool activeTextPainted = false;
            int textPathDepth = -1;
            double? rootWidth = null;
            double? rootHeight = null;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                ancestors.Observe(reader, 0);
                if (activeCandidateLevel >= 0 && descriptionDepth >= 0 && reader.NodeType != XmlNodeType.Element)
                {
                    if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == descriptionDepth) descriptionDepth = -1;
                    continue;
                }
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (!hasRoot)
                    {
                        SvgStructuralAdmission.ThrowIfNotSvgRoot(reader);
                        hasRoot = true;
                        rootDepth = reader.Depth;
                    }
                    SvgStructuralAdmission.ValidateElementStructure(reader, limits);
                    if (nodeCount >= limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                    nodeCount++;
                    bool isSvgElement = reader.NamespaceURI == SvgNamespace;
                    if (activeCandidateLevel >= 0 && (descriptionDepth >= 0 || isSvgElement && SvgStructuralVocabulary.IsDescriptiveElement(reader.LocalName)))
                    {
                        if (descriptionDepth >= 0)
                        {
                            if (isSvgElement && SvgStructuralVocabulary.IsStyleScriptOrAnimationElement(reader.LocalName)) hasUnsupportedContent = true;
                        }
                        else if (!reader.IsEmptyElement) descriptionDepth = reader.Depth;
                        // Preserve the enclosing candidate, whitespace and font
                        // coverage chunk while still metering every ignored node.
                        continue;
                    }
                    if (isSvgElement && reader.LocalName is ("linearGradient" or "radialGradient" or "pattern") && reader.GetAttribute("id") is {} paintId)
                        paintServerIds.TryAdd(styleStrings.Copy(paintId), true);
                    if (isSvgElement && reader.LocalName == "filter" && reader.GetAttribute("id") is {} filterId)
                        filterIds.TryAdd(styleStrings.Copy(filterId), true);
                    bool insideDirectText = activeCandidateLevel >= 0;
                    bool directChild = reader.Depth == rootDepth + 1;
                    bool hasGroupAncestor = groupDepth != 0;
                    SvgTextWhitespaceMode inheritedWhitespaceMode = whitespaceLevels.Count == 0 ? SvgTextWhitespaceMode.Default : whitespaceLevels[whitespaceLevels.Count - 1];
                    SvgTextWhitespaceMode childWhitespaceMode = inheritedWhitespaceMode;
                    InlineStyle tspanStyleSheet = insideDirectText && isSvgElement && reader.LocalName == "tspan" ? SvgStyleSheet.Resolve(reader, styleRules, ancestors, cancellationToken) : default;
                    bool admittedTextSpan = false;
                    SvgTextSpanStyle? parsedTextSpan = null;
                    if (insideDirectText && isSvgElement && reader.LocalName == "tspan" && SvgTextSyntax.TryParseTspanStyle(reader, new SvgTextSpanStyle(inheritedWhitespaceMode, 0) { Paint = inheritedLevels[^1].TextPaint(paintResources), Font = inheritedLevels[^1].Text }, tspanStyleSheet, cancellationToken, out SvgTextSpanStyle spanStyle))
                    {
                        childWhitespaceMode = spanStyle.WhitespaceMode;
                        admittedTextSpan = textPathDepth < 0 || spanStyle.PositionOffset is not SvgTextPositionOffset offset || offset.X is null && offset.Y is null;
                        if (admittedTextSpan) parsedTextSpan = spanStyle;
                    }
                    if (insideDirectText && isSvgElement && reader.LocalName == "textPath" && reader.Depth == rootDepth + activeCandidateLevel + 1)
                    {
                        try
                        {
                            _ = SvgTextPathSyntax.Parse(reader, cancellationToken);
                            childWhitespaceMode = SvgTextSyntax.ParseWhitespaceMode(reader, null, inheritedWhitespaceMode);
                            admittedTextSpan = true;
                            if (!reader.IsEmptyElement) { textPathDepth = reader.Depth; coverage?.BeginText(); }
                        }
                        catch (SvgException exception) when (exception.ErrorCode == SvgErrorCode.UnsupportedFeature) { admittedTextSpan = false; }
                    }
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
                        // systemLanguage selection evaluates against an empty locale set, so language-tagged
                        // switch children never match: skip them without consuming the first-child slot.
                        if (reader.GetAttribute("systemLanguage") is not null)
                        {
                            if (!reader.IsEmptyElement) skipDepth = reader.Depth;
                            sterile = true;
                        }
                        else if (top.FirstChildConsumed)
                        {
                            if (!reader.IsEmptyElement) skipDepth = reader.Depth;
                            sterile = true;
                        }
                        else switchScopes[switchScopes.Count - 1] = top with { FirstChildConsumed = true };
                    }
                    if (descriptionDepth >= 0 && reader.Depth > descriptionDepth)
                    {
                        if (isSvgElement && SvgStructuralVocabulary.IsStyleScriptOrAnimationElement(reader.LocalName)) hasUnsupportedContent = true;
                        sterile = true;
                    }
                    else if (!sterile && isSvgElement && SvgStructuralVocabulary.IsDescriptiveElement(reader.LocalName) && !reader.IsEmptyElement)
                        descriptionDepth = reader.Depth;
                    bool rootElement = reader.Depth == rootDepth;
                    TextStyle style;
                    if (inheritedLevels.Count == 0)
                    {
                        SvgRootAttributes root = SvgRootAttributes.Parse(reader, SvgRootParser.CssNamedColors, styleRules, cancellationToken);
                        rootWidth = root.UserViewportWidth;
                        rootHeight = root.UserViewportHeight;
                        style = new TextStyle(root.InheritedTextStyle, root.InheritedPresentation.Paint, root.InheritedPresentation.IsDisplayed, root.InheritedPresentation.IsVisible, root.InheritedPresentation.Transform, null, root.InheritedStroke.Stroke) { Markers = root.InheritedPresentation.Markers, OwnOpacity = root.InheritedPresentation.ElementOpacity, ElementDisplay = root.InheritedPresentation.ElementDisplay, PaintServerFill = root.InheritedPresentation.PaintServerFill, StrokeStyle = root.InheritedStroke, PaintOrder = root.InheritedPresentation.PaintOrder };
                    }
                    else style = inheritedLevels[inheritedLevels.Count - 1];
                    bool collectedStyleElement = isSvgElement && reader.LocalName == "style"
                        && (directChild || definitionsDepth >= 0 && reader.Depth == definitionsDepth + 1
                            || !sterile && !insideDirectText && inertDepth < 0 && (hasGroupAncestor || viewportSizes.Count > 0));
                    if (collectedStyleElement)
                    {
                        SvgRootStyleSheetParser.Consume(reader, budget, cancellationToken);
                        continue;
                    }
                    if (!sterile && isSvgElement && SvgStructuralVocabulary.IsDefinitionsContainer(reader.LocalName) && (directChild || hasGroupAncestor || viewportSizes.Count > 0) && definitionsDepth < 0 && !reader.IsEmptyElement) definitionsDepth = reader.Depth;
                    bool definitionResourcesDisplayed = definitionsDepth < 0 || reader.Depth != definitionsDepth + 1 || style.ElementDisplay != SvgDisplay.None;
                    bool symbolDefinition = !sterile && isSvgElement && reader.LocalName is ("symbol" or "marker")
                        && SvgStructuralVocabulary.IsSymbolDefinitionLocation(directChild, parentAllowsText && hasGroupAncestor, definitionsDepth, symbolDepth, reader.Depth);
                    if (symbolDefinition)
                    {
                        if (reader.LocalName == "marker") ObserveMarkerDefinition(reader, style.Text, definitionResourcesDisplayed);
                        else SvgElementAttributePolicy.ValidateSymbol(reader, cancellationToken);
                        if (!reader.IsEmptyElement) symbolDepth = reader.Depth;
                    }
                    if (!sterile && isSvgElement && SvgStructuralVocabulary.IsStyleScriptOrAnimationElement(reader.LocalName)) hasUnsupportedContent = true;
                    bool patternDefinition = !sterile && isSvgElement && reader.LocalName == "pattern" && patternDepth < 0
                        && (definitionsDepth >= 0 ? reader.Depth == definitionsDepth + 1 : directChild || parentAllowsText && hasGroupAncestor);
                    bool patternHasSize = true;
                    if (patternDefinition)
                    {
                        SvgElementAttributePolicy.ValidatePattern(reader, cancellationToken);
                        parentAllowsText = true;
                        double? patternWidth = reader.GetAttribute("width") is {} width ? SvgGeometrySyntax.ParseRectangleCoordinate(width) : null;
                        double? patternHeight = reader.GetAttribute("height") is {} height ? SvgGeometrySyntax.ParseRectangleCoordinate(height) : null;
                        bool noTemplate = reader.GetAttribute("href") is null && reader.GetAttribute("href", "http://www.w3.org/1999/xlink") is null;
                        patternHasSize = patternWidth != 0 && patternHeight != 0 && (!noTemplate || patternWidth is not null && patternHeight is not null);
                        patternHasSize |= paintResources?.RequiresPatternContent(reader.GetAttribute("id")) == true;
                        if (!reader.IsEmptyElement)
                        {
                            patternDepth = reader.Depth;
                            patternInheritedInertDepth = inertDepth;
                            inertDepth = -1;
                        }
                    }
                    bool textElement = isSvgElement && reader.LocalName == "text";
                    bool isTextEligible = !sterile && parentAllowsText && textElement;
                    bool childAllowsText = !sterile && parentAllowsText && isSvgElement && (reader.Depth == rootDepth || reader.LocalName == "svg" || patternDefinition || SvgStructuralVocabulary.IsGroupOrLinkContainer(reader.LocalName));
                    bool plainViewport = false;
                    bool emptyViewport = false;
                    if (!sterile && inertDepth < 0 && !insideDirectText && parentAllowsText && isSvgElement && !rootElement && (directChild || hasGroupAncestor || viewportSizes.Count > 0) && reader.LocalName == "svg")
                    {
                        SvgElementAttributePolicy.ValidateNestedViewport(reader, cancellationToken);
                        double? baseWidth = viewportSizes.Count == 0 ? rootWidth : viewportSizes[^1].Width;
                        double? baseHeight = viewportSizes.Count == 0 ? rootHeight : viewportSizes[^1].Height;
                        SvgNestedViewportSyntax viewport = SvgNestedViewportSyntax.Parse(reader, baseWidth, baseHeight);
                        emptyViewport = viewport.Width == 0 || viewport.Height == 0;
                        SvgViewBox? viewBox = SvgGeometrySyntax.ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio"), cancellationToken);
                        plainViewport = true;
                        if (!reader.IsEmptyElement) viewportSizes.Add(new SvgViewportSize(viewBox?.Width ?? viewport.Width, viewBox?.Height ?? viewport.Height, reader.Depth));
                    }
                    if (!sterile && !insideDirectText && isSvgElement && reader.LocalName == "switch" && (parentAllowsText || viewportSizes.Count > 0) && !reader.IsEmptyElement) switchScopes.Add(new SwitchScope(reader.Depth, false));
                    bool isKnownContent = isSvgElement && (SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName) || reader.LocalName is ("svg" or "g" or "a" or "switch" or "text" or "use" or "style" or "defs" or "title" or "desc" or "metadata" or "symbol" or "marker" or "clipPath" or "mask" or "image" or "pattern" or "linearGradient" or "radialGradient" or "stop"));
                    bool isInertForeignElement = !isSvgElement && reader.IsEmptyElement;
                    bool unsupportedContainer = (!rootElement && (reader.LocalName == "style" || (reader.LocalName == "symbol" && !symbolDefinition))) || (!rootElement && reader.LocalName == "svg" && !plainViewport) || (!directChild && !hasGroupAncestor && viewportSizes.Count == 0 && reader.LocalName == "defs");
                    bool rendererRejectsKnownContent = isSvgElement && unsupportedContainer;
                    bool consumeGeometryBody = !sterile && !insideDirectText && isSvgElement
                        && (SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName) || reader.LocalName is "use" or "image")
                        && !reader.IsEmptyElement && (parentAllowsText || definitionsDepth >= 0 && reader.Depth == definitionsDepth + 1);
                    // Retain requests admitted before this point: lowering still paints them before conversion rejects the unsupported sibling, so clearing here would omit a font lowering requires.
                    if (!sterile && !hasUnsupportedContent && inertDepth < 0 && !insideDirectText && parentAllowsText && ((!isKnownContent && !isInertForeignElement) || rendererRejectsKnownContent))
                    {
                        hasUnsupportedContent = true;
                    }
                    if (!sterile && inertDepth < 0 && !insideDirectText && parentAllowsText && isSvgElement)
                    {
                        if (!rootElement && reader.LocalName == "switch") SvgElementAttributePolicy.ValidateSwitch(reader, cancellationToken);
                        else if (!rootElement && SvgStructuralVocabulary.IsGroupOrLinkContainer(reader.LocalName)) SvgElementAttributePolicy.ValidateGroup(reader, cancellationToken);
                        else if ((directChild || hasGroupAncestor || viewportSizes.Count > 0) && SvgStructuralVocabulary.IsDefinitionsContainer(reader.LocalName)) SvgElementAttributePolicy.ValidateDefinitionContainer(reader, cancellationToken);
                    }
                    if (!sterile && inertDepth >= 0 && reader.Depth == inertDepth + 1 && isSvgElement)
                    {
                        if (reader.LocalName == "rect") SvgElementAttributePolicy.ValidateRectangle(reader, cancellationToken);
                        else if (reader.LocalName == "line") SvgElementAttributePolicy.ValidateLine(reader, cancellationToken);
                        else if (reader.LocalName == "symbol") SvgElementAttributePolicy.ValidateSymbol(reader, cancellationToken);
                        else if (reader.LocalName == "marker") ObserveMarkerDefinition(reader, style.Text, definitionResourcesDisplayed);
                    }
                    InlineStyle styleSheet = isSvgElement && reader.LocalName is ("svg" or "defs" or "g" or "a" or "switch" or "pattern" or "text" or "use" or "mask") ? SvgStyleSheet.Resolve(reader, styleRules, ancestors, cancellationToken) : default;
                    if (!sterile && !insideDirectText && isSvgElement && reader.LocalName == "mask"
                        && (directChild || parentAllowsText && hasGroupAncestor || definitionsDepth >= 0 && reader.Depth == definitionsDepth + 1))
                        instanceFonts?.RegisterMask(reader, SourceContext(style), styleSheet, definitionResourcesDisplayed,
                            new(viewportSizes.Count == 0 ? rootWidth : viewportSizes[^1].Width, viewportSizes.Count == 0 ? rootHeight : viewportSizes[^1].Height));
                    SvgTextWhitespaceMode inheritedTextWhitespaceMode = style.Text.WhitespaceMode;
                    string? specifiedWhiteSpace = null;
                    // Reset definition-ancestor display before applying the resource's own declaration.
                    if (patternDefinition) style = style with { Metrics = default, IsDisplayed = patternHasSize && paintResources?.IsPatternDisplayed(reader.GetAttribute("id")) != false
                        && (paintResources?.RequiresPatternContent(reader.GetAttribute("id")) ?? true) };
                    if (!sterile && isSvgElement && !rootElement && !insideDirectText && parentAllowsText && reader.LocalName is ("svg" or "defs" or "g" or "a" or "switch" or "pattern" or "text" or "use")) style = ApplyAttributes(reader, style, styleSheet, paintResources, cancellationToken, out specifiedWhiteSpace);
                    if (emptyViewport) style = style with { IsDisplayed = false };
                    if (!sterile && parsedTextSpan is SvgTextSpanStyle parsedSpan)
                    {
                        SvgTextPaintStyle paint = parsedSpan.Paint;
                        style = style with { Text = parsedSpan.Font, Paint = paint.Paint, PaintServerFill = paint.FillServer, IsDisplayed = paint.IsDisplayed, IsVisible = paint.IsVisible, Stroke = paint.Stroke,
                            ElementDisplay = paint.ElementDisplay, StrokeStyle = paint.StrokeStyle, PaintOrder = paint.PaintOrder, NonScalingStroke = paint.NonScalingStroke };
                    }
                    if (!sterile && !hasUnsupportedContent && inertDepth < 0 && !insideDirectText && parentAllowsText && isSvgElement
                        && reader.LocalName is "line" or "path" or "polygon" or "polyline")
                    {
                        InlineStyle leafSheet = SvgStyleSheet.Resolve(reader, styleRules, ancestors, cancellationToken);
                        SvgComputedStyle leaf = SvgComputedStyleResolver.Resolve(reader, leafSheet, cancellationToken).ResolveDisplay(style.ElementDisplay);
                        if (style.IsDisplayed && (leaf.GetDisplay() != SvgDisplay.None)
                            && SvgPresentationResolver.ParseVisibility(leaf.Visibility, style.IsVisible)
                            && (reader.LocalName != "path" || !string.IsNullOrWhiteSpace(reader.GetAttribute("d"))))
                            ObserveMarkers(SvgMarkerStyle.Resolve(leaf, style.Markers));
                    }
                    if (!sterile && !hasUnsupportedContent && inertDepth < 0 && !insideDirectText && parentAllowsText && isSvgElement)
                    {
                        if (reader.LocalName == "use") instanceFonts?.AddUse(reader, SourceContext(style));
                        if (instanceFonts is not null && SvgLocalUrl.ReadReference(reader, "mask") is not null)
                        {
                            InlineStyle targetSheet = SvgStyleSheet.Resolve(reader, styleRules, ancestors, cancellationToken);
                            SvgComputedStyle target = SvgComputedStyleResolver.Resolve(reader, targetSheet, cancellationToken, true).ResolveDisplay(style.ElementDisplay);
                            SvgInheritedContainer maskContext = SourceContext(style);
                            maskContext = maskContext with { Presentation = maskContext.Presentation with
                            { IsDisplayed = style.IsDisplayed && (target.GetDisplay() != SvgDisplay.None),
                                IsVisible = SvgPresentationResolver.ParseVisibility(target.Visibility, style.IsVisible) } };
                            instanceFonts.AddMask(reader, maskContext, target.Filter);
                        }
                    }
                    if (!sterile && inertDepth < 0 && isSvgElement && (SvgStructuralVocabulary.IsInertContainer(reader.LocalName) || symbolDefinition) && !reader.IsEmptyElement) inertDepth = reader.Depth;
                    SvgTextWhitespaceMode whitespaceMode = SvgTextWhitespaceMode.Default;
                    if (inertDepth < 0 && !insideDirectText && isTextEligible)
                    {
                        SvgElementAttributePolicy.ValidateText(reader, cancellationToken);
                        whitespaceMode = SvgTextSyntax.ParseWhitespaceMode(reader, specifiedWhiteSpace, inheritedTextWhitespaceMode);
                    }
                    // SVG text ignores valid fill-rule values; discovery still validates the admitted syntax.
                    if (inertDepth < 0 && !insideDirectText && isTextEligible) SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
                    DiscoveryCandidate candidateState;
                    if (!hasUnsupportedContent && inertDepth < 0 && !insideDirectText && isTextEligible && style.Text.Family is string family)
                    {
                        candidateState = new DiscoveryCandidate(true, family, style.Text.Weight, style.Text.Italic, whitespaceMode, false, false) { Filter = style.Filter, SourceContext = SvgSourceContext.Capture(reader) };
                    }
                    else
                    {
                        candidateState = new DiscoveryCandidate(false, null, 400, false, SvgTextWhitespaceMode.Default, false, false);
                    }
                    if (candidateState.HasRequest) childWhitespaceMode = candidateState.WhitespaceMode;
                    if (consumeGeometryBody)
                    {
                        SvgLeafSubtree subtree = SvgStructuralAdmission.ConsumeLeafSubtree(reader, limits, nodeCount, cancellationToken);
                        nodeCount = subtree.NodeCount;
                        hasUnsupportedContent |= subtree.HasUnsupportedChildren;
                    }
                    else if (reader.IsEmptyElement)
                    {
                        if (candidateState.HasRequest) AddCandidate(candidateState, requestKeys, cancellationToken);
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
                        if (candidateState.HasRequest) { activeCandidateLevel = directTextLevels.Count - 1; activeTextPainted = false; hasPriorText = false; pendingWhitespace = null; coverage?.BeginText(); }
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.Depth == textPathDepth) { textPathDepth = -1; hasPriorText = false; pendingWhitespace = null; coverage?.BeginText(); }
                    if (reader.Depth == skipDepth) skipDepth = -1;
                    if (switchScopes.Count > 0 && switchScopes[switchScopes.Count - 1].Depth == reader.Depth) switchScopes.RemoveLast();
                    if (reader.Depth == inertDepth) inertDepth = -1;
                    if (reader.Depth == descriptionDepth) descriptionDepth = -1;
                    if (reader.Depth == definitionsDepth) definitionsDepth = -1;
                    if (reader.Depth == symbolDepth) symbolDepth = -1;
                    if (reader.Depth == patternDepth) { patternDepth = -1; inertDepth = patternInheritedInertDepth; patternInheritedInertDepth = -1; }
                    if (viewportSizes.Count > 0 && viewportSizes[^1].Depth == reader.Depth) viewportSizes.RemoveLast();
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
                            if (activeTextPainted)
                            {
                                AddCandidate(candidateState, requestKeys, cancellationToken);
                                if (candidateState.Filter is {} filter) ObserveFilter(filter);
                            }
                            if (activeTextPainted && !candidateState.HasChildElement)
                                foreach (DiscoveryCandidate span in spanCandidates.AsSpan()) AddCandidate(span, requestKeys, cancellationToken);
                            while (spanCandidates.Count != 0) spanCandidates.RemoveLast();
                            activeCandidateLevel = -1;
                        }
                    }
                }
                else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                {
                    if (activeCandidateLevel >= 0)
                    {
                        SvgTextWhitespaceMode activeWhitespace = whitespaceLevels[whitespaceLevels.Count - 1];
                        SvgTextPaintStyle paint = inheritedLevels[^1].TextPaint(paintResources);
                        SvgInheritedTextStyle currentFont = inheritedLevels[^1].Text;
                        SvgFontCoverageDiscovery? visibleCoverage = paint.IsDisplayed ? coverage : null;
                        visibleCoverage?.SetFont(currentFont);
                        bool hasRenderable = HasRenderableText(reader, GetTextChunk(), activeWhitespace, visibleCoverage, cancellationToken, out bool hasCharacters, out bool endsWithCollapsible);
                        if (hasRenderable && paint.IsDisplayed && pendingWhitespace is {} separator)
                        {
                            ObserveFont(separator);
                            pendingWhitespace = null;
                        }
                        if (hasRenderable)
                        {
                            paint.ValidateEmission();
                            if (paint.GlyphPaint.FillServer is {} fillValue && SvgLocalUrl.TryParseId(fillValue, out string fillId))
                                textPaintIds.Add(new(styleStrings.Copy(fillId)));
                            if (paint.GlyphPaint.HasStroke && paint.Stroke is {} stroke && SvgLocalUrl.TryParseId(stroke, out string? strokeId))
                                textPaintIds.Add(new(styleStrings.Copy(strokeId)));
                            activeTextPainted |= paint.GlyphPaint.IsPainted;
                            // Displayed hidden/unpainted glyphs still advance later
                            // painted spans. Acquire their metrics only when the
                            // containing run actually has painted output.
                            if (paint.IsDisplayed) ObserveFont(new(true, currentFont.Family, currentFont.Weight, currentFont.Italic, activeWhitespace, false, true));
                            hasPriorText |= paint.IsDisplayed;
                        }
                        if (paint.IsDisplayed && hasCharacters && hasPriorText && endsWithCollapsible && activeWhitespace == SvgTextWhitespaceMode.Default)
                            pendingWhitespace ??= new(true, currentFont.Family, currentFont.Weight, currentFont.Italic, activeWhitespace, false, true);
                    }
                }
            }
            if (!hasRoot) throw new SvgException(SvgErrorCode.MalformedInput, "SVG input has no root element.");
            instanceFonts?.AddRequests(
                request => AddCandidate(new(true, request.Family, request.Weight, request.Italic, SvgTextWhitespaceMode.Default, false, true) { SourceContext = request.SourceContext }, requestKeys, cancellationToken),
                id => textPaintIds.Add(new(styleStrings.Copy(id))), ObserveFilter, ObserveMarkers, coverage);
            foreach (MarkerReference reference in markerReferences.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!markerFonts.TryGetValue(reference.Id, out MarkerFontReference retained)) continue;
                SvgMarkerFontLengths lengths = retained.Lengths;
                if (lengths.Font.Family is not {} family) throw new SvgException(SvgErrorCode.UnresolvedResource, "Font-relative marker coordinates require a font family.");
                AddCandidate(new(true, family, lengths.Font.Weight, lengths.Font.Italic, SvgTextWhitespaceMode.Default, false, true), requestKeys, cancellationToken);
            }
            foreach (TextPaintReference reference in textPaintIds.AsSpan())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!paintServerIds.TryGetValue(reference.Id, out _))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "A text paint requires a defined local paint server.");
            }
            foreach (TextFilterReference reference in textFilterIds.AsSpan())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!filterIds.TryGetValue(reference.Id, out _))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "A text filter requires a defined local filter.");
            }
            var result = new SvgFontFaceRequest[requestKeys.Count];
            for (int index = 0; index < result.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TextRequestKey key = requestKeys[index];
                if (key.Family is not string family)
                {
                    throw new SvgException(SvgErrorCode.MalformedInput, "The discovered font request is missing its family.");
                }
                result[index] = SvgFontFaceRequest.FromCss(family, key.Weight, key.Italic, key.SourceContext, cancellationToken);
            }
            return result;

            static SvgInheritedContainer SourceContext(TextStyle style)
                => new(new(style.Paint, style.IsDisplayed, style.IsVisible, SvgAffineTransform.Identity)
                    { Markers = style.Markers, TextMetrics = style.Metrics, OwnOpacity = style.ElementOpacity, ElementDisplay = style.ElementDisplay, PaintServerFill = style.PaintServerFill, PaintOrder = style.PaintOrder },
                    style.Text, style.FillRule, style.ElementOpacity.Alpha, style.StrokeStyle with { Stroke = style.Stroke });

            void ObserveMarkerDefinition(XmlReader marker, SvgInheritedTextStyle inherited, bool definitionResourcesDisplayed)
            {
                InlineStyle sheet = SvgStyleSheet.Resolve(marker, styleRules, ancestors, cancellationToken);
                SvgMarkerDefinition definition = SvgMarkerSyntax.Parse(marker,
                    SvgComputedStyleResolver.Resolve(marker, sheet, cancellationToken, true),
                    new(viewportSizes.Count == 0 ? rootWidth : viewportSizes[^1].Width, viewportSizes.Count == 0 ? rootHeight : viewportSizes[^1].Height),
                    inherited, sheet, cancellationToken);
                if (definitionResourcesDisplayed && definition.FontLengths is {} lengths && definition.Width != 0 && definition.Height != 0 && marker.GetAttribute("id") is {} id)
                    markerFonts.TryAdd(styleStrings.Copy(id), new(lengths));
            }

            void ObserveMarkers(SvgMarkerStyle markers)
            {
                Add(markers.Start); Add(markers.Mid); Add(markers.End);
                void Add(string? value)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!SvgMarkerStyle.Active(value)) return;
                    string id = styleStrings.Copy(SvgGradientSyntax.ParsePaintId(value));
                    markerReferences.TryAdd(id, new(id));
                }
            }

            void ObserveFilter(string filter)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (filter.Trim() == "none") return;
                if (!SvgLocalUrl.TryParseId(filter, out string id))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local text filter references are implemented.");
                textFilterIds.Add(new(styleStrings.Copy(id)));
            }

            void ObserveFont(DiscoveryCandidate candidate)
            {
                DiscoveryCandidate active = directTextLevels[activeCandidateLevel];
                if (!active.HasRequest) return;
                bool sameRequest = string.Equals(candidate.Family, active.Family, StringComparison.OrdinalIgnoreCase) && candidate.Weight == active.Weight && candidate.Italic == active.Italic;
                if (sameRequest) directTextLevels[activeCandidateLevel] = active with { HasRenderableTextContent = true };
                else if (spanCandidates.Count == 0 || spanCandidates[^1] != candidate) spanCandidates.Add(candidate);
            }
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
            RenderBudget.BudgetReservation reservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount(4096, sizeof(char)), SvgMemoryCategory.Parser);
            try { textChunk = new char[4096]; }
            catch
            {
                reservation.Dispose();
                throw;
            }
            textChunkReservation = reservation;
            return textChunk;
        }
    }

    private static void AddCandidate(DiscoveryCandidate candidate, BudgetedParserList<TextRequestKey> requests, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
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
            cancellationToken.ThrowIfCancellationRequested();
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
        requests.Add(new TextRequestKey(family, candidate.Weight, candidate.Italic) { SourceContext = candidate.SourceContext });
    }

    private static bool HasRenderableText(XmlReader reader, char[] chunk, SvgTextWhitespaceMode whitespaceMode, SvgFontCoverageDiscovery? coverage, CancellationToken cancellationToken, out bool hasCharacters, out bool endsWithCollapsible)
    {
        bool hasRenderableText = false;
        hasCharacters = false;
        endsWithCollapsible = false;
        int read;
        do
        {
            read = reader.ReadValueChunk(chunk, 0, chunk.Length);
            if (read != 0)
            {
                hasCharacters = true;
                endsWithCollapsible = chunk[read - 1] is ' ' or '\t' or '\r' or '\n';
                hasRenderableText |= whitespaceMode == SvgTextWhitespaceMode.Preserve
                    ? SvgTextWhitespaceNormalizer.HasText(chunk.AsSpan(0, read), cancellationToken)
                    : SvgTextWhitespaceNormalizer.HasNonWhitespaceText(chunk.AsSpan(0, read), cancellationToken);
                coverage?.Observe(chunk.AsSpan(0, read), whitespaceMode);
            }
        } while (read != 0);
        return hasRenderableText;
    }

    private static TextStyle ApplyAttributes(XmlReader reader, TextStyle inherited, InlineStyle styleSheet, SvgPaintResourceSelection? resources, CancellationToken cancellationToken, out string? specifiedWhiteSpace)
    {
        InlineStyle inlineStyle = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), cancellationToken, true);
        SvgComputedStyle computedStyle = SvgComputedStyle.Resolve(reader, inlineStyle, styleSheet).ResolveOpacity(inherited.ElementOpacity).ResolveDisplay(inherited.ElementDisplay);
        specifiedWhiteSpace = SvgInlineStyleParser.MergeWithImportance(inlineStyle.WhiteSpace, inlineStyle.Important, InlineImportantProperties.WhiteSpace, styleSheet.WhiteSpace, styleSheet.Important);
        if (reader.LocalName is "svg" or "defs" or "use" || SvgStructuralVocabulary.IsGroupOrLinkContainer(reader.LocalName))
        {
            SvgInheritedContainer resolved = SvgContainerInheritance.Resolve(reader, inlineStyle, computedStyle, styleSheet, new SvgInheritedPresentation(inherited.Paint, inherited.IsDisplayed, inherited.IsVisible, inherited.Transform) { TextMetrics = inherited.Metrics, OwnOpacity = inherited.ElementOpacity, ElementDisplay = inherited.ElementDisplay, PaintServerFill = inherited.PaintServerFill }, inherited.FillRule, inherited.Text, SvgInheritedStrokeStyle.Default, SvgRootParser.CssNamedColors, resources, cancellationToken);
            return new TextStyle(resolved.TextStyle, resolved.Presentation.Paint, resolved.Presentation.IsDisplayed, resolved.Presentation.IsVisible, resolved.Presentation.Transform, resolved.FillRule, computedStyle.Stroke ?? inherited.Stroke) { Metrics = resolved.Presentation.TextMetrics, StrokeStyle = inherited.StrokeStyle.ResolveChild(computedStyle), OwnOpacity = resolved.Presentation.ElementOpacity, ElementDisplay = resolved.Presentation.ElementDisplay, PaintServerFill = resolved.Presentation.PaintServerFill, PaintOrder = computedStyle.PaintOrder ?? inherited.PaintOrder, Markers = SvgMarkerStyle.Resolve(computedStyle, inherited.Markers) };
        }
        SvgInheritedTextStyle text = SvgTextInheritance.Resolve(reader, inherited.Text, SvgInlineStyleParser.MergeFont(inlineStyle.TextFont, inlineStyle.Important, styleSheet.TextFont, styleSheet.Important), specifiedWhiteSpace, SvgInlineStyleParser.MergeWithImportance(inlineStyle.LetterSpacing, inlineStyle.Important, InlineImportantProperties.LetterSpacing, styleSheet.LetterSpacing, styleSheet.Important), SvgInlineStyleParser.MergeWithImportance(inlineStyle.WordSpacing, inlineStyle.Important, InlineImportantProperties.WordSpacing, styleSheet.WordSpacing, styleSheet.Important));
        SvgAffineTransform transform = inherited.Transform.ComposeChecked(SvgGeometrySyntax.ParseElementTransform(reader, cancellationToken));
        if (computedStyle.Fill is null && inherited.PaintServerFill is string fill) computedStyle = computedStyle with { Fill = fill };
        SvgResolvedShapePresentation textPresentation = SvgShapePresentationResolver.Resolve(SvgTextPaintStyle.ForColorResolution(computedStyle) with { Filter = null, Stroke = "none", Linecap = null, Linejoin = null, Dasharray = null, Dashoffset = null, Width = null, Miterlimit = null, StrokeOpacity = null }, inherited.Paint, inherited.IsDisplayed, inherited.IsVisible, false, SvgRootParser.CssNamedColors);
        bool isDisplayed = inherited.IsDisplayed && (computedStyle.GetDisplay() != SvgDisplay.None);
        bool isVisible = SvgPresentationResolver.ParseVisibility(computedStyle.Visibility, inherited.IsVisible);
        SvgTextPaintStyle decorationPaint = new(textPresentation.Paint, true, true, computedStyle.Stroke ?? inherited.Stroke, computedStyle.GetOpacity())
        { StrokeStyle = inherited.StrokeStyle.ResolveChild(computedStyle), PaintOrder = computedStyle.PaintOrder ?? inherited.PaintOrder };
        SvgTextDecorationStyle? decoration = SvgTextDecorationSyntax.Resolve(reader, inlineStyle, styleSheet, inherited.Text.Decoration, decorationPaint.GlyphPaint);
        if (decoration != text.Decoration) text = text.WithDecoration(decoration);
        return new TextStyle(text, textPresentation.Paint, isDisplayed, isVisible, transform, computedStyle.FillRule ?? inherited.FillRule, computedStyle.Stroke ?? inherited.Stroke) { Metrics = inherited.Metrics.Enter(computedStyle.Filter, resources), Filter = computedStyle.Filter, PaintServerFill = SvgTextPaintStyle.IsLocalFill(computedStyle.Fill) ? computedStyle.Fill : null, StrokeStyle = inherited.StrokeStyle.ResolveChild(computedStyle), OwnOpacity = computedStyle.GetOpacity(), ElementDisplay = computedStyle.GetDisplay(), PaintOrder = computedStyle.PaintOrder ?? inherited.PaintOrder, Markers = SvgMarkerStyle.Resolve(computedStyle, inherited.Markers), NonScalingStroke = SvgStrokeStyleSyntax.ParseVectorEffect(computedStyle.VectorEffect) };
    }

    private readonly record struct TextStyle(SvgInheritedTextStyle Text, SvgPaint Paint, bool IsDisplayed, bool IsVisible, SvgAffineTransform Transform, string? FillRule, string? Stroke)
    {
        internal SvgPaintOpacity? OwnOpacity { get; init; }
        internal SvgDisplay ElementDisplay { get; init; }
        internal SvgPaintOpacity ElementOpacity => OwnOpacity ?? SvgPaintOpacity.Opaque;
        internal string? PaintServerFill { get; init; }
        internal string? Filter { get; init; }
        internal SvgInheritedStrokeStyle StrokeStyle { get; init; }
        internal SvgPaintOrder PaintOrder { get; init; }
        internal bool NonScalingStroke { get; init; }
        internal SvgMarkerStyle Markers { get; init; }
        internal SvgTextMetricScope Metrics { get; init; }
        internal SvgTextPaintStyle TextPaint(SvgPaintResourceSelection? resources) => new SvgTextPaintStyle(Paint, IsDisplayed, IsVisible, Stroke, ElementOpacity) { ElementDisplay = ElementDisplay, FillServer = PaintServerFill, StrokeStyle = StrokeStyle, PaintOrder = PaintOrder, NonScalingStroke = NonScalingStroke, Decoration = Text.Decoration }.ResolveResources(Metrics.NeedsBounds ? null : resources, Metrics.CanSuppress);
    }

    private readonly record struct TextRequestKey(string Family, int Weight, bool Italic)
    {
        internal SvgSourceContext? SourceContext { get; init; }
    }

    private readonly record struct DiscoveryCandidate(bool HasRequest, string? Family, int Weight, bool Italic, SvgTextWhitespaceMode WhitespaceMode, bool HasChildElement, bool HasRenderableTextContent)
    {
        internal SvgSourceContext? SourceContext { get; init; }
        internal string? Filter { get; init; }
    }
}
