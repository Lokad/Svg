namespace Lokad.Svg.Parsing;

/// <summary>Represents a parsed direct local use reference before its target is resolved.</summary>
internal readonly record struct SvgUseReference(string Id, double X, double Y, AxisTransform Transform, byte Opacity, bool IsPainted, SvgPaint? FillOverride);

/// <summary>Records the placeholders that must be replaced when a forward local use reference resolves.</summary>
internal readonly record struct PendingUse(SvgSceneSlot Slot, SvgUseReference Reference);

/// <summary>Applies typed local references through their geometry-specific supported transforms.</summary>
internal static class SvgReferenceResolver
{
    internal static SvgGeometry Apply(SvgDefinition definition, SvgUseReference reference)
    {
        SvgGeometry geometry = ApplyInstanceFill(definition, reference);
        return geometry.Kind switch
        {
            SvgGeometryKind.Rectangle => SvgGeometry.FromRectangle(ApplyRectangle(geometry.Rectangle, reference)),
            SvgGeometryKind.AxisAlignedLine => SvgGeometry.FromAxisAlignedLine(ApplyAxisAlignedLine(geometry.AxisAlignedLine, reference)),
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "The referenced SVG geometry is not implemented.")
        };
    }

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
            scene.Replace(pendingUse.Slot, Apply(definition, pendingUse.Reference));
        }
    }

    private static byte MultiplyAlpha(byte left, byte right) => (byte)((left * right + 127) / 255);
}
