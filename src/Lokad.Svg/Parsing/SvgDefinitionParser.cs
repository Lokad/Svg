using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Routes bounded definitions, symbols and local instances while preserving streaming order.</summary>
internal static class SvgDefinitionParser
{
    internal static bool TryParse(XmlReader reader, bool directChild, bool isEligible, SvgRootAttributes root, SvgDocumentState state, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        bool inDefinitions = state.DefinitionsDepth >= 0 && (reader.Depth == state.DefinitionsDepth + 1 || reader.Depth == state.SymbolDepth + 1);
        bool rootDefinition = state.DefinitionsDepth < 0 && state.SymbolDepth < 0 && directChild && reader.GetAttribute("id") is not null;
        bool groupNestedDefinition = state.DefinitionsDepth < 0 && state.SymbolDepth < 0 && state.PatternId is null && state.MaskId is null && state.ClipPathId is null && state.GroupDepth > 0 && reader.GetAttribute("id") is not null;
        if ((inDefinitions || rootDefinition || groupNestedDefinition) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName))
        {
            string? id = state.SymbolDepth >= 0 ? state.SymbolId : reader.GetAttribute("id");
            if (reader.LocalName == "rect") SvgElementAttributePolicy.ValidateRectangle(reader, workCounter, cancellationToken);
            else if (reader.LocalName == "line") SvgElementAttributePolicy.ValidateLine(reader, workCounter, cancellationToken);
            else if (reader.LocalName == "path") SvgElementAttributePolicy.ValidatePath(reader, workCounter, cancellationToken);
            else if (reader.LocalName == "circle" || reader.LocalName == "ellipse") SvgElementAttributePolicy.ValidateCircle(reader, reader.LocalName, workCounter, cancellationToken);
            else SvgElementAttributePolicy.ValidatePointList(reader, reader.LocalName, workCounter, cancellationToken);
            SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, directChild, root, state, namedColors, workCounter, cancellationToken);
            SvgViewBox? symbolViewBox = state.SymbolDepth >= 0 ? state.SymbolViewBox : null;
            SvgDefinition definition = reader.LocalName == "rect" ? ParseRectangleDefinition(reader, context, state, symbolViewBox)
                : reader.LocalName == "line" ? new SvgDefinition(RetainParsedGeometries(SvgLeafParser.ParseLine(reader, context, null, state)), false, null, symbolViewBox)
                : ParseShapeDefinition(reader, context, state, symbolViewBox);
            if (id is not null)
            {
                workCounter.Consume(cancellationToken);
                if (state.Definitions.TryGetValue(id, out _)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
                string retainedId = state.Strings.Copy(id);
                if (!state.Definitions.TryAdd(retainedId, definition)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
            }
            state.HasUnsupportedContent |= !reader.IsEmptyElement;
            state.SawStyleableNode = true;
            return inDefinitions;
        }
        if (state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "symbol")
        {
            SvgElementAttributePolicy.ValidateSymbol(reader, workCounter, cancellationToken);
            if (SvgDocumentStyleResolver.Resolve(reader, directChild, state, workCounter, cancellationToken) != default) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Stylesheet rules on symbol definitions are not implemented.");
            state.SymbolId = reader.GetAttribute("id") ?? throw new SvgException(SvgErrorCode.MalformedInput, "A symbol requires an id.");
            state.SymbolViewBox = SvgGeometrySyntax.ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio"));
            if (!reader.IsEmptyElement) state.SymbolDepth = reader.Depth;
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
                using SvgParsedGeometry parsedContent = SvgLeafParser.ParseRectangle(reader, tileContext, null, state);
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
            state.HasUnsupportedContent = true;
            return true;
        }
        if ((state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 || directChild) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "radialGradient")
        {
            SvgElementAttributePolicy.ValidateRadialGradient(reader, workCounter, cancellationToken);
            state.RadialGradientId = reader.GetAttribute("id");
            SvgRadialTemplate template = SvgGradientSyntax.ParseRadialVector(reader);
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
            SvgGradientTemplate template = SvgGradientSyntax.ParseLinearVector(reader);
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
                double offset = SvgGradientSyntax.ParseStopOffset(reader.GetAttribute("offset"));
                SvgColor stopColor = SvgGradientSyntax.ParseStopColor(reader, namedColors, workCounter, cancellationToken);
                state.GradientStops.Add(new SvgGradientStop(offset, stopColor));
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
            SvgUseReference reference = SvgLocalReferenceParser.ParseUse(reader, context, state.Strings, baseWidth, baseHeight);
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
        (double centerX, double centerY, double radius, double focalX, double focalY, double focalRadius, bool isUserSpace, SvgGradientSpread spread, double referenceScaleX, double referenceScaleY, bool hasRadialRotation) = ResolveRadialVector(template, state);
        if (!isUserSpace) ValidateRadialFocalCircle(centerX, centerY, radius, focalX, focalY, focalRadius);
        SvgRadialGradient gradient = new(start, end, centerX, centerY, radius, focalX, focalY, focalRadius, offset1, offset2, isEmpty, isUserSpace, spread, stops, referenceScaleX, referenceScaleY, hasRadialRotation);
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

    private static (double CenterX, double CenterY, double Radius, double FocalX, double FocalY, double FocalRadius, bool IsUserSpace, SvgGradientSpread Spread, double ReferenceScaleX, double ReferenceScaleY, bool HasRotation) ResolveRadialVector(SvgRadialTemplate template, SvgDocumentState state)
    {
        double centerX = template.CenterX;
        double centerY = template.CenterY;
        double radius = template.Radius;
        double focalRadius = template.FocalRadius;
        bool isUserSpace = template.IsUserSpace;
        double referenceScaleX = 1;
        double referenceScaleY = 1;
        SvgGradientSpread spread = template.Spread;
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
        return (centerX, centerY, radius, focalX, focalY, focalRadius, isUserSpace, spread, referenceScaleX, referenceScaleY, hasRotation);
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
            if (state.GradientStops[index].Offset < state.GradientStops[index - 1].Offset) state.GradientStops[index] = new SvgGradientStop(state.GradientStops[index - 1].Offset, state.GradientStops[index].Color);
        }
        SvgGradientStop[] clamped = new SvgGradientStop[state.GradientStops.Count];
        for (int index = 0; index < clamped.Length; index++)
        {
            clamped[index] = new SvgGradientStop(Math.Clamp(state.GradientStops[index].Offset, 0, 1), state.GradientStops[index].Color);
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

    private static SvgDefinition ParseShapeDefinition(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state, SvgViewBox? symbolViewBox)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        // Element-local lowering: the shape lowerers compose the inherited transform with the element attribute themselves, so the inherited
        // side stays identity here; carrying the parsed attribute would apply it twice. Use clones exclude definition ancestors.
        SvgPaintableElementContext localContext = context with { InheritedPresentation = context.InheritedPresentation with { Transform = SvgAffineTransform.Identity } };
        string? pathData = reader.LocalName == "path" ? reader.GetAttribute("d") : null;
        string? definitionTransform = reader.LocalName == "path" ? reader.GetAttribute("transform") : null;
        string? vectorEffect = reader.LocalName == "path" ? reader.GetAttribute("vector-effect") : null;
        SvgGeometry[] geometries = reader.LocalName switch
        {
            "polygon" or "polyline" => RetainParsedGeometries(SvgOrthogonalShapeParser.ParsePointList(reader, reader.LocalName, localContext, int.MaxValue, state.Budget, null, state)),
            "path" => RetainParsedGeometries(SvgOrthogonalShapeParser.ParsePath(reader, localContext, int.MaxValue, state.Budget, null, state)),
            "circle" => RetainCircleGeometries(SvgLeafParser.ParseCircle(reader, localContext, null, state)),
            "ellipse" => RetainParsedGeometries(SvgLeafParser.ParseEllipse(reader, localContext, null, state)),
            "rect" => RetainParsedGeometries(SvgLeafParser.ParseRoundedRectangleDefinition(reader, localContext, state)),
            _ => throw new InvalidOperationException("Unsupported definition leaf reached shape lowering."),
        };
        if (geometries.Length > 0) state.DefinitionReservations.Add(state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(geometries.Length), SvgMemoryCategory.Scene));
        return new SvgDefinition(geometries, IsOverrideableFill(style, geometries), ResolveInstanceStrokeMiterLimit(style, geometries, pathData, definitionTransform, vectorEffect, context.WorkCounter, context.CancellationToken), symbolViewBox);

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
                SvgAffineTransform elementMap = SvgGeometrySyntax.ParseTransform(transformAttribute);
                if (!elementMap.TryGetUniformScale(out double definitionScale) || Math.Abs(definitionScale - 1) > 1e-9) return null;
            }
            if (!SvgPathDataParser.IsSingleClosedPath(pathData, workCounter, cancellationToken)) return null;
            return SvgStrokeStyleSyntax.ParseMiterLimit(computed.Miterlimit);
        }

        static SvgGeometry[] RetainCircleGeometries(SvgParsedGeometry parsed)
        {
            try { return parsed.Geometries.ToArray(); }
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

    private static SvgGeometry[] RetainParsedGeometries(SvgParsedGeometry parsed)
    {
        try { return parsed.Geometries.ToArray(); }
        finally { parsed.Dispose(); }
    }

    private static SvgDefinition ParseRectangleDefinition(XmlReader reader, SvgPaintableElementContext context, SvgDocumentState state, SvgViewBox? symbolViewBox)
    {
        SvgStrokeStyleSyntax.RequireButtCapJoin(reader, "rectangle", context.WorkCounter, context.CancellationToken);
        SvgRectangleSyntax roundedSyntax = SvgRectangleSyntax.Parse(reader);
        if ((roundedSyntax.CornerX ?? roundedSyntax.CornerY ?? 0) != 0 || (roundedSyntax.CornerY ?? roundedSyntax.CornerX ?? 0) != 0) return ParseShapeDefinition(reader, context, state, symbolViewBox);
        SvgComputedStyle style = context.ResolveStyle(reader);
        if (SvgPatternSyntax.IsPatternStroke(style.Stroke, state)) return new SvgDefinition(RetainParsedGeometries(SvgPatternSyntax.LowerPatternStrokeRect(reader, context, style, state, null)), false, null, symbolViewBox);
        if (SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke)) return new SvgDefinition(RetainParsedGeometries(SvgGradientSyntax.LowerGradientStrokeRect(reader, context, style, state, null)), false, null, symbolViewBox);
        SvgParsedRectangle parsed = SvgLeafParser.ParseRectangleWithStyle(reader, context);
        return new SvgDefinition([parsed.Geometry], parsed.Geometry.Rectangle.HasFill && parsed.Style == default, null, symbolViewBox);
    }
}
