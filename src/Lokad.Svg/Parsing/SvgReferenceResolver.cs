namespace Lokad.Svg.Parsing;

/// <summary>Represents a parsed direct local use reference before its target is resolved.</summary>
internal readonly record struct SvgUseReference(string Id, double X, double Y, double? Width, double? Height, AxisTransform Transform, AxisTransform InheritedTransform, byte Opacity, bool IsPainted, SvgPaint? FillOverride);

/// <summary>Records the placeholders that must be replaced when a forward local use reference resolves.</summary>
internal readonly record struct PendingUse(SvgSceneSlot Slot, SvgUseReference Reference, SvgViewportFrame? Frame, byte? Mask);

/// <summary>Applies typed local references through their geometry-specific supported transforms.</summary>
internal static class SvgReferenceResolver
{
    /// <summary>Resolves an already parsed local reference immediately or retains its typed placeholder for document-end resolution.</summary>
    internal static void ResolveOrDefer(SvgUseReference reference, BudgetedParserDictionary<string, SvgDefinition> definitions, BudgetedParserList<PendingUse> pendingUses, SvgSceneBuilder scene, SvgViewportFrame? frame, byte? mask, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        workCounter.Consume(cancellationToken);
        if (definitions.TryGetValue(reference.Id, out SvgDefinition definition)) scene.AddGeometry(ApplyMask(definition, reference, frame, mask));
        else pendingUses.Add(new PendingUse(scene.AddReferencePlaceholder(), reference, frame, mask));
    }

    internal static SvgGeometry Apply(SvgDefinition definition, SvgUseReference reference, SvgViewportFrame? frame)
    {
        SvgGeometry geometry = ApplyInstanceFill(definition, reference);
        if (definition.SymbolViewBox is SvgViewBox symbolBox) return ApplySymbolInstance(geometry, symbolBox, reference, frame);
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
        SvgRectangle transformed = reference.Transform.Apply(placed);
        transformed = transformed with { Color = new SvgColor(transformed.Color.Red, transformed.Color.Green, transformed.Color.Blue, MultiplyAlpha(transformed.Color.Alpha, reference.Opacity)), HasFill = transformed.HasFill && reference.IsPainted };
        SvgClipBox viewport = reference.InheritedTransform.Apply(new SvgClipBox(reference.X, reference.Y, reference.Width.Value, reference.Height.Value));
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
        double scaleX = viewportWidth / viewBox.Width;
        double scaleY = viewportHeight / viewBox.Height;
        if (viewBox.HorizontalAlignment != SvgViewBoxAlignment.None)
        {
            double scale = viewBox.Fit == SvgViewBoxFit.Slice ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
            scaleX = scale;
            scaleY = scale;
        }
        double offsetX = GetAlignmentOffset(viewportWidth - viewBox.Width * scaleX, viewBox.HorizontalAlignment) - viewBox.X * scaleX;
        double offsetY = GetAlignmentOffset(viewportHeight - viewBox.Height * scaleY, viewBox.VerticalAlignment) - viewBox.Y * scaleY;
        return box with { X = box.X * scaleX + offsetX, Y = box.Y * scaleY + offsetY, Width = box.Width * scaleX, Height = box.Height * scaleY };

        static double GetAlignmentOffset(double remaining, SvgViewBoxAlignment alignment) => alignment switch
        {
            SvgViewBoxAlignment.Min or SvgViewBoxAlignment.None => 0,
            SvgViewBoxAlignment.Mid => remaining / 2,
            SvgViewBoxAlignment.Max => remaining,
            _ => throw new InvalidOperationException("Unknown viewBox alignment.")
        };
    }

    private static SvgGeometry ApplyMask(SvgDefinition definition, SvgUseReference reference, SvgViewportFrame? frame, byte? mask)
    {
        SvgGeometry geometry = Apply(definition, reference, frame);
        if (mask is null) return geometry;
        return SvgMaskSyntax.ApplyMask(geometry, mask.Value, SvgMaskSyntax.MaskRegion(SvgMaskSyntax.BoundingBox(geometry)));
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

    internal static void ResolvePendingUses(BudgetedParserDictionary<string, SvgDefinition> definitions, BudgetedParserList<PendingUse> pendingUses, SvgSceneBuilder scene, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        for (int pendingUseIndex = 0; pendingUseIndex < pendingUses.Count; pendingUseIndex++)
        {
            workCounter.Consume(cancellationToken);
            PendingUse pendingUse = pendingUses[pendingUseIndex];
            if (!definitions.TryGetValue(pendingUse.Reference.Id, out SvgDefinition definition)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only supported local geometry use references are implemented.");
            scene.Replace(pendingUse.Slot, ApplyMask(definition, pendingUse.Reference, pendingUse.Frame, pendingUse.Mask));
        }
    }

    internal static byte MultiplyAlpha(byte left, byte right) => (byte)((left * right + 127) / 255);
}