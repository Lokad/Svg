using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Routes bounded definitions, symbols and local instances while preserving streaming order.</summary>
internal static class SvgDefinitionParser
{
    internal static bool TryParse(XmlReader reader, bool directChild, bool isEligible, SvgRootAttributes root, SvgDocumentState state, SvgRenderLimits limits, IReadOnlyDictionary<string, SvgColor> namedColors, Lokad.Svg.Fonts.SvgFontSnapshot? fontSnapshot, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (state.FilterDefinition is {} activeFilter && reader.Depth > state.FilterDepth)
        {
            if (reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsStyleScriptOrAnimationElement(reader.LocalName)) state.HasUnsupportedContent = true;
            activeFilter.Add(reader, reader.Depth == state.FilterDepth + 1);
            return true;
        }
        if (state.HasPatternTemplateReferences && state.PatternDepth < 0 && reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName))
            SvgPatternReferenceResolver.Resolve(state, false, workCounter, cancellationToken);
        bool symbolLocation = state.SymbolDepth >= 0 && state.SymbolDefinition is {} activeSymbol && reader.Depth == activeSymbol.ChildDepth(state.SymbolDepth);
        if (symbolLocation && reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgSymbolContentParser.TryParse(reader, root, state, namedColors, workCounter, cancellationToken, false)) return true;
        bool inDefinitions = (state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1) || symbolLocation;
        if (inDefinitions && state.SymbolDepth < 0 && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "g")
        {
            // A dormant group shares ordered source replay with symbols, without
            // adding a viewport or painting its definition location.
            state.SymbolDefinition = SvgSymbolDefinitionBuilder.Create(state.Budget, workCounter, cancellationToken,
                new(SvgPaint.Default, default, null, false, null, null, SvgPaintOpacity.Opaque));
            _ = SvgSymbolContentParser.TryParse(reader, root, state, namedColors, workCounter, cancellationToken, true);
            if (reader.IsEmptyElement) RegisterSymbol(state, workCounter, cancellationToken);
            else state.SymbolDepth = reader.Depth;
            return true;
        }
        bool rootDefinition = state.DefinitionsDepth < 0 && state.SymbolDepth < 0 && directChild && reader.GetAttribute("id") is not null;
        bool groupNestedDefinition = state.DefinitionsDepth < 0 && state.SymbolDepth < 0 && state.PatternId is null && state.MaskId is null && state.ClipPathId is null && state.GroupDepth > 0 && reader.GetAttribute("id") is not null;
        if (inDefinitions && state.MarkerDefinition is null && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "text")
        {
            SvgElementAttributePolicy.ValidateText(reader, workCounter, cancellationToken);
            string? id = reader.GetAttribute("id");
            if (id is not null && (id == state.SymbolId || state.Definitions.TryGetValue(id, out _) || state.GroupSnapshots.TryGetValue(id, out _) || state.RenderedGroupDefinitions.TryGetValue(id, out _)))
                throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
            SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
            SvgComputedStyle style = SvgComputedStyleResolver.Resolve(reader, context.StyleSheet, workCounter, cancellationToken, true);
            SvgRetainedTextSource source = SvgRetainedTextSource.Capture(reader, context, state, limits);
            state.NodeCount += source.ChildNodeCount; state.TextCharacterCount += source.CharacterCount;
            SvgDefinition definition = new([], false, null, null)
            {
                TextSource = source, SourceDisplayed = SvgPresentationResolver.ParseDisplay(style.Display),
                SourceVisible = style.Visibility is null ? null : SvgPresentationResolver.ParseVisibility(style.Visibility, true)
            };
            if (symbolLocation)
            {
                SvgSymbolDefinitionBuilder builder = state.SymbolDefinition ?? throw new InvalidOperationException("Text source requires its definition builder.");
                builder.EnableContent(); builder.Add(definition); state.HasRetainedSymbolContent = true;
            }
            if (id is not null) state.Definitions.TryAdd(state.Strings.Copy(id), definition with { BorrowedFromSymbol = symbolLocation });
            return true;
        }
        if ((inDefinitions || rootDefinition || groupNestedDefinition) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "filter")
        {
            state.FilterDefinition = SvgFilterDefinitionBuilder.Create(reader, state.Budget, workCounter, cancellationToken);
            if (reader.IsEmptyElement) RegisterFilter(state);
            else state.FilterDepth = reader.Depth;
            return true;
        }
        if ((inDefinitions || rootDefinition || groupNestedDefinition) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName))
        {
            bool symbolChild = state.SymbolDepth >= 0;
            string? id = reader.GetAttribute("id");
            if (id is not null && (id == state.SymbolId || state.Definitions.TryGetValue(id, out _) || state.GroupSnapshots.TryGetValue(id, out _) || state.RenderedGroupDefinitions.TryGetValue(id, out _)))
                throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
            if (reader.LocalName == "rect") SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken);
            else if (reader.LocalName == "line") SvgElementAttributePolicy.ValidateLine(reader, workCounter, cancellationToken);
            else if (reader.LocalName == "path") SvgElementAttributePolicy.ValidatePath(reader, workCounter, cancellationToken);
            else if (reader.LocalName == "circle" || reader.LocalName == "ellipse") SvgElementAttributePolicy.ValidateCircle(reader, reader.LocalName, workCounter, cancellationToken);
            else if (reader.LocalName == "image") SvgElementAttributePolicy.ValidateImage(reader, workCounter, cancellationToken);
            else SvgElementAttributePolicy.ValidatePointList(reader, reader.LocalName, workCounter, cancellationToken);
            SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
            if (symbolChild)
            {
                SvgSymbolPresentation symbol = (state.SymbolDefinition ?? throw new InvalidOperationException("Symbol children require a presentation context.")).Presentation;
                context = context with { InheritedPresentation = new(symbol.Paint, true, true, SvgAffineTransform.Identity) { OwnOpacity = symbol.SourceOpacity, Markers = symbol.Markers, PaintOrder = symbol.PaintOrder ?? SvgPaintOrder.FillStrokeMarkers, PaintServerFill = symbol.PaintServerFill }, InheritedStroke = symbol.Stroke, InheritedFillRule = symbol.FillRule, RecordingScope = new(1) };
            }
            SvgComputedStyle retainedStyle = SvgComputedStyleResolver.Resolve(reader, context.StyleSheet, workCounter, cancellationToken);
            if (symbolChild && retainedStyle.InheritsOpacity)
            {
                (state.SymbolDefinition ?? throw new InvalidOperationException("Symbol children require a presentation context.")).EnableContent();
                state.HasRetainedSymbolContent = true;
            }
            if (state.MarkerDefinition is not null && SvgMarkerStyle.Resolve(retainedStyle, context.InheritedPresentation.Markers).HasMarkers)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Markers on retained definition leaves are not implemented.");
            // Definition bodies are dormant. Replay source markers only after the
            // complete registry is available, with each instance's presentation.
            SvgPaintableElementContext dormant = !SvgMarkerStyle.Resolve(retainedStyle, context.InheritedPresentation.Markers).HasMarkers ? context : context with
            {
                AuthoredStyle = retainedStyle with { MarkerStart = "none", MarkerMid = "none", MarkerEnd = "none" },
                InheritedPresentation = context.InheritedPresentation with { Markers = default }
            };
            SvgViewBox? symbolViewBox = null;
            // Relative geometry binds in the live instance viewport, not its
            // definition location. Keep the existing bounded source adapter.
            bool deferredOval = reader.LocalName is "circle" or "ellipse"
                && (IsPercentage("cx") || IsPercentage("cy") || IsPercentage("r") || IsPercentage("rx") || IsPercentage("ry"));
            // Keep rectangle definition validation below; live instances reconstruct authored percentages.
            bool relativeRectangle = reader.LocalName == "rect"
                && (IsPercentage("x") || IsPercentage("y") || IsPercentage("width") || IsPercentage("height") || IsPercentage("rx") || IsPercentage("ry"));
            bool IsPercentage(string attribute)
                => reader.GetAttribute(attribute) is {} value && value.AsSpan().Trim().EndsWith("%", StringComparison.Ordinal);
            // Dormant geometry does not paint. Keep its source when an inherited or
            // authored server is not available yet; live consumers construct it later.
            bool deferredSource = deferredOval || MissingPaint(retainedStyle.Fill ?? context.InheritedPresentation.PaintServerFill)
                || MissingPaint(retainedStyle.Stroke ?? context.InheritedStroke.Stroke);
            bool MissingPaint(string? value)
            {
                if (value is null || !SvgLocalUrl.TryParseId(value, out string paintId)) return false;
                workCounter.Consume(cancellationToken);
                return !state.Gradients.TryGetValue(paintId, out _) && !state.RadialGradients.TryGetValue(paintId, out _)
                    && !state.Patterns.TryGetValue(paintId, out _);
            }
            if (retainedStyle.Filter is not null) dormant = dormant with { AuthoredStyle = (dormant.AuthoredStyle ?? retainedStyle) with { Filter = null } };
            SvgDefinition definition = reader.LocalName == "image" || deferredSource ? new([], false, null, symbolViewBox)
                : reader.LocalName == "rect" ? ParseRectangleDefinition(reader, dormant, state, symbolViewBox)
                : reader.LocalName == "line" ? ParseLineDefinition(reader, dormant, state)
                : ParseShapeDefinition(reader, dormant, state, symbolViewBox, true);
            bool retained = false;
            try
            {
                SvgPaintRecording.FinishDefinition(definition.Geometries, state, workCounter, cancellationToken);
                SvgDefinition source = deferredSource ? definition : BindSymbolFill(definition, reader, context, state, workCounter, cancellationToken, false);
                SvgComputedStyle sourceStyle = SvgComputedStyleResolver.Resolve(reader, context.StyleSheet, workCounter, cancellationToken);
                definition = definition with { SourceFillBindings = source.SymbolFillBindings, SourceDisplayed = SvgPresentationResolver.ParseDisplay(sourceStyle.Display), SourceVisible = sourceStyle.Visibility is null ? null : SvgPresentationResolver.ParseVisibility(sourceStyle.Visibility, true), LeafSource = reader.LocalName == "image"
                    ? SvgDefinitionLeafSource.CaptureImage(reader, context, state, limits) : deferredSource || relativeRectangle
                        ? SvgDefinitionLeafSource.CaptureUnconstructed(reader, context, state) : SvgDefinitionLeafSource.Capture(reader, context, state) };
                if (symbolChild)
                {
                    definition = BindSymbolFill(definition, reader, context, state, workCounter, cancellationToken, true);
                    SvgSymbolDefinitionBuilder builder = state.SymbolDefinition ?? throw new InvalidOperationException("Symbol children require an open definition builder.");
                    if (reader.LocalName == "image" || deferredSource || relativeRectangle || SvgLocalUrl.ReadReference(reader, "mask") is not null) { builder.EnableContent(); state.HasRetainedSymbolContent = true; }
                    builder.Add(definition);
                    retained = true;
                }
                if (id is not null)
                {
                    workCounter.Consume(cancellationToken);
                    if (state.Definitions.TryGetValue(id, out _)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
                    string retainedId = state.Strings.Copy(id);
                    SvgDefinition registered = symbolChild ? definition with { SymbolFillBindings = source.SymbolFillBindings, BorrowedFromSymbol = true } : definition;
                    if (!state.Definitions.TryAdd(retainedId, registered)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
                    retained = true;
                }
            }
            finally { if (!retained) foreach (SvgGeometry geometry in definition.Geometries) geometry.Dispose(); }
            if (inDefinitions && !reader.IsEmptyElement)
            {
                SvgLeafSubtree subtree = SvgStructuralAdmission.ConsumeLeafSubtree(reader, limits, state.NodeCount, workCounter, cancellationToken);
                state.NodeCount = subtree.NodeCount;
                state.HasUnsupportedContent |= subtree.HasUnsupportedChildren;
            }
            return inDefinitions;
        }
        if (reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName is ("symbol" or "marker")
            && SvgStructuralVocabulary.IsSymbolDefinitionLocation(directChild, isEligible && state.GroupDepth > 0, state.DefinitionsDepth, state.SymbolDepth, reader.Depth))
        {
            bool marker = reader.LocalName == "marker";
            if (!marker) SvgElementAttributePolicy.ValidateSymbol(reader, workCounter, cancellationToken);
            InlineStyle sheet = SvgDocumentStyleResolver.Resolve(reader, state, workCounter, cancellationToken);
            SvgPaintableElementContext markerContext = marker ? SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken) : default;
            SvgComputedStyle style = marker ? markerContext.ResolveStyle(reader, true) : SvgComputedStyleResolver.Resolve(reader, sheet, workCounter, cancellationToken, true);
            state.MarkerDefinition = marker ? SvgMarkerSyntax.Parse(reader, style, new(state.EffectiveFrame?.ViewportWidth ?? root.UserViewportWidth, state.EffectiveFrame?.ViewportHeight ?? root.UserViewportHeight), markerContext.InheritedTextStyle, markerContext.StyleSheet, workCounter, cancellationToken) : null;
            SvgPaint inheritedPaint = marker ? markerContext.InheritedPresentation.Paint : SvgPaint.Default;
            // Pinned Chrome honors display on symbol instances, but ignores it on marker definitions.
            bool displayed = SvgPresentationResolver.ParseDisplay(style.Display);
            bool shader = SvgPatternSyntax.IsPatternFill(style.Fill);
            SvgPaint paint = SvgPaintResolver.Resolve(shader ? "#ffffff" : style.Fill, style.FillOpacity, style.Color, inheritedPaint, namedColors);
            string? shaderFill = null, shaderId = null;
            if (shader && style.Fill is string fill)
            {
                workCounter.ConsumeText(fill.Length, cancellationToken);
                shaderFill = CopyStyleValue(fill, state);
                shaderId = CopyStyleValue(SvgGradientSyntax.ParsePaintId(fill), state);
            }
            SvgInheritedStrokeStyle stroke = new(CopyStyleValue(style.Linecap, state), CopyStyleValue(style.Linejoin, state), CopyStyleValue(style.Dasharray, state), CopyStyleValue(style.Dashoffset, state), CopyStyleValue(style.Width, state), CopyStyleValue(style.Miterlimit, state), CopyStyleValue(style.StrokeOpacity, state), CopyStyleValue(style.Stroke, state));
            SvgSymbolPresentation presentation = new(paint, stroke, CopyStyleValue(style.FillRule, state), marker || style.Fill is not null,
                marker ? paint.FillOpacity : style.FillOpacity is null ? null : SvgColorParser.ParsePaintOpacity(style.FillOpacity), marker ? paint.CurrentColor : style.Color is null ? null : SvgColorParser.Parse(style.Color, namedColors), marker ? style.GetOpacity() : style.ResolveOpacity(SvgPaintOpacity.Opaque).GetOpacity()) { IsHidden = !marker && !displayed, IsVisible = marker ? SvgPresentationResolver.ParseVisibility(style.Visibility, markerContext.InheritedPresentation.IsVisible) : style.Visibility is null ? null : SvgPresentationResolver.ParseVisibility(style.Visibility, true), InheritsOpacity = !marker && style.InheritsOpacity, Markers = new SvgMarkerStyle(style.MarkerStart, style.MarkerMid, style.MarkerEnd).Retain(state, workCounter, cancellationToken), PaintOrder = style.PaintOrder, PaintServerFill = shaderFill, PaintServerId = shaderId };
            if (!marker) presentation = presentation with
            {
                TextStyleSource = SvgRetainedTextSource.CaptureElement(reader,
                    SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken), state, limits)
            };
            string? id = reader.GetAttribute("id");
            if (id is not null && (state.Definitions.TryGetValue(id, out _) || state.GroupSnapshots.TryGetValue(id, out _) || state.RenderedGroupDefinitions.TryGetValue(id, out _))) throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
            state.SymbolId = id is null ? null : state.Strings.Copy(id);
            state.SymbolViewBox = SvgGeometrySyntax.ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio"), workCounter, cancellationToken);
            state.SymbolDefinition = SvgSymbolDefinitionBuilder.Create(state.Budget, workCounter, cancellationToken, presentation);
            state.SymbolDefinition.EstablishesViewport = true;
            // Keep authored symbol fill-rule on its scope. A flattened child reconstructed
            // for an instance would otherwise inherit the caller's rule instead.
            if (shaderFill is not null || marker || presentation.IsHidden || presentation.IsVisible is not null || presentation.Markers.HasMarkers || presentation.InheritsOpacity || presentation.FillRule is not null || sheet != default)
            {
                state.SymbolDefinition.EnableContent();
                state.HasRetainedSymbolContent = true;
            }
            if (reader.IsEmptyElement) RegisterSymbol(state, workCounter, cancellationToken);
            else state.SymbolDepth = reader.Depth;
            return true;
        }
        bool resourceDefinition = state.MaskDepth >= 0 && reader.Depth == state.MaskDepth + 1 && reader.LocalName is ("clipPath" or "linearGradient" or "radialGradient");
        bool inClipDefinitions = state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1;
        if ((inClipDefinitions || rootDefinition || resourceDefinition) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "clipPath")
        {
            SvgElementAttributePolicy.ValidateClipPath(reader, workCounter, cancellationToken);
            state.ClipPathId = reader.GetAttribute("id");
            state.ClipBox = null;
            state.ClipRectangleSource = null;
            state.ClipPolygon = null;
            state.ClipIsObjectBoundingBox = SvgClipPathSyntax.ParseClipUnits(reader.GetAttribute("clipPathUnits"));
            SvgPaintableElementContext clipParent = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
            SvgComputedStyle clipParentStyle = SvgComputedStyleResolver.Resolve(reader, clipParent.StyleSheet, workCounter, cancellationToken);
            state.ClipIsDisplayed = SvgPresentationResolver.ParseDisplay(clipParentStyle.Display);
            state.ClipIsEvenOdd = SvgClipPathSyntax.ParseClipRule((clipParentStyle.ClipRule ?? clipParent.InheritedClipRule ?? "nonzero").AsSpan());
            state.ClipIsVisible = SvgPresentationResolver.ParseVisibility(clipParentStyle.Visibility, clipParent.InheritedPresentation.IsVisible);
            if (reader.IsEmptyElement)
            {
                if (state.ClipPathId is not null) RegisterClipBox(state, workCounter, cancellationToken);
            }
            else state.ClipPathDepth = reader.Depth;
            return true;
        }
        if (state.ClipPathDepth >= 0 && reader.Depth == state.ClipPathDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg")
        {
            if (state.ClipUse is not null && reader.LocalName != "use") throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-element clip content is implemented.");
            if (reader.LocalName == "use")
            {
                SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
                context = context with { InheritedPresentation = context.InheritedPresentation with { IsDisplayed = true, IsVisible = state.ClipIsVisible } };
                if (SvgResourceUseReplay.Retain(reader, context, root, state) is not {} use) return true;
                if (state.ClipIsObjectBoundingBox) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse referenced clip content is implemented.");
                if (state.ClipUse is not null || state.ClipBox is not null || state.ClipPolygon is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-element clip content is implemented.");
                state.ClipUse = use;
                return true;
            }
            if (reader.LocalName == "rect")
            {
                SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken);
                SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
                InlineStyle clipInline = SvgInlineStyleParser.ParseGeometry(reader.GetAttribute("style"), workCounter, cancellationToken);
                SvgComputedStyle clipStyle = SvgComputedStyle.Resolve(reader, clipInline, context.StyleSheet);
                RetainClipSource(reader, context, clipStyle, state);
                SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader, state.ClipIsObjectBoundingBox ? new(1, 1) : context.LengthBases);
                // A resource uses its own child map; ancestors place the referencing element.
                SvgAffineTransform clipTransform = SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform"), workCounter, cancellationToken);
                SvgAffineViewportClip source = new(new(syntax.X, syntax.Y, syntax.Width, syntax.Height), clipTransform);
                string? nestedClip = SvgClipPathSyntax.ResolveClipId(reader, state);
                nestedClip = nestedClip is null ? null : state.Strings.Copy(nestedClip);
                if (!SvgPresentationResolver.IsPainted(clipStyle.Display, clipStyle.Visibility, true, state.ClipIsVisible))
                {
                    SvgStructuralAdmission.ConsumeLeafChildren(reader, state, workCounter, cancellationToken);
                    return true;
                }
                PromoteClipRectangle(state, workCounter, cancellationToken);
                double radiusX = syntax.CornerX ?? syntax.CornerY ?? 0;
                double radiusY = syntax.CornerY ?? syntax.CornerX ?? 0;
                if (radiusX > 0 && radiusY > 0 && syntax.Width > 0 && syntax.Height > 0)
                {
                    using BudgetedParserList<SvgAuthoredPathCommand> commands = new(state.Budget, SvgMemoryCategory.Scene, workCounter, cancellationToken);
                    SvgRoundedRectPath.Append(commands, syntax.X, syntax.Y, syntax.Width, syntax.Height, radiusX, radiusY);
                    AppendClipChild(CreateClipSource(commands.Detach(), clipTransform, false, nestedClip, state, workCounter, cancellationToken),
                        state, workCounter, cancellationToken);
                }
                else if (clipTransform.IsAxisAligned && state.ClipPolygon is null && nestedClip is null)
                {
                    SvgRectangle rect = clipTransform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, false, null));
                    state.ClipBox = new SvgClipBox(rect.X, rect.Y, rect.Width, rect.Height);
                    state.ClipRectangleSource = source;
                }
                else AppendClipChild(CreateClipRectangle(source, state, workCounter, cancellationToken) with
                    { NestedClipId = nestedClip, UsesNativeReplay = nestedClip is not null }, state, workCounter, cancellationToken);
                SvgStructuralAdmission.ConsumeLeafChildren(reader, state, workCounter, cancellationToken);
                return true;
            }
            if (reader.LocalName == "path")
            {
                SvgElementAttributePolicy.ValidatePath(reader, workCounter, cancellationToken);
                SvgPaintableElementContext clipContext = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
                // A resource uses its own child map; ancestors place the referencing element.
                SvgAffineTransform clipTransform = SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform"), workCounter, cancellationToken);
                SvgComputedStyle clipStyle = SvgComputedStyleResolver.Resolve(reader, clipContext.StyleSheet, workCounter, cancellationToken);
                RetainClipSource(reader, clipContext, clipStyle, state);
                bool isEvenOdd = clipStyle.ClipRule is null ? state.ClipIsEvenOdd : SvgClipPathSyntax.ParseClipRule(clipStyle.ClipRule.AsSpan());
                string? nestedClip = SvgClipPathSyntax.ResolveClipId(reader, state);
                nestedClip = nestedClip is null ? null : state.Strings.Copy(nestedClip);
                bool visible = SvgPresentationResolver.IsPainted(clipStyle.Display, clipStyle.Visibility, true, state.ClipIsVisible);
                if (visible) PromoteClipRectangle(state, workCounter, cancellationToken);
                SvgClipPolygon clipContent = ParseClipPathPolygon(reader.GetAttribute("d"), clipTransform, isEvenOdd, visible, limits.MaximumPathCommands, state, workCounter, cancellationToken)
                    with { IsObjectBoundingBox = state.ClipIsObjectBoundingBox };
                if (visible)
                {
                    clipContent = clipContent with { NestedClipId = nestedClip, UsesNativeReplay = clipContent.UsesNativeReplay || nestedClip is not null };
                    AppendClipChild(clipContent, state, workCounter, cancellationToken);
                }
                SvgStructuralAdmission.ConsumeLeafChildren(reader, state, workCounter, cancellationToken);
                return true;
            }
            if (reader.LocalName is "circle" or "ellipse")
            {
                SvgElementAttributePolicy.ValidateCircle(reader, reader.LocalName, workCounter, cancellationToken);
                SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
                SvgComputedStyle clipStyle = SvgComputedStyleResolver.Resolve(reader, context.StyleSheet, workCounter, cancellationToken);
                SvgViewportLengthBases clipBases = state.ClipIsObjectBoundingBox ? new(1, 1) : context.LengthBases;
                RetainClipSource(reader, context, clipStyle, state);
                SvgEllipseSyntax oval = reader.LocalName == "circle" ? SvgEllipseSyntax.ParseCircle(reader, clipBases) : SvgEllipseSyntax.ParseEllipse(reader, clipBases);
                SvgAffineTransform childMap = SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform"), workCounter, cancellationToken);
                if (!SvgPresentationResolver.IsPainted(clipStyle.Display, clipStyle.Visibility, true, state.ClipIsVisible))
                {
                    SvgStructuralAdmission.ConsumeLeafChildren(reader, state, workCounter, cancellationToken);
                    return true;
                }
                PromoteClipRectangle(state, workCounter, cancellationToken);
                using BudgetedParserList<SvgAuthoredPathCommand> commands = new(state.Budget, SvgMemoryCategory.Scene, workCounter, cancellationToken);
                if (!SvgOrthogonalGeometryLowering.AppendNativeEllipseCommands(commands, oval.CenterX, oval.CenterY, oval.RadiusX, oval.RadiusY))
                {
                    if (state.ClipPolygon is null)
                    {
                        if (state.ClipIsObjectBoundingBox) state.ClipPolygon = new([], []) { IsObjectBoundingBox = true };
                        else state.ClipBox = new(0, 0, 0, 0);
                    }
                    else AppendClipChild(new([], []) { IsObjectBoundingBox = state.ClipIsObjectBoundingBox }, state, workCounter, cancellationToken);
                }
                else
                {
                    string? nestedClip = SvgClipPathSyntax.ResolveClipId(reader, state);
                    nestedClip = nestedClip is null ? null : state.Strings.Copy(nestedClip);
                    AppendClipChild(CreateClipSource(commands.Detach(), childMap, childMap.IsAxisAligned, nestedClip, state, workCounter, cancellationToken),
                        state, workCounter, cancellationToken);
                }
                SvgStructuralAdmission.ConsumeLeafChildren(reader, state, workCounter, cancellationToken);
                return true;
            }
            state.HasUnsupportedContent = true;
            return true;
        }
        if ((inDefinitions || directChild || groupNestedDefinition) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "mask")
        {
            SvgElementAttributePolicy.ValidateMask(reader, workCounter, cancellationToken);
            SvgPaintableElementContext maskContext = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
            // A resource inherits presentation, while its content coordinates start
            // at the resource rather than at a rendered definition ancestor.
            maskContext = maskContext with { InheritedPresentation = maskContext.InheritedPresentation with { Transform = SvgAffineTransform.Identity, IsDisplayed = true } };
            InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
            SvgComputedStyle style = SvgComputedStyle.Resolve(reader, inline, maskContext.StyleSheet).ResolveOpacity(maskContext.InheritedPresentation.ElementOpacity);
            // The resource container supplies inherited style; its children paint
            // the mask without an extra opacity layer around the resource itself.
            SvgInheritedContainer inherited = SvgContainerInheritance.Resolve(reader, inline, style, maskContext.StyleSheet,
                maskContext.InheritedPresentation, maskContext.InheritedFillRule, maskContext.InheritedTextStyle,
                maskContext.InheritedStroke, namedColors, workCounter, cancellationToken);
            if (style.Fill is not null && inherited.Presentation.PaintServerFill is string fill)
            {
                workCounter.ConsumeText(fill.Length, cancellationToken);
                inherited = inherited with { Presentation = inherited.Presentation with { PaintServerFill = state.Strings.Copy(fill) } };
            }
            SvgGroupScope scope = new(reader.Depth, SvgGroupKind.Mask, inherited.Presentation, inherited.TextStyle,
                false, false, inherited.Stroke, inherited.FillRule)
            {
                RecordingScope = maskContext.RecordingScope,
                ClipRule = style.ClipRule ?? maskContext.InheritedClipRule
            };
            state.MaskId = reader.GetAttribute("id");
            state.MaskIsDisplayed = SvgPresentationResolver.ParseDisplay(style.Display);
            state.MaskIsAlpha = SvgMaskSyntax.ParseMaskType(reader.GetAttribute("mask-type"));
            state.MaskObjectBoundingBoxContent = reader.GetAttribute("maskContentUnits") switch
            {
                null or "userSpaceOnUse" => false,
                "objectBoundingBox" => true,
                _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse and objectBoundingBox mask content units are implemented.")
            };
            state.MaskValue = null;
            state.MaskContentBox = null;
            state.MaskFractionRegion = SvgMaskSyntax.ParseMaskFractionRegion(reader);
            state.MaskCustomRegion = state.MaskFractionRegion is null ? SvgMaskSyntax.ParseMaskRegion(reader, new(maskContext.LengthBases.Width ?? 300, maskContext.LengthBases.Height ?? 150)) : null;
            state.MaskDefaultUserSpaceRegion = reader.GetAttribute("maskUnits") == "userSpaceOnUse"
                && reader.GetAttribute("x") is null && reader.GetAttribute("y") is null && reader.GetAttribute("width") is null && reader.GetAttribute("height") is null;
            if (state.MaskDefaultUserSpaceRegion)
            {
                double width = maskContext.LengthBases.Width ?? 300;
                double height = maskContext.LengthBases.Height ?? 150;
                state.MaskCustomRegion = SvgMaskSyntax.MaskRegion(new(0, 0, width, height, default, false, null));
            }
            if (reader.IsEmptyElement)
            {
                if (state.MaskId is not null)
                {
                    if (state.MaskIsDisplayed) RegisterMask(state, workCounter, cancellationToken);
                    else SvgMaskSyntax.RegisterDisabled(state, workCounter, cancellationToken);
                }
                state.MaskId = null;
                state.MaskCustomRegion = null;
                state.MaskFractionRegion = null;
                state.MaskDefaultUserSpaceRegion = false;
                state.MaskObjectBoundingBoxContent = false;
            }
            else
            {
                // Resource children inherit the same bounded presentation scope as
                // ordinary containers, without creating a rendered group layer.
                state.MaskDepth = reader.Depth;
                state.Groups.Add(scope);
            }
            return true;
        }
        if (state.MaskDepth >= 0 && reader.Depth == state.MaskDepth + 1 && !resourceDefinition && reader.NamespaceURI == "http://www.w3.org/2000/svg")
        {
            if (SvgMaskContentReplay.Retain(reader, root, state, namedColors, workCounter, cancellationToken)) return true;
            if (state.MaskUse is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-element mask content is implemented.");
            if (reader.LocalName == "use")
            {
                if (state.MaskContentBuilder is not null || state.MaskValue is not null || state.MaskContentGeometries is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-element mask content is implemented.");
                state.MaskUse = SvgResourceUseReplay.Retain(reader, root, state, namedColors, workCounter, cancellationToken);
                return true;
            }
            if (state.MaskContentBuilder is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-element mask content is implemented.");
            if (SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName))
            {
                SvgPaintableElementContext maskContext = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
                if (SvgGradientSyntax.IsGradientFill(maskContext.ResolveStyle(reader).Fill))
                {
                    if (state.MaskValue is not null || state.MaskContentGeometries is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-element mask content is implemented.");
                    if (SvgLocalUrl.ReadReference(reader, "mask") is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Masks on mask content are not implemented.");
                    if (SvgLocalUrl.ReadReference(reader, "clip-path") is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths on mask content are not implemented.");
                    SvgDefinition lowered = LowerInstanceSource(reader, maskContext, state);
                    try
                    {
                        state.MaskContentBox = UnionMaskContentBox(lowered.Geometries, workCounter, cancellationToken);
                        SvgSceneBuilder builder = state.MaskContentBuilder = new(state.Budget, workCounter, cancellationToken);
                        SvgPaintRecording.AdoptLeafScene(lowered, builder, state);
                    }
                    catch { foreach (SvgGeometry geometry in lowered.Geometries) geometry.Dispose(); throw; }
                    SvgStructuralAdmission.ConsumeLeafChildren(reader, state, workCounter, cancellationToken);
                    return true;
                }
            }
            if (reader.LocalName == "rect")
            {
                if (state.MaskValue is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-rectangle masks are implemented.");
                SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken);
                SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
                SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader, context.LengthBases);
                SvgResolvedPaintableElement element = context.ResolveShape(reader, false);
                if (!element.Presentation.IsPainted) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Invisible mask content is not implemented.");
                state.MaskValue = SvgMaskSyntax.InterpretValue(element.Presentation.Color, state.MaskIsAlpha);
                state.MaskContentBox = element.Transform.Apply(new SvgClipBox(syntax.X, syntax.Y, syntax.Width, syntax.Height));
                SvgStructuralAdmission.ConsumeLeafChildren(reader, state, workCounter, cancellationToken);
                return true;
            }
            if (reader.LocalName == "circle")
            {
                if (state.MaskValue is not null || state.MaskContentGeometries is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-element mask content is implemented.");
                SvgElementAttributePolicy.ValidateCircle(reader, reader.LocalName, workCounter, cancellationToken);
                if (SvgLocalUrl.ReadReference(reader, "clip-path") is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths on mask content are not implemented.");
                if (SvgLocalUrl.ReadReference(reader, "mask") is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Masks on mask content are not implemented.");
                SvgPaintableElementContext contentContext = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
                using SvgParsedGeometry lowered = SvgLeafParser.ParseCircle(reader, contentContext, null, state);
                try
                {
                    (SvgGeometry[] contentGeometries, SvgColor contentPaint) = RetainPaintedMaskContent(lowered.Geometries, state, workCounter, cancellationToken);
                    state.MaskContentGeometries = contentGeometries;
                    state.MaskContentLuminance = contentPaint.Red;
                    state.MaskContentIsAlpha = state.MaskIsAlpha;
                    state.MaskContentBox = UnionMaskContentBox(state.MaskContentGeometries, workCounter, cancellationToken);
                }
                catch { lowered.Discard(); throw; }
                SvgStructuralAdmission.ConsumeLeafChildren(reader, state, workCounter, cancellationToken);
                return true;
            }
            state.HasUnsupportedContent = true;
            return true;
        }
        if ((state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 || directChild) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "pattern")
        {
            SvgElementAttributePolicy.ValidatePattern(reader, workCounter, cancellationToken);
            state.PatternId = reader.GetAttribute("id");
            state.PatternTemplate = SvgPatternSyntax.ParsePatternElement(reader, state, workCounter, cancellationToken);
            state.PatternTile = state.PatternTemplate.Grid;
            SvgPaintableElementContext patternContext = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
            InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
            SvgComputedStyle style = SvgComputedStyle.Resolve(reader, inline, patternContext.StyleSheet).ResolveOpacity(patternContext.InheritedPresentation.ElementOpacity);
            if (style.GetOpacity().Value != 1)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Opacity on pattern containers is not implemented.");
            SvgInheritedContainer inherited = SvgContainerInheritance.Resolve(reader, inline, style, patternContext.StyleSheet,
                patternContext.InheritedPresentation, patternContext.InheritedFillRule, patternContext.InheritedTextStyle,
                patternContext.InheritedStroke, namedColors, workCounter, cancellationToken);
            if (style.Fill is not null && inherited.Presentation.PaintServerFill is string fill)
            {
                workCounter.ConsumeText(fill.Length, cancellationToken);
                inherited = inherited with { Presentation = inherited.Presentation with { PaintServerFill = state.Strings.Copy(fill) } };
            }
            state.PatternScopeStart = state.PatternScopes?.Count ?? 0;
            state.PatternCommandStart = state.PendingCommandEnd;
            // Definition ancestors do not suppress a resource; its own display still controls its content and aliases.
            bool patternDisplayed = SvgPresentationResolver.ParseDisplay(style.Display) && state.PatternContents?.IsDisplayed(state.PatternId) != false;
            state.PatternTemplate = state.PatternTemplate with { IsDisplayed = patternDisplayed };
            state.Groups.Add(new(reader.Depth, SvgGroupKind.Pattern, inherited.Presentation with { Transform = SvgAffineTransform.Identity, IsDisplayed = patternDisplayed }, inherited.TextStyle, false, false, inherited.Stroke, inherited.FillRule)
                { RecordingScope = patternContext.RecordingScope, ClipRule = style.ClipRule ?? patternContext.InheritedClipRule });
            if (!reader.IsEmptyElement) state.PatternDepth = reader.Depth;
            else
            {
                state.Groups.RemoveLast();
                if (state.PatternId is not null) RegisterPattern(state, workCounter, cancellationToken);
                state.PatternId = null;
                state.PatternTile = null;
                state.PatternTemplate = default;
            }
            return true;
        }
        if (state.PatternDepth >= 0 && reader.Depth > state.PatternDepth && reader.NamespaceURI == "http://www.w3.org/2000/svg")
        {
            if (reader.LocalName == "use")
            {
                SvgPatternUseReplay.Retain(reader, root, state, namedColors,
                    GetPatternContentBuilder(state, workCounter, cancellationToken), workCounter, cancellationToken);
                return true;
            }
            if (reader.LocalName == "g")
            {
                SvgParsedGroup group = SvgGroupParser.Parse(reader, root, state, namedColors, workCounter, cancellationToken);
                if (!reader.IsEmptyElement || group.Scope.HasFilter)
                {
                    SvgSceneBuilder builder = GetPatternContentBuilder(state, workCounter, cancellationToken);
                    bool clip = group.Scope.ClipPathId is not null && !group.Scope.HasFilter;
                    bool mask = group.DeferredMask is not null;
                    bool isolate = !group.Scope.HasFilter && group.Scope.IsolatesOpacity || clip || mask;
                    int firstCommand = builder.CommandCount;
                    if (isolate) builder.BeginSourceOpacityScope(group.SourceOpacity, group.RecordingScope);
                    if (group.FilterId is {} filterId)
                    {
                        SvgFilterArena filters = builder.Filters;
                        filters.BorrowResourcesFrom(state.DocumentFilters);
                        int binding = filters.AddBinding(filterId, default, default, group.Scope.InheritedPresentation.Transform,
                            group.LengthBases, true, group.Scope.ClipPathId);
                        builder.BeginSourceOpacityScope(isolate ? SvgPaintOpacity.Opaque : group.SourceOpacity, group.Scope.RecordingScope);
                        int position = builder.CommandCount - 1;
                        builder.ReplaceCommand(position, builder.CommandAt(position) with { RegionKind = SvgLayerRegionKind.Filter, MaskContentIndex = binding });
                        state.FilterGroupDepth++;
                    }
                    if (clip || mask)
                    {
                        state.PatternScopes ??= new(state.Budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
                        state.PatternScopes.Add(new(group.Scope.ClipPathId, group.DeferredMask, group.Scope.InheritedPresentation.Transform, firstCommand, group.Scope.IsolatesOpacity));
                    }
                    state.Groups.Add(group.Scope with { Kind = SvgGroupKind.PatternGroup, IsolatesOpacity = isolate, FilterGeometryStart = builder.GeometryCount });
                    state.GroupDepth++;
                }
                return true;
            }
            if (reader.LocalName is "rect" or "circle" or "ellipse" or "path" or "polygon" or "polyline" or "line" or "image" or "text")
            {
                SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken).PrepareStyle(reader);
                SvgComputedStyle style = context.ResolveStyle(reader, reader.LocalName == "text");
                bool rendered = reader.LocalName is "image" or "text" || SvgLocalUrl.ReadReference(reader, "clip-path") is not null || SvgLocalUrl.ReadReference(reader, "mask") is not null || style.Filter is {} filter && filter.Trim() != "none";
                bool deferred = SvgDeferredPaintLeaves.NeedsReplay(reader, context, state);
                if (rendered || deferred)
                {
                    if (!SvgRenderedLeafParser.ValidateLeafAttributes(reader, workCounter, cancellationToken))
                        throw new InvalidOperationException("Pattern replay requires a supported leaf.");
                    RejectPatternContentPaint(style, state);
                    SvgPatternTemplate template = state.PatternTemplate;
                    if ((template.Width == 0 || template.Height == 0 || template.Href is null && (template.Width is null || template.Height is null))
                        && state.PatternContents?.RequiresContent(state.PatternId) != true)
                        context = context with { InheritedPresentation = context.InheritedPresentation with { IsDisplayed = false } };
                    SvgSceneBuilder builder = GetPatternContentBuilder(state, workCounter, cancellationToken);
                    if (state.DefinitionReplayScene is not null) throw new InvalidOperationException("Definition leaf replay cannot nest.");
                    double previousTextScale = state.OutputTextScale;
                    if (state.EffectiveFrame is {} textHost)
                        state.OutputTextScale *= Lokad.Svg.Fonts.SvgGlyphDeviceMetrics.GetScreenScale(
                            SvgAffineTransform.FromScaleOffset(textHost.ScaleX, textHost.ScaleY, 0, 0));
                    bool textContent = reader.LocalName == "text";
                    state.DefinitionReplayScene = builder;
                    try
                    {
                        if (deferred) SvgDeferredPaintLeaves.Retain(reader, context, state, rendered ? SvgDeferredPaintKind.Rendered : SvgDeferredPaintKind.PatternDefinition);
                        else _ = SvgRenderedLeafParser.ParseResolved(reader, context, root, state, limits, state.Budget, fontSnapshot, workCounter, cancellationToken);
                    }
                    finally { state.DefinitionReplayScene = null; state.OutputTextScale = previousTextScale; }
                    if (!textContent && !reader.IsEmptyElement)
                    {
                        SvgLeafSubtree subtree = SvgStructuralAdmission.ConsumeLeafSubtree(reader, limits, state.NodeCount, workCounter, cancellationToken);
                        state.NodeCount = subtree.NodeCount;
                        state.HasUnsupportedContent |= subtree.HasUnsupportedChildren;
                    }
                    return true;
                }
            }
            if (reader.LocalName == "rect" && KeepsLegacyRectanglePaint() && state.PatternTemplate.KeepsLegacyRectangle && state.PatternContentBuilder is null && state.PatternTile?.Content is null)
            {
                SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken);
                SvgPaintableElementContext tileContext = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
                tileContext = tileContext with { InheritedPresentation = tileContext.InheritedPresentation with { Transform = SvgAffineTransform.Identity } };
                // The tile payload retains a resolved rectangle, so its paint must be resolved before storing it.
                using SvgParsedGeometry parsedContent = SvgLeafParser.ParseRectangleCore(reader, tileContext, null, state);
                try
                {
                    SvgPatternTile tile = state.PatternTile ?? throw new InvalidOperationException("Pattern tile accumulation requires pattern dimensions.");
                    if (!CanKeepLegacyContent(parsedContent, tile))
                    {
                        foreach (SvgGeometry unused in parsedContent.Geometries) unused.Dispose();
                        SvgComputedStyle style = tileContext.ResolveStyle(reader);
                        RejectPatternContentPaint(style, state);
                        SvgSceneBuilder builder = GetPatternContentBuilder(state, workCounter, cancellationToken);
                        SvgPaintRecording.AdoptLeafScene(LowerInstanceSource(reader, tileContext, state), builder, state);
                        SvgStructuralAdmission.ConsumeLeafChildren(reader, state, workCounter, cancellationToken);
                        return true;
                    }
                    SvgGeometry validated = parsedContent.Geometries[0];
                    SvgRectangle shifted = validated.Rectangle with { X = validated.Rectangle.X + tile.ShiftX, Y = validated.Rectangle.Y + tile.ShiftY };
                    state.PatternTile = tile with { Content = SvgGeometry.FromRectangle(shifted) with { SolidPaint = validated.SolidPaint } };
                    // Preserve authored attributes for promotion; resolved rectangle bytes remain the
                    // legacy one-leaf fast path, while vector scenes need native opacity/stroke recording.
                    state.PatternRectangleSource = SvgDefinitionLeafSource.Capture(reader, tileContext, state);
                    state.PatternRectangleContext = tileContext;
                    SvgStructuralAdmission.ConsumeLeafChildren(reader, state, workCounter, cancellationToken);
                    return true;

                    static bool CanKeepLegacyContent(SvgParsedGeometry parsed, SvgPatternTile tile)
                    {
                        if (parsed.Geometries.Length != 1 || parsed.Geometries[0].Kind != SvgGeometryKind.Rectangle) return false;
                        SvgRectangle box = parsed.Geometries[0].Rectangle;
                        if (!box.HasFill || box.X != Math.Truncate(box.X) || box.Y != Math.Truncate(box.Y) || box.Width != Math.Truncate(box.Width) || box.Height != Math.Truncate(box.Height)) return false;
                        if (box.X < tile.OriginX || box.Y < tile.OriginY || box.X + box.Width > tile.OriginX + tile.TileWidth || box.Y + box.Height > tile.OriginY + tile.TileHeight) return false;
                        double x = box.X + tile.ShiftX, y = box.Y + tile.ShiftY;
                        if (x < tile.OriginX || y < tile.OriginY || x + box.Width > tile.OriginX + tile.TileWidth || y + box.Height > tile.OriginY + tile.TileHeight) return false;
                        return box.Stroke is not SvgStroke stroke || x - stroke.Width / 2d >= tile.OriginX && y - stroke.Width / 2d >= tile.OriginY
                            && x + box.Width + stroke.Width / 2d <= tile.OriginX + tile.TileWidth && y + box.Height + stroke.Width / 2d <= tile.OriginY + tile.TileHeight;
                    }
                }
                catch { parsedContent.Discard(); throw; }
            }
            if (reader.LocalName is "rect" or "circle" or "ellipse" or "path" or "polygon" or "polyline" or "line")
            {
                switch (reader.LocalName)
                {
                    case "rect": SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken); break;
                    case "circle": case "ellipse": SvgElementAttributePolicy.ValidateCircle(reader, reader.LocalName, workCounter, cancellationToken); break;
                    case "path": SvgElementAttributePolicy.ValidatePath(reader, workCounter, cancellationToken); break;
                    case "line": SvgElementAttributePolicy.ValidateLine(reader, workCounter, cancellationToken); break;
                    default: SvgElementAttributePolicy.ValidatePointList(reader, reader.LocalName, workCounter, cancellationToken); break;
                }
                SvgSceneBuilder builder = GetPatternContentBuilder(state, workCounter, cancellationToken);
                SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
                SvgComputedStyle style = context.ResolveStyle(reader);
                RejectPatternContentPaint(style, state);
                AdoptLeaf(LowerInstanceSource(reader, context, state));
                SvgStructuralAdmission.ConsumeLeafChildren(reader, state, workCounter, cancellationToken);
                return true;

                void AdoptLeaf(SvgDefinition lowered) => SvgPaintRecording.AdoptLeafScene(lowered, builder, state);
            }
            state.HasUnsupportedContent = true;
            return true;

            bool KeepsLegacyRectanglePaint()
            {
                SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
                SvgComputedStyle style = context.ResolveStyle(reader);
                bool hasFill = style.Fill is {} fill ? !fill.Trim().Equals("none", StringComparison.Ordinal) : context.InheritedPresentation.Paint.HasFill;
                bool hasStroke = (style.Stroke ?? context.InheritedStroke.Stroke) is {} stroke && !stroke.Trim().Equals("none", StringComparison.Ordinal);
                // Independent fill and stroke need the ordinary recorded element
                // layer. Keep qualified single-paint legacy pixels unchanged.
                return !style.PaintOrder.GetValueOrDefault().StrokeFirst()
                    && (!hasFill || !hasStroke || style.GetOpacity().Value == 1);
            }
        }
        if ((state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 || directChild || resourceDefinition) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "radialGradient")
        {
            SvgElementAttributePolicy.ValidateRadialGradient(reader, workCounter, cancellationToken);
            state.RadialGradientId = reader.GetAttribute("id");
            if (state.RadialGradientId is {} radialId) state.RadialGradientId = ClaimGradientId(state, radialId, workCounter, cancellationToken);
            SvgRadialTemplate template = SvgGradientSyntax.ParseRadialVector(reader, workCounter, cancellationToken);
            state.RadialGradient = template;
            if (!reader.IsEmptyElement) state.RadialGradientDepth = reader.Depth;
            else if (state.RadialGradientId is not null) RegisterRadialGradient(state, workCounter, cancellationToken);
            state.GradientStops.Clear();
            return true;
        }
        if ((state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 || directChild || resourceDefinition) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "linearGradient")
        {
            SvgElementAttributePolicy.ValidateLinearGradient(reader, workCounter, cancellationToken);
            state.GradientId = reader.GetAttribute("id");
            if (state.GradientId is {} linearId) state.GradientId = ClaimGradientId(state, linearId, workCounter, cancellationToken);
            SvgGradientTemplate template = SvgGradientSyntax.ParseLinearVector(reader, workCounter, cancellationToken);
            state.Gradient = template;
            if (!reader.IsEmptyElement) state.GradientDepth = reader.Depth;
            else if (state.GradientId is not null) RegisterGradient(state, workCounter, cancellationToken);
            state.GradientStops.Clear();
            return true;
        }
        if (((state.GradientDepth >= 0 && reader.Depth == state.GradientDepth + 1) || (state.RadialGradientDepth >= 0 && reader.Depth == state.RadialGradientDepth + 1)) && reader.NamespaceURI == "http://www.w3.org/2000/svg")
        {
            if (reader.LocalName == "stop")
            {
                if (state.GradientStops.Count >= SvgGradientSyntax.MaximumGradientStops) throw new SvgException(SvgErrorCode.LimitExceeded, "Gradient stop count exceeds the supported limit.");
                SvgElementAttributePolicy.ValidateGradientStop(reader, workCounter, cancellationToken);
                state.GradientStops.Add(SvgGradientSyntax.ParseStop(reader, namedColors, workCounter, cancellationToken));
                SvgStructuralAdmission.ConsumeLeafChildren(reader, state, workCounter, cancellationToken);
                return true;
            }
            state.HasUnsupportedContent = true;
            return true;
        }
        if (state.DefinitionsDepth < 0 && (directChild || state.GroupDepth > 0 || state.Viewports.Count > 0) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsDefinitionsContainer(reader.LocalName))
        {
            SvgElementAttributePolicy.ValidateDefinitionContainer(reader, workCounter, cancellationToken);
            SvgPaintableElementContext definitionContext = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
            InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
            SvgComputedStyle style = SvgComputedStyle.Resolve(reader, inline, definitionContext.StyleSheet);
            SvgInheritedContainer inherited = SvgContainerInheritance.Resolve(reader, inline, style, definitionContext.StyleSheet, definitionContext.InheritedPresentation,
                definitionContext.InheritedFillRule, definitionContext.InheritedTextStyle, definitionContext.InheritedStroke, namedColors, workCounter, cancellationToken);
            if (!reader.IsEmptyElement) state.DefinitionInheritance = inherited;
            if (!reader.IsEmptyElement) state.DefinitionsDepth = reader.Depth;
            return true;
        }
        if (state.DefinitionsDepth < 0 && (directChild || isEligible) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "use")
        {
            SvgElementAttributePolicy.ValidateUse(reader, workCounter, cancellationToken);
            SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, root, state, namedColors, workCounter, cancellationToken);
            InlineStyle inline = SvgInlineStyleParser.ParseUse(reader.GetAttribute("style"), workCounter, cancellationToken);
            context = context with { AuthoredStyle = SvgComputedStyle.Resolve(reader, inline, context.StyleSheet) };
            SvgViewportFrame? parentFrame = state.EffectiveFrame;
            double? baseWidth = parentFrame?.ViewportWidth ?? root.UserViewportWidth;
            double? baseHeight = parentFrame?.ViewportHeight ?? root.UserViewportHeight;
            if (SvgLocalReferenceParser.ParseUse(reader, context, inline, state.Strings, baseWidth, baseHeight, state) is not {} reference)
            {
                SvgStructuralAdmission.ConsumeLeafChildren(reader, state, workCounter, cancellationToken);
                return true;
            }
            SvgRenderedGroupSourceParser.CaptureUse(reader, context, reference, state);
            SvgDeferredClip? deferredClip = ResolveDeferredClip(reader, reference, state);
            SvgViewportFrame? useFrame;
            if (deferredClip is null)
            {
                useFrame = ResolveUseClipFrame(reader, reference, state);
            }
            else useFrame = deferredClip.Value.Parent;
            string? maskAttribute = SvgLocalUrl.ReadReference(reader, "mask");
            SvgReferenceResolver.ResolveOrDefer(reference, state.Definitions, state.PendingUses, state.Scene, useFrame, ResolveDeferredMaskValue(reader, state), maskAttribute is null ? null : state.Strings.Copy(maskAttribute), deferredClip, state, workCounter, cancellationToken);
            SvgStructuralAdmission.ConsumeLeafChildren(reader, state, workCounter, cancellationToken);
            return true;
        }
        return false;

        static string? CopyStyleValue(string? value, SvgDocumentState document) => value is null ? null : document.Strings.Copy(value);

        static SvgDeferredClip? ResolveDeferredClip(XmlReader useReader, SvgUseReference useReference, SvgDocumentState document)
        {
            string? clipAttribute = SvgLocalUrl.ReadReference(useReader, "clip-path");
            if (clipAttribute is null) return null;
            string clipId = SvgClipPathSyntax.ParseClipId(clipAttribute);
            if (SvgClipPathSyntax.IsDisabled(clipId, document)) return null;
            bool clipKnown = document.Clips.TryGetValue(clipId, out _) || document.ObjectClips.TryGetValue(clipId, out _);
            bool definitionKnown = document.Definitions.TryGetValue(useReference.Id, out _);
            if (document.Clips.TryGetValue(clipId, out _) && useReference.Transform != SvgAffineTransform.Identity)
                return new SvgDeferredClip(document.Strings.Copy(clipId), document.EffectiveFrame, useReader.Depth);
            if (definitionKnown && clipKnown) return null;
            if (!clipKnown) return new SvgDeferredClip(document.Strings.Copy(clipId), document.EffectiveFrame, useReader.Depth);
            if (document.ObjectClips.TryGetValue(clipId, out _)) return new SvgDeferredClip(document.Strings.Copy(clipId), document.EffectiveFrame, useReader.Depth);
            return null;
        }

        static byte? ResolveDeferredMaskValue(XmlReader useReader, SvgDocumentState document)
        {
            string? maskAttribute = SvgLocalUrl.ReadReference(useReader, "mask");
            if (maskAttribute is null) return null;
            if (document.Masks.TryGetValue(SvgMaskSyntax.ParseMaskId(maskAttribute), out byte value)) return value;
            return null;
        }

        static SvgViewportFrame? ResolveUseClipFrame(XmlReader useReader, SvgUseReference useReference, SvgDocumentState document)
        {
            string? clipAttribute = SvgLocalUrl.ReadReference(useReader, "clip-path");
            if (clipAttribute is null) return SvgClipPathSyntax.ResolveLeafClip(useReader, document);
            if (!document.ObjectClips.TryGetValue(SvgClipPathSyntax.ParseClipId(clipAttribute), out _)) return SvgClipPathSyntax.ResolveLeafClip(useReader, document);
            if (!document.Definitions.TryGetValue(useReference.Id, out SvgDefinition definition)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "objectBoundingBox clip paths on forward use references are not implemented.");
            if (definition.SymbolViewBox is SvgViewBox symbolBox) return ResolveSymbolUseClipFrame(useReader, useReference, document, definition, symbolBox);
            double left = double.PositiveInfinity;
            double top = double.PositiveInfinity;
            double right = double.NegativeInfinity;
            double bottom = double.NegativeInfinity;
            foreach (SvgGeometry source in definition.Geometries)
            {
                SvgRectangle box = SvgMaskSyntax.BoundingBox(source);
                SvgRectangle placed = useReference.Transform.Apply(new SvgRectangle(box.X + useReference.X, box.Y + useReference.Y, box.Width, box.Height, default, true, null));
                left = Math.Min(left, placed.X);
                top = Math.Min(top, placed.Y);
                right = Math.Max(right, placed.X + placed.Width);
                bottom = Math.Max(bottom, placed.Y + placed.Height);
            }
            SvgRectangle instanceBox = new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), default, true, null);
            return SvgClipPathSyntax.ResolveLeafClipOrPlacement(useReader, document, document.EffectiveFrame, instanceBox);
        }

        static SvgViewportFrame? ResolveSymbolUseClipFrame(XmlReader useReader, SvgUseReference useReference, SvgDocumentState document, SvgDefinition definition, SvgViewBox symbolBox)
        {
            if (useReference.Width is null || useReference.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            double left = double.PositiveInfinity;
            double top = double.PositiveInfinity;
            double right = double.NegativeInfinity;
            double bottom = double.NegativeInfinity;
            foreach (SvgGeometry source in definition.Geometries)
            {
                SvgRectangle box = SvgMaskSyntax.BoundingBox(source);
                SvgRectangle mapped = SvgReferenceResolver.MapSymbolBox(box, symbolBox, useReference.Width.Value, useReference.Height.Value);
                SvgRectangle placed = useReference.Transform.Apply(new SvgRectangle(mapped.X + useReference.X, mapped.Y + useReference.Y, mapped.Width, mapped.Height, default, true, null));
                left = Math.Min(left, placed.X);
                top = Math.Min(top, placed.Y);
                right = Math.Max(right, placed.X + placed.Width);
                bottom = Math.Max(bottom, placed.Y + placed.Height);
            }
            SvgRectangle instanceBox = new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), default, true, null);
            SvgClipBox viewport = useReference.Transform.Apply(new SvgClipBox(useReference.X, useReference.Y, useReference.Width.Value, useReference.Height.Value));
            return SvgClipPathSyntax.ResolveLeafClipOrPlacement(useReader, document, document.EffectiveFrame, viewport.ClipRectangle(instanceBox));
        }

        static SvgClipBox UnionMaskContentBox(SvgGeometry[] geometries, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            double left = double.PositiveInfinity;
            double top = double.PositiveInfinity;
            double right = double.NegativeInfinity;
            double bottom = double.NegativeInfinity;
            foreach (SvgGeometry geometry in geometries)
            {
                workCounter.Consume(cancellationToken);
                SvgRectangle box = SvgMaskSyntax.BoundingBox(geometry);
                if (box.X < left) left = box.X;
                if (box.Y < top) top = box.Y;
                if (box.X + box.Width > right) right = box.X + box.Width;
                if (box.Y + box.Height > bottom) bottom = box.Y + box.Height;
            }
            return new SvgClipBox(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }

        static (SvgGeometry[] Geometries, SvgColor Paint) RetainPaintedMaskContent(ReadOnlySpan<SvgGeometry> geometries, SvgDocumentState maskState, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(maskState);
            ArgumentNullException.ThrowIfNull(workCounter);
            using BudgetedParserList<int> kept = new(maskState.Budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            SvgColor paint = default;
            bool hasPaint = false;
            for (int index = 0; index < geometries.Length; index++)
            {
                workCounter.Consume(cancellationToken);
                if (geometries[index].Kind != SvgGeometryKind.PolygonFill) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only solid mask content is implemented.");
                if (!geometries[index].Polygon.HasFill)
                {
                    // The mask adopts only painted owners; discarded fill storage
                    // must release even when the source also contributes a stroke.
                    geometries[index].Dispose();
                    continue;
                }
                SvgColor color = geometries[index].Polygon.Color;
                if (color.Red != color.Green || color.Red != color.Blue) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only grayscale luminance mask content is implemented.");
                if (!hasPaint) paint = color;
                else if (!paint.Equals(color)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-paint mask content is implemented.");
                hasPaint = true;
                kept.Add(index);
            }
            if (!hasPaint) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Invisible mask content is not implemented.");
            maskState.DefinitionReservations.Add(maskState.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(kept.Count), SvgMemoryCategory.Scene));
            SvgGeometry[] retained = new SvgGeometry[kept.Count];
            for (int index = 0; index < kept.Count; index++)
            {
                workCounter.Consume(cancellationToken);
                retained[index] = geometries[kept[index]];
            }
            return (retained, paint);
        }

        // Keep single rectangle resources on their established box route. Only
        // a sibling materializes native commands, retaining the child's own map.
        static void PromoteClipRectangle(SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
        {
            if (state.ClipBox is null) return;
            if (state.ClipPolygon is null && state.ClipRectangleSource is {} source)
                state.ClipPolygon = CreateClipRectangle(source, state, work, token);
            state.ClipRectangleSource = null;
            state.ClipBox = null;
        }

        static SvgClipPolygon CreateClipRectangle(SvgAffineViewportClip source, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
        {
            SvgClipBox box = source.Box;
            ReadOnlySpan<SvgDevicePoint> corners = stackalloc SvgDevicePoint[]
            {
                new((float)box.X, (float)box.Y), new((float)box.Right, (float)box.Y),
                new((float)box.Right, (float)box.Bottom), new((float)box.X, (float)box.Bottom)
            };
            state.DefinitionReservations.Add(state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(4)
                + ParserCollectionMemory.GetArrayByteCount<int>(1), SvgMemoryCategory.Scene));
            SvgDevicePoint[] points = new SvgDevicePoint[4];
            int[] ends = [4];
            using BudgetedParserList<SvgAuthoredPathCommand> commands = new(state.Budget, SvgMemoryCategory.Scene, work, token);
            for (int index = 0; index < corners.Length; index++)
            {
                work.Consume(token);
                SvgDevicePoint point = corners[index];
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Native clip rectangle coordinates exceed the finite float range.");
                points[index] = source.Map.Apply(point.X, point.Y);
                commands.Add(new(index == 0 ? SvgAuthoredPathVerb.Move : SvgAuthoredPathVerb.Line,
                    SvgAuthoredArcFlags.None, point, default, default));
            }
            commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
            OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
            SvgAuthoredPath path;
            try { path = SvgAuthoredPath.Create(owned, source.Map, state.Budget); }
            catch { owned.Dispose(); throw; }
            return new(points, ends) { SourcePath = path, IsObjectBoundingBox = state.ClipIsObjectBoundingBox };
        }

        // Rounded rectangles and ovals share one native source/staging owner.
        // Resource units and nested intersections bind later in the final scene.
        static SvgClipPolygon CreateClipSource(OwnedParserArray<SvgAuthoredPathCommand> owned, SvgAffineTransform map,
            bool oval, string? nestedClip, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
        {
            SvgAuthoredPath source;
            try { source = SvgAuthoredPath.CreateConvexShape(owned, map, state.Budget); }
            catch { owned.Dispose(); throw; }
            try
            {
                using SvgPreparedPathContours prepared = source.Prepare(SvgAffineTransform.Identity, false, state.Budget, work, token);
                state.DefinitionReservations.Add(state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(prepared.Points.Length)
                    + ParserCollectionMemory.GetArrayByteCount<int>(prepared.ContourEnds.Length), SvgMemoryCategory.Scene));
                SvgDevicePoint[] points = new SvgDevicePoint[prepared.Points.Length];
                int[] ends = new int[prepared.ContourEnds.Length];
                for (int index = 0; index < points.Length; index++) { work.Consume(token); points[index] = prepared.Points[index]; }
                for (int index = 0; index < ends.Length; index++) { work.Consume(token); ends[index] = prepared.ContourEnds[index]; }
                return new(points, ends) { SourcePath = source, UsesNativeReplay = true, IsOval = oval,
                    IsObjectBoundingBox = state.ClipIsObjectBoundingBox, NestedClipId = nestedClip };
            }
            catch { source.Dispose(); throw; }
        }

        // Adopt each source once; union failure retains the first staging owner
        // and releases the incoming owner. Publish before cancellable classification.
        static void AppendClipChild(SvgClipPolygon child, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
        {
            if (child.Points.Length == 0 && state.ClipPolygon is {} painted)
            {
                child.SourcePath?.Dispose();
                if (painted.NestedClipId is null && !painted.UsesNativeReplay && painted.ContourEnds.Length == 1 && TryGetAxisClipBox(painted.Points, work, token, out SvgClipBox box))
                    state.ClipBox = box;
                return;
            }
            if (state.ClipPolygon is { Points.Length: 0 } empty)
            {
                empty.SourcePath?.Dispose(); state.ClipPolygon = null; state.ClipBox = null;
            }
            if (state.ClipPolygon is {} first)
            {
                SvgClipPolygon combined;
                try { combined = CombineClipChildren(first, child, state, work, token); }
                finally { child.SourcePath?.Dispose(); }
                state.ClipPolygon = combined;
                first.SourcePath?.Dispose();
                child = combined;
            }
            else state.ClipPolygon = child;
            state.ClipBox = child.Points.Length == 0 ? new(0, 0, 0, 0)
                : child.NestedClipId is null && !child.UsesNativeReplay && child.ContourEnds.Length == 1 && TryGetAxisClipBox(child.Points, work, token, out SvgClipBox clipRect) ? clipRect : null;
        }

        // Borrow both input owners, bake each native child map once, then retain a
        // disjoint straight union with an identity source map. Every copy and
        // separating-axis/convexity visit is budgeted and cancellable; temporary
        // commands release on failure and only success transfers the new owner.
        static SvgClipPolygon CombineClipChildren(SvgClipPolygon first, SvgClipPolygon next, SvgDocumentState state,
            ParserWorkCounter work, CancellationToken token)
        {
            if (first.NestedClipId is not null || next.NestedClipId is not null)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Nested clips on union children are not implemented.");
            SvgAuthoredPath nextPath = next.SourcePath ?? throw new InvalidOperationException("Clip children require retained source commands.");
            BudgetedParserList<SvgClipChild> originals = state.ClipChildren ??= new(state.Budget, SvgMemoryCategory.Scene, work, token);
            SvgClipChildRange range;
            if (first.Children is {} sourceRange) range = sourceRange;
            else
            {
                range = new(originals.Count, 1);
                Retain(first.SourcePath ?? throw new InvalidOperationException("Clip children require retained source commands."), first.IsEvenOdd, originals, state.Budget, work, token);
            }
            if (range.First + range.Count != originals.Count) throw new InvalidOperationException("Clip child staging must remain contiguous.");
            Retain(nextPath, next.IsEvenOdd, originals, state.Budget, work, token);
            range = range with { Count = range.Count + 1 };
            if (first.ChildCount >= 42)
            {
                SvgPaintBounds a = first.ResourceBounds ?? throw new InvalidOperationException("A clip union requires source bounds.");
                SvgPaintBounds b = ChildBounds(next, nextPath, work, token);
                return first with { SourcePath = null, Children = range, UsesMaskFallback = true, IsResolvedUnion = true,
                    ChildCount = first.ChildCount + 1, IsOval = false,
                    ResourceBounds = new(MathF.Min(a.Left, b.Left), MathF.Min(a.Top, b.Top), MathF.Max(a.Right, b.Right), MathF.Max(a.Bottom, b.Bottom)) };
            }
            SvgAuthoredPath firstPath = first.SourcePath ?? throw new InvalidOperationException("Clip children require retained source commands.");
            if ((first.UsesNativeReplay && first.ChildCount == 1) || next.UsesNativeReplay
                || (first.IsEvenOdd && first.ChildCount == 1) || next.IsEvenOdd || next.ContourEnds.Length != 1
                || (first.ChildCount == 1 && first.ContourEnds.Length != 1) || first.IsResolvedUnion)
            {
                return ResolveNative(first, next, firstPath, nextPath, range, state, work, token);
            }
            using SvgPreparedPathContours left = firstPath.Prepare(SvgAffineTransform.Identity, false, state.Budget, work, token);
            using SvgPreparedPathContours right = nextPath.Prepare(SvgAffineTransform.Identity, false, state.Budget, work, token);
            if (!IsConvex(right) || first.ChildCount == 1 && !IsConvex(left))
                return ResolveNative(first, next, firstPath, nextPath, range, state, work, token);
            bool needsWindingUnion = first.ChildCount > 1 && !first.IsEvenOdd;
            for (int contour = 0; contour < left.ContourEnds.Length; contour++)
            {
                int start = contour == 0 ? 0 : left.ContourEnds[contour - 1];
                ReadOnlySpan<SvgDevicePoint> ring = left.Points.AsSpan(start, left.ContourEnds[contour] - start);
                if (!Separated(ring, right.Points) && !Separated(right.Points, ring))
                    needsWindingUnion = true;
            }
            int pointCount = checked(left.Points.Length + right.Points.Length);
            int contourCount = checked(left.ContourEnds.Length + right.ContourEnds.Length);
            RenderBudget.BudgetReservation retained = state.Budget.Reserve(
                ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCount)
                + ParserCollectionMemory.GetArrayByteCount<int>(contourCount), SvgMemoryCategory.Scene);
            state.DefinitionReservations.Add(retained);
            SvgDevicePoint[] points = new SvgDevicePoint[pointCount];
            int[] ends = new int[contourCount];
            using BudgetedParserList<SvgAuthoredPathCommand> commands = new(state.Budget, SvgMemoryCategory.Scene, work, token);
            Append(left, 0, 0);
            Append(right, left.Points.Length, left.ContourEnds.Length);
            SvgPaintBounds firstBounds = ChildBounds(first, firstPath, work, token), nextBounds = ChildBounds(next, nextPath, work, token);
            SvgPaintBounds resourceBounds = new(MathF.Min(firstBounds.Left, nextBounds.Left), MathF.Min(firstBounds.Top, nextBounds.Top),
                MathF.Max(firstBounds.Right, nextBounds.Right), MathF.Max(firstBounds.Bottom, nextBounds.Bottom));
            OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
            SvgAuthoredPath combined;
            try { combined = SvgAuthoredPath.Create(owned, SvgAffineTransform.Identity, state.Budget); }
            catch { owned.Dispose(); throw; }
            return new(points, ends) { SourcePath = combined, UsesNativeReplay = true, ChildCount = first.ChildCount + 1, ResourceBounds = resourceBounds, IsEvenOdd = !needsWindingUnion, IsObjectBoundingBox = first.IsObjectBoundingBox, Children = range };

            static SvgClipPolygon ResolveNative(SvgClipPolygon first, SvgClipPolygon next, SvgAuthoredPath firstPath, SvgAuthoredPath nextPath,
                SvgClipChildRange range, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
            {
                SvgAuthoredPath united = SvgClipUnion.ResolveNativeUnion(firstPath, first.IsEvenOdd, nextPath, next.IsEvenOdd, state.Budget, work, token);
                try
                {
                    using SvgPreparedPathContours prepared = united.Prepare(SvgAffineTransform.Identity, false, state.Budget, work, token);
                    state.DefinitionReservations.Add(state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(prepared.Points.Length)
                        + ParserCollectionMemory.GetArrayByteCount<int>(prepared.ContourEnds.Length), SvgMemoryCategory.Scene));
                    SvgDevicePoint[] retainedPoints = new SvgDevicePoint[prepared.Points.Length];
                    int[] retainedEnds = new int[prepared.ContourEnds.Length];
                    for (int index = 0; index < retainedPoints.Length; index++) { work.Consume(token); retainedPoints[index] = prepared.Points[index]; }
                    for (int index = 0; index < retainedEnds.Length; index++) { work.Consume(token); retainedEnds[index] = prepared.ContourEnds[index]; }
                    SvgPaintBounds a = ChildBounds(first, firstPath, work, token), b = ChildBounds(next, nextPath, work, token);
                    return new(retainedPoints, retainedEnds) { SourcePath = united, UsesNativeReplay = true, IsResolvedUnion = true, Children = range,
                        ChildCount = first.ChildCount + 1, IsObjectBoundingBox = first.IsObjectBoundingBox,
                        ResourceBounds = new(MathF.Min(a.Left, b.Left), MathF.Min(a.Top, b.Top), MathF.Max(a.Right, b.Right), MathF.Max(a.Bottom, b.Bottom)) };
                }
                catch { united.Dispose(); throw; }
            }

            static void Retain(SvgAuthoredPath path, bool isEvenOdd, BudgetedParserList<SvgClipChild> originals,
                RenderBudget budget, ParserWorkCounter work, CancellationToken token)
            {
                SvgAuthoredPath copy = path.Copy(SvgAffineTransform.Identity, budget, work, token);
                try { originals.Add(new(copy, isEvenOdd)); }
                catch { copy.Dispose(); throw; }
            }

            static SvgPaintBounds ChildBounds(SvgClipPolygon polygon, SvgAuthoredPath path, ParserWorkCounter work, CancellationToken token)
            {
                if (polygon.ResourceBounds is {} bounds) return bounds;
                SvgRectangle tight = SvgPathBounds.Compute(path.Commands, work, token);
                return new SvgPaintBounds((float)tight.X, (float)tight.Y, (float)(tight.X + tight.Width), (float)(tight.Y + tight.Height)).Map(path.SceneMap, work, token);
            }

            bool IsConvex(SvgPreparedPathContours path)
            {
                if (path.ContourEnds.Length != 1 || path.Points.Length < 3)
                    return false;
                long pairs = checked((long)path.Points.Length * path.Points.Length);
                for (long visit = 0; visit < pairs; visit++) work.Consume(token);
                return SvgRawContourConvexity.IsConvex(path.Points, [], [], token);
            }

            bool Separated(ReadOnlySpan<SvgDevicePoint> axisRing, ReadOnlySpan<SvgDevicePoint> other)
            {
                double area = 0;
                for (int index = 0; index < axisRing.Length; index++)
                {
                    work.Consume(token);
                    SvgDevicePoint a = axisRing[index], b = axisRing[(index + 1) % axisRing.Length];
                    area += a.X * b.Y - b.X * a.Y;
                }
                if (area == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Degenerate clip children are not implemented.");
                for (int edge = 0; edge < axisRing.Length; edge++)
                {
                    SvgDevicePoint a = axisRing[edge], b = axisRing[(edge + 1) % axisRing.Length];
                    if (a == b) continue;
                    bool outside = true;
                    foreach (SvgDevicePoint p in other)
                    {
                        work.Consume(token);
                        double cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
                        if ((area > 0 && cross > 0) || (area < 0 && cross < 0)) { outside = false; break; }
                    }
                    if (outside) return true;
                }
                return false;
            }

            void Append(SvgPreparedPathContours path, int pointOffset, int contourOffset)
            {
                int start = 0;
                for (int contour = 0; contour < path.ContourEnds.Length; contour++)
                {
                    int end = path.ContourEnds[contour];
                    double area = 0;
                    for (int index = start; index < end; index++)
                    {
                        work.Consume(token);
                        SvgDevicePoint a = path.Points[index], b = path.Points[index + 1 < end ? index + 1 : start];
                        area += a.X * b.Y - b.X * a.Y;
                    }
                    // The native union builder fixes each simplified child to CCW
                    // by reversing from its last stored endpoint. An explicit
                    // closing line and an implicit close have different starts.
                    for (int index = start; index < end; index++)
                    {
                        work.Consume(token);
                        SvgDevicePoint point = path.Points[area > 0 ? end - 1 - (index - start) : index];
                        points[pointOffset + index] = point;
                        if (index == start || (point != points[pointOffset + index - 1]
                            && (index + 1 < end || point != points[pointOffset + start])))
                            commands.Add(new(index == start ? SvgAuthoredPathVerb.Move : SvgAuthoredPathVerb.Line,
                                SvgAuthoredArcFlags.None, point, default, default));
                    }
                    // Native union writes the closing endpoint before Close.
                    // Its point count controls scanline density dispatch, even
                    // though the implicit closing edge has the same geometry.
                    commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, points[pointOffset + start], default, default));
                    commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
                    ends[contourOffset + contour] = pointOffset + end;
                    start = end;
                }
            }
        }

        static void RetainClipSource(XmlReader reader, SvgPaintableElementContext context, SvgComputedStyle style, SvgDocumentState state)
        {
            if (reader.GetAttribute("id") is not string id) return;
            if (state.Definitions.TryGetValue(id, out _) || state.GroupSnapshots.TryGetValue(id, out _) || state.RenderedGroupDefinitions.TryGetValue(id, out _))
                throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
            // Ordinary uses replay authored paint/stroke independently of stroke-free clip coverage.
            state.Definitions.TryAdd(state.Strings.Copy(id), new([], false, null, null)
            {
                LeafSource = SvgDefinitionLeafSource.CaptureUnconstructed(reader, context with { AuthoredStyle = style }, state),
                SourceDisplayed = SvgPresentationResolver.ParseDisplay(style.Display),
                SourceVisible = style.Visibility is null ? null : SvgPresentationResolver.ParseVisibility(style.Visibility, true)
            });
        }

        static SvgClipPolygon ParseClipPathPolygon(string? pathData, SvgAffineTransform clipTransform, bool isEvenOdd, bool visible, int maximumCommands, SvgDocumentState clipState, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(clipState);
            ArgumentNullException.ThrowIfNull(workCounter);
            if (string.IsNullOrWhiteSpace(pathData)) return new([], []);
            (int letters, bool hasCurve) = ScanClipPathData(pathData, workCounter, cancellationToken);
            using SvgRenderablePathData renderData = SvgRenderablePathData.Read(pathData, maximumCommands, clipState.Budget, workCounter, cancellationToken);
            pathData = renderData.Value;
            if (pathData.Length == 0) return new([], []);
            int pointCapacity = (int)Math.Min(hasCurve ? (long)letters * (SvgOrthogonalGeometryLowering.CurveSegmentsPerCommand + 1) : pathData.Length / 2L + 2, SvgOrthogonalGeometryLowering.MaximumFlattenedContourPoints);
            int contourCapacity = Math.Min(letters + 1, pointCapacity);
            long scratchBytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity) * (hasCurve ? 2 : 1) + ((hasCurve ? 4 : 2) * ParserCollectionMemory.GetArrayByteCount<int>(contourCapacity)));
            using RenderBudget.BudgetReservation scratch = clipState.Budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
            SvgContourBuilder builder = hasCurve ? SvgContourBuilder.CreateGrowing(pointCapacity, contourCapacity) : new(pointCapacity, contourCapacity);
            SvgFlattenPolicy flattenPolicy = new(SvgOrthogonalGeometryLowering.FlattenFlatness, SvgOrthogonalGeometryLowering.FlattenMaxDepth, SvgOrthogonalGeometryLowering.CurveSegmentsPerCommand);
            using BudgetedParserList<SvgAuthoredPathCommand> authored = new(clipState.Budget, SvgMemoryCategory.Scene, workCounter, cancellationToken);
            SvgPathDataParser.ParsePath(pathData, clipTransform, builder, flattenPolicy, letters, workCounter, cancellationToken, null, authored);
            if (!visible) return new([], []);
            using BudgetedParserList<SvgDevicePoint> stripped = new(clipState.Budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            using BudgetedParserList<int> strippedEnds = new(clipState.Budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            for (int contour = 0; contour < builder.ContourCount; contour++)
            {
                workCounter.Consume(cancellationToken);
                ReadOnlySpan<SvgDevicePoint> ring = builder.GetContour(contour);
                using BudgetedParserList<SvgDevicePoint> cleaned = new(clipState.Budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
                SvgDevicePoint first = default;
                SvgDevicePoint previous = default;
                bool hasKept = false;
                for (int index = 0; index < ring.Length; index++)
                {
                    workCounter.Consume(cancellationToken);
                    SvgDevicePoint vertex = ring[index];
                    if (hasKept && previous == vertex) continue;
                    if (hasKept && vertex == first && index == ring.Length - 1) continue;
                    if (!hasKept) first = vertex;
                    previous = vertex;
                    hasKept = true;
                    cleaned.Add(vertex);
                }
                if (cleaned.Count < 3) continue;
                for (int index = 0; index < cleaned.Count; index++)
                {
                    workCounter.Consume(cancellationToken);
                    stripped.Add(cleaned[index]);
                }
                strippedEnds.Add(stripped.Count);
            }
            if (stripped.Count == 0) return new([], []);
            clipState.DefinitionReservations.Add(clipState.Budget.Reserve(checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(stripped.Count) + ParserCollectionMemory.GetArrayByteCount<int>(strippedEnds.Count)), SvgMemoryCategory.Scene));
            SvgDevicePoint[] retainedPoints = new SvgDevicePoint[stripped.Count];
            int[] retainedEnds = new int[strippedEnds.Count];
            for (int index = 0; index < stripped.Count; index++)
            {
                workCounter.Consume(cancellationToken);
                retainedPoints[index] = stripped[index];
            }
            for (int index = 0; index < strippedEnds.Count; index++)
            {
                workCounter.Consume(cancellationToken);
                retainedEnds[index] = strippedEnds[index];
            }
            OwnedParserArray<SvgAuthoredPathCommand> owned = authored.Detach();
            SvgAuthoredPath source;
            try { source = SvgAuthoredPath.Create(owned, clipTransform, clipState.Budget); }
            catch { owned.Dispose(); throw; }
            return new SvgClipPolygon(retainedPoints, retainedEnds) { SourcePath = source, IsEvenOdd = isEvenOdd, UsesNativeReplay = hasCurve || builder.ContourCount > 1 };
        }

        static (int Letters, bool HasCurve) ScanClipPathData(string pathData, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            int letters = 0;
            bool hasCurve = false;
            for (int index = 0; index < pathData.Length; index++)
            {
                if ((index & 4095) == 0) workCounter.Consume(cancellationToken);
                char current = pathData[index];
                if (current is 'e' or 'E')
                {
                    if (index > 0 && (char.IsDigit(pathData[index - 1]) || pathData[index - 1] == '.')) continue;
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only straight clip paths are implemented.");
                }
                if ((current < 'A' || current > 'Z') && (current < 'a' || current > 'z')) continue;
                if (current is not ('M' or 'm' or 'L' or 'l' or 'H' or 'h' or 'V' or 'v' or 'Z' or 'z' or 'Q' or 'q' or 'T' or 't' or 'C' or 'c' or 'S' or 's' or 'A' or 'a')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Unsupported clip-path command.");
                hasCurve |= current is 'Q' or 'q' or 'T' or 't' or 'C' or 'c' or 'S' or 's' or 'A' or 'a';
                letters++;
            }
            workCounter.Consume(cancellationToken);
            if (letters == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Degenerate clip paths are not implemented.");
            return (letters, hasCurve);
        }

        static bool TryGetAxisClipBox(SvgDevicePoint[] ring, ParserWorkCounter workCounter, CancellationToken cancellationToken, out SvgClipBox box)
        {
            box = default;
            if (ring.Length != 4) return false;
            double minX = Math.Min(Math.Min(ring[0].X, ring[1].X), Math.Min(ring[2].X, ring[3].X));
            double maxX = Math.Max(Math.Max(ring[0].X, ring[1].X), Math.Max(ring[2].X, ring[3].X));
            double minY = Math.Min(Math.Min(ring[0].Y, ring[1].Y), Math.Min(ring[2].Y, ring[3].Y));
            double maxY = Math.Max(Math.Max(ring[0].Y, ring[1].Y), Math.Max(ring[2].Y, ring[3].Y));
            workCounter.Consume(cancellationToken);
            if (!(maxX > minX) || !(maxY > minY)) return false;
            for (int index = 0; index < 4; index++)
            {
                workCounter.Consume(cancellationToken);
                bool onX = ring[index].X == minX || ring[index].X == maxX;
                bool onY = ring[index].Y == minY || ring[index].Y == maxY;
                if (!onX || !onY) return false;
                SvgDevicePoint next = ring[(index + 1) % 4];
                if (ring[index].X != next.X && ring[index].Y != next.Y) return false;
            }
            box = new SvgClipBox(minX, minY, maxX - minX, maxY - minY);
            return true;
        }
    }

    private static string ClaimGradientId(SvgDocumentState state, string id, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        bool known = state.GradientRegistrations.TryGetValue(id, out SvgGradientRegistration registration);
        if (known && registration.IsDefined) throw new SvgException(SvgErrorCode.MalformedInput, "SVG gradient ids must be unique.");
        string retained = state.Strings.Copy(id);
        if (known) state.GradientRegistrations.Replace(id, registration with { IsDefined = true });
        else if (!state.GradientRegistrations.TryAdd(retained, new(true, null))) throw new InvalidOperationException("Gradient identifiers register once.");
        return retained;
    }

    internal static void RegisterGradient(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        SvgGradientTemplate template = state.Gradient ?? throw new InvalidOperationException("Gradient registration requires gradient dimensions.");
        if (state.GradientStops.Count > SvgGradientSyntax.MaximumGradientStops) throw new SvgException(SvgErrorCode.LimitExceeded, "Gradient stop count exceeds the supported limit.");
        if (template.Href is not null)
        {
            if (template.VectorCount is > 0 and < 4 && template.HasUnits) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Partial gradient vector overrides with gradient unit overrides are not implemented.");
            if (template.HasUnits && template.VectorCount == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient unit overrides without vector overrides are not implemented.");
        }
        bool inheritAttributes = template.VectorCount == 4 && (!template.HasUnits || !template.HasTransform || !template.HasSpread);
        if (!state.ThrowOnMissingGradientHref && template.Href is not null && (state.GradientStops.Count == 0 || template.VectorCount < 4 || inheritAttributes))
        {
            string hrefId = SvgGradientSyntax.ParseGradientHrefId(template.Href);
            if (!state.Gradients.TryGetValue(hrefId, out _) && !state.RadialGradients.TryGetValue(hrefId, out _))
            {
                StashPendingLinearGradient(state, template, hrefId, workCounter, cancellationToken);
                return;
            }
        }
        (SvgColor start, SvgColor end, double offset1, double offset2, bool isEmpty, SvgGradientStop[] stops) = ResolveGradientStops(state, template, workCounter, cancellationToken);
        if (inheritAttributes && template.Href is {} href)
        {
            var inherited = ResolveGradientTemplateAttributes(href, state);
            // A complete replacement vector applies the resolved definition map once.
            template = template with
            {
                IsUserSpace = template.HasUnits ? template.IsUserSpace : inherited.IsUserSpace,
                Spread = template.HasSpread ? template.Spread : inherited.Spread,
                Transform = template.HasTransform ? template.Transform : inherited.Transform,
                HasTransform = true,
                Href = null,
            };
        }
        (double x1, double y1, double x2, double y2, bool isUserSpace, SvgGradientSpread spread, double referenceScaleX, double referenceScaleY, bool hasLinearRotation) = ResolveGradientVector(template, state);
        SvgLinearGradient gradient = new(start, end, x1, y1, x2, y2, offset1, offset2, isEmpty, isUserSpace, spread, stops, referenceScaleX, referenceScaleY, hasLinearRotation)
        { SourcePaint = RetainSourcePaint(template, state, workCounter, cancellationToken) };
        string retainedId = state.GradientId ?? throw new InvalidOperationException("Gradient registration requires an identifier.");
        if (!state.Gradients.TryAdd(retainedId, gradient)) throw new InvalidOperationException("Resolved gradients register once.");
        ResolveGradientDependents(state, retainedId, workCounter, cancellationToken);

        static SvgLinearPaintMap RetainSourcePaint(SvgGradientTemplate source, SvgDocumentState document, ParserWorkCounter work, CancellationToken token)
        {
            work.Consume(token);
            SvgLinearPaintMapData? inherited = null;
            if (source.Href is not null && source.VectorCount < 4 && document.Gradients.TryGetValue(SvgGradientSyntax.ParseGradientHrefId(source.Href), out SvgLinearGradient parent))
                inherited = parent.SourcePaint?.Data;
            SvgGradientTransform local = source.HasTransform ? source.Transform : inherited?.Local ?? SvgGradientTransform.Identity;
            return document.Scene.RetainLinearPaintMap(new(
                source.HasX1 ? source.X1 : inherited?.X1 ?? source.X1,
                source.HasY1 ? source.Y1 : inherited?.Y1 ?? source.Y1,
                source.HasX2 ? source.X2 : inherited?.X2 ?? source.X2,
                source.HasY2 ? source.Y2 : inherited?.Y2 ?? source.Y2,
                local, SvgAffineTransform.Identity, false));
        }
    }

    /// <summary>Retains a linear template and its authored stops until its target registers.</summary>
    private static void StashPendingLinearGradient(SvgDocumentState state, SvgGradientTemplate template, string hrefId, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        string id = state.GradientId ?? throw new InvalidOperationException("Gradient registration requires an identifier.");
        string href = state.Strings.Copy(hrefId.AsSpan());
        string hrefRef = state.Strings.Copy(("#" + hrefId).AsSpan());
        SvgGradientStop[] stops = AllocateGradientStops(state);
        for (int index = 0; index < stops.Length; index++)
        {
            workCounter.Consume(cancellationToken);
            stops[index] = state.GradientStops[index];
        }
        workCounter.Consume(cancellationToken);
        SvgPendingGradientReference reference = new(SvgGradientKind.Linear, state.PendingLinearGradientHrefs.Count);
        SvgPendingGradientReference? next = LinkPendingGradient(state, href, reference, workCounter, cancellationToken);
        state.PendingLinearGradientHrefs.Add(new(id, template with { Href = hrefRef }, stops) { Next = next });
    }

    private static SvgPendingGradientReference? LinkPendingGradient(SvgDocumentState state, string target, SvgPendingGradientReference reference,
        ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        if (state.GradientRegistrations.TryGetValue(target, out SvgGradientRegistration registration))
        {
            state.GradientRegistrations.Replace(target, registration with { FirstDependent = reference });
            return registration.FirstDependent;
        }
        if (!state.GradientRegistrations.TryAdd(target, new(false, reference))) throw new InvalidOperationException("Gradient waiting lists register once.");
        return null;
    }

    /// <summary>Wakes only aliases of a newly resolved gradient, visiting each dependency once across both kinds.</summary>
    /// <remarks>Document-owned lists keep stable indices and borrow their stop arrays until teardown.
    /// Links and ready storage are reserved before growth; traversal charges work and observes cancellation.
    /// Nested registrations append to the same iterative stack without recursion. State is conversion-local.</remarks>
    private static void ResolveGradientDependents(SvgDocumentState state, string target, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        if (state.GradientRegistrations.TryGetValue(target, out SvgGradientRegistration registration) && registration.FirstDependent is {} first)
        {
            state.GradientRegistrations.Replace(target, registration with { FirstDependent = null });
            SvgPendingGradientReference? next = first;
            while (next is {} reference)
            {
                workCounter.Consume(cancellationToken);
                state.ReadyGradientHrefs.Add(reference);
                next = reference.Kind == SvgGradientKind.Linear ? state.PendingLinearGradientHrefs[reference.Index].Next : state.PendingRadialGradientHrefs[reference.Index].Next;
            }
        }
        if (state.DrainingGradientHrefs || state.ReadyGradientHrefs.Count == 0) return;
        state.DrainingGradientHrefs = true;
        try
        {
            while (state.ReadyGradientHrefs.Count > 0)
            {
                workCounter.Consume(cancellationToken);
                SvgPendingGradientReference reference = state.ReadyGradientHrefs[^1];
                state.ReadyGradientHrefs.RemoveLast();
                if (reference.Kind == SvgGradientKind.Linear) RegisterPendingLinearGradient(state, state.PendingLinearGradientHrefs[reference.Index], workCounter, cancellationToken);
                else RegisterPendingRadialGradient(state, state.PendingRadialGradientHrefs[reference.Index], workCounter, cancellationToken);
            }
        }
        finally
        {
            state.DrainingGradientHrefs = false;
        }
    }

    /// <summary>Resolves deferred gradient href inheritances, rejecting targets that never registered with the original registration error.</summary>
    internal static void ResolvePendingGradientHrefs(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        if (state.PendingLinearGradientHrefs.Count > 0 || state.PendingRadialGradientHrefs.Count > 0)
        {
            state.ThrowOnMissingGradientHref = true;
            try
            {
                foreach (PendingLinearGradientHref pending in state.PendingLinearGradientHrefs.AsSpan())
                {
                    workCounter.Consume(cancellationToken);
                    if (!state.Gradients.TryGetValue(pending.Id, out _)) RegisterPendingLinearGradient(state, pending, workCounter, cancellationToken);
                }
                foreach (PendingRadialGradientHref pending in state.PendingRadialGradientHrefs.AsSpan())
                {
                    workCounter.Consume(cancellationToken);
                    if (!state.RadialGradients.TryGetValue(pending.Id, out _)) RegisterPendingRadialGradient(state, pending, workCounter, cancellationToken);
                }
            }
            finally
            {
                state.ThrowOnMissingGradientHref = false;
            }
        }
    }

    private static void RegisterPendingLinearGradient(SvgDocumentState state, PendingLinearGradientHref pending, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        state.GradientId = pending.Id;
        state.Gradient = pending.Template;
        state.GradientStops.Clear();
        foreach (SvgGradientStop stop in pending.Stops)
        {
            workCounter.Consume(cancellationToken);
            state.GradientStops.Add(stop);
        }

        try
        {
            RegisterGradient(state, workCounter, cancellationToken);
        }
        finally
        {
            state.GradientId = null;
            state.Gradient = null;
            state.GradientStops.Clear();
        }
    }

    private static void RegisterPendingRadialGradient(SvgDocumentState state, PendingRadialGradientHref pending, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        state.RadialGradientId = pending.Id;
        state.RadialGradient = pending.Template;
        state.GradientStops.Clear();
        foreach (SvgGradientStop stop in pending.Stops)
        {
            workCounter.Consume(cancellationToken);
            state.GradientStops.Add(stop);
        }

        try
        {
            RegisterRadialGradient(state, workCounter, cancellationToken);
        }
        finally
        {
            state.RadialGradientId = null;
            state.RadialGradient = null;
            state.GradientStops.Clear();
        }
    }
    internal static void RegisterRadialGradient(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        SvgRadialTemplate template = state.RadialGradient ?? throw new InvalidOperationException("Radial gradient registration requires gradient dimensions.");
        if (state.GradientStops.Count > SvgGradientSyntax.MaximumGradientStops) throw new SvgException(SvgErrorCode.LimitExceeded, "Gradient stop count exceeds the supported limit.");
        if (template.Href is not null && template.VectorCount > 0 && template.VectorCount < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Partial radial vector overrides are not implemented.");
        if (template.Href is not null && template.VectorCount == 0 && (template.HasFocalX || template.HasFocalY || template.HasFocalRadius)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Partial radial vector overrides are not implemented.");
        if (template.Href is not null && template.HasUnits && template.VectorCount == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Radial unit overrides without vector overrides are not implemented.");
        bool inheritAttributes = template.VectorCount == 3 && (!template.HasUnits || !template.HasTransform || !template.HasSpread);
        if (!state.ThrowOnMissingGradientHref && template.Href is not null && (state.GradientStops.Count == 0 || template.VectorCount == 0 || inheritAttributes))
        {
            string hrefId = SvgGradientSyntax.ParseGradientHrefId(template.Href);
            if (!state.Gradients.TryGetValue(hrefId, out _) && !state.RadialGradients.TryGetValue(hrefId, out _))
            {
                StashPendingRadialGradient(state, template, hrefId, workCounter, cancellationToken);
                return;
            }
        }
        (SvgColor start, SvgColor end, double offset1, double offset2, bool isEmpty, SvgGradientStop[] stops) = ResolveRadialStops(state, template, workCounter, cancellationToken);
        if (inheritAttributes && template.Href is {} href)
        {
            var inherited = ResolveGradientTemplateAttributes(href, state);
            // A complete replacement vector applies the resolved definition map once.
            template = template with
            {
                IsUserSpace = template.HasUnits ? template.IsUserSpace : inherited.IsUserSpace,
                Spread = template.HasSpread ? template.Spread : inherited.Spread,
                Transform = template.HasTransform ? template.Transform : inherited.Transform,
                HasTransform = true,
                Href = null,
            };
        }
        (double centerX, double centerY, double radius, double focalX, double focalY, double focalRadius, bool isUserSpace, SvgGradientSpread spread, double referenceScaleX, double referenceScaleY, bool hasRadialRotation, SvgGradientTransform inverse, bool hasGeneralTransform) = ResolveRadialVector(template, state);
        if (!isUserSpace) ValidateRadialFocalCircle(centerX, centerY, radius, focalX, focalY, focalRadius);
        SvgRadialGradient gradient = new(start, end, centerX, centerY, radius, focalX, focalY, focalRadius, offset1, offset2, isEmpty, isUserSpace, spread, stops, referenceScaleX, referenceScaleY, hasRadialRotation, inverse, hasGeneralTransform)
        {
            TemplateTransform = template.HasTransform ? template.Transform
                : template.Href is {} parentHref ? ResolveGradientTemplateAttributes(parentHref, state).Transform : SvgGradientTransform.Identity,
        };
        if (hasGeneralTransform && template.HasTransform)
        {
            SvgGradientTransform map = template.Transform;
            gradient = gradient with { NativePaintMatrices = new(new((float)map.A, (float)map.C, (float)map.E, (float)map.B, (float)map.D, (float)map.F), SvgAffineTransform.Identity) };
        }
        string retainedId = state.RadialGradientId ?? throw new InvalidOperationException("Radial gradient registration requires an identifier.");
        if (!state.RadialGradients.TryAdd(retainedId, gradient)) throw new InvalidOperationException("Resolved gradients register once.");
        ResolveGradientDependents(state, retainedId, workCounter, cancellationToken);
    }

    /// <summary>Reads independently inherited attributes from a resolved local template without owning storage.</summary>
    private static (bool IsUserSpace, SvgGradientTransform Transform, SvgGradientSpread Spread) ResolveGradientTemplateAttributes(string href, SvgDocumentState state)
    {
        string id = SvgGradientSyntax.ParseGradientHrefId(href);
        if (state.Gradients.TryGetValue(id, out SvgLinearGradient linear))
            return (linear.IsUserSpace, linear.SourcePaint?.Data.Local ?? SvgGradientTransform.Identity, linear.Spread);
        if (state.RadialGradients.TryGetValue(id, out SvgRadialGradient radial))
            return (radial.IsUserSpace, radial.TemplateTransform, radial.Spread);
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
    }

    /// <summary>Retains a radial template and its authored stops until its target registers.</summary>
    private static void StashPendingRadialGradient(SvgDocumentState state, SvgRadialTemplate template, string hrefId, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        string id = state.RadialGradientId ?? throw new InvalidOperationException("Radial gradient registration requires an identifier.");
        string href = state.Strings.Copy(hrefId.AsSpan());
        string hrefRef = state.Strings.Copy(("#" + hrefId).AsSpan());
        SvgGradientStop[] stops = AllocateGradientStops(state);
        for (int index = 0; index < stops.Length; index++)
        {
            workCounter.Consume(cancellationToken);
            stops[index] = state.GradientStops[index];
        }
        workCounter.Consume(cancellationToken);
        SvgPendingGradientReference reference = new(SvgGradientKind.Radial, state.PendingRadialGradientHrefs.Count);
        SvgPendingGradientReference? next = LinkPendingGradient(state, href, reference, workCounter, cancellationToken);
        state.PendingRadialGradientHrefs.Add(new(id, template with { Href = hrefRef }, stops) { Next = next });
    }

    private static (SvgColor Start, SvgColor End, double Offset1, double Offset2, bool IsEmpty, SvgGradientStop[] Stops) ResolveRadialStops(SvgDocumentState state, SvgRadialTemplate template, ParserWorkCounter work, CancellationToken token)
    {
        if (state.GradientStops.Count >= 1) return ClampStopList(state, work, token);
        if (template.Href is null) return (default, default, 0, 1, true, Array.Empty<SvgGradientStop>());
        string stopHrefId = SvgGradientSyntax.ParseGradientHrefId(template.Href);
        if (state.RadialGradients.TryGetValue(stopHrefId, out SvgRadialGradient parent)) return (parent.Start, parent.End, parent.Offset1, parent.Offset2, parent.IsEmpty, parent.Stops);
        if (!state.Gradients.TryGetValue(stopHrefId, out SvgLinearGradient linear)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local radial gradient references are implemented.");
        return (linear.Start, linear.End, linear.Offset1, linear.Offset2, linear.IsEmpty, linear.Stops);
    }

    /// <summary>Reports whether one radial gradient transform stays on the legacy endpoint-mapping path.</summary>
    /// <remarks>Mirrors the historical admission gates exactly so previously admitted inputs keep bitwise-identical records: axis-aligned transforms without rotated-chain non-uniformity, plus similarities under uniform incoming scales; objectBoundingBox axis maps need uniformity outright since only userSpace evaluation carries reference scales. Everything else routes to the inverse-map path when href-free and invertible.</remarks>
    private static bool IsLegacyRadialTransform(SvgGradientTransform transform, bool isUserSpace, bool hasRotation, double referenceScaleX, double referenceScaleY)
    {
        if (transform.B != 0 || transform.C != 0) return transform.IsSimilarity() && Math.Abs(referenceScaleX) == Math.Abs(referenceScaleY);
        return isUserSpace ? (!hasRotation || transform.A == transform.D) : transform.A == transform.D;
    }

    private static (double CenterX, double CenterY, double Radius, double FocalX, double FocalY, double FocalRadius, bool IsUserSpace, SvgGradientSpread Spread, double ReferenceScaleX, double ReferenceScaleY, bool HasRotation, SvgGradientTransform Inverse, bool HasGeneralTransform) ResolveRadialVector(SvgRadialTemplate template, SvgDocumentState state)
    {
        double centerX = template.CenterX;
        double centerY = template.CenterY;
        double radius = template.Radius;
        double focalRadius = template.FocalRadius;
        bool isUserSpace = template.IsUserSpace;
        bool usesInitialVector = template.VectorCount < 3;
        double referenceScaleX = 1;
        double referenceScaleY = 1;
        SvgGradientSpread spread = template.Spread;
        SvgGradientTransform inverse = SvgGradientTransform.Identity;
        bool hasGeneralTransform = false;
        bool hasRotation = false;
        double focalX;
        double focalY;
        if (template.Href is not null && template.VectorCount == 0 && !template.HasFocalX && !template.HasFocalY && !template.HasFocalRadius)
        {
            string vectorHrefId = SvgGradientSyntax.ParseGradientHrefId(template.Href);
            if (state.RadialGradients.TryGetValue(vectorHrefId, out SvgRadialGradient parent))
            {
                usesInitialVector = false;
                centerX = parent.CenterX;
                centerY = parent.CenterY;
                radius = parent.Radius;
                if (!template.HasSpread) spread = parent.Spread;
                if (!template.HasUnits) isUserSpace = parent.IsUserSpace;
                focalX = parent.FocalX;
                focalY = parent.FocalY;
                focalRadius = parent.FocalRadius;
                referenceScaleX = parent.ReferenceScaleX;
                referenceScaleY = parent.ReferenceScaleY;
                hasRotation = parent.HasRotation;
            }
            else if (state.Gradients.TryGetValue(vectorHrefId, out SvgLinearGradient linear))
            {
                if (!template.HasSpread) spread = linear.Spread;
                if (!template.HasUnits) isUserSpace = linear.IsUserSpace;
                focalX = centerX;
                focalY = centerY;
                referenceScaleX = linear.ReferenceScaleX;
                referenceScaleY = linear.ReferenceScaleY;
                hasRotation = linear.HasRotation;
            }
            else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local radial gradient references are implemented.");
        }
        else
        {
            focalX = template.HasFocalX ? template.FocalX : centerX;
            focalY = template.HasFocalY ? template.FocalY : centerY;
        }
        SvgGradientSyntax.ValidateCoordinateUnits(isUserSpace, template.HasPercentageCoordinates || usesInitialVector);
        if (isUserSpace) ValidateRadialFocalCircle(centerX, centerY, radius, focalX, focalY, focalRadius);
        if (template.HasTransform)
        {
            if (template.Href is null && !IsLegacyRadialTransform(template.Transform, isUserSpace, hasRotation, referenceScaleX, referenceScaleY))
            {
                if (!template.Transform.TryInvert(out SvgGradientTransform generalInverse)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Singular radial gradient transforms are not implemented.");
                hasRotation = true;
                hasGeneralTransform = true;
                inverse = generalInverse;
            }
            else
            {
            if ((template.Transform.B != 0 || template.Transform.C != 0) && (!template.Transform.IsSimilarity() || Math.Abs(referenceScaleX) != Math.Abs(referenceScaleY))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned or similar radial gradient transforms are implemented.");
            if (template.Transform.B == 0 && template.Transform.C == 0 && hasRotation && template.Transform.A != template.Transform.D) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned or similar radial gradient transforms are implemented.");
            hasRotation = hasRotation || template.Transform.B != 0 || template.Transform.C != 0;
            (centerX, centerY) = template.Transform.Apply(centerX, centerY);
            (focalX, focalY) = template.Transform.Apply(focalX, focalY);
            double radiusScale = template.Transform.B == 0 && template.Transform.C == 0 ? Math.Abs(template.Transform.A) : Math.Sqrt(template.Transform.A * template.Transform.A + template.Transform.B * template.Transform.B);
            radius *= radiusScale;
            focalRadius *= radiusScale;
            if (isUserSpace)
            {
                referenceScaleX *= template.Transform.A;
                referenceScaleY *= template.Transform.D;
            }
            }
        }
        return (centerX, centerY, radius, focalX, focalY, focalRadius, isUserSpace, spread, referenceScaleX, referenceScaleY, hasRotation, inverse, hasGeneralTransform);
    }

    private static void ValidateRadialFocalCircle(double centerX, double centerY, double radius, double focalX, double focalY, double focalRadius)
    {
        double focalDeltaX = focalX - centerX;
        double focalDeltaY = focalY - centerY;
        if (radius == 0)
        {
            if (focalDeltaX != 0 || focalDeltaY != 0 || focalRadius != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Offset radial foci outside the gradient circle are not implemented.");
        }
        else
        {
            double inner = radius - focalRadius;
            if (inner <= 0 || focalDeltaX * focalDeltaX + focalDeltaY * focalDeltaY >= inner * inner) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Offset radial foci outside the gradient circle are not implemented.");
        }
    }

    private static (SvgColor Start, SvgColor End, double Offset1, double Offset2, bool IsEmpty, SvgGradientStop[] Stops) ResolveGradientStops(SvgDocumentState state, SvgGradientTemplate template, ParserWorkCounter work, CancellationToken token)
    {
        if (state.GradientStops.Count >= 1) return ClampStopList(state, work, token);
        if (template.Href is null) return (default, default, 0, 1, true, Array.Empty<SvgGradientStop>());
        if (!state.Gradients.TryGetValue(SvgGradientSyntax.ParseGradientHrefId(template.Href), out SvgLinearGradient parent))
        {
            if (!state.RadialGradients.TryGetValue(SvgGradientSyntax.ParseGradientHrefId(template.Href), out SvgRadialGradient radial)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
            return (radial.Start, radial.End, radial.Offset1, radial.Offset2, radial.IsEmpty, radial.Stops);
        }

        return (parent.Start, parent.End, parent.Offset1, parent.Offset2, parent.IsEmpty, parent.Stops);
    }

    /// <summary>Reserves each owned stop array once; href aliases borrow it and scene adoption retains its own allowance.</summary>
    private static SvgGradientStop[] AllocateGradientStops(SvgDocumentState state)
    {
        int count = state.GradientStops.Count;
        if (count == 0) return [];
        RenderBudget.BudgetReservation reservation = state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGradientStop>(count), SvgMemoryCategory.Parser);
        try { state.DefinitionReservations.Add(reservation); }
        catch { reservation.Dispose(); throw; }
        return new SvgGradientStop[count];
    }

    private static (SvgColor Start, SvgColor End, double Offset1, double Offset2, bool IsEmpty, SvgGradientStop[] Stops) ClampStopList(SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        // A stop offset below the previous effective offset rises to it, forming a hard edge; Math.Clamp below preserves the order.
        for (int index = 1; index < state.GradientStops.Count; index++)
        {
            work.Consume(token);
            if (state.GradientStops[index].Offset < state.GradientStops[index - 1].Offset) state.GradientStops[index] = state.GradientStops[index] with { Offset = state.GradientStops[index - 1].Offset };
        }
        SvgGradientStop[] clamped = AllocateGradientStops(state);
        for (int index = 0; index < clamped.Length; index++)
        {
            work.Consume(token);
            clamped[index] = state.GradientStops[index] with { Offset = Math.Clamp(state.GradientStops[index].Offset, 0, 1) };
        }
        if (clamped.Length > 1 && !(clamped[0].Offset < clamped[^1].Offset)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only gradient stops spanning a nonzero clamped offset range are implemented.");
        return (clamped[0].Color, clamped[^1].Color, clamped[0].Offset, clamped[^1].Offset, false, clamped);
    }

    private static (double X1, double Y1, double X2, double Y2, bool IsUserSpace, SvgGradientSpread Spread, double ReferenceScaleX, double ReferenceScaleY, bool HasRotation) ResolveGradientVector(SvgGradientTemplate template, SvgDocumentState state)
    {
        double x1 = template.X1;
        double y1 = template.Y1;
        double x2 = template.X2;
        double y2 = template.Y2;
        bool isUserSpace = template.IsUserSpace;
        bool usesInitialX2 = !template.HasX2;
        double referenceScaleX = 1;
        double referenceScaleY = 1;
        SvgGradientSpread spread = template.Spread;
        bool hasRotation = false;
        if (template.Href is not null && template.VectorCount < 4)
        {
            if (!state.Gradients.TryGetValue(SvgGradientSyntax.ParseGradientHrefId(template.Href), out SvgLinearGradient parent))
            {
                if (!state.RadialGradients.TryGetValue(SvgGradientSyntax.ParseGradientHrefId(template.Href), out SvgRadialGradient radial)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
                if (template.VectorCount != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Partial gradient vector overrides with cross-type gradient references are not implemented.");
                if (!template.HasUnits) isUserSpace = radial.IsUserSpace;
                if (!template.HasSpread) spread = radial.Spread;
                referenceScaleX = radial.ReferenceScaleX;
                referenceScaleY = radial.ReferenceScaleY;
                hasRotation = radial.HasRotation;
            }
            else
            {
                usesInitialX2 = false;
                x1 = template.HasX1 ? template.X1 : parent.X1;
                y1 = template.HasY1 ? template.Y1 : parent.Y1;
                x2 = template.HasX2 ? template.X2 : parent.X2;
                y2 = template.HasY2 ? template.Y2 : parent.Y2;
                if (!template.HasUnits) isUserSpace = parent.IsUserSpace;
                if (!template.HasSpread) spread = parent.Spread;
                referenceScaleX = parent.ReferenceScaleX;
                referenceScaleY = parent.ReferenceScaleY;
                hasRotation = parent.HasRotation;
            }
        }
        SvgGradientSyntax.ValidateCoordinateUnits(isUserSpace, template.HasPercentageCoordinates || usesInitialX2);
        if (template.HasTransform)
        {
            if ((template.Transform.B != 0 || template.Transform.C != 0) && !template.Transform.IsSimilarity())
                return (x1, y1, x2, y2, isUserSpace, spread, referenceScaleX, referenceScaleY, hasRotation);
            if ((template.Transform.B != 0 || template.Transform.C != 0) && Math.Abs(referenceScaleX) != Math.Abs(referenceScaleY)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned or similar linear gradient transforms are implemented.");
            if (template.Transform.B == 0 && template.Transform.C == 0 && hasRotation && template.Transform.A != template.Transform.D) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned or similar linear gradient transforms are implemented.");
            hasRotation = hasRotation || template.Transform.B != 0 || template.Transform.C != 0;
            (x1, y1) = template.Transform.Apply(x1, y1);
            (x2, y2) = template.Transform.Apply(x2, y2);
            if (isUserSpace)
            {
                referenceScaleX *= template.Transform.A;
                referenceScaleY *= template.Transform.D;
            }
        }
        return (x1, y1, x2, y2, isUserSpace, spread, referenceScaleX, referenceScaleY, hasRotation);

    }

    /// <summary>Promotes the legacy first rectangle before appending a vector leaf or entering a tile group.</summary>
    private static SvgSceneBuilder GetPatternContentBuilder(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgPatternTile tile = state.PatternTile ?? throw new InvalidOperationException("Vector pattern content requires a tile grid.");
        SvgSceneBuilder builder = state.PatternContentBuilder ??= new(state.Budget, workCounter, cancellationToken);
        if (tile.Content is not null)
        {
            SvgDefinitionLeafSource first = state.PatternRectangleSource ?? throw new InvalidOperationException("Rectangle promotion requires retained source attributes.");
            SvgPaintableElementContext previous = state.PatternRectangleContext;
            SvgPaintRecording.AdoptLeafScene(first.Lower(previous.InheritedStroke, previous.InheritedPresentation.Paint, previous.InheritedFillRule, previous.InheritedPresentation.PaintServerFill, previous.InheritedPresentation.PaintOrder, previous.InheritedPresentation.Markers, previous.InheritedPresentation.ElementOpacity, previous.InheritedPresentation.NonScalingStroke, SvgAffineTransform.Identity, previous.LengthBases, false, previous.InheritedPresentation.IsDisplayed, state), builder, state);
            state.PatternTile = tile with { Content = null };
        }
        return builder;
    }

    internal static void RegisterPattern(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        string retainedId = state.Strings.Copy(state.PatternId ?? throw new InvalidOperationException("Pattern registration requires an identifier."));
        SvgPatternTile tile = state.PatternTile ?? throw new InvalidOperationException("Pattern registration requires pattern dimensions.");
        if (state.PatternDefinitions.TryGetValue(retainedId, out _)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG pattern ids must be unique.");
        if (state.PatternContentBuilder is {} builder)
        {
            int scopeCount = (state.PatternScopes?.Count ?? 0) - state.PatternScopeStart;
            SvgVectorContent content = SvgVectorContent.CreatePending(state.Budget);
            try { state.Scene.AdoptVectorContent(content); }
            catch { content.Dispose(); throw; }
            state.DeferredPatternContents.Add(new(builder, content, state.PatternUseStart, state.PatternUseCount, state.PatternScopeStart, scopeCount, state.PatternCommandStart, state.PendingCommandEnd));
            state.PatternContentBuilder = null;
            tile = tile with { Content = null, VectorContent = content };
        }
        if (!state.PatternTemplate.IsDisplayed) tile = tile with { Content = null, VectorContent = null };
        SvgPatternDefinition definition = new(state.PatternTemplate, tile, state.PatternRectangleSource, state.PatternRectangleContext, false);
        if (!state.PatternDefinitions.TryAdd(retainedId, definition)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG pattern ids must be unique.");
        if (definition.Template.Href is null)
        {
            if (!state.Patterns.TryAdd(retainedId, tile)) throw new InvalidOperationException("A pattern must register once.");
        }
        SvgPatternReferenceResolver.Register(state, retainedId, workCounter, cancellationToken);
    }

    internal static void RegisterMaskContent(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        string retainedId = state.Strings.Copy(state.MaskId ?? throw new InvalidOperationException("Mask content registration requires an identifier."));
        SvgGeometry[] geometries = state.MaskContentGeometries ?? throw new InvalidOperationException("Mask content registration requires retained geometries.");
        if (state.Masks.TryGetValue(retainedId, out _) || !state.MaskContents.TryAdd(retainedId, new SvgMaskContent(geometries, state.MaskContentLuminance, state.MaskContentIsAlpha))) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskCustomRegion is SvgClipBox region && !state.MaskRegions.TryAdd(retainedId, region)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskFractionRegion is SvgClipBox fractions && !state.MaskFractionRegions.TryAdd(retainedId, fractions)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskContentBox is SvgClipBox content && !state.MaskContentBoxes.TryAdd(retainedId, content)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
    }

    internal static void RegisterMask(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        string retainedId = state.Strings.Copy(state.MaskId ?? throw new InvalidOperationException("Mask registration requires an identifier."));
        if (state.MaskContents.TryGetValue(retainedId, out _) || !state.Masks.TryAdd(retainedId, state.MaskValue ?? 0)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskCustomRegion is SvgClipBox region && !state.MaskRegions.TryAdd(retainedId, region)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskFractionRegion is SvgClipBox fractions && !state.MaskFractionRegions.TryAdd(retainedId, fractions)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskContentBox is SvgClipBox content && !state.MaskContentBoxes.TryAdd(retainedId, content)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskValue is not null && state.MaskReplayChildCount == 1
            && state.MaskReplayChildren[state.MaskReplayChildStart].Source.LocalName == "rect")
        {
            // Retain the existing borrowed snapshot, not another geometry copy.
            // Covering targets keep the scalar route; partial targets replay native paint.
            state.MaskContents.TryAdd(retainedId, new([], 255, state.MaskIsAlpha)
            {
                UniformSource = new(state.MaskReplayChildStart)
            });
        }
        else if (!state.MaskDefaultUserSpaceRegion && state.MaskCustomRegion is SvgClipBox custom && state.MaskContentBox is SvgClipBox cover && !cover.Contains(custom.X, custom.Y, custom.X + custom.Width, custom.Y + custom.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only mask content covering its custom region is implemented.");
    }

    internal static void RegisterClipPolygon(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        string retainedId = state.Strings.Copy(state.ClipPathId ?? throw new InvalidOperationException("Clip polygon registration requires an identifier."));
        SvgClipPolygon polygon = state.ClipPolygon ?? throw new InvalidOperationException("Clip polygon registration requires retained points.");
        polygon = polygon with { IsDisabled = !state.ClipIsDisplayed };
        if (!polygon.IsDisabled && polygon.ChildCount > 1 && !polygon.IsResolvedUnion)
        {
            SvgAuthoredPath previous = polygon.SourcePath ?? throw new InvalidOperationException("A child union requires source commands.");
            SvgAuthoredPath normalized = NormalizeUnion(polygon, state.Budget, workCounter, cancellationToken);
            polygon = polygon with { SourcePath = normalized };
            // Publish ownership before releasing the old path or registering the
            // id, so duplicate ids and cancellation retain one cleanup owner.
            state.ClipPolygon = polygon;
            previous.Dispose();
        }
        if (state.Clips.TryGetValue(retainedId, out _) || state.ObjectClips.TryGetValue(retainedId, out _) || !state.ClipPolygons.TryAdd(retainedId, polygon)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG clip-path ids must be unique.");

        // Preserve the qualified contour ordering and cyclic starts. Same-directed
        // nonzero contours represent the union of overlapping convex children;
        // disjoint content retains its established parity replay.
        static SvgAuthoredPath NormalizeUnion(SvgClipPolygon polygon, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        {
            if (SvgClipUnion.ResolveCrossings(polygon, budget, work, token) is {} resolved) return resolved;
            int count = polygon.ContourEnds.Length;
            if (count > 42) throw new InvalidOperationException("A retained union exceeded its admitted child limit.");
            Span<int> order = stackalloc int[42];
            Span<SvgPaintBounds> bounds = stackalloc SvgPaintBounds[42];
            int start = 0;
            for (int contour = 0; contour < count; contour++)
            {
                float left = float.PositiveInfinity, top = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.NegativeInfinity;
                int end = polygon.ContourEnds[contour];
                for (int index = start; index < end; index++)
                {
                    work.Consume(token);
                    SvgDevicePoint point = polygon.Points[index];
                    left = MathF.Min(left, (float)point.X); top = MathF.Min(top, (float)point.Y);
                    right = MathF.Max(right, (float)point.X); bottom = MathF.Max(bottom, (float)point.Y);
                }
                bounds[contour] = new(left, top, right, bottom);
                int position = contour;
                while (position > 0)
                {
                    work.Consume(token);
                    SvgPaintBounds preceding = bounds[order[position - 1]];
                    if (preceding.Top < top || (preceding.Top == top && preceding.Left <= left)) break;
                    order[position] = order[position - 1]; position--;
                }
                order[position] = contour;
                start = end;
            }
            using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, work, token);
            for (int position = 0; position < count; position++)
            {
                work.Consume(token);
                int contour = order[position];
                start = contour == 0 ? 0 : polygon.ContourEnds[contour - 1];
                int end = polygon.ContourEnds[contour];
                while (end > start && polygon.Points[end - 1] == polygon.Points[start]) { work.Consume(token); end--; }
                int next = start + 1;
                while (next < end && polygon.Points[next] == polygon.Points[start]) { work.Consume(token); next++; }
                if (next >= end) throw new InvalidOperationException("A retained union contour became degenerate.");
                SvgDevicePoint first = polygon.Points[next];
                commands.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, first, default, default));
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, polygon.Points[start], default, default));
                for (int index = end - 1; index > next; index--)
                {
                    work.Consume(token);
                    if (polygon.Points[index] != polygon.Points[index + 1 < end ? index + 1 : start])
                        commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, polygon.Points[index], default, default));
                }
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, first, default, default));
                commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
            }
            OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
            try { return SvgAuthoredPath.Create(owned, SvgAffineTransform.Identity, budget); }
            catch { owned.Dispose(); throw; }
        }
    }

    internal static void RegisterClipBox(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        string retainedId = state.Strings.Copy(state.ClipPathId ?? throw new InvalidOperationException("Clip-path registration requires an identifier."));
        if (state.ClipPolygons.TryGetValue(retainedId, out _)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG clip-path ids must be unique.");
        if (!state.ClipIsDisplayed)
        {
            if (state.Clips.TryGetValue(retainedId, out _) || state.ObjectClips.TryGetValue(retainedId, out _)
                || !state.ClipPolygons.TryAdd(retainedId, new([], []) { IsDisabled = true }))
                throw new SvgException(SvgErrorCode.MalformedInput, "SVG clip-path ids must be unique.");
            state.ClipPolygon?.SourcePath?.Dispose();
            state.ClipPolygon = null;
            return;
        }
        if (state.ClipIsObjectBoundingBox) { if (state.Clips.TryGetValue(retainedId, out _) || !state.ObjectClips.TryAdd(retainedId, state.ClipBox ?? new SvgClipBox(0, 0, 0, 0))) throw new SvgException(SvgErrorCode.MalformedInput, "SVG clip-path ids must be unique."); } else if (state.ObjectClips.TryGetValue(retainedId, out _) || !state.Clips.TryAdd(retainedId, state.ClipBox ?? new SvgClipBox(0, 0, 0, 0))) throw new SvgException(SvgErrorCode.MalformedInput, "SVG clip-path ids must be unique.");
        state.ClipPolygon?.SourcePath?.Dispose();
        state.ClipPolygon = null;
    }

    internal static void RejectPatternContentPaint(SvgComputedStyle style, SvgDocumentState state)
    {
        if (style.Fill is {} fill && SvgLocalUrl.TryParseId(fill, out string fillId) && (fillId == state.PatternId || state.Patterns.TryGetValue(fillId, out _))
            || style.Stroke is {} stroke && SvgLocalUrl.TryParseId(stroke, out string strokeId) && (strokeId == state.PatternId || state.Patterns.TryGetValue(strokeId, out _)))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern paints inside pattern content are not implemented.");
    }

    internal static void RegisterFilter(SvgDocumentState state)
    {
        SvgFilterDefinitionBuilder builder = state.FilterDefinition ?? throw new InvalidOperationException("No filter definition is open.");
        SvgFilterResource resource = builder.Finish();
        bool adopted = false;
        try
        {
            if (builder.Id is string id) { state.Scene.Filters.AddResource(id, resource); adopted = true; }
        }
        finally
        {
            if (!adopted) resource.Dispose();
            builder.Dispose();
            state.FilterDefinition = null;
            state.FilterDepth = -1;
        }
    }

    internal static void RegisterSymbol(SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        SvgSymbolDefinitionBuilder builder = state.SymbolDefinition ?? throw new InvalidOperationException("Symbol registration requires an open builder.");
        SvgDefinition definition = builder.Build(state.SymbolViewBox, state, work, token) with { Marker = state.MarkerDefinition, EstablishesViewport = builder.EstablishesViewport };
        bool retained = false;
        try
        {
            SvgPaintRecording.WrapInstance(definition.Geometries, builder.Presentation.SourceOpacity, state.NewRecordingScope(), state, work, token);
            work.Consume(token);
            if (state.SymbolId is string id)
            {
                if (!state.Definitions.TryAdd(id, definition)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
            }
            else state.RetainAnonymousSymbol(definition, work, token);
            if (state.MaskGroupChildIndex >= 0)
            {
                SvgMaskReplayChild child = state.MaskReplayChildren[state.MaskGroupChildIndex];
                state.MaskReplayChildren[state.MaskGroupChildIndex] = child with
                {
                    GroupContent = definition.SymbolContent ?? throw new InvalidOperationException("Mask groups require sealed source content.")
                };
                state.MaskGroupChildIndex = -1;
            }
            retained = true;
        }
        finally
        {
            if (!retained) { foreach (SvgGeometry geometry in definition.Geometries) geometry.Dispose(); definition.SymbolContent?.Content.Dispose(); }
            builder.Dispose();
            state.SymbolDefinition = null;
            state.SymbolDepth = -1;
            state.SymbolId = null;
            state.SymbolViewBox = null;
            state.MarkerDefinition = null;
        }
    }

    private static SvgDefinition ParseLineDefinition(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state)
    {
        _ = SvgLineSyntax.Parse(reader);
        SvgComputedStyle style = context.ResolveStyle(reader);
        string? stroke = style.Stroke ?? context.InheritedStroke.Stroke;
        if (stroke is null || stroke == "none" || SvgStrokeStyleSyntax.IsZeroStrokeWidth(style.Width)) return new([], false, null, null);
        return new(RetainParsedGeometries(SvgLeafParser.ParseLine(reader, context, null, state), state, context.WorkCounter, context.CancellationToken), false, null, null);
    }

    /// <summary>Lowers one immutable source leaf with its resolved instance stroke, reusing the ordinary shape routes.</summary>
    /// <remarks>The caller owns returned geometries. Document reservations own array/binding storage; failure releases
    /// native geometry and every lowerer observes the supplied work, cancellation and memory bounds.</remarks>
    internal static SvgDefinition LowerInstanceSource(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state)
        => LowerInstanceSource(reader, context, state, context.InheritedPresentation.IsDisplayed);

    /// <summary>Retains ordinary raw geometry while activating source filters only for the instance's actual display state.</summary>
    internal static SvgDefinition LowerInstanceSource(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state, bool isDisplayed)
    {
        SvgComputedStyle sourceStyle = context.ResolveStyle(reader);
        bool filtered = sourceStyle.Filter is {} filter && filter.Trim() != "none";
        if (filtered) context = context with { AuthoredStyle = sourceStyle with { Filter = null, Opacity = null, ComputedOpacity = null } };
        if (reader.LocalName is not ("line" or "path" or "polygon" or "polyline") && SvgMarkerStyle.Resolve(context.ResolveStyle(reader), context.InheritedPresentation.Markers).HasMarkers)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Markers on shapes other than lines and paths are not implemented.");
        string? clipId = SvgClipPathSyntax.ResolveClipId(reader, state);
        bool previousClipDeferral = state.DeferringLeafClip;
        state.DeferringLeafClip |= clipId is not null;
        SvgDefinition definition;
        try
        {
            definition = reader.LocalName == "rect"
                ? new(RetainParsedGeometries(SvgLeafParser.ParseRectangle(reader, context, null, state), state, context.WorkCounter, context.CancellationToken), false, null, null)
                : reader.LocalName == "image" ? ParseImageSource(reader, context, state)
                : reader.LocalName == "line" ? ParseLineDefinition(reader, context, state)
                : ParseShapeDefinition(reader, context, state, null, false);
        }
        finally { state.DeferringLeafClip = previousClipDeferral; }
        try
        {
            if (filtered && isDisplayed && SvgPresentationResolver.ParseDisplay(sourceStyle.Display) && SvgPaintRecording.IsFilterSource(reader, context))
                SvgPaintRecording.WrapFilter(definition.Geometries, sourceStyle.Filter, context.ResolveTransform(reader), context.LengthBases, state, context.WorkCounter, context.CancellationToken);
            SvgReferenceResolver.ApplyMask(definition.Geometries, null, null, SvgLocalUrl.ReadReference(reader, "mask"), context.ResolveTransform(reader), state, context.WorkCounter, context.CancellationToken);
            if (clipId is not null)
                SvgClipPathSyntax.ApplyResourceClip(definition.Geometries, clipId, context.ResolveTransform(reader), state, context.WorkCounter, context.CancellationToken);
            if (filtered) SvgPaintRecording.WrapInstance(definition.Geometries, sourceStyle.GetOpacity(), state.NewRecordingScope(), state, context.WorkCounter, context.CancellationToken);
            SvgPaintRecording.FinishDefinition(definition.Geometries, state, context.WorkCounter, context.CancellationToken);
            return BindSymbolFill(definition, reader, context, state, context.WorkCounter, context.CancellationToken, false);
        }
        catch { foreach (SvgGeometry geometry in definition.Geometries) geometry.Dispose(); throw; }

        static SvgDefinition ParseImageSource(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state)
        {
            if (reader is not SvgDefinitionLeafSource source || source.ImageLimits is not {} limits)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Image source replay requires retained resource limits.");
            SvgGeometry image = SvgLeafParser.ParseImage(reader, context, null, context.LengthBases.Width, context.LengthBases.Height, limits, state);
            try
            {
                using BudgetedParserList<SvgGeometry> batch = new(state.Budget, SvgMemoryCategory.Scene, context.WorkCounter, context.CancellationToken);
                batch.Add(image);
                return new(SvgReferenceResolver.RetainInstanceBatch(batch.AsSpan(), state, context.WorkCounter, context.CancellationToken), false, null, null);
            }
            catch { image.Dispose(); throw; }
        }
    }

    private static SvgDefinition BindSymbolFill(SvgDefinition definition, XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state, ParserWorkCounter work, CancellationToken token, bool includesSymbolPresentation)
    {
        if (definition.Geometries.Length == 0) return definition with { SymbolFillBindings = [] };
        // Bind only authored fill properties: container URL injection belongs to lowering,
        // while source aliases must exclude their definition ancestors.
        SvgComputedStyle authored = context.AuthoredStyle ?? SvgComputedStyleResolver.Resolve(reader, context.StyleSheet, work, token);
        SvgComputedStyle style = context.InheritedStroke.ApplyTo(authored);
        SvgSymbolPresentation symbol = includesSymbolPresentation ? (state.SymbolDefinition ?? throw new InvalidOperationException("Symbol fills require a presentation context.")).Presentation : default;
        bool inherited = reader.LocalName != "line" && style.Fill is null && (!includesSymbolPresentation || !symbol.HasAuthoredFill);
        SvgPaintOpacity? opacity = style.FillOpacity is null ? (includesSymbolPresentation ? symbol.FillOpacity : null) : SvgColorParser.ParsePaintOpacity(style.FillOpacity);
        SvgColor? color = style.Color is null ? (includesSymbolPresentation ? symbol.CurrentColor : null) : SvgColorParser.Parse(style.Color, context.NamedColors);
        SvgPaint? authoredFill = !inherited && reader.LocalName != "line" && !SvgPatternSyntax.IsPatternFill(style.Fill)
            ? SvgPaintResolver.Resolve(style.Fill, style.FillOpacity, style.Color, includesSymbolPresentation ? symbol.Paint : SvgPaint.Default, context.NamedColors)
            : null;
        bool isPainted = SvgPresentationResolver.IsPainted(style.Display, style.Visibility, true, true);
        string? stroke = style.Stroke ?? context.InheritedStroke.Stroke;
        SvgPaint? authoredStroke = stroke is not null && !SvgPatternSyntax.IsPatternFill(stroke)
            ? SvgPaintResolver.Resolve(stroke, null, null, SvgPaint.Default, context.NamedColors) : null;
        SvgComputedStyle sourceStyle = context.AuthoredStyle ?? SvgComputedStyleResolver.Resolve(reader, context.StyleSheet, work, token);
        string? strokeOpacity = sourceStyle.StrokeOpacity ?? (includesSymbolPresentation ? symbol.Stroke.StrokeOpacity : null);
        SvgPaintOpacity? authoredStrokeOpacity = strokeOpacity is null ? null : SvgColorParser.ParsePaintOpacity(strokeOpacity);
        RenderBudget.BudgetReservation reservation = state.Budget.Reserve(128 + ParserCollectionMemory.GetArrayByteCount<SvgSymbolFillBinding>(definition.Geometries.Length), SvgMemoryCategory.Scene);
        try
        {
            SvgSymbolFillBinding[] bindings = new SvgSymbolFillBinding[definition.Geometries.Length];
            for (int index = 0; index < bindings.Length; index++)
            {
                work.Consume(token);
                SvgRecordedPaintValue recorded = state.Scene.GetRecordedPaintValue(definition.Geometries[index].RecordedPaint);
                bool strokeChannel = recorded.IsStroke;
                if (recorded.IsMarkerContent)
                {
                    bindings[index] = new(false, null, null) { IsStroke = strokeChannel };
                    continue;
                }
                bool hasStroke = strokeChannel || definition.Geometries[index].RecordedStrokePaint.Identity != 0;
                bindings[index] = new(inherited && !strokeChannel, opacity, color) { AuthoredFill = strokeChannel ? null : authoredFill, AuthoredStroke = hasStroke ? authoredStroke : null, StrokeOpacity = authoredStrokeOpacity, IsStroke = strokeChannel, IsPainted = isPainted, IsPaintServer = !strokeChannel && (SvgPatternSyntax.IsPatternFill(style.Fill) || style.Fill is null && context.InheritedPresentation.PaintServerFill is not null), IsStrokePaintServer = hasStroke && stroke is not null && SvgLocalUrl.TryParseId(stroke, out _) };
            }
            state.DefinitionReservations.Add(reservation);
            return definition with { SymbolFillBindings = bindings };
        }
        catch { reservation.Dispose(); throw; }
    }

    private static SvgDefinition ParseShapeDefinition(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state, SvgViewBox? symbolViewBox, bool excludeDefinitionAncestors)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        // Use clones exclude definition ancestors. Direct scene content keeps its container
        // map; ordinary shape lowerers compose the authored element map exactly once.
        SvgPaintableElementContext localContext = excludeDefinitionAncestors
            ? context with { InheritedPresentation = context.InheritedPresentation with { Transform = SvgAffineTransform.Identity }, RecordingScope = new(1) }
            : context;
        string? pathData = reader.LocalName == "path" ? reader.GetAttribute("d") : null;
        string? definitionTransform = reader.LocalName == "path" ? reader.GetAttribute("transform") : null;
        string? vectorEffect = reader.LocalName == "path" ? reader.GetAttribute("vector-effect") : null;
        if (pathData is not null && style.Stroke is null && style.Width is null && style.StrokeOpacity is null
            && (style.Linejoin is null || style.Linejoin.AsSpan().Trim().Equals("miter".AsSpan(), StringComparison.OrdinalIgnoreCase))
            && (style.Dasharray is null || style.Dasharray.AsSpan().Trim().Equals("none".AsSpan(), StringComparison.OrdinalIgnoreCase))
            && (vectorEffect is null || !SvgStrokeStyleSyntax.ParseVectorEffect(vectorEffect))
            && SvgPathDataParser.IsSingleClosedPath(pathData, context.WorkCounter, context.CancellationToken))
        {
            if (definitionTransform is null) localContext = localContext with { RetainInstanceStrokePath = true };
            else
            {
                SvgAffineTransform elementMap = SvgGeometrySyntax.ParseTransform(definitionTransform, context.WorkCounter, context.CancellationToken);
                if (elementMap.TryGetUniformScale(out double definitionScale) && Math.Abs(definitionScale - 1) <= 1e-9)
                    localContext = localContext with { RetainInstanceStrokePath = true };
            }
        }
        SvgGeometry[] geometries = reader.LocalName switch
        {
            "polygon" or "polyline" => RetainDefinitionGeometries(SvgOrthogonalShapeParser.ParsePointList(reader, reader.LocalName, localContext, int.MaxValue, state.Budget, null, state), state),
            "path" => RetainDefinitionGeometries(SvgOrthogonalShapeParser.ParsePath(reader, localContext, int.MaxValue, state.Budget, null, state), state),
            "circle" => RetainDefinitionGeometries(SvgLeafParser.ParseCircle(reader, localContext, null, state), state),
            "ellipse" => RetainDefinitionGeometries(SvgLeafParser.ParseEllipse(reader, localContext, null, state), state),
            "rect" => RetainDefinitionGeometries(SvgLeafParser.ParseRoundedRectangleDefinition(reader, localContext, state), state),
            _ => throw new InvalidOperationException("Unsupported definition leaf reached shape lowering."),
        };
        try
        {
            if ((reader.LocalName is "circle" or "ellipse") && geometries.Length > 0 && geometries[0].AuthoredPath is null)
            {
                SvgEllipseSyntax oval = reader.LocalName == "circle" ? SvgEllipseSyntax.ParseCircle(reader, context.LengthBases) : SvgEllipseSyntax.ParseEllipse(reader, context.LengthBases);
                if (oval.RadiusX > 0 && oval.RadiusY > 0)
                {
                    float left = (float)oval.CenterX - (float)oval.RadiusX, right = (float)oval.CenterX + (float)oval.RadiusX;
                    float top = (float)oval.CenterY - (float)oval.RadiusY, bottom = (float)oval.CenterY + (float)oval.RadiusY;
                    if (!float.IsFinite(left) || !float.IsFinite(right) || !float.IsFinite(top) || !float.IsFinite(bottom)) throw new SvgException(SvgErrorCode.LimitExceeded, "Oval instance coordinates exceed the finite float range.");
                    float midX = (float)(0.5 * ((double)left + right)), midY = (float)(0.5 * ((double)top + bottom));
                    using BudgetedParserList<SvgAuthoredPathCommand> commands = new(state.Budget, SvgMemoryCategory.Scene, context.WorkCounter, context.CancellationToken);
                    commands.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(right, midY), default, default));
                    commands.Add(new(SvgAuthoredPathVerb.Conic, SvgAuthoredArcFlags.None, new(right, bottom), new(midX, bottom), new(SvgRationalQuadratic.QuarterCircleWeight, 0)));
                    commands.Add(new(SvgAuthoredPathVerb.Conic, SvgAuthoredArcFlags.None, new(left, bottom), new(left, midY), new(SvgRationalQuadratic.QuarterCircleWeight, 0)));
                    commands.Add(new(SvgAuthoredPathVerb.Conic, SvgAuthoredArcFlags.None, new(left, top), new(midX, top), new(SvgRationalQuadratic.QuarterCircleWeight, 0)));
                    commands.Add(new(SvgAuthoredPathVerb.Conic, SvgAuthoredArcFlags.None, new(right, top), new(right, midY), new(SvgRationalQuadratic.QuarterCircleWeight, 0)));
                    commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
                    OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
                    try { geometries[0] = geometries[0] with { AuthoredPath = SvgAuthoredPath.CreateConvexShape(owned, localContext.ResolveTransform(reader), state.Budget) }; }
                    catch { owned.Dispose(); throw; }
                }
            }
            return new SvgDefinition(geometries, IsOverrideableFill(style, geometries), ResolveInstanceStrokeMiterLimit(style, geometries, pathData, definitionTransform, vectorEffect, context.WorkCounter, context.CancellationToken), symbolViewBox);
        }
        catch
        {
            foreach (SvgGeometry geometry in geometries) geometry.Dispose();
            throw;
        }

        static double? ResolveInstanceStrokeMiterLimit(SvgComputedStyle computed, SvgGeometry[] lowered, string? pathData, string? transformAttribute, string? vectorEffect, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            if (lowered.Length != 1 || lowered[0].Kind != SvgGeometryKind.PolygonFill) return null;
            if (lowered[0].Polygon.ContourEnds.Length != 1) return null;
            if (computed.Stroke is not null || computed.Width is not null || computed.StrokeOpacity is not null) return null;
            if (computed.Linejoin is not null && !computed.Linejoin.AsSpan().Trim().Equals("miter".AsSpan(), StringComparison.OrdinalIgnoreCase)) return null;
            if (computed.Dasharray is not null && !computed.Dasharray.AsSpan().Trim().Equals("none".AsSpan(), StringComparison.OrdinalIgnoreCase)) return null;
            if (vectorEffect is not null && SvgStrokeStyleSyntax.ParseVectorEffect(vectorEffect)) return null;
            if (transformAttribute is not null)
            {
                SvgAffineTransform elementMap = SvgGeometrySyntax.ParseTransform(transformAttribute, workCounter, cancellationToken);
                if (!elementMap.TryGetUniformScale(out double definitionScale) || Math.Abs(definitionScale - 1) > 1e-9) return null;
            }
            if (!SvgPathDataParser.IsSingleClosedPath(pathData, workCounter, cancellationToken)) return null;
            return SvgStrokeStyleSyntax.ParseMiterLimit(computed.Miterlimit);
        }

        static SvgGeometry[] RetainDefinitionGeometries(SvgParsedGeometry parsed, SvgDocumentState state)
        {
            try
            {
                if (parsed.Geometries.Length == 0) return [];
                RenderBudget.BudgetReservation reservation = state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(parsed.Geometries.Length), SvgMemoryCategory.Scene);
                try
                {
                    SvgGeometry[] retained = parsed.Geometries.ToArray();
                    state.DefinitionReservations.Add(reservation);
                    return retained;
                }
                catch { reservation.Dispose(); throw; }
            }
            catch { parsed.Discard(); throw; }
            finally { parsed.Dispose(); }
        }

        static bool IsOverrideableFill(SvgComputedStyle computed, SvgGeometry[] lowered)
        {
            if (computed.Color is not null || computed.Fill is not null || computed.FillOpacity is not null || computed.Stroke is not null || computed.Display is not null || computed.Visibility is not null || computed.Opacity is not null || computed.FillRule is not null || lowered.Length != 1) return false;
            return lowered[0].Kind switch
            {
                SvgGeometryKind.OrthogonalBand => lowered[0].Rectangle.HasFill,
                SvgGeometryKind.PolygonFill => lowered[0].Polygon.HasFill,
                _ => false,
            };
        }
    }

    private static SvgGeometry[] RetainParsedGeometries(SvgParsedGeometry parsed, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        try
        {
            if (parsed.Geometries.Length == 0) return [];
            RenderBudget.BudgetReservation reservation = state.Budget.Reserve(128 + ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(parsed.Geometries.Length), SvgMemoryCategory.Scene);
            try
            {
                SvgGeometry[] retained = new SvgGeometry[parsed.Geometries.Length];
                for (int index = 0; index < retained.Length; index++)
                {
                    work.Consume(token);
                    retained[index] = parsed.Geometries[index];
                }
                state.DefinitionReservations.Add(reservation);
                return retained;
            }
            catch { reservation.Dispose(); throw; }
        }
        catch { parsed.Discard(); throw; }
        finally { parsed.Dispose(); }
    }

    private static SvgDefinition ParseRectangleDefinition(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state, SvgViewBox? symbolViewBox)
    {
        SvgRectangleSyntax roundedSyntax = SvgRectangleSyntax.Parse(reader, context.LengthBases);
        if ((roundedSyntax.CornerX ?? roundedSyntax.CornerY ?? 0) != 0 || (roundedSyntax.CornerY ?? roundedSyntax.CornerX ?? 0) != 0) return ParseShapeDefinition(reader, context, state, symbolViewBox, true);
        SvgComputedStyle style = context.ResolveStyle(reader);
        // Named live leaves also register a dormant source. Its authored map
        // needs the ordinary retained shape route before any use is encountered.
        bool nativeRectangle = context.RetainInstanceStrokePath
            || !SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform"), context.WorkCounter, context.CancellationToken).IsAxisAligned
            || SvgStrokeStyleSyntax.ParseLinejoin(style.Linejoin, true) != SvgStrokeJoin.Miter
            || SvgStrokeStyleSyntax.ParseDasharray(style.Dasharray) is not null || style.NonScalingStroke;
        if (!nativeRectangle && style.Stroke is string nativeStroke && !nativeStroke.AsSpan().Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            double width = SvgStrokeStyleSyntax.ParseStrokeWidth(style.Width ?? roundedSyntax.StrokeWidth, "rectangle");
            nativeRectangle = width > roundedSyntax.Width || width > roundedSyntax.Height
                || style.Miterlimit is not null && SvgStrokeStyleSyntax.ParseMiterLimit(style.Miterlimit) < 1.4142135623730951;
        }
        if (nativeRectangle)
            return ParseShapeDefinition(reader, context, state, symbolViewBox, true);
        SvgStrokeStyleSyntax.RequireButtCapJoin(reader, "rectangle", context.WorkCounter, context.CancellationToken);
        context = context with { InheritedPresentation = context.InheritedPresentation with { Transform = SvgAffineTransform.Identity }, RecordingScope = new(1) };
        SvgRecordedPaintStyle source = SvgPaintRecording.Resolve(reader, context, state, null);
        context = context with { DeferPaintOpacity = true };
        SvgParsedGeometry geometry;
        bool allowsOverride = false;
        if (SvgPatternSyntax.IsPatternFill(style.Fill)) geometry = SvgLeafParser.ParseRectangleCore(reader, context, null, state);
        else if (SvgPatternSyntax.IsPatternStroke(style.Stroke, state)) geometry = SvgPatternSyntax.LowerPatternStrokeRect(reader, context, style, state, null);
        else if (SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke)) geometry = SvgGradientSyntax.LowerGradientStrokeRect(reader, context, style, state, null);
        else
        {
            SvgParsedRectangle parsed = SvgLeafParser.ParseRectangleWithStyle(reader, context);
            allowsOverride = parsed.Geometry.Rectangle.HasFill && parsed.Style == default;
            using BudgetedGeometryBuilder retained = new(state.Budget, SvgMemoryCategory.Geometry, context.WorkCounter, context.CancellationToken);
            retained.Add(parsed.Geometry);
            geometry = new(retained.Detach(), 0);
        }
        SvgParsedGeometry attached = SvgPaintRecording.Attach(geometry, source, reader.LocalName, state, context.WorkCounter, context.CancellationToken);
        return new SvgDefinition(RetainParsedGeometries(attached, state, context.WorkCounter, context.CancellationToken), allowsOverride, null, symbolViewBox);
    }
}
