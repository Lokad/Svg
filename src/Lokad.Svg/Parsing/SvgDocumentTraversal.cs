using System.Xml;
using Lokad.Svg;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Traverses one bounded SVG document after reader setup without materializing a DOM.</summary>
internal static class SvgDocumentTraversal
{
    internal static SvgRoot Parse(XmlReader reader, SvgRenderLimits limits, RenderBudget budget, IReadOnlyDictionary<string, SvgColor> namedColors, Lokad.Svg.Fonts.SvgFontSnapshot? fontSnapshot, ParserWorkCounter workCounter, CancellationToken cancellationToken, SvgAffineTransform outputMap, SvgViewport? explicitViewport, BudgetedParserList<CssRule> styleRules, BudgetedParserStringStore styleStrings, double rasterScale, SvgPaintResourceSelection? paintResources)
    {
        ArgumentNullException.ThrowIfNull(workCounter);
        while (reader.Read())
        {
            workCounter.Consume(cancellationToken);
            if (reader.NodeType != XmlNodeType.Element) continue;
            SvgStructuralAdmission.ValidateElementStructure(reader, limits);
            SvgStructuralAdmission.ThrowIfNotSvgRoot(reader);
            SvgRootAttributes root = SvgRootAttributes.Parse(reader, namedColors, styleRules, workCounter, cancellationToken);
            return TraverseRoot(reader, limits, budget, namedColors, fontSnapshot, cancellationToken, workCounter, root, outputMap, explicitViewport, styleRules, styleStrings, rasterScale, paintResources);
        }
        throw new SvgException(SvgErrorCode.MalformedInput, "SVG input has no root element.");


    }

    private static SvgRoot TraverseRoot(XmlReader reader, SvgRenderLimits limits, RenderBudget budget, IReadOnlyDictionary<string, SvgColor> namedColors, Lokad.Svg.Fonts.SvgFontSnapshot? fontSnapshot, CancellationToken cancellationToken, ParserWorkCounter workCounter, SvgRootAttributes root, SvgAffineTransform outputMap, SvgViewport? explicitViewport, BudgetedParserList<CssRule> styleRules, BudgetedParserStringStore styleStrings, double rasterScale, SvgPaintResourceSelection? paintResources)
    {
        if (reader.IsEmptyElement)
        {
            ValidateDocumentEnd(reader, workCounter, cancellationToken);
            return new SvgRoot(root.Width, root.Height, root.ViewBox, [], [], false, workCounter.Used, null);
        }
        int rootDepth = reader.Depth;
        root = root with { ContainingViewport = explicitViewport };
        using SvgDocumentState state = new(budget, workCounter, cancellationToken, styleRules, styleStrings, false);
        state.PaintResources = paintResources;
        state.CssAncestors.Observe(reader, 0);
        // The root element consumes one node, matching font-request discovery's uniform per-element count.
        state.NodeCount = 1;
        if (root.InheritedPresentation.PaintServerFill is string rootFill)
        {
            workCounter.ConsumeText(rootFill.Length, cancellationToken);
            root = root with { InheritedPresentation = root.InheritedPresentation with { PaintServerFill = state.Strings.Copy(rootFill) } };
        }
        state.OutputMagnification = System.Math.Max(System.Math.Abs(outputMap.A), System.Math.Abs(outputMap.D));
        state.OutputTextScale = outputMap.A / rasterScale;
        state.OutputMap = outputMap;
        SvgViewport logicalViewport = SvgDocumentSizing.Resolve(explicitViewport, root.Width, root.Height, root.ViewBox).LogicalViewport;
        state.OutputClip = new(0, 0, logicalViewport.Width * rasterScale, logicalViewport.Height * rasterScale);
        state.TextReplay = new(root, limits, fontSnapshot);
        if (root.SourceOpacity.Value != 1) state.Scene.BeginSourceOpacityScope(root.SourceOpacity, new(0));
        while (reader.Read())
        {
            SvgSourceContext? sourceContext = reader.NodeType == XmlNodeType.Element ? SvgSourceContext.Capture(reader) : null;
            workCounter.Consume(cancellationToken);
            state.CssAncestors.Observe(reader, 0);
            if (reader.NodeType == XmlNodeType.Element)
            {
                SvgStructuralAdmission.ValidateElementStructure(reader, limits);
                if (state.NodeCount >= limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                state.NodeCount++;
                if (state.PatternDepth >= 0 && reader.Depth == state.PatternDepth + 1)
                    state.PatternTemplate = state.PatternTemplate with { HasChildElements = true };
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
                    SvgPaintableElementContext viewportContext = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
                    InlineStyle viewportInline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
                    SvgComputedStyle viewportStyle = SvgComputedStyle.Resolve(reader, viewportInline, viewportContext.StyleSheet);
                    SvgInheritedContainer inheritedViewport = SvgContainerInheritance.Resolve(reader, viewportInline, viewportStyle, viewportContext.StyleSheet, viewportContext.InheritedPresentation,
                        viewportContext.InheritedFillRule, viewportContext.InheritedTextStyle, viewportContext.InheritedStroke, namedColors, state.PaintResources, workCounter, cancellationToken);
                    if (viewport.Width == 0 || viewport.Height == 0)
                        inheritedViewport = inheritedViewport with { Presentation = inheritedViewport.Presentation with { IsDisplayed = false } };
                    SvgPaintOpacity sourceViewportOpacity = inheritedViewport.Presentation.ElementOpacity;
                    // The viewport's own transform precedes its x/y and viewBox,
                    // together with ancestor placement and the physical clip.
                    SvgAffineTransform viewportTransform = inheritedViewport.Presentation.Transform;
                    SvgAffineTransform textParent = parentFrame is {} textFrame ? textFrame.MapText(viewportTransform) : viewportTransform;
                    SvgAffineTransform textPlacement = textParent.ComposeChecked(SvgAffineTransform.FromScaleOffset(localScaleX, localScaleY, viewport.X + viewOffsetX, viewport.Y + viewOffsetY));
                    SvgAffineViewportClip textClip = new(new(viewport.X, viewport.Y, viewport.Width, viewport.Height), textParent);
                    if (viewportTransform != SvgAffineTransform.Identity)
                    {
                        // Geometry applies the scalar viewport after its inherited
                        // source map. Rebase that ancestry so outer group placement
                        // stays outside the child viewBox scale, as it does for text.
                        SvgAffineTransform geometryViewport = SvgAffineTransform.FromScaleOffset(composedScaleX, composedScaleY, composedOffsetX, composedOffsetY);
                        if (geometryViewport.TryInvert(out SvgAffineTransform inverseViewport))
                            inheritedViewport = inheritedViewport with { Presentation = inheritedViewport.Presentation with { Transform = inverseViewport.ComposeChecked(textPlacement) } };
                        box = textParent.IsAxisAligned
                            ? textParent.Apply(new SvgClipBox(viewport.X, viewport.Y, viewport.Width, viewport.Height))
                            : SvgClipBox.Unbounded;
                        if (parentClip is {} enclosingClip) box = box.Intersect(enclosingClip);
                    }
                    bool affineClipScope = !reader.IsEmptyElement && !textParent.IsAxisAligned;
                    if (!reader.IsEmptyElement) state.Viewports.Add(new SvgViewportFrame(composedOffsetX, composedOffsetY, composedScaleX, composedScaleY, nestedViewBox?.Width ?? viewport.Width, nestedViewBox?.Height ?? viewport.Height, box, reader.Depth) { RecordingScope = state.NewRecordingScope(), OwnOpacity = sourceViewportOpacity, HasAffineClipScope = affineClipScope, TextContext = SvgViewportTextContext.Create(state, inheritedViewport, textPlacement, textClip) });
                    if (affineClipScope)
                    {
                        (SvgClipBox bounds, int contentIndex) = textClip.Register(state, state.Scene, workCounter, cancellationToken);
                        state.Scene.InsertCommands(state.Scene.CommandCount,
                            [new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, 255, bounds, contentIndex, 255) { RegionKind = SvgLayerRegionKind.AffineViewport }]);
                    }
                    byte viewportOpacity = sourceViewportOpacity.Alpha;
                    if (viewportOpacity != 255)
                    {
                        if (!reader.IsEmptyElement)
                        {
                            state.Scene.BeginSourceOpacityScope(sourceViewportOpacity, new(-1));
                        }
                    }
                }
                else if (SvgRenderedLeafParser.TryParse(reader, inScope, root, state, limits, budget, namedColors, fontSnapshot, workCounter, cancellationToken))
                {
                    // Balanced leaves share the self-closing behavior; inert descriptions remain allowed.
                    // Drawing, animation and unknown children stay unsupported. Text owns its own subtree.
                    if (!textElement && !reader.IsEmptyElement)
                    {
                        SvgLeafSubtree subtree = SvgStructuralAdmission.ConsumeLeafSubtree(reader, limits, state.NodeCount, workCounter, cancellationToken);
                        state.NodeCount = subtree.NodeCount;
                        state.HasUnsupportedContent |= subtree.HasUnsupportedChildren;
                    }
                }
                else if (inScope && reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsGroupOrLinkContainer(reader.LocalName))
                {
                    SvgParsedGroup parsedGroup = SvgGroupParser.Parse(reader, root, state, namedColors, workCounter, cancellationToken);
                    SvgRenderedGroupSourceParser.BeginGroup(reader, root, state, limits, namedColors, workCounter, cancellationToken);
                    if (!reader.IsEmptyElement || parsedGroup.Scope.HasFilter)
                    {
                        bool isolates = parsedGroup.Scope.IsolatesOpacity || parsedGroup.Scope.HasDeferredMask || parsedGroup.Scope.HasFilter;
                        SvgViewportFrame? isolationParent = isolates ? state.EffectiveFrame : null;
                        bool isolatedViewport = isolationParent is { HasAffineClipScope: false } viewportParent && viewportParent.Clip != SvgClipBox.Unbounded;
                        state.Groups.Add(parsedGroup.Scope with { HasIsolatedViewportScope = isolatedViewport, PreviousIsolatedViewportDepth = state.IsolatedViewportDepth, FilterGeometryStart = state.Scene.GeometryCount });
                        // The inherited clip surrounds the isolated surface. Clear it
                        // before binding an own clip so that children keep only their
                        // local clipping, without losing the scalar viewport map.
                        if (isolates) state.IsolatedViewportDepth = reader.Depth;
                        string? groupId = reader.GetAttribute("id");
                        SvgGroupSnapshotStart snapshot = new(groupId is null ? null : state.Strings.Copy(groupId), SvgLocalUrl.ReadReference(reader, "clip-path") is not null, SvgLocalUrl.ReadReference(reader, "mask") is not null);
                        if (parsedGroup.Scope.Kind == SvgGroupKind.Switch) state.Switches.Add(new SwitchScope(reader.Depth, false));
                        string? groupClipId = parsedGroup.Scope.ClipPathId;
                        if (groupClipId is not null && !parsedGroup.Scope.HasFilter)
                        {
                            // Opacity and mask owners retain their clip outside the
                            // isolated surface. Zero-opacity owners still paint nothing.
                            bool clipsChildren = parsedGroup.SourceOpacity.Value == 0
                                || parsedGroup.SourceOpacity.Value == 1 && !parsedGroup.Scope.HasDeferredMask;
                            if (clipsChildren && state.Clips.TryGetValue(groupClipId, out SvgClipBox clipBox) && parsedGroup.Scope.InheritedPresentation.Transform.IsAxisAligned)
                            {
                                SvgViewportFrame? clipParent = state.EffectiveFrame;
                                SvgClipBox clip = SvgClipPathSyntax.MapGroupClip(clipBox, parsedGroup.Scope.InheritedPresentation.Transform, clipParent, workCounter, cancellationToken);
                                state.Viewports.Add(new SvgViewportFrame(clipParent?.OffsetX ?? 0, clipParent?.OffsetY ?? 0, clipParent?.ScaleX ?? 1, clipParent?.ScaleY ?? 1, clipParent?.ViewportWidth, clipParent?.ViewportHeight, clip, reader.Depth));
                                state.ClipDepths.Add(reader.Depth);
                            }
                            else
                            {
                                SvgViewportFrame? groupParent = state.EffectiveFrame;
                                snapshot = snapshot with { PendingClip = state.PendingGroupClips.Count };
                                state.PendingGroupClips.Add(new PendingGroupClip(state.Strings.Copy(groupClipId), groupParent, parsedGroup.Scope.InheritedPresentation.Transform, state.Scene.CommandCount, -1, reader.Depth, state.Scene));
                                SvgClipBox passthrough = groupParent?.Clip ?? SvgClipBox.Unbounded;
                                state.Viewports.Add(new SvgViewportFrame(groupParent?.OffsetX ?? 0, groupParent?.OffsetY ?? 0, groupParent?.ScaleX ?? 1, groupParent?.ScaleY ?? 1, groupParent?.ViewportWidth, groupParent?.ViewportHeight, passthrough, reader.Depth));
                                state.ClipDepths.Add(reader.Depth);
                            }
                        }
                        if (parsedGroup.Scope.Kind == SvgGroupKind.Group) state.GroupDepth++;
                        if (parsedGroup.Scope.HasFilter) state.FilterGroupDepth++;
                        if (isolatedViewport && isolationParent is {} clippedParent)
                        {
                            SvgDrawCommand clip = parsedGroup.Scope.HasDeferredMask && clippedParent.Clip.Antialiasing == SvgClipAntialiasing.All
                                ? SvgClipPathSyntax.CreateRectangleScope(clippedParent.Clip, SvgAffineTransform.Identity, state, state.Scene, workCounter, cancellationToken)
                                : new(SvgDrawCommandKind.BeginGroup, -1, 255, clippedParent.Clip, -1, 255) { RegionKind = parsedGroup.Scope.HasFilter && clippedParent.RecordingScope.Value == 0 ? SvgLayerRegionKind.FilterOutputClip : SvgLayerRegionKind.Viewport };
                            state.Scene.InsertCommands(state.Scene.CommandCount, [clip]);
                            if (snapshot.PendingClip is {} clipIndex)
                            {
                                PendingGroupClip pending = state.PendingGroupClips[clipIndex];
                                state.PendingGroupClips[clipIndex] = pending with { FirstCommandIndex = pending.FirstCommandIndex + 1 };
                            }
                        }
                        if (parsedGroup.DeferredMask is not null)
                        {
                            snapshot = snapshot with { PendingMask = state.PendingGroupMasks.Count };
                            state.PendingGroupMasks.Add(new PendingGroupMask(parsedGroup.DeferredMask, state.Scene.CommandCount, -1, reader.Depth, state.EffectiveFrame, parsedGroup.Scope.InheritedPresentation.Transform, state.Scene, parsedGroup.Scope.HasFilter));
                        }
                        if (parsedGroup.Scope.IsolatesOpacity || parsedGroup.Scope.HasDeferredMask || parsedGroup.Scope.HasFilter)
                        {
                            state.Scene.BeginSourceOpacityScope(parsedGroup.SourceOpacity, parsedGroup.RecordingScope);
                            if (parsedGroup.FilterId is {} filterId)
                            {
                                if (parsedGroup.Scope.HasDeferredMask)
                                    state.Scene.BeginSourceOpacityScope(SvgPaintOpacity.Opaque, parsedGroup.Scope.RecordingScope);
                                SvgAffineTransform filterMap = SvgViewportFrame.MapGeometry(parsedGroup.Scope.InheritedPresentation.Transform, isolationParent);
                                int binding = state.Scene.Filters.AddBinding(filterId, default, default,
                                    filterMap, parsedGroup.LengthBases, true, groupClipId);
                                int position = state.Scene.CommandCount - 1;
                                state.Scene.ReplaceCommand(position, state.Scene.CommandAt(position) with
                                { RegionKind = SvgLayerRegionKind.Filter, MaskContentIndex = binding,
                                    GeometryIndex = parsedGroup.Scope.HasDeferredMask && groupClipId is not null
                                        ? parsedGroup.RecordingScope.Value : state.Scene.CommandAt(position).GeometryIndex });
                            }
                        }
                        state.GroupSnapshotStarts.Add(snapshot);
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
                if (state.HasUnsupportedContent && state.UnsupportedDiagnostic is null && sourceContext is {} unsupportedSource)
                    state.UnsupportedDiagnostic = unsupportedSource.Describe(string.Empty, string.Empty);
            }
            // An empty filtered group has the same effect lifetime as its explicitly closed form.
            bool closeEmptyFilter = reader.NodeType == XmlNodeType.Element && reader.IsEmptyElement
                && state.Groups.Count > 0 && state.Groups[^1].Depth == reader.Depth && state.Groups[^1].HasFilter;
            if (reader.NodeType == XmlNodeType.EndElement || closeEmptyFilter)
            {
                if (reader.Depth == state.FilterDepth) SvgDefinitionParser.RegisterFilter(state);
                if (reader.Depth == state.SkipDepth) state.SkipDepth = -1;
                if (reader.Depth == state.DescriptionDepth) state.DescriptionDepth = -1;
                if (reader.Depth == state.DefinitionsDepth)
                {
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
                    if (state.PatternContentBuilder is {} unused)
                    {
                        // Anonymous resources own no deferred scene. Drop borrowed ranges before disposal.
                        while (state.DeferredPaintLeaves.Count > state.PatternCommandStart.Paint)
                        {
                            workCounter.Consume(cancellationToken);
                            state.DeferredPaintLeaves.RemoveLast();
                        }
                        while (state.PendingTextRuns.Count > state.PatternCommandStart.Text)
                        {
                            workCounter.Consume(cancellationToken);
                            SvgPendingTextRun text = state.PendingTextRuns[^1];
                            text.Run?.Dispose(); text.Continuation?.Dispose(); text.Owner.Dispose();
                            state.PendingTextRuns.RemoveLast();
                        }
                        while (state.PendingGroupClips.Count > state.PatternCommandStart.Clip)
                        {
                            workCounter.Consume(cancellationToken);
                            state.PendingGroupClips.RemoveLast();
                        }
                        while (state.PendingGroupMasks.Count > state.PatternCommandStart.Mask)
                        {
                            workCounter.Consume(cancellationToken);
                            state.PendingGroupMasks.RemoveLast();
                        }
                        while (state.PendingLeafMasks.Count > 0 && ReferenceEquals(state.PendingLeafMasks[^1].Scene, unused))
                        {
                            workCounter.Consume(cancellationToken);
                            state.PendingLeafMasks.RemoveLast();
                        }
                        while (state.PendingLeafClips.Count > 0 && ReferenceEquals(state.PendingLeafClips[^1].Scene, unused))
                        {
                            workCounter.Consume(cancellationToken);
                            state.PendingLeafClips.RemoveLast();
                        }
                        while (state.PatternUseCount > 0)
                        {
                            workCounter.Consume(cancellationToken);
                            state.PatternUses.RemoveLast();
                            state.PatternUseCount--;
                        }
                        while (state.PatternScopes is {} scopes && scopes.Count > state.PatternScopeStart)
                        {
                            workCounter.Consume(cancellationToken);
                            scopes.RemoveLast();
                        }
                        unused.Dispose();
                    }
                    state.PatternContentBuilder = null;
                    state.PatternRectangleSource = null;
                    state.PatternRectangleContext = default;
                    state.PatternUseStart = 0;
                    state.PatternUseCount = 0;
                    state.PatternScopeStart = 0;
                    state.PatternCommandStart = default;
                    state.PatternTemplate = default;
                    state.PatternDepth = -1;
                    state.PatternId = null;
                    state.PatternTile = null;
                }
                if (reader.Depth == state.MaskDepth)
                {
                    if (state.MaskId is not null)
                    {
                        if (!state.MaskIsDisplayed) SvgMaskSyntax.RegisterDisabled(state, workCounter, cancellationToken);
                        else if (state.MaskHasReplayContent) SvgMaskContentReplay.Register(state, workCounter, cancellationToken);
                        else if (state.MaskContentBuilder is not null) RegisterMaskScene(state, workCounter, cancellationToken);
                        else if (state.MaskContentGeometries is not null) SvgDefinitionParser.RegisterMaskContent(state, workCounter, cancellationToken);
                        else SvgDefinitionParser.RegisterMask(state, workCounter, cancellationToken);
                        if (state.MaskIsDisplayed && state.MaskReplayChildCount > 0 && state.MaskContents.TryGetValue(state.MaskId, out SvgMaskContent source))
                            state.MaskContents.Replace(state.MaskId, source with { ReplaySource = new(state.MaskReplayChildStart, state.MaskReplayChildCount) });
                    }
                    if (state.MaskContentGeometries is {} anonymousMask && state.MaskId is null)
                        foreach (SvgGeometry geometry in anonymousMask) geometry.Dispose();
                    if (state.Groups.Count == 0 || state.Groups[^1].Kind != SvgGroupKind.Mask || state.Groups[^1].Depth != reader.Depth)
                        throw new InvalidOperationException("Mask presentation scope is unbalanced.");
                    state.Groups.RemoveLast();
                    state.MaskReplayChildCount = 0;
                    state.MaskHasReplayContent = false;
                    state.MaskDepth = -1;
                    state.MaskId = null;
                    state.MaskIsAlpha = false;
                    state.MaskObjectBoundingBoxContent = false;
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
                        if (state.ClipUse is not null) SvgResourceUseReplay.RegisterClip(state, workCounter, cancellationToken);
                        else if (state.ClipBox is not null) SvgDefinitionParser.RegisterClipBox(state, workCounter, cancellationToken);
                        else if (state.ClipPolygon is not null) SvgDefinitionParser.RegisterClipPolygon(state, workCounter, cancellationToken);
                        else SvgDefinitionParser.RegisterClipBox(state, workCounter, cancellationToken);
                    }
                    if (state.ClipPathId is null) state.ClipPolygon?.SourcePath?.Dispose();
                    state.ClipUse = null;
                    state.ClipPathDepth = -1;
                    state.ClipPathId = null;
                    state.ClipBox = null;
                    state.ClipRectangleSource = null;
                    state.ClipPolygon = null;
                    state.ClipIsObjectBoundingBox = false;
                    state.ClipIsVisible = true;
                }
                state.SymbolDefinition?.EndGroup(reader.Depth, state);
                SvgRenderedGroupSourceParser.EndGroup(reader.Depth, state, workCounter, cancellationToken);
                if (reader.Depth == state.SymbolDepth) SvgDefinitionParser.RegisterSymbol(state, workCounter, cancellationToken);
                SvgViewportFrame? closingViewport = state.Viewports.Count > 0 && state.Viewports[^1].Depth == reader.Depth ? state.Viewports[^1] : null;
                if (closingViewport is not null) state.Viewports.RemoveLast();
                if (closingViewport?.OwnOpacity is {} closingOpacity && closingOpacity.Alpha != 255)
                {
                    state.Scene.EndOpacityScope();
                }
                if (closingViewport?.HasAffineClipScope == true) state.Scene.EndOpacityScope();
                if (state.ClipDepths.Count > 0 && state.ClipDepths[^1] == reader.Depth) state.ClipDepths.RemoveLast();
                if (state.Groups.Count > 0 && state.Groups[^1].Depth == reader.Depth && state.Groups[^1].Kind == SvgGroupKind.PatternGroup)
                {
                    if (state.Groups[^1].HasFilter)
                    {
                        SvgSceneBuilder builder = state.PatternContentBuilder ?? throw new InvalidOperationException("A filtered pattern group requires its replay builder.");
                        if (builder.GeometryCount == state.Groups[^1].FilterGeometryStart)
                            builder.AddGeometry(SvgPaintRecording.CreateEffectAnchor(builder, state, state.Groups[^1].RecordingScope));
                        builder.EndOpacityScope();
                        state.FilterGroupDepth--;
                    }
                    if (state.Groups[^1].HasMaskScope) state.MaskScopes.RemoveLast();
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
                    if (group.HasFilter)
                    {
                        if (state.Scene.GeometryCount == group.FilterGeometryStart)
                            state.Scene.AddGeometry(SvgPaintRecording.CreateEffectAnchor(state.Scene, state, group.RecordingScope));
                        state.FilterGroupDepth--;
                    }
                    if (group.IsolatesOpacity || group.HasDeferredMask || group.HasFilter)
                        state.IsolatedViewportDepth = group.PreviousIsolatedViewportDepth;
                    if (group.HasFilter && group.HasDeferredMask) state.Scene.EndOpacityScope();
                    if (snapshotStart.PendingMask is {} pendingMaskIndex)
                    {
                        workCounter.Consume(cancellationToken);
                        PendingGroupMask pendingMask = state.PendingGroupMasks[pendingMaskIndex];
                        state.PendingGroupMasks[pendingMaskIndex] = pendingMask with { EndCommandIndex = state.Scene.CommandCount };
                    }
                    if (group.IsolatesOpacity || group.HasDeferredMask || group.HasFilter)
                    {
                        state.Scene.EndOpacityScope();
                    }
                    if (snapshotStart.PendingClip is {} pendingGroupIndex)
                    {
                        workCounter.Consume(cancellationToken);
                        PendingGroupClip pendingGroup = state.PendingGroupClips[pendingGroupIndex];
                        state.PendingGroupClips[pendingGroupIndex] = pendingGroup with { EndCommandIndex = state.Scene.CommandCount };
                    }
                    if (group.HasIsolatedViewportScope) state.Scene.EndOpacityScope();
                }
                if (reader.Depth == rootDepth)
                {
                    SvgDefinitionParser.ResolvePendingGradientHrefs(state, workCounter, cancellationToken);
                    SvgPatternReferenceResolver.Resolve(state, true, workCounter, cancellationToken);
                    SvgDeferredPaintLeaves.Resolve(root, state, limits, workCounter, cancellationToken);
                    if (state.DeferredTextFailure is {} textFailure) throw textFailure;
                    SvgTextPathLayout.Resolve(state, limits, workCounter, cancellationToken);
                    state.ValidateSymbolReferences(workCounter, cancellationToken);
                    state.PaintResources?.ValidateUnusedEffects(state, workCounter, cancellationToken);
                    SvgResourceUseReplay.ResolveClips(state, workCounter, cancellationToken);
                    SvgMaskContentReplay.Resolve(root, state, workCounter, cancellationToken);
                    SvgPatternUseReplay.Resolve(root, state, limits, workCounter, cancellationToken);
                    SvgReferenceResolver.ResolvePendingUses(state.Definitions, state.PendingUses, state.Scene, state, workCounter, cancellationToken);
                    SvgClipPathSyntax.ResolvePendingLeafClips(state, workCounter, cancellationToken);
                    SvgClipPathSyntax.ResolvePendingGroupClips(state, workCounter, cancellationToken);
                    SvgMaskSyntax.ResolvePendingLeafMasks(state, workCounter, cancellationToken);
                    SvgMaskSyntax.ResolvePendingGroupMasks(state, workCounter, cancellationToken);
                    SvgPatternUseReplay.Complete(state, workCounter, cancellationToken);
                    if (root.SourceOpacity.Value != 1) state.Scene.EndOpacityScope();
                    SvgLeafMaskComposition.Emit(state, workCounter, cancellationToken);
                    SvgSceneStorage sceneStorage = state.Scene.Build();
                    state.ReleaseDefinitions();
                    try
                    {
                        ValidateDocumentEnd(reader, workCounter, cancellationToken);
                        return new SvgRoot(root.Width, root.Height, root.ViewBox, sceneStorage.Geometries, sceneStorage.Commands, state.HasUnsupportedContent, workCounter.Used, sceneStorage)
                            { UnsupportedDiagnostic = state.UnsupportedDiagnostic };
                    }
                    catch { sceneStorage.Dispose(); throw; }
                }
            }
        }
        throw new SvgException(SvgErrorCode.MalformedInput, "SVG root element is not closed.");

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
