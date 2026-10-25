namespace Lokad.Svg.Parsing;

/// <summary>Represents supported inherited fill state before element opacity is applied.</summary>
internal readonly record struct SvgPaint(SvgSolidPaint FillPaint, bool HasFill, SvgPaintOpacity FillOpacity, SvgSolidPaint CurrentPaint, bool FillUsesCurrentColor)
{
    internal SvgColor FillColor => FillPaint.Color;
    internal SvgColor CurrentColor => CurrentPaint.Color;
    internal static SvgPaint Default { get; } = new(new(new(0, 0, 0, 255), 1), true, SvgPaintOpacity.Opaque, new(new(0, 0, 0, 255), 1), false);
}

/// <summary>Resolves the supported inherited paint subset independently of XML traversal.</summary>
internal static class SvgPaintResolver
{
    /// <summary>Removes only resolved inert servers from an element-local style; unknown references retain normal admission.</summary>
    /// <remarks>Definition tables are borrowed. Scalar stroke validation stays independent of paint
    /// availability; no arrays, geometry, retained storage or font acquisition is created here.</remarks>
    internal static bool RemoveInertServers(ref SvgComputedStyle style, SvgDocumentState state, string elementName)
    {
        if (!SvgGradientSyntax.IsGradientFill(style.Fill) && !SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke)) return false;
        bool fill = IsInert(style.Fill, state);
        bool stroke = IsInert(style.Stroke, state);
        if (!fill && !stroke) return false;
        if (stroke)
        {
            _ = SvgStrokeStyleSyntax.ParseStrokeWidth(style.Width, elementName);
            _ = SvgStrokeStyleSyntax.ParseLinecap(style.Linecap);
            _ = SvgStrokeStyleSyntax.ParseLinejoin(style.Linejoin, true);
            _ = SvgStrokeStyleSyntax.ParseMiterLimit(style.Miterlimit);
            SvgStrokeStyleSyntax.ValidateDasharray(style.Dasharray, true);
            _ = SvgStrokeStyleSyntax.ParseDashoffset(style.Dashoffset);
            _ = SvgStrokeStyleSyntax.ParsePathLength(style.PathLength);
            if (style.StrokeOpacity is not null) _ = SvgColorParser.ParsePaintOpacity(style.StrokeOpacity);
        }
        style = style with { Fill = fill ? "none" : style.Fill, Stroke = stroke ? "none" : style.Stroke };
        return true;

        static bool IsInert(string? value, SvgDocumentState document)
        {
            if (value is null || !SvgLocalUrl.TryParseId(value, out string id)) return false;
            if (document.Gradients.TryGetValue(id, out SvgLinearGradient linear)) return linear.IsEmpty;
            if (document.RadialGradients.TryGetValue(id, out SvgRadialGradient radial)) return radial.IsEmpty;
            return document.Patterns.TryGetValue(id, out SvgPatternTile tile) && (!tile.HasContent || tile.TileWidth == 0 || tile.TileHeight == 0);
        }
    }

    internal static SvgPaint Resolve(string? fill, string? fillOpacity, string? currentColor, SvgPaint inheritedPaint, IReadOnlyDictionary<string, SvgColor> namedColors)
    {
        SvgSolidPaint resolvedCurrentColor = currentColor is null ? inheritedPaint.CurrentPaint : SvgColorParser.ParsePaint(currentColor, namedColors);
        bool hasFill = fill is null ? inheritedPaint.HasFill : !fill.Equals("none", StringComparison.OrdinalIgnoreCase) && !fill.Equals("transparent", StringComparison.OrdinalIgnoreCase);
        bool fillUsesCurrentColor = fill is null ? inheritedPaint.FillUsesCurrentColor : fill.Equals("currentColor", StringComparison.OrdinalIgnoreCase);
        SvgSolidPaint fillColor = fill is null ? inheritedPaint.FillPaint : fillUsesCurrentColor ? new(SvgColor.Transparent, 0) : hasFill ? SvgColorParser.ParsePaint(fill, namedColors) : new(SvgColor.Transparent, 0);
        SvgPaintOpacity resolvedFillOpacity = fillOpacity is null ? inheritedPaint.FillOpacity : SvgColorParser.ParsePaintOpacity(fillOpacity);
        return new SvgPaint(fillColor, hasFill, resolvedFillOpacity, resolvedCurrentColor, fillUsesCurrentColor);
    }

    internal static SvgColor ResolveFillColor(SvgPaint paint, byte opacity)
    {
        SvgColor color = paint.FillUsesCurrentColor ? paint.CurrentColor : paint.FillColor;
        return ApplyOpacity(ApplyOpacity(color, paint.FillOpacity.Alpha), opacity);
    }

    internal static SvgSolidPaint ResolveSolidPaint(SvgPaint paint)
    {
        SvgSolidPaint source = paint.FillUsesCurrentColor ? paint.CurrentPaint : paint.FillPaint;
        return source with { Alpha = source.Alpha * paint.FillOpacity.Value };
    }

    internal static SvgColor ResolveStrokeColor(string value, string? currentColor, SvgPaint inheritedPaint, IReadOnlyDictionary<string, SvgColor> namedColors)
        => ResolveStrokePaint(value, currentColor, inheritedPaint, namedColors).Color;

    internal static SvgSolidPaint ResolveStrokePaint(string value, string? currentColor, SvgPaint inheritedPaint, IReadOnlyDictionary<string, SvgColor> namedColors)
    {
        SvgSolidPaint resolvedCurrentColor = currentColor is null ? inheritedPaint.CurrentPaint : SvgColorParser.ParsePaint(currentColor, namedColors);
        return value.Equals("currentColor", StringComparison.OrdinalIgnoreCase) ? resolvedCurrentColor : SvgColorParser.ParsePaint(value, namedColors);
    }

    internal static SvgColor ApplyOpacity(SvgColor color, byte opacity) => new(color.Red, color.Green, color.Blue, MultiplyAlpha(color.Alpha, opacity));

    private static byte MultiplyAlpha(byte left, byte right) => (byte)((left * right + 127) / 255);
}
