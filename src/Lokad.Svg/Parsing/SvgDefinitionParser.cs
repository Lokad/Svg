using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Routes bounded definitions, symbols and local instances while preserving streaming order.</summary>
internal static class SvgDefinitionParser
{
    internal static bool TryParse(XmlReader reader, bool directChild, bool isEligible, SvgRootAttributes root, SvgDocumentState state, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (state.HasPatternTemplateReferences && state.PatternDepth < 0 && reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName))
            SvgPatternReferenceResolver.Resolve(state, false, workCounter, cancellationToken);
        bool symbolLocation = state.SymbolDepth >= 0 && state.SymbolDefinition is {} activeSymbol && reader.Depth == activeSymbol.ChildDepth(state.SymbolDepth);
        if (symbolLocation && reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgSymbolContentParser.TryParse(reader, root, state, namedColors, workCounter, cancellationToken)) return true;
        bool inDefinitions = (state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1) || symbolLocation;
        bool rootDefinition = state.DefinitionsDepth < 0 && state.SymbolDepth < 0 && directChild && reader.GetAttribute("id") is not null;
        bool groupNestedDefinition = state.DefinitionsDepth < 0 && state.SymbolDepth < 0 && state.PatternId is null && state.MaskId is null && state.ClipPathId is null && state.GroupDepth > 0 && reader.GetAttribute("id") is not null;
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
            else SvgElementAttributePolicy.ValidatePointList(reader, reader.LocalName, workCounter, cancellationToken);
            SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, directChild, root, state, namedColors, workCounter, cancellationToken);
            if (symbolChild)
            {
                SvgSymbolPresentation symbol = (state.SymbolDefinition ?? throw new InvalidOperationException("Symbol children require a presentation context.")).Presentation;
                context = context with { InheritedPresentation = new(symbol.Paint, true, true, SvgAffineTransform.Identity) { Markers = symbol.Markers, PaintOrder = symbol.PaintOrder ?? SvgPaintOrder.FillStrokeMarkers, PaintServerFill = symbol.PaintServerFill }, InheritedStroke = symbol.Stroke, InheritedFillRule = symbol.FillRule, RecordingScope = new(1) };
            }
            SvgComputedStyle retainedStyle = SvgComputedStyleResolver.Resolve(reader, context.StyleSheet, workCounter, cancellationToken);
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
            SvgDefinition definition = reader.LocalName == "rect" ? ParseRectangleDefinition(reader, dormant, state, symbolViewBox)
                : reader.LocalName == "line" ? ParseLineDefinition(reader, dormant, state)
                : ParseShapeDefinition(reader, dormant, state, symbolViewBox);
            bool retained = false;
            try
            {
                SvgPaintRecording.FinishDefinition(definition.Geometries, state, workCounter, cancellationToken);
                SvgDefinition source = BindSymbolFill(definition, reader, context, state, workCounter, cancellationToken, false);
                SvgComputedStyle sourceStyle = SvgComputedStyleResolver.Resolve(reader, context.StyleSheet, workCounter, cancellationToken);
                definition = definition with { SourceFillBindings = source.SymbolFillBindings, SourceDisplayed = SvgPresentationResolver.ParseDisplay(sourceStyle.Display), SourceVisible = sourceStyle.Visibility is null ? null : SvgPresentationResolver.ParseVisibility(sourceStyle.Visibility, true), LeafSource = SvgDefinitionLeafSource.Capture(reader, context, state) };
                if (symbolChild)
                {
                    definition = BindSymbolFill(definition, reader, context, state, workCounter, cancellationToken, true);
                    SvgSymbolDefinitionBuilder builder = state.SymbolDefinition ?? throw new InvalidOperationException("Symbol children require an open definition builder.");
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
            state.HasUnsupportedContent |= !reader.IsEmptyElement;
            state.SawStyleableNode = true;
            return inDefinitions;
        }
        if (reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName is ("symbol" or "marker")
            && SvgStructuralVocabulary.IsSymbolDefinitionLocation(directChild, isEligible && state.GroupDepth > 0, state.DefinitionsDepth, state.SymbolDepth, reader.Depth))
        {
            bool marker = reader.LocalName == "marker";
            state.MarkerDefinition = marker ? SvgMarkerSyntax.Parse(reader, new(state.EffectiveFrame?.ViewportWidth ?? root.UserViewportWidth, state.EffectiveFrame?.ViewportHeight ?? root.UserViewportHeight), workCounter, cancellationToken) : null;
            if (!marker) SvgElementAttributePolicy.ValidateSymbol(reader, workCounter, cancellationToken);
            InlineStyle sheet = SvgDocumentStyleResolver.Resolve(reader, directChild, state, workCounter, cancellationToken);
            SvgPaintableElementContext markerContext = marker ? SvgPaintableElementContext.Create(reader, directChild, root, state, namedColors, workCounter, cancellationToken) : default;
            SvgComputedStyle style = marker ? markerContext.ResolveStyle(reader) : SvgComputedStyleResolver.Resolve(reader, sheet, workCounter, cancellationToken);
            SvgPaint inheritedPaint = marker ? markerContext.InheritedPresentation.Paint : SvgPaint.Default;
            if (style.Display is not null || style.Visibility is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Display and visibility on symbol containers are not implemented.");
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
                marker ? paint.FillOpacity : style.FillOpacity is null ? null : SvgColorParser.ParsePaintOpacity(style.FillOpacity), marker ? paint.CurrentColor : style.Color is null ? null : SvgColorParser.Parse(style.Color, namedColors), SvgColorParser.ParsePaintOpacity(style.Opacity)) { Markers = new SvgMarkerStyle(style.MarkerStart, style.MarkerMid, style.MarkerEnd).Retain(state, workCounter, cancellationToken), PaintOrder = style.PaintOrder, PaintServerFill = shaderFill, PaintServerId = shaderId };
            string? id = reader.GetAttribute("id");
            if (id is not null && (state.Definitions.TryGetValue(id, out _) || state.GroupSnapshots.TryGetValue(id, out _) || state.RenderedGroupDefinitions.TryGetValue(id, out _))) throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
            state.SymbolId = id is null ? null : state.Strings.Copy(id);
            state.SymbolViewBox = SvgGeometrySyntax.ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio"), workCounter, cancellationToken);
            state.SymbolDefinition = SvgSymbolDefinitionBuilder.Create(state.Budget, workCounter, cancellationToken, presentation);
            if (shaderFill is not null || marker || presentation.Markers.HasMarkers)
            {
                state.SymbolDefinition.EnableContent();
                state.HasRetainedSymbolContent = true;
            }
            state.SawStyleableNode = true;
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
            state.ClipPolygon = null;
            state.ClipIsObjectBoundingBox = SvgClipPathSyntax.ParseClipUnits(reader.GetAttribute("clipPathUnits"));
            SvgPaintableElementContext clipParent = SvgPaintableElementContext.Create(reader, directChild, root, state, namedColors, workCounter, cancellationToken);
            SvgComputedStyle clipParentStyle = SvgComputedStyleResolver.Resolve(reader, clipParent.StyleSheet, workCounter, cancellationToken);
            state.ClipIsEvenOdd = SvgClipPathSyntax.ParseClipRule((clipParentStyle.ClipRule ?? clipParent.InheritedClipRule ?? "nonzero").AsSpan());
            if (reader.IsEmptyElement)
            {
                if (state.ClipPathId is not null) RegisterClipBox(state, workCounter, cancellationToken);
            }
            else state.ClipPathDepth = reader.Depth;
            state.SawStyleableNode = true;
            return true;
        }
        if (state.ClipPathDepth >= 0 && reader.Depth == state.ClipPathDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg")
        {
            if (state.ClipUse is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-element clip content is implemented.");
            if (reader.LocalName == "use")
            {
                if (state.ClipIsObjectBoundingBox) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse referenced clip content is implemented.");
                if (state.ClipBox is not null || state.ClipPolygon is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-element clip content is implemented.");
                state.ClipUse = SvgResourceUseReplay.Retain(reader, root, state, namedColors, workCounter, cancellationToken);
                return true;
            }
            if (reader.LocalName == "rect")
            {
                if (state.ClipBox is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-rectangle clip paths are implemented.");
                SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken);
                SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, false, root, state, namedColors, workCounter, cancellationToken);
                SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader, state.ClipIsObjectBoundingBox ? new(1, 1) : context.LengthBases);
                // A resource uses its own child map; ancestors place the referencing element.
                SvgAffineTransform clipTransform = SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform"), workCounter, cancellationToken);
                if (state.ClipIsObjectBoundingBox && clipTransform != SvgAffineTransform.Identity) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Transformed objectBoundingBox clip content is not implemented.");
                SvgRectangle rect = clipTransform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, false, null));
                state.ClipBox = new SvgClipBox(rect.X, rect.Y, rect.Width, rect.Height);
                state.HasUnsupportedContent |= !reader.IsEmptyElement;
                state.SawStyleableNode = true;
                return true;
            }
            if (reader.LocalName == "path")
            {
                if (state.ClipBox is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-rectangle clip paths are implemented.");
                SvgElementAttributePolicy.ValidatePath(reader, workCounter, cancellationToken);
                if (state.ClipIsObjectBoundingBox) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse clip-path paths are implemented.");
                SvgPaintableElementContext clipContext = SvgPaintableElementContext.Create(reader, false, root, state, namedColors, workCounter, cancellationToken);
                // A resource uses its own child map; ancestors place the referencing element.
                SvgAffineTransform clipTransform = SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform"), workCounter, cancellationToken);
                SvgComputedStyle clipStyle = SvgComputedStyleResolver.Resolve(reader, clipContext.StyleSheet, workCounter, cancellationToken);
                bool isEvenOdd = clipStyle.ClipRule is null ? state.ClipIsEvenOdd : SvgClipPathSyntax.ParseClipRule(clipStyle.ClipRule.AsSpan());
                SvgClipPolygon clipContent = ParseClipPathPolygon(reader.GetAttribute("d"), clipTransform, isEvenOdd, state, workCounter, cancellationToken);
                if (state.ClipPolygon is {} first)
                {
                    SvgClipPolygon combined;
                    try { combined = CombineClipChildren(first, clipContent, state, workCounter, cancellationToken); }
                    finally { clipContent.SourcePath?.Dispose(); }
                    state.ClipPolygon = combined;
                    first.SourcePath?.Dispose();
                    clipContent = combined;
                }
                else state.ClipPolygon = clipContent;
                // Publish the source owner before any cancellable classification.
                if (!clipContent.UsesNativeReplay && clipContent.ContourEnds.Length == 1 && TryGetAxisClipBox(clipContent.Points, workCounter, cancellationToken, out SvgClipBox clipRect))
                {
                    state.ClipBox = clipRect;
                    clipContent.SourcePath?.Dispose();
                    state.ClipPolygon = null;
                }
                state.HasUnsupportedContent |= !reader.IsEmptyElement;
                state.SawStyleableNode = true;
                return true;
            }
            if (reader.LocalName is "circle" or "ellipse")
            {
                if (state.ClipBox is not null || state.ClipPolygon is not null)
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-element curved clip content is implemented.");
                if (state.ClipIsObjectBoundingBox)
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse oval clip content is implemented.");
                SvgElementAttributePolicy.ValidateCircle(reader, reader.LocalName, workCounter, cancellationToken);
                SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, false, root, state, namedColors, workCounter, cancellationToken);
                SvgEllipseSyntax oval = reader.LocalName == "circle" ? SvgEllipseSyntax.ParseCircle(reader, context.LengthBases) : SvgEllipseSyntax.ParseEllipse(reader, context.LengthBases);
                SvgAffineTransform childMap = SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform"), workCounter, cancellationToken);
                using BudgetedParserList<SvgAuthoredPathCommand> commands = new(state.Budget, SvgMemoryCategory.Scene, workCounter, cancellationToken);
                if (!SvgOrthogonalGeometryLowering.AppendNativeEllipseCommands(commands, oval.CenterX, oval.CenterY, oval.RadiusX, oval.RadiusY))
                    state.ClipBox = new(0, 0, 0, 0);
                else
                {
                    OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
                    SvgAuthoredPath source;
                    try { source = SvgAuthoredPath.CreateConvexShape(owned, childMap, state.Budget); }
                    catch { owned.Dispose(); throw; }
                    state.ClipPolygon = new([], []) { SourcePath = source, UsesNativeReplay = true, IsOval = childMap.IsAxisAligned };
                    using SvgPreparedPathContours prepared = source.Prepare(SvgAffineTransform.Identity, false, state.Budget, workCounter, cancellationToken);
                    state.DefinitionReservations.Add(state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(prepared.Points.Length)
                        + ParserCollectionMemory.GetArrayByteCount<int>(prepared.ContourEnds.Length), SvgMemoryCategory.Scene));
                    SvgDevicePoint[] points = new SvgDevicePoint[prepared.Points.Length];
                    int[] ends = new int[prepared.ContourEnds.Length];
                    for (int index = 0; index < points.Length; index++) { workCounter.Consume(cancellationToken); points[index] = prepared.Points[index]; }
                    for (int index = 0; index < ends.Length; index++) { workCounter.Consume(cancellationToken); ends[index] = prepared.ContourEnds[index]; }
                    state.ClipPolygon = new(points, ends) { SourcePath = source, UsesNativeReplay = true, IsOval = childMap.IsAxisAligned };
                }
                state.HasUnsupportedContent |= !reader.IsEmptyElement;
                state.SawStyleableNode = true;
                return true;
            }
            state.HasUnsupportedContent = true;
            return true;
        }
        if ((state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 || directChild) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "mask")
        {
            SvgElementAttributePolicy.ValidateMask(reader, workCounter, cancellationToken);
            SvgPaintableElementContext maskContext = SvgPaintableElementContext.Create(reader, directChild, root, state, namedColors, workCounter, cancellationToken);
            InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
            SvgComputedStyle style = SvgComputedStyle.Resolve(reader, inline, maskContext.StyleSheet);
            if (SvgColorParser.ParsePaintOpacity(style.Opacity).Value != 1)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Opacity on mask containers is not implemented.");
            SvgInheritedContainer inherited = SvgContainerInheritance.Resolve(reader, inline, style, maskContext.StyleSheet,
                maskContext.InheritedPresentation, maskContext.InheritedFillRule, maskContext.InheritedTextStyle,
                maskContext.InheritedStroke, false, namedColors, workCounter, cancellationToken);
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
            state.MaskIsAlpha = SvgMaskSyntax.ParseMaskType(reader.GetAttribute("mask-type"));
            state.MaskValue = null;
            state.MaskContentBox = null;
            state.MaskFractionRegion = SvgMaskSyntax.ParseMaskFractionRegion(reader);
            state.MaskCustomRegion = state.MaskFractionRegion is null ? SvgMaskSyntax.ParseMaskRegion(reader) : null;
            state.MaskDefaultUserSpaceRegion = reader.GetAttribute("maskUnits") == "userSpaceOnUse"
                && reader.GetAttribute("x") is null && reader.GetAttribute("y") is null && reader.GetAttribute("width") is null && reader.GetAttribute("height") is null;
            if (state.MaskDefaultUserSpaceRegion)
            {
                double? width = state.EffectiveFrame?.ViewportWidth ?? root.UserViewportWidth;
                double? height = state.EffectiveFrame?.ViewportHeight ?? root.UserViewportHeight;
                if (width is null || height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Default user-space mask regions require a known viewport.");
                state.MaskCustomRegion = SvgMaskSyntax.MaskRegion(new(0, 0, width.Value, height.Value, default, false, null));
            }
            if (reader.IsEmptyElement)
            {
                if (state.MaskId is not null) RegisterMask(state, workCounter, cancellationToken);
                state.MaskId = null;
                state.MaskCustomRegion = null;
                state.MaskFractionRegion = null;
                state.MaskDefaultUserSpaceRegion = false;
            }
            else
            {
                // Resource children inherit the same bounded presentation scope as
                // ordinary containers, without creating a rendered group layer.
                state.MaskDepth = reader.Depth;
                state.Groups.Add(scope);
            }
            state.SawStyleableNode = true;
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
                SvgPaintableElementContext maskContext = SvgPaintableElementContext.Create(reader, false, root, state, namedColors, workCounter, cancellationToken);
                if (SvgGradientSyntax.IsGradientFill(maskContext.ResolveStyle(reader).Fill))
                {
                    if (state.MaskValue is not null || state.MaskContentGeometries is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-element mask content is implemented.");
                    if (reader.GetAttribute("mask") is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Masks on mask content are not implemented.");
                    if (reader.GetAttribute("clip-path") is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths on mask content are not implemented.");
                    SvgDefinition lowered = LowerInstanceSource(reader, maskContext, state);
                    try
                    {
                        state.MaskContentBox = UnionMaskContentBox(lowered.Geometries, workCounter, cancellationToken);
                        SvgSceneBuilder builder = state.MaskContentBuilder = new(state.Budget, workCounter, cancellationToken);
                        SvgPaintRecording.AdoptLeafScene(lowered, builder, state);
                    }
                    catch { foreach (SvgGeometry geometry in lowered.Geometries) geometry.Dispose(); throw; }
                    state.HasUnsupportedContent |= !reader.IsEmptyElement;
                    state.SawStyleableNode = true;
                    return true;
                }
            }
            if (reader.LocalName == "rect")
            {
                if (state.MaskValue is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-rectangle masks are implemented.");
                SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken);
                SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, false, root, state, namedColors, workCounter, cancellationToken);
                SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader, context.LengthBases);
                SvgResolvedPaintableElement element = context.ResolveShape(reader, false);
                if (!element.Presentation.IsPainted) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Invisible mask content is not implemented.");
                state.MaskValue = SvgMaskSyntax.InterpretValue(element.Presentation.Color, state.MaskIsAlpha);
                state.MaskContentBox = element.Transform.Apply(new SvgClipBox(syntax.X, syntax.Y, syntax.Width, syntax.Height));
                state.HasUnsupportedContent |= !reader.IsEmptyElement;
                state.SawStyleableNode = true;
                return true;
            }
            if (reader.LocalName == "circle")
            {
                if (state.MaskValue is not null || state.MaskContentGeometries is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-element mask content is implemented.");
                SvgElementAttributePolicy.ValidateCircle(reader, reader.LocalName, workCounter, cancellationToken);
                if (reader.GetAttribute("clip-path") is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths on mask content are not implemented.");
                if (reader.GetAttribute("mask") is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Masks on mask content are not implemented.");
                SvgPaintableElementContext contentContext = SvgPaintableElementContext.Create(reader, false, root, state, namedColors, workCounter, cancellationToken);
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
                state.HasUnsupportedContent |= !reader.IsEmptyElement;
                state.SawStyleableNode = true;
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
            if (state.PatternTemplate.Href is null) state.PatternTemplate.ValidateUnits();
            else state.HasPatternTemplateReferences = true;
            state.PatternTile = state.PatternTemplate.Grid;
            SvgPaintableElementContext patternContext = SvgPaintableElementContext.Create(reader, directChild, root, state, namedColors, workCounter, cancellationToken);
            InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
            SvgComputedStyle style = SvgComputedStyle.Resolve(reader, inline, patternContext.StyleSheet);
            if (SvgColorParser.ParsePaintOpacity(style.Opacity).Value != 1)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Opacity on pattern containers is not implemented.");
            SvgInheritedContainer inherited = SvgContainerInheritance.Resolve(reader, inline, style, patternContext.StyleSheet,
                patternContext.InheritedPresentation, patternContext.InheritedFillRule, patternContext.InheritedTextStyle,
                patternContext.InheritedStroke, false, namedColors, workCounter, cancellationToken);
            if (style.Fill is not null && inherited.Presentation.PaintServerFill is string fill)
            {
                workCounter.ConsumeText(fill.Length, cancellationToken);
                inherited = inherited with { Presentation = inherited.Presentation with { PaintServerFill = state.Strings.Copy(fill) } };
            }
            state.Groups.Add(new(reader.Depth, SvgGroupKind.Pattern, inherited.Presentation, inherited.TextStyle, false, false, inherited.Stroke, inherited.FillRule)
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
            state.SawStyleableNode = true;
            return true;
        }
        if (state.PatternDepth >= 0 && reader.Depth == state.PatternDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg")
        {
            if (reader.LocalName == "rect" && !SvgPaintableElementContext.Create(reader, false, root, state, namedColors, workCounter, cancellationToken).ResolveStyle(reader).PaintOrder.GetValueOrDefault().StrokeFirst() && state.PatternTemplate.KeepsLegacyRectangle && state.PatternContentBuilder is null && state.PatternTile?.Content is null)
            {
                SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken);
                if (reader.GetAttribute("mask") is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Masks on pattern tile content are not implemented.");
                SvgPaintableElementContext tileContext = SvgPaintableElementContext.Create(reader, false, root, state, namedColors, workCounter, cancellationToken);
                tileContext = tileContext with { InheritedPresentation = tileContext.InheritedPresentation with { Transform = SvgAffineTransform.Identity } };
                // The tile payload retains a resolved rectangle, so its paint must be resolved before storing it.
                using SvgParsedGeometry parsedContent = SvgLeafParser.ParseRectangleCore(reader, tileContext, null, state);
                try
                {
                    if (parsedContent.Geometries.Length != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-rectangle pattern tiles are implemented.");
                    SvgGeometry content = parsedContent.Geometries[0];
                    SvgPatternTile tile = state.PatternTile ?? throw new InvalidOperationException("Pattern tile accumulation requires pattern dimensions.");
                    SvgGeometry validated = RequirePatternTileContent(content, tile);
                    SvgRectangle shifted = validated.Rectangle with { X = validated.Rectangle.X + tile.ShiftX, Y = validated.Rectangle.Y + tile.ShiftY };
                    if (shifted.X < tile.OriginX || shifted.Y < tile.OriginY || shifted.X + shifted.Width > tile.OriginX + tile.TileWidth || shifted.Y + shifted.Height > tile.OriginY + tile.TileHeight) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern tile content shifted outside its tile is not implemented.");
                    if (shifted.Stroke is SvgStroke contentStroke && (shifted.X - contentStroke.Width / 2d < tile.OriginX || shifted.Y - contentStroke.Width / 2d < tile.OriginY || shifted.X + shifted.Width + contentStroke.Width / 2d > tile.OriginX + tile.TileWidth || shifted.Y + shifted.Height + contentStroke.Width / 2d > tile.OriginY + tile.TileHeight)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern tile content strokes overflowing their tile are not implemented.");
                    state.PatternTile = tile with { Content = SvgGeometry.FromRectangle(shifted) with { SolidPaint = validated.SolidPaint } };
                    // Preserve authored attributes for promotion; resolved rectangle bytes remain the
                    // legacy one-leaf fast path, while vector scenes need native opacity/stroke recording.
                    state.PatternRectangleSource = SvgDefinitionLeafSource.Capture(reader, tileContext, state);
                    state.PatternRectangleContext = tileContext;
                    state.HasUnsupportedContent |= !reader.IsEmptyElement;
                    state.SawStyleableNode = true;
                    return true;
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
                if (reader.GetAttribute("mask") is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Masks on pattern tile content are not implemented.");
                SvgPatternTile tile = state.PatternTile ?? throw new InvalidOperationException("Vector pattern content requires a tile grid.");
                SvgSceneBuilder builder = state.PatternContentBuilder ??= new(state.Budget, workCounter, cancellationToken);
                if (tile.Content is not null)
                {
                    SvgDefinitionLeafSource first = state.PatternRectangleSource ?? throw new InvalidOperationException("Rectangle promotion requires retained source attributes.");
                    SvgPaintableElementContext previous = state.PatternRectangleContext;
                    AdoptLeaf(first.Lower(previous.InheritedStroke, previous.InheritedPresentation.Paint, previous.InheritedFillRule, previous.InheritedPresentation.PaintServerFill, previous.InheritedPresentation.PaintOrder, previous.InheritedPresentation.Markers, false, state));
                    state.PatternTile = tile with { Content = null };
                }
                SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, false, root, state, namedColors, workCounter, cancellationToken);
                context = context with { InheritedPresentation = context.InheritedPresentation with { Transform = SvgAffineTransform.Identity } };
                SvgComputedStyle style = context.ResolveStyle(reader);
                if (style.Fill is {} fill && SvgLocalUrl.TryParseId(fill, out string fillId) && (fillId == state.PatternId || state.Patterns.TryGetValue(fillId, out _))
                    || style.Stroke is {} stroke && SvgLocalUrl.TryParseId(stroke, out string strokeId) && (strokeId == state.PatternId || state.Patterns.TryGetValue(strokeId, out _)))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern paints inside pattern content are not implemented.");
                AdoptLeaf(LowerInstanceSource(reader, context, state));
                state.HasUnsupportedContent |= !reader.IsEmptyElement;
                state.SawStyleableNode = true;
                return true;

                void AdoptLeaf(SvgDefinition lowered) => SvgPaintRecording.AdoptLeafScene(lowered, builder, state);
            }
            state.HasUnsupportedContent = true;
            return true;
        }
        if ((state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 || directChild || resourceDefinition) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "radialGradient")
        {
            SvgElementAttributePolicy.ValidateRadialGradient(reader, workCounter, cancellationToken);
            state.RadialGradientId = reader.GetAttribute("id");
            SvgRadialTemplate template = SvgGradientSyntax.ParseRadialVector(reader, workCounter, cancellationToken);
            state.RadialGradient = template;
            if (!reader.IsEmptyElement) state.RadialGradientDepth = reader.Depth;
            else if (state.RadialGradientId is not null) RegisterRadialGradient(state, workCounter, cancellationToken);
            state.GradientStops.Clear();
            state.SawStyleableNode = true;
            return true;
        }
        if ((state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 || directChild || resourceDefinition) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "linearGradient")
        {
            SvgElementAttributePolicy.ValidateLinearGradient(reader, workCounter, cancellationToken);
            state.GradientId = reader.GetAttribute("id");
            SvgGradientTemplate template = SvgGradientSyntax.ParseLinearVector(reader, workCounter, cancellationToken);
            state.Gradient = template;
            if (!reader.IsEmptyElement) state.GradientDepth = reader.Depth;
            else if (state.GradientId is not null) RegisterGradient(state, workCounter, cancellationToken);
            state.GradientStops.Clear();
            state.SawStyleableNode = true;
            return true;
        }
        if (((state.GradientDepth >= 0 && reader.Depth == state.GradientDepth + 1) || (state.RadialGradientDepth >= 0 && reader.Depth == state.RadialGradientDepth + 1)) && reader.NamespaceURI == "http://www.w3.org/2000/svg")
        {
            if (reader.LocalName == "stop")
            {
                if (state.GradientStops.Count >= SvgGradientSyntax.MaximumGradientStops) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only gradients with at most eight stops are implemented.");
                SvgElementAttributePolicy.ValidateGradientStop(reader, workCounter, cancellationToken);
                state.GradientStops.Add(SvgGradientSyntax.ParseStop(reader, namedColors, workCounter, cancellationToken));
                state.HasUnsupportedContent |= !reader.IsEmptyElement;
                state.SawStyleableNode = true;
                return true;
            }
            state.HasUnsupportedContent = true;
            return true;
        }
        if (state.DefinitionsDepth < 0 && (directChild || state.GroupDepth > 0) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsDefinitionsContainer(reader.LocalName))
        {
            SvgElementAttributePolicy.ValidateDefinitionContainer(reader, workCounter, cancellationToken);
            if (!reader.IsEmptyElement) state.DefinitionsDepth = reader.Depth;
            return true;
        }
        if (state.DefinitionsDepth < 0 && (directChild || isEligible) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "use")
        {
            SvgElementAttributePolicy.ValidateUse(reader, workCounter, cancellationToken);
            SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, directChild, root, state, namedColors, workCounter, cancellationToken);
            SvgViewportFrame? parentFrame = state.EffectiveFrame;
            double? baseWidth = parentFrame?.ViewportWidth ?? root.UserViewportWidth;
            double? baseHeight = parentFrame?.ViewportHeight ?? root.UserViewportHeight;
            SvgUseReference reference = SvgLocalReferenceParser.ParseUse(reader, context, state.Strings, baseWidth, baseHeight, state);
            SvgRenderedGroupSourceParser.CaptureUse(reader, context, reference, state);
            SvgDeferredClip? deferredClip = ResolveDeferredClip(reader, reference, state);
            SvgViewportFrame? useFrame;
            if (deferredClip is null)
            {
                useFrame = ResolveUseClipFrame(reader, reference, state);
                if (useFrame is not null && useFrame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Use instances in scaled viewports are not implemented.");
            }
            else useFrame = deferredClip.Value.Parent;
            string? maskAttribute = reader.GetAttribute("mask");
            SvgReferenceResolver.ResolveOrDefer(reference, state.Definitions, state.PendingUses, state.Scene, useFrame, ResolveDeferredMaskValue(reader, state), maskAttribute is null ? null : state.Strings.Copy(maskAttribute), deferredClip, state, workCounter, cancellationToken);
            state.HasUnsupportedContent |= !reader.IsEmptyElement;
            state.SawStyleableNode = true;
            return true;
        }
        return false;

        static string? CopyStyleValue(string? value, SvgDocumentState document) => value is null ? null : document.Strings.Copy(value);

        static SvgDeferredClip? ResolveDeferredClip(XmlReader useReader, SvgUseReference useReference, SvgDocumentState document)
        {
            string? clipAttribute = useReader.GetAttribute("clip-path");
            if (clipAttribute is null) return null;
            string clipId = SvgClipPathSyntax.ParseClipId(clipAttribute);
            bool clipKnown = document.Clips.TryGetValue(clipId, out _) || document.ObjectClips.TryGetValue(clipId, out _);
            bool definitionKnown = document.Definitions.TryGetValue(useReference.Id, out _);
            if (definitionKnown && clipKnown) return null;
            if (!clipKnown) return new SvgDeferredClip(document.Strings.Copy(clipId), document.EffectiveFrame, useReader.Depth);
            if (document.ObjectClips.TryGetValue(clipId, out _)) return new SvgDeferredClip(document.Strings.Copy(clipId), document.EffectiveFrame, useReader.Depth);
            return null;
        }

        static byte? ResolveDeferredMaskValue(XmlReader useReader, SvgDocumentState document)
        {
            string? maskAttribute = useReader.GetAttribute("mask");
            if (maskAttribute is null) return null;
            if (document.Masks.TryGetValue(SvgMaskSyntax.ParseMaskId(maskAttribute), out byte value)) return value;
            return null;
        }

        static SvgViewportFrame? ResolveUseClipFrame(XmlReader useReader, SvgUseReference useReference, SvgDocumentState document)
        {
            string? clipAttribute = useReader.GetAttribute("clip-path");
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

        // Borrow both input owners, bake each native child map once, then retain a
        // disjoint straight union with an identity source map. Every copy and
        // separating-axis/convexity visit is budgeted and cancellable; temporary
        // commands release on failure and only success transfers the new owner.
        static SvgClipPolygon CombineClipChildren(SvgClipPolygon first, SvgClipPolygon next, SvgDocumentState state,
            ParserWorkCounter work, CancellationToken token)
        {
            if ((first.UsesNativeReplay && first.ChildCount == 1) || next.UsesNativeReplay
                || (first.IsEvenOdd && first.ChildCount == 1) || next.IsEvenOdd || next.ContourEnds.Length != 1
                || (first.ChildCount == 1 && first.ContourEnds.Length != 1))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only disjoint straight single-contour clip children are implemented.");
            if (first.ChildCount >= 42)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip content requiring the browser's multi-shape mask fallback is not implemented.");
            SvgAuthoredPath firstPath = first.SourcePath ?? throw new InvalidOperationException("Clip children require retained source commands.");
            SvgAuthoredPath nextPath = next.SourcePath ?? throw new InvalidOperationException("Clip children require retained source commands.");
            using SvgPreparedPathContours left = firstPath.Prepare(SvgAffineTransform.Identity, false, state.Budget, work, token);
            using SvgPreparedPathContours right = nextPath.Prepare(SvgAffineTransform.Identity, false, state.Budget, work, token);
            RequireConvex(right);
            if (first.ChildCount == 1) RequireConvex(left);
            for (int contour = 0; contour < left.ContourEnds.Length; contour++)
            {
                int start = contour == 0 ? 0 : left.ContourEnds[contour - 1];
                ReadOnlySpan<SvgDevicePoint> ring = left.Points.AsSpan(start, left.ContourEnds[contour] - start);
                if (!Separated(ring, right.Points) && !Separated(right.Points, ring))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Overlapping clip children require native path union, which is not implemented.");
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
            SvgPaintBounds firstBounds = ChildBounds(first, firstPath), nextBounds = ChildBounds(next, nextPath);
            SvgPaintBounds resourceBounds = new(MathF.Min(firstBounds.Left, nextBounds.Left), MathF.Min(firstBounds.Top, nextBounds.Top),
                MathF.Max(firstBounds.Right, nextBounds.Right), MathF.Max(firstBounds.Bottom, nextBounds.Bottom));
            OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
            SvgAuthoredPath combined;
            try { combined = SvgAuthoredPath.Create(owned, SvgAffineTransform.Identity, state.Budget); }
            catch { owned.Dispose(); throw; }
            return new(points, ends) { SourcePath = combined, UsesNativeReplay = true, ChildCount = first.ChildCount + 1, ResourceBounds = resourceBounds, IsEvenOdd = true };

            SvgPaintBounds ChildBounds(SvgClipPolygon polygon, SvgAuthoredPath path)
            {
                if (polygon.ResourceBounds is {} bounds) return bounds;
                SvgRectangle tight = SvgPathBounds.Compute(path.Commands, work, token);
                return new SvgPaintBounds((float)tight.X, (float)tight.Y, (float)(tight.X + tight.Width), (float)(tight.Y + tight.Height)).Map(path.SceneMap, work, token);
            }

            void RequireConvex(SvgPreparedPathContours path)
            {
                if (path.ContourEnds.Length != 1 || path.Points.Length < 3)
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only nondegenerate straight clip children are implemented.");
                long pairs = checked((long)path.Points.Length * path.Points.Length);
                for (long visit = 0; visit < pairs; visit++) work.Consume(token);
                if (!SvgRawContourConvexity.IsConvex(path.Points, [], [], token))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only convex sibling clip children are implemented.");
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

        static SvgClipPolygon ParseClipPathPolygon(string? pathData, SvgAffineTransform clipTransform, bool isEvenOdd, SvgDocumentState clipState, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(clipState);
            ArgumentNullException.ThrowIfNull(workCounter);
            if (string.IsNullOrEmpty(pathData)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Degenerate clip paths are not implemented.");
            (int letters, bool hasCurve) = ScanClipPathData(pathData, workCounter, cancellationToken);
            int pointCapacity = (int)Math.Min(hasCurve ? (long)letters * (SvgOrthogonalGeometryLowering.CurveSegmentsPerCommand + 1) : pathData.Length / 2L + 2, SvgOrthogonalGeometryLowering.MaximumFlattenedContourPoints);
            int contourCapacity = Math.Min(letters + 1, pointCapacity);
            long scratchBytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity) * (hasCurve ? 2 : 1) + ((hasCurve ? 4 : 2) * ParserCollectionMemory.GetArrayByteCount<int>(contourCapacity)));
            using RenderBudget.BudgetReservation scratch = clipState.Budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
            SvgContourBuilder builder = hasCurve ? SvgContourBuilder.CreateGrowing(pointCapacity, contourCapacity) : new(pointCapacity, contourCapacity);
            SvgFlattenPolicy flattenPolicy = new(SvgOrthogonalGeometryLowering.FlattenFlatness, SvgOrthogonalGeometryLowering.FlattenMaxDepth, SvgOrthogonalGeometryLowering.CurveSegmentsPerCommand);
            using BudgetedParserList<SvgAuthoredPathCommand> authored = new(clipState.Budget, SvgMemoryCategory.Scene, workCounter, cancellationToken);
            SvgPathDataParser.ParsePath(pathData, clipTransform, builder, flattenPolicy, letters, workCounter, cancellationToken, null, authored);
            if (builder.ContourCount < 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Degenerate clip paths are not implemented.");
            if (!hasCurve && builder.ContourCount > 1 && isEvenOdd) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Evenodd multi-contour clip paths are not implemented.");
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
                if (cleaned.Count < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Degenerate clip paths are not implemented.");
                if (!hasCurve && builder.ContourCount > 1)
                {
                    // Geometric intersection still requires convex rings. Single
                    // contours use native directed winding, whose nonempty lobes
                    // can cancel in the signed-area sum.
                    if (ClipRingArea(cleaned, workCounter, cancellationToken) == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Degenerate clip paths are not implemented.");
                    if (!IsConvexRing(cleaned, workCounter, cancellationToken)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only convex clip paths are implemented.");
                }
                for (int index = 0; index < cleaned.Count; index++)
                {
                    workCounter.Consume(cancellationToken);
                    stripped.Add(cleaned[index]);
                }
                strippedEnds.Add(stripped.Count);
            }
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
            return new SvgClipPolygon(retainedPoints, retainedEnds) { SourcePath = source, IsEvenOdd = isEvenOdd, UsesNativeReplay = hasCurve };
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

        static double ClipRingArea(BudgetedParserList<SvgDevicePoint> ring, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            double twiceArea = 0;
            for (int index = 0; index < ring.Count; index++)
            {
                if ((index & 1023) == 0) workCounter.Consume(cancellationToken);
                SvgDevicePoint current = ring[index];
                SvgDevicePoint next = ring[(index + 1) % ring.Count];
                twiceArea += (current.X * next.Y) - (next.X * current.Y);
            }
            return twiceArea / 2;
        }

        static bool IsConvexRing(BudgetedParserList<SvgDevicePoint> ring, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            int turn = 0;
            for (int index = 0; index < ring.Count; index++)
            {
                if ((index & 1023) == 0) workCounter.Consume(cancellationToken);
                SvgDevicePoint current = ring[index];
                SvgDevicePoint next = ring[(index + 1) % ring.Count];
                SvgDevicePoint following = ring[(index + 2) % ring.Count];
                double cross = ((next.X - current.X) * (following.Y - next.Y)) - ((next.Y - current.Y) * (following.X - next.X));
                if (cross == 0) continue;
                int sign = cross > 0 ? 1 : -1;
                if (turn == 0) turn = sign;
                else if (turn != sign) return false;
            }
            return turn != 0;
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

    internal static void RegisterGradient(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        SvgGradientTemplate template = state.Gradient ?? throw new InvalidOperationException("Gradient registration requires gradient dimensions.");
        if (state.GradientStops.Count != 0 && (state.GradientStops.Count > SvgGradientSyntax.MaximumGradientStops)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only empty or one-to-eight-stop linear gradients are implemented.");
        if (template.Href is not null)
        {
            if (template.VectorCount is > 0 and < 4 && template.HasUnits) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Partial gradient vector overrides with gradient unit overrides are not implemented.");
            if (template.HasUnits && template.VectorCount == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gradient unit overrides without vector overrides are not implemented.");
        }
        if (!state.ThrowOnMissingGradientHref && template.Href is not null && (state.GradientStops.Count == 0 || template.VectorCount < 4))
        {
            string hrefId = SvgGradientSyntax.ParseGradientHrefId(template.Href);
            if (!state.Gradients.TryGetValue(hrefId, out _) && !state.RadialGradients.TryGetValue(hrefId, out _))
            {
                StashPendingLinearGradient(state, template, hrefId, workCounter, cancellationToken);
                return;
            }
        }
        (SvgColor start, SvgColor end, double offset1, double offset2, bool isEmpty, SvgGradientStop[] stops) = ResolveGradientStops(state, template);
        (double x1, double y1, double x2, double y2, bool isUserSpace, SvgGradientSpread spread, double referenceScaleX, double referenceScaleY, bool hasLinearRotation) = ResolveGradientVector(template, state);
        SvgLinearGradient gradient = new(start, end, x1, y1, x2, y2, offset1, offset2, isEmpty, isUserSpace, spread, stops, referenceScaleX, referenceScaleY, hasLinearRotation)
        { SourcePaint = RetainSourcePaint(template, state, workCounter, cancellationToken) };
        string retainedId = state.Strings.Copy(state.GradientId ?? throw new InvalidOperationException("Gradient registration requires an identifier."));
        if (!state.Gradients.TryAdd(retainedId, gradient)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG gradient ids must be unique.");
        DrainPendingGradientHrefs(state, workCounter, cancellationToken);

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

    /// <summary>Retains one linear-gradient href inheritance whose target is not registered yet for document-end resolution.</summary>
    private static void StashPendingLinearGradient(SvgDocumentState state, SvgGradientTemplate template, string hrefId, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        string id = state.Strings.Copy(state.GradientId ?? throw new InvalidOperationException("Gradient registration requires an identifier."));
        string href = state.Strings.Copy(hrefId.AsSpan());
        string hrefRef = state.Strings.Copy(("#" + hrefId).AsSpan());
        SvgGradientStop[] stops = new SvgGradientStop[state.GradientStops.Count];
        for (int index = 0; index < stops.Length; index++)
        {
            workCounter.Consume(cancellationToken);
            stops[index] = state.GradientStops[index];
        }
        workCounter.Consume(cancellationToken);
        state.PendingLinearGradientHrefs.Add(new PendingLinearGradientHref(id, href, template with { Href = hrefRef }, stops));
    }

    /// <summary>Resolves deferred gradient href inheritances whose targets registered later, in dependency order across both gradient kinds.</summary>
    /// <remarks>Targets still missing stay pending without error so later definitions and the document-end pass can resolve chains; callers charge one work unit per scanned entry plus the registration work itself.</remarks>
    internal static void DrainPendingGradientHrefs(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        if (state.DrainingGradientHrefs) return;
        state.DrainingGradientHrefs = true;
        try
        {
            while (state.PendingLinearGradientHrefs.Count > 0 || state.PendingRadialGradientHrefs.Count > 0)
            {
                bool resolvedAny = DrainPendingLinearGradientHrefs(state, workCounter, cancellationToken);
                resolvedAny |= DrainPendingRadialGradientHrefs(state, workCounter, cancellationToken);
                if (!resolvedAny) break;
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
        DrainPendingGradientHrefs(state, workCounter, cancellationToken);
        if (state.PendingLinearGradientHrefs.Count > 0 || state.PendingRadialGradientHrefs.Count > 0)
        {
            state.ThrowOnMissingGradientHref = true;
            try
            {
                if (state.PendingLinearGradientHrefs.Count > 0) RegisterPendingLinearGradient(state, state.PendingLinearGradientHrefs[0], workCounter, cancellationToken);
                if (state.PendingRadialGradientHrefs.Count > 0) RegisterPendingRadialGradient(state, state.PendingRadialGradientHrefs[0], workCounter, cancellationToken);
            }
            finally
            {
                state.ThrowOnMissingGradientHref = false;
            }
        }
    }

    private static bool DrainPendingLinearGradientHrefs(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        bool resolvedAny = false;
        for (int index = state.PendingLinearGradientHrefs.Count - 1; index >= 0; index--)
        {
            workCounter.Consume(cancellationToken);
            PendingLinearGradientHref pending = state.PendingLinearGradientHrefs[index];
            if (!state.Gradients.TryGetValue(pending.HrefId, out _) && !state.RadialGradients.TryGetValue(pending.HrefId, out _)) continue;
            RegisterPendingLinearGradient(state, pending, workCounter, cancellationToken);
            int last = state.PendingLinearGradientHrefs.Count - 1;
            if (index != last) state.PendingLinearGradientHrefs[index] = state.PendingLinearGradientHrefs[last];
            state.PendingLinearGradientHrefs.RemoveLast();
            resolvedAny = true;
        }

        return resolvedAny;
    }

    private static bool DrainPendingRadialGradientHrefs(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        bool resolvedAny = false;
        for (int index = state.PendingRadialGradientHrefs.Count - 1; index >= 0; index--)
        {
            workCounter.Consume(cancellationToken);
            PendingRadialGradientHref pending = state.PendingRadialGradientHrefs[index];
            if (!state.Gradients.TryGetValue(pending.HrefId, out _) && !state.RadialGradients.TryGetValue(pending.HrefId, out _)) continue;
            RegisterPendingRadialGradient(state, pending, workCounter, cancellationToken);
            int last = state.PendingRadialGradientHrefs.Count - 1;
            if (index != last) state.PendingRadialGradientHrefs[index] = state.PendingRadialGradientHrefs[last];
            state.PendingRadialGradientHrefs.RemoveLast();
            resolvedAny = true;
        }

        return resolvedAny;
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
        if (state.GradientStops.Count != 0 && (state.GradientStops.Count > SvgGradientSyntax.MaximumGradientStops)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only empty or one-to-eight-stop radial gradients are implemented.");
        if (template.Href is not null && template.VectorCount > 0 && template.VectorCount < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Partial radial vector overrides are not implemented.");
        if (template.Href is not null && template.VectorCount == 0 && (template.HasFocalX || template.HasFocalY || template.HasFocalRadius)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Partial radial vector overrides are not implemented.");
        if (template.HasUnits && template.VectorCount == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Radial unit overrides without vector overrides are not implemented.");
        if (!state.ThrowOnMissingGradientHref && template.Href is not null && (state.GradientStops.Count == 0 || (template.VectorCount == 0 && !template.HasFocalX && !template.HasFocalY && !template.HasFocalRadius)))
        {
            string hrefId = SvgGradientSyntax.ParseGradientHrefId(template.Href);
            if (!state.Gradients.TryGetValue(hrefId, out _) && !state.RadialGradients.TryGetValue(hrefId, out _))
            {
                StashPendingRadialGradient(state, template, hrefId, workCounter, cancellationToken);
                return;
            }
        }
        (SvgColor start, SvgColor end, double offset1, double offset2, bool isEmpty, SvgGradientStop[] stops) = ResolveRadialStops(state, template);
        (double centerX, double centerY, double radius, double focalX, double focalY, double focalRadius, bool isUserSpace, SvgGradientSpread spread, double referenceScaleX, double referenceScaleY, bool hasRadialRotation, SvgGradientTransform inverse, bool hasGeneralTransform) = ResolveRadialVector(template, state);
        if (!isUserSpace) ValidateRadialFocalCircle(centerX, centerY, radius, focalX, focalY, focalRadius);
        SvgRadialGradient gradient = new(start, end, centerX, centerY, radius, focalX, focalY, focalRadius, offset1, offset2, isEmpty, isUserSpace, spread, stops, referenceScaleX, referenceScaleY, hasRadialRotation, inverse, hasGeneralTransform);
        if (hasGeneralTransform && template.HasTransform)
        {
            SvgGradientTransform map = template.Transform;
            gradient = gradient with { NativePaintMatrices = new(new((float)map.A, (float)map.C, (float)map.E, (float)map.B, (float)map.D, (float)map.F), SvgAffineTransform.Identity) };
        }
        string retainedId = state.Strings.Copy(state.RadialGradientId ?? throw new InvalidOperationException("Radial gradient registration requires an identifier."));
        if (state.Gradients.TryGetValue(retainedId, out _)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG gradient ids must be unique.");
        if (!state.RadialGradients.TryAdd(retainedId, gradient)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG gradient ids must be unique.");
        DrainPendingGradientHrefs(state, workCounter, cancellationToken);
    }

    /// <summary>Retains one radial-gradient href inheritance whose target is not registered yet for document-end resolution.</summary>
    private static void StashPendingRadialGradient(SvgDocumentState state, SvgRadialTemplate template, string hrefId, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        string id = state.Strings.Copy(state.RadialGradientId ?? throw new InvalidOperationException("Radial gradient registration requires an identifier."));
        string href = state.Strings.Copy(hrefId.AsSpan());
        string hrefRef = state.Strings.Copy(("#" + hrefId).AsSpan());
        SvgGradientStop[] stops = new SvgGradientStop[state.GradientStops.Count];
        for (int index = 0; index < stops.Length; index++)
        {
            workCounter.Consume(cancellationToken);
            stops[index] = state.GradientStops[index];
        }
        workCounter.Consume(cancellationToken);
        state.PendingRadialGradientHrefs.Add(new PendingRadialGradientHref(id, href, template with { Href = hrefRef }, stops));
    }

    private static (SvgColor Start, SvgColor End, double Offset1, double Offset2, bool IsEmpty, SvgGradientStop[] Stops) ResolveRadialStops(SvgDocumentState state, SvgRadialTemplate template)
    {
        if (state.GradientStops.Count >= 1) return ClampStopList(state);
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

    private static (SvgColor Start, SvgColor End, double Offset1, double Offset2, bool IsEmpty, SvgGradientStop[] Stops) ResolveGradientStops(SvgDocumentState state, SvgGradientTemplate template)
    {
        if (state.GradientStops.Count >= 1) return ClampStopList(state);
        if (template.Href is null) return (default, default, 0, 1, true, Array.Empty<SvgGradientStop>());
        if (!state.Gradients.TryGetValue(SvgGradientSyntax.ParseGradientHrefId(template.Href), out SvgLinearGradient parent))
        {
            if (!state.RadialGradients.TryGetValue(SvgGradientSyntax.ParseGradientHrefId(template.Href), out SvgRadialGradient radial)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
            return (radial.Start, radial.End, radial.Offset1, radial.Offset2, radial.IsEmpty, radial.Stops);
        }

        return (parent.Start, parent.End, parent.Offset1, parent.Offset2, parent.IsEmpty, parent.Stops);
    }

    private static (SvgColor Start, SvgColor End, double Offset1, double Offset2, bool IsEmpty, SvgGradientStop[] Stops) ClampStopList(SvgDocumentState state)
    {
        // A stop offset below the previous effective offset rises to it, forming a hard edge; Math.Clamp below preserves the order.
        for (int index = 1; index < state.GradientStops.Count; index++)
        {
            if (state.GradientStops[index].Offset < state.GradientStops[index - 1].Offset) state.GradientStops[index] = state.GradientStops[index] with { Offset = state.GradientStops[index - 1].Offset };
        }
        SvgGradientStop[] clamped = new SvgGradientStop[state.GradientStops.Count];
        for (int index = 0; index < clamped.Length; index++)
        {
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

    internal static void RegisterPattern(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        string retainedId = state.Strings.Copy(state.PatternId ?? throw new InvalidOperationException("Pattern registration requires an identifier."));
        SvgPatternTile tile = state.PatternTile ?? throw new InvalidOperationException("Pattern registration requires pattern dimensions.");
        if (state.PatternDefinitions.TryGetValue(retainedId, out _)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG pattern ids must be unique.");
        if (state.PatternContentBuilder is {} builder)
        {
            SvgLeafMaskComposition.Emit(state, builder, workCounter, cancellationToken);
            SvgVectorContent content = SvgVectorContent.Create(builder.Build(), state.Budget);
            try { state.Scene.AdoptVectorContent(content); }
            catch { content.Dispose(); throw; }
            tile = tile with { Content = null, VectorContent = content };
        }
        SvgPatternDefinition definition = new(retainedId, state.PatternTemplate, tile, state.PatternRectangleSource, state.PatternRectangleContext, false);
        if (!state.PatternDefinitions.TryAdd(retainedId, definition)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG pattern ids must be unique.");
        if (definition.Template.Href is null && !state.Patterns.TryAdd(retainedId, tile)) throw new InvalidOperationException("A pattern must register once.");
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
        if (!state.MaskDefaultUserSpaceRegion && state.MaskCustomRegion is SvgClipBox custom && state.MaskContentBox is SvgClipBox cover && !cover.Contains(custom.X, custom.Y, custom.X + custom.Width, custom.Y + custom.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only mask content covering its custom region is implemented.");
    }

    internal static void RegisterClipPolygon(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        string retainedId = state.Strings.Copy(state.ClipPathId ?? throw new InvalidOperationException("Clip polygon registration requires an identifier."));
        SvgClipPolygon polygon = state.ClipPolygon ?? throw new InvalidOperationException("Clip polygon registration requires retained points.");
        if (polygon.ChildCount > 1)
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

        // The bounded disjoint convex subset needs no intersections. Adapt the
        // native final Simplify pass: sort contour bounds, then traverse the first
        // CCW segment from its end back to its start and retain the closing line.
        // That cyclic start determines adjacent clipped-edge combination.
        static SvgAuthoredPath NormalizeUnion(SvgClipPolygon polygon, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        {
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
        if (state.ClipIsObjectBoundingBox) { if (state.Clips.TryGetValue(retainedId, out _) || !state.ObjectClips.TryAdd(retainedId, state.ClipBox ?? new SvgClipBox(0, 0, 0, 0))) throw new SvgException(SvgErrorCode.MalformedInput, "SVG clip-path ids must be unique."); } else if (state.ObjectClips.TryGetValue(retainedId, out _) || !state.Clips.TryAdd(retainedId, state.ClipBox ?? new SvgClipBox(0, 0, 0, 0))) throw new SvgException(SvgErrorCode.MalformedInput, "SVG clip-path ids must be unique.");
    }

    private static SvgGeometry RequirePatternTileContent(SvgGeometry content, SvgPatternTile tile)
    {
        if (content.Kind != SvgGeometryKind.Rectangle) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only rectangle pattern tile content is implemented.");
        SvgRectangle box = content.Rectangle;
        if (!box.HasFill) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Unpainted pattern tile content is not implemented.");
        if (box.X != Math.Truncate(box.X) || box.Y != Math.Truncate(box.Y) || box.Width != Math.Truncate(box.Width) || box.Height != Math.Truncate(box.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer pattern tile content is implemented.");
        if (box.X < tile.OriginX || box.Y < tile.OriginY || box.X + box.Width > tile.OriginX + tile.TileWidth || box.Y + box.Height > tile.OriginY + tile.TileHeight) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern tile content overflowing its tile is not implemented.");
        return content;
    }

    internal static void RegisterSymbol(SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        SvgSymbolDefinitionBuilder builder = state.SymbolDefinition ?? throw new InvalidOperationException("Symbol registration requires an open builder.");
        SvgDefinition definition = builder.Build(state.SymbolViewBox, state, work, token) with { Marker = state.MarkerDefinition };
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
    {
        if (reader.LocalName is not ("line" or "path") && SvgMarkerStyle.Resolve(context.ResolveStyle(reader), context.InheritedPresentation.Markers).HasMarkers)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Markers on shapes other than lines and paths are not implemented.");
        if (reader.LocalName == "rect")
        {
            SvgRectangleSyntax rectangle = SvgRectangleSyntax.Parse(reader, context.LengthBases);
            SvgComputedStyle style = context.ResolveStyle(reader);
            string? stroke = style.Stroke ?? context.InheritedStroke.Stroke;
            if ((rectangle.CornerX ?? rectangle.CornerY ?? 0) == 0 && (rectangle.CornerY ?? rectangle.CornerX ?? 0) == 0
                && stroke is not null && !stroke.Equals("none", StringComparison.OrdinalIgnoreCase) && !SvgStrokeStyleSyntax.IsZeroStrokeWidth(style.Width)
                && SvgStrokeStyleSyntax.ParseLinejoin(style.Linejoin, true) != SvgStrokeJoin.Miter)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Non-miter stroke joins on rectangle instances are not implemented.");
        }
        SvgDefinition definition = reader.LocalName == "rect"
            ? new(RetainParsedGeometries(SvgLeafParser.ParseRectangle(reader, context, null, state), state, context.WorkCounter, context.CancellationToken), false, null, null)
            : reader.LocalName == "line" ? ParseLineDefinition(reader, context, state)
            : ParseShapeDefinition(reader, context, state, null);
        try
        {
            SvgReferenceResolver.ApplyMask(definition.Geometries, null, null, reader.GetAttribute("mask"), state, context.WorkCounter, context.CancellationToken);
            SvgPaintRecording.FinishDefinition(definition.Geometries, state, context.WorkCounter, context.CancellationToken);
            return BindSymbolFill(definition, reader, context, state, context.WorkCounter, context.CancellationToken, false);
        }
        catch { foreach (SvgGeometry geometry in definition.Geometries) geometry.Dispose(); throw; }
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

    private static SvgDefinition ParseShapeDefinition(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state, SvgViewBox? symbolViewBox)
    {
        // Instance replay must retain the host viewport before relative oval lengths can be admitted here.
        if (reader.LocalName is "circle" or "ellipse"
            && (IsPercentage(reader, "cx") || IsPercentage(reader, "cy") || IsPercentage(reader, "r") || IsPercentage(reader, "rx") || IsPercentage(reader, "ry")))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Percentage oval definitions require retained instance viewport bases.");
        SvgComputedStyle style = context.ResolveStyle(reader);
        // Element-local lowering: the shape lowerers compose the inherited transform with the element attribute themselves, so the inherited
        // side stays identity here; carrying the parsed attribute would apply it twice. Use clones exclude definition ancestors.
        SvgPaintableElementContext localContext = context with { InheritedPresentation = context.InheritedPresentation with { Transform = SvgAffineTransform.Identity }, RecordingScope = new(1) };
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

        static bool IsPercentage(XmlReader input, string attribute)
            => input.GetAttribute(attribute) is {} value && value.AsSpan().Trim().EndsWith("%", StringComparison.Ordinal);

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
        SvgStrokeStyleSyntax.RequireButtCapJoin(reader, "rectangle", context.WorkCounter, context.CancellationToken);
        SvgRectangleSyntax roundedSyntax = SvgRectangleSyntax.Parse(reader, context.LengthBases);
        if ((roundedSyntax.CornerX ?? roundedSyntax.CornerY ?? 0) != 0 || (roundedSyntax.CornerY ?? roundedSyntax.CornerX ?? 0) != 0) return ParseShapeDefinition(reader, context, state, symbolViewBox);
        context = context with { InheritedPresentation = context.InheritedPresentation with { Transform = SvgAffineTransform.Identity }, RecordingScope = new(1) };
        SvgRecordedPaintStyle source = SvgPaintRecording.Resolve(reader, context, state, null);
        context = context with { DeferPaintOpacity = true };
        SvgComputedStyle style = context.ResolveStyle(reader);
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
