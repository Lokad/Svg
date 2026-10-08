using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Routes bounded definitions, symbols and local instances while preserving streaming order.</summary>
internal static class SvgDefinitionParser
{
    internal static bool TryParse(XmlReader reader, bool directChild, bool isEligible, SvgRootAttributes root, SvgDocumentState state, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        bool symbolLocation = state.SymbolDepth >= 0 && state.SymbolDefinition is {} activeSymbol && reader.Depth == activeSymbol.ChildDepth(state.SymbolDepth);
        if (symbolLocation && reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgSymbolContentParser.TryParse(reader, root, state, namedColors, workCounter, cancellationToken)) return true;
        bool inDefinitions = (state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1) || symbolLocation;
        bool rootDefinition = state.DefinitionsDepth < 0 && state.SymbolDepth < 0 && directChild && reader.GetAttribute("id") is not null;
        bool groupNestedDefinition = state.DefinitionsDepth < 0 && state.SymbolDepth < 0 && state.PatternId is null && state.MaskId is null && state.ClipPathId is null && state.GroupDepth > 0 && reader.GetAttribute("id") is not null;
        if ((inDefinitions || rootDefinition || groupNestedDefinition) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName))
        {
            bool symbolChild = state.SymbolDepth >= 0;
            string? id = reader.GetAttribute("id");
            if (id is not null && (id == state.SymbolId || state.Definitions.TryGetValue(id, out _) || state.GroupSnapshots.TryGetValue(id, out _)))
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
                context = context with { InheritedPresentation = new(symbol.Paint, true, true, SvgAffineTransform.Identity), InheritedStroke = symbol.Stroke, InheritedFillRule = symbol.FillRule, RecordingScope = new(1) };
            }
            SvgViewBox? symbolViewBox = null;
            SvgDefinition definition = reader.LocalName == "rect" ? ParseRectangleDefinition(reader, context, state, symbolViewBox)
                : reader.LocalName == "line" ? ParseLineDefinition(reader, context, state)
                : ParseShapeDefinition(reader, context, state, symbolViewBox);
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
        if (reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "symbol"
            && SvgStructuralVocabulary.IsSymbolDefinitionLocation(directChild, isEligible && state.GroupDepth > 0, state.DefinitionsDepth, state.SymbolDepth, reader.Depth))
        {
            SvgElementAttributePolicy.ValidateSymbol(reader, workCounter, cancellationToken);
            InlineStyle sheet = SvgDocumentStyleResolver.Resolve(reader, directChild, state, workCounter, cancellationToken);
            SvgComputedStyle style = SvgComputedStyleResolver.Resolve(reader, sheet, workCounter, cancellationToken);
            if (style.Display is not null || style.Visibility is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Display and visibility on symbol containers are not implemented.");
            SvgPaint paint = SvgPaintResolver.Resolve(style.Fill, style.FillOpacity, style.Color, SvgPaint.Default, namedColors);
            SvgInheritedStrokeStyle stroke = new(CopyStyleValue(style.Linecap, state), CopyStyleValue(style.Linejoin, state), CopyStyleValue(style.Dasharray, state), CopyStyleValue(style.Dashoffset, state), CopyStyleValue(style.Width, state), CopyStyleValue(style.Miterlimit, state), CopyStyleValue(style.StrokeOpacity, state), CopyStyleValue(style.Stroke, state));
            SvgSymbolPresentation presentation = new(paint, stroke, CopyStyleValue(style.FillRule, state), style.Fill is not null,
                style.FillOpacity is null ? null : SvgColorParser.ParsePaintOpacity(style.FillOpacity), style.Color is null ? null : SvgColorParser.Parse(style.Color, namedColors), SvgColorParser.ParsePaintOpacity(style.Opacity));
            string? id = reader.GetAttribute("id");
            if (id is not null && (state.Definitions.TryGetValue(id, out _) || state.GroupSnapshots.TryGetValue(id, out _))) throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
            state.SymbolId = id is null ? null : state.Strings.Copy(id);
            state.SymbolViewBox = SvgGeometrySyntax.ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio"), workCounter, cancellationToken);
            state.SymbolDefinition = SvgSymbolDefinitionBuilder.Create(state.Budget, workCounter, cancellationToken, presentation);
            state.SawStyleableNode = true;
            if (reader.IsEmptyElement) RegisterSymbol(state, workCounter, cancellationToken);
            else state.SymbolDepth = reader.Depth;
            return true;
        }
        if (state.SymbolDepth >= 0 && reader.Depth == state.SymbolDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg"
            && reader.LocalName is "title" or "desc" or "metadata")
        {
            if (!reader.IsEmptyElement) state.SymbolDescriptionDepth = reader.Depth;
            return true;
        }
        bool inClipDefinitions = state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1;
        if ((inClipDefinitions || rootDefinition) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "clipPath")
        {
            SvgElementAttributePolicy.ValidateClipPath(reader, workCounter, cancellationToken);
            state.ClipPathId = reader.GetAttribute("id");
            state.ClipBox = null;
            state.ClipPolygon = null;
            state.ClipIsObjectBoundingBox = SvgClipPathSyntax.ParseClipUnits(reader.GetAttribute("clipPathUnits"));
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
            if (reader.LocalName == "rect")
            {
                if (state.ClipBox is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-rectangle clip paths are implemented.");
                SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken);
                SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, false, root, state, namedColors, workCounter, cancellationToken);
                SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
                SvgAffineTransform clipTransform = context.ResolveTransform(reader);
                if (state.ClipIsObjectBoundingBox && clipTransform != SvgAffineTransform.Identity) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Transformed objectBoundingBox clip content is not implemented.");
                SvgRectangle rect = clipTransform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, false, null));
                state.ClipBox = new SvgClipBox(rect.X, rect.Y, rect.Width, rect.Height);
                state.HasUnsupportedContent |= !reader.IsEmptyElement;
                state.SawStyleableNode = true;
                return true;
            }
            if (reader.LocalName == "path")
            {
                if (state.ClipBox is not null || state.ClipPolygon is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-rectangle clip paths are implemented.");
                SvgElementAttributePolicy.ValidatePath(reader, workCounter, cancellationToken);
                if (state.ClipIsObjectBoundingBox) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse clip-path paths are implemented.");
                SvgPaintableElementContext clipContext = SvgPaintableElementContext.Create(reader, false, root, state, namedColors, workCounter, cancellationToken);
                SvgAffineTransform clipTransform = clipContext.ResolveTransform(reader);
                SvgClipPolygon clipContent = ParseClipPathPolygon(reader.GetAttribute("d"), clipTransform, state, workCounter, cancellationToken);
                if (clipContent.ContourEnds.Length == 1 && TryGetAxisClipBox(clipContent.Points, workCounter, cancellationToken, out SvgClipBox clipRect)) state.ClipBox = clipRect;
                else state.ClipPolygon = clipContent;
                state.HasUnsupportedContent |= !reader.IsEmptyElement;
                state.SawStyleableNode = true;
                return true;
            }
            state.HasUnsupportedContent = true;
            return true;
        }
        if (state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "mask")
        {
            SvgElementAttributePolicy.ValidateMask(reader, workCounter, cancellationToken);
            state.MaskId = reader.GetAttribute("id");
            state.MaskIsAlpha = SvgMaskSyntax.ParseMaskType(reader.GetAttribute("mask-type"));
            state.MaskValue = null;
            state.MaskFractionRegion = SvgMaskSyntax.ParseMaskFractionRegion(reader);
            state.MaskCustomRegion = state.MaskFractionRegion is null ? SvgMaskSyntax.ParseMaskRegion(reader) : null;
            state.MaskFractionRegion = SvgMaskSyntax.ParseMaskFractionRegion(reader);
            if (reader.IsEmptyElement)
            {
                if (state.MaskId is not null) RegisterMask(state, workCounter, cancellationToken);
            }
            else state.MaskDepth = reader.Depth;
            state.SawStyleableNode = true;
            return true;
        }
        if (state.MaskDepth >= 0 && reader.Depth == state.MaskDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg")
        {
            if (reader.LocalName == "rect")
            {
                if (state.MaskValue is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-rectangle masks are implemented.");
                SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken);
                SvgRectangleSyntax syntax = SvgRectangleSyntax.Parse(reader);
                SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, false, root, state, namedColors, workCounter, cancellationToken);
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
                (SvgGeometry[] contentGeometries, SvgColor contentPaint) = RetainPaintedMaskContent(lowered.Geometries, state, workCounter, cancellationToken);
                state.MaskContentGeometries = contentGeometries;
                state.MaskContentLuminance = contentPaint.Red;
                state.MaskContentIsAlpha = state.MaskIsAlpha;
                state.MaskContentBox = UnionMaskContentBox(state.MaskContentGeometries, workCounter, cancellationToken);
                state.HasUnsupportedContent |= !reader.IsEmptyElement;
                state.SawStyleableNode = true;
                return true;
            }
            state.HasUnsupportedContent = true;
            return true;
        }
        if (state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "pattern")
        {
            SvgElementAttributePolicy.ValidatePattern(reader, workCounter, cancellationToken);
            state.PatternId = reader.GetAttribute("id");
            state.PatternTile = SvgPatternSyntax.ParsePatternElement(reader);
            if (!reader.IsEmptyElement) state.PatternDepth = reader.Depth;
            else if (state.PatternId is not null) RegisterPattern(state, workCounter, cancellationToken);
            state.SawStyleableNode = true;
            return true;
        }
        if (state.PatternDepth >= 0 && reader.Depth == state.PatternDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg")
        {
            if (reader.LocalName == "rect")
            {
                if (state.PatternTile?.Content is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-rectangle pattern tiles are implemented.");
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
                    state.PatternTile = tile with { Content = SvgGeometry.FromRectangle(shifted) };
                    state.HasUnsupportedContent |= !reader.IsEmptyElement;
                    state.SawStyleableNode = true;
                    return true;
                }
                catch { parsedContent.Discard(); throw; }
            }
            state.HasUnsupportedContent = true;
            return true;
        }
        if ((state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 || directChild) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "radialGradient")
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
        if ((state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 || directChild) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "linearGradient")
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
            double? baseWidth = parentFrame?.ViewportWidth ?? root.Width?.ToAbsoluteOrNull();
            double? baseHeight = parentFrame?.ViewportHeight ?? root.Height?.ToAbsoluteOrNull();
            SvgUseReference reference = SvgLocalReferenceParser.ParseUse(reader, context, state.Strings, baseWidth, baseHeight, state);
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
            if (document.MaskContents.TryGetValue(SvgMaskSyntax.ParseMaskId(maskAttribute), out _)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Spatial mask content on use instances is not implemented.");
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
                if (!geometries[index].Polygon.HasFill) continue;
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

        static SvgClipPolygon ParseClipPathPolygon(string? pathData, SvgAffineTransform clipTransform, SvgDocumentState clipState, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(clipState);
            ArgumentNullException.ThrowIfNull(workCounter);
            if (string.IsNullOrEmpty(pathData)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Degenerate clip paths are not implemented.");
            int letters = ScanClipPathData(pathData, workCounter, cancellationToken);
            int pointCapacity = Math.Min(pathData.Length / 2 + 2, SvgOrthogonalGeometryLowering.MaximumFlattenedContourPoints);
            int contourCapacity = Math.Min(letters + 1, pointCapacity);
            long scratchBytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity) + (2 * ParserCollectionMemory.GetArrayByteCount<int>(contourCapacity)));
            using RenderBudget.BudgetReservation scratch = clipState.Budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
            SvgContourBuilder builder = new(pointCapacity, contourCapacity);
            SvgFlattenPolicy flattenPolicy = new(SvgOrthogonalGeometryLowering.FlattenFlatness, SvgOrthogonalGeometryLowering.FlattenMaxDepth, SvgOrthogonalGeometryLowering.CurveSegmentsPerCommand);
            SvgPathDataParser.ParsePath(pathData, clipTransform, builder, flattenPolicy, letters, workCounter, cancellationToken, null);
            if (builder.ContourCount < 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Degenerate clip paths are not implemented.");
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
                if (ClipRingArea(cleaned, workCounter, cancellationToken) == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Degenerate clip paths are not implemented.");
                if (!IsConvexRing(cleaned, workCounter, cancellationToken)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only convex clip paths are implemented.");
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
            return new SvgClipPolygon(retainedPoints, retainedEnds);
        }

        static int ScanClipPathData(string pathData, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            int letters = 0;
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
                if (current is not ('M' or 'm' or 'L' or 'l' or 'H' or 'h' or 'V' or 'v' or 'Z' or 'z')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only straight clip paths are implemented.");
                letters++;
            }
            workCounter.Consume(cancellationToken);
            if (letters == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Degenerate clip paths are not implemented.");
            return letters;
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
        SvgLinearGradient gradient = new(start, end, x1, y1, x2, y2, offset1, offset2, isEmpty, isUserSpace, spread, stops, referenceScaleX, referenceScaleY, hasLinearRotation);
        string retainedId = state.Strings.Copy(state.GradientId ?? throw new InvalidOperationException("Gradient registration requires an identifier."));
        if (!state.Gradients.TryAdd(retainedId, gradient)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG gradient ids must be unique.");
        DrainPendingGradientHrefs(state, workCounter, cancellationToken);
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
            gradient = gradient with { NativePaintMatrices = new(new((float)map.A, (float)map.C, (float)map.E, (float)map.B, (float)map.D, (float)map.F), new(1, 0, 0, 0, 1, 0)) };
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
            if ((template.Transform.B != 0 || template.Transform.C != 0) && (!template.Transform.IsSimilarity() || Math.Abs(referenceScaleX) != Math.Abs(referenceScaleY))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only axis-aligned or similar linear gradient transforms are implemented.");
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
        if (!state.Patterns.TryAdd(retainedId, tile)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG pattern ids must be unique.");
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
        if (!state.Masks.TryAdd(retainedId, state.MaskValue ?? 0)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskCustomRegion is SvgClipBox region && !state.MaskRegions.TryAdd(retainedId, region)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskFractionRegion is SvgClipBox fractions && !state.MaskFractionRegions.TryAdd(retainedId, fractions)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskContentBox is SvgClipBox content && !state.MaskContentBoxes.TryAdd(retainedId, content)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskCustomRegion is SvgClipBox custom && state.MaskContentBox is SvgClipBox cover && !cover.Contains(custom.X, custom.Y, custom.X + custom.Width, custom.Y + custom.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only mask content covering its custom region is implemented.");
    }

    internal static void RegisterClipPolygon(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        string retainedId = state.Strings.Copy(state.ClipPathId ?? throw new InvalidOperationException("Clip polygon registration requires an identifier."));
        SvgClipPolygon polygon = state.ClipPolygon ?? throw new InvalidOperationException("Clip polygon registration requires retained points.");
        if (state.Clips.TryGetValue(retainedId, out _) || state.ObjectClips.TryGetValue(retainedId, out _) || !state.ClipPolygons.TryAdd(retainedId, polygon)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG clip-path ids must be unique.");
    }

    internal static void RegisterClipBox(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        string retainedId = state.Strings.Copy(state.ClipPathId ?? throw new InvalidOperationException("Clip-path registration requires an identifier."));
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
        SvgDefinition definition = builder.Build(state.SymbolViewBox, state, work, token);
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
        if (reader.LocalName == "rect")
        {
            SvgRectangleSyntax rectangle = SvgRectangleSyntax.Parse(reader);
            SvgResolvedPaintableElement shape = context.ResolveShape(reader, true);
            if ((rectangle.CornerX ?? rectangle.CornerY ?? 0) == 0 && (rectangle.CornerY ?? rectangle.CornerX ?? 0) == 0
                && shape.Presentation.StrokeColor is not null && !SvgStrokeStyleSyntax.IsZeroStrokeWidth(shape.Style.Width)
                && SvgStrokeStyleSyntax.ParseLinejoin(shape.Style.Linejoin, true) != SvgStrokeJoin.Miter)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Non-miter stroke joins on rectangle instances are not implemented.");
        }
        SvgDefinition definition = reader.LocalName == "rect" ? ParseRectangleDefinition(reader, context, state, null)
            : reader.LocalName == "line" ? ParseLineDefinition(reader, context, state)
            : ParseShapeDefinition(reader, context, state, null);
        try
        {
            SvgPaintRecording.FinishDefinition(definition.Geometries, state, context.WorkCounter, context.CancellationToken);
            return BindSymbolFill(definition, reader, context, state, context.WorkCounter, context.CancellationToken, false);
        }
        catch { foreach (SvgGeometry geometry in definition.Geometries) geometry.Dispose(); throw; }
    }

    private static SvgDefinition BindSymbolFill(SvgDefinition definition, XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state, ParserWorkCounter work, CancellationToken token, bool includesSymbolPresentation)
    {
        if (definition.Geometries.Length == 0) return definition with { SymbolFillBindings = [] };
        SvgComputedStyle style = context.ResolveStyle(reader);
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
                bool strokeChannel = state.Scene.GetRecordedPaintValue(definition.Geometries[index].RecordedPaint).IsStroke;
                bool hasStroke = strokeChannel || definition.Geometries[index].RecordedStrokePaint.Identity != 0;
                bindings[index] = new(inherited && !strokeChannel, opacity, color) { AuthoredFill = strokeChannel ? null : authoredFill, AuthoredStroke = hasStroke ? authoredStroke : null, StrokeOpacity = authoredStrokeOpacity, IsStroke = strokeChannel, IsPainted = isPainted, IsPaintServer = !strokeChannel && style.Fill is not null && SvgLocalUrl.TryParseId(style.Fill, out _), IsStrokePaintServer = hasStroke && stroke is not null && SvgLocalUrl.TryParseId(stroke, out _) };
            }
            state.DefinitionReservations.Add(reservation);
            return definition with { SymbolFillBindings = bindings };
        }
        catch { reservation.Dispose(); throw; }
    }

    private static SvgDefinition ParseShapeDefinition(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state, SvgViewBox? symbolViewBox)
    {
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
                SvgEllipseSyntax oval = reader.LocalName == "circle" ? SvgEllipseSyntax.ParseCircle(reader) : SvgEllipseSyntax.ParseEllipse(reader);
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
        SvgStrokeStyleSyntax.RequireButtCapJoin(reader, "rectangle", context.WorkCounter, context.CancellationToken);
        SvgRectangleSyntax roundedSyntax = SvgRectangleSyntax.Parse(reader);
        if ((roundedSyntax.CornerX ?? roundedSyntax.CornerY ?? 0) != 0 || (roundedSyntax.CornerY ?? roundedSyntax.CornerX ?? 0) != 0) return ParseShapeDefinition(reader, context, state, symbolViewBox);
        context = context with { InheritedPresentation = context.InheritedPresentation with { Transform = SvgAffineTransform.Identity }, RecordingScope = new(1) };
        SvgRecordedPaintStyle source = SvgPaintRecording.Resolve(reader, context, state, null);
        context = context with { DeferPaintOpacity = true };
        SvgComputedStyle style = context.ResolveStyle(reader);
        SvgParsedGeometry geometry;
        bool allowsOverride = false;
        if (SvgPatternSyntax.IsPatternStroke(style.Stroke, state)) geometry = SvgPatternSyntax.LowerPatternStrokeRect(reader, context, style, state, null);
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
