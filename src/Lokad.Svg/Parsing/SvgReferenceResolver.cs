using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Represents a parsed direct local use reference before its target is resolved.</summary>
internal readonly record struct SvgUseReference(string Id, double X, double Y, double? Width, double? Height, AxisTransform Transform, AxisTransform InheritedTransform, byte Opacity, bool IsPainted, SvgPaint? FillOverride, string? PaintServerId);

/// <summary>Records the placeholders that must be replaced when a forward local use reference resolves.</summary>
internal readonly record struct PendingUse(SvgSceneSlot Slot, SvgUseReference Reference, SvgViewportFrame? Frame, byte? Mask, string? MaskReference, int MaskScopeStart, int MaskScopeCount);

/// <summary>Applies typed local references through their geometry-specific supported transforms.</summary>
internal static class SvgReferenceResolver
{
    /// <summary>Resolves an already parsed local reference immediately or retains its typed placeholder for document-end resolution.</summary>
    internal static void ResolveOrDefer(SvgUseReference reference, BudgetedParserDictionary<string, SvgDefinition> definitions, BudgetedParserList<PendingUse> pendingUses, SvgSceneBuilder scene, SvgViewportFrame? frame, byte? mask, string? maskReference, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        if (definitions.TryGetValue(reference.Id, out SvgDefinition definition))
        {
            foreach (SvgGeometry geometry in ApplyMask(definition, reference, frame, mask, maskReference, state)) scene.AddGeometry(geometry);
        }
        else
        {
            int scopeStart = SnapshotMaskScopes(state, workCounter, cancellationToken);
            pendingUses.Add(new PendingUse(scene.AddReferencePlaceholder(), reference, frame, mask, maskReference, scopeStart, state.MaskScopeSnapshots.Count - scopeStart));
        }
    }

    internal static SvgGeometry[] Apply(SvgDefinition definition, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state)
    {
        if (definition.SymbolViewBox is SvgViewBox symbolBox) return ApplySymbol(definition, symbolBox, reference, frame, state);
        if (reference.PaintServerId is not null) return [LowerDirectInstancePaintFill(definition, reference, frame, state)];
        SvgGeometry[] sources = ApplyInstanceFill(definition, reference, state);
        SvgGeometry[] applied = new SvgGeometry[sources.Length];
        for (int index = 0; index < sources.Length; index++)
        {
            applied[index] = sources[index].Kind switch
            {
                SvgGeometryKind.Rectangle or SvgGeometryKind.OrthogonalBand => SvgGeometry.FromRectangle(ClipRectangle(ApplyRectangle(sources[index].Rectangle, reference), frame)),
                SvgGeometryKind.AxisAlignedLine => ClipLine(ApplyAxisAlignedLine(sources[index].AxisAlignedLine, reference), frame),
                SvgGeometryKind.PolygonFill => ApplyPolygon(sources[index].Polygon, reference, frame, state),
                _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "The referenced SVG geometry is not implemented.")
            };
        }

        return applied;
    }

    private static SvgGeometry ApplyPolygon(SvgPolygonFill polygon, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        SvgAffineTransform instanceMap = SvgAffineTransform.FromMatrix(reference.Transform.ScaleX, 0, 0, reference.Transform.ScaleY, reference.Transform.OffsetX, reference.Transform.OffsetY);
        SvgDevicePoint[] mapped = new SvgDevicePoint[polygon.Points.Length];
        for (int index = 0; index < mapped.Length; index++)
        {
            SvgDevicePoint placed = instanceMap.Apply(polygon.Points[index].X + reference.X, polygon.Points[index].Y + reference.Y);
            if (frame is not null)
            {
                double shiftedX = placed.X * frame.Value.ScaleX + frame.Value.OffsetX;
                double shiftedY = placed.Y * frame.Value.ScaleY + frame.Value.OffsetY;
                if (!double.IsFinite(shiftedX) || !double.IsFinite(shiftedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
                placed = new SvgDevicePoint(shiftedX, shiftedY);
            }
            mapped[index] = placed;
        }
        SvgClipBox? clip = polygon.Clip;
        if (frame is not null) clip = clip is null ? frame.Value.Clip : clip.Value.Intersect(frame.Value.Clip);
        SvgColor color = new(polygon.Color.Red, polygon.Color.Green, polygon.Color.Blue, MultiplyAlpha(polygon.Color.Alpha, reference.Opacity));
        return SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(mapped, polygon.ContourEnds, polygon.Signs, color, polygon.HasFill && reference.IsPainted, polygon.IsEvenOdd, polygon.Mapping, clip, state.Budget));
    }

    private static SvgGeometry ApplySymbolInstance(SvgGeometry geometry, SvgViewBox symbolBox, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state)
    {
        if (geometry.Kind == SvgGeometryKind.PolygonFill) return ApplySymbolPolygon(geometry.Polygon, symbolBox, reference, frame, state);
        bool isBand;
        SvgRectangle box;
        if (geometry.Kind == SvgGeometryKind.Rectangle)
        {
            if (geometry.Rectangle.Stroke is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle use references with strokes are not implemented.");
            box = geometry.Rectangle;
            isBand = false;
        }
        else if (geometry.Kind == SvgGeometryKind.OrthogonalBand)
        {
            box = geometry.Rectangle;
            isBand = true;
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

        static SvgGeometry ApplySymbolPolygon(SvgPolygonFill source, SvgViewBox viewBox, SvgUseReference instance, SvgViewportFrame? viewportFrame, SvgDocumentState document)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (instance.Width is null || instance.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(viewBox, instance.Width.Value, instance.Height.Value);
            if (scaleX != scaleY) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Non-uniform symbol viewBox scaling of polygon content is not implemented.");
            SvgDevicePoint[] mapped = new SvgDevicePoint[source.Points.Length];
            for (int index = 0; index < mapped.Length; index++)
            {
                double contentX = source.Points[index].X * scaleX + offsetX + instance.X;
                double contentY = source.Points[index].Y * scaleY + offsetY + instance.Y;
                double placedX = instance.Transform.ScaleX * contentX + instance.Transform.OffsetX;
                double placedY = instance.Transform.ScaleY * contentY + instance.Transform.OffsetY;
                if (viewportFrame is not null)
                {
                    placedX = placedX * viewportFrame.Value.ScaleX + viewportFrame.Value.OffsetX;
                    placedY = placedY * viewportFrame.Value.ScaleY + viewportFrame.Value.OffsetY;
                }
                if (!double.IsFinite(placedX) || !double.IsFinite(placedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
                mapped[index] = new SvgDevicePoint(placedX, placedY);
            }
            SvgClipBox viewport = instance.Transform.Apply(new SvgClipBox(instance.X, instance.Y, instance.Width.Value, instance.Height.Value));
            if (viewportFrame is not null)
            {
                viewport = new SvgClipBox(viewport.X + viewportFrame.Value.OffsetX, viewport.Y + viewportFrame.Value.OffsetY, viewport.Width, viewport.Height);
                viewport = viewport.Intersect(viewportFrame.Value.Clip);
            }
            SvgClipBox clip;
            if (source.Clip is null) clip = viewport;
            else
            {
                SvgClipBox content = source.Clip.Value;
                SvgClipBox shifted = new(content.X * scaleX + offsetX + instance.X, content.Y * scaleY + offsetY + instance.Y, content.Width * scaleX, content.Height * scaleY);
                SvgClipBox placed = instance.Transform.Apply(shifted);
                if (viewportFrame is not null) placed = new SvgClipBox(placed.X + viewportFrame.Value.OffsetX, placed.Y + viewportFrame.Value.OffsetY, placed.Width, placed.Height);
                clip = placed.Intersect(viewport);
            }
            SvgColor color = new(source.Color.Red, source.Color.Green, source.Color.Blue, MultiplyAlpha(source.Color.Alpha, instance.Opacity));
            return SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(mapped, source.ContourEnds, source.Signs, color, source.HasFill && instance.IsPainted, source.IsEvenOdd, source.Mapping, clip, document.Budget));
        }
    }

    private static SvgRectangle MapSymbolBox(SvgRectangle box, SvgViewBox viewBox, double viewportWidth, double viewportHeight)
    {
        (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(viewBox, viewportWidth, viewportHeight);
        return box with { X = box.X * scaleX + offsetX, Y = box.Y * scaleY + offsetY, Width = box.Width * scaleX, Height = box.Height * scaleY };
    }

    private static SvgGeometry[] ApplyMask(SvgDefinition definition, SvgUseReference reference, SvgViewportFrame? frame, byte? mask, string? maskReference, SvgDocumentState state)
    {
        SvgGeometry[] sources = Apply(definition, reference, frame, state);
        SvgGeometry[] applied = new SvgGeometry[sources.Length];
        for (int index = 0; index < sources.Length; index++)
        {
            SvgMaskSyntax.ValidateMaskScopes(state, sources[index]);
            if (mask is null) applied[index] = sources[index];
            else
            {
                SvgRectangle box = SvgMaskSyntax.BoundingBox(sources[index]);
                if (maskReference is not null) SvgMaskSyntax.ValidateMaskCoverage(maskReference, state, box);
                applied[index] = SvgMaskSyntax.ApplyMask(sources[index], mask.Value, SvgMaskSyntax.MaskRegion(box));
            }
        }

        return applied;
    }

    private static SvgRectangle ClipRectangle(SvgRectangle rectangle, SvgViewportFrame? frame) => frame is null ? rectangle : frame.Value.ClipShifted(rectangle);

    private static SvgGeometry ClipLine(SvgAxisAlignedLine line, SvgViewportFrame? frame) => frame is null ? SvgGeometry.FromAxisAlignedLine(line) : frame.Value.ClipShifted(line);

    private static SvgGeometry[] ApplyInstanceFill(SvgDefinition definition, SvgUseReference reference, SvgDocumentState state)
    {
        if (reference.FillOverride is null) return definition.Geometries;
        if (definition.Geometries.Length != 1 || !definition.AllowsInstanceFillOverride) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill styles on use elements require an unstyled visible single-geometry definition.");
        SvgPaint fill = reference.FillOverride.Value;
        return definition.Geometries[0].Kind switch
        {
            SvgGeometryKind.Rectangle => [SvgGeometry.FromRectangle(definition.Geometries[0].Rectangle with { Color = SvgPaintResolver.ResolveFillColor(fill, 255), HasFill = fill.HasFill })],
            SvgGeometryKind.OrthogonalBand => [SvgGeometry.FromOrthogonalBand(definition.Geometries[0].Rectangle with { Color = SvgPaintResolver.ResolveFillColor(fill, 255), HasFill = fill.HasFill })],
            SvgGeometryKind.PolygonFill => [ApplyInstancePolygonFill(definition.Geometries[0].Polygon, fill, state)],
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill styles on use elements require an unstyled visible single-geometry definition."),
        };

        static SvgGeometry ApplyInstancePolygonFill(SvgPolygonFill source, SvgPaint paint, SvgDocumentState document)
        {
            ArgumentNullException.ThrowIfNull(document);
            return SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(source.Points, source.ContourEnds, source.Signs, SvgPaintResolver.ResolveFillColor(paint, 255), paint.HasFill, source.IsEvenOdd, source.Mapping, source.Clip, document.Budget));
        }
    }

    private static SvgGeometry[] ApplySymbol(SvgDefinition definition, SvgViewBox symbolBox, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state)
    {
        if (reference.PaintServerId is null)
        {
            SvgGeometry[] sources = ApplyInstanceFill(definition, reference, state);
            SvgGeometry[] applied = new SvgGeometry[sources.Length];
            for (int index = 0; index < sources.Length; index++) applied[index] = ApplySymbolInstance(sources[index], symbolBox, reference, frame, state);
            return applied;
        }

        return [ApplySymbolPaintServerFill(definition, symbolBox, reference, frame, state)];
    }

    private static SvgGeometry LowerDirectInstancePaintFill(SvgDefinition definition, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state)
    {
        if (definition.Geometries.Length != 1 || definition.Geometries[0].Kind != SvgGeometryKind.Rectangle || !definition.AllowsInstanceFillOverride) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill styles on use elements require an unstyled visible rectangle definition.");
        if (definition.Geometries[0].Rectangle.Stroke is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle use references with strokes are not implemented.");
        string paintId = reference.PaintServerId ?? throw new InvalidOperationException("Instance paint lowering requires a paint-server reference.");
        if (state.Patterns.TryGetValue(paintId, out SvgPatternTile tile)) return LowerInstancePatternFill(tile, InstancePlacement(definition.Geometries[0].Rectangle, reference), reference, frame);
        if (state.Gradients.TryGetValue(paintId, out SvgLinearGradient gradient)) return LowerInstanceGradientFill(gradient, InstancePlacement(definition.Geometries[0].Rectangle, reference), InstanceBox(definition.Geometries[0].Rectangle, reference, frame), reference, frame);
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
        if (definition.Geometries.Length != 1 || definition.Geometries[0].Kind != SvgGeometryKind.Rectangle || !definition.AllowsInstanceFillOverride) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill styles on use elements require an unstyled visible rectangle definition.");
        if (definition.Geometries[0].Rectangle.Stroke is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle use references with strokes are not implemented.");
        if (reference.Width is null || reference.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
        string paintId = reference.PaintServerId ?? throw new InvalidOperationException("Instance paint lowering requires a paint-server reference.");
        SvgRectangle fillBox = ApplySymbolInstance(definition.Geometries[0], symbolBox, reference, frame, state).Rectangle with { Color = default };
        (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(symbolBox, reference.Width.Value, reference.Height.Value);
        SvgRectangle contentBox = definition.Geometries[0].Rectangle;
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
        int commandShift = 0;
        for (int pendingUseIndex = 0; pendingUseIndex < pendingUses.Count; pendingUseIndex++)
        {
            workCounter.Consume(cancellationToken);
            PendingUse pendingUse = pendingUses[pendingUseIndex];
            if (!definitions.TryGetValue(pendingUse.Reference.Id, out SvgDefinition definition)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only supported local geometry use references are implemented.");
            SvgGeometry[] applied = ApplyMask(definition, pendingUse.Reference, pendingUse.Frame, pendingUse.Mask, pendingUse.MaskReference, state);
            if (pendingUse.MaskScopeCount > 0) foreach (SvgGeometry geometry in applied) SvgMaskSyntax.ValidateMaskScopeSnapshot(state, pendingUse.MaskScopeStart, pendingUse.MaskScopeCount, SvgMaskSyntax.BoundingBox(geometry));
            commandShift += scene.ReplaceWithMany(pendingUse.Slot with { CommandIndex = pendingUse.Slot.CommandIndex + commandShift }, applied);
        }
    }

    /// <summary>Copies the open masked-group scope boxes into the retained snapshot store, returning the snapshot start.</summary>
    private static int SnapshotMaskScopes(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        int start = state.MaskScopeSnapshots.Count;
        foreach (SvgClipBox scope in state.MaskScopes.AsSpan())
        {
            workCounter.Consume(cancellationToken);
            state.MaskScopeSnapshots.Add(scope);
        }
        return start;
    }

    internal static byte MultiplyAlpha(byte left, byte right) => (byte)((left * right + 127) / 255);
}