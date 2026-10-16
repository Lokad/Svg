namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Xml;

/// <summary>Discovers the explicit single-family text requests admitted by the initial preparation subset.</summary>
internal static class SvgFontRequestDiscovery
{
    private const int MaximumRequests = 256;
    private const string SvgNamespace = "http://www.w3.org/2000/svg";
    private readonly record struct TextStrokeReference(string Id);

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
        using var spanCandidates = new BudgetedParserList<DiscoveryCandidate>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        using var whitespaceLevels = new BudgetedParserList<SvgTextWhitespaceMode>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        using var eligibleLevels = new BudgetedParserList<bool>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        using var groupLevels = new BudgetedParserList<bool>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        using var viewportSizes = new BudgetedParserList<SvgViewportSize>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        using var switchScopes = new BudgetedParserList<SwitchScope>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        using var linearStrokeIds = new BudgetedParserDictionary<string, bool>(budget, SvgMemoryCategory.Parser, 96);
        using var textStrokeIds = new BudgetedParserList<TextStrokeReference>(budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        char[]? textChunk = null;
        RenderBudget.BudgetReservation? textChunkReservation = null;
        try
        {
            XmlReaderSettings settings = SvgXmlReaderPolicy.Create(svg, limits, budget, workCounter, cancellationToken);
            SvgRootStyleSheetParser.Collect(svg, settings, limits, styleRules, styleStrings, budget, workCounter, cancellationToken);
            using SvgInstanceFontDiscovery? instanceFonts = SvgInstanceFontDiscovery.Collect(svg, settings, limits, styleRules, styleStrings, budget, workCounter, cancellationToken);
            using SvgCssAncestorContext ancestors = new(styleRules, budget, workCounter, cancellationToken);
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
                workCounter.Consume(cancellationToken);
                ancestors.Observe(reader, 0);
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
                    if (isSvgElement && reader.LocalName == "linearGradient" && reader.GetAttribute("id") is {} linearId)
                        linearStrokeIds.TryAdd(styleStrings.Copy(linearId), true);
                    bool insideDirectText = activeCandidateLevel >= 0;
                    bool directChild = reader.Depth == rootDepth + 1;
                    bool hasGroupAncestor = groupDepth != 0;
                    SvgTextWhitespaceMode inheritedWhitespaceMode = whitespaceLevels.Count == 0 ? SvgTextWhitespaceMode.Default : whitespaceLevels[whitespaceLevels.Count - 1];
                    SvgTextWhitespaceMode childWhitespaceMode = inheritedWhitespaceMode;
                    InlineStyle tspanStyleSheet = insideDirectText && isSvgElement && reader.LocalName == "tspan" ? SvgStyleSheet.Resolve(reader, styleRules, ancestors, workCounter, cancellationToken) : default;
                    bool admittedTextSpan = false;
                    SvgTextSpanStyle? parsedTextSpan = null;
                    if (insideDirectText && isSvgElement && reader.LocalName == "tspan" && SvgTextSyntax.TryParseTspanStyle(reader, new SvgTextSpanStyle(inheritedWhitespaceMode, 0) { Paint = inheritedLevels[^1].TextPaint, Font = inheritedLevels[^1].Text }, tspanStyleSheet, workCounter, cancellationToken, out SvgTextSpanStyle spanStyle))
                    {
                        childWhitespaceMode = spanStyle.WhitespaceMode;
                        admittedTextSpan = textPathDepth < 0 || spanStyle.PositionOffset is not SvgTextPositionOffset offset || offset.X is null && offset.Y is null;
                        if (admittedTextSpan) parsedTextSpan = spanStyle;
                    }
                    if (insideDirectText && isSvgElement && reader.LocalName == "textPath" && reader.Depth == rootDepth + activeCandidateLevel + 1)
                    {
                        try
                        {
                            _ = SvgTextPathSyntax.Parse(reader, workCounter, cancellationToken);
                            childWhitespaceMode = SvgTextSyntax.ParseWhitespaceMode(reader, null, inheritedWhitespaceMode);
                            admittedTextSpan = true;
                            if (!reader.IsEmptyElement) textPathDepth = reader.Depth;
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
                        SvgRootAttributes root = SvgRootAttributes.Parse(reader, SvgRootParser.CssNamedColors, styleRules, workCounter, cancellationToken);
                        rootWidth = root.UserViewportWidth;
                        rootHeight = root.UserViewportHeight;
                        style = new TextStyle(root.InheritedTextStyle, root.InheritedPresentation.Paint, root.InheritedPresentation.IsDisplayed, root.InheritedPresentation.IsVisible, root.InheritedPresentation.Transform, null, root.InheritedStroke.Stroke) { OwnOpacity = root.InheritedPresentation.ElementOpacity, PaintServerFill = root.InheritedPresentation.PaintServerFill, StrokeStyle = root.InheritedStroke, PaintOrder = root.InheritedPresentation.PaintOrder };
                    }
                    else style = inheritedLevels[inheritedLevels.Count - 1];
                    bool collectedStyleElement = isSvgElement && reader.LocalName == "style"
                        && (directChild || definitionsDepth >= 0 && reader.Depth == definitionsDepth + 1
                            || !sterile && !insideDirectText && inertDepth < 0 && (hasGroupAncestor || viewportSizes.Count > 0));
                    if (collectedStyleElement)
                    {
                        SvgRootStyleSheetParser.Consume(reader, budget, workCounter, cancellationToken);
                        continue;
                    }
                    if (!sterile && isSvgElement && SvgStructuralVocabulary.IsDefinitionsContainer(reader.LocalName) && (directChild || hasGroupAncestor || viewportSizes.Count > 0) && definitionsDepth < 0 && !reader.IsEmptyElement) definitionsDepth = reader.Depth;
                    bool symbolDefinition = !sterile && isSvgElement && reader.LocalName is ("symbol" or "marker")
                        && SvgStructuralVocabulary.IsSymbolDefinitionLocation(directChild, parentAllowsText && hasGroupAncestor, definitionsDepth, symbolDepth, reader.Depth);
                    if (symbolDefinition)
                    {
                        if (reader.LocalName == "marker") _ = SvgMarkerSyntax.Parse(reader, SvgComputedStyleResolver.Resolve(reader, SvgStyleSheet.Resolve(reader, styleRules, ancestors, workCounter, cancellationToken), workCounter, cancellationToken), new(viewportSizes.Count == 0 ? rootWidth : viewportSizes[^1].Width, viewportSizes.Count == 0 ? rootHeight : viewportSizes[^1].Height), workCounter, cancellationToken);
                        else SvgElementAttributePolicy.ValidateSymbol(reader, workCounter, cancellationToken);
                        if (!reader.IsEmptyElement) symbolDepth = reader.Depth;
                    }
                    if (!sterile && isSvgElement && SvgStructuralVocabulary.IsStyleScriptOrAnimationElement(reader.LocalName)) hasUnsupportedContent = true;
                    bool patternDefinition = !sterile && isSvgElement && reader.LocalName == "pattern" && patternDepth < 0
                        && (definitionsDepth >= 0 ? reader.Depth == definitionsDepth + 1 : directChild || parentAllowsText && hasGroupAncestor);
                    if (patternDefinition)
                    {
                        SvgElementAttributePolicy.ValidatePattern(reader, workCounter, cancellationToken);
                        parentAllowsText = true;
                        double? patternWidth = reader.GetAttribute("width") is {} width ? SvgGeometrySyntax.ParseRectangleCoordinate(width) : null;
                        double? patternHeight = reader.GetAttribute("height") is {} height ? SvgGeometrySyntax.ParseRectangleCoordinate(height) : null;
                        bool noTemplate = reader.GetAttribute("href") is null && reader.GetAttribute("href", "http://www.w3.org/1999/xlink") is null;
                        if (patternWidth == 0 || patternHeight == 0 || noTemplate && (patternWidth is null || patternHeight is null))
                            style = style with { IsDisplayed = false };
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
                    if (!sterile && inertDepth < 0 && !insideDirectText && parentAllowsText && isSvgElement && !rootElement && (directChild || hasGroupAncestor || viewportSizes.Count > 0) && reader.LocalName == "svg")
                    {
                        SvgElementAttributePolicy.ValidateNestedViewport(reader, workCounter, cancellationToken);
                        double? baseWidth = viewportSizes.Count == 0 ? rootWidth : viewportSizes[^1].Width;
                        double? baseHeight = viewportSizes.Count == 0 ? rootHeight : viewportSizes[^1].Height;
                        SvgNestedViewportSyntax viewport = SvgNestedViewportSyntax.Parse(reader, baseWidth, baseHeight);
                        SvgViewBox? viewBox = SvgGeometrySyntax.ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio"), workCounter, cancellationToken);
                        plainViewport = true;
                        if (!reader.IsEmptyElement) viewportSizes.Add(new SvgViewportSize(viewBox?.Width ?? viewport.Width, viewBox?.Height ?? viewport.Height, reader.Depth));
                    }
                    if (!sterile && !insideDirectText && isSvgElement && reader.LocalName == "switch" && (parentAllowsText || viewportSizes.Count > 0) && !reader.IsEmptyElement) switchScopes.Add(new SwitchScope(reader.Depth, false));
                    bool isKnownContent = isSvgElement && (SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName) || reader.LocalName is ("svg" or "g" or "a" or "switch" or "text" or "use" or "style" or "defs" or "title" or "desc" or "metadata" or "symbol" or "marker" or "clipPath" or "mask" or "image" or "pattern" or "linearGradient" or "radialGradient" or "stop"));
                    bool isInertForeignElement = !isSvgElement && reader.IsEmptyElement;
                    bool unsupportedContainer = (!rootElement && (reader.LocalName == "style" || (reader.LocalName == "symbol" && !symbolDefinition))) || (!rootElement && reader.LocalName == "svg" && !plainViewport) || (!directChild && !hasGroupAncestor && viewportSizes.Count == 0 && reader.LocalName == "defs");
                    bool nonemptyUnsupportedLeaf = !reader.IsEmptyElement && reader.LocalName is ("use" or "image");
                    bool rendererRejectsKnownContent = isSvgElement && (unsupportedContainer || nonemptyUnsupportedLeaf);
                    bool consumeGeometryBody = !sterile && !insideDirectText && isSvgElement && SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName) && !reader.IsEmptyElement && (parentAllowsText || definitionsDepth >= 0 && reader.Depth == definitionsDepth + 1);
                    // Retain requests admitted before this point: lowering still paints them before conversion rejects the unsupported sibling, so clearing here would omit a font lowering requires.
                    if (!sterile && !hasUnsupportedContent && inertDepth < 0 && !insideDirectText && parentAllowsText && ((!isKnownContent && !isInertForeignElement) || rendererRejectsKnownContent))
                    {
                        hasUnsupportedContent = true;
                    }
                    if (!sterile && inertDepth < 0 && !insideDirectText && parentAllowsText && isSvgElement)
                    {
                        if (!rootElement && reader.LocalName == "switch") SvgElementAttributePolicy.ValidateSwitch(reader, workCounter, cancellationToken);
                        else if (!rootElement && SvgStructuralVocabulary.IsGroupOrLinkContainer(reader.LocalName)) SvgElementAttributePolicy.ValidateGroup(reader, workCounter, cancellationToken);
                        else if ((directChild || hasGroupAncestor || viewportSizes.Count > 0) && SvgStructuralVocabulary.IsDefinitionsContainer(reader.LocalName)) SvgElementAttributePolicy.ValidateDefinitionContainer(reader, workCounter, cancellationToken);
                    }
                    if (!sterile && inertDepth >= 0 && reader.Depth == inertDepth + 1 && isSvgElement)
                    {
                        if (reader.LocalName == "rect") SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken);
                        else if (reader.LocalName == "line") SvgElementAttributePolicy.ValidateLine(reader, workCounter, cancellationToken);
                        else if (reader.LocalName == "symbol") SvgElementAttributePolicy.ValidateSymbol(reader, workCounter, cancellationToken);
                        else if (reader.LocalName == "marker") _ = SvgMarkerSyntax.Parse(reader, SvgComputedStyleResolver.Resolve(reader, SvgStyleSheet.Resolve(reader, styleRules, ancestors, workCounter, cancellationToken), workCounter, cancellationToken), new(viewportSizes.Count == 0 ? rootWidth : viewportSizes[^1].Width, viewportSizes.Count == 0 ? rootHeight : viewportSizes[^1].Height), workCounter, cancellationToken);
                    }
                    InlineStyle styleSheet = isSvgElement && reader.LocalName is ("svg" or "defs" or "g" or "a" or "switch" or "pattern" or "text" or "use") ? SvgStyleSheet.Resolve(reader, styleRules, ancestors, workCounter, cancellationToken) : default;
                    SvgTextWhitespaceMode inheritedTextWhitespaceMode = style.Text.WhitespaceMode;
                    string? specifiedWhiteSpace = null;
                    if (!sterile && isSvgElement && !rootElement && !insideDirectText && parentAllowsText && reader.LocalName is ("svg" or "defs" or "g" or "a" or "switch" or "pattern" or "text" or "use")) style = ApplyAttributes(reader, style, styleSheet, workCounter, cancellationToken, out specifiedWhiteSpace);
                    if (!sterile && parsedTextSpan is SvgTextSpanStyle parsedSpan)
                    {
                        SvgTextPaintStyle paint = parsedSpan.Paint;
                        style = style with { Text = parsedSpan.Font, Paint = paint.Paint, IsDisplayed = paint.IsDisplayed, IsVisible = paint.IsVisible, Stroke = paint.Stroke,
                            StrokeStyle = paint.StrokeStyle, PaintOrder = paint.PaintOrder, NonScalingStroke = paint.NonScalingStroke };
                    }
                    if (!sterile && !hasUnsupportedContent && inertDepth < 0 && !insideDirectText && parentAllowsText && isSvgElement && reader.LocalName == "use")
                        instanceFonts?.AddUse(reader, new(new(style.Paint, style.IsDisplayed, style.IsVisible, Lokad.Svg.Rendering.SvgAffineTransform.Identity)
                            { OwnOpacity = style.ElementOpacity, PaintServerFill = style.PaintServerFill, PaintOrder = style.PaintOrder },
                            style.Text, style.FillRule, style.ElementOpacity.Alpha, style.StrokeStyle with { Stroke = style.Stroke }));
                    if (!sterile && inertDepth < 0 && isSvgElement && (SvgStructuralVocabulary.IsInertContainer(reader.LocalName) || symbolDefinition) && !reader.IsEmptyElement) inertDepth = reader.Depth;
                    SvgTextWhitespaceMode whitespaceMode = SvgTextWhitespaceMode.Default;
                    if (inertDepth < 0 && !insideDirectText && isTextEligible)
                    {
                        SvgElementAttributePolicy.ValidateText(reader, workCounter, cancellationToken);
                        whitespaceMode = SvgTextSyntax.ParseWhitespaceMode(reader, specifiedWhiteSpace, inheritedTextWhitespaceMode);
                    }
                    // SVG text ignores valid fill-rule values; discovery still validates the admitted syntax.
                    if (inertDepth < 0 && !insideDirectText && isTextEligible) SvgShapePresentationResolver.ValidateFillRule(style.FillRule);
                    DiscoveryCandidate candidateState;
                    if (!hasUnsupportedContent && inertDepth < 0 && !insideDirectText && isTextEligible && style.Text.Family is string family)
                    {
                        candidateState = new DiscoveryCandidate(true, family, style.Text.Weight, style.Text.Italic, whitespaceMode, false, false);
                    }
                    else
                    {
                        candidateState = new DiscoveryCandidate(false, null, 400, false, SvgTextWhitespaceMode.Default, false, false);
                    }
                    if (candidateState.HasRequest) childWhitespaceMode = candidateState.WhitespaceMode;
                    if (consumeGeometryBody)
                    {
                        SvgLeafSubtree subtree = SvgStructuralAdmission.ConsumeLeafSubtree(reader, limits, nodeCount, workCounter, cancellationToken);
                        nodeCount = subtree.NodeCount;
                        hasUnsupportedContent |= subtree.HasElements;
                    }
                    else if (reader.IsEmptyElement)
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
                        if (candidateState.HasRequest) { activeCandidateLevel = directTextLevels.Count - 1; activeTextPainted = false; hasPriorText = false; pendingWhitespace = null; }
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.Depth == textPathDepth) { textPathDepth = -1; hasPriorText = false; pendingWhitespace = null; }
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
                            if (activeTextPainted) AddCandidate(candidateState, requestKeys, workCounter, cancellationToken);
                            if (activeTextPainted && !candidateState.HasChildElement)
                                foreach (DiscoveryCandidate span in spanCandidates.AsSpan()) AddCandidate(span, requestKeys, workCounter, cancellationToken);
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
                        bool hasRenderable = HasRenderableText(reader, GetTextChunk(), activeWhitespace, workCounter, cancellationToken, out bool hasCharacters, out bool endsWithCollapsible);
                        SvgTextPaintStyle paint = inheritedLevels[^1].TextPaint;
                        SvgInheritedTextStyle currentFont = inheritedLevels[^1].Text;
                        if (hasRenderable && paint.IsDisplayed && pendingWhitespace is {} separator)
                        {
                            ObserveFont(separator);
                            pendingWhitespace = null;
                        }
                        if (hasRenderable)
                        {
                            paint.ValidateEmission();
                            if (paint.GlyphPaint.HasStroke && paint.Stroke is {} stroke && SvgLocalUrl.TryParseId(stroke, out string? strokeId))
                                textStrokeIds.Add(new(styleStrings.Copy(strokeId)));
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
                request => AddCandidate(new(true, request.Family, request.Weight, request.Italic, SvgTextWhitespaceMode.Default, false, true), requestKeys, workCounter, cancellationToken),
                id => textStrokeIds.Add(new(styleStrings.Copy(id))));
            foreach (TextStrokeReference reference in textStrokeIds.AsSpan())
            {
                workCounter.Consume(cancellationToken);
                if (!linearStrokeIds.TryGetValue(reference.Id, out _))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "A text stroke requires a defined local linear gradient.");
            }
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

    private static bool HasRenderableText(XmlReader reader, char[] chunk, SvgTextWhitespaceMode whitespaceMode, ParserWorkCounter workCounter, CancellationToken cancellationToken, out bool hasCharacters, out bool endsWithCollapsible)
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
                    ? SvgTextWhitespaceNormalizer.HasText(chunk.AsSpan(0, read), workCounter, cancellationToken)
                    : SvgTextWhitespaceNormalizer.HasNonWhitespaceText(chunk.AsSpan(0, read), workCounter, cancellationToken);
            }
        } while (read != 0);
        return hasRenderableText;
    }

    private static TextStyle ApplyAttributes(XmlReader reader, TextStyle inherited, InlineStyle styleSheet, ParserWorkCounter workCounter, CancellationToken cancellationToken, out string? specifiedWhiteSpace)
    {
        InlineStyle inlineStyle = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
        SvgComputedStyle computedStyle = SvgComputedStyle.Resolve(reader, inlineStyle, styleSheet).ResolveOpacity(inherited.ElementOpacity);
        specifiedWhiteSpace = SvgInlineStyleParser.MergeWithImportance(inlineStyle.WhiteSpace, inlineStyle.Important, InlineImportantProperties.WhiteSpace, styleSheet.WhiteSpace, styleSheet.Important);
        if (reader.LocalName is "svg" or "defs" or "use" || SvgStructuralVocabulary.IsGroupOrLinkContainer(reader.LocalName))
        {
            SvgInheritedContainer resolved = SvgContainerInheritance.Resolve(reader, inlineStyle, computedStyle, styleSheet, new SvgInheritedPresentation(inherited.Paint, inherited.IsDisplayed, inherited.IsVisible, inherited.Transform) { OwnOpacity = inherited.ElementOpacity, PaintServerFill = inherited.PaintServerFill }, inherited.FillRule, inherited.Text, SvgInheritedStrokeStyle.Default, SvgRootParser.CssNamedColors, workCounter, cancellationToken);
            return new TextStyle(resolved.TextStyle, resolved.Presentation.Paint, resolved.Presentation.IsDisplayed, resolved.Presentation.IsVisible, resolved.Presentation.Transform, resolved.FillRule, computedStyle.Stroke ?? inherited.Stroke) { StrokeStyle = inherited.StrokeStyle.ResolveChild(computedStyle), OwnOpacity = resolved.Presentation.ElementOpacity, PaintServerFill = resolved.Presentation.PaintServerFill, PaintOrder = computedStyle.PaintOrder ?? inherited.PaintOrder };
        }
        SvgInheritedTextStyle text = SvgTextInheritance.Resolve(reader, inherited.Text, SvgInlineStyleParser.MergeFont(inlineStyle.TextFont, inlineStyle.Important, styleSheet.TextFont, styleSheet.Important), specifiedWhiteSpace, SvgInlineStyleParser.MergeWithImportance(inlineStyle.LetterSpacing, inlineStyle.Important, InlineImportantProperties.LetterSpacing, styleSheet.LetterSpacing, styleSheet.Important), SvgInlineStyleParser.MergeWithImportance(inlineStyle.WordSpacing, inlineStyle.Important, InlineImportantProperties.WordSpacing, styleSheet.WordSpacing, styleSheet.Important));
        SvgAffineTransform transform = inherited.Transform.ComposeChecked(SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform"), workCounter, cancellationToken));
        if (computedStyle.Fill is null && inherited.PaintServerFill is string fill) computedStyle = computedStyle with { Fill = fill };
        SvgResolvedShapePresentation textPresentation = SvgShapePresentationResolver.Resolve(computedStyle with { Stroke = "none", Linecap = null, Linejoin = null, Dasharray = null, Dashoffset = null, Width = null, Miterlimit = null, StrokeOpacity = null }, inherited.Paint, inherited.IsDisplayed, inherited.IsVisible, false, SvgRootParser.CssNamedColors);
        bool isDisplayed = inherited.IsDisplayed && SvgPresentationResolver.ParseDisplay(computedStyle.Display);
        bool isVisible = SvgPresentationResolver.ParseVisibility(computedStyle.Visibility, inherited.IsVisible);
        SvgTextPaintStyle decorationPaint = new(textPresentation.Paint, true, true, computedStyle.Stroke ?? inherited.Stroke, computedStyle.GetOpacity().Alpha)
        { StrokeStyle = inherited.StrokeStyle.ResolveChild(computedStyle), PaintOrder = computedStyle.PaintOrder ?? inherited.PaintOrder };
        SvgTextDecorationStyle? decoration = SvgTextDecorationSyntax.Resolve(reader, inlineStyle, styleSheet, inherited.Text.Decoration, decorationPaint.GlyphPaint);
        if (decoration != text.Decoration) text = text.WithDecoration(decoration);
        return new TextStyle(text, textPresentation.Paint, isDisplayed, isVisible, transform, computedStyle.FillRule ?? inherited.FillRule, computedStyle.Stroke ?? inherited.Stroke) { StrokeStyle = inherited.StrokeStyle.ResolveChild(computedStyle), OwnOpacity = computedStyle.GetOpacity(), PaintOrder = computedStyle.PaintOrder ?? inherited.PaintOrder, NonScalingStroke = SvgStrokeStyleSyntax.ParseVectorEffect(computedStyle.VectorEffect) };
    }

    private readonly record struct TextStyle(SvgInheritedTextStyle Text, SvgPaint Paint, bool IsDisplayed, bool IsVisible, SvgAffineTransform Transform, string? FillRule, string? Stroke)
    {
        internal SvgPaintOpacity? OwnOpacity { get; init; }
        internal SvgPaintOpacity ElementOpacity => OwnOpacity ?? SvgPaintOpacity.Opaque;
        internal string? PaintServerFill { get; init; }
        internal SvgInheritedStrokeStyle StrokeStyle { get; init; }
        internal SvgPaintOrder PaintOrder { get; init; }
        internal bool NonScalingStroke { get; init; }
        internal SvgTextPaintStyle TextPaint => new(Paint, IsDisplayed, IsVisible, Stroke, ElementOpacity.Alpha) { StrokeStyle = StrokeStyle, PaintOrder = PaintOrder, NonScalingStroke = NonScalingStroke, Decoration = Text.Decoration };
    }

    private readonly record struct TextRequestKey(string Family, int Weight, bool Italic);

    private readonly record struct DiscoveryCandidate(bool HasRequest, string? Family, int Weight, bool Italic, SvgTextWhitespaceMode WhitespaceMode, bool HasChildElement, bool HasRenderableTextContent);
}
