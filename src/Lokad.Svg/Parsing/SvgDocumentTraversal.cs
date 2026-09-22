using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Traverses one bounded SVG document after reader setup without materializing a DOM.</summary>
internal static class SvgDocumentTraversal
{
    internal static SvgRoot Parse(XmlReader reader, SvgRenderLimits limits, RenderBudget budget, IReadOnlyDictionary<string, SvgColor> namedColors, Lokad.Svg.Fonts.SvgFontSnapshot? fontSnapshot, CancellationToken cancellationToken)
        => Parse(reader, limits, budget, namedColors, fontSnapshot, new ParserWorkCounter(limits.MaximumWorkUnits), cancellationToken);

    /// <summary>Traverses one document under a caller-owned operation work counter.</summary>
    internal static SvgRoot Parse(XmlReader reader, SvgRenderLimits limits, RenderBudget budget, IReadOnlyDictionary<string, SvgColor> namedColors, Lokad.Svg.Fonts.SvgFontSnapshot? fontSnapshot, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workCounter);
        while (reader.Read())
        {
            workCounter.Consume(cancellationToken);
            if (reader.NodeType != XmlNodeType.Element) continue;
            SvgStructuralAdmission.ValidateElementStructure(reader, limits);
            SvgStructuralAdmission.ThrowIfNotSvgRoot(reader);
            SvgRootAttributes root = SvgRootAttributes.Parse(reader, namedColors, workCounter, cancellationToken);
            return TraverseRoot(reader, limits, budget, namedColors, fontSnapshot, cancellationToken, workCounter, root);
        }
        throw new SvgException(SvgErrorCode.MalformedInput, "SVG input has no root element.");
    }

    private static SvgRoot TraverseRoot(XmlReader reader, SvgRenderLimits limits, RenderBudget budget, IReadOnlyDictionary<string, SvgColor> namedColors, Lokad.Svg.Fonts.SvgFontSnapshot? fontSnapshot, CancellationToken cancellationToken, ParserWorkCounter workCounter, SvgRootAttributes root)
    {
        if (reader.IsEmptyElement)
        {
            ValidateDocumentEnd(reader, workCounter, cancellationToken);
            return new SvgRoot(root.Width, root.Height, root.ViewBox, [], [], false, workCounter.Used, null);
        }
        int rootDepth = reader.Depth;
        using SvgDocumentState state = new(budget, workCounter, cancellationToken);
        // The root element consumes one node, matching font-request discovery's uniform per-element count.
        state.NodeCount = 1;
        if (root.Opacity != 255) state.Scene.BeginOpacityScope(root.Opacity);
        while (reader.Read())
        {
            workCounter.Consume(cancellationToken);
            if (reader.NodeType == XmlNodeType.Element)
            {
                SvgStructuralAdmission.ValidateElementStructure(reader, limits);
                if (state.NodeCount >= limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                state.NodeCount++;
                bool skipped = state.SkipDepth >= 0 && reader.Depth > state.SkipDepth;
                if (!skipped && state.Switches.Count > 0 && reader.Depth == state.Switches[state.Switches.Count - 1].Depth + 1)
                {
                    SwitchScope top = state.Switches[state.Switches.Count - 1];
                    if (top.FirstChildConsumed)
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
                else if (SvgDefinitionParser.TryParse(reader, directChild, inScope, root, state, namedColors, workCounter, cancellationToken)) { }
                else if (state.SymbolDepth >= 0 && reader.Depth > state.SymbolDepth) state.HasUnsupportedContent = true;
                else if ((directChild || inViewport) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "svg")
                {
                    SvgElementAttributePolicy.ValidateNestedViewport(reader, workCounter, cancellationToken);
                    SvgViewportFrame? parentFrame = state.EffectiveFrame;
                    SvgNestedViewportSyntax viewport = SvgNestedViewportSyntax.Parse(reader, parentFrame?.ViewportWidth ?? root.Width, parentFrame?.ViewportHeight ?? root.Height);
                    SvgViewBox? nestedViewBox = SvgGeometrySyntax.ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio"));
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
                    if (!reader.IsEmptyElement) state.Viewports.Add(new SvgViewportFrame(composedOffsetX, composedOffsetY, composedScaleX, composedScaleY, viewport.Width, viewport.Height, box, reader.Depth));
                    state.SawStyleableNode = true;
                }
                else if (inViewport && textElement && reader.NamespaceURI == "http://www.w3.org/2000/svg") throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text inside nested viewports is not implemented.");
                else if (state.ClipDepths.Count > 0 && textElement && reader.NamespaceURI == "http://www.w3.org/2000/svg") throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text inside clipped groups is not implemented.");
                else if (SvgRenderedLeafParser.TryParse(reader, directChild, inScope, root, state, limits, budget, namedColors, fontSnapshot, workCounter, cancellationToken))
                {
                    state.HasUnsupportedContent |= !textElement && !reader.IsEmptyElement;
                    state.SawStyleableNode = true;
                }
                else if (inScope && reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsGroupOrLinkContainer(reader.LocalName))
                {
                    SvgParsedGroup parsedGroup = SvgGroupParser.Parse(reader, directChild, root, state, namedColors, workCounter, cancellationToken);
                    if (!reader.IsEmptyElement)
                    {
                        state.Groups.Add(parsedGroup.Scope);
                        if (parsedGroup.Scope.Kind == SvgGroupKind.Switch) state.Switches.Add(new SwitchScope(reader.Depth, false));
                        string? clipReference = reader.GetAttribute("clip-path");
                        if (clipReference is not null)
                        {
                            if (!state.Clips.TryGetValue(SvgClipPathSyntax.ParseClipId(clipReference), out SvgClipBox clipBox)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local clip-path references are implemented.");
                            SvgViewportFrame? clipParent = state.EffectiveFrame;
                            SvgClipBox clip = clipParent is null ? clipBox : clipParent.Value.Clip.Intersect(clipBox);
                            if (clipParent?.IsScaled ?? false) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths in scaled viewports are not implemented.");
                            state.Viewports.Add(new SvgViewportFrame(clipParent?.OffsetX ?? 0, clipParent?.OffsetY ?? 0, clipParent?.ScaleX ?? 1, clipParent?.ScaleY ?? 1, clipParent?.ViewportWidth, clipParent?.ViewportHeight, clip, reader.Depth));
                            state.ClipDepths.Add(reader.Depth);
                        }
                        if (parsedGroup.Scope.Kind == SvgGroupKind.Group) state.GroupDepth++;
                        if (parsedGroup.Scope.IsolatesOpacity)
                        {
                            state.Scene.BeginOpacityScope(parsedGroup.Opacity);
                            state.HasOpacityScope = true;
                        }
                    }
                }
                else if (directChild && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "style")
                {
                    SvgRootStyleSheetParser.Parse(reader, root, state, limits.MaximumNodes, budget, workCounter, cancellationToken);
                }
                else if (reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsStyleScriptOrAnimationElement(reader.LocalName)) state.HasUnsupportedContent = true;
                else if (directChild && reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsInertContainer(reader.LocalName)) { }
                else if (inScope) state.HasUnsupportedContent = true;
            }
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.Depth == state.SkipDepth) state.SkipDepth = -1;
                if (state.Switches.Count > 0 && state.Switches[state.Switches.Count - 1].Depth == reader.Depth) state.Switches.RemoveLast();
                if (reader.Depth == state.DefinitionsDepth) state.DefinitionsDepth = -1;
                if (reader.Depth == state.GradientDepth)
                {
                    if (state.GradientId is not null) SvgDefinitionParser.RegisterGradient(state, workCounter, cancellationToken);
                    state.GradientDepth = -1;
                    state.GradientId = null;
                    state.Gradient = null;
                    state.GradientStops.Clear();
                }
                if (reader.Depth == state.PatternDepth)
                {
                    if (state.PatternId is not null) SvgDefinitionParser.RegisterPattern(state, workCounter, cancellationToken);
                    state.PatternDepth = -1;
                    state.PatternId = null;
                    state.PatternTile = null;
                }
                if (reader.Depth == state.MaskDepth)
                {
                    if (state.MaskId is not null) SvgDefinitionParser.RegisterMask(state, workCounter, cancellationToken);
                    state.MaskDepth = -1;
                    state.MaskId = null;
                    state.MaskIsAlpha = false;
                    state.MaskValue = null;
                    state.MaskCustomRegion = null;
                }
                if (reader.Depth == state.ClipPathDepth)
                {
                    if (state.ClipPathId is not null) SvgDefinitionParser.RegisterClipBox(state, workCounter, cancellationToken);
                    state.ClipPathDepth = -1;
                    state.ClipPathId = null;
                    state.ClipBox = null;
                    state.ClipIsObjectBoundingBox = false;
                }
                if (reader.Depth == state.SymbolDepth) { state.SymbolDepth = -1; state.SymbolId = null; state.SymbolViewBox = null; }
                if (state.Viewports.Count > 0 && state.Viewports[^1].Depth == reader.Depth) state.Viewports.RemoveLast();
                if (state.ClipDepths.Count > 0 && state.ClipDepths[^1] == reader.Depth) state.ClipDepths.RemoveLast();
                if (state.Groups.Count > 0 && state.Groups[^1].Depth == reader.Depth)
                {
                    SvgGroupScope group = state.Groups[^1];
                    state.Groups.RemoveLast();
                    if (group.Kind == SvgGroupKind.Group) state.GroupDepth--;
                    if (group.IsolatesOpacity)
                    {
                        state.Scene.EndOpacityScope();
                        state.HasOpacityScope = false;
                    }
                }
                if (reader.Depth == rootDepth)
                {
                    SvgReferenceResolver.ResolvePendingUses(state.Definitions, state.PendingUses, state.Scene, workCounter, cancellationToken);
                    if (root.Opacity != 255) state.Scene.EndOpacityScope();
                    SvgSceneStorage sceneStorage = state.Scene.Build();
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
    }

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
