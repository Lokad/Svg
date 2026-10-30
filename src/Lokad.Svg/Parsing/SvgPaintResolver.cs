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
    /// <summary>Resolves explicit fallbacks against completed resource headers while retaining available server declarations.</summary>
    /// <remarks>Header metadata is borrowed only for this call. Fallback uses declared availability,
    /// never exceptions from rendering, cancellation or memory admission. Color syntax keeps its
    /// original representation for the existing alpha and currentColor resolution stages.</remarks>
    internal static bool ResolveFallbacks(ref SvgComputedStyle style, SvgPaintResourceSelection? resources,
        IReadOnlyDictionary<string, SvgColor> namedColors, string elementName)
    {
        string? fill = Resolve(style.Fill, resources, namedColors);
        string? stroke = Resolve(style.Stroke, resources, namedColors);
        if (stroke == "none" && style.Stroke != stroke && SvgPaintReference.IsReference(style.Stroke))
            ValidateSuppressedStroke(style, elementName);
        if (fill == style.Fill && stroke == style.Stroke) return false;
        style = style with { Fill = fill, Stroke = stroke };
        return true;

        static string? Resolve(string? value, SvgPaintResourceSelection? resources, IReadOnlyDictionary<string, SvgColor> namedColors)
        {
            if (value is null || !SvgPaintReference.TryRead(value.AsSpan(), out SvgPaintReference reference)
                || reference.Fallback.IsEmpty || !SvgLocalUrl.TryParseId(reference.Url, out string id)) return value;
            string fallback = reference.Fallback.ToString();
            bool none = fallback.Equals("none", StringComparison.OrdinalIgnoreCase);
            if (!none && !fallback.Equals("currentColor", StringComparison.OrdinalIgnoreCase)) _ = SvgColorParser.ParsePaint(fallback, namedColors);
            return resources?.CanResolvePaint(id) == true ? value : none ? "none" : fallback;
        }
    }

    /// <summary>Resolves fallback and inert paint before recording or lowering an element.</summary>
    /// <remarks>Definition tables are borrowed. Scalar stroke validation stays independent of paint
    /// availability; no arrays, geometry, retained storage or font acquisition is created here.</remarks>
    internal static bool ResolvePaintServers(ref SvgComputedStyle style, SvgDocumentState state, string elementName,
        IReadOnlyDictionary<string, SvgColor> namedColors)
    {
        bool changed = ResolveFallbacks(ref style, state.PaintResources, namedColors, elementName);
        if (!SvgGradientSyntax.IsGradientFill(style.Fill) && !SvgGradientSyntax.IsGradientStrokeUrl(style.Stroke)) return changed;
        bool fill = IsInert(style.Fill, state);
        bool stroke = IsInert(style.Stroke, state);
        if (!fill && !stroke) return changed;
        if (stroke) ValidateSuppressedStroke(style, elementName);
        style = style with { Fill = fill ? "none" : style.Fill, Stroke = stroke ? "none" : style.Stroke };
        return true;

        static bool IsInert(string? value, SvgDocumentState document)
        {
            if (value is null || !SvgPaintReference.TryParseId(value, out string id)) return false;
            if (document.Gradients.TryGetValue(id, out SvgLinearGradient linear)) return linear.IsEmpty;
            if (document.RadialGradients.TryGetValue(id, out SvgRadialGradient radial)) return radial.IsEmpty;
            return document.Patterns.TryGetValue(id, out SvgPatternTile tile) && (!tile.HasContent || tile.TileWidth == 0 || tile.TileHeight == 0);
        }
    }

    private static void ValidateSuppressedStroke(SvgComputedStyle style, string elementName)
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
