using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Represents a parsed direct local use reference before its target is resolved.</summary>
internal readonly record struct SvgUseReference(string Id, double X, double Y, double? Width, double? Height, AxisTransform Transform, AxisTransform InheritedTransform, byte Opacity, bool IsPainted, SvgPaint? FillOverride, string? PaintServerId);

/// <summary>Retains one clip-path reference whose box needs a forward definition or clip-path definition.</summary>
internal readonly record struct SvgDeferredClip(string ClipId, SvgViewportFrame? Parent, int Depth);

/// <summary>Records the placeholders that must be replaced when a forward local use reference resolves.</summary>
internal readonly record struct PendingUse(SvgSceneSlot Slot, SvgUseReference Reference, SvgViewportFrame? Frame, byte? Mask, string? MaskReference, int MaskScopeStart, int MaskScopeCount, SvgDeferredClip? DeferredClip);

/// <summary>Applies typed local references through their geometry-specific supported transforms.</summary>
internal static class SvgReferenceResolver
{
    /// <summary>Resolves an already parsed local reference immediately or retains its typed placeholder for document-end resolution.</summary>
    internal static void ResolveOrDefer(SvgUseReference reference, BudgetedParserDictionary<string, SvgDefinition> definitions, BudgetedParserList<PendingUse> pendingUses, SvgSceneBuilder scene, SvgViewportFrame? frame, byte? mask, string? maskReference, SvgDeferredClip? deferredClip, SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        if (deferredClip is null && (maskReference is null || mask is not null) && definitions.TryGetValue(reference.Id, out SvgDefinition definition))
        {
            foreach (SvgGeometry geometry in ApplyMask(definition, reference, frame, mask, maskReference, state)) scene.AddGeometry(geometry);
        }
        else
        {
            int scopeStart = SnapshotMaskScopes(state, workCounter, cancellationToken);
            pendingUses.Add(new PendingUse(scene.AddReferencePlaceholder(), reference, frame, mask, maskReference, scopeStart, state.MaskScopeSnapshots.Count - scopeStart, deferredClip));
        }
    }

    internal static SvgGeometry[] Apply(SvgDefinition definition, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state)
    {
        if (definition.SymbolViewBox is SvgViewBox symbolBox) return ApplySymbol(definition, symbolBox, reference, frame, state);
        if (reference.PaintServerId is not null) return [LowerDirectInstancePaintFill(definition, reference, frame, state)];
        RequireUniformOutlineScales(definition, reference);
        SvgGeometry[] sources = ApplyInstanceFill(definition, reference, state);
        SvgGeometry[] applied = new SvgGeometry[sources.Length];
        for (int index = 0; index < sources.Length; index++)
        {
            applied[index] = sources[index].Kind switch
            {
                SvgGeometryKind.Rectangle => ClipStrokedInstance(ApplyRectangle(sources[index].Rectangle, reference), frame),
                SvgGeometryKind.OrthogonalBand => SvgGeometry.FromRectangle(ClipRectangle(ApplyRectangle(sources[index].Rectangle, reference), frame)),
                SvgGeometryKind.AxisAlignedLine => ClipLine(ApplyAxisAlignedLine(sources[index].AxisAlignedLine, reference), frame),
                SvgGeometryKind.PolygonFill => ApplyPolygon(sources[index].Polygon, reference, frame, state),
                SvgGeometryKind.GradientPolygon => ApplyGradientPolygon(sources[index].GradientPolygon, reference, frame, state),
                SvgGeometryKind.RadialPolygon => ApplyRadialPolygon(sources[index].RadialPolygon, reference, frame, state),
                SvgGeometryKind.PatternPolygon => ApplyPatternPolygon(sources[index].PatternPolygon, reference, frame, state),
                SvgGeometryKind.GradientFill => LowerEmbeddedInstanceGradientFill(sources[index].Gradient, reference, frame),
                SvgGeometryKind.RadialGradient => LowerEmbeddedInstanceRadialFill(sources[index].Radial, reference, frame),
                SvgGeometryKind.PatternFill => LowerEmbeddedInstancePatternFill(sources[index].Pattern, reference, frame),
                SvgGeometryKind.GradientStroke => LowerEmbeddedInstanceGradientStroke(sources[index].GradientStroke, reference, frame),
                _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "The referenced SVG geometry is not implemented.")
            };
        }

        return applied;

        static SvgGeometry LowerEmbeddedInstancePatternFill(SvgPatternGeometry band, SvgUseReference instance, SvgViewportFrame? viewportFrame)
        {
            SvgPatternTile tile = new(SvgGeometry.FromRectangle(band.Content), band.OriginX, band.OriginY, band.TileWidth, band.TileHeight, 0, 0);
            SvgRectangle placement = InstancePlacement(band.Fill, instance);
            SvgUseReference composed = instance with { Opacity = MultiplyAlpha(band.Opacity, instance.Opacity), IsPainted = band.Fill.HasFill && instance.IsPainted };
            return LowerInstancePatternFill(tile, placement, composed, viewportFrame);
        }

        static SvgGeometry ClipStrokedInstance(SvgRectangle rectangle, SvgViewportFrame? frame)
        {
            if (frame is null) return SvgGeometry.FromRectangle(rectangle);
            if (rectangle.Stroke is SvgStroke stroke)
            {
                SvgRectangle shifted = frame.Value.Shift(rectangle);
                double halfWidth = stroke.Width / 2d;
                if (!frame.Value.Clip.Contains(shifted.X - halfWidth, shifted.Y - halfWidth, shifted.X + shifted.Width + halfWidth, shifted.Y + shifted.Height + halfWidth)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a nested viewport boundary are not implemented.");
            }
            return SvgGeometry.FromRectangle(frame.Value.ClipShifted(rectangle));
        }

        static SvgGeometry LowerEmbeddedInstanceGradientFill(SvgGradientGeometry band, SvgUseReference instance, SvgViewportFrame? viewportFrame)
        {
            SvgRectangle placement = InstancePlacement(band.Fill, instance);
            SvgRectangle box = InstanceBox(band.Box, instance, viewportFrame);
            SvgUseReference composed = instance with { Opacity = MultiplyAlpha(band.Opacity, instance.Opacity), IsPainted = band.Fill.HasFill && instance.IsPainted };
            return LowerInstanceGradientFill(band.Gradient, placement, box, composed, viewportFrame);
        }

        static SvgGeometry LowerEmbeddedInstanceRadialFill(SvgRadialGeometry radial, SvgUseReference instance, SvgViewportFrame? viewportFrame)
        {
            SvgRectangle placement = InstancePlacement(radial.Fill, instance);
            SvgRectangle box = InstanceBox(radial.Box, instance, viewportFrame);
            SvgRadialGradient circle = radial.Gradient;
            if (circle.IsUserSpace) circle = circle with { CenterX = circle.CenterX + instance.X, CenterY = circle.CenterY + instance.Y, FocalX = circle.FocalX + instance.X, FocalY = circle.FocalY + instance.Y };
            SvgUseReference composed = instance with { Opacity = MultiplyAlpha(radial.Opacity, instance.Opacity), IsPainted = radial.Fill.HasFill && instance.IsPainted };
            return LowerPlacedRadialFill(circle, placement, box, composed.Opacity, composed.IsPainted, instance.Transform, viewportFrame);
        }

        static SvgGeometry LowerEmbeddedInstanceGradientStroke(SvgGradientStrokeGeometry stroke, SvgUseReference instance, SvgViewportFrame? viewportFrame)
        {
            SvgLinearGradient vector = stroke.Gradient;
            if (vector.IsUserSpace) vector = vector with { X1 = vector.X1 + instance.X, Y1 = vector.Y1 + instance.Y, X2 = vector.X2 + instance.X, Y2 = vector.Y2 + instance.Y };
            vector = SvgGradientSyntax.MapUserSpaceVector(instance.Transform, vector, viewportFrame);
            vector = SvgGradientSyntax.MirrorBoundingBoxVector(instance.Transform, vector);
            SvgRectangle fill = InstancePlacement(stroke.Fill, instance);
            SvgRectangle top = InstancePlacement(stroke.Top, instance);
            SvgRectangle bottom = InstancePlacement(stroke.Bottom, instance);
            SvgRectangle left = InstancePlacement(stroke.Left, instance);
            SvgRectangle right = InstancePlacement(stroke.Right, instance);
            SvgRectangle box = InstancePlacement(stroke.Box, instance);
            if (viewportFrame is not null)
            {
                SvgRectangle extents = InstancePlacement(stroke.OuterBox(), instance);
                if (!viewportFrame.Value.Clip.Contains(extents.X, extents.Y, extents.X + extents.Width, extents.Y + extents.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a nested viewport boundary are not implemented.");
                fill = viewportFrame.Value.Clip.ClipRectangle(viewportFrame.Value.Shift(fill));
                top = viewportFrame.Value.Clip.ClipRectangle(viewportFrame.Value.Shift(top));
                bottom = viewportFrame.Value.Clip.ClipRectangle(viewportFrame.Value.Shift(bottom));
                left = viewportFrame.Value.Clip.ClipRectangle(viewportFrame.Value.Shift(left));
                right = viewportFrame.Value.Clip.ClipRectangle(viewportFrame.Value.Shift(right));
                box = viewportFrame.Value.Shift(box);
            }
            byte opacity = MultiplyAlpha(stroke.Opacity, instance.Opacity);
            bool painted = stroke.Fill.HasFill && instance.IsPainted;
            SvgColor baseColor = stroke.Fill.Color;
            SvgRectangle finished = fill with { Color = new SvgColor(baseColor.Red, baseColor.Green, baseColor.Blue, MultiplyAlpha(baseColor.Alpha, instance.Opacity)), HasFill = painted };
            return SvgGeometry.FromGradientStroke(new SvgGradientStrokeGeometry(finished, top, bottom, left, right, vector, box, opacity, stroke.HasStroke && instance.IsPainted));
        }
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

    private static SvgGeometry ApplyGradientPolygon(SvgGradientPolygon outline, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        SvgAffineTransform instanceMap = SvgAffineTransform.FromMatrix(reference.Transform.ScaleX, 0, 0, reference.Transform.ScaleY, reference.Transform.OffsetX, reference.Transform.OffsetY);
        SvgDevicePoint[] mapped = new SvgDevicePoint[outline.Points.Length];
        for (int index = 0; index < mapped.Length; index++)
        {
            SvgDevicePoint placed = instanceMap.Apply(outline.Points[index].X + reference.X, outline.Points[index].Y + reference.Y);
            if (frame is not null)
            {
                double shiftedX = placed.X * frame.Value.ScaleX + frame.Value.OffsetX;
                double shiftedY = placed.Y * frame.Value.ScaleY + frame.Value.OffsetY;
                if (!double.IsFinite(shiftedX) || !double.IsFinite(shiftedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
                placed = new SvgDevicePoint(shiftedX, shiftedY);
            }
            mapped[index] = placed;
        }
        SvgLinearGradient vector = outline.Gradient;
        if (vector.IsUserSpace) vector = vector with { X1 = vector.X1 + reference.X, Y1 = vector.Y1 + reference.Y, X2 = vector.X2 + reference.X, Y2 = vector.Y2 + reference.Y };
        SvgRectangle box = InstanceBox(outline.Box, reference, frame);
        vector = SvgGradientSyntax.MapUserSpaceVector(reference.Transform, vector, frame);
        vector = SvgGradientSyntax.MirrorBoundingBoxVector(reference.Transform, vector);
        SvgClipBox? clip = outline.Clip;
        if (frame is not null) clip = clip is null ? frame.Value.Clip : clip.Value.Intersect(frame.Value.Clip);
        byte opacity = MultiplyAlpha(outline.Opacity, reference.Opacity);
        return SvgGeometry.FromGradientPolygon(SvgGradientPolygon.Create(mapped, outline.ContourEnds, outline.Signs, vector, box, outline.HasFill && reference.IsPainted, outline.IsEvenOdd, opacity, outline.Mapping, clip, state.Budget));
    }

    private static SvgGeometry ApplyRadialPolygon(SvgRadialPolygon outline, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        SvgAffineTransform instanceMap = SvgAffineTransform.FromMatrix(reference.Transform.ScaleX, 0, 0, reference.Transform.ScaleY, reference.Transform.OffsetX, reference.Transform.OffsetY);
        SvgDevicePoint[] mapped = new SvgDevicePoint[outline.Points.Length];
        for (int index = 0; index < mapped.Length; index++)
        {
            SvgDevicePoint placed = instanceMap.Apply(outline.Points[index].X + reference.X, outline.Points[index].Y + reference.Y);
            if (frame is not null)
            {
                double shiftedX = placed.X * frame.Value.ScaleX + frame.Value.OffsetX;
                double shiftedY = placed.Y * frame.Value.ScaleY + frame.Value.OffsetY;
                if (!double.IsFinite(shiftedX) || !double.IsFinite(shiftedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
                placed = new SvgDevicePoint(shiftedX, shiftedY);
            }
            mapped[index] = placed;
        }
        SvgRadialGradient circle = outline.Gradient;
        if (circle.IsUserSpace) circle = circle with { CenterX = circle.CenterX + reference.X, CenterY = circle.CenterY + reference.Y, FocalX = circle.FocalX + reference.X, FocalY = circle.FocalY + reference.Y };
        SvgRectangle box = InstanceBox(outline.Box, reference, frame);
        circle = SvgGradientSyntax.MapUserSpaceRadial(reference.Transform, circle, frame);
        SvgClipBox? clip = outline.Clip;
        if (frame is not null) clip = clip is null ? frame.Value.Clip : clip.Value.Intersect(frame.Value.Clip);
        byte opacity = MultiplyAlpha(outline.Opacity, reference.Opacity);
        return SvgGeometry.FromRadialPolygon(SvgRadialPolygon.Create(mapped, outline.ContourEnds, outline.Signs, circle, box, outline.HasFill && reference.IsPainted, outline.IsEvenOdd, opacity, outline.Mapping, clip, state.Budget));
    }

    private static SvgGeometry ApplyPatternPolygon(SvgPatternPolygon outline, SvgUseReference reference, SvgViewportFrame? frame, SvgDocumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        SvgAffineTransform instanceMap = SvgAffineTransform.FromMatrix(reference.Transform.ScaleX, 0, 0, reference.Transform.ScaleY, reference.Transform.OffsetX, reference.Transform.OffsetY);
        SvgDevicePoint[] mapped = new SvgDevicePoint[outline.Points.Length];
        for (int index = 0; index < mapped.Length; index++)
        {
            SvgDevicePoint placed = instanceMap.Apply(outline.Points[index].X + reference.X, outline.Points[index].Y + reference.Y);
            if (frame is not null)
            {
                double shiftedX = placed.X * frame.Value.ScaleX + frame.Value.OffsetX;
                double shiftedY = placed.Y * frame.Value.ScaleY + frame.Value.OffsetY;
                if (!double.IsFinite(shiftedX) || !double.IsFinite(shiftedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour device mapping exceeds the admitted numeric range.");
                placed = new SvgDevicePoint(shiftedX, shiftedY);
            }
            mapped[index] = placed;
        }
        SvgClipBox? clip = outline.Clip;
        if (frame is not null) clip = clip is null ? frame.Value.Clip : clip.Value.Intersect(frame.Value.Clip);
        byte opacity = MultiplyAlpha(outline.Opacity, reference.Opacity);
        return SvgGeometry.FromPatternPolygon(SvgPatternPolygon.Create(mapped, outline.ContourEnds, outline.Signs, outline.Content, outline.OriginX, outline.OriginY, outline.TileWidth, outline.TileHeight, outline.HasFill && reference.IsPainted, outline.IsEvenOdd, opacity, outline.Mapping, clip, state.Budget));
    }

    /// <summary>Rejects non-uniformly scaled outline strokes on use instances, whose pre-scale miters would silently distort.</summary>
    private static void RequireUniformOutlineScales(SvgDefinition definition, SvgUseReference reference)
    {
        if ((long)reference.Transform.ScaleX == (long)reference.Transform.ScaleY || (long)reference.Transform.ScaleX == -(long)reference.Transform.ScaleY) return;
        foreach (SvgGeometry source in definition.Geometries)
        {
            if (source.Kind is SvgGeometryKind.GradientPolygon or SvgGeometryKind.RadialPolygon or SvgGeometryKind.PatternPolygon) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only uniformly scaled outline strokes on use instances are implemented.");
        }
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

    /// <summary>Maps one symbol-content box through a viewBox into use-viewport coordinates.</summary>
    internal static SvgRectangle MapSymbolBox(SvgRectangle box, SvgViewBox viewBox, double viewportWidth, double viewportHeight)
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
                applied[index] = SvgMaskSyntax.ApplyMask(sources[index], mask.Value, ResolveInstanceMaskRegion(maskReference, frame, state, box));
            }
        }

        return applied;
    }


        static SvgClipBox ResolveInstanceMaskRegion(string? maskReference, SvgViewportFrame? frame, SvgDocumentState document, SvgRectangle fallback)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (maskReference is not null && document.MaskRegions.TryGetValue(SvgMaskSyntax.ParseMaskId(maskReference), out SvgClipBox custom))
            {
                if (frame is not null && !frame.Value.IsTrivial) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Custom mask regions inside viewports are not implemented.");
                return custom;
            }
            if (maskReference is not null && document.MaskFractionRegions.TryGetValue(SvgMaskSyntax.ParseMaskId(maskReference), out SvgClipBox fractions))
            {
                if (frame is not null && !frame.Value.IsTrivial) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Custom mask regions inside viewports are not implemented.");
                SvgClipBox resolved = new(fallback.X + fractions.X * fallback.Width, fallback.Y + fractions.Y * fallback.Height, fractions.Width * fallback.Width, fractions.Height * fallback.Height);
                if (document.MaskContentBoxes.TryGetValue(SvgMaskSyntax.ParseMaskId(maskReference), out SvgClipBox cover) && !cover.Contains(resolved.X, resolved.Y, resolved.X + resolved.Width, resolved.Y + resolved.Height)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only mask content covering its custom region is implemented.");
                return resolved;
            }
            return SvgMaskSyntax.MaskRegion(fallback);
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
            RequireUniformOutlineScales(definition, reference);
            SvgGeometry[] sources = ApplyInstanceFill(definition, reference, state);
            List<SvgGeometry> applied = new(sources.Length);
            for (int index = 0; index < sources.Length; index++)
            {
                if (sources[index].Kind == SvgGeometryKind.Rectangle && sources[index].Rectangle.Stroke is not null)
                {
                    foreach (SvgGeometry expanded in ApplySymbolStrokedRectangle(sources[index].Rectangle, symbolBox, reference, frame, state)) applied.Add(expanded);
                    continue;
                }
                applied.Add(sources[index].Kind switch
                {
                    SvgGeometryKind.GradientFill => ApplySymbolGradientFill(sources[index].Gradient, symbolBox, reference, frame, state),
                    SvgGeometryKind.PatternFill => ApplySymbolPatternFill(sources[index].Pattern, symbolBox, reference, frame, state),
                    SvgGeometryKind.RadialGradient => ApplySymbolRadialFill(sources[index].Radial, symbolBox, reference, frame, state),
                    SvgGeometryKind.GradientStroke => ApplySymbolGradientStroke(sources[index].GradientStroke, symbolBox, reference, frame, state),
                    SvgGeometryKind.GradientPolygon => ApplySymbolGradientPolygon(sources[index].GradientPolygon, symbolBox, reference, frame, state),
                    SvgGeometryKind.RadialPolygon => ApplySymbolRadialPolygon(sources[index].RadialPolygon, symbolBox, reference, frame, state),
                    SvgGeometryKind.PatternPolygon => ApplySymbolPatternPolygon(sources[index].PatternPolygon, symbolBox, reference, frame, state),
                    _ => ApplySymbolInstance(sources[index], symbolBox, reference, frame, state),
                });
            }
            return applied.ToArray();

        static SvgGeometry[] ApplySymbolStrokedRectangle(SvgRectangle source, SvgViewBox viewBox, SvgUseReference instance, SvgViewportFrame? viewportFrame, SvgDocumentState document)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (source.Stroke is not SvgStroke sourceStroke) throw new InvalidOperationException("Symbol stroke expansion requires a stroked rectangle.");
            if (instance.Width is null || instance.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            (double scaleX, double scaleY, double _, double _) = SvgGeometrySyntax.MapViewBox(viewBox, instance.Width.Value, instance.Height.Value);
            if (scaleX != scaleY) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Non-uniform symbol viewBox scaling of rectangle strokes is not implemented.");
            if ((long)instance.Transform.ScaleX != (long)instance.Transform.ScaleY && (long)instance.Transform.ScaleX != -(long)instance.Transform.ScaleY) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only uniformly scaled rectangle strokes on use instances are implemented.");
            double scaledWidth = (double)sourceStroke.Width * Math.Abs(scaleX) * Math.Abs(instance.Transform.ScaleX);
            SvgRectangle mapped = MapSymbolBox(source, viewBox, instance.Width.Value, instance.Height.Value);
            SvgRectangle placed = mapped with { X = mapped.X + instance.X, Y = mapped.Y + instance.Y };
            if (placed.X != Math.Truncate(placed.X) || placed.Y != Math.Truncate(placed.Y) || placed.Width != Math.Truncate(placed.Width) || placed.Height != Math.Truncate(placed.Height) || !double.IsFinite(scaledWidth) || scaledWidth < 2 || scaledWidth > int.MaxValue || scaledWidth != Math.Truncate(scaledWidth) || (int)scaledWidth % 2 != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer-mapped even-width symbol-held rectangle strokes are implemented.");
            SvgGeometry[] expanded = new SvgGeometry[instance.IsPainted ? 5 : 1];
            expanded[0] = ApplySymbolInstance(SvgGeometry.FromRectangle(source with { Stroke = null }), viewBox, instance, viewportFrame, document);
            if (instance.IsPainted)
            {
                double sourceHalfWidth = sourceStroke.Width / 2d;
                SvgRectangle[] sources =
                [
                    new(source.X - sourceHalfWidth, source.Y - sourceHalfWidth, source.Width + sourceStroke.Width, sourceStroke.Width, sourceStroke.Color, true, null),
                    new(source.X - sourceHalfWidth, source.Y + source.Height - sourceHalfWidth, source.Width + sourceStroke.Width, sourceStroke.Width, sourceStroke.Color, true, null),
                    new(source.X - sourceHalfWidth, source.Y + sourceHalfWidth, sourceStroke.Width, source.Height - sourceStroke.Width, sourceStroke.Color, true, null),
                    new(source.X + source.Width - sourceHalfWidth, source.Y + sourceHalfWidth, sourceStroke.Width, source.Height - sourceStroke.Width, sourceStroke.Color, true, null),
                ];
                for (int band = 0; band < sources.Length; band++)
                    expanded[band + 1] = ApplySymbolInstance(SvgGeometry.FromRectangle(sources[band]), viewBox, instance, viewportFrame, document);
            }
            return expanded;
        }

        static SvgGeometry ApplySymbolGradientPolygon(SvgGradientPolygon source, SvgViewBox viewBox, SvgUseReference instance, SvgViewportFrame? viewportFrame, SvgDocumentState document)
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
            SvgLinearGradient vector = source.Gradient;
            if (vector.IsUserSpace) vector = vector with { X1 = vector.X1 * scaleX + offsetX + instance.X, Y1 = vector.Y1 * scaleY + offsetY + instance.Y, X2 = vector.X2 * scaleX + offsetX + instance.X, Y2 = vector.Y2 * scaleY + offsetY + instance.Y, ReferenceScaleX = vector.ReferenceScaleX * scaleX, ReferenceScaleY = vector.ReferenceScaleY * scaleY };
            SvgRectangle contentBox = source.Box;
            SvgRectangle box = instance.Transform.Apply(new SvgRectangle(contentBox.X * scaleX + offsetX + instance.X, contentBox.Y * scaleY + offsetY + instance.Y, contentBox.Width * scaleX, contentBox.Height * scaleY, default, true, null));
            if (viewportFrame is not null) box = viewportFrame.Value.Shift(box);
            vector = SvgGradientSyntax.MapUserSpaceVector(instance.Transform, vector, viewportFrame);
            vector = SvgGradientSyntax.MirrorBoundingBoxVector(instance.Transform, vector);
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
            byte opacity = MultiplyAlpha(source.Opacity, instance.Opacity);
            return SvgGeometry.FromGradientPolygon(SvgGradientPolygon.Create(mapped, source.ContourEnds, source.Signs, vector, box, source.HasFill && instance.IsPainted, source.IsEvenOdd, opacity, source.Mapping, clip, document.Budget));
        }
        static SvgGeometry ApplySymbolGradientFill(SvgGradientGeometry band, SvgViewBox viewBox, SvgUseReference instance, SvgViewportFrame? viewportFrame, SvgDocumentState document)
        {
            if (instance.Width is null || instance.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(viewBox, instance.Width.Value, instance.Height.Value);
            SvgRectangle fill = ApplySymbolInstance(SvgGeometry.FromRectangle(band.Fill), viewBox, instance, viewportFrame, document).Rectangle;
            SvgRectangle contentBox = band.Box;
            SvgRectangle box = instance.Transform.Apply(new SvgRectangle(contentBox.X * scaleX + offsetX + instance.X, contentBox.Y * scaleY + offsetY + instance.Y, contentBox.Width * scaleX, contentBox.Height * scaleY, default, true, null));
            if (viewportFrame is not null) box = viewportFrame.Value.Shift(box);
            SvgLinearGradient vector = band.Gradient;
            if (vector.IsUserSpace) vector = vector with { X1 = vector.X1 * scaleX + offsetX + instance.X, Y1 = vector.Y1 * scaleY + offsetY + instance.Y, X2 = vector.X2 * scaleX + offsetX + instance.X, Y2 = vector.Y2 * scaleY + offsetY + instance.Y, ReferenceScaleX = vector.ReferenceScaleX * scaleX, ReferenceScaleY = vector.ReferenceScaleY * scaleY };
            return LowerPlacedGradientFill(vector, fill, box, MultiplyAlpha(band.Opacity, instance.Opacity), fill.HasFill, instance.Transform, viewportFrame);
        }
        static SvgGeometry ApplySymbolPatternFill(SvgPatternGeometry band, SvgViewBox viewBox, SvgUseReference instance, SvgViewportFrame? viewportFrame, SvgDocumentState document)
        {
            if (instance.Width is null || instance.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(viewBox, instance.Width.Value, instance.Height.Value);
            SvgRectangle fill = ApplySymbolInstance(SvgGeometry.FromRectangle(band.Fill), viewBox, instance, viewportFrame, document).Rectangle;
            double originX = band.OriginX * scaleX + offsetX;
            double originY = band.OriginY * scaleY + offsetY;
            double tileWidth = band.TileWidth * scaleX;
            double tileHeight = band.TileHeight * scaleY;
            double contentX = band.Content.X * scaleX + offsetX;
            double contentY = band.Content.Y * scaleY + offsetY;
            double contentWidth = band.Content.Width * scaleX;
            double contentHeight = band.Content.Height * scaleY;
            if (!IsIntegerGrid(originX) || !IsIntegerGrid(originY) || !IsIntegerGrid(tileWidth) || !IsIntegerGrid(tileHeight) || !IsIntegerGrid(contentX) || !IsIntegerGrid(contentY) || !IsIntegerGrid(contentWidth) || !IsIntegerGrid(contentHeight)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer-mapped symbol-held pattern tiles are implemented.");
            SvgRectangle content = new(contentX, contentY, contentWidth, contentHeight, band.Content.Color, band.Content.HasFill, band.Content.Stroke);
            SvgPatternTile tile = new(SvgGeometry.FromRectangle(content), (int)originX, (int)originY, (int)tileWidth, (int)tileHeight, 0, 0);
            SvgUseReference composed = instance with { Opacity = MultiplyAlpha(band.Opacity, instance.Opacity), IsPainted = band.Fill.HasFill && instance.IsPainted };
            return LowerInstancePatternFill(tile, fill, composed, viewportFrame);

            static bool IsIntegerGrid(double value)
                => double.IsFinite(value) && value == Math.Truncate(value) && value >= int.MinValue && value <= int.MaxValue;
        }

        static SvgGeometry ApplySymbolRadialFill(SvgRadialGeometry radial, SvgViewBox viewBox, SvgUseReference instance, SvgViewportFrame? viewportFrame, SvgDocumentState document)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (instance.Width is null || instance.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(viewBox, instance.Width.Value, instance.Height.Value);
            SvgRectangle fill = ApplySymbolInstance(SvgGeometry.FromRectangle(radial.Fill), viewBox, instance, viewportFrame, document).Rectangle;
            SvgRectangle contentBox = radial.Box;
            SvgRectangle box = instance.Transform.Apply(new SvgRectangle(contentBox.X * scaleX + offsetX + instance.X, contentBox.Y * scaleY + offsetY + instance.Y, contentBox.Width * scaleX, contentBox.Height * scaleY, default, true, null));
            if (viewportFrame is not null) box = viewportFrame.Value.Shift(box);
            SvgRadialGradient circle = radial.Gradient;
            if (circle.IsUserSpace) circle = circle with { CenterX = circle.CenterX * scaleX + offsetX + instance.X, CenterY = circle.CenterY * scaleY + offsetY + instance.Y, FocalX = circle.FocalX * scaleX + offsetX + instance.X, FocalY = circle.FocalY * scaleY + offsetY + instance.Y, Radius = circle.Radius * Math.Abs(scaleX), FocalRadius = circle.FocalRadius * Math.Abs(scaleX), ReferenceScaleX = circle.ReferenceScaleX * scaleX, ReferenceScaleY = circle.ReferenceScaleY * scaleY };
            return LowerPlacedRadialFill(circle, fill, box, MultiplyAlpha(radial.Opacity, instance.Opacity), fill.HasFill, instance.Transform, viewportFrame);
        }

        static SvgGeometry ApplySymbolRadialPolygon(SvgRadialPolygon source, SvgViewBox viewBox, SvgUseReference instance, SvgViewportFrame? viewportFrame, SvgDocumentState document)
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
            SvgRadialGradient circle = source.Gradient;
            if (circle.IsUserSpace) circle = circle with { CenterX = circle.CenterX * scaleX + offsetX + instance.X, CenterY = circle.CenterY * scaleY + offsetY + instance.Y, FocalX = circle.FocalX * scaleX + offsetX + instance.X, FocalY = circle.FocalY * scaleY + offsetY + instance.Y, Radius = circle.Radius * Math.Abs(scaleX), FocalRadius = circle.FocalRadius * Math.Abs(scaleX) };
            SvgRectangle contentBox = source.Box;
            SvgRectangle box = instance.Transform.Apply(new SvgRectangle(contentBox.X * scaleX + offsetX + instance.X, contentBox.Y * scaleY + offsetY + instance.Y, contentBox.Width * scaleX, contentBox.Height * scaleY, default, true, null));
            if (viewportFrame is not null) box = viewportFrame.Value.Shift(box);
            circle = SvgGradientSyntax.MapUserSpaceRadial(instance.Transform, circle, viewportFrame);
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
            byte opacity = MultiplyAlpha(source.Opacity, instance.Opacity);
            return SvgGeometry.FromRadialPolygon(SvgRadialPolygon.Create(mapped, source.ContourEnds, source.Signs, circle, box, source.HasFill && instance.IsPainted, source.IsEvenOdd, opacity, source.Mapping, clip, document.Budget));
        }

        static SvgGeometry ApplySymbolPatternPolygon(SvgPatternPolygon source, SvgViewBox viewBox, SvgUseReference instance, SvgViewportFrame? viewportFrame, SvgDocumentState document)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (instance.Width is null || instance.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(viewBox, instance.Width.Value, instance.Height.Value);
            if (scaleX != scaleY) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Non-uniform symbol viewBox scaling of polygon content is not implemented.");
            if (viewportFrame is not null && viewportFrame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Pattern fills in scaled viewports are not implemented.");
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
            double gridOriginX = source.OriginX * scaleX + offsetX;
            double gridOriginY = source.OriginY * scaleY + offsetY;
            double gridTileWidth = source.TileWidth * scaleX;
            double gridTileHeight = source.TileHeight * scaleY;
            double gridContentX = source.Content.X * scaleX + offsetX;
            double gridContentY = source.Content.Y * scaleY + offsetY;
            double gridContentWidth = source.Content.Width * scaleX;
            double gridContentHeight = source.Content.Height * scaleY;
            if (!IsIntegerGrid(gridOriginX) || !IsIntegerGrid(gridOriginY) || !IsIntegerGrid(gridTileWidth) || !IsIntegerGrid(gridTileHeight) || !IsIntegerGrid(gridContentX) || !IsIntegerGrid(gridContentY) || !IsIntegerGrid(gridContentWidth) || !IsIntegerGrid(gridContentHeight)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only integer-mapped symbol-held pattern tiles are implemented.");
            SvgRectangle content = new(gridContentX, gridContentY, gridContentWidth, gridContentHeight, source.Content.Color, source.Content.HasFill, source.Content.Stroke);
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
                SvgClipBox shiftedSource = source.Clip.Value;
                SvgClipBox shifted = new(shiftedSource.X * scaleX + offsetX + instance.X, shiftedSource.Y * scaleY + offsetY + instance.Y, shiftedSource.Width * scaleX, shiftedSource.Height * scaleY);
                SvgClipBox placed = instance.Transform.Apply(shifted);
                if (viewportFrame is not null) placed = new SvgClipBox(placed.X + viewportFrame.Value.OffsetX, placed.Y + viewportFrame.Value.OffsetY, placed.Width, placed.Height);
                clip = placed.Intersect(viewport);
            }
            byte opacity = MultiplyAlpha(source.Opacity, instance.Opacity);
            return SvgGeometry.FromPatternPolygon(SvgPatternPolygon.Create(mapped, source.ContourEnds, source.Signs, content, (int)gridOriginX, (int)gridOriginY, (int)gridTileWidth, (int)gridTileHeight, source.HasFill && instance.IsPainted, source.IsEvenOdd, opacity, source.Mapping, clip, document.Budget));

            static bool IsIntegerGrid(double value)
                => double.IsFinite(value) && value == Math.Truncate(value) && value >= int.MinValue && value <= int.MaxValue;
        }

        static SvgGeometry ApplySymbolGradientStroke(SvgGradientStrokeGeometry stroke, SvgViewBox viewBox, SvgUseReference instance, SvgViewportFrame? viewportFrame, SvgDocumentState document)
        {
            if (instance.Width is null || instance.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
            (double scaleX, double scaleY, double offsetX, double offsetY) = SvgGeometrySyntax.MapViewBox(viewBox, instance.Width.Value, instance.Height.Value);
            SvgRectangle fill = ApplySymbolInstance(SvgGeometry.FromRectangle(stroke.Fill), viewBox, instance, viewportFrame, document).Rectangle;
            SvgRectangle top = ApplySymbolInstance(SvgGeometry.FromRectangle(stroke.Top), viewBox, instance, viewportFrame, document).Rectangle;
            SvgRectangle bottom = ApplySymbolInstance(SvgGeometry.FromRectangle(stroke.Bottom), viewBox, instance, viewportFrame, document).Rectangle;
            SvgRectangle left = ApplySymbolInstance(SvgGeometry.FromRectangle(stroke.Left), viewBox, instance, viewportFrame, document).Rectangle;
            SvgRectangle right = ApplySymbolInstance(SvgGeometry.FromRectangle(stroke.Right), viewBox, instance, viewportFrame, document).Rectangle;
            SvgRectangle contentBox = stroke.Box;
            SvgRectangle box = instance.Transform.Apply(new SvgRectangle(contentBox.X * scaleX + offsetX + instance.X, contentBox.Y * scaleY + offsetY + instance.Y, contentBox.Width * scaleX, contentBox.Height * scaleY, default, true, null));
            if (viewportFrame is not null) box = viewportFrame.Value.Shift(box);
            SvgLinearGradient vector = stroke.Gradient;
            if (vector.IsUserSpace) vector = vector with { X1 = vector.X1 * scaleX + offsetX + instance.X, Y1 = vector.Y1 * scaleY + offsetY + instance.Y, X2 = vector.X2 * scaleX + offsetX + instance.X, Y2 = vector.Y2 * scaleY + offsetY + instance.Y, ReferenceScaleX = vector.ReferenceScaleX * scaleX, ReferenceScaleY = vector.ReferenceScaleY * scaleY };
            vector = SvgGradientSyntax.MapUserSpaceVector(instance.Transform, vector, viewportFrame);
            vector = SvgGradientSyntax.MirrorBoundingBoxVector(instance.Transform, vector);
            byte opacity = MultiplyAlpha(stroke.Opacity, instance.Opacity);
            SvgColor baseColor = stroke.Fill.Color;
            SvgRectangle finished = fill with { Color = new SvgColor(baseColor.Red, baseColor.Green, baseColor.Blue, MultiplyAlpha(baseColor.Alpha, instance.Opacity)), HasFill = fill.HasFill };
            return SvgGeometry.FromGradientStroke(new SvgGradientStrokeGeometry(finished, top, bottom, left, right, vector, box, opacity, stroke.HasStroke && instance.IsPainted));
        }
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
        if (state.RadialGradients.TryGetValue(paintId, out SvgRadialGradient circle)) return LowerInstanceRadialFill(circle, InstancePlacement(definition.Geometries[0].Rectangle, reference), InstanceBox(definition.Geometries[0].Rectangle, reference, frame), reference, frame);
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
        return LowerPlacedGradientFill(gradient, placement, box, reference.Opacity, reference.IsPainted, reference.Transform, frame);
    }

    private static SvgGeometry LowerInstanceRadialFill(SvgRadialGradient circle, SvgRectangle placement, SvgRectangle box, SvgUseReference reference, SvgViewportFrame? frame)
    {
        if (circle.IsUserSpace) circle = circle with { CenterX = circle.CenterX + reference.X, CenterY = circle.CenterY + reference.Y, FocalX = circle.FocalX + reference.X, FocalY = circle.FocalY + reference.Y };
        return LowerPlacedRadialFill(circle, placement, box, reference.Opacity, reference.IsPainted, reference.Transform, frame);
    }

    private static SvgGeometry LowerPlacedGradientFill(SvgLinearGradient vector, SvgRectangle placement, SvgRectangle box, byte opacity, bool painted, AxisTransform vectorTransform, SvgViewportFrame? frame)
    {
        vector = SvgGradientSyntax.MapUserSpaceVector(vectorTransform, vector, frame);
        vector = SvgGradientSyntax.MirrorBoundingBoxVector(vectorTransform, vector);
        if (frame is not null) placement = frame.Value.Clip.ClipRectangle(frame.Value.Shift(placement));
        return FinishInstanceGradientFill(vector, box, placement, opacity, painted);
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

    private static SvgGeometry LowerPlacedRadialFill(SvgRadialGradient circle, SvgRectangle placement, SvgRectangle box, byte opacity, bool painted, AxisTransform circleTransform, SvgViewportFrame? frame)
    {
        circle = SvgGradientSyntax.MapUserSpaceRadial(circleTransform, circle, frame);
        if (frame is not null) placement = frame.Value.Clip.ClipRectangle(frame.Value.Shift(placement));
        return FinishInstanceRadialFill(circle, box, placement, opacity, painted);
    }

    private static SvgGeometry FinishInstanceRadialFill(SvgRadialGradient circle, SvgRectangle box, SvgRectangle placement, byte opacity, bool painted)
    {
        SvgRectangle fill = placement with { HasFill = painted && !circle.IsEmpty };
        if (circle.Stops.Length == 1 || (circle.Radius == 0 && !circle.IsEmpty)) return SvgGeometry.FromRectangle(fill with { Color = SvgPaintResolver.ApplyOpacity(circle.End, opacity) });
        return SvgGeometry.FromRadialFill(new SvgRadialGeometry(circle, box, fill, opacity, null));
    }

    private static SvgGeometry FinishInstanceGradientFill(SvgLinearGradient gradient, SvgRectangle box, SvgRectangle placement, byte opacity, bool painted)
    {
        SvgRectangle fill = placement with { HasFill = painted && !gradient.IsEmpty };
        if (gradient.Stops.Length == 1 || (gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2 && !gradient.IsEmpty))
        {
            if (gradient.Stops.Length > 1 && gradient.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient fills are implemented.");
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
            if (gradient.IsUserSpace) gradient = gradient with { X1 = (gradient.X1 * scaleX + offsetX) + reference.X, Y1 = (gradient.Y1 * scaleY + offsetY) + reference.Y, X2 = (gradient.X2 * scaleX + offsetX) + reference.X, Y2 = (gradient.Y2 * scaleY + offsetY) + reference.Y, ReferenceScaleX = gradient.ReferenceScaleX * scaleX, ReferenceScaleY = gradient.ReferenceScaleY * scaleY };
            gradient = SvgGradientSyntax.MapUserSpaceVector(reference.Transform, gradient, frame);
            gradient = SvgGradientSyntax.MirrorBoundingBoxVector(reference.Transform, gradient);
            return FinishInstanceGradientFill(gradient, box, fillBox, reference.Opacity, reference.IsPainted);
        }
        if (state.RadialGradients.TryGetValue(paintId, out SvgRadialGradient circle))
        {
            if (circle.IsUserSpace) circle = circle with { CenterX = (circle.CenterX * scaleX + offsetX) + reference.X, CenterY = (circle.CenterY * scaleY + offsetY) + reference.Y, FocalX = (circle.FocalX * scaleX + offsetX) + reference.X, FocalY = (circle.FocalY * scaleY + offsetY) + reference.Y, Radius = circle.Radius * Math.Abs(scaleX), FocalRadius = circle.FocalRadius * Math.Abs(scaleX), ReferenceScaleX = circle.ReferenceScaleX * scaleX, ReferenceScaleY = circle.ReferenceScaleY * scaleY };
            circle = SvgGradientSyntax.MapUserSpaceRadial(reference.Transform, circle, frame);
            return FinishInstanceRadialFill(circle, box, fillBox, reference.Opacity, reference.IsPainted);
        }
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local pattern or gradient references are implemented.");
    }

    private static SvgRectangle ApplyRectangle(SvgRectangle rectangle, SvgUseReference reference)
    {
        SvgStroke? stroke = MapInstanceStroke(rectangle.Stroke, reference);
        SvgRectangle transformed = reference.Transform.Apply(rectangle with { X = rectangle.X + reference.X, Y = rectangle.Y + reference.Y });
        return transformed with { Color = new SvgColor(transformed.Color.Red, transformed.Color.Green, transformed.Color.Blue, MultiplyAlpha(transformed.Color.Alpha, reference.Opacity)), HasFill = transformed.HasFill && reference.IsPainted, Stroke = reference.IsPainted ? stroke : null };

        static SvgStroke? MapInstanceStroke(SvgStroke? stroke, SvgUseReference reference)
        {
            if (stroke is null) return null;
            if ((long)reference.Transform.ScaleX != (long)reference.Transform.ScaleY && (long)reference.Transform.ScaleX != -(long)reference.Transform.ScaleY) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only uniformly scaled rectangle strokes on use instances are implemented.");
            int width;
            try { width = checked(stroke.Value.Width * Math.Abs(reference.Transform.ScaleX)); }
            catch (OverflowException) { throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported integer transform exceeds the coordinate range."); }
            SvgColor color = stroke.Value.Color;
            return new SvgStroke(new SvgColor(color.Red, color.Green, color.Blue, MultiplyAlpha(color.Alpha, reference.Opacity)), width);
        }
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
            SvgViewportFrame? frame = pendingUse.Frame;
            if (pendingUse.DeferredClip is SvgDeferredClip deferred) frame = ResolveDeferredClipFrame(definition, pendingUse.Reference, deferred, state);
            if (frame is not null && frame.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Use instances in scaled viewports are not implemented.");
            byte? mask = pendingUse.Mask;
            if (pendingUse.MaskReference is not null && mask is null) mask = ResolveDeferredMaskValue(pendingUse.MaskReference, state);
            SvgGeometry[] applied = ApplyMask(definition, pendingUse.Reference, frame, mask, pendingUse.MaskReference, state);
            if (pendingUse.MaskScopeCount > 0) foreach (SvgGeometry geometry in applied) SvgMaskSyntax.ValidateMaskScopeSnapshot(state, pendingUse.MaskScopeStart, pendingUse.MaskScopeCount, SvgMaskSyntax.BoundingBox(geometry));
            int adjustedCommand = pendingUse.Slot.CommandIndex + commandShift;
            int insertedCommands = scene.ReplaceWithMany(pendingUse.Slot with { CommandIndex = adjustedCommand }, applied);
            commandShift += insertedCommands;
            ShiftPendingCommandRanges(state, adjustedCommand + 1, insertedCommands, workCounter, cancellationToken);
        }


        static byte ResolveDeferredMaskValue(string maskReference, SvgDocumentState document)
        {
            ArgumentNullException.ThrowIfNull(document);
            string maskId = SvgMaskSyntax.ParseMaskId(maskReference);
            if (!document.Masks.TryGetValue(maskId, out byte value)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local mask references are implemented.");
            return value;
        }

        static SvgViewportFrame ResolveDeferredClipFrame(SvgDefinition definition, SvgUseReference reference, SvgDeferredClip deferred, SvgDocumentState document)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (!document.ObjectClips.TryGetValue(deferred.ClipId, out SvgClipBox fractions))
            {
                if (document.Clips.TryGetValue(deferred.ClipId, out SvgClipBox box))
                {
                    if (deferred.Parent is not null && deferred.Parent.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths in scaled viewports are not implemented.");
                    SvgClipBox userClip = deferred.Parent is null ? box : deferred.Parent.Value.Clip.Intersect(box);
                    return new SvgViewportFrame(deferred.Parent?.OffsetX ?? 0, deferred.Parent?.OffsetY ?? 0, deferred.Parent?.ScaleX ?? 1, deferred.Parent?.ScaleY ?? 1, deferred.Parent?.ViewportWidth, deferred.Parent?.ViewportHeight, userClip, deferred.Depth);
                }
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local clip-path references are implemented.");
            }
            if (deferred.Parent is not null && deferred.Parent.Value.IsScaled) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths in scaled viewports are not implemented.");
            SvgRectangle instanceBox;
            if (definition.SymbolViewBox is SvgViewBox symbolBox)
            {
                if (reference.Width is null || reference.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol use dimensions default to 100 percent of unknown parent dimensions, which is not implemented.");
                double left = double.PositiveInfinity;
                double top = double.PositiveInfinity;
                double right = double.NegativeInfinity;
                double bottom = double.NegativeInfinity;
                foreach (SvgGeometry source in definition.Geometries)
                {
                    SvgRectangle box = SvgMaskSyntax.BoundingBox(source);
                    SvgRectangle mapped = MapSymbolBox(box, symbolBox, reference.Width.Value, reference.Height.Value);
                    SvgRectangle placed = reference.Transform.Apply(new SvgRectangle(mapped.X + reference.X, mapped.Y + reference.Y, mapped.Width, mapped.Height, default, true, null));
                    left = Math.Min(left, placed.X);
                    top = Math.Min(top, placed.Y);
                    right = Math.Max(right, placed.X + placed.Width);
                    bottom = Math.Max(bottom, placed.Y + placed.Height);
                }
                SvgRectangle contentBox = new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), default, true, null);
                SvgClipBox viewport = reference.Transform.Apply(new SvgClipBox(reference.X, reference.Y, reference.Width.Value, reference.Height.Value));
                instanceBox = viewport.ClipRectangle(contentBox);
            }
            else
            {
                double left = double.PositiveInfinity;
                double top = double.PositiveInfinity;
                double right = double.NegativeInfinity;
                double bottom = double.NegativeInfinity;
                foreach (SvgGeometry source in definition.Geometries)
                {
                    SvgRectangle box = SvgMaskSyntax.BoundingBox(source);
                    SvgRectangle placed = reference.Transform.Apply(new SvgRectangle(box.X + reference.X, box.Y + reference.Y, box.Width, box.Height, default, true, null));
                    left = Math.Min(left, placed.X);
                    top = Math.Min(top, placed.Y);
                    right = Math.Max(right, placed.X + placed.Width);
                    bottom = Math.Max(bottom, placed.Y + placed.Height);
                }
                instanceBox = new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), default, true, null);
            }
            SvgClipBox resolved = new(instanceBox.X + fractions.X * instanceBox.Width, instanceBox.Y + fractions.Y * instanceBox.Height, fractions.Width * instanceBox.Width, fractions.Height * instanceBox.Height);
            SvgClipBox clip = deferred.Parent is null ? resolved : deferred.Parent.Value.Clip.Intersect(resolved);
            return new SvgViewportFrame(deferred.Parent?.OffsetX ?? 0, deferred.Parent?.OffsetY ?? 0, deferred.Parent?.ScaleX ?? 1, deferred.Parent?.ScaleY ?? 1, deferred.Parent?.ViewportWidth, deferred.Parent?.ViewportHeight, clip, deferred.Depth);
        }
    }

    /// <summary>Shifts retained command ranges right after scene insertions, keeping deferred group clips and masks aligned.</summary>
    internal static void ShiftPendingCommandRanges(SvgDocumentState state, int insertPosition, int insertedCount, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        if (insertedCount == 0) return;
        for (int groupIndex = 0; groupIndex < state.PendingGroupClips.Count; groupIndex++)
        {
            workCounter.Consume(cancellationToken);
            PendingGroupClip group = state.PendingGroupClips[groupIndex];
            int first = group.FirstCommandIndex > insertPosition ? group.FirstCommandIndex + insertedCount : group.FirstCommandIndex;
            int end = group.EndCommandIndex >= insertPosition ? group.EndCommandIndex + insertedCount : group.EndCommandIndex;
            state.PendingGroupClips[groupIndex] = group with { FirstCommandIndex = first, EndCommandIndex = end };
        }
        for (int maskIndex = 0; maskIndex < state.PendingGroupMasks.Count; maskIndex++)
        {
            workCounter.Consume(cancellationToken);
            PendingGroupMask mask = state.PendingGroupMasks[maskIndex];
            int first = mask.FirstCommandIndex > insertPosition ? mask.FirstCommandIndex + insertedCount : mask.FirstCommandIndex;
            int end = mask.EndCommandIndex >= insertPosition ? mask.EndCommandIndex + insertedCount : mask.EndCommandIndex;
            state.PendingGroupMasks[maskIndex] = mask with { FirstCommandIndex = first, EndCommandIndex = end };
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