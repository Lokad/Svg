namespace Lokad.Svg.Parsing;

/// <summary>Represents the calibrated integer transform subset shared by parsed geometry and local references.</summary>
internal readonly record struct AxisTransform(int ScaleX, int ScaleY, int OffsetX, int OffsetY)
{
    internal static AxisTransform Identity { get; } = new(1, 1, 0, 0);

    internal AxisTransform Compose(AxisTransform child) => new(Multiply(ScaleX, child.ScaleX), Multiply(ScaleY, child.ScaleY), Offset(ScaleX, child.OffsetX, OffsetX), Offset(ScaleY, child.OffsetY, OffsetY));

    private static int Multiply(int left, int right)
    {
        try { return checked(left * right); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported integer transform exceeds the coordinate range."); }
    }

    private static int Offset(int scale, int value, int offset)
    {
        try { return checked(scale * value + offset); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.UnsupportedFeature, "The supported integer transform exceeds the coordinate range."); }
    }
}

/// <summary>Represents a parsed direct local use reference before its target is resolved.</summary>
internal readonly record struct SvgUseReference(string Id, double X, double Y, AxisTransform Transform, byte Opacity, bool IsPainted);

/// <summary>Records the placeholders that must be replaced when a forward local use reference resolves.</summary>
internal readonly record struct PendingUse(SvgSceneSlot Slot, SvgUseReference Reference);

/// <summary>Applies and resolves the current bounded local rectangle-reference subset.</summary>
internal static class SvgReferenceResolver
{
    internal static SvgRectangle Apply(SvgRectangle rectangle, SvgUseReference reference)
    {
        if (rectangle.Stroke is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle use references with strokes are not implemented.");
        return rectangle with { X = reference.Transform.ScaleX * (rectangle.X + reference.X) + reference.Transform.OffsetX, Y = reference.Transform.ScaleY * (rectangle.Y + reference.Y) + reference.Transform.OffsetY, Width = reference.Transform.ScaleX * rectangle.Width, Height = reference.Transform.ScaleY * rectangle.Height, Color = new SvgColor(rectangle.Color.Red, rectangle.Color.Green, rectangle.Color.Blue, MultiplyAlpha(rectangle.Color.Alpha, reference.Opacity)), HasFill = rectangle.HasFill && reference.IsPainted };
    }

    internal static void ResolvePendingUses(BudgetedParserDictionary<string, SvgRectangle> definitions, BudgetedParserList<PendingUse> pendingUses, SvgSceneBuilder scene)
    {
        for (int pendingUseIndex = 0; pendingUseIndex < pendingUses.Count; pendingUseIndex++)
        {
            PendingUse pendingUse = pendingUses[pendingUseIndex];
            if (!definitions.TryGetValue(pendingUse.Reference.Id, out SvgRectangle definition)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local rectangle use references are implemented.");
            SvgRectangle rectangle = Apply(definition, pendingUse.Reference);
            scene.Replace(pendingUse.Slot, rectangle);
        }
    }

    private static byte MultiplyAlpha(byte left, byte right) => (byte)((left * right + 127) / 255);
}
