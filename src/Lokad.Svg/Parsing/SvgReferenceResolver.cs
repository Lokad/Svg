namespace Lokad.Svg.Parsing;

/// <summary>Represents a parsed direct local use reference before its target is resolved.</summary>
internal readonly record struct SvgUseReference(string Id, double X, double Y, AxisTransform Transform, byte Opacity, bool IsPainted);

/// <summary>Records the placeholders that must be replaced when a forward local use reference resolves.</summary>
internal readonly record struct PendingUse(SvgSceneSlot Slot, SvgUseReference Reference);

/// <summary>Applies typed local references while keeping current rectangle transforms geometry-specific.</summary>
internal static class SvgReferenceResolver
{
    internal static SvgGeometry Apply(SvgGeometry geometry, SvgUseReference reference)
        => geometry.Kind switch
        {
            SvgGeometryKind.Rectangle => SvgGeometry.FromRectangle(ApplyRectangle(geometry.Rectangle, reference)),
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "The referenced SVG geometry is not implemented.")
        };

    private static SvgRectangle ApplyRectangle(SvgRectangle rectangle, SvgUseReference reference)
    {
        if (rectangle.Stroke is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle use references with strokes are not implemented.");
        SvgRectangle transformed = reference.Transform.Apply(rectangle with { X = rectangle.X + reference.X, Y = rectangle.Y + reference.Y });
        return transformed with { Color = new SvgColor(transformed.Color.Red, transformed.Color.Green, transformed.Color.Blue, MultiplyAlpha(transformed.Color.Alpha, reference.Opacity)), HasFill = transformed.HasFill && reference.IsPainted };
    }

    internal static void ResolvePendingUses(BudgetedParserDictionary<string, SvgGeometry> definitions, BudgetedParserList<PendingUse> pendingUses, SvgSceneBuilder scene, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        for (int pendingUseIndex = 0; pendingUseIndex < pendingUses.Count; pendingUseIndex++)
        {
            workCounter.Consume(cancellationToken);
            PendingUse pendingUse = pendingUses[pendingUseIndex];
            if (!definitions.TryGetValue(pendingUse.Reference.Id, out SvgGeometry definition)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local rectangle use references are implemented.");
            scene.Replace(pendingUse.Slot, Apply(definition, pendingUse.Reference));
        }
    }

    private static byte MultiplyAlpha(byte left, byte right) => (byte)((left * right + 127) / 255);
}
