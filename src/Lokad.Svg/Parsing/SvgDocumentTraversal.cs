using System.Xml;
using Lokad.Svg;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Traverses one bounded SVG document after reader setup without materializing a DOM.</summary>
internal static class SvgDocumentTraversal
{
    internal static SvgRoot Parse(XmlReader reader, SvgRenderLimits limits, RenderBudget budget, IReadOnlyDictionary<string, SvgColor> namedColors, Lokad.Svg.Fonts.SvgFontSnapshot? fontSnapshot, ParserWorkCounter workCounter, CancellationToken cancellationToken, SvgAffineTransform outputMap, SvgViewport? explicitViewport, BudgetedParserList<CssRule> styleRules, BudgetedParserStringStore styleStrings)
    {
        ArgumentNullException.ThrowIfNull(workCounter);
        while (reader.Read())
        {
            workCounter.Consume(cancellationToken);
            if (reader.NodeType != XmlNodeType.Element) continue;
            SvgStructuralAdmission.ValidateElementStructure(reader, limits);
            SvgStructuralAdmission.ThrowIfNotSvgRoot(reader);
            SvgRootAttributes root = SvgRootAttributes.Parse(reader, namedColors, styleRules, workCounter, cancellationToken);
            return TraverseRoot(reader, limits, budget, namedColors, fontSnapshot, cancellationToken, workCounter, root, outputMap, explicitViewport, styleRules, styleStrings);
        }
        throw new SvgException(SvgErrorCode.MalformedInput, "SVG input has no root element.");


    }

    private static SvgRoot TraverseRoot(XmlReader reader, SvgRenderLimits limits, RenderBudget budget, IReadOnlyDictionary<string, SvgColor> namedColors, Lokad.Svg.Fonts.SvgFontSnapshot? fontSnapshot, CancellationToken cancellationToken, ParserWorkCounter workCounter, SvgRootAttributes root, SvgAffineTransform outputMap, SvgViewport? explicitViewport, BudgetedParserList<CssRule> styleRules, BudgetedParserStringStore styleStrings)
    {
        if (reader.IsEmptyElement)
        {
            ValidateDocumentEnd(reader, workCounter, cancellationToken);
            return new SvgRoot(root.Width, root.Height, root.ViewBox, [], [], false, workCounter.Used, null);
        }
        int rootDepth = reader.Depth;
        root = root with { ContainingViewport = explicitViewport };
        using SvgDocumentState state = new(budget, workCounter, cancellationToken, styleRules, styleStrings, false);
        state.CssAncestors.Observe(reader, 0);
        // The root element consumes one node, matching font-request discovery's uniform per-element count.
        state.NodeCount = 1;
        if (root.InheritedPresentation.PaintServerFill is string rootFill)
        {
            workCounter.ConsumeText(rootFill.Length, cancellationToken);
            root = root with { InheritedPresentation = root.InheritedPresentation with { PaintServerFill = state.Strings.Copy(rootFill) } };
        }
        state.OutputMagnification = System.Math.Max(System.Math.Abs(outputMap.A), System.Math.Abs(outputMap.D));
        state.OutputTextScale = outputMap.A;
        state.OutputMap = outputMap;
        if (root.SourceOpacity.Value != 1) state.Scene.BeginSourceOpacityScope(root.SourceOpacity, new(0));
        while (reader.Read())
        {
            workCounter.Consume(cancellationToken);
            state.CssAncestors.Observe(reader, 0);
            if (reader.NodeType == XmlNodeType.Element)
            {
                SvgStructuralAdmission.ValidateElementStructure(reader, limits);
                if (state.NodeCount >= limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                state.NodeCount++;
                bool skipped = state.SkipDepth >= 0 && reader.Depth > state.SkipDepth;
                if (!skipped && state.Switches.Count > 0 && reader.Depth == state.Switches[state.Switches.Count - 1].Depth + 1)
                {
                    SwitchScope top = state.Switches[state.Switches.Count - 1];
                    // systemLanguage selection evaluates against an empty locale set, so language-tagged
                    // switch children never match: skip them without consuming the first-child slot.
                    if (reader.GetAttribute("systemLanguage") is not null)
                    {
                        if (!reader.IsEmptyElement) state.SkipDepth = reader.Depth;
                        skipped = true;
                    }
                    else if (top.FirstChildConsumed)
                    {
                        if (!reader.IsEmptyElement) state.SkipDepth = reader.Depth;
                        skipped = true;
                    }
                    else state.Switches[state.Switches.Count - 1] = top with { FirstChildConsumed = true };
                }
                bool directChild = reader.Depth == rootDepth + 1;
                bool nestedInGroup = state.Groups.Count > 0;
                bool inViewport = state.Viewports.Count > 0;
                bool inScope = directChild || nestedInGroup || inViewport;
                bool textElement = reader.LocalName == "text";
                if (skipped)
                {
                    // Skipped switch content never renders and never invalidates the operation.
                }
                else if (state.DescriptionDepth >= 0 && reader.Depth > state.DescriptionDepth)
                {
                    // Descriptive subtrees never create drawing or reference definitions. Active content still invalidates the operation.
                    if (reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsStyleScriptOrAnimationElement(reader.LocalName)) state.HasUnsupportedContent = true;
                }
                else if (reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsDescriptiveElement(reader.LocalName))
                {
                    if (!reader.IsEmptyElement) state.DescriptionDepth = reader.Depth;
                }
                else if (SvgDefinitionParser.TryParse(reader, directChild, inScope, root, state, limits, namedColors, fontSnapshot, workCounter, cancellationToken)) { }
                else if (state.SymbolDepth >= 0 && reader.Depth > state.SymbolDepth) state.HasUnsupportedContent = true;
                else if (inScope && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "svg")
                {
                    state.RenderedGroupContent?.AddUnsupported();
                    SvgElementAttributePolicy.ValidateNestedViewport(reader, workCounter, cancellationToken);
                    SvgViewportFrame? parentFrame = state.EffectiveFrame;
                    SvgNestedViewportSyntax viewport = SvgNestedViewportSyntax.Parse(reader, parentFrame?.ViewportWidth ?? root.UserViewportWidth, parentFrame?.ViewportHeight ?? root.UserViewportHeight);
                    SvgViewBox? nestedViewBox = SvgGeometrySyntax.ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio"), workCounter, cancellationToken);
                    double localScaleX = 1;
                    double localScaleY = 1;
                    double viewOffsetX = 0;
                    double viewOffsetY = 0;
                    if (nestedViewBox is SvgViewBox viewBox)
                    {
                        (localScaleX, localScaleY, viewOffsetX, viewOffsetY) = SvgGeometrySyntax.MapViewBox(viewBox, viewport.Width, viewport.Height);
                    }
                    double composedScaleX = localScaleX;
                    double composedScaleY = localScaleY;
                    double composedOffsetX = viewport.X + viewOffsetX;
                    double composedOffsetY = viewport.Y + viewOffsetY;
                    SvgClipBox? parentClip = null;
                    if (parentFrame is not null)
                    {
                        composedScaleX = parentFrame.Value.ScaleX * localScaleX;
                        composedScaleY = parentFrame.Value.ScaleY * localScaleY;
                        composedOffsetX = parentFrame.Value.ScaleX * (viewport.X + viewOffsetX) + parentFrame.Value.OffsetX;
                        composedOffsetY = parentFrame.Value.ScaleY * (viewport.Y + viewOffsetY) + parentFrame.Value.OffsetY;
                        parentClip = parentFrame.Value.Clip;
                    }
                    double clipOriginX = viewport.X;
                    double clipOriginY = viewport.Y;
                    double clipScaleX = 1;
                    double clipScaleY = 1;
                    if (parentFrame is not null)
                    {
                        clipScaleX = parentFrame.Value.ScaleX;
                        clipScaleY = parentFrame.Value.ScaleY;
                        clipOriginX = parentFrame.Value.ScaleX * viewport.X + parentFrame.Value.OffsetX;
                        clipOriginY = parentFrame.Value.ScaleY * viewport.Y + parentFrame.Value.OffsetY;
                    }
                    SvgClipBox box = new(clipOriginX, clipOriginY, viewport.Width * clipScaleX, viewport.Height * clipScaleY);
                    if (parentClip is not null) box = box.Intersect(parentClip.Value);
                    if (!reader.IsEmptyElement && (composedScaleX != 1 || composedScaleY != 1)) state.ScaledViewportPushes++;
                    SvgPaintableElementContext viewportContext = SvgPaintableElementContext.Create(reader, directChild, root, state, namedColors, workCounter, cancellationToken);
                    InlineStyle viewportInline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
                    SvgComputedStyle viewportStyle = SvgComputedStyle.Resolve(reader, viewportInline, viewportContext.StyleSheet);
                    SvgInheritedContainer inheritedViewport = SvgContainerInheritance.Resolve(reader, viewportInline, viewportStyle, viewportContext.StyleSheet, viewportContext.InheritedPresentation,
                        viewportContext.InheritedFillRule, viewportContext.InheritedTextStyle, viewportContext.InheritedStroke, false, namedColors, workCounter, cancellationToken);
                    SvgPaintOpacity sourceViewportOpacity = inheritedViewport.Presentation.ElementOpacity;
                    SvgAffineTransform textParent = parentFrame is {} textFrame ? textFrame.MapText(viewportContext.InheritedPresentation.Transform) : viewportContext.InheritedPresentation.Transform;
                    SvgAffineTransform textPlacement = textParent.ComposeChecked(SvgAffineTransform.FromScaleOffset(localScaleX, localScaleY, viewport.X + viewOffsetX, viewport.Y + viewOffsetY));
                    SvgAffineViewportClip textClip = new(new(viewport.X, viewport.Y, viewport.Width, viewport.Height), textParent);
                    if (!reader.IsEmptyElement) state.Viewports.Add(new SvgViewportFrame(composedOffsetX, composedOffsetY, composedScaleX, composedScaleY, nestedViewBox?.Width ?? viewport.Width, nestedViewBox?.Height ?? viewport.Height, box, reader.Depth) { OwnOpacity = sourceViewportOpacity, TextContext = SvgViewportTextContext.Create(state, inheritedViewport, textPlacement, textClip) });
                    byte viewportOpacity = sourceViewportOpacity.Alpha;
                    if (viewportOpacity != 255)
                    {
                        if (root.Opacity != 255 || state.HasOpacityScope) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Nested viewport opacity requires separate compositing calibration.");
                        if (!reader.IsEmptyElement)
                        {
                            state.Scene.BeginSourceOpacityScope(sourceViewportOpacity, new(-1));
                            state.HasOpacityScope = true;
                            state.ViewportOpacityDepth = reader.Depth;
                        }
                    }
                }
                else if (state.ClipDepths.Count > 0 && textElement && reader.NamespaceURI == "http://www.w3.org/2000/svg") throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text inside clipped groups is not implemented.");
                else if (textElement && reader.NamespaceURI == "http://www.w3.org/2000/svg" && HasMaskedGroup(state, workCounter, cancellationToken)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text inside masked groups is not implemented.");
                else if (SvgRenderedLeafParser.TryParse(reader, directChild, inScope, root, state, limits, budget, namedColors, fontSnapshot, workCounter, cancellationToken))
                {
                    // Rendered leaves admit no rendered children: an explicitly closed leaf with an element-free
                    // subtree renders exactly like the self-closing form, while any child element keeps the
                    // unsupported flag (animation, unknown and nested content stay rejected). Text consumes
                    // its own subtree and keeps its path.
                    if (!textElement && !reader.IsEmptyElement)
                    {
                        SvgLeafSubtree subtree = SvgStructuralAdmission.ConsumeLeafSubtree(reader, limits, state.NodeCount, workCounter, cancellationToken);
                        state.NodeCount = subtree.NodeCount;
                        state.HasUnsupportedContent |= subtree.HasElements;
                    }
                }
                else if (inScope && reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsGroupOrLinkContainer(reader.LocalName))
                {
                    SvgParsedGroup parsedGroup = SvgGroupParser.Parse(reader, directChild, root, state, namedColors, workCounter, cancellationToken);
                    SvgRenderedGroupSourceParser.BeginGroup(reader, directChild, root, state, namedColors, workCounter, cancellationToken);
                    if (!reader.IsEmptyElement)
                    {
                        state.Groups.Add(parsedGroup.Scope);
                        string? groupId = reader.GetAttribute("id");
                        state.GroupSnapshotStarts.Add(new SvgGroupSnapshotStart(groupId is null ? null : state.Strings.Copy(groupId), reader.GetAttribute("clip-path") is not null, reader.GetAttribute("mask") is not null));
                        if (parsedGroup.Scope.Kind == SvgGroupKind.Switch) state.Switches.Add(new SwitchScope(reader.Depth, false));
                        string? groupClipId = parsedGroup.Scope.ClipPathId;
                        if (groupClipId is not null && !parsedGroup.Scope.HasFilter)
                        {
                            if (state.Clips.TryGetValue(groupClipId, out SvgClipBox clipBox))
                            {
                                SvgViewportFrame? clipParent = state.EffectiveFrame;
                                if (clipParent?.IsScaled ?? false) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths in scaled viewports are not implemented.");
                                SvgClipBox clip = SvgClipPathSyntax.MapGroupClip(clipBox, parsedGroup.Scope.InheritedPresentation.Transform, clipParent, workCounter, cancellationToken);
                                state.Viewports.Add(new SvgViewportFrame(clipParent?.OffsetX ?? 0, clipParent?.OffsetY ?? 0, clipParent?.ScaleX ?? 1, clipParent?.ScaleY ?? 1, clipParent?.ViewportWidth, clipParent?.ViewportHeight, clip, reader.Depth));
                                state.ClipDepths.Add(reader.Depth);
                            }
                            else if (state.ObjectClips.TryGetValue(groupClipId, out _))
                            {
                                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local clip-path references are implemented.");
                            }
                            else if (parsedGroup.DeferredMask is {} knownMaskId && state.Masks.TryGetValue(knownMaskId, out _))
                            {
                                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Forward clip-path definitions on masked groups are not implemented.");
                            }
                            else
                            {
                                SvgViewportFrame? groupParent = state.EffectiveFrame;
                                state.PendingGroupClips.Add(new PendingGroupClip(state.Strings.Copy(groupClipId), groupParent, parsedGroup.Scope.InheritedPresentation.Transform, state.Scene.CommandCount, -1, reader.Depth, state.ScaledViewportPushes, -1));
                                SvgClipBox passthrough = groupParent?.Clip ?? SvgClipBox.Unbounded;
                                state.Viewports.Add(new SvgViewportFrame(groupParent?.OffsetX ?? 0, groupParent?.OffsetY ?? 0, groupParent?.ScaleX ?? 1, groupParent?.ScaleY ?? 1, groupParent?.ViewportWidth, groupParent?.ViewportHeight, passthrough, reader.Depth));
                                state.ClipDepths.Add(reader.Depth);
                            }
                        }
                        if (parsedGroup.Scope.Kind == SvgGroupKind.Group) state.GroupDepth++;
                        if (parsedGroup.Scope.HasFilter) state.FilterGroupDepth++;
                        if (parsedGroup.DeferredMask is not null)
                        {
                            state.PendingGroupMasks.Add(new PendingGroupMask(parsedGroup.DeferredMask, state.Scene.CommandCount, -1, reader.Depth, state.EffectiveFrame, parsedGroup.Scope.InheritedPresentation.Transform));
                        }
                        if (parsedGroup.Scope.IsolatesOpacity || parsedGroup.Scope.HasDeferredMask || parsedGroup.Scope.HasFilter)
                        {
                            state.Scene.BeginSourceOpacityScope(parsedGroup.SourceOpacity, parsedGroup.RecordingScope);
                            if (parsedGroup.FilterId is {} filterId)
                            {
                                int binding = state.Scene.Filters.AddBinding(filterId, default, default,
                                    parsedGroup.Scope.InheritedPresentation.Transform, parsedGroup.LengthBases, true, groupClipId);
                                int position = state.Scene.CommandCount - 1;
                                state.Scene.ReplaceCommand(position, state.Scene.CommandAt(position) with
                                { RegionKind = SvgLayerRegionKind.Filter, MaskContentIndex = binding });
                            }
                            if (parsedGroup.Scope.IsolatesOpacity) state.HasOpacityScope = true;
                        }
                    }
                }
                else if (inScope && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "style")
                {
                    SvgRootStyleSheetParser.Consume(reader, budget, workCounter, cancellationToken);
                }
                else if (state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "style")
                {
                    SvgRootStyleSheetParser.Consume(reader, budget, workCounter, cancellationToken);
                }
                else if (reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsStyleScriptOrAnimationElement(reader.LocalName)) state.HasUnsupportedContent = true;
                else if (directChild && reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsInertContainer(reader.LocalName)) { }
                else if (inScope && reader.NamespaceURI != "http://www.w3.org/2000/svg" && reader.IsEmptyElement)
                {
                    // Empty foreign-namespace elements (editor metadata like sodipodi:namedview) paint nothing and carry no renderable descendants.
                }
                else if (inScope && reader.NamespaceURI != "http://www.w3.org/2000/svg" && !reader.IsEmptyElement && TrySkipInertForeignSubtree(reader, state, limits, workCounter, cancellationToken)) { }
                else if (inScope) state.HasUnsupportedContent = true;
            }
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.Depth == state.FilterDepth) SvgDefinitionParser.RegisterFilter(state);
                if (reader.Depth == state.SkipDepth) state.SkipDepth = -1;
                if (reader.Depth == state.DescriptionDepth) state.DescriptionDepth = -1;
                if (reader.Depth == state.DefinitionsDepth)
                {
                    SvgDefinitionParser.DrainPendingGradientHrefs(state, workCounter, cancellationToken);
                    SvgPatternReferenceResolver.Resolve(state, false, workCounter, cancellationToken);
                }
                if (reader.Depth == state.DefinitionsDepth) { state.DefinitionsDepth = -1; state.DefinitionInheritance = null; }
                if (reader.Depth == state.GradientDepth)
                {
                    if (state.GradientId is not null) SvgDefinitionParser.RegisterGradient(state, workCounter, cancellationToken);
                    state.GradientDepth = -1;
                    state.GradientId = null;
                    state.Gradient = null;
                    state.GradientStops.Clear();
                }
                if (reader.Depth == state.RadialGradientDepth)
                {
                    if (state.RadialGradientId is not null) SvgDefinitionParser.RegisterRadialGradient(state, workCounter, cancellationToken);
                    state.RadialGradientDepth = -1;
                    state.RadialGradientId = null;
                    state.RadialGradient = null;
                    state.GradientStops.Clear();
                }
                if (reader.Depth == state.PatternDepth)
                {
                    if (state.Groups.Count == 0 || state.Groups[^1].Kind != SvgGroupKind.Pattern || state.Groups[^1].Depth != reader.Depth)
                        throw new InvalidOperationException("Pattern presentation scope is unbalanced.");
                    state.Groups.RemoveLast();
                    if (state.PatternId is not null) SvgDefinitionParser.RegisterPattern(state, workCounter, cancellationToken);
                    state.PatternContentBuilder?.Dispose();
                    state.PatternContentBuilder = null;
                    state.PatternRectangleSource = null;
                    state.PatternRectangleContext = default;
                    state.PatternUseStart = 0;
                    state.PatternUseCount = 0;
                    state.PatternTemplate = default;
                    state.PatternDepth = -1;
                    state.PatternId = null;
                    state.PatternTile = null;
                }
                if (reader.Depth == state.MaskDepth)
                {
                    if (state.MaskId is not null)
                    {
                        if (state.MaskHasReplayContent) SvgMaskContentReplay.Register(state, workCounter, cancellationToken);
                        else if (state.MaskUse is not null) SvgResourceUseReplay.Register(state, SvgResourceUseKind.Mask, workCounter, cancellationToken);
                        else if (state.MaskContentBuilder is not null) RegisterMaskScene(state, workCounter, cancellationToken);
                        else if (state.MaskContentGeometries is not null) SvgDefinitionParser.RegisterMaskContent(state, workCounter, cancellationToken);
                        else SvgDefinitionParser.RegisterMask(state, workCounter, cancellationToken);
                    }
                    if (state.MaskContentGeometries is {} anonymousMask && state.MaskId is null)
                        foreach (SvgGeometry geometry in anonymousMask) geometry.Dispose();
                    if (state.Groups.Count == 0 || state.Groups[^1].Kind != SvgGroupKind.Mask || state.Groups[^1].Depth != reader.Depth)
                        throw new InvalidOperationException("Mask presentation scope is unbalanced.");
                    state.Groups.RemoveLast();
                    state.MaskUse = null;
                    state.MaskReplayChildCount = 0;
                    state.MaskHasReplayContent = false;
                    state.MaskDepth = -1;
                    state.MaskId = null;
                    state.MaskIsAlpha = false;
                    state.MaskValue = null;
                    state.MaskContentGeometries = null;
                    state.MaskContentBuilder?.Dispose();
                    state.MaskContentBuilder = null;
                    state.MaskCustomRegion = null;
                    state.MaskDefaultUserSpaceRegion = false;
                    state.MaskFractionRegion = null;
                }
                if (reader.Depth == state.ClipPathDepth)
                {
                    if (state.ClipPathId is not null)
                    {
                        if (state.ClipUse is not null) SvgResourceUseReplay.Register(state, SvgResourceUseKind.Clip, workCounter, cancellationToken);
                        else if (state.ClipBox is not null) SvgDefinitionParser.RegisterClipBox(state, workCounter, cancellationToken);
                        else if (state.ClipPolygon is not null) SvgDefinitionParser.RegisterClipPolygon(state, workCounter, cancellationToken);
                        else SvgDefinitionParser.RegisterClipBox(state, workCounter, cancellationToken);
                    }
                    if (state.ClipPathId is null) state.ClipPolygon?.SourcePath?.Dispose();
                    state.ClipUse = null;
                    state.ClipPathDepth = -1;
                    state.ClipPathId = null;
                    state.ClipBox = null;
                    state.ClipPolygon = null;
                    state.ClipIsObjectBoundingBox = false;
                }
                state.SymbolDefinition?.EndGroup(reader.Depth, state);
                SvgRenderedGroupSourceParser.EndGroup(reader.Depth, state, workCounter, cancellationToken);
                if (reader.Depth == state.SymbolDepth) SvgDefinitionParser.RegisterSymbol(state, workCounter, cancellationToken);
                if (state.Viewports.Count > 0 && state.Viewports[^1].Depth == reader.Depth) state.Viewports.RemoveLast();
                if (state.ViewportOpacityDepth == reader.Depth)
                {
                    state.ViewportOpacityDepth = -1;
                    state.Scene.EndOpacityScope();
                    state.HasOpacityScope = false;
                }
                if (state.ClipDepths.Count > 0 && state.ClipDepths[^1] == reader.Depth) state.ClipDepths.RemoveLast();
                if (state.Groups.Count > 0 && state.Groups[^1].Depth == reader.Depth && state.Groups[^1].Kind == SvgGroupKind.PatternGroup)
                {
                    if (state.Groups[^1].IsolatesOpacity)
                        (state.PatternContentBuilder ?? throw new InvalidOperationException("An isolated pattern group requires its replay builder.")).EndOpacityScope();
                    state.Groups.RemoveLast();
                    state.GroupDepth--;
                }
                else if (state.Groups.Count > 0 && state.Groups[^1].Depth == reader.Depth)
                {
                    SvgGroupScope group = state.Groups[^1];
                    state.Groups.RemoveLast();
                    SvgGroupSnapshotStart snapshotStart = state.GroupSnapshotStarts[^1];
                    state.GroupSnapshotStarts.RemoveLast();
                    if (snapshotStart.Id is not null && group.Kind == SvgGroupKind.Group)
                    {
                        workCounter.Consume(cancellationToken);
                        if (state.Definitions.TryGetValue(snapshotStart.Id, out _) || state.GroupSnapshots.TryGetValue(snapshotStart.Id, out _)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
                        string retainedId = state.Strings.Copy(snapshotStart.Id);
                        bool hasViewport = false;
                        for (int viewportIndex = 0; viewportIndex < state.Viewports.Count; viewportIndex++)
                        {
                            workCounter.Consume(cancellationToken);
                            SvgViewportFrame viewportFrame = state.Viewports[viewportIndex];
                            if (viewportFrame.ScaleX != 1 || viewportFrame.ScaleY != 1 || viewportFrame.OffsetX != 0 || viewportFrame.OffsetY != 0) { hasViewport = true; break; }
                        }
                        if (!state.GroupSnapshots.TryAdd(retainedId, new SvgGroupSnapshot(group.IsolatesOpacity, snapshotStart.HasClip, snapshotStart.HasMask, hasViewport))) throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
                    }
                    if (group.HasMaskScope) state.MaskScopes.RemoveLast();
                    if (group.Kind == SvgGroupKind.Group) state.GroupDepth--;
                    if (group.HasFilter) state.FilterGroupDepth--;
                    for (int pendingGroupIndex = state.PendingGroupClips.Count - 1; pendingGroupIndex >= 0; pendingGroupIndex--)
                    {
                        workCounter.Consume(cancellationToken);
                        PendingGroupClip pendingGroup = state.PendingGroupClips[pendingGroupIndex];
                        if (pendingGroup.Depth == reader.Depth && pendingGroup.EndCommandIndex < 0)
                        {
                            state.PendingGroupClips[pendingGroupIndex] = pendingGroup with { EndCommandIndex = state.Scene.CommandCount, ScaledPushesAtClose = state.ScaledViewportPushes };
                            break;
                        }
                    }
                    for (int pendingMaskIndex = state.PendingGroupMasks.Count - 1; pendingMaskIndex >= 0; pendingMaskIndex--)
                    {
                        workCounter.Consume(cancellationToken);
                        PendingGroupMask pendingMask = state.PendingGroupMasks[pendingMaskIndex];
                        if (pendingMask.Depth == reader.Depth && pendingMask.EndCommandIndex < 0)
                        {
                            state.PendingGroupMasks[pendingMaskIndex] = pendingMask with { EndCommandIndex = state.Scene.CommandCount };
                            break;
                        }
                    }
                    if (group.IsolatesOpacity || group.HasDeferredMask || group.HasFilter)
                    {
                        state.Scene.EndOpacityScope();
                        if (group.IsolatesOpacity) state.HasOpacityScope = false;
                    }
                }
                if (reader.Depth == rootDepth)
                {
                    SvgDefinitionParser.ResolvePendingGradientHrefs(state, workCounter, cancellationToken);
                    SvgPatternReferenceResolver.Resolve(state, true, workCounter, cancellationToken);
                    SvgDeferredPaintLeaves.Resolve(root, state, limits, workCounter, cancellationToken);
                    if (state.DeferredTextFailure is {} textFailure) throw textFailure;
                    SvgTextPathLayout.Resolve(state, limits, workCounter, cancellationToken);
                    state.ValidateSymbolReferences(workCounter, cancellationToken);
                    SvgPatternUseReplay.Resolve(state, workCounter, cancellationToken);
                    SvgResourceUseReplay.Resolve(state, workCounter, cancellationToken);
                    SvgMaskContentReplay.Resolve(root, state, workCounter, cancellationToken);
                    SvgReferenceResolver.ResolvePendingUses(state.Definitions, state.PendingUses, state.Scene, state, workCounter, cancellationToken);
                    SvgClipPathSyntax.ResolvePendingLeafClips(state, workCounter, cancellationToken);
                    SvgClipPathSyntax.ResolvePendingGroupClips(state, workCounter, cancellationToken);
                    SvgMaskSyntax.ResolvePendingLeafMasks(state, workCounter, cancellationToken);
                    SvgMaskSyntax.ResolvePendingGroupMasks(state, workCounter, cancellationToken);
                    if (root.SourceOpacity.Value != 1) state.Scene.EndOpacityScope();
                    SvgLeafMaskComposition.Emit(state, workCounter, cancellationToken);
                    SvgSceneStorage sceneStorage = state.Scene.Build();
                    state.ReleaseDefinitions();
                    try
                    {
                        ValidateDocumentEnd(reader, workCounter, cancellationToken);
                        return new SvgRoot(root.Width, root.Height, root.ViewBox, sceneStorage.Geometries, sceneStorage.Commands, state.HasUnsupportedContent, workCounter.Used, sceneStorage);
                    }
                    catch { sceneStorage.Dispose(); throw; }
                }
            }
        }
        throw new SvgException(SvgErrorCode.MalformedInput, "SVG root element is not closed.");

        static bool HasMaskedGroup(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            foreach (SvgGroupScope group in state.Groups.AsSpan())
            {
                workCounter.Consume(cancellationToken);
                if (group.HasDeferredMask) return true;
            }
            return false;
        }

        // Seal shader-mask replay under one parent-owned vector scene; failure releases staging owners.
        static void RegisterMaskScene(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            string id = state.Strings.Copy(state.MaskId ?? throw new InvalidOperationException("Mask scene registration requires an identifier."));
            if (state.Masks.TryGetValue(id, out _) || state.MaskContents.TryGetValue(id, out _)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
            SvgSceneBuilder builder = state.MaskContentBuilder ?? throw new InvalidOperationException("Mask scene registration requires a builder.");
            SvgLeafMaskComposition.Emit(state, builder, workCounter, cancellationToken);
            SvgVectorContent content = SvgVectorContent.Create(builder.Build(), state.Budget);
            try { state.Scene.AdoptVectorContent(content); }
            catch { content.Dispose(); throw; }
            if (!state.MaskContents.TryAdd(id, new([], 255, state.MaskIsAlpha) { VectorContent = content })) throw new InvalidOperationException("A mask scene must register once.");
            if (state.MaskCustomRegion is {} region && !state.MaskRegions.TryAdd(id, region)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
            if (state.MaskFractionRegion is {} fractions && !state.MaskFractionRegions.TryAdd(id, fractions)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        }
    }

    /// <summary>Consumes one non-empty foreign-namespace subtree when no descendant can render, reporting whether it was skipped.</summary>
    /// <remarks>
    /// <para>Only editor namedview containers are eligible: unknown foreign elements keep the unsupported flag even when fully foreign (see the public-contract foreign-rect lock), so the allowlist cannot grow by content shape. A skipped subtree holds no SVG-namespace descendants, and this engine renders only SVG-namespace elements, so skipping paints nothing and drops no definitions.</para>
    /// <para>Mirrors the main loop per-element accounting: structural limits, node budget, work and cancellation, plus the symbol-depth rule. Leaves the reader on the foreign end element so the main loop end-element bookkeeping runs normally. Well-formed XML always closes the subtree; reaching the reader end reports false.</para>
    /// </remarks>
    private static bool TrySkipInertForeignSubtree(System.Xml.XmlReader reader, SvgDocumentState state, SvgRenderLimits limits, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(workCounter);
        if (!string.Equals(reader.LocalName, "namedview", StringComparison.Ordinal)) return false;
        int foreignDepth = reader.Depth;
        bool clean = true;
        while (reader.Read())
        {
            workCounter.Consume(cancellationToken);
            if (reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                SvgStructuralAdmission.ValidateElementStructure(reader, limits);
                if (state.NodeCount >= limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                state.NodeCount++;
                if (state.SymbolDepth >= 0 && reader.Depth > state.SymbolDepth) clean = false;
                if (reader.NamespaceURI == "http://www.w3.org/2000/svg") clean = false;
            }
            else if (reader.NodeType == System.Xml.XmlNodeType.EndElement && reader.Depth == foreignDepth) return clean;
        }

        return false;
    }

    /// <summary>Consumes one non-empty rendered-leaf subtree, reporting whether it holds any child elements.</summary>
    /// <remarks>Element-free subtrees (whitespace or text only, which the leaf lowerers and the main loop both ignore) render exactly like the self-closing form. Mirrors the main-loop per-element accounting while consuming, and leaves the reader on the leaf end element so end-element bookkeeping runs normally.</remarks>
    private static void ValidateDocumentEnd(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        while (reader.Read())
        {
            workCounter.Consume(cancellationToken);
            if (reader.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace) continue;
            throw new SvgException(SvgErrorCode.MalformedInput, "SVG input has content after its root element.");
        }
    }

}
