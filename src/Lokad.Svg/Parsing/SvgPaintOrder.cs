namespace Lokad.Svg.Parsing;

/// <summary>Retains all three ordered paint operations, including markers for later marker replay.</summary>
internal enum SvgPaintOrder : byte
{
    FillStrokeMarkers, FillMarkersStroke, StrokeFillMarkers, StrokeMarkersFill, MarkersFillStroke, MarkersStrokeFill
}

internal enum SvgPaintPass { All, Fill, Stroke }

internal static class SvgPaintOrderSyntax
{
    internal static bool StrokeFirst(this SvgPaintOrder order)
        => order is SvgPaintOrder.StrokeFillMarkers or SvgPaintOrder.StrokeMarkersFill or SvgPaintOrder.MarkersStrokeFill;

    /// <summary>Resolves a bounded specified value; omitted, inherit and unset defer to the parent.</summary>
    internal static SvgPaintOrder? Parse(string? value)
    {
        return value is null ? null : Parse(value.AsSpan());
    }

    internal static SvgPaintOrder? Parse(ReadOnlySpan<char> value)
    {
        if (value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters)
            throw new SvgException(SvgErrorCode.LimitExceeded, "Paint order exceeds the configured syntax limit.");
        ReadOnlySpan<char> input = SvgCssWhitespace.Trim(value);
        if (input.Equals("inherit", StringComparison.OrdinalIgnoreCase) || input.Equals("unset", StringComparison.OrdinalIgnoreCase)) return null;
        if (input.Equals("normal", StringComparison.OrdinalIgnoreCase) || input.Equals("initial", StringComparison.OrdinalIgnoreCase)) return SvgPaintOrder.FillStrokeMarkers;
        Span<int> operations = stackalloc int[3];
        int count = 0, seen = 0;
        while (!input.IsEmpty)
        {
            int length = 0;
            while (length < input.Length && !SvgCssWhitespace.Is(input[length])) length++;
            ReadOnlySpan<char> token = input[..length];
            int operation = token.Equals("fill", StringComparison.OrdinalIgnoreCase) ? 0
                : token.Equals("stroke", StringComparison.OrdinalIgnoreCase) ? 1
                : token.Equals("markers", StringComparison.OrdinalIgnoreCase) ? 2 : -1;
            if (operation < 0 || (seen & (1 << operation)) != 0 || count == 3)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Paint order requires distinct fill, stroke and markers tokens.");
            operations[count++] = operation; seen |= 1 << operation;
            input = input[length..].TrimStart(SvgCssWhitespace.Characters);
        }
        if (count == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Paint order cannot be empty.");
        for (int operation = 0; operation < 3; operation++)
            if ((seen & (1 << operation)) == 0) operations[count++] = operation;
        return (operations[0], operations[1]) switch
        {
            (0, 1) => SvgPaintOrder.FillStrokeMarkers,
            (0, 2) => SvgPaintOrder.FillMarkersStroke,
            (1, 0) => SvgPaintOrder.StrokeFillMarkers,
            (1, 2) => SvgPaintOrder.StrokeMarkersFill,
            (2, 0) => SvgPaintOrder.MarkersFillStroke,
            _ => SvgPaintOrder.MarkersStrokeFill
        };
    }
}
