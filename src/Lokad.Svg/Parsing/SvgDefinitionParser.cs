using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Routes bounded definitions, symbols and local instances while preserving streaming order.</summary>
internal static class SvgDefinitionParser
{
    internal static bool TryParse(XmlReader reader, bool directChild, bool isEligible, SvgRootAttributes root, SvgDocumentState state, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (state.DefinitionsDepth >= 0 && (reader.Depth == state.DefinitionsDepth + 1 || reader.Depth == state.SymbolDepth + 1) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName))
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
                : reader.LocalName == "line" ? new SvgDefinition([SvgLeafParser.ParseLine(reader, context, null, state)], false, symbolViewBox)
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
            return true;
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
        if (state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "clipPath")
        {
            SvgElementAttributePolicy.ValidateClipPath(reader, workCounter, cancellationToken);
            state.ClipPathId = reader.GetAttribute("id");
            state.ClipBox = null;
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
                AxisTransform clipTransform = context.ResolveTransform(reader);
                if (state.ClipIsObjectBoundingBox && clipTransform != AxisTransform.Identity) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Transformed objectBoundingBox clip content is not implemented.");
                SvgRectangle rect = clipTransform.Apply(new SvgRectangle(syntax.X, syntax.Y, syntax.Width, syntax.Height, default, false, null));
                state.ClipBox = new SvgClipBox(rect.X, rect.Y, rect.Width, rect.Height);
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
            state.MaskCustomRegion = SvgMaskSyntax.ParseMaskRegion(reader);
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
                tileContext = tileContext with { InheritedPresentation = tileContext.InheritedPresentation with { Transform = AxisTransform.Identity } };
                SvgGeometry content = SvgLeafParser.ParseRectangle(reader, tileContext, null, state);
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
        if (state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "radialGradient")
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
        if (state.DefinitionsDepth >= 0 && reader.Depth == state.DefinitionsDepth + 1 && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "linearGradient")
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
            SvgElementAttributePolicy.ValidateDefinitionContainer(reader);
            if (!reader.IsEmptyElement) state.DefinitionsDepth = reader.Depth;
            return true;
        }
        if (state.DefinitionsDepth < 0 && (directChild || isEligible) && reader.NamespaceURI == "http://www.w3.org/2000/svg" && reader.LocalName == "use")
        {
            SvgElementAttributePolicy.ValidateUse(reader, workCounter, cancellationToken);
            SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, directChild, root, state, namedColors, workCounter, cancellationToken);
            SvgViewportFrame? parentFrame = state.EffectiveFrame;
            double? baseWidth = parentFrame?.ViewportWidth ?? root.Width;
            double? baseHeight = parentFrame?.ViewportHeight ?? root.Height;
            SvgUseReference reference = SvgLocalReferenceParser.ParseUse(reader, context, state.Strings, baseWidth, baseHeight);
            if (SvgMaskSyntax.HasCustomRegion(reader, state)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Custom mask regions on use instances are not implemented.");
            SvgViewportFrame? useFrame = ResolveUseClipFrame(reader, reference, state);
            if (useFrame is not null && useFrame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Use instances in scaled viewports are not implemented.");
            string? maskAttribute = reader.GetAttribute("mask");
            SvgReferenceResolver.ResolveOrDefer(reference, state.Definitions, state.PendingUses, state.Scene, useFrame, SvgMaskSyntax.ResolveOptionalMask(reader, state), maskAttribute is null ? null : state.Strings.Copy(maskAttribute), state, workCounter, cancellationToken);
            state.HasUnsupportedContent |= !reader.IsEmptyElement;
            state.SawStyleableNode = true;
            return true;
        }
        return false;

        static SvgViewportFrame? ResolveUseClipFrame(XmlReader useReader, SvgUseReference useReference, SvgDocumentState document)
        {
            string? clipAttribute = useReader.GetAttribute("clip-path");
            if (clipAttribute is null) return SvgClipPathSyntax.ResolveLeafClip(useReader, document);
            if (!document.ObjectClips.TryGetValue(SvgClipPathSyntax.ParseClipId(clipAttribute), out _)) return SvgClipPathSyntax.ResolveLeafClip(useReader, document);
            if (!document.Definitions.TryGetValue(useReference.Id, out SvgDefinition definition)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "objectBoundingBox clip paths on forward use references are not implemented.");
            if (definition.SymbolViewBox is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "objectBoundingBox clip paths on symbol instances are not implemented.");
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
        (SvgColor start, SvgColor end, double offset1, double offset2, bool isEmpty, SvgGradientStop[] stops) = ResolveGradientStops(state, template);
        (double x1, double y1, double x2, double y2, bool isUserSpace, SvgGradientSpread spread) = ResolveGradientVector(template, state);
        SvgLinearGradient gradient = new(start, end, x1, y1, x2, y2, offset1, offset2, isEmpty, isUserSpace, spread, stops);
        string retainedId = state.Strings.Copy(state.GradientId ?? throw new InvalidOperationException("Gradient registration requires an identifier."));
        if (!state.Gradients.TryAdd(retainedId, gradient)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG gradient ids must be unique.");
    }

    internal static void RegisterRadialGradient(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        SvgRadialTemplate template = state.RadialGradient ?? throw new InvalidOperationException("Radial gradient registration requires gradient dimensions.");
        if (state.GradientStops.Count != 0 && (state.GradientStops.Count > SvgGradientSyntax.MaximumGradientStops)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only empty or one-to-eight-stop radial gradients are implemented.");
        if (template.Href is not null && template.VectorCount > 0 && template.VectorCount < 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Partial radial vector overrides are not implemented.");
        if (template.HasUnits && template.VectorCount == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Radial unit overrides without vector overrides are not implemented.");
        (SvgColor start, SvgColor end, double offset1, double offset2, bool isEmpty, SvgGradientStop[] stops) = ResolveRadialStops(state, template);
        (double centerX, double centerY, double radius, SvgGradientSpread spread) = ResolveRadialVector(template, state);
        SvgRadialGradient gradient = new(start, end, centerX, centerY, radius, offset1, offset2, isEmpty, spread, stops);
        string retainedId = state.Strings.Copy(state.RadialGradientId ?? throw new InvalidOperationException("Radial gradient registration requires an identifier."));
        if (state.Gradients.TryGetValue(retainedId, out _)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG gradient ids must be unique.");
        if (!state.RadialGradients.TryAdd(retainedId, gradient)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG gradient ids must be unique.");
    }

    private static (SvgColor Start, SvgColor End, double Offset1, double Offset2, bool IsEmpty, SvgGradientStop[] Stops) ResolveRadialStops(SvgDocumentState state, SvgRadialTemplate template)
    {
        if (state.GradientStops.Count >= 1) return ClampStopList(state);
        if (template.Href is null) return (default, default, 0, 1, true, Array.Empty<SvgGradientStop>());
        if (!state.RadialGradients.TryGetValue(SvgGradientSyntax.ParseGradientHrefId(template.Href), out SvgRadialGradient parent)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local radial gradient references are implemented.");
        return (parent.Start, parent.End, parent.Offset1, parent.Offset2, parent.IsEmpty, parent.Stops);
    }

    private static (double CenterX, double CenterY, double Radius, SvgGradientSpread Spread) ResolveRadialVector(SvgRadialTemplate template, SvgDocumentState state)
    {
        double centerX = template.CenterX;
        double centerY = template.CenterY;
        double radius = template.Radius;
        SvgGradientSpread spread = template.Spread;
        if (template.Href is not null && template.VectorCount == 0)
        {
            if (!state.RadialGradients.TryGetValue(SvgGradientSyntax.ParseGradientHrefId(template.Href), out SvgRadialGradient parent)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local radial gradient references are implemented.");
            centerX = parent.CenterX;
            centerY = parent.CenterY;
            radius = parent.Radius;
            if (!template.HasSpread) spread = parent.Spread;
        }
        return (centerX, centerY, radius, spread);
    }

    private static (SvgColor Start, SvgColor End, double Offset1, double Offset2, bool IsEmpty, SvgGradientStop[] Stops) ResolveGradientStops(SvgDocumentState state, SvgGradientTemplate template)
    {
        if (state.GradientStops.Count >= 1) return ClampStopList(state);
        if (template.Href is null) return (default, default, 0, 1, true, Array.Empty<SvgGradientStop>());
        if (!state.Gradients.TryGetValue(SvgGradientSyntax.ParseGradientHrefId(template.Href), out SvgLinearGradient parent)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
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

    private static (double X1, double Y1, double X2, double Y2, bool IsUserSpace, SvgGradientSpread Spread) ResolveGradientVector(SvgGradientTemplate template, SvgDocumentState state)
    {
        double x1 = template.X1;
        double y1 = template.Y1;
        double x2 = template.X2;
        double y2 = template.Y2;
        bool isUserSpace = template.IsUserSpace;
        SvgGradientSpread spread = template.Spread;
        if (template.Href is not null && template.VectorCount < 4)
        {
            if (!state.Gradients.TryGetValue(SvgGradientSyntax.ParseGradientHrefId(template.Href), out SvgLinearGradient parent)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local gradient references are implemented.");
            x1 = template.HasX1 ? template.X1 : parent.X1;
            y1 = template.HasY1 ? template.Y1 : parent.Y1;
            x2 = template.HasX2 ? template.X2 : parent.X2;
            y2 = template.HasY2 ? template.Y2 : parent.Y2;
            if (!template.HasUnits) isUserSpace = parent.IsUserSpace;
            if (!template.HasSpread) spread = parent.Spread;
        }
        if (template.HasTransform)
        {
            (x1, y1) = template.Transform.Apply(x1, y1);
            (x2, y2) = template.Transform.Apply(x2, y2);
        }
        return (x1, y1, x2, y2, isUserSpace, spread);

    }

    internal static void RegisterPattern(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        string retainedId = state.Strings.Copy(state.PatternId ?? throw new InvalidOperationException("Pattern registration requires an identifier."));
        SvgPatternTile tile = state.PatternTile ?? throw new InvalidOperationException("Pattern registration requires pattern dimensions.");
        if (!state.Patterns.TryAdd(retainedId, tile)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG pattern ids must be unique.");
    }

    internal static void RegisterMask(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        string retainedId = state.Strings.Copy(state.MaskId ?? throw new InvalidOperationException("Mask registration requires an identifier."));
        if (!state.Masks.TryAdd(retainedId, state.MaskValue ?? 0)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskCustomRegion is SvgClipBox region && !state.MaskRegions.TryAdd(retainedId, region)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskContentBox is SvgClipBox content && !state.MaskContentBoxes.TryAdd(retainedId, content)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskCustomRegion is SvgClipBox custom && state.MaskContentBox is SvgClipBox cover && !cover.Contains(custom.X, custom.Y, custom.X + custom.Width, custom.Y + custom.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only mask content covering its custom region is implemented.");
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
        SvgGeometry[] geometries = reader.LocalName switch
        {
            "polygon" or "polyline" => RetainParsedGeometries(SvgOrthogonalShapeParser.ParsePointList(reader, reader.LocalName, context, int.MaxValue, state.Budget, null, state)),
            "path" => RetainParsedGeometries(SvgOrthogonalShapeParser.ParsePath(reader, context, int.MaxValue, state.Budget, null, state)),
            "circle" => RetainCircleGeometries(SvgLeafParser.ParseCircle(reader, context, null, state)),
            "ellipse" => [SvgLeafParser.ParseEllipse(reader, context, null, state)],
            _ => throw new InvalidOperationException("Unsupported definition leaf reached shape lowering."),
        };
        if (geometries.Length > 0) state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(geometries.Length), SvgMemoryCategory.Scene);
        return new SvgDefinition(geometries, IsOverrideableFill(style, geometries), symbolViewBox);

        static SvgGeometry[] RetainCircleGeometries(SvgParsedGeometry parsed)
        {
            try { return parsed.Geometries.ToArray(); }
            finally { parsed.Dispose(); }
        }

        static bool IsOverrideableFill(SvgComputedStyle computed, SvgGeometry[] lowered)
        {
            if (computed != default || lowered.Length != 1) return false;
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
        SvgStrokeStyleSyntax.RequireButtCapJoin(reader, "rectangle");
        SvgComputedStyle style = context.ResolveStyle(reader);
        if (SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke)) return new SvgDefinition([SvgGradientSyntax.LowerGradientStrokeRect(reader, context, style, state, null)], false, symbolViewBox);
        SvgParsedRectangle parsed = SvgLeafParser.ParseRectangleWithStyle(reader, context);
        return new SvgDefinition([parsed.Geometry], parsed.Geometry.Rectangle.HasFill && parsed.Style == default, symbolViewBox);
    }
}
