namespace Lokad.Svg.Parsing;

/// <summary>Represents a parsed direct local use reference before its target is resolved.</summary>
internal readonly record struct SvgUseReference(string Id, double X, double Y, double? Width, double? Height, AxisTransform Transform, AxisTransform InheritedTransform, byte Opacity, bool IsPainted, SvgPaint? FillOverride, string? PaintServerId);

/// <summary>Records the placeholders that must be replaced when a forward local use reference resolves.</summary>
internal readonly record struct PendingUse(SvgSceneSlot Slot, SvgUseReference Reference, SvgViewportFrame? Frame, byte? Mask, string? MaskReference);

/// <summary>Applies typed local references through their geometry-specific supported transforms.</summary>
internal static class SvgReferenceResolver
{
    /// <summary>Resolves an already parsed local reference immediately or retains its typed placeholder for document-end resolution.</summary>
    internal static void ResolveOrDefer(SvgUseReference reference, BudgetedParserDictionary<string, SvgDefinition> definitions, BudgetedParserList<PendingUse> pendingUses, SvgSceneBuilder scene, SvgViewportFrame? frame, byte? mask, string? maskReference, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        if (definitions.TryGetValue(reference.Id, out SvgDefinition definition)) scene.AddGeometry(ApplyMask(definition, reference, frame, mask, maskReference, state));
        else pendingUses.Add(new PendingUse(scene.AddReferencePlaceholder(), reference, frame, mask, maskReference));
    }

    internal static SvgGeometry Apply(SvgDefinition definition, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state)
    {
        if (definition.SymbolViewBox is SvgViewBox symbolBox) return ApplySymbol(definition, symbolBox, reference, frame, state);
        if (reference.PaintServerId is not null) return LowerDirectInstancePaintFill(definition, reference, frame, state);
        SvgGeometry geometry = ApplyInstanceFill(definition, reference);
        return geometry.Kind switch
        {
            SvgGeometryKind.Rectangle => SvgGeometry.FromRectangle(ClipRectangle(ApplyRectangle(geometry.Rectangle, reference), frame)),
            SvgGeometryKind.AxisAlignedLine => ClipLine(ApplyAxisAlignedLine(geometry.AxisAlignedLine, reference), frame),
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "The referenced SVG geometry is not implemented.")
        };
    }

    private static SvgGeometry ApplySymbolInstance(SvgGeometry geometry, SvgViewBox symbolBox, SvgUseReference reference, SvgViewportFrame? frame)
    {
        bool isBand;
        SvgRectangle box;
        if (geometry.Kind == SvgGeometryKind.Rectangle)
        {
            if (geometry.Rectangle.Stroke is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle use references with strokes are not implemented.");
            box = geometry.Rectangle;
            isBand = false;
        }
        else if (geometry.Kind == SvgGeometryKind.AxisAlignedLine)
        {
            box = geometry.AxisAlignedLine.ToStrokeBand();
            isBand = true;
        }
        else throw new SvgException(SvgErrorCode.UnsupportedFeature, "The referenced SVG geometry is not implemented.");
        if (reference.Width is null || reference.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
        SvgRectangle mapped = MapSymbolBox(box, symbolBox, reference.Width.Value, reference.Height.Value);
        SvgRectangle placed = mapped with { X = mapped.X + reference.X, Y = mapped.Y + reference.Y };
        SvgClipBox localViewport = new(reference.X, reference.Y, reference.Width.Value, reference.Height.Value);
        SvgRectangle transformed = reference.Transform.Apply(localViewport.ClipRectangle(placed));
        transformed = transformed with { Color = new SvgColor(transformed.Color.Red, transformed.Color.Green, transformed.Color.Blue, MultiplyAlpha(transformed.Color.Alpha, reference.Opacity)), HasFill = transformed.HasFill && reference.IsPainted };
        SvgClipBox viewport = reference.Transform.Apply(new SvgClipBox(reference.X, reference.Y, reference.Width.Value, reference.Height.Value));
        if (frame is not null)
        {
            transformed = frame.Value.Shift(transformed);
            viewport = new SvgClipBox(viewport.X + frame.Value.OffsetX, viewport.Y + frame.Value.OffsetY, viewport.Width, viewport.Height);
            viewport = viewport.Intersect(frame.Value.Clip);
        }
        SvgRectangle clipped = viewport.ClipRectangle(transformed);
        return isBand ? SvgGeometry.FromOrthogonalBand(clipped) : SvgGeometry.FromRectangle(clipped);
    }

    private static SvgRectangle MapSymbolBox(SvgRectangle box, SvgViewBox viewBox, double viewportWidth, double viewportHeight)
    {
        (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(viewBox, viewportWidth, viewportHeight);
        return box with { X = box.X * scaleX + offsetX, Y = box.Y * scaleY + offsetY, Width = box.Width * scaleX, Height = box.Height * scaleY };
    }

    private static SvgGeometry ApplyMask(SvgDefinition definition, SvgUseReference reference, SvgViewportFrame? frame, byte? mask, string? maskReference, SvgDocumentState state)
    {
        SvgGeometry geometry = Apply(definition, reference, frame, state);
        if (mask is null) return geometry;
        SvgRectangle box = SvgMaskSyntax.BoundingBox(geometry);
        if (maskReference is not null) SvgMaskSyntax.ValidateMaskCoverage(maskReference, state, box);
        return SvgMaskSyntax.ApplyMask(geometry, mask.Value, SvgMaskSyntax.MaskRegion(box));
    }

    private static SvgRectangle ClipRectangle(SvgRectangle rectangle, SvgViewportFrame? frame) => frame is null ? rectangle : frame.Value.ClipShifted(rectangle);

    private static SvgGeometry ClipLine(SvgAxisAlignedLine line, SvgViewportFrame? frame) => frame is null ? SvgGeometry.FromAxisAlignedLine(line) : frame.Value.ClipShifted(line);

    private static SvgGeometry ApplyInstanceFill(SvgDefinition definition, SvgUseReference reference)
    {
        if (reference.FillOverride is null) return definition.Geometry;
        if (definition.Geometry.Kind != SvgGeometryKind.Rectangle || !definition.AllowsInstanceFillOverride) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill styles on use elements require an unstyled visible rectangle definition.");
        SvgPaint fill = reference.FillOverride.Value;
        return SvgGeometry.FromRectangle(definition.Geometry.Rectangle with { Color = SvgPaintResolver.ResolveFillColor(fill, 255), HasFill = fill.HasFill });
    }

    private static SvgGeometry ApplySymbol(SvgDefinition definition, SvgViewBox symbolBox, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state)
    {
        if (reference.PaintServerId is null) return ApplySymbolInstance(ApplyInstanceFill(definition, reference), symbolBox, reference, frame);
        return ApplySymbolPaintServerFill(definition, symbolBox, reference, frame, state);
    }

    private static SvgGeometry LowerDirectInstancePaintFill(SvgDefinition definition, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state)
    {
        if (definition.Geometry.Kind != SvgGeometryKind.Rectangle || !definition.AllowsInstanceFillOverride) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill styles on use elements require an unstyled visible rectangle definition.");
        if (definition.Geometry.Rectangle.Stroke is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle use references with strokes are not implemented.");
        string paintId = reference.PaintServerId ?? throw new InvalidOperationException("Instance paint lowering requires a paint-server reference.");
        if (state.Patterns.TryGetValue(paintId, out SvgPatternTile tile)) return LowerInstancePatternFill(tile, InstancePlacement(definition.Geometry.Rectangle, reference), reference, frame);
        if (state.Gradients.TryGetValue(paintId, out SvgLinearGradient gradient)) return LowerInstanceGradientFill(gradient, InstancePlacement(definition.Geometry.Rectangle, reference), InstanceBox(definition.Geometry.Rectangle, reference, frame), reference, frame);
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern or gradient references are implemented.");
    }

    private static SvgRectangle InstancePlacement(SvgRectangle definitionBox, SvgUseReference reference)
        => reference.Transform.Apply(definitionBox with { X = definitionBox.X + reference.X, Y = definitionBox.Y + reference.Y, Color = default });

    private static SvgRectangle InstanceBox(SvgRectangle definitionBox, SvgUseReference reference, SvgViewportFrame? frame)
    {
        SvgRectangle box = InstancePlacement(definitionBox, reference);
        return frame is null ? box : frame.Value.Shift(box);
    }

    private static SvgGeometry LowerInstanceGradientFill(SvgLinearGradient gradient, SvgRectangle placement, SvgRectangle box, SvgUseReference reference, SvgViewportFrame? frame)
    {
        if (gradient.IsUserSpace) gradient = gradient with { X1 = gradient.X1 + reference.X, Y1 = gradient.Y1 + reference.Y, X2 = gradient.X2 + reference.X, Y2 = gradient.Y2 + reference.Y };
        gradient = SvgGradientSyntax.MapUserSpaceVector(reference.Transform, gradient, frame);
        gradient = SvgGradientSyntax.MirrorBoundingBoxVector(reference.Transform, gradient);
        if (frame is not null) placement = frame.Value.Clip.ClipRectangle(frame.Value.Shift(placement));
        return FinishInstanceGradientFill(gradient, box, placement, reference.Opacity, reference.IsPainted);
    }

    private static SvgGeometry LowerInstancePatternFill(SvgPatternTile tile, SvgRectangle placement, SvgUseReference reference, SvgViewportFrame? frame)
    {
        if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills in scaled viewports are not implemented.");
        if (reference.Transform.ScaleX < 0 || reference.Transform.ScaleY < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only non-reflected pattern fills are implemented.");
        if (frame is not null) placement = frame.Value.Clip.ClipRectangle(frame.Value.Shift(placement));
        return FinishInstancePatternFill(tile, placement, reference.Opacity, reference.IsPainted);
    }

    private static SvgGeometry FinishInstancePatternFill(SvgPatternTile tile, SvgRectangle placement, byte opacity, bool painted)
    {
        SvgRectangle fill = placement with { HasFill = painted };
        if (tile.TileWidth == 0 || tile.TileHeight == 0 || tile.Content is null) return SvgGeometry.FromOrthogonalBand(new SvgRectangle(fill.X, fill.Y, fill.Width, fill.Height, SvgColor.Transparent, false, null));
        SvgRectangle content = tile.Content.Value.Rectangle;
        return SvgGeometry.FromPatternFill(new SvgPatternGeometry(content, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, fill, opacity, null));
    }

    private static SvgGeometry FinishInstanceGradientFill(SvgLinearGradient gradient, SvgRectangle box, SvgRectangle placement, byte opacity, bool painted)
    {
        SvgRectangle fill = placement with { HasFill = painted && !gradient.IsEmpty };
        if (gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty)
        {
            if (gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient fills are implemented.");
            return SvgGeometry.FromRectangle(fill with { Color = SvgPaintResolver.ApplyOpacity(gradient.End, opacity) });
        }
        return SvgGeometry.FromGradientFill(new SvgGradientGeometry(gradient, box, fill, opacity, null));
    }

    private static SvgGeometry ApplySymbolPaintServerFill(SvgDefinition definition, SvgViewBox symbolBox, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state)
    {
        if (definition.Geometry.Kind != SvgGeometryKind.Rectangle || !definition.AllowsInstanceFillOverride) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill styles on use elements require an unstyled visible rectangle definition.");
        if (definition.Geometry.Rectangle.Stroke is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle use references with strokes are not implemented.");
        if (reference.Width is null || reference.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
        string paintId = reference.PaintServerId ?? throw new InvalidOperationException("Instance paint lowering requires a paint-server reference.");
        SvgRectangle fillBox = ApplySymbolInstance(definition.Geometry, symbolBox, reference, frame).Rectangle with { Color = default };
        (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(symbolBox, reference.Width.Value, reference.Height.Value);
        SvgRectangle contentBox = definition.Geometry.Rectangle;
        SvgRectangle box = reference.Transform.Apply(new SvgRectangle(contentBox.X * scaleX + offsetX + reference.X, contentBox.Y * scaleY + offsetY + reference.Y, contentBox.Width * scaleX, contentBox.Height * scaleY, default, true, null));
        if (frame is not null) box = frame.Value.Shift(box);
        if (state.Patterns.TryGetValue(paintId, out SvgPatternTile tile))
        {
            if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills in scaled viewports are not implemented.");
            if (reference.Transform.ScaleX < 0 || reference.Transform.ScaleY < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only non-reflected pattern fills are implemented.");
            return FinishInstancePatternFill(tile, fillBox, reference.Opacity, reference.IsPainted);
        }
        if (state.Gradients.TryGetValue(paintId, out SvgLinearGradient gradient))
        {
            if (gradient.IsUserSpace) gradient = gradient with { X1 = (gradient.X1 * scaleX + offsetX) + reference.X, Y1 = (gradient.Y1 * scaleY + offsetY) + reference.Y, X2 = (gradient.X2 * scaleX + offsetX) + reference.X, Y2 = (gradient.Y2 * scaleY + offsetY) + reference.Y };
            gradient = SvgGradientSyntax.MapUserSpaceVector(reference.Transform, gradient, frame);
            gradient = SvgGradientSyntax.MirrorBoundingBoxVector(reference.Transform, gradient);
            return FinishInstanceGradientFill(gradient, box, fillBox, reference.Opacity, reference.IsPainted);
        }
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern or gradient references are implemented.");
    }

    private static SvgRectangle ApplyRectangle(SvgRectangle rectangle, SvgUseReference reference)
    {
        if (rectangle.Stroke is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle use references with strokes are not implemented.");
        SvgRectangle transformed = reference.Transform.Apply(rectangle with { X = rectangle.X + reference.X, Y = rectangle.Y + reference.Y });
        return transformed with { Color = new SvgColor(transformed.Color.Red, transformed.Color.Green, transformed.Color.Blue, MultiplyAlpha(transformed.Color.Alpha, reference.Opacity)), HasFill = transformed.HasFill && reference.IsPainted };
    }

    private static SvgAxisAlignedLine ApplyAxisAlignedLine(SvgAxisAlignedLine line, SvgUseReference reference)
    {
        if (reference.X != Math.Truncate(reference.X) || reference.Y != Math.Truncate(reference.Y)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integral axis-aligned line use offsets are implemented.");
        SvgAxisAlignedLine transformed = reference.Transform.Apply(line with { X1 = line.X1 + reference.X, Y1 = line.Y1 + reference.Y, X2 = line.X2 + reference.X, Y2 = line.Y2 + reference.Y });
        return transformed with { Color = new SvgColor(transformed.Color.Red, transformed.Color.Green, transformed.Color.Blue, MultiplyAlpha(transformed.Color.Alpha, reference.Opacity)), IsPainted = transformed.IsPainted && reference.IsPainted };
    }

    internal static void ResolvePendingUses(BudgetedParserDictionary<string, SvgDefinition> definitions, BudgetedParserList<PendingUse> pendingUses, SvgSceneBuilder scene, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        for (int pendingUseIndex = 0; pendingUseIndex < pendingUses.Count; pendingUseIndex++)
        {
            workCounter.Consume(cancellationToken);
            PendingUse pendingUse = pendingUses[pendingUseIndex];
            if (!definitions.TryGetValue(pendingUse.Reference.Id, out SvgDefinition definition)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only supported local geometry use references are implemented.");
            scene.Replace(pendingUse.Slot, ApplyMask(definition, pendingUse.Reference, pendingUse.Frame, pendingUse.Mask, pendingUse.MaskReference, state));
        }
    }

    internal static byte MultiplyAlpha(byte left, byte right) => (byte)((left * right + 127) / 255);
}