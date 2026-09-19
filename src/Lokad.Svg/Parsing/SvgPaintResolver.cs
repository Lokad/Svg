namespace Lokad.Svg.Parsing;

/// <summary>Represents supported inherited fill state before element opacity is applied.</summary>
internal readonly record struct SvgPaint(SvgColor FillColor, bool HasFill, byte FillOpacity, SvgColor CurrentColor, bool FillUsesCurrentColor)
{
    internal static SvgPaint Default { get; } = new(new SvgColor(0, 0, 0, 255), true, 255, new SvgColor(0, 0, 0, 255), false);
}

/// <summary>Resolves the supported inherited paint subset independently of XML traversal.</summary>
internal static class SvgPaintResolver
{
    internal static SvgPaint Resolve(string? fill, string? fillOpacity, string? currentColor, SvgPaint inheritedPaint, IReadOnlyDictionary<string, SvgColor> namedColors)
    {
        SvgColor resolvedCurrentColor = currentColor is null ? inheritedPaint.CurrentColor : SvgColorParser.Parse(currentColor, namedColors);
        bool hasFill = fill is null ? inheritedPaint.HasFill : !fill.Equals("none", StringComparison.OrdinalIgnoreCase) && !fill.Equals("transparent", StringComparison.OrdinalIgnoreCase);
        bool fillUsesCurrentColor = fill is null ? inheritedPaint.FillUsesCurrentColor : fill.Equals("currentColor", StringComparison.OrdinalIgnoreCase);
        SvgColor fillColor = fill is null ? inheritedPaint.FillColor : fillUsesCurrentColor ? SvgColor.Transparent : hasFill ? SvgColorParser.Parse(fill, namedColors) : SvgColor.Transparent;
        byte resolvedFillOpacity = fillOpacity is null ? inheritedPaint.FillOpacity : SvgColorParser.ParseOpacity(fillOpacity);
        return new SvgPaint(fillColor, hasFill, resolvedFillOpacity, resolvedCurrentColor, fillUsesCurrentColor);
    }

    internal static SvgColor ResolveFillColor(SvgPaint paint, byte opacity)
    {
        SvgColor color = paint.FillUsesCurrentColor ? paint.CurrentColor : paint.FillColor;
        return ApplyOpacity(ApplyOpacity(color, paint.FillOpacity), opacity);
    }

    internal static SvgColor ResolveStrokeColor(string value, string? currentColor, SvgPaint inheritedPaint, IReadOnlyDictionary<string, SvgColor> namedColors)
    {
        SvgColor resolvedCurrentColor = currentColor is null ? inheritedPaint.CurrentColor : SvgColorParser.Parse(currentColor, namedColors);
        return value.Equals("currentColor", StringComparison.OrdinalIgnoreCase) ? resolvedCurrentColor : SvgColorParser.Parse(value, namedColors);
    }

    internal static SvgColor ApplyOpacity(SvgColor color, byte opacity) => new(color.Red, color.Green, color.Blue, MultiplyAlpha(color.Alpha, opacity));

    private static byte MultiplyAlpha(byte left, byte right) => (byte)((left * right + 127) / 255);
}
